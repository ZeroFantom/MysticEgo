import {useState} from 'react';
import {Activity,Cpu,HardDrive,MemoryStick,Network} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,Badge,ErrorBanner} from '../components/primitives';

type Sample={at:string;cpuPercent:number;memoryBytes:number;managedBytes:number;memoryLimitBytes:number;rxBytesPerSecond:number;txBytesPerSecond:number;
  diskFreeBytes:number;diskTotalBytes:number;uptimeSeconds:number;hostCpuPercent?:number;hostMemoryUsedBytes?:number;hostMemoryTotalBytes?:number};
type Connection={from:string;to:string;name:string;parentName:string;online:boolean;measurement?:{latencyMs?:number;mbps?:number;measuredAt:string}};
type Telemetry={os:string;processors:number;samples:Sample[];links:Connection[];observabilityAvailable:boolean;audit?:{enrolled:number;online:number}};
const format=(value:number)=>new Intl.NumberFormat('ru',{maximumFractionDigits:1}).format(value);
const mib=(value:number)=>format(value/1048576)+' МиБ';
function ResourceChart({values,formatValue,tint,maximum}:{values:number[];formatValue:(v:number)=>string;tint?:string;maximum?:number}){
  if(values.length<2)return <div className="chart-loading">Собираем замеры…</div>;
  const top=maximum||Math.max(1,...values);
  const width=420,height=112;
  const curve=values.map((value,i)=>`${i*width/(values.length-1)},${height-5-(height-10)*Math.min(1,value/top)}`).join(' ');
  return <div className="resource-plot">
    <div className="chart-scale"><span>{formatValue(top)}</span><span>0</span></div>
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label="Изменение показателя во времени" style={{color:tint}}>
      {[7,55,105].map(y=><path key={y} d={`M 0 ${y} H ${width}`} className="chart-gridline"/>)}
      <polygon points={`0,${height} ${curve} ${width},${height}`}/>
      <polyline points={curve}/>
    </svg>
  </div>;
}
export function Diagnostics(){
  const {api,mode}=useApp();
  const data=useRemote(()=>mode==='orchestra'?api.request<Telemetry>('/orchestra/telemetry'):Promise.resolve(null),[api,mode],5000);
  const basic=useRemote(()=>mode!=='orchestra'?api.links():Promise.resolve([]),[api,mode],6000);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const [range,setRange]=useState(5);
  const [started,setStarted]=useState(false);
  async function launch(){
    setBusy(true);setError('');
    try{await api.request('/orchestra/telemetry/monitoring',{method:'POST'});setStarted(true)}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  const samples=(data.value?.samples??[]).slice(-range*12);
  const last=samples.at(-1);
  const graphs=last?[
    {label:'CPU Оркестра',value:format(last.cpuPercent)+'%',values:samples.map(x=>x.cpuPercent),display:(v:number)=>format(v)+'%',icon:Cpu,maximum:100},
    {label:'Память Оркестра',value:mib(last.memoryBytes),values:samples.map(x=>x.memoryBytes),display:mib,icon:MemoryStick},
    {label:'Входящий трафик сервера',value:format(last.rxBytesPerSecond/1024)+' КиБ/с',values:samples.map(x=>x.rxBytesPerSecond),display:(v:number)=>format(v/1024)+' КиБ/с',icon:Network},
    {label:'Исходящий трафик сервера',value:format(last.txBytesPerSecond/1024)+' КиБ/с',values:samples.map(x=>x.txBytesPerSecond),display:(v:number)=>format(v/1024)+' КиБ/с',icon:Activity},
    ...(last.hostCpuPercent!=null?[{label:'CPU сервера',value:format(last.hostCpuPercent)+'%',values:samples.flatMap(x=>x.hostCpuPercent==null?[]:[x.hostCpuPercent]),display:(v:number)=>format(v)+'%',icon:Cpu,maximum:100}]:[]),
    ...(last.hostMemoryUsedBytes!=null?[{label:'Память сервера',value:mib(last.hostMemoryUsedBytes),values:samples.flatMap(x=>x.hostMemoryUsedBytes==null?[]:[x.hostMemoryUsedBytes]),display:mib,icon:MemoryStick,maximum:last.hostMemoryTotalBytes}]:[])
  ]:[];
  return <>
    <Header title="Диагностика" description={mode==='orchestra'?'Состояние сервера и связь между узлами сети':'Доступность подключений этого узла'}/>
    <ErrorBanner text={error||data.error||basic.error}/>
    {mode==='orchestra'&&<>
      {last&&<div className="diag-summary">
        <label className="chart-range">Интервал<select value={range} onChange={event=>setRange(Number(event.target.value))}>
         <option value={1}>1 мин</option><option value={5}>5 мин</option><option value={15}>15 мин</option></select></label>
        {data.value?.audit&&<span>Audit в сети · {data.value.audit.online} из {data.value.audit.enrolled}</span>}
        <span><HardDrive size={17}/>{format(last.diskFreeBytes/1073741824)} ГиБ свободно</span>
        <span><Cpu size={17}/>{data.value?.processors} логических CPU</span>
        <span><Activity size={17}/>{format(last.uptimeSeconds/60)} мин в работе</span>
      </div>}
      <div className="telemetry-grid">{graphs.map(graph=><Panel key={graph.label} title={graph.label} aside={<graph.icon size={18}/>}>
        <p className="telemetry-value">{graph.value}</p><ResourceChart values={graph.values} formatValue={graph.display} maximum={graph.maximum}/>
        {samples.length>1&&<div className="chart-times"><time>{new Date(samples[0].at).toLocaleTimeString('ru-RU')}</time><time>{new Date(samples[samples.length-1].at).toLocaleTimeString('ru-RU')}</time></div>}
      </Panel>)}</div>
      <Panel title="Связи между узлами">
        <div className="table-wrap"><table><thead><tr><th>Дочерний узел</th><th>Родитель</th><th>Состояние</th><th>Задержка</th><th>Скорость</th><th>Замер</th></tr></thead><tbody>
          {data.value?.links.map(link=><tr key={link.from}><td>{link.name}</td><td>{link.parentName}</td>
            <td><Badge ok={link.online} text={link.online?'В сети':'Нет отчёта'}/></td>
            <td>{link.measurement?.latencyMs!=null?format(link.measurement.latencyMs)+' мс':'—'}</td>
            <td>{link.measurement?.mbps!=null?format(link.measurement.mbps)+' Мбит/с':'—'}</td>
            <td>{link.measurement?.measuredAt?new Date(link.measurement.measuredAt).toLocaleTimeString('ru-RU'):'—'}</td>
          </tr>)}
        </tbody></table></div>
        {!data.value?.links.length&&<p className="footnote">Замеры появятся после подключения узлов.</p>}
      </Panel>
      {data.value?.observabilityAvailable&&<Panel title="Prometheus и Grafana">
        <div className="row-actions"><button className="btn secondary" disabled={busy} onClick={()=>void launch()}>{busy?'Запускаем…':'Запустить на сервере'}</button>
          {started&&<span className="success">Prometheus · 9090, Grafana · 3000 на главном сервере</span>}</div>
      </Panel>}
    </>}
    {mode!=='orchestra'&&<Panel title="Подключения">{basic.value?.map(link=><div className="line" key={link.name}>
      <div className="grow"><strong>{link.name}</strong><small>{link.url}</small></div><Badge ok={link.reachable} text={link.reachable?format(link.ms)+' мс':'Недоступно'}/>
    </div>)}</Panel>}
  </>;
}
