import type {ReactNode} from 'react';
import {Activity} from 'lucide-react';
import './dashboard.css';

export function DashboardHero({title,description,eyebrow,action,status}:{title:string;description:string;eyebrow:string;action?:ReactNode;status?:ReactNode}){
 return <section className="dashboard-hero">
   <div className="dashboard-hero-copy"><span className="dashboard-eyebrow"><Activity size={15} aria-hidden="true"/>{eyebrow}</span>
   <h1>{title}</h1><p>{description}</p>
   {status&&<div className="dashboard-hero-status">{status}</div>}
   {action&&<div className="dashboard-hero-actions">{action}</div>}
   </div>
 </section>;
}
