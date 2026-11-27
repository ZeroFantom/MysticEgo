import {useLocale} from '../app/presentation';
import {useState} from 'react';
import {useServerUpdate,type UpdateState} from '../app/serverUpdate';
import './infrastructure-workspace.css';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Panel} from '../components/primitives';
type Format='xlsx'|'json'|'txt';
type Service={unit:string;at:string;cpuPercent?:number;memoryBytes?:number;tasks?:number;loadState?:string;activeState?:string;subState?:string;type?:string;result?:string;lastCpuMilliseconds?:number;lastPeakBytes?:number;lastDurationMilliseconds?:number;lastFinishedAt?:string;measurement?:string;lastResult?:string};
type RecoveryCandidate={tag:string;digest:string;asset:string;id:number;size:number};
type RecoveryMode='reinstall'|'cycle';
type Plan={recovery?:{installed:string;options:Partial<Record<RecoveryMode,RecoveryCandidate[]>>};recoveryProgress?:{phase:string;mode:RecoveryMode;steps:string[];completed:number;message?:string};action:string;reason:string;checkedAt:string;update?:UpdateState;components:{name:string;state:string;checkedAt:string}[];services:Service[];host?:{hostCpuPercent?:number;hostMemoryUsedBytes?:number;hostMemoryTotalBytes?:number;rxBytesPerSecond:number;txBytesPerSecond:number}};
export function ReportDownload({scope='all',label='Скачать отчёт',selfTests=false}:{scope?:string;label?:string;selfTests?:boolean}){
 const t=useLocale();const {api}=useApp();const [format,setFormat]=useState<Format>('xlsx'),[busy,setBusy]=useState(false),[error,setError]=useState('');
 async function download(){setBusy(true);setError('');try{const blob=selfTests?await api.selfTestReport(format):await api.diagnosticReport(format,scope);const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='MysticEgo-'+(selfTests?'self-tests':scope)+'-'+new Date().toISOString().replace(/[:.]/g,'-')+'.'+format;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}catch(e){setError(String(e))}finally{setBusy(false)}}
 return <div className="infra-report-actions"><select aria-label={t('Формат отчёта')} value={format} onChange={e=>setFormat(e.target.value as Format)}><option value="xlsx">XLSX</option><option value="json">JSON</option><option value="txt">TXT</option></select><button className="btn secondary" disabled={busy} onClick={()=>void download()}>{t(label)}</button>{error&&<p role="alert">{error}</p>}</div>
}
const metric=(v:number|undefined|null,div=1)=>v==null?'—':(v/div).toLocaleString(undefined,{maximumFractionDigits:1});
export function ServiceMaintenance(){
 const {api}=useApp();const tracker=useServerUpdate();
 const remote=useRemote(()=>api.request<Plan>('/orchestra/update/plan'),[api],5000,true);
 const [busy,setBusy]=useState(false),[message,setMessage]=useState('');
 const p=remote.value,action=p?.action;
 const recovery=p?.recovery,progress=p?.recoveryProgress;
 const recoveryBusy=busy||!!tracker?.pending||!!remote.error||action==='busy';
 async function recover(mode:RecoveryMode){
  const releases=recovery?.options[mode];
  if(!recovery||!releases?.length||recoveryBusy)return;
  const steps=releases.map(r=>r.tag).join(' → ');
  const prompt=mode==='cycle'
   ?'Восстановление: '+steps+'. Будут перезапуски Orchestra. PostgreSQL не откатывается. Продолжайте только если проверена совместимость предыдущей версии с текущей схемой БД и имеется проверенная резервная копия. Подтверждаете?'
   :'Принудительно переустановить '+steps+' из проверенного релиза? Orchestra будет перезапущена.';
  if(!window.confirm(prompt))return;
  setBusy(true);setMessage('');
  try{await api.request('/orchestra/update/recovery-cycle',{method:'POST',body:JSON.stringify({mode,installed:recovery.installed,releases,confirmed:true,backupConfirmed:mode==='cycle'})});setMessage('Запрос восстановления принят. Проверяйте состояние всех шагов.');remote.reload()}
  catch(e){setMessage(String(e))}finally{setBusy(false)}
 }
 async function execute(){if(!p||!['update','repair'].includes(p.action)||!window.confirm(p.action==='update'?'Установить '+p.update?.candidate?.tag+'?':'Восстановить неисправные службы?'))return;
 setBusy(true);setMessage('');try{if(p.action==='update'&&p.update?.candidate&&tracker){await tracker.approve({...p.update.candidate,target:'orchestra'},p.update,true);remote.reload();return;}await api.request('/orchestra/update/plan',{method:'POST',body:JSON.stringify({action:p.action,confirmed:true,tag:p.update?.candidate?.tag,digest:p.update?.candidate?.digest})});setMessage('Запрос принят. Проверяем результат.');remote.reload()}catch(e){setMessage(String(e))}finally{setBusy(false)}}
 return <section className="infra-service-center" aria-label="Обновление и восстановление">
 <Panel title="Обновление и восстановление">
 <ErrorBanner text={remote.error||''}/>
 <p role="status">{p?.reason??'Ожидаем данные сервера'}</p><p>Версия: {p?.update?.current??'—'} · {p?.checkedAt?new Date(p.checkedAt).toLocaleTimeString():'—'}</p>
 <button className="btn" disabled={busy||!!tracker?.pending||!!remote.error||!['update','repair'].includes(action??'')} onClick={()=>void execute()}>Обновить / восстановить</button>
 <div className="infra-report-actions">
  <button className="btn secondary" disabled={recoveryBusy||!recovery?.options.reinstall?.length} onClick={()=>void recover('reinstall')}>Принудительно переустановить</button>
  <button className="btn secondary" disabled={recoveryBusy||!recovery?.options.cycle?.length} onClick={()=>void recover('cycle')}>Откат → два шага вперёд</button>
 </div>
 {recovery?.options.cycle&&<p>План восстановления: {recovery.options.cycle.map(r=>r.tag).join(' → ')}. Если новый релиз отсутствует, последний шаг повторно устанавливает текущую версию. Данные БД не откатываются.</p>}
 {progress&&<div role="status"><strong>Последний цикл: {({running:'выполняется',success:'завершён',failed:'ошибка',interrupted:'прерван'} as Record<string,string>)[progress.phase]??progress.phase} · {progress.completed}/{progress.steps.length}</strong><p>{progress.steps.join(' → ')}</p>{progress.message&&<p>{progress.message}</p>}</div>}
 <p className="infra-auto-policy">Автоматическое обновление включено · штатная политика Orchestra</p>
 {message&&<p role="status">{message}</p>}
 <ReportDownload label="Сводный отчёт"/>
 </Panel>
 <Panel title="Нагрузка хоста">
 <p>CPU хоста {metric(p?.host?.hostCpuPercent)}% · RAM {metric(p?.host?.hostMemoryUsedBytes,1048576)} / {metric(p?.host?.hostMemoryTotalBytes,1048576)} MiB · RX {metric(p?.host?.rxBytesPerSecond,1048576)} / TX {metric(p?.host?.txBytesPerSecond,1048576)} MiB/s</p>
 </Panel>
 <h2>Компоненты и зависимости</h2>
 <div className="infra-status-grid">{p?.components.map(c=><article className="infra-status-card" key={c.name}><h3>{c.name}</h3><p>{c.state} · {new Date(c.checkedAt).toLocaleTimeString()}</p><ReportDownload scope={c.name}/></article>)}</div>
 <h2>Системные службы</h2>
 <div className="infra-status-grid">{p?.services.map(s=><article className="infra-status-card" key={s.unit}><h3 style={{overflowWrap:'anywhere'}}>{s.unit}</h3><p>{s.loadState==='loaded'&&s.measurement==='idle'&&s.type==='oneshot'&&s.result==='success'?'Ожидает следующего запуска':s.activeState??'Нет данных'} · {s.subState??'—'}</p>
 {s.measurement==='not-applicable'?<p>Таймер / сокет / путь · нагрузка не применяется</p>:<div className="infra-resource-values"><span>CPU <b>{s.cpuPercent==null?'Нет замера':metric(s.cpuPercent)+'%'}</b></span><span>RAM <b>{s.memoryBytes==null?'Нет замера':metric(s.memoryBytes,1048576)+' MiB'}</b></span><span>Задачи <b>{metric(s.tasks)}</b></span></div>}
 {s.type==='oneshot'&&<div className="infra-last-run"><strong>Последнее выполнение</strong><p>Результат: {s.lastResult??s.result??'Нет данных'}</p><p>Длительность: {s.lastDurationMilliseconds==null?'Нет замера':metric(s.lastDurationMilliseconds,1000)+' с'}</p><p>CPU-время: {s.lastCpuMilliseconds==null?'Нет замера':metric(s.lastCpuMilliseconds,1000)+' с'} · Пик RAM: {s.lastPeakBytes==null?'Нет замера':metric(s.lastPeakBytes,1048576)+' MiB'}</p>{s.lastFinishedAt&&<small>{new Date(s.lastFinishedAt).toLocaleString()}</small>}</div>}<small>{new Date(s.at).toLocaleTimeString()}</small><ReportDownload scope={s.unit}/></article>)}</div>
 </section>
}
