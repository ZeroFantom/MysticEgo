import './panel-surface.css';
import {useAccountPresentation,useLocale} from './presentation';
import './typography.css';
import './cinematic.css';
import {SharePage} from '../pages/SharePage';
import {ServerUpdateProvider,useServerUpdate} from './serverUpdate';
import {CloudDeskDownloads,PublicDesktopDownloads} from '../pages/DesktopDownloads';
import {NoticeBell,NotificationProvider,useNotice} from './notifications';
import {MaintenanceNotices} from '../pages/MaintenanceStatus';
import {InfrastructureWorkspace,type InfrastructureSection} from '../pages/InfrastructureWorkspace';
import './workspace-rail.css';
import './workspace-polish.css';
import './motion.css';
import {ErrorBanner} from '../components/primitives';
import {accountScope,useBrowserSession} from './browserSession';
import {ReleaseStatus,ProjectBranches} from '../pages/ProjectConsole';
import {useEffect,useId,useLayoutEffect,useMemo,useRef,useState,type ReactNode} from 'react';
import {BrowserRouter,Link,NavLink,Navigate,Route,Routes,useLocation} from 'react-router-dom';
import {
  Activity,Bell,BookOpen,Boxes,ChevronDown,ChevronRight,Clapperboard,Cloud,Cog,
  HardDrive,House,LayoutDashboard,LogOut,Menu,Network,Rocket,ShieldCheck,Sparkles,UsersRound,
  UserRound,Clock3,Bot,ServerCog,X,Building2,Container,BrainCircuit,Database,Radio
} from 'lucide-react';
import {NodeApi,type NodeMode} from '@mysticego/sdk';
import {CTX,useApp,useRemote,type PersonalAccount} from './context';
import {ProfilePage,ProfileAvatar} from '../pages/Profile';
import {Collective} from '../pages/Collective';
import {NetworkPage,OrchestraOverview} from '../pages/Network';
import {Deployment,OrganizationRequests} from '../pages/Deployment';
import {Overview} from '../pages/Overview';
import {AgentList,AgentDetails} from '../pages/Agents';
import {Audit} from '../pages/Audit';
import {AgentSystem} from '../pages/AgentSystem';
import {Settings,initPalette,ThemeToggle,applyAccountAppearance} from '../pages/Settings';
import {SignIn} from '../pages/SignIn';
import {AgentDesktopEntry,PanelDirectory} from '../pages/PanelDirectory';
import {Identities} from '../pages/Identities';
import {Users} from '../pages/Users';
import {Diagnostics} from '../pages/Diagnostics';
import {CommandCenter} from '../pages/CommandCenter';
import {DevelopmentTeam} from '../pages/DevelopmentTeam';
import {AgentUpdates} from '../pages/AgentUpdates';
import {AssistantPage} from '../pages/Assistant';
import {AutomationPage} from '../pages/Automation';
import {SoftwarePage} from '../pages/Software';
import {NotificationsPage} from '../pages/Notifications';
import {GuestPortal} from '../pages/GuestPortal';
import {AvatarStudio} from '../pages/AvatarStudio';
import {AssistantCompanion} from '../avatar/AssistantCompanion';
import {AuditSystem} from '../pages/AuditSystem';
import {Faq} from '../pages/Faq';
import {DeveloperPage, LicenseLink, StudioPage} from '../pages/Studio';
import {LicensePage} from '../pages/LicensePage';

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
      ...(mode==='audit'?[{to:'/organizations',label:'Организации',icon:Building2}]:[]),
      ...(['audit','orchestra'].includes(mode)?[{to:'/development-team',label:'Команда разработки',icon:UsersRound}]:[])
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
  const t=useLocale();
  const {personal,session}=useApp();
  const {pathname}=useLocation();
  const [open,setOpen]=useState(false);
  const box=useRef<HTMLDivElement>(null);
  const menuId=useId();
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
    <button className="account-trigger" type="button" aria-label={'Меню аккаунта: '+name} aria-expanded={open} aria-controls={menuId} onClick={()=>setOpen(value=>!value)}>
      <ProfileAvatar url={personal?.profile.avatarUrl} name={name}/><span>{name}</span><ChevronDown size={15}/>
    </button>
    <div id={menuId} className="account-popover motion-disclosure" data-motion-open={open} inert={!open} aria-hidden={!open}>
      <Link to="/profile" onClick={()=>setOpen(false)}><UserRound size={17}/>{t("Мой профиль")}</Link>
      <button type="button" onClick={logout}><LogOut size={17}/>{t("Выйти")}</button>
    </div>
  </div>;
}

type Notification={id:string;text:string;read:boolean;at:string};
function NotificationsFloat(){
  const {api}=useApp();
  const data=useRemote(()=>api.request<Notification[]>('/collective/notifications'),[api],10000);
  const [problem,setProblem]=useState('');
  const pending=useServerUpdate()?.pending;
  useNotice(problem||(!pending?data.error:''));
  async function read(id:string){
    try{await api.request('/collective/notifications/'+id+'/read',{method:'POST'});setProblem('');data.reload()}
    catch(error){setProblem(String(error))}
  }
  return <NoticeBell placement="header" server={data.value??[]} onRead={id=>void read(id)}/>;
}

function AnimatedArea({children}:{children:ReactNode}){
  const {pathname}=useLocation();
  return <div className="page-motion" key={pathname}>{children}</div>;
}

function Workspace({logout}:{logout:()=>void}){
  const t=useLocale();
  const {mode,portal,preferenceScope,api}=useApp();
  const developer=useRemote(()=>mode==='orchestra'?api.request<{owner:boolean;membership?:{status:string}}>('/managed/development-team'):Promise.resolve(null),[api,mode]);
  const cloudDownloads=mode==='orchestra'&&(developer.value?.owner||developer.value?.membership?.status==='approved');
  const {pathname}=useLocation();
  const nav=navigation(mode,portal);
  const infrastructureOwner=mode==='orchestra'&&developer.value?.owner===true;
  if(infrastructureOwner)nav.splice(nav.length-1,0,{name:'Инфраструктура',items:[{to:'/infrastructure/services',label:'Сервисы',icon:Container},{to:'/infrastructure/ai',label:'AI и речь',icon:BrainCircuit},{to:'/infrastructure/database',label:'Базы данных',icon:Database},{to:'/infrastructure/metrics',label:'Мониторинг',icon:Radio}]});
  if(mode==='orchestra')nav[0].items.push({to:'/project-branches',label:'Ветки GitHub',icon:Network});
  if(cloudDownloads)nav[0].items.push({to:'/clouddesk-downloads',label:'Скачать CloudDesk',icon:Cloud});
  const current=nav.flatMap(section=>section.items).find(item=>item.to===pathname)?.label??(pathname==='/profile'?'Мой профиль':modes[mode].name);
  const [mobile,setMobile]=useState(false);
  const [narrow,setNarrow]=useState(()=>matchMedia('(max-width:850px)').matches);
  const drawer=useRef<HTMLElement>(null);
  const menuTrigger=useRef<HTMLButtonElement>(null);
  const closeDrawer=useRef<HTMLButtonElement>(null);
  const drawerOpen=narrow&&mobile;
  useEffect(()=>{
    const media=matchMedia('(max-width:850px)');
    const sync=()=>{setNarrow(media.matches);if(!media.matches)setMobile(false)};
    sync();media.addEventListener('change',sync);
    return()=>media.removeEventListener('change',sync);
  },[]);
  useEffect(()=>setMobile(false),[pathname]);
  useEffect(()=>{
    if(!drawerOpen)return;
    const previousOverflow=document.body.style.overflow;
    document.body.style.overflow='hidden';
    closeDrawer.current?.focus();
    const onKey=(event:KeyboardEvent)=>{
      if(event.key==='Escape'){event.preventDefault();setMobile(false);return}
      if(event.key!=='Tab')return;
      const targets=Array.from(drawer.current?.querySelectorAll<HTMLElement>('a[href],button:not([disabled]),[tabindex="0"]')??[]).filter(element=>element.getClientRects().length>0);
      const first=targets[0],last=targets[targets.length-1];
      if(!first||!last)return;
      if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus()}
      else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus()}
    };
    document.addEventListener('keydown',onKey);
    return()=>{
      document.body.style.overflow=previousOverflow;
      document.removeEventListener('keydown',onKey);
      if(menuTrigger.current?.getClientRects().length)menuTrigger.current.focus();
    };
  },[drawerOpen]);
  const emblem=modes[mode].icon;
  const ModeIcon=emblem;
  return <div className="app-shell mystic-workspace" data-workspace={mode} data-drawer-open={drawerOpen}>
    <aside id="workspace-navigation" ref={drawer} className={'sidebar '+(drawerOpen?'open':'')} inert={narrow&&!drawerOpen} role={drawerOpen?'dialog':undefined} aria-modal={drawerOpen?true:undefined} aria-label={t("Навигация MysticEgo")}>
      <button ref={closeDrawer} className="icon-btn drawer-close" type="button" aria-label={t("Закрыть меню")} onClick={()=>setMobile(false)}><X size={20}/></button>
      <Link to="/" className="brand" aria-label="MysticEgo" onClick={()=>setMobile(false)}><div className="brand-mark"><img src={"/mystic-mark.png"} alt=""/></div><strong>MysticEgo</strong></Link>
      <div className="node-label"><span className="node-icon"><ModeIcon size={18}/></span><div><strong>{t(modes[mode].name)}</strong><small>{t(modes[mode].subtitle)}</small></div></div>
      <nav aria-label={t("Главная навигация")}>{nav.map(section=><div className="nav-section" key={t(section.name)}>
        <div className="nav-title">{section.name}</div>
        {section.items.map(item=><NavLink key={item.to} to={item.to} aria-label={t(item.label)} title={t(item.label)} end={item.to==='/'} onClick={()=>setMobile(false)} className={({isActive})=>'nav-link '+(isActive?'active':'')}>
          <item.icon size={17}/><span>{t(item.label)}</span>
        </NavLink>)}
      </div>)}</nav>
      <footer className="workspace-project">
        {mode === 'orchestra' && <a href="/studio">{t("Студия и команда")}</a>}
        <LicenseLink/>
      </footer>
      <ReleaseStatus/>
    </aside>
    {drawerOpen&&<div className="shade workspace-shade" aria-hidden="true" onClick={()=>setMobile(false)}/>}
    <div className="main" inert={drawerOpen}>
      <header className="topbar orchestral-topbar">
        <span className="topbar-light" aria-hidden="true"><span className="topbar-sweep"/></span>
        <button ref={menuTrigger} type="button" className="icon-btn mobile-toggle" onClick={()=>setMobile(value=>!value)} aria-label={t("Открыть меню")} aria-controls="workspace-navigation" aria-expanded={drawerOpen}><Menu size={22}/></button>
        <div className="breadcrumb"><span>{modes[mode].name}</span><ChevronRight size={14}/><strong>{t(current)}</strong></div>
        <div className="top-actions"><MaintenanceNotices enabled={mode==='orchestra'&&developer.value?.owner===true}/><ThemeToggle scope={preferenceScope}/><AccountMenu logout={logout}/><NotificationsFloat/></div>
      </header>
      <main className="content"><AnimatedArea><Routes>
        <Route path="/" element={mode==='orchestra'||mode==='global'||mode==='local'?<OrchestraOverview/>:mode==='audit'?<OrganizationRequests overview/>:<Overview/>}/>
        {['audit','orchestra'].includes(mode)&&<Route path="/development-team" element={<DevelopmentTeam/>}/>}
        {mode==='orchestra'&&<Route path="/model-workers" element={<Navigate to="/assistant?tab=models" replace/>}/>}
        {mode==='orchestra'&&<Route path="/clouddesk-downloads" element={cloudDownloads?<CloudDeskDownloads/>:
          <p role="status">{developer.loading?'Проверяем доступ…':'Скачивание CloudDesk доступно команде разработки.'}</p>}/>}
        {mode==='orchestra'&&<Route path="/panels" element={<PanelDirectory/>}/>}
        {mode==='orchestra'&&<Route path="/project-branches" element={<ProjectBranches/>}/>}
        {mode==='orchestra'&&<Route path="/developer-assistant" element={<Navigate to="/assistant?tab=developer" replace/>}/>}
        <Route path="/profile" element={<ProfilePage/>}/>
        {mode==='orchestra'&&<Route path="/orchestra-update" element={<ProjectBranches/>}/>}
        <Route path="/collective" element={<Collective/>}/>
        {mode==='audit'&&<Route path="/organizations" element={<OrganizationRequests/>}/>}
        <Route path="/nodes" element={<NetworkPage/>}/>
        <Route path="/setup" element={<Deployment/>}/>
        <Route path="/commands" element={<CommandCenter/>}/>
        <Route path="/automation" element={<AutomationPage/>}/>
        <Route path="/faq" element={<Faq/>}/>
        <Route path="/audit" element={<Audit/>}/>
        <Route path="/diagnostics" element={<Diagnostics/>}/>
        {mode==='orchestra'&&(['services','ai','database','metrics'] as InfrastructureSection[]).map(section=><Route key={section} path={'/infrastructure/'+section} element={infrastructureOwner?<InfrastructureWorkspace key={section} section={section}/>:<p role="status">{developer.loading?'Проверяем доступ…':'Управление инфраструктурой доступно владельцу Orchestra.'}</p>}/>)}
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
        <Route path="*" element={<div className="empty"><p>{t("Страница не найдена")}</p><Link className="btn secondary" to="/">{t("К обзору")}</Link></div>}/>
      </Routes></AnimatedArea></main>
      {(mode==='agent'||mode==='audit')&&pathname!=='/assistant'&&<AssistantCompanion/>}
    </div>
  </div>;
}

export function MysticApp({mode,defaultApi}:{mode:NodeMode;title:string;defaultApi:string}){
  const t=useLocale();
  const runtime=window.__MYSTIC_CONFIG__??{};
  const portal=location.pathname==='/audit-portal'||location.pathname.startsWith('/audit-portal/');
  const storageKey='mysticego.session.v1.'+mode+(portal?'.portal':'');
  const {session,setSession,logout,restoring,restoreError,retryRestore}=useBrowserSession(storageKey,{
    url:runtime.apiUrl||(import.meta.env.DEV?defaultApi:location.origin),token:'',
    authUrl:runtime.authUrl||(import.meta.env.DEV?(import.meta.env.VITE_AUTH_URL||'http://127.0.0.1:5400'):location.origin)
  });
  const [guestToken,setGuestToken]=useState('');
  const api=useMemo(()=>new NodeApi(session.url,()=>session.token),[session.url,session.token]);
  const scope=accountScope(session,mode,portal);
  const profile=useRemote(async()=>session.token?{scope,account:await api.request<PersonalAccount>('/collective/me')}:null,[api,scope]);
  useLayoutEffect(()=>initPalette(scope),[scope]);
  useAccountPresentation(session.authUrl,session.token,scope,value=>applyAccountAppearance(scope,value));
  // Public pages work with or without a session, including during service recovery.
  const publicPath = location.pathname.replace(/\/$/, '');
  if (publicPath === '/share' || (portal && publicPath === '/audit-portal/share')) return <SharePage mode={portal?'audit':mode} portal={portal} scope={scope}/>;
  if (mode === 'orchestra') {
    if (publicPath === '/license') return <LicensePage scope={scope}/>;
    if (publicPath === '/studio') return <StudioPage scope={scope}/>;
    if (publicPath === '/developer') return <DeveloperPage scope={scope}/>;
    if (publicPath === '/desktop-downloads') return <PublicDesktopDownloads scope={scope}/>;
  }
  if(mode==='orchestra'&&location.pathname==='/agent-desktop')return <AgentDesktopEntry scope={scope}/>;
  if(restoring)return <NotificationProvider><main className="auth-experience auth-recovery"><section className="auth-form-side">
    <div className="auth-form-card">
      <h2>{t("Восстановление входа")}</h2>
      <p role="status">{t("Проверяем сохранённую сессию. Если сервер ещё запускается, вход восстановится автоматически.")}</p><ErrorBanner text={restoreError}/>
      {restoreError&&<button className="btn" onClick={retryRestore}>{t("Повторить")}</button>}
      <button className="btn secondary" onClick={logout}>{t("Войти заново")}</button>
    </div>
  </section></main><NoticeBell/></NotificationProvider>;
  if(!session.token){
    if(mode==='agent'&&guestToken)return <NotificationProvider><GuestPortal apiUrl={session.url} token={guestToken} onExit={()=>setGuestToken('')}/><NoticeBell/></NotificationProvider>;
    return <NotificationProvider><SignIn mode={mode} session={session} onLogin={setSession} onGuest={setGuestToken}/></NotificationProvider>;
  }
  return <CTX.Provider value={{mode,session,setSession,api,portal,preferenceScope:scope,personal:profile.value?.scope===scope?profile.value.account:null,profileError:profile.error,reloadProfile:profile.reload}}>
    <BrowserRouter basename={portal?'/audit-portal':undefined}><NotificationProvider key={scope}><ServerUpdateProvider><Workspace logout={logout}/></ServerUpdateProvider></NotificationProvider></BrowserRouter>
  </CTX.Provider>;
}

