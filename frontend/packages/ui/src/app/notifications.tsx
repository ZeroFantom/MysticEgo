import {createContext,useCallback,useContext,useEffect,useId,useMemo,useRef,useState,type ReactNode} from 'react';
import {Bell,CheckCircle2,Info,TriangleAlert,X,XCircle} from 'lucide-react';

export type NoticeTone='info'|'success'|'warning'|'error';
type Notice={id:string;text:string;at:string;read:boolean;tone?:NoticeTone;kind?:string;expiresAt?:number};
type Feed={notices:Notice[];notify:(text:string,tone?:NoticeTone)=>void;read:(id:string)=>void;readAll:()=>void};
const duration:Record<NoticeTone,number>={success:4500,info:6000,warning:9000,error:12000};
const icons={success:CheckCircle2,info:Info,warning:TriangleAlert,error:XCircle};
export function noticeTone(item:{tone?:NoticeTone;kind?:string}):NoticeTone{
  return item.tone??(item.kind==='success'||item.kind==='warning'||item.kind==='error'?item.kind:'info');
}
const Context=createContext<Feed|null>(null);

export function NotificationProvider({children}:{children:ReactNode}){
  const [notices,setNotices]=useState<Notice[]>([]);
  const serial=useRef(0);
  const notify=useCallback((text:string,tone:NoticeTone='info')=>{
    const value=text.trim();
    if(!value)return;
    const now=Date.now();
    const id="local-"+now+"-"+(++serial.current);
    setNotices(items=>{
      if(items.some(item=>item.text===value&&item.tone===tone&&now-Date.parse(item.at)<30000))return items;
      return [{id,text:value,at:new Date(now).toISOString(),read:false,tone,expiresAt:now+duration[tone]},...items].slice(0,30);
    });
  },[]);
  const read=useCallback((id:string)=>setNotices(items=>items.map(item=>item.id===id?{...item,read:true}:item)),[]);
  const readAll=useCallback(()=>setNotices(items=>items.map(item=>item.read?item:{...item,read:true})),[]);
  useEffect(()=>{
    const pending=notices.filter(item=>!item.read&&item.expiresAt!==undefined);
    if(!pending.length)return;
    const timer=window.setTimeout(()=>{
      const now=Date.now();
      setNotices(items=>items.map(item=>!item.read&&(item.expiresAt??Infinity)<=now?{...item,read:true}:item));
    },Math.max(0,Math.min(...pending.map(item=>item.expiresAt!))-Date.now()));
    return ()=>window.clearTimeout(timer);
  },[notices]);
  const value=useMemo(()=>({notices,notify,read,readAll}),[notices,notify,read,readAll]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
export const useNoticeFeed=()=>useContext(Context);
export function useNotice(text:string,tone:NoticeTone='error'){
  const notify=useNoticeFeed()?.notify;
  useEffect(()=>{if(text)notify?.(text,tone)},[notify,text,tone]);
  return !!notify;
}

export function NoticeBell({server=[],onRead}:{server?:Notice[];onRead?:(id:string)=>void}){
  const feed=useNoticeFeed();
  const [open,setOpen]=useState(false);
  const drawerId=useId();
  const container=useRef<HTMLDivElement>(null);
  const trigger=useRef<HTMLButtonElement>(null);
  const close=useRef<HTMLButtonElement>(null);
  useEffect(()=>{
    if(!open)return;
    close.current?.focus();
    const outside=(event:PointerEvent)=>{if(event.target instanceof Node&&!container.current?.contains(event.target))setOpen(false)};
    const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'&&!event.defaultPrevented){setOpen(false);trigger.current?.focus()}};
    document.addEventListener('pointerdown',outside);document.addEventListener('keydown',escape);
    return()=>{document.removeEventListener('pointerdown',outside);document.removeEventListener('keydown',escape)};
  },[open]);
  const local=feed?.notices??[];
  const all=[...local,...server].sort((a,b)=>Date.parse(b.at)-Date.parse(a.at));
  const unread=all.filter(row=>!row.read).length;
  const recent=local.find(item=>!item.read);
  const Icon=icons[noticeTone(recent??{})];
  return <div className="notification-float" ref={container}>
    {!open&&recent&&<div className="notification-drawer notification-preview" data-tone={noticeTone(recent)} role="status">
      <Icon size={19} aria-hidden="true"/><p>{recent.text}</p>
      <button className="icon-btn" onClick={()=>feed?.read(recent.id)} aria-label="Скрыть уведомление"><X size={16}/></button></div>}
    <section id={drawerId} className="notification-drawer motion-disclosure" data-motion-open={open} inert={!open} aria-hidden={!open} aria-label="Уведомления">
      <header><h2>Уведомления</h2><button ref={close} type="button" className="icon-btn" onClick={()=>{setOpen(false);trigger.current?.focus()}} aria-label="Закрыть уведомления"><X size={20}/></button></header>
      {all.length===0&&<p className="footnote">Новых уведомлений нет.</p>}
      {all.map(item=><article key={item.id} data-tone={noticeTone(item)}><div><p>{item.text}</p><time>{new Date(item.at).toLocaleString('ru-RU')}</time></div>
        {!item.read&&<button className="icon-btn" onClick={()=>local.some(row=>row.id===item.id)?feed?.read(item.id):onRead?.(item.id)}
          aria-label="Отметить прочитанным">✓</button>}
      </article>)}
    </section>
    <button ref={trigger} type="button" className="bell-button" aria-controls={drawerId} aria-label={'Уведомления'+(unread?' · '+unread+' непрочитанных':'')} aria-expanded={open} onClick={()=>{if(!open)feed?.readAll();setOpen(!open)}}>
      <Bell size={22}/>{unread>0&&<span>{unread>99?'99+':unread}</span>}
    </button>
  </div>;
}
