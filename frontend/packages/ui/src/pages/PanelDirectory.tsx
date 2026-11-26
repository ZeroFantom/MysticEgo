import {ThemeToggle} from './Settings';
import {useEffect,useState} from 'react';
import {ArrowRight,Building2,Download,ExternalLink,Monitor,ShieldCheck,UserRound} from 'lucide-react';
import {Link,useSearchParams} from 'react-router-dom';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Header,Panel} from '../components/primitives';
import './panel-directory.css';

type Kind='audit'|'global'|'agent';
type Entry={kind:Kind;name:string;url:string};
const categories=[
  {kind:'audit',name:'Audit',description:'Аккаунт аудитора и личный профиль',icon:UserRound},
  {kind:'global',name:'Global',description:'Управление организацией',icon:Building2},
  {kind:'agent',name:'Agent',description:'Вход на рабочую станцию',icon:Monitor},
] as const;

function safeEntries(data:unknown):Entry[]{
  if(!Array.isArray(data))return [];
  return data.filter((entry):entry is Entry=>{
    if(!entry||!categories.some(category=>category.kind===entry.kind)||typeof entry.name!=='string'||typeof entry.url!=='string')return false;
    try{
      const url=new URL(entry.url);
      return url.protocol==='https:'&&!url.username&&!url.password&&!url.search&&!url.hash;
    }catch{return false}
  });
}

function usePublicEntries(){
  const [entries,setEntries]=useState<Entry[]>([]);
  useEffect(()=>{
    const abort=new AbortController();
    const timer=window.setTimeout(()=>abort.abort(),6000);
    async function load(){
      setEntries([]);
      try{
        const address='/api/v1/public/panel-entrypoints';
        const response=await fetch(address,{signal:abort.signal,credentials:'omit',redirect:'error'});
        if(!response.ok)return;
        const rows=safeEntries(await response.json());
        if(!abort.signal.aborted)setEntries(rows);
      }catch{/* The built-in Audit and Orchestra links work independently. */}
    }
    void load();
    return ()=>{window.clearTimeout(timer);abort.abort()};
  },[]);
  return entries;
}

function entryLinks(rows:Entry[]){
  return rows.map(row=><a key={row.url} className="panel-directory-link" href={row.url} rel="noreferrer">
    <span>{row.name}</span><ExternalLink size={15} aria-hidden="true"/>
  </a>);
}

export function PanelLoginLinks(){
  const entries=usePublicEntries();
  const requested=new URLSearchParams(location.search).get('kind');
  const roleCards=[
    {name:'Orchestra',icon:ShieldCheck,href:'/',description:'Управление сетью',kind:null},
    {name:'Audit',icon:UserRound,href:'/audit-portal/',description:'Личный профиль',kind:'audit'},
    {name:'Global',icon:Building2,href:'/panels?kind=global',description:'Панель организации',kind:'global'},
    {name:'Agent',icon:Monitor,href:'/agent-desktop',description:'Рабочая станция',kind:'agent'},
  ] as const;
  return <section className="panel-login-picker" aria-label="Выбор панели">
    {location.pathname==='/panels'&&(requested==='global'||requested==='agent')&&
      <p className="footnote">Войдите, чтобы увидеть доступные вам панели {requested==='global'?'Global':'Agent'}.</p>}
    <nav aria-label="Вход в панели" className="panel-directory-grid">
      {roleCards.map(({name,icon:Icon,href,description,kind})=><a key={name} className="panel-login-card" href={href}
        aria-current={kind===null&&location.pathname==='/'?'page':undefined}>
        <Icon size={17} aria-hidden="true"/><span><strong>{name}</strong><small>{description}</small></span>
      </a>)}
    </nav>
    <a className="panel-directory-link" href="/desktop-downloads"><span>Скачать приложения</span><Download size={16} aria-hidden="true"/></a>
    {entries.length>0&&<details className="panel-published-links"><summary>Прямые ссылки</summary><div>{entryLinks(entries)}</div></details>}
  </section>;
}

function LocalAgent(){
  return <a className="panel-directory-link" href="/agent-desktop">
    <span>Подключить Agent через AgentDesktop</span><ArrowRight size={15} aria-hidden="true"/>
  </a>;
}

export function AgentDesktopEntry({scope=''}:{scope?:string}={}){
  return <main className="agent-desktop-entry"><div className="appearance-public-bar"><ThemeToggle scope={scope}/></div>
    <section className="panel">
      <Monitor size={27} aria-hidden="true"/>
      <h1>Вход в Agent</h1>
      <p>Для входа на рабочую станцию нужен запущенный AgentDesktop.</p>
      <ol>
        <li>Если AgentDesktop ещё не установлен, скачайте комплект приложений MysticEgo.</li>
        <li>Запустите <code>AgentDesktop.cmd</code> на Windows или <code>./agent-desktop</code> на Linux.</li>
        <li>Для клубного Agent используйте комплект, выданный администратором Local. Откройте «Панель роли» в AgentDesktop.</li>
      </ol>
      <div className="row-actions">
        <a className="btn secondary" href="/">К входу Orchestra</a>
        <a className="btn" href="/desktop-downloads" rel="noreferrer">Скачать AgentDesktop</a>
        <a className="btn secondary" href="/panels?kind=agent">Удалённые Agent</a>
      </div>
    </section>
  </main>;
}

export function PanelDirectory(){
  const {api}=useApp();
  const [params]=useSearchParams();
  const requested=params.get('kind');
  const category=requested==='global'||requested==='agent'?requested:null;
  const configured=usePublicEntries();
  const managed=useRemote(()=>api.request<{entries:Entry[];truncated:boolean}>('/managed/panel-entrypoints'),[api]);
  const entries=[...new Map([...configured,...safeEntries(managed.value?.entries)].map(row=>[row.kind+' '+row.url,row])).values()];
  return <div className="panel-directory">
    <Header title="Вход в панели" description="Выбирайте панели сети из Orchestra."
      action={category&&<Link className="btn secondary" to="/panels">Все панели</Link>}/>
    <ErrorBanner text={managed.error}/>
    {!category&&<Panel title="Orchestra"><p>Управление экосистемой</p>
      <Link className="panel-directory-link" to="/"><span>Остаться в Orchestra</span><ArrowRight size={15} aria-hidden="true"/></Link>
    </Panel>}
    {categories.filter(row=>!category||category===row.kind).map(({kind,name,description,icon:Icon})=>{
      const rows=entries.filter(row=>row.kind===kind);
      return <Panel key={kind} title={name} aside={<Icon size={20} aria-hidden="true"/>}>
        <p>{description}</p>
        {kind==='audit'&&<a className="panel-directory-link" href="/audit-portal/"><span>Открыть Audit</span><ArrowRight size={15} aria-hidden="true"/></a>}
        {entryLinks(rows)}
        {kind!=='audit'&&rows.length===0&&<p className="footnote">{managed.loading?'Загружаем доступные панели…':
          kind==='agent'?'У доступных вам Agent пока нет HTTPS-адреса. Локальный Agent открывается через AgentDesktop.':'Для этого аккаунта пока нет Global с настроенным HTTPS-адресом.'}</p>}
        {kind==='agent'&&<LocalAgent/>}
      </Panel>;
    })}
    {managed.value?.truncated&&<p className="footnote">Показаны первые 500 доступных панелей.</p>}
  </div>;
}


