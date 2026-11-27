import {ApiError} from '@mysticego/sdk';
import {createContext,useContext,useEffect,useRef,useState,type ReactNode} from 'react';
import {useApp} from './context';
import {useNoticeFeed} from './notifications';

export type UpdateTarget='orchestra'|'audit'|'both';
export type MaintenanceUpdate={state:string;release?:string;reason?:string;checkedAt:string;retryAfter?:number};
export type UpdateState={phase:string;protocol:number;current?:string;currentAudit?:string;maintenance?:MaintenanceUpdate;
  candidate?:{tag:string;digest:string};targets?:UpdateTarget[];message?:string;lastTarget?:UpdateTarget};
type Request={tag:string;digest:string;target:UpdateTarget};
type Pending={tag:string;target:UpdateTarget;startedAt:number;previousFailure?:string};
export const targetLabel:Record<UpdateTarget,string>={orchestra:'Orchestra',audit:'Audit',both:'Orchestra + Audit'};
const timeout=20*60_000;
const updatePath='/orchestra/update';

export function updateInstalled(status:UpdateState,order:Pending){
  return (order.target==='audit'||status.current===order.tag)&&
    (order.target==='orchestra'||status.currentAudit===order.tag);
}
export function transientUpdateError(error:unknown){
  return error instanceof ApiError?[408,429,502,503,504].includes(error.status):
    error instanceof TypeError||error instanceof SyntaxError||error instanceof Error&&/тайм|timeout|время ожидания|отведённое время/i.test(error.message);
}
const Context=createContext<{pending:Pending|null;reloading:boolean;statusText:string;stopTracking:()=>void;approve:(request:Request,status:UpdateState,viaPlan?:boolean)=>Promise<void>}|null>(null);
export const useServerUpdate=()=>useContext(Context);

export function ServerUpdateProvider({children}:{children:ReactNode}){
  const {api,mode,preferenceScope}=useApp();
  const notify=useNoticeFeed()?.notify;
  const key='mysticego.server-update.'+preferenceScope;
  const [pending,setPending]=useState<Pending|null>(()=>{
    if(mode!=='orchestra')return null;
    try{
      const value=JSON.parse(sessionStorage.getItem(key)??'null') as Pending|null;
      return value&&Object.hasOwn(targetLabel,value.target)&&Date.now()-value.startedAt<timeout?value:null;
    }catch{return null}
  });
  const [reloading,setReloading]=useState(false);
  const [statusText,setStatusText]=useState('Проверяем состояние обновления.');
  const active=useRef(pending);
  function save(value:Pending|null){
    active.current=value;
    setPending(value);
    try{if(value)sessionStorage.setItem(key,JSON.stringify(value));else sessionStorage.removeItem(key)}catch{}
  }
  async function approve(request:Request,status:UpdateState,viaPlan=false){
    if(active.current)return;
    const order={tag:request.tag,target:request.target,startedAt:Date.now(),
      previousFailure:status.phase==='failed'?status.message??'':undefined};
    setStatusText('Отправляем подтверждение релиза '+request.tag+'…');save(order);
    try{
      await api.request<void>(viaPlan?updatePath+'/plan':updatePath,{method:'POST',body:JSON.stringify({...request,confirmed:true,...(viaPlan?{action:'update'}:{})})});
      setStatusText('Запрос принят. Ожидаем службу обновления.');
      notify?.(`Обновление ${targetLabel[request.target]} до ${request.tag} принято.`,'info');
    }catch(error){
      if(active.current!==order)return;
      if(transientUpdateError(error)&&!(error instanceof ApiError&&error.status===503&&error.message.includes('не настроена'))){
        setStatusText('Сервер не ответил. Проверяем версию и состояние службы; повторная отправка не выполняется.');
        notify?.('Связь с сервером прервалась. Проверяем, было ли принято обновление.','warning');
        return;
      }
      save(null);
      throw error;
    }
  }
  useEffect(()=>{
    if(!pending)return;
    let canceled=false,progress=false;
    let timer:number;
    async function poll(order:Pending){
      if(Date.now()-order.startedAt>=timeout){
        setReloading(false);save(null);
        notify?.('Не удалось подтвердить итог обновления. Проверьте установленную версию и журнал службы.','warning');
        return;
      }
      try{
        const status=await api.request<UpdateState>(updatePath,{cache:'no-store'});
        if(canceled||active.current!==order)return;
        setStatusText('Сервер отвечает. Установлено: '+(status.current??'неизвестно')+' · '+status.phase);
        if(status.phase!=='switching'&&status.phase!=='downloading'&&updateInstalled(status,order)&&
          !(order.target!=='audit'&&status.maintenance?.release===order.tag&&status.maintenance.state==='repairing')){
          setReloading(true);setStatusText('Версия подтверждена. Перезагружаем интерфейс.');
          const maintenanceProblem=order.target!=='audit'&&status.maintenance&&['degraded','unavailable'].includes(status.maintenance.state);
          notify?.(`${targetLabel[order.target]} обновлён до ${order.tag}.`+(maintenanceProblem?' Служба обслуживания будет проверена повторно.':' Страница скоро перезагрузится.'),maintenanceProblem?'warning':'success');
          timer=window.setTimeout(()=>{
            save(null);
            window.location.reload();
          },4500);
          return;
        }
        if(status.candidate?.tag===order.tag&&['downloading','switching'].includes(status.phase))progress=true;
        const failure=status.phase==='failed'&&status.candidate?.tag===order.tag&&
          (!status.lastTarget||status.lastTarget===order.target)&&(progress||order.previousFailure!==(status.message??''));
        if(failure){
          save(null);
          notify?.(status.message||'Служба прервала обновление. Проверьте журнал на VPS.','error');
          return;
        }
      }catch(error){
        if(canceled||active.current!==order)return;
        if(!transientUpdateError(error)){
          save(null);
          notify?.(error instanceof Error?error.message:String(error),'error');
          return;
        }
        setStatusText('Сервер пока недоступен. Причина может быть вне обновления. Проверяем связь без повторной установки.');
      }
      if(!canceled&&active.current===order)timer=window.setTimeout(()=>void poll(order),3500);
    }
    timer=window.setTimeout(()=>void poll(pending),2000);
    return ()=>{canceled=true;window.clearTimeout(timer)};
  },[api,pending,notify,key]);
  return <Context.Provider value={{pending,reloading,approve,statusText,stopTracking:()=>{setReloading(false);save(null)}}}>{children}{pending&&<aside role="status" className="browser-consent"><strong>Обновление {targetLabel[pending.target]} · {pending.tag}</strong><p>{statusText}</p><p>Начато: {new Date(pending.startedAt).toLocaleTimeString()}. Проверка до 20 минут.</p><button className="btn secondary" onClick={()=>{setReloading(false);save(null)}}>Прекратить отслеживание (не отменяет установку)</button></aside>}</Context.Provider>;
}
