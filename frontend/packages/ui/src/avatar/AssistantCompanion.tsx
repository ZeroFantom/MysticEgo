import {useState} from 'react';
import {Link} from 'react-router-dom';
import {Bot,Maximize2,MoveHorizontal,X} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {AssistantChat,type AssistantStatus} from './AssistantChat';

export function AssistantCompanion(){
  const {api}=useApp();
  const [open,setOpen]=useState(false);
  const [patrol,setPatrol]=useState(false);
  const status=useRemote(()=>open?api.request<AssistantStatus>('/assistant/workbench'):Promise.resolve(null),[api,open],open?15000:0);
  return <aside className={'assistant-float'+(patrol&&open?' drifting':'')} aria-label="Ассистент MysticEgo">
    {open&&<section className="assistant-float-box" aria-label="Чат с ассистентом">
      <header><strong>Ассистент</strong><Link className="icon-btn" to="/assistant" aria-label="Открыть полный чат"><Maximize2 size={17}/></Link>
        <button className="icon-btn" aria-label="Двигаться вдоль страницы" aria-pressed={patrol} onClick={()=>setPatrol(value=>!value)}><MoveHorizontal size={17}/></button>
        <button className="icon-btn" aria-label="Закрыть чат" onClick={()=>{setOpen(false);setPatrol(false)}}><X size={19}/></button></header>
      {status.error&&<p className="error-banner">{status.error}</p>}
      <AssistantChat status={status.value} compact patrol={patrol}/>
    </section>}
    <button className="assistant-float-button" aria-label={open?'Свернуть ассистента':'Открыть чат с ассистентом'} aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      {open?<X size={22}/>:<Bot size={23}/>}</button>
  </aside>;
}
