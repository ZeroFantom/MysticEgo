import {useEffect,useRef,useState} from 'react';
import {useLocation} from 'react-router-dom';
import {MousePointer2,Square} from 'lucide-react';
import {useApp} from './context';
import './interface-control.css';

type Target={path:string;label:string};
type Frame={path:string;targets:Target[]};
type Outcome={id:string;state:'done'|'failed';path:string};
type Session={id:string;expiresAt:string;pending?:{id:string;path:string;expiresAt:string};last?:Outcome};
const endpoint='/interface-control/sessions';
const routePattern=/^(?:\/(?:[a-z][a-z0-9-]*(?:\/[a-z][a-z0-9-]*){0,2})?|\/assistant\?tab=(?:chat|persona|models|developer))$/;
function observedPath(path:string,search:string){const candidate=path+search;return routePattern.test(candidate)?candidate:routePattern.test(path)?path:'/unlisted'}
const delay=(ms:number)=>new Promise<void>(resolve=>window.setTimeout(resolve,ms));
function navigationControls(){
  return Array.from(document.querySelectorAll<HTMLElement>('nav[data-interface-nav] a[data-interface-path],nav.assistant-workspace-nav button[data-interface-path]'));
}
function readFrame(path:string):Frame{
  return {path:routePattern.test(path)?path:'/unlisted',targets:navigationControls().flatMap(a=>{
    const target=a.dataset.interfacePath;
    const label=a.getAttribute('aria-label')??'';
    return target&&routePattern.test(target)&&label?[{path:target,label:label.slice(0,80)}]:[];
  })};
}
function isVisible(element:HTMLElement){
  const rect=element.getBoundingClientRect();
  return rect.width>0&&rect.height>0&&!element.closest('[inert],[hidden]')&&getComputedStyle(element).visibility!=='hidden';
}

/** Only an existing, explicitly marked navigation link may be activated. */
export async function visibleNavigation(path:string,allowed:()=>boolean){
  if(!routePattern.test(path)||!allowed()||document.visibilityState!=='visible')return false;
  const link=navigationControls().find(a=>a.dataset.interfacePath===path);
  if(!link)return false;
  if(!isVisible(link)){
    const menu=document.querySelector<HTMLButtonElement>('button[data-interface-menu]');
    if(!menu||!isVisible(menu))return false;
    menu.click();await delay(300);
  }
  if(!allowed()||!isVisible(link))return false;
  link.scrollIntoView({block:'nearest',behavior:'smooth'});await delay(220);
  if(!allowed()||!isVisible(link))return false;
  const point=link.getBoundingClientRect();
  const cursor=document.createElement('div');
  cursor.className='interface-control-cursor';cursor.setAttribute('aria-hidden','true');
  cursor.textContent='➤';
  cursor.style.left=window.innerWidth/2+'px';cursor.style.top=window.innerHeight/2+'px';
  document.body.append(cursor);
  try{
    await delay(30);cursor.style.left=point.left+point.width/2+'px';cursor.style.top=point.top+point.height/2+'px';
    await delay(390);
    if(!allowed()||document.visibilityState!=='visible'||!link.isConnected||!isVisible(link))return false;
    const hit=document.elementFromPoint(point.left+point.width/2,point.top+point.height/2);
    if(!hit||!link.contains(hit))return false;
    link.focus({preventScroll:true});cursor.classList.add('clicked');link.click();
    await delay(220);return true;
  }finally{cursor.remove()}
}
let localAction:((path:string)=>Promise<boolean>)|null=null;
export function assistantNavigate(path:string){return localAction?.(path)??Promise.resolve(false)}

export function InterfaceControl(){
  const {api}=useApp();
  const {pathname,search}=useLocation();
  const current=useRef(observedPath(pathname,search));current.current=observedPath(pathname,search);
  const [session,setSession]=useState<Session|null>(null),[busy,setBusy]=useState(false),[message,setMessage]=useState('');
  const active=useRef<Session|null>(null),running=useRef(false),epoch=useRef(0);
  function stopLocal(){
    const previous=active.current;active.current=null;epoch.current++;
    setSession(null);
    if(previous)void api.request(endpoint+'/'+previous.id,{method:'DELETE',signal:AbortSignal.timeout(3000)}).catch(()=>{});
  }
  useEffect(()=>{
    return()=>{const previous=active.current;active.current=null;epoch.current++;if(previous)void api.request(endpoint+'/'+previous.id,{method:'DELETE',signal:AbortSignal.timeout(3000)}).catch(()=>{});};
  },[api]);
  useEffect(()=>{
    const key=(event:KeyboardEvent)=>{if(event.key==='Escape'&&active.current){stopLocal();setMessage('Управление остановлено.')}};
    const visibility=()=>{if(document.visibilityState!=='visible'&&active.current){stopLocal();setMessage('Управление остановлено: окно скрыто.')}};
    window.addEventListener('keydown',key);document.addEventListener('visibilitychange',visibility);
    return()=>{window.removeEventListener('keydown',key);document.removeEventListener('visibilitychange',visibility)};
  },[api]);
  useEffect(()=>{
    localAction=async path=>{
      const version=epoch.current;
      if(!active.current||running.current||Date.now()>=Date.parse(active.current.expiresAt))return false;
      running.current=true;
      try{
        const clicked=await visibleNavigation(path,()=>!!active.current&&version===epoch.current&&Date.now()<Date.parse(active.current.expiresAt));
        return clicked&&current.current===path;
      }finally{running.current=false}
    };
    return()=>{localAction=null};
  },[api]);
  useEffect(()=>{
    if(!session)return;
    const expiry=window.setTimeout(()=>{stopLocal();setMessage('Срок разрешения истёк.');},Math.max(0,Date.parse(session.expiresAt)-Date.now()));
    let stopped=false,timer:number|undefined,result:Outcome|undefined;
    const id=session.id,version=epoch.current;
    async function tick(){
      if(stopped||!active.current||version!==epoch.current)return;
      try{
        const data=await api.request<Session>(endpoint+'/'+id+'/poll',{method:'POST',body:JSON.stringify({frame:readFrame(current.current),result}),signal:AbortSignal.timeout(4000)});
        result=undefined;
        if(stopped||!active.current||version!==epoch.current)return;
        if(data.pending&&!running.current){
          running.current=true;
          try{
            const pending=data.pending;
            const clicked=Date.now()<Date.parse(pending.expiresAt)&&await visibleNavigation(pending.path,()=>!!active.current&&version===epoch.current&&Date.now()<Date.parse(pending.expiresAt));
            result={id:pending.id,state:clicked&&current.current===pending.path?'done':'failed',path:current.current};
            setMessage(result.state==='done'?'Переход выполнен: '+pending.path:'Переход не выполнен.');
          }finally{running.current=false}
        }
      }catch{
        if(!stopped&&version===epoch.current){stopLocal();setMessage('Сессия завершена или сервер недоступен.');}
        return;
      }
      if(!stopped)timer=window.setTimeout(tick,900);
    }
    void tick();
    return()=>{stopped=true;window.clearTimeout(timer);window.clearTimeout(expiry)};
  },[session?.id,api]);
  async function start(){
    if(busy||active.current)return;
    if(!window.confirm('Разрешить ассистенту и MCP управлять навигацией этого видимого окна на 10 минут? Нажмите Escape, чтобы остановить.'))return;
    setBusy(true);setMessage('');
    const version=epoch.current;
    try{
      const data=await api.request<Session>(endpoint,{method:'POST',body:JSON.stringify(readFrame(current.current)),signal:AbortSignal.timeout(5000)});
      if(version!==epoch.current||document.visibilityState!=='visible'){void api.request(endpoint+'/'+data.id,{method:'DELETE'}).catch(()=>{});return;}
      active.current=data;setSession(data);setMessage('Разрешена навигация в этом окне.');
    }catch(error){setMessage(error instanceof Error?error.message:String(error))}
    finally{setBusy(false)}
  }
  return <div className="interface-control" data-enabled={!!session}>
    <button type="button" className="btn secondary" disabled={busy} onClick={()=>session?stopLocal():void start()} aria-label={session?'Остановить MCP':'Управление MCP'} title="Управление интерфейсом через ассистента и MCP">
      {session?<Square size={15}/>:<MousePointer2 size={15}/>} <span className="interface-control-label">{session?'Остановить MCP':'Управление MCP'}</span>
    </button>
    {session&&<span className="interface-control-active" role="status">ИИ управляет навигацией · Esc</span>}
    {message&&<span className="interface-control-message" role="status">{message}</span>}
  </div>;
}
