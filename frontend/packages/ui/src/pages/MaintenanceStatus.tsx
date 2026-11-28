import {useEffect,useRef} from 'react';
import {useApp,useRemote} from '../app/context';
import {useNoticeFeed} from '../app/notifications';

export type Maintenance={installed:boolean;workerState?:string;source?:string;socketAvailable?:boolean;components:{name:string;state:string;message:string;checkedAt:string;reason?:string;lastAttemptAt?:string;nextRetryAt?:string}[]};
const endpoint='/managed/infrastructure-maintenance';
export const names:Record<string,string>={worker:'Служба обслуживания Orchestra',orchestra:'Orchestra',docker:'Docker / Compose',lmstudio:'LM Studio',voice:'Распознавание речи','postgres-service':'Служба PostgreSQL',database:'Текущая база'};
export const labels:Record<string,string>={checking:'Проверяется',available:'Готово',repairing:'Восстановление',degraded:'Требует внимания',external:'Заданная конфигурация',disabled:'Отключено'};
export const useMaintenance=()=>{const {api}=useApp();return useRemote(()=>api.request<Maintenance>(endpoint),[api],10000)};
export function workerMessage(value:Maintenance){
  if(value.workerState==='missing')return 'Служба обслуживания не обнаружена. Обновление Orchestra устанавливает её автоматически.';
  if(value.workerState==='unreachable')return 'Локальный сокет занят или недоступен. Проверка будет повторена.';
  if(value.workerState==='unreadable')return 'Отчёт службы обслуживания недоступен. Будет выполнена повторная проверка.';
  return 'Ожидается первый отчёт службы обслуживания на VPS.';
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
      const workerKey=result.installed?'reporting':result.workerState??'waiting';
      if(!result.installed&&previous.current.get('worker-state')!==workerKey)notify?.('Обслуживание Orchestra: '+workerMessage(result),'warning');
      if(result.installed&&previous.current.has('worker-state')&&previous.current.get('worker-state')!=='reporting')notify?.('Служба обслуживания Orchestra передала отчёт.','success');
      previous.current.set('worker-state',workerKey);
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
