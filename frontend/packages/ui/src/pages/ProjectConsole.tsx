import {useEffect,useState,type FormEvent} from 'react';
import {useNavigate} from 'react-router-dom';
import {ArrowDownToLine, ArrowUpRight, Braces, CalendarDays, GitBranch, GitPullRequest, RefreshCw, Search, ShieldCheck, Tag, X} from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import './project-console.css';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

type Release={tag:string;publishedAt?:string;prerelease?:boolean};
type PublishedRelease=Release&{name:string;publishedAt:string;url:string;notes:string;installable:boolean};
type ReleaseInfo={installed?:string;releaseOrigin?:string};
const views:Record<string,{path:string;label:string}>={
  overview:{path:'/',label:'Обзор'},nodes:{path:'/nodes',label:'Узлы сети'},
  deployment:{path:'/setup',label:'Развёртывание'},branches:{path:'/project-branches',label:'Ветки GitHub'},
  team:{path:'/development-team',label:'Команда разработки'},models:{path:'/model-workers',label:'Модели LM Studio'}
};

export function ReleaseStatus(){
  const {session,mode,portal}=useApp();
  const [revision,setRevision]=useState(0);
  useEffect(()=>{const refresh=()=>setRevision(v=>v+1);window.addEventListener('project-github-changed',refresh);return()=>window.removeEventListener('project-github-changed',refresh);},[]);
  const [installed,setInstalled]=useState(''),[latest,setLatest]=useState<Release|null>(null),[failed,setFailed]=useState(false);
  useEffect(()=>{
    let active=true;
    const signal=AbortSignal.timeout(12000);
    async function load(){
      const info:ReleaseInfo=await fetch(portal?'/audit-portal/release-info.json':'/release-info.json',{signal})
        .then(r=>r.ok&&r.headers.get('content-type')?.includes('json')?r.json():null).catch(()=>null)
        ?? await fetch('/release-info.json',{signal}).then(r=>r.ok?r.json():{}).catch(()=>({}));
      if(!active)return;
      setInstalled(info.installed??'');setLatest(null);setFailed(false);
      const origins=[mode==='orchestra'?session.url:session.authUrl,info.releaseOrigin].filter((x):x is string=>!!x);
      for(const origin of new Set(origins)){
        try{
          const url=new URL('/api/v1/public/project-release',origin);
          const response=await fetch(url,{signal,credentials:'omit',redirect:'error'});
          if(!response.ok)continue;
          const data=await response.json() as {latest:Release|null};
          if(active)setLatest(data.latest);
          return;
        }catch{/* try the central release authority */}
      }
      if(active)setFailed(true);
    }
    void load();
    return ()=>{active=false};
  },[session.url,session.authUrl,mode,portal,revision]);
  return <div className="release-status" aria-label="Версии MysticEgo">
    <small>Установлено <strong>{installed||'не указано'}</strong></small>
    <small>GitHub <strong>{latest?.tag||(failed?'недоступен':'—')}</strong>{latest?.prerelease?' · предварительный':''}</small>
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
type Target='orchestra'|'audit'|'both';
type Candidate={tag:string;digest:string};
type UpdateState={phase:string;protocol:number;current?:string;currentAudit?:string;candidate?:Candidate;targets?:Target[];message?:string};
const targetLabel:Record<Target,string>={orchestra:'Orchestra',audit:'Audit',both:'Orchestra + Audit'};
const phases:Record<string,string>={ready:'Доступно обновление',current:'Установлены актуальные версии',
  downloading:'Скачиваем и проверяем релиз',switching:'Применяем обновление и проверяем панели',
  unavailable:'Связь службы обновления с GitHub недоступна',failed:'Обновление прервано'};

function ReleaseUpdate(){
  const {api}=useApp();
  const state=useRemote(()=>api.request<UpdateState>('/orchestra/update'),[api],4000);
  const [approval,setApproval]=useState<{tag:string;at:number}|null>(null);
  const [error,setError]=useState('');
  const status=state.value;
  const candidate=status?.candidate;
  const canApprove=status?.phase==='ready'||status?.phase==='failed';
  const waiting=canApprove&&!!approval&&candidate?.tag===approval.tag&&Date.now()-approval.at<90000;
  const targets=status?.protocol===2 ? status.targets??[] : ['orchestra'] as Target[];

  async function approve(target:Target){
    if(!canApprove||waiting||!candidate||
      !window.confirm(`Установить ${candidate.tag} для ${targetLabel[target]}?${target==='audit'?'':' Orchestra кратковременно перезапустится.'}`))return;
    setApproval({tag:candidate.tag,at:Date.now()});setError('');
    try{
      await api.request('/orchestra/update',{method:'POST',
        body:JSON.stringify({tag:candidate.tag,digest:candidate.digest,target,confirmed:true})});
    }catch(e){setApproval(null);setError(e instanceof Error?e.message:String(e));}
  }

  return <section className="panel github-update">
    <div className="github-section-head"><ArrowDownToLine size={21}/><div><h2>Обновление сервера</h2>
      <p>Выбери, что обновить на VPS Orchestra.</p></div></div>
    <div className="github-installed">
      <div><small>Orchestra</small><strong>{status?.current??'—'}</strong></div>
      <div><small>Audit на этом VPS</small><strong>{status?.currentAudit??'—'}</strong></div>
      <div><small>Релиз для установки</small><strong>{candidate?.tag??'—'}</strong></div>
    </div>
    <p role="status">{waiting?'Подтверждено. Ожидаем службу обновления.':
      status?(phases[status.phase]??status.phase):state.loading?'Проверяем службу обновления…':''}</p>
    {status?.message&&<p>{status.message}</p>}
    {status&&status.protocol!==2&&<p>Обновите службу на VPS через CloudDesk, чтобы включить отдельное обновление Audit.</p>}
    {canApprove&&candidate&&<div className="row-actions">{targets.map(target=><button key={target}
      className={'btn '+(target===targets.at(-1)?'':'secondary')} disabled={waiting} onClick={()=>void approve(target)}>
      <ArrowDownToLine size={16}/> Обновить {targetLabel[target]}</button>)}</div>}
    <ErrorBanner text={error||state.error}/>
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

type AssistantReply={text:string;view?:string;toolResult?:string};
export function DeveloperAssistant(){
  const {api}=useApp();
  const navigate=useNavigate();
  const [prompt,setPrompt]=useState(''),[reply,setReply]=useState<AssistantReply|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function submit(event:FormEvent){
    event.preventDefault();setBusy(true);setError('');setReply(null);
    try{
      setReply(await api.request<AssistantReply>('/managed/panel-assistant',{method:'POST',body:JSON.stringify({text:prompt}),signal:AbortSignal.timeout(60000)}));
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  const view=reply?.view&&Object.hasOwn(views,reply.view)?views[reply.view]:null;
  return <>
    <Header title="Помощник разработчика" description="Локальная модель на сервере Orchestra"/>
    <ErrorBanner text={error}/>
    <Panel title="Запрос">
      <form className="stack" onSubmit={submit}>
        <label>Что нужно сделать?<textarea required maxLength={3000} value={prompt} onChange={event=>setPrompt(event.target.value)}/></label>
        <button className="btn" disabled={busy||!prompt.trim()}>{busy?'Обработка…':'Отправить'}</button>
      </form>
      {reply&&<div className="stack"><p style={{whiteSpace:'pre-wrap'}}>{reply.text}</p>
        {reply.toolResult&&<pre className="output">{reply.toolResult}</pre>}
        {view&&<button className="btn secondary" onClick={()=>navigate(view.path)}>Открыть «{view.label}»</button>}
      </div>}
    </Panel>
  </>;
}
