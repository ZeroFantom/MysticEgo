import {useEffect,useRef,useState} from 'react';
import {useApp} from '../app/context';
import {ANDROID_ORIGIN} from '../app/androidLaunch';
import {deviceLabel,type SessionDevice} from '../app/QrSignIn';
import {useLocale} from '../app/presentation';
import {validSnapshot,type Snapshot} from './avatarTransferState';
type Target={id:string;device?:SessionDevice;ready:boolean};
type Offer={id:string;state:string;expires:string};
export function AvatarTransfer({snapshot}:{snapshot:()=>Snapshot}){
 const {session,preferenceScope}=useApp(),t=useLocale();
 const eligible=location.origin===ANDROID_ORIGIN&&session.authUrl.replace(/\/$/,'')===location.origin;
 const [targets,setTargets]=useState<Target[]|null>(null),[target,setTarget]=useState(''),[offer,setOffer]=useState<Offer|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const epoch=useRef(0);
 useEffect(()=>{epoch.current++;setTargets(null);setTarget('');setOffer(null);setBusy(false);setError('');return()=>{epoch.current++}},[session.token,preferenceScope]);
 async function mcp(name:string,args:Record<string,unknown>){
  const r=await fetch('/api/auth/device-actions/mcp',{method:'POST',headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',id:crypto.randomUUID(),method:'tools/call',params:{name,arguments:args}}),credentials:'omit',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
  if(!r.ok)throw Error();const data=await r.json();if(data.error||typeof data.result?.content?.[0]?.text!=='string')throw Error();return JSON.parse(data.result.content[0].text);
 }
 async function run(work:(id:number)=>Promise<void>){const id=epoch.current;setBusy(true);setError('');try{await work(id)}catch{if(id===epoch.current)setError('Запрос недоступен или истёк.')}finally{if(id===epoch.current)setBusy(false)}}
 if(!eligible)return null;
 return <section className="panel stack"><h3>Перенести аватар</h3><p>На другом устройстве откройте безопасность аккаунта и включите связь устройств. Передаются только движение, эмоция и фаза анимации. История доступна отдельно в том же серверном аккаунте.</p>
 <button className="btn secondary" disabled={busy} onClick={()=>void run(async id=>{const rows=await mcp('device.list',{});if(id===epoch.current){setTargets(rows);setTarget('')}})}>Найти принимающие устройства</button>
 {targets&&<><label>Устройство<select value={target} onChange={e=>setTarget(e.target.value)}><option value="">Выберите устройство</option>{targets.map(row=><option key={row.id} value={row.id} disabled={!row.ready}>{deviceLabel(row.device,undefined,t)} · {row.ready?'Принимает':'Недоступно'}</option>)}</select></label>
 <button className="btn" disabled={busy||!targets.some(row=>row.id===target&&row.ready)} onClick={()=>void run(async id=>{const value=snapshot();if(!validSnapshot(value))throw Error();const result=await mcp('avatar.handoff',{targetId:target,...value});if(id===epoch.current)setOffer(result)})}>Предложить перенос VRM</button></>}
 {offer&&<div className="stack"><p role="status">{offer.state==='approved'?'Перенос принят (рендеринг не подтверждён).':offer.state==='pending'?'Ожидает подтверждения до двух минут.':offer.state}</p><button className="btn secondary" disabled={busy} onClick={()=>void run(async id=>{const result=await mcp('device.status',{requestId:offer.id});if(id===epoch.current)setOffer(result)})}>Обновить статус</button>{offer.state==='pending'&&<button className="btn secondary" disabled={busy} onClick={()=>void run(async id=>{const result=await mcp('device.cancel',{requestId:offer.id});if(id===epoch.current)setOffer({...offer,...result})})}>Отменить перенос</button>}</div>}
 {error&&<p role="alert">{error}</p>}</section>;
}
