import {Stations} from './Stations';
import {useState} from 'react';
import {HardDrive,Server,ShieldCheck} from 'lucide-react';
import {useApp} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

/** Docker-free plans are saved in the CLOSED Orchestra control plane. No SSH passwords in the browser. */
export function NativeDeployment(){
 const {api,mode}=useApp();
 const [planId,setPlanId]=useState('pilot');
 const [json,setJson]=useState(JSON.stringify({nodes:[{host:'local',roles:['auth','orchestra']} ]},null,2));
 const [message,setMessage]=useState('');const [error,setError]=useState('');const [busy,setBusy]=useState(false);
 const [jobId,setJobId]=useState('');const [jobStatus,setJobStatus]=useState('');
 const base='/orchestra/deployments/native/plans/';
 const valid=/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(planId);
 const save=async()=>{
  setBusy(true);setMessage('');setError('');
  try{
   const data=JSON.parse(json);
   if(!Array.isArray(data.nodes)||data.nodes.length<1)throw Error('Укажите хотя бы один узел.');
   const result=await api.request<{nodes:number;mode:string}>(base+planId,{method:'PUT',body:JSON.stringify(data)});
   setMessage('План сохранён в Orchestra. Узлов: '+result.nodes+'. Запуск производится единым доверенным установщиком, не веб-сервером.');
  }catch(e){setError(String(e))}finally{setBusy(false)}
 };
 const load=async()=>{setBusy(true);setError('');try{const data=await api.request<unknown>(base+planId);setJson(JSON.stringify(data,null,2));setMessage('План загружен.')}catch(e){setError(String(e))}finally{setBusy(false)}};
 const apply=async()=>{
  if(!window.confirm('Запустить установку на доверенных серверах через Orchestra? Изменения могут затронуть службы ОС.'))return;
  setBusy(true);setError('');
  try{const job=await api.request<{id:string;status:string}>(base+planId+'/apply',{method:'POST'});setJobId(job.id);setJobStatus(job.status);setMessage('Операция поставлена в очередь Orchestra. Задание: '+job.id)}
  catch(e){setError(String(e))}finally{setBusy(false)}
 };
 const refreshJob=async()=>{if(!jobId)return;setBusy(true);setError('');try{const job=await api.request<{status:string;message?:string}>('/orchestra/deployments/native/jobs/'+jobId);setJobStatus(job.status);if(job.message)setMessage(job.message)}catch(e){setError(String(e))}finally{setBusy(false)}};
 return <><Header title="Развёртывание без Docker" description="Нативные .NET-сервисы Windows/Linux · централизованные планы Orchestra"/>
  {mode==='orchestra'&&<Stations/>}<details><summary>Расширенные планы SSH для системных служб</summary><div className="metrics"><div className="metric"><Server/><div className="metric-value">Native</div><div className="metric-label">Режим установки</div></div><div className="metric"><HardDrive/><div className="metric-value">systemd / SCM</div><div className="metric-label">Службы ОС</div></div><div className="metric"><ShieldCheck/><div className="metric-value">Orchestra</div><div className="metric-label">Закрытое управление</div></div></div>
  <ErrorBanner text={error}/>{message&&<Panel title="Состояние"><p>{message}</p></Panel>}
  {mode!=='orchestra'?<Panel title="Недоступно"><p>Управление развёртыванием доступно только через Orchestra.</p></Panel>:<>
  <Panel title="План нативного развёртывания"><div className="stack">
   <label>Идентификатор плана<input value={planId} onChange={e=>setPlanId(e.target.value)} placeholder="pilot"/></label>
   <label>Узлы и зависимости в JSON<textarea value={json} rows={14} spellCheck={false} onChange={e=>setJson(e.target.value)} style={{fontFamily:'monospace'}}/></label>
   <div style={{display:'flex',gap:10,flexWrap:'wrap'}}><button className="btn" disabled={busy||!valid} onClick={save}>Сохранить план</button><button className="btn secondary" disabled={busy||!valid} onClick={load}>Загрузить план</button><button className="btn secondary" disabled={busy||!valid} onClick={apply}>Запустить с Orchestra</button></div>
   <p className="footnote">Пароли, паспортные сведения, приватные ключи и VPN-подписки не должны присутствовать в JSON.</p>
  </div></Panel>
  <Panel title="Применение плана"><p>Прямой запуск из Orchestra доступен только после включения защищённого серверного исполнителя в конфигурации и задания точного списка разрешённых SSH-хостов. По умолчанию он выключен.</p>
   {jobId&&<div className="stack"><p><strong>Задание:</strong> {jobId} · <strong>Статус:</strong> {jobStatus}</p><button className="btn secondary" disabled={busy} onClick={refreshJob}>Обновить статус</button></div>}
   <p>Альтернативно на доверенном административном ПК запусти тот же единый опубликованный MysticEgo.Installer с параметрами:</p>
   <pre style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>MysticEgo.Installer --mode native --plan-id {valid?planId:'pilot'} --orchestra-url https://YOUR-ORCHESTRA --preflight-only</pre>
   <p>После проверки убери <code>--preflight-only</code> и добавь <code>--install-services</code> для регистрации служб ОС.</p>
   <p className="footnote">Для CLI требуется MYSTICEGO_ORCHESTRA_TOKEN с правами команды разработчиков. Серверный исполнитель использует только заранее настроенный бинарник установщика и утверждённые SSH-хосты; команды из браузера не принимаются.</p>
  </Panel></>}</details>
 </>;
}
