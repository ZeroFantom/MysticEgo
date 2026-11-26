import {ArrowUpRight,Boxes,ShieldCheck} from 'lucide-react';
import type {NodeMode} from '@mysticego/sdk';
const roles:Record<NodeMode,{title:string;description:string}>={
  orchestra:{title:'Orchestra',description:'Центр разработки и управления экосистемой.'},
  global:{title:'Global',description:'Управление организацией и сетью площадок.'},
  local:{title:'Local',description:'Управление площадкой и рабочими станциями.'},
  agent:{title:'Agent',description:'Рабочая станция и инструменты диагностики.'},
  audit:{title:'Audit',description:'Личное пространство и профиль MysticEgo.'}
};
export function SharePage({mode,portal}:{mode:NodeMode;portal:boolean}){
  const role=roles[mode];
  return <main className="mystic-share" data-share-role={mode}>
    <section className="mystic-share-card">
      <p className="mystic-share-brand"><Boxes size={23} aria-hidden="true"/> MysticEgo</p>
      <span className="mystic-share-kicker">CONTROL NETWORK</span>
      <h1>{role.title}</h1>
      <p>{role.description}</p>
      <a className="btn mystic-share-open" href={portal?'/audit-portal/':'/'}>Открыть панель <ArrowUpRight size={18} aria-hidden="true"/></a>
      <small><ShieldCheck size={15} aria-hidden="true"/> Доступ к рабочим данным только после входа.</small>
    </section>
  </main>;
}
