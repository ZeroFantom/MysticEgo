import {useEffect,useRef} from 'react';
import {useApp,useRemote} from '../app/context';
import {useNoticeFeed} from '../app/notifications';
import {Panel} from '../components/primitives';

export type Maintenance={installed:boolean;components:{name:string;state:string;message:string;checkedAt:string}[]};
const endpoint='/managed/infrastructure-maintenance';
const names:Record<string,string>={worker:'Служба обслуживания',orchestra:'Orchestra',docker:'Docker / Compose',lmstudio:'LM Studio',voice:'Распознавание речи','postgres-service':'Служба PostgreSQL',database:'Текущая база'};
const labels:Record<string,string>={checking:'Проверяется',available:'Готово',repairing:'Восстановление',degraded:'Требует внимания',external:'Заданная конфигурация',disabled:'Отключено'};
export function MaintenanceStatus(){
  const {api}=useApp();
  const state=useRemote(()=>api.request<Maintenance>(endpoint),[api],10000);
  if(!state.value)return null;
  return <Panel title="Автоматическое обслуживание Orchestra">
    {!state.value.installed&&<p role="status">Локальный worker ещё не передал состояние. Проверьте встроенное обновление Orchestra.</p>}
    <div className="stack">{state.value.components.map(component=><article className="panel" key={component.name}>
      <strong>{names[component.name]??component.name} · {labels[component.state]??component.state}</strong>
      <p>{component.message}</p><small>Проверено: {new Date(component.checkedAt).toLocaleString('ru-RU')}</small>
    </article>)}</div>
    <p className="footnote">Проверки выполняются на VPS независимо от CloudDesk. Смена провайдера и несовместимые миграции базы требуют отдельного подтверждения.</p>
  </Panel>;
}

export function MaintenanceNotices({enabled}:{enabled:boolean}){
  const {api}=useApp();
  const notify=useNoticeFeed()?.notify;
  const previous=useRef(new Map<string,string>());
  const snapshot=useRemote(()=>enabled?api.request<Maintenance>(endpoint):Promise.resolve(null),[api,enabled],15000);
  const update=useRemote(()=>enabled?api.request<{phase:string;candidate?:{tag:string};message?:string}>('/orchestra/update'):Promise.resolve(null),[api,enabled],15000);
  useEffect(()=>{
    if(!enabled)return;
    const result=snapshot.value;
    if(result){
      if(!result.installed&&!previous.current.has('worker'))notify?.('Обслуживание Orchestra: локальный worker ещё не передал состояние. Проверьте встроенное обновление.','warning');
      previous.current.set('worker',String(result.installed));
      for(const c of result.components){
        const before=previous.current.get(c.name);
        if(before!==c.state){
          if(c.state==='degraded')notify?.((names[c.name]??c.name)+': '+c.message,'warning');
          if(c.state==='repairing')notify?.((names[c.name]??c.name)+': '+c.message,'info');
          if(c.state==='available'&&before&&['degraded','repairing'].includes(before))notify?.((names[c.name]??c.name)+': восстановлено.','success');
          previous.current.set(c.name,c.state);
        }
      }
    }
    if(update.value){
      const key=update.value.phase+':'+(update.value.candidate?.tag??'');
      if(previous.current.get('update')!==key&&update.value.phase==='ready'&&update.value.candidate)
        notify?.('Доступно обновление '+update.value.candidate.tag+'. Откройте «Ветки GitHub» для подтверждения.','info');
      previous.current.set('update',key);
    }
  },[enabled,snapshot.value,update.value,notify]);
  return null;
}
