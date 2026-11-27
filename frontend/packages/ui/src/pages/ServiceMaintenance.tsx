import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Panel} from '../components/primitives';
type Format='xlsx'|'json'|'txt';
type Service={unit:string;at:string;cpuPercent?:number;memoryBytes?:number;tasks?:number;loadState?:string;activeState?:string;subState?:string};
type Plan={action:string;reason:string;checkedAt:string;update?:{current?:string;candidate?:{tag:string;digest:string}};components:{name:string;state:string;checkedAt:string}[];services:Service[];host?:{hostCpuPercent?:number;hostMemoryUsedBytes?:number;hostMemoryTotalBytes?:number;rxBytesPerSecond:number;txBytesPerSecond:number}};
export function ReportDownload({scope='all',label='Скачать отчёт',selfTests=false}:{scope?:string;label?:string;selfTests?:boolean}){
 const {api}=useApp();const [format,setFormat]=useState<Format>('xlsx'),[busy,setBusy]=useState(false),[error,setError]=useState('');
 async function download(){setBusy(true);setError('');try{const blob=selfTests?await api.selfTestReport(format):await api.diagnosticReport(format,scope);const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='MysticEgo-'+(selfTests?'self-tests':scope)+'-'+new Date().toISOString().replace(/[:.]/g,'-')+'.'+format;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}catch(e){setError(String(e))}finally{setBusy(false)}}
 return <div><select aria-label="Формат отчёта" value={format} onChange={e=>setFormat(e.target.value as Format)}><option value="xlsx">XLSX</option><option value="json">JSON</option><option value="txt">TXT</option></select><button className="btn secondary" disabled={busy} onClick={()=>void download()}>{label}</button>{error&&<p role="alert">{error}</p>}</div>
}
const metric=(v:number|undefined|null,div=1)=>v==null?'—':(v/div).toLocaleString(undefined,{maximumFractionDigits:1});
export function ServiceMaintenance(){
 const {api}=useApp();
 const remote=useRemote(()=>api.request<Plan>('/orchestra/update/plan'),[api],5000,true);
 const auto=useRemote(()=>api.request<{enabled:boolean;available:boolean}>('/orchestra/update/automatic'),[api],5000,true);
 const [busy,setBusy]=useState(false),[message,setMessage]=useState('');
 const p=remote.value,action=p?.action;
 async function execute(){if(!p||!['update','repair'].includes(p.action)||!window.confirm(p.action==='update'?'Установить '+p.update?.candidate?.tag+'?':'Восстановить неисправные службы?'))return;
 setBusy(true);setMessage('');try{await api.request('/orchestra/update/plan',{method:'POST',body:JSON.stringify({action:p.action,confirmed:true,tag:p.update?.candidate?.tag,digest:p.update?.candidate?.digest})});setMessage('Запрос принят. Проверяем результат.');remote.reload()}catch(e){setMessage(String(e))}finally{setBusy(false)}}
 async function automatic(){if(!auto.value||!window.confirm(auto.value.enabled?'Отключить автоматическое обновление?':'Разрешить автоматическую установку новых проверенных релизов Orchestra, включая beta, с возможным перезапуском?'))return;
 setBusy(true);try{await api.request('/orchestra/update/automatic',{method:'POST',body:JSON.stringify({enabled:!auto.value.enabled,confirmed:true})});auto.reload()}catch(e){setMessage(String(e))}finally{setBusy(false)}}
 return <Panel title="Обновление и восстановление">
 <ErrorBanner text={remote.error||auto.error||''}/>
 <p role="status">{p?.reason??'Ожидаем данные сервера'}</p><p>Версия: {p?.update?.current??'—'} · {p?.checkedAt?new Date(p.checkedAt).toLocaleTimeString():'—'}</p>
 <button className="btn" disabled={busy||!!remote.error||!['update','repair'].includes(action??'')} onClick={()=>void execute()}>Обновить / восстановить</button>
 <label><input type="checkbox" checked={auto.value?.enabled===true} disabled={busy||!auto.value?.available||!!auto.error} onChange={()=>void automatic()}/> Автоматическое обновление Orchestra</label>
 {message&&<p role="status">{message}</p>}
 <ReportDownload label="Сводный отчёт"/>
 <p>CPU хоста {metric(p?.host?.hostCpuPercent)}% · RAM {metric(p?.host?.hostMemoryUsedBytes,1048576)} / {metric(p?.host?.hostMemoryTotalBytes,1048576)} MiB · RX {metric(p?.host?.rxBytesPerSecond,1048576)} / TX {metric(p?.host?.txBytesPerSecond,1048576)} MiB/s</p>
 <div className="infra-status-grid">{p?.components.map(c=><article className="infra-status-card" key={c.name}><h3>{c.name}</h3><p>{c.state} · {new Date(c.checkedAt).toLocaleTimeString()}</p><ReportDownload scope={c.name}/></article>)}</div>
 <div className="infra-status-grid">{p?.services.map(s=><article className="infra-status-card" key={s.unit}><h3 style={{overflowWrap:'anywhere'}}>{s.unit}</h3><p>{s.loadState??'unknown'} · {s.activeState??'unknown'} / {s.subState??'—'}</p><p>CPU {metric(s.cpuPercent)}% · RAM {metric(s.memoryBytes,1048576)} MiB · Tasks {metric(s.tasks)}</p><small>{new Date(s.at).toLocaleTimeString()}</small><ReportDownload scope={s.unit}/></article>)}</div>
 </Panel>
}
