import {useLocale} from '../app/presentation';
import type {ReactNode} from 'react';
import {Activity} from 'lucide-react';
import './dashboard.css';

export function DashboardHero({title,description,eyebrow,action,status}:{title:string;description:string;eyebrow:string;action?:ReactNode;status?:ReactNode}){
 const t=useLocale();
 return <section className="dashboard-hero">
   <div className="dashboard-landscape" aria-hidden="true"/>
   <div className="dashboard-hero-copy"><span className="dashboard-eyebrow"><Activity size={15} aria-hidden="true"/>{eyebrow.split(' · ').map(tPart=>t(tPart)).join(' · ')}</span>
   <h1>{t(title)}</h1><p>{t(description)}</p>
   {status&&<div className="dashboard-hero-status">{status}</div>}
   {action&&<div className="dashboard-hero-actions">{action}</div>}
   </div>
 </section>;
}
