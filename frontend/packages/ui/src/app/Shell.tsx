import {useEffect,useMemo,useRef,useState,type ReactNode} from 'react';
import {BrowserRouter,Link,NavLink,Navigate,Route,Routes,useLocation} from 'react-router-dom';
import {
  Activity,Bell,BookOpen,Boxes,ChevronDown,ChevronRight,Clapperboard,Cloud,Cog,
  HardDrive,House,LayoutDashboard,LogOut,Menu,Network,Rocket,ShieldCheck,Sparkles,UsersRound,
  UserRound,Clock3,Bot,ServerCog,X,Building2
} from 'lucide-react';
import {NodeApi,type NodeMode} from '@mysticego/sdk';
import {CTX,useApp,useRemote,type PersonalAccount,type Session} from './context';
import {ProfilePage,ProfileAvatar,bannerStyle} from '../pages/Profile';
import {Collective} from '../pages/Collective';
import {NetworkPage,OrchestraOverview} from '../pages/Network';
import {Deployment,OrganizationRequests} from '../pages/Deployment';
import {Overview} from '../pages/Overview';
import {AgentList,AgentDetails} from '../pages/Agents';
import {Audit} from '../pages/Audit';
import {AgentSystem} from '../pages/AgentSystem';
import {Settings,initPalette,ThemeToggle} from '../pages/Settings';
import {SignIn} from '../pages/SignIn';
import {Identities} from '../pages/Identities';
import {Users} from '../pages/Users';
import {Diagnostics} from '../pages/Diagnostics';
import {CommandCenter} from '../pages/CommandCenter';
import {AgentUpdates} from '../pages/AgentUpdates';
import {OrchestraUpdate} from '../pages/OrchestraUpdate';
import {AssistantPage} from '../pages/Assistant';
import {AutomationPage} from '../pages/Automation';
import {SoftwarePage} from '../pages/Software';
import {NotificationsPage} from '../pages/Notifications';
import {GuestPortal} from '../pages/GuestPortal';
import {AvatarStudio} from '../pages/AvatarStudio';
import {AssistantCompanion} from '../avatar/AssistantCompanion';
import {AuditSystem} from '../pages/AuditSystem';
import {Faq} from '../pages/Faq';

declare global {interface Window {__MYSTIC_CONFIG__?:{apiUrl?:string;authUrl?:string}}}
const modes={
  orchestra:{name:'Оркестр',subtitle:'Управление экосистемой',icon:ShieldCheck},
  global:{name:'Global',subtitle:'Управление организацией',icon:Cloud},
  local:{name:'Local',subtitle:'Управление площадкой',icon:ServerCog},
  agent:{name:'Agent',subtitle:'Рабочая станция',icon:HardDrive},
  audit:{name:'Audit',subtitle:'Личное пространство',icon:House}
};
type Navigation={to:string;label:string;icon:typeof Bell};
type Section={name:string;items:Navigation[]};

function navigation(mode:NodeMode,portal:boolean):Section[]{
  const device=mode==='agent'||mode==='audit'&&!portal;
  const manager=['orchestra','global','local'].includes(mode);
  return [
    {name:'Рабочая область',items:[
      {to:'/',label:'Обзор',icon:LayoutDashboard},
      ...(manager?[{to:'/nodes',label:'Узлы сети',icon:Network}]:[]),
      ...(manager||device?[
        {to:'/commands',label:'Быстрые команды',icon:Sparkles},
        {to:'/automation',label:'Автоматизация',icon:Clapperboard}
      ]:[]),
      ...(manager?[{to:'/setup',label:mode==='local'?'Комплект Agent':'Развёртывание',icon:Rocket}]:[]),
      ...(['local','global'].includes(mode)?[{to:'/updates',label:'Обновления Agent',icon:Rocket}]:[]),
      ...(mode==='orchestra'?[{to:'/orchestra-update',label:'Обновление Orchestra',icon:Rocket}]:[]),
      ...(mode==='audit'?[{to:'/organizations',label:'Организации',icon:Building2}]:[])
    ]},
    {name:'Личное пространство',items:[
      {to:'/collective',label:'Коллектив',icon:UsersRound},
      ...(device?[{to:'/system',label:'Мой компьютер',icon:HardDrive}]:[]),
      ...(device||mode==='audit'?[
        {to:'/assistant',label:'Ассистент',icon:Bot},
        {to:'/avatars',label:'Аватар VRM',icon:Boxes}
      ]:manager?[{to:'/assistant',label:'Ассистент',icon:Bot},...(mode==='global'?[{to:'/avatars',label:'Тема VRM',icon:Boxes}]:[])]:[])
    ]},
    {name:'Система',items:[
      ...(manager?[{to:'/audit',label:'Журнал действий',icon:Clock3},{to:'/diagnostics',label:'Диагностика',icon:Activity}]:[]),
      {to:'/faq',label:'FAQ',icon:BookOpen},
      {to:'/settings',label:'Настройки',icon:Cog}
    ]}
  ];
}

function AccountMenu({logout}:{logout:()=>void}){
  const {personal,session}=useApp();
  const {pathname}=useLocation();
  const [open,setOpen]=useState(false);
  const box=useRef<HTMLDivElement>(null);
  useEffect(()=>setOpen(false),[pathname]);
  useEffect(()=>{
    if(!open)return;
    const outside=(event:PointerEvent)=>{if(event.target instanceof Node&&!box.current?.contains(event.target))setOpen(false)};
    const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){setOpen(false);box.current?.querySelector('button')?.focus()}};
    document.addEventListener('pointerdown',outside);
    document.addEventListener('keydown',escape);
    return()=>{document.removeEventListener('pointerdown',outside);document.removeEventListener('keydown',escape)};
  },[open]);
  const name=personal?.profile.displayName||personal?.game.nickname||session.username||'Аккаунт';
  return <div className="account-menu" ref={box}>
    <button className="account-trigger" type="button" aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      <ProfileAvatar url={personal?.profile.avatarUrl} name={name}/><span>{name}</span><ChevronDown size={15}/>
    </button>
    {open&&<div className="account-popover">
      <Link to="/profile" onClick={()=>setOpen(false)}><UserRound size={17}/> Мой профиль</Link>
      <button onClick={logout}><LogOut size={17}/> Выйти</button>
    </div>}
  </div>;
}

type Notification={id:string;text:string;read:boolean;at:string};
function NotificationsFloat(){
  const {api}=useApp();
  const [open,setOpen]=useState(false);
  const data=useRemote(()=>api.request<Notification[]>('/collective/notifications'),[api],10000);
  const unread=data.value?.filter(x=>!x.read).length??0;
  const [problem,setProblem]=useState('');
  async function read(id:string){
    try{await api.request('/collective/notifications/'+id+'/read',{method:'POST'});data.reload()}
    catch(error){setProblem(String(error))}
  }
  return <div className="notification-float">
    {open&&<section className="notification-drawer" aria-label="Уведомления">
      <header><h2>Уведомления</h2><button className="icon-btn" onClick={()=>setOpen(false)} aria-label="Закрыть уведомления"><X size={20}/></button></header>
      {(problem||data.error)&&<p className="error-banner">{problem||data.error}</p>}
      {!data.value?.length&&!data.loading&&!data.error&&<p className="footnote">Новых уведомлений нет.</p>}
      {data.value?.map(item=><article key={item.id}><div><p>{item.text}</p><time>{new Date(item.at).toLocaleString('ru-RU')}</time></div>
        {!item.read&&<button className="icon-btn" onClick={()=>void read(item.id)} aria-label="Отметить прочитанным">✓</button>}
      </article>)}
    </section>}
    <button className="bell-button" aria-label={'Уведомления'+(unread?' · '+unread+' непрочитанных':'')} aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      <Bell size={22}/>{unread>0&&<span>{unread>99?'99+':unread}</span>}
    </button>
  </div>;
}

function AnimatedArea({children}:{children:ReactNode}){
  const {pathname}=useLocation();
  return <div className="page-motion" key={pathname}>{children}</div>;
}

function Workspace({logout}:{logout:()=>void}){
  const {mode,portal,personal}=useApp();
  const {pathname}=useLocation();
  const nav=navigation(mode,portal);
  const current=nav.flatMap(section=>section.items).find(item=>item.to===pathname)?.label??(pathname==='/profile'?'Мой профиль':modes[mode].name);
  const [mobile,setMobile]=useState(false);
  const emblem=modes[mode].icon;
  const ModeIcon=emblem;
  return <div className="app-shell mystic-workspace">
    <aside className={'sidebar '+(mobile?'open':'')}>
      <Link to="/" className="brand" onClick={()=>setMobile(false)}><div className="brand-mark"><Boxes size={23}/></div><strong>MysticEgo</strong></Link>
      <div className="node-label"><span className="node-icon"><ModeIcon size={18}/></span><div><strong>{modes[mode].name}</strong><small>{modes[mode].subtitle}</small></div></div>
      <nav aria-label="Главная навигация">{nav.map(section=><div className="nav-section" key={section.name}>
        <div className="nav-title">{section.name}</div>
        {section.items.map(item=><NavLink key={item.to} to={item.to} end={item.to==='/'} onClick={()=>setMobile(false)} className={({isActive})=>'nav-link '+(isActive?'active':'')}>
          <item.icon size={17}/><span>{item.label}</span>
        </NavLink>)}
      </div>)}</nav>
    </aside>
    {mobile&&<div className="shade" onClick={()=>setMobile(false)}/>}
    <div className="main">
      <header className="topbar orchestral-topbar" style={bannerStyle(personal?.profile.coverUrl)}>
        <span className="topbar-light" aria-hidden="true"/>
        <button className="icon-btn mobile-toggle" onClick={()=>setMobile(value=>!value)} aria-label="Открыть меню"><Menu size={22}/></button>
        <div className="breadcrumb"><span>{modes[mode].name}</span><ChevronRight size={14}/><strong>{current}</strong></div>
        <div className="top-actions"><ThemeToggle/><AccountMenu logout={logout}/></div>
      </header>
      <main className="content"><AnimatedArea><Routes>
        <Route path="/" element={mode==='orchestra'||mode==='global'||mode==='local'?<OrchestraOverview/>:mode==='audit'?<OrganizationRequests overview/>:<Overview/>}/>
        <Route path="/profile" element={<ProfilePage/>}/>
        {mode==='orchestra'&&<Route path="/orchestra-update" element={<OrchestraUpdate/>}/>}
        <Route path="/collective" element={<Collective/>}/>
        {mode==='audit'&&<Route path="/organizations" element={<OrganizationRequests/>}/>}
        <Route path="/nodes" element={<NetworkPage/>}/>
        <Route path="/setup" element={<Deployment/>}/>
        <Route path="/commands" element={<CommandCenter/>}/>
        <Route path="/automation" element={<AutomationPage/>}/>
        <Route path="/faq" element={<Faq/>}/>
        <Route path="/audit" element={<Audit/>}/>
        <Route path="/diagnostics" element={<Diagnostics/>}/>
        <Route path="/settings" element={<Settings/>}/>
        <Route path="/notifications" element={<NotificationsPage/>}/>
        <Route path="/agents" element={<AgentList/>}/><Route path="/agents/:id" element={<AgentDetails/>}/>
        <Route path="/assistant" element={<AssistantPage/>}/>
        <Route path="/avatars" element={<AvatarStudio/>}/>
        <Route path="/system" element={mode==='audit'?<AuditSystem/>:<AgentSystem/>}/>
        <Route path="/software" element={<SoftwarePage/>}/>
        <Route path="/identities" element={<Identities/>}/><Route path="/users" element={<Users/>}/><Route path="/updates" element={<AgentUpdates/>}/>
        {['/social','/directory'].map(path=><Route key={path} path={path} element={<Navigate to="/collective" replace/>}/>)}
        <Route path="/presets" element={<Navigate to="/commands" replace/>}/>
        <Route path="/deploy-native" element={<Navigate to="/setup" replace/>}/>
        <Route path="*" element={<div className="empty"><p>Страница не найдена</p><Link className="btn secondary" to="/">К обзору</Link></div>}/>
      </Routes></AnimatedArea></main>
      <NotificationsFloat/>
      {(mode==='agent'||mode==='audit')&&pathname!=='/assistant'&&<AssistantCompanion/>}
    </div>
  </div>;
}

export function MysticApp({mode,defaultApi}:{mode:NodeMode;title:string;defaultApi:string}){
  const runtime=window.__MYSTIC_CONFIG__??{};
  const portal=location.pathname==='/audit-portal'||location.pathname.startsWith('/audit-portal/');
  const [session,setSession]=useState<Session>({
    url:runtime.apiUrl||(import.meta.env.DEV?defaultApi:location.origin),token:'',
    authUrl:runtime.authUrl||(import.meta.env.DEV?(import.meta.env.VITE_AUTH_URL||'http://127.0.0.1:5400'):location.origin)
  });
  const [guestToken,setGuestToken]=useState('');
  const api=useMemo(()=>new NodeApi(session.url,()=>session.token),[session.url,session.token]);
  const profile=useRemote(()=>session.token?api.request<PersonalAccount>('/collective/me'):Promise.resolve(null),[api,session.token]);
  useEffect(initPalette,[]);
  useEffect(()=>{
    if(!session.refreshToken)return;
    let active=true;
    const timer=window.setTimeout(async()=>{
      try{
        const response=await fetch(session.authUrl.replace(/\/$/,'')+'/api/auth/refresh',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({refreshToken:session.refreshToken})});
        if(!response.ok)throw new Error();
        const data=await response.json();
        if(active)setSession(value=>({...value,token:data.accessToken,refreshToken:data.refreshToken}));
      }catch{if(active)setSession(value=>({...value,token:'',refreshToken:undefined}))}
    },9*60*1000);
    return()=>{active=false;window.clearTimeout(timer)};
  },[session.authUrl,session.refreshToken]);
  function logout(){
    if(session.refreshToken)void fetch(session.authUrl.replace(/\/$/,'')+'/api/auth/logout',{
      method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({refreshToken:session.refreshToken})
    }).catch(()=>{});
    setSession(value=>({...value,token:'',refreshToken:undefined,username:undefined}));
  }
  if(!session.token){
    if(mode==='agent'&&guestToken)return <GuestPortal apiUrl={session.url} token={guestToken} onExit={()=>setGuestToken('')}/>;
    return <SignIn mode={mode} session={session} onLogin={setSession} onGuest={setGuestToken}/>;
  }
  return <CTX.Provider value={{mode,session,setSession,api,portal,personal:profile.value,profileError:profile.error,reloadProfile:profile.reload}}>
    <BrowserRouter basename={portal?'/audit-portal':undefined}><Workspace logout={logout}/></BrowserRouter>
  </CTX.Provider>;
}
