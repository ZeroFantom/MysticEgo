import {createContext,useCallback,useContext,useEffect,useMemo,useRef,useState,type ReactNode} from 'react';
import {Bell,X} from 'lucide-react';

type Notice={id:string;text:string;at:string;read:boolean};
type Feed={notices:Notice[];notify:(text:string)=>void;read:(id:string)=>void};
const Context=createContext<Feed|null>(null);

export function NotificationProvider({children}:{children:ReactNode}){
  const [notices,setNotices]=useState<Notice[]>([]);
  const serial=useRef(0);
  const notify=useCallback((text:string)=>{
    const value=text.trim();
    if(!value)return;
    const id="local-"+Date.now()+"-"+(++serial.current);
    setNotices(items=>{
      if(items.some(item=>item.text===value&&Date.now()-Date.parse(item.at)<30000))return items;
      return [{id,text:value,at:new Date().toISOString(),read:false},...items].slice(0,30);
    });
  },[]);
  const read=useCallback((id:string)=>setNotices(items=>items.map(item=>item.id===id?{...item,read:true}:item)),[]);
  const value=useMemo(()=>({notices,notify,read}),[notices,notify,read]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
export const useNoticeFeed=()=>useContext(Context);
export function useNotice(text:string){
  const notify=useNoticeFeed()?.notify;
  useEffect(()=>{if(text)notify?.(text)},[notify,text]);
  return !!notify;
}

export function NoticeBell({server=[],onRead}:{server?:Notice[];onRead?:(id:string)=>void}){
  const feed=useNoticeFeed();
  const [open,setOpen]=useState(false);
  const [dismissed,setDismissed]=useState('');
  const local=feed?.notices??[];
  const all=[...local,...server].sort((a,b)=>Date.parse(b.at)-Date.parse(a.at));
  const unread=all.filter(row=>!row.read).length;
  const recent=local[0];
  const visible=recent&&!recent.read&&recent.id!==dismissed;
  useEffect(()=>{
    if(!recent||recent.read)return;
    const timer=setTimeout(()=>setDismissed(recent.id),6000);
    return ()=>clearTimeout(timer);
  },[recent?.id,recent?.read]);
  return <div className="notification-float">
    {!open&&visible&&<div className="notification-drawer notification-preview" role="status"><p>{recent.text}</p>
      <button className="icon-btn" onClick={()=>setDismissed(recent.id)} aria-label="Скрыть уведомление"><X size={16}/></button></div>}
    {open&&<section className="notification-drawer" aria-label="Уведомления">
      <header><h2>Уведомления</h2><button className="icon-btn" onClick={()=>setOpen(false)} aria-label="Закрыть уведомления"><X size={20}/></button></header>
      {all.length===0&&<p className="footnote">Новых уведомлений нет.</p>}
      {all.map(item=><article key={item.id}><div><p>{item.text}</p><time>{new Date(item.at).toLocaleString('ru-RU')}</time></div>
        {!item.read&&<button className="icon-btn" onClick={()=>local.some(row=>row.id===item.id)?feed?.read(item.id):onRead?.(item.id)}
          aria-label="Отметить прочитанным">✓</button>}
      </article>)}
    </section>}
    <button className="bell-button" aria-label={'Уведомления'+(unread?' · '+unread+' непрочитанных':'')} aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      <Bell size={22}/>{unread>0&&<span>{unread>99?'99+':unread}</span>}
    </button>
  </div>;
}
