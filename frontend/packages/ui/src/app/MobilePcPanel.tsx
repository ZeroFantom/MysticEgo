import {useEffect,useRef,useState} from 'react';
import {useApp} from './context';
import {ANDROID_ORIGIN} from './androidLaunch';
import {Panel,ErrorBanner} from '../components/primitives';

const actions={'system.health':'Состояние агента ПК','integrations.status':'Состояние интеграций ПК'} as const;
const metrics:Record<string,string>={uptimeMs:'Время работы, мс',managedHeapBytes:'Память .NET, байт',agentWorkingSetBytes:'Память агента, байт',agentThreads:'Потоки агента',openRgb:'OpenRGB',logitechGHub:'Logitech G HUB',msiCenter:'MSI Center',nvidia:'NVIDIA',lmStudioApi:'LM Studio API'};
type Proposal={targetId:string;id:string;alias:string;state:string;expires:string};
type Station={id:string;name:string;online:boolean;lastSeenUtc:string};
type Result={targetId:string;action:string;executionId:string;data:Record<string,number|boolean>};
export function MobilePcPanel(){
 const {session,preferenceScope}=useApp();
 const eligible=location.origin===ANDROID_ORIGIN&&session.url.replace(/\/$/,'')===location.origin;
 const [rows,setRows]=useState<Station[]|null>(null),[target,setTarget]=useState(''),[action,setAction]=useState<keyof typeof actions>('system.health'),[result,setResult]=useState<Result|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [alias,setAlias]=useState(''),[proposal,setProposal]=useState<Proposal|null>(null);
 const epoch=useRef(0);
 useEffect(()=>{epoch.current++;setRows(null);setTarget('');setResult(null);setProposal(null);setAlias('');setError('');setBusy(false);return()=>{epoch.current++}},[session.token,preferenceScope]);
 async function mcp(name:string,args:Record<string,unknown>){
  const response=await fetch('/api/v1/mobile-pc/mcp',{method:'POST',headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',id:crypto.randomUUID(),method:'tools/call',params:{name,arguments:args}}),credentials:'omit',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(25000)});
  if(response.status===403)throw Error('Доступ к диагностике не настроен для этого аккаунта.');
  if(!response.ok)throw Error('Диагностика недоступна.');
  const body=await response.json();if(body.error||typeof body.result?.content?.[0]?.text!=='string')throw Error('Компьютер или команда недоступны.');return JSON.parse(body.result.content[0].text);
 }
 async function run(work:(generation:number)=>Promise<void>){const id=epoch.current;setBusy(true);setError('');setResult(null);try{await work(id)}catch(e){if(id===epoch.current)setError(String(e))}finally{if(id===epoch.current)setBusy(false)}}
 if(!eligible)return null;
 return <Panel title="Компьютеры · диагностика MCP"><p>Диагностика и предложения запуска для явно разрешённых ПК. Запуск возможен только после отдельного подтверждения на самом компьютере.</p>
  <ErrorBanner text={error}/>
  <button className="btn secondary" disabled={busy} onClick={()=>void run(async id=>{const list=await mcp('pc.list',{});if(id===epoch.current){setRows(list);setTarget('')}})}>Получить список разрешённых ПК</button>
  {rows&&<div className="stack">
   {!rows.length&&<p>Нет доступных компьютеров в списке.</p>}
   <label>Компьютер<select value={target} onChange={e=>{setTarget(e.target.value);setResult(null)}}><option value="">Выберите ПК</option>{rows.map(r=><option key={r.id} value={r.id} disabled={!r.online}>{r.name||r.id} · {r.online?'Онлайн':'Офлайн'}</option>)}</select></label>
   <label>Диагностика<select value={action} onChange={e=>{setAction(e.target.value as keyof typeof actions);setResult(null)}}>{Object.entries(actions).map(([k,v])=><option key={k} value={k}>{v}</option>)}</select></label>
   <button className="btn" disabled={busy||!rows.some(r=>r.id===target&&r.online)} onClick={()=>void run(async id=>{const value=await mcp('pc.diagnose',{targetId:target,action});if(id===epoch.current)setResult(value)})}>Запросить диагностику</button>
   <label>Псевдоним программы из локального списка ПК<input value={alias} maxLength={40} onChange={e=>setAlias(e.target.value)} placeholder="например, editor"/></label>
   <button className="btn secondary" disabled={busy||!rows.some(r=>r.id===target&&r.online)||! /^[A-Za-z0-9_.-]{1,40}$/.test(alias)} onClick={()=>void run(async id=>{const value=await mcp('pc.launch.propose',{targetId:target,alias});if(id===epoch.current)setProposal(value)})}>Предложить запуск на ПК</button>
  </div>}
  {proposal&&<div className="panel stack"><strong>{proposal.alias}</strong><p>Статус предложения: {proposal.state==='launch-requested'?'ПК сообщил о запросе запуска':proposal.state==='pending'?'Ожидает локального подтверждения':proposal.state}</p><button className="btn secondary" disabled={busy} onClick={()=>void run(async id=>{const value=await mcp('pc.launch.status',{targetId:proposal.targetId,requestId:proposal.id});if(id===epoch.current)setProposal(value)})}>Обновить статус запуска</button></div>}
  {result&&<div className="panel"><strong>{actions[result.action as keyof typeof actions]}</strong>{Object.entries(result.data).filter(([k])=>Object.hasOwn(metrics,k)).map(([k,v])=><p key={k}>{metrics[k]}: {typeof v==='boolean'?(v?'Да':'Нет'):v.toLocaleString()}</p>)}<small>ID выполнения: {result.executionId}</small></div>}
 </Panel>;
}
