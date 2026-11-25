import type {ReactNode} from 'react';
import {useNotice} from '../app/notifications';
import {RefreshCw,AlertCircle,Box} from 'lucide-react';
export const spin=<RefreshCw size={16} className="spin"/>;
export function ErrorBanner({text}:{text:string}){
  const moved=useNotice(text);
  return text&&!moved?<div className="error-banner"><AlertCircle size={17}/>{text}</div>:null;
}
export function Panel({title,aside,children}:{title:string;aside?:ReactNode;children:ReactNode}){return <section className="panel"><div className="panel-title"><h2>{title}</h2>{aside}</div>{children}</section>}
export function Header({title,description,action}:{title:string;description?:string;action?:ReactNode}){return <div className="page-header"><div><h1>{title}</h1>{description&&<p>{description}</p>}</div>{action}</div>}
export function Empty({text}:{text:string}){return <div className="empty"><Box size={26}/><p>{text}</p></div>}
export function Badge({ok,text}:{ok:boolean;text:string}){return <span className={'status '+(ok?'good':'bad')}><span className="dot"/>{text}</span>}

