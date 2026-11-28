import {useEffect,useRef,useState} from 'react';
import {useNavigate} from 'react-router-dom';
import {useApp} from './context';
import {ANDROID_ORIGIN} from './androidLaunch';
import {deviceLabel,type SessionDevice} from './QrSignIn';
import {useLocale} from './presentation';
import {browserKind} from './browserPolicy';
import {advertisedActions,actionLabels,isAction,navigationActions,nativeActionLink,type DeviceAction} from './deviceActionPolicy';
import './device-actions.css';

type SessionRow={id:string|null;current?:boolean;ready?:boolean;device?:SessionDevice;userAgent?:string;actions?:string[]};
type Item={id:string;source:string;target:string;action:DeviceAction;expires:string;state:string;incoming:boolean};
export function DeviceActions(){
 const {session,preferenceScope}=useApp(),navigate=useNavigate(),t=useLocale();
 const [enabled,setEnabled]=useState(false),[sessions,setSessions]=useState<SessionRow[]>([]),[items,setItems]=useState<Item[]>([]);
 const [target,setTarget]=useState(''),[action,setAction]=useState<DeviceAction>('open-assistant'),[busy,setBusy]=useState(false),[message,setMessage]=useState('');
 const [local,setLocal]=useState<{action:DeviceAction;expires:number}|null>(null);
 const epoch=useRef(0),audio=useRef<AudioContext|null>(null);
 const android=browserKind()==='android',eligible=location.origin===ANDROID_ORIGIN&&session.authUrl.replace(/\/$/,'')===location.origin;
 const advertised=advertisedActions(android);
 const targetRow=sessions.find(s=>s.id===target&&s.ready),choices=(targetRow?.actions??[]).filter(isAction);
 async function call(path:string,body?:unknown,signal?:AbortSignal){
  const response=await fetch('/api/auth/'+path,{method:body===undefined?'GET':'POST',headers:{Authorization:'Bearer '+session.token,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),cache:'no-store',credentials:'omit',redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(10000)]):AbortSignal.timeout(10000)});
  if(!response.ok)throw Error('Сервис запросов недоступен.');return response.json();
 }
 async function mcp(name:string,args:Record<string,unknown>,signal?:AbortSignal){
  const response=await call('device-actions/mcp',{jsonrpc:'2.0',id:crypto.randomUUID(),method:'tools/call',params:{name,arguments:args}},signal);
  if(response.error||response.result?.isError||typeof response.result?.content?.[0]?.text!=='string')throw Error('MCP request failed');
  return JSON.parse(response.result.content[0].text);
 }
 function stop(){epoch.current++;setEnabled(false);setLocal(null);setItems([]);setSessions([]);setTarget('');setBusy(false);void audio.current?.close().catch(()=>{});audio.current=null}
 useEffect(()=>{stop()},[preferenceScope,session.token,eligible]);
 useEffect(()=>{
  if(!eligible||!enabled)return;const controller=new AbortController();let timer:number|undefined;
  async function poll(){try{if(document.hidden)return;await call('device-actions/presence',{enabled:true,actions:advertisedActions(android)},controller.signal);const [s,a]=await Promise.all([mcp('device.list',{},controller.signal),call('device-actions',undefined,controller.signal)]);if(!controller.signal.aborted){setSessions(s);setItems(a)}}catch{if(!controller.signal.aborted)setMessage('Не удалось обновить список запросов.')}finally{if(!controller.signal.aborted)timer=window.setTimeout(poll,12000)}}
  const hide=()=>{if(document.hidden)stop()};document.addEventListener('visibilitychange',hide);
  void poll();return()=>{controller.abort();window.clearTimeout(timer);document.removeEventListener('visibilitychange',hide);epoch.current++;void call('device-actions/presence',{enabled:false}).catch(()=>{})};
 },[enabled,eligible,session.token,android]);
 useEffect(()=>{if(!local)return;const timer=window.setTimeout(()=>setLocal(null),Math.max(0,local.expires-Date.now()));return()=>window.clearTimeout(timer)},[local]);
 useEffect(()=>{if(choices.length&&!choices.includes(action))setAction(choices[0])},[target,action,sessions]);
 async function send(){
  if(!enabled||document.hidden||!targetRow||!choices.includes(action))return;
  const generation=epoch.current;setBusy(true);setMessage('');try{await mcp('device.request',{targetId:target,action});if(generation!==epoch.current)return;setMessage('Запрос отправлен. Подтвердите на другом устройстве.');const next=await call('device-actions');if(generation===epoch.current)setItems(next)}catch{if(generation===epoch.current)setMessage('Не удалось отправить запрос.')}finally{if(generation===epoch.current)setBusy(false)}
 }
 async function decide(item:Item,approve:boolean){
  if(!enabled||document.hidden||!advertised.includes(item.action))return;
  const generation=epoch.current;setBusy(true);try{const r=await call('device-actions/decide',{id:item.id,approve});if(generation!==epoch.current||document.hidden)return;setItems(old=>old.filter(x=>x.id!==item.id));if(approve&&r.state==='approved'&&r.action===item.action&&isAction(r.action)){
   if(Object.hasOwn(navigationActions,item.action))navigate(navigationActions[item.action as keyof typeof navigationActions]);
   else setLocal({action:item.action,expires:Math.min(Date.parse(item.expires),Date.now()+30000)});
  }else setMessage('Запрос отклонён.')}catch{if(generation===epoch.current)setMessage('Запрос истёк или недоступен.')}finally{if(generation===epoch.current)setBusy(false)}
 }
 async function cancel(id:string){const generation=epoch.current;setBusy(true);try{await call('device-actions/cancel',{id});if(generation===epoch.current){setItems(await call('device-actions'));setMessage('Запрос отменён.')}}catch{if(generation===epoch.current)setMessage('Запрос уже обработан или истёк.')}finally{if(generation===epoch.current)setBusy(false)}}
 function signal(){
  if(!local||local.action!=='signal-device'||local.expires<=Date.now()||document.hidden||!enabled)return;
  setLocal(null);setMessage('Локальный сигнал подтверждён.');
  try{const ctx=new AudioContext();audio.current=ctx;const oscillator=ctx.createOscillator(),gain=ctx.createGain();gain.gain.value=.05;oscillator.frequency.value=520;oscillator.connect(gain).connect(ctx.destination);oscillator.onended=()=>{if(audio.current===ctx)audio.current=null;void ctx.close().catch(()=>{})};oscillator.start();oscillator.stop(ctx.currentTime+.3)}catch{/* Visible local confirmation remains; audio may be blocked. */}
 }
 if(!eligible)return null;
 const link=local?nativeActionLink(local.action,android):null;
 const pending=items.filter(i=>i.incoming&&i.state==='pending').length;
 return <details className="device-actions-dock"><summary>{t('Связь устройств · MCP')}{enabled?' · '+t('Приём включён'):''}{pending?' · '+pending+' '+t('входящих'):''}</summary><section className="panel stack">
 <p>Приём действует на всех страницах, пока этот клиент открыт и видим. Запросы живут две минуты; переходы и нативные действия требуют подтверждения на принимающем устройстве.</p>
 <button type="button" className="btn secondary" onClick={()=>enabled?stop():setEnabled(true)}>{enabled?'Выключить приём':'Включить связь устройств'}</button>
 {enabled&&<><label>Устройство<select value={target} onChange={e=>setTarget(e.target.value)}><option value="">Выберите сессию</option>{sessions.filter(s=>!s.current&&s.id).map(s=><option key={s.id!} value={s.id!} disabled={!s.ready}>{deviceLabel(s.device,s.userAgent,t)} · {s.ready?'Принимает запросы':'Не принимает'}</option>)}</select></label>
 <label>Действие<select value={choices.includes(action)?action:''} onChange={e=>{if(isAction(e.target.value))setAction(e.target.value)}}><option value="">Выберите действие</option>{choices.map(id=><option key={id} value={id}>{t(actionLabels[id])}</option>)}</select></label>
 <button type="button" className="btn" disabled={busy||!targetRow||!choices.includes(action)} onClick={()=>void send()}>Отправить запрос</button>
 {items.filter(i=>isAction(i.action)).map(i=><article className="panel" key={i.id}><strong>{t(actionLabels[i.action])}</strong><p>{i.incoming?'Входящий запрос':'Исходящий запрос'} · {i.state==='pending'?'Ожидает':i.state==='approved'?'Подтверждён (не проверено выполнение)':i.state==='cancelled'?'Отменён':'Отклонён'}</p>{!i.incoming&&i.state==='pending'&&<button type="button" className="btn secondary" disabled={busy} onClick={()=>void cancel(i.id)}>Отменить запрос</button>}{i.incoming&&i.state==='pending'&&<div className="actions"><button type="button" className="btn" disabled={busy||!advertised.includes(i.action)} onClick={()=>void decide(i,true)}>Подтвердить запрос</button><button type="button" className="btn secondary" disabled={busy} onClick={()=>void decide(i,false)}>Отклонить</button></div>}</article>)}
 {local&&<div className="panel stack"><strong>{t(actionLabels[local.action])}</strong><p>Выполните действие здесь вручную. Это дополнительное локальное подтверждение.</p>{link?<a className="btn" href={link} onClick={e=>{if(document.hidden||local.expires<=Date.now()||!enabled){e.preventDefault();setLocal(null);return}window.setTimeout(()=>setLocal(null),0)}}>Открыть нативное действие</a>:local.action==='signal-device'?<button className="btn" type="button" onClick={signal}>Подать короткий сигнал</button>:<p>Действие недоступно в этом клиенте.</p>}<button className="btn secondary" type="button" onClick={()=>setLocal(null)}>Отмена</button></div>}</>}
 {message&&<p role="status">{message}</p>}</section></details>;
}
