import {useEffect,useState} from 'react';
import {ArrowDownToLine, ArrowUpRight, Braces, CalendarDays, GitBranch, GitPullRequest, RefreshCw, Search, ShieldCheck, Tag, X} from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import './project-console.css';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {useNotice} from '../app/notifications';
import {targetLabel,useServerUpdate,type UpdateState,type UpdateTarget as Target} from '../app/serverUpdate';

type Release={tag:string;publishedAt?:string;prerelease?:boolean};
type PublishedRelease=Release&{name:string;publishedAt:string;url:string;notes:string;installable:boolean};
type ReleaseInfo={installed?:string;releaseOrigin?:string};
export function ReleaseStatus(){
  const {session,mode,portal}=useApp();
  const [revision,setRevision]=useState(0);
  useEffect(()=>{const refresh=()=>setRevision(v=>v+1);window.addEventListener('project-github-changed',refresh);return()=>window.removeEventListener('project-github-changed',refresh);},[]);
  const [installed,setInstalled]=useState(''),[latest,setLatest]=useState<Release|null>(null),[failed,setFailed]=useState(false);
  useEffect(()=>{
    const lifecycle=new AbortController();
    let timer:number|undefined;
    let running=false;
    async function load(){
      if(running||lifecycle.signal.aborted)return;
      running=true;
      const signal=AbortSignal.any([lifecycle.signal,AbortSignal.timeout(12000)]);
      const settings={signal,cache:'no-store' as const};
      try{
        const info:ReleaseInfo=await fetch(portal?'/audit-portal/release-info.json':'/release-info.json',settings)
          .then(r=>r.ok&&r.headers.get('content-type')?.includes('json')?r.json():null).catch(()=>null)
          ?? await fetch('/release-info.json',settings).then(r=>r.ok?r.json():{}).catch(()=>({}));
        if(lifecycle.signal.aborted)return;
        setInstalled(info.installed??'');
        const origins=[mode==='orchestra'?session.url:session.authUrl,info.releaseOrigin].filter((x):x is string=>!!x);
        for(const origin of new Set(origins)){
          try{
            const url=new URL('/api/v1/public/project-release',origin);
            const response=await fetch(url,{...settings,credentials:'omit',redirect:'error'});
            if(!response.ok)continue;
            const data=await response.json() as {latest:Release|null};
            if(!lifecycle.signal.aborted){setLatest(data.latest);setFailed(false)}
            return;
          }catch{/* try the central release authority */}
        }
        if(!lifecycle.signal.aborted)setFailed(true);
      }finally{
        running=false;
        if(!lifecycle.signal.aborted)timer=window.setTimeout(()=>{if(document.hidden)timer=undefined;else void load()},15000);
      }
    }
    function refresh(){
      if(document.hidden)return;
      window.clearTimeout(timer);
      void load();
    }
    void load();
    document.addEventListener('visibilitychange',refresh);
    window.addEventListener('focus',refresh);
    return ()=>{lifecycle.abort();window.clearTimeout(timer);document.removeEventListener('visibilitychange',refresh);window.removeEventListener('focus',refresh)};
  },[session.url,session.authUrl,mode,portal,revision]);
  return <div className="release-status" aria-label="Версии MysticEgo"
    title={`Установлено: ${installed||'—'} · GitHub: ${latest?.tag??'—'}`}>
    <Tag className="release-status-icon" size={17} aria-hidden="true"/>
    <small>Установлено <strong>{installed||'не указано'}</strong></small>
    <small>GitHub <strong>{latest?.tag||(failed?'недоступен':'—')}</strong>{failed&&latest?' · данные устарели':latest?.prerelease?' · предварительный':''}</small>
  </div>;
}

function GitHubConnection({onChange}:{onChange:()=>void}){
  const {api}=useApp();
  const path='/managed/project-github';
  const connection=useRemote(()=>api.request<{connected:boolean;source:string}>(path),[api]);
  const [token,setToken]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function update(method:'POST'|'DELETE'){
    setBusy(true);setError('');
    try{
      await api.request(path,{method,...(method==='POST'?{body:JSON.stringify({token})}:{})});
      setToken('');connection.reload();onChange();
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  if(!connection.value)return null;
  return <details className="panel github-connection" open={connection.value.connected?undefined:true}><summary>Доступ к GitHub</summary>
    <p>{connection.value.connected?'Токен подключён':'Токен не подключён'}</p>
    <form className="stack" onSubmit={e=>{e.preventDefault();void update('POST');}}>
      <label>Токен чтения Contents для ZeroFantom/MysticEgo<input type="password" autoComplete="off" value={token} onChange={e=>setToken(e.target.value)} required maxLength={300}/></label>
      <div className="row-actions">
        <button className="btn" disabled={busy||!token.trim()}>Проверить и подключить</button>
        {connection.value.source==='panel'&&<button type="button" className="btn secondary" disabled={busy} onClick={()=>void update('DELETE')}>Отключить</button>}
      </div>
    </form>
    <ErrorBanner text={error}/>
  </details>;
}

const repository='https://github.com/ZeroFantom/MysticEgo';
const formatDate=(value:string)=>new Date(value).toLocaleDateString('ru-RU',{day:'numeric',month:'long',year:'numeric'});
type Branch={name:string;sha:string;isProtected:boolean};
const phases:Record<string,string>={ready:'Доступно обновление',current:'Установлены актуальные версии',
  downloading:'Скачиваем и проверяем релиз',switching:'Применяем обновление и проверяем панели',
  unavailable:'Связь службы обновления с GitHub недоступна',failed:'Обновление прервано'};

function ReleaseUpdate(){
  const {api}=useApp();
  const update=useServerUpdate();
  const [confirmation,setConfirmation]=useState<{tag:string;digest:string;target:Target}|null>(null);
  const state=useRemote(()=>api.request<UpdateState>('/orchestra/update'),[api],4000);
  const [error,setError]=useState('');
  const status=state.value;
  const candidate=status?.candidate;
  const canApprove=status?.phase==='ready'||status?.phase==='failed';
  const waiting=!!update?.pending;
  const targets=status?.protocol===2 ? status.targets??[] : ['orchestra'] as Target[];

  const pendingConfirmation=canApprove&&!waiting&&confirmation&&candidate&&
    confirmation.tag===candidate.tag&&confirmation.digest===candidate.digest&&targets.includes(confirmation.target)
    ?confirmation:null;
  const failed=status?.phase==='failed'||status?.phase==='unavailable';
  useNotice(error||(!waiting?state.error:''));

  async function approve(){
    if(!pendingConfirmation||!update||!status)return;
    setConfirmation(null);setError('');
    try{await update.approve(pendingConfirmation,status)}
    catch(e){setError(e instanceof Error?e.message:String(e));}
  }

  return <section className="panel github-update">
    <div className="github-section-head"><ArrowDownToLine size={21}/><div><h2>Обновление сервера</h2>
      <p>Выбери, что обновить на VPS Orchestra.</p></div></div>
    <div className="github-installed">
      <div><small>Orchestra</small><strong>{status?.current??'—'}</strong></div>
      <div><small>Audit на этом VPS</small><strong>{status?.currentAudit??'—'}</strong></div>
      <div><small>Релиз для установки</small><strong>{candidate?.tag??'—'}</strong></div>
    </div>
    <p role="status">{update?.reloading?'Обновление проверено. Перезагружаем страницу…':waiting&&state.error?'Orchestra перезапускается. Проверяем связь…':waiting&&canApprove?'Подтверждено. Ожидаем службу обновления.':
      status?(phases[status.phase]??status.phase):state.loading?'Проверяем службу обновления…':''}</p>
    {status?.message&&(!failed||!waiting)&&<p>{status.message}</p>}
    {status&&status.protocol!==2&&<p>Обновите службу на VPS через CloudDesk, чтобы включить отдельное обновление Audit.</p>}
    {canApprove&&candidate&&<div className="row-actions">{targets.map(target=><button key={target}
      className={'btn '+(target===targets.at(-1)?'':'secondary')} disabled={waiting||!!pendingConfirmation} onClick={()=>setConfirmation({tag:candidate.tag,digest:candidate.digest,target})}>
      <ArrowDownToLine size={16}/> Обновить {targetLabel[target]}</button>)}</div>}
    {pendingConfirmation&&<div className="stack">
      <p>Установить {pendingConfirmation.tag} для {targetLabel[pendingConfirmation.target]}?
        {pendingConfirmation.target!=='audit'&&' Orchestra кратковременно перезапустится.'}</p>
      <div className="row-actions">
        <button type="button" className="btn" onClick={()=>void approve()}>Подтвердить обновление</button>
        <button type="button" className="btn secondary" onClick={()=>setConfirmation(null)}>Отмена</button>
      </div>
    </div>}
  </section>;
}

function ReleaseCard({release,latest=false}:{release:PublishedRelease;latest?:boolean}){
  return <article className={'github-release'+(latest?' github-release-latest':'')}>
    <div className="github-release-title">
      <div className="github-release-icon"><Tag size={20}/></div>
      <div><h3>{release.name||release.tag}</h3>
        <div className="github-release-meta"><code>{release.tag}</code>
          <span><CalendarDays size={14}/> {formatDate(release.publishedAt)}</span></div>
      </div>
    </div>
    <div className="github-release-badges">
      {latest&&<span>Последний релиз</span>}
      {release.prerelease&&<span>Предварительный</span>}
    </div>
    {release.notes&&<details className="github-release-notes" open={latest||undefined}>
      <summary>Что изменилось</summary><ReactMarkdown>{release.notes}</ReactMarkdown>
    </details>}
    <div className="github-release-footer">
      {release.installable&&<span><ShieldCheck size={15}/> Пакет для обновления сервера</span>}
      <a href={release.url} target="_blank" rel="noreferrer">Открыть на GitHub <ArrowUpRight size={15}/></a>
    </div>
  </article>;
}

export function ProjectBranches(){
  const {api}=useApp();
  const releases=useRemote(()=>api.request<{releases:PublishedRelease[];canUpdate:boolean}>('/managed/project-releases'),[api]);
  const data=useRemote(()=>api.request<{branches:Branch[];truncated:boolean}>('/managed/project-branches'),[api]);
  const [filter,setFilter]=useState('');
  const branches=data.value?.branches.filter(branch=>branch.name.toLowerCase().includes(filter.toLowerCase().trim()))??[];
  const [latest,...history]=releases.value?.releases??[];
  const refresh=()=>{releases.reload();data.reload();window.dispatchEvent(new Event('project-github-changed'));};
  return <div className="github-console">
    <Header title="Ветки GitHub" description="Релизы, обновления сервера и ветки MysticEgo"
      action={<button className="btn secondary" onClick={refresh}><RefreshCw size={16}/> Обновить данные</button>}/>
    <div className="github-repo">
      <GitPullRequest size={21}/><strong>ZeroFantom / MysticEgo</strong>
      <a href={repository} target="_blank" rel="noreferrer">Репозиторий <ArrowUpRight size={15}/></a>
    </div>
    <GitHubConnection onChange={refresh}/>
    {releases.value?.canUpdate&&<ReleaseUpdate/>}
    <section className="github-releases" aria-label="Релизы GitHub">
      <div className="github-section-head"><Tag size={21}/><h2>Релизы</h2></div>
      <ErrorBanner text={releases.error}/>
      {latest&&<ReleaseCard release={latest} latest/>}
      {history.length>0&&<details className="panel github-history"><summary>Предыдущие релизы · {history.length}</summary>
        {history.map(release=><ReleaseCard key={release.tag} release={release}/>)}</details>}
      {releases.value&&!latest&&<p>Опубликованных релизов пока нет.</p>}
    </section>
    <section className="panel github-branches">
      <div className="github-section-head"><GitBranch size={21}/><h2>Ветки проекта</h2></div>
      <div className="mystic-search"><Search size={18} aria-hidden="true"/>
        <input type="search" aria-label="Найти ветку" value={filter} placeholder="Найти ветку" onChange={event=>setFilter(event.target.value)}/>
        {filter&&<button type="button" aria-label="Очистить поиск" onClick={()=>setFilter('')}><X size={17}/></button>}
      </div>
      <ErrorBanner text={data.error}/>
      <ul className="github-branch-list">{branches.map(branch=><li key={branch.name}>
        <a href={repository+'/tree/'+encodeURIComponent(branch.name)} target="_blank" rel="noreferrer">
          <GitBranch size={16}/> {branch.name} <ArrowUpRight size={14}/></a>
        <a className="github-commit" href={repository+'/commit/'+encodeURIComponent(branch.sha)} target="_blank" rel="noreferrer">
          <Braces size={14}/><code>{branch.sha.slice(0,8)}</code></a>
        {branch.isProtected&&<span><ShieldCheck size={15}/> Защищена</span>}
      </li>)}</ul>
      {data.value?.truncated&&<p>Показаны первые 100 веток.</p>}
      {data.value&&!branches.length&&<p>Ветки не найдены.</p>}
    </section>
  </div>;
}



