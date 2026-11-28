import {useEffect,useState} from 'react';
import {useNavigate} from 'react-router-dom';
import {useApp} from './context';
import {ANDROID_ORIGIN} from './androidLaunch';
import {deviceLabel,type SessionDevice} from './QrSignIn';
import {useLocale} from './presentation';

const routes={'open-assistant':'/assistant','open-collective':'/collective','open-profile':'/profile','open-avatars':'/avatars'} as const;
const labels={'open-assistant':'Открыть ассистента','open-collective':'Открыть коллектив','open-profile':'Открыть профиль','open-avatars':'Открыть студию VRM'} as const;
type Action=keyof typeof routes;
type SessionRow={id:string|null;current:boolean;device?:SessionDevice;userAgent?:string};
type Item={id:string;source:string;target:string;action:Action;expires:string;state:string;incoming:boolean};
export function DeviceActions(){
 const {session}=useApp(),navigate=useNavigate(),t=useLocale();
 const [enabled,setEnabled]=useState(false),[sessions,setSessions]=useState<SessionRow[]>([]),[items,setItems]=useState<Item[]>([]);
 const [target,setTarget]=useState(''),[action,setAction]=useState<Action>('open-assistant'),[busy,setBusy]=useState(false),[message,setMessage]=useState('');
 const eligible=location.origin===ANDROID_ORIGIN&&session.authUrl.replace(/\/$/,'')===location.origin;
 async function call(path:string,body?:unknown,signal?:AbortSignal){
  const response=await fetch('/api/auth/'+path,{method:body===undefined?'GET':'POST',headers:{Authorization:'Bearer '+session.token,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),cache:'no-store',credentials:'omit',redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(10000)]):AbortSignal.timeout(10000)});
  if(!response.ok)throw Error('Сервис запросов недоступен.');return response.json();
 }
 useEffect(()=>{setEnabled(false);setItems([]);setSessions([]);setTarget('');},[session.username]);
 useEffect(()=>{
  if(!eligible||!enabled)return;const controller=new AbortController();let timer:number|undefined;
  async function poll(){try{const [s,a]=await Promise.all([call('sessions',undefined,controller.signal),call('device-actions',undefined,controller.signal)]);if(!controller.signal.aborted){setSessions(s);setItems(a)}}catch{if(!controller.signal.aborted)setMessage('Не удалось обновить список запросов.')}finally{if(!controller.signal.aborted)timer=window.setTimeout(poll,12000)}}
  void poll();return()=>{controller.abort();window.clearTimeout(timer)};
 },[enabled,eligible,session.token]);
 async function send(){
  setBusy(true);setMessage('');try{const r=await call('device-actions/mcp',{jsonrpc:'2.0',id:crypto.randomUUID(),method:'tools/call',params:{name:'device.request',arguments:{targetId:target,action}}});if(r.error||r.result?.isError)throw Error();setMessage('Запрос отправлен. Подтвердите на другом устройстве.');setItems(await call('device-actions'))}catch{setMessage('Не удалось отправить запрос.')}finally{setBusy(false)}
 }
 async function decide(item:Item,approve:boolean){
  setBusy(true);try{const r=await call('device-actions/decide',{id:item.id,approve});setItems(old=>old.filter(x=>x.id!==item.id));if(approve&&r.state==='approved'&&r.action===item.action&&Object.hasOwn(routes,item.action))navigate(routes[item.action]);else setMessage('Запрос отклонён.')}catch{setMessage('Запрос истёк или недоступен.')}finally{setBusy(false)}
 }
 if(!eligible)return null;
 return <section className="panel stack"><h2>Связь устройств · MCP</h2><p>Запросы действуют две минуты и открывают только разрешённые разделы. На принимающем устройстве откройте безопасность аккаунта и включите приём. Никакие действия не выполняются без его подтверждения.</p>
 <button type="button" className="btn secondary" onClick={()=>setEnabled(x=>!x)}>{enabled?'Выключить приём':'Включить связь устройств'}</button>
 {enabled&&<><label>Устройство<select value={target} onChange={e=>setTarget(e.target.value)}><option value="">Выберите сессию</option>{sessions.filter(s=>!s.current&&s.id).map(s=><option key={s.id!} value={s.id!}>{deviceLabel(s.device,s.userAgent,t)}</option>)}</select></label>
 <label>Действие<select value={action} onChange={e=>setAction(e.target.value as Action)}>{Object.entries(labels).map(([id,label])=><option key={id} value={id}>{label}</option>)}</select></label>
 <button type="button" className="btn" disabled={busy||!target} onClick={()=>void send()}>Отправить запрос</button>
 {items.filter(i=>Object.hasOwn(routes,i.action)).map(i=><article className="panel" key={i.id}><strong>{labels[i.action]}</strong><p>{i.incoming?'Входящий запрос':'Исходящий запрос'} · {i.state==='pending'?'Ожидает':i.state==='approved'?'Подтверждён':'Отклонён'}</p>{i.incoming&&i.state==='pending'&&<div className="actions"><button type="button" className="btn" disabled={busy} onClick={()=>void decide(i,true)}>Подтвердить и открыть</button><button type="button" className="btn secondary" disabled={busy} onClick={()=>void decide(i,false)}>Отклонить</button></div>}</article>)}</>}
 {message&&<p role="status">{message}</p>}</section>;
}
