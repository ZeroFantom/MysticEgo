import {useEffect,useRef,useState} from 'react';
import {useApp,useRemote} from './context';
import {ANDROID_ORIGIN} from './androidLaunch';
import {Panel,ErrorBanner} from '../components/primitives';

type Device={id:string;name:string;os:string;createdUtc:string;lastSeenUtc?:string;revokedUtc?:string;ready:boolean};
type Item={id:string;target:string;action:string;state:string;result?:string;expires:string};
const actions={'battery-status':'Уровень батареи','vibrate':'Короткая вибрация','open-assistant':'Открыть ассистента'} as const;
const states:Record<string,string>={pending:'Ожидает подтверждения',accepted:'Принят, результат ещё не получен',declined:'Отклонён','reported-success':'Устройство сообщило об успехе','reported-failure':'Устройство сообщило об ошибке'};
export function MobileAgentPanel(){
 const {session,preferenceScope}=useApp();
 const eligible=location.origin===ANDROID_ORIGIN&&session.authUrl.replace(/\/$/,'')===location.origin;
 const [ticket,setTicket]=useState(''),[target,setTarget]=useState(''),[action,setAction]=useState<keyof typeof actions>('battery-status'),[busy,setBusy]=useState(false),[error,setError]=useState(''),[revoke,setRevoke]=useState('');
 const generation=useRef(0);
 async function call(path:string,body?:unknown){
  const r=await fetch('/api/auth/mobile-agent/'+path,{method:body===undefined?'GET':'POST',headers:{Authorization:'Bearer '+session.token,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),credentials:'omit',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(12000)});
  if(!r.ok)throw Error('Mobile Agent: HTTP '+r.status);return r.status===204?null:r.json();
 }
 async function mcp(name:string,args:Record<string,unknown>={}){
  const r=await call('mcp',{jsonrpc:'2.0',id:crypto.randomUUID(),method:'tools/call',params:{name,arguments:args}});
  if(r?.error||typeof r?.result?.content?.[0]?.text!=='string')throw Error('Запрос MCP отклонён.');
  return JSON.parse(r.result.content[0].text);
 }
 const devices=useRemote<Device[]>(()=>eligible?call('devices'):Promise.resolve([]),[eligible,preferenceScope,session.token],15000);
 const jobs=useRemote<Item[]>(()=>eligible?mcp('mobile.status'):Promise.resolve([]),[eligible,preferenceScope,session.token],10000);
 useEffect(()=>{generation.current++;setTicket('');setTarget('');setRevoke('');setError('');setBusy(false);return()=>{generation.current++}},[preferenceScope,session.token]);
 useEffect(()=>{if(!ticket)return;const timer=window.setTimeout(()=>setTicket(''),120000);return()=>window.clearTimeout(timer)},[ticket]);
 async function run(work:()=>Promise<void>){const current=generation.current;setBusy(true);setError('');try{await work();if(current===generation.current){devices.reload();jobs.reload()}}catch(e){if(current===generation.current)setError(String(e))}finally{if(current===generation.current)setBusy(false)}}
 if(!eligible)return null;
 return <Panel title="Зарегистрированные телефоны · Mobile Agent">
  <p>Отдельная регистрация Android, не равная веб-сессии. Приём работает только на открытом экране MCP в приложении. Последний сигнал не гарантирует, что телефон всё ещё доступен.</p>
  <ErrorBanner text={error||devices.error||jobs.error}/>
  <button className="btn secondary" disabled={busy} onClick={()=>void run(async()=>{const current=generation.current;const r=await call('ticket',{});if(current===generation.current&&typeof r?.code==='string')setTicket(r.code)})}>Создать код регистрации</button>
  {ticket&&<div className="panel stack"><p>Одноразовый код действует две минуты. Вставьте его в нативный экран MCP приложения. Не передавайте код другим людям.</p><input readOnly value={ticket} onFocus={e=>e.target.select()}/><button className="btn secondary" onClick={()=>setTicket('')}>Скрыть код</button></div>}
  {devices.value?.map(d=><article className="panel" key={d.id}><strong>{d.name} · {d.os}</strong><p>{d.revokedUtc?'Отозван':d.ready?'Недавний сигнал':'Нет недавнего сигнала'}</p><small>{d.lastSeenUtc?new Date(d.lastSeenUtc).toLocaleString():'Сигналов пока нет'}</small>{!d.revokedUtc&&<div><button className="btn secondary" disabled={busy} onClick={()=>setRevoke(d.id)}>Отозвать ключ</button></div>}</article>)}
  {revoke&&<div className="panel"><p>Отозвать независимый ключ телефона? Обычная веб-сессия не завершится автоматически.</p><button className="btn" disabled={busy} onClick={()=>void run(async()=>{await call('revoke',{id:revoke});setRevoke('')})}>Подтвердить отзыв</button><button className="btn secondary" onClick={()=>setRevoke('')}>Отмена</button></div>}
  <div className="stack"><label>Телефон<select value={target} onChange={e=>setTarget(e.target.value)}><option value="">Выберите устройство</option>{devices.value?.filter(d=>!d.revokedUtc).map(d=><option key={d.id} value={d.id} disabled={!d.ready}>{d.name} · {d.os}</option>)}</select></label>
  <label>Действие<select value={action} onChange={e=>setAction(e.target.value as keyof typeof actions)}>{Object.entries(actions).map(([id,title])=><option value={id} key={id}>{title}</option>)}</select></label>
  <button className="btn" disabled={busy||!devices.value?.some(d=>d.id===target&&d.ready&&!d.revokedUtc)} onClick={()=>void run(async()=>{await mcp('mobile.request',{targetId:target,action})})}>Отправить запрос на телефон</button></div>
  {jobs.value?.map(j=><article className="panel" key={j.id}><strong>{actions[j.action as keyof typeof actions]??'Запрос'}</strong><p>{states[j.state]??j.state}{j.action==='battery-status'&&j.result?' · '+j.result+'%':''}</p></article>)}
  <small>Результаты доступны до истечения двухминутного запроса. Это сообщения самого клиента, а не независимое доказательство исполнения.</small>
 </Panel>;
}
