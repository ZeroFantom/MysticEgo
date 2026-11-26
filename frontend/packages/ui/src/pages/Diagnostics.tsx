import {useState} from 'react';
import {Activity,Cpu,HardDrive,MemoryStick,Network} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,Badge,ErrorBanner} from '../components/primitives';
import {InfrastructureSetup} from './InfrastructureSetup';
import './monitoring.css';

type Sample={at:string;cpuPercent:number;memoryBytes:number;managedBytes:number;memoryLimitBytes:number;rxBytesPerSecond:number;txBytesPerSecond:number;
  diskFreeBytes:number;diskTotalBytes:number;uptimeSeconds:number;hostCpuPercent?:number;hostMemoryUsedBytes?:number;hostMemoryTotalBytes?:number};
type Connection={from:string;to:string;name:string;parentName:string;online:boolean;measurement?:{latencyMs?:number;mbps?:number;measuredAt:string}};
type Telemetry={os:string;processors:number;samples:Sample[];links:Connection[];observabilityAvailable:boolean;audit?:{enrolled:number;online:number}};
const format=(value:number)=>new Intl.NumberFormat('ru',{maximumFractionDigits:1}).format(value);
const mib=(value:number)=>format(value/1048576)+' МиБ';
type Point={at:string;value:number|null|undefined};
const valid=(value:unknown):value is number=>typeof value==='number'&&Number.isFinite(value)&&value>=0;
function ResourceChart({points,formatValue,maximum,label,start,end}:{points:Point[];formatValue:(v:number)=>string;maximum?:number;label:string;start:number;end:number}){
  const actual=points.filter(p=>valid(p.value)&&Number.isFinite(Date.parse(p.at)));
  if(!actual.length)return <div className="chart-loading">Нет доступных замеров</div>;
  const top=Math.max(1,valid(maximum)?maximum:0,...actual.map(p=>p.value!));
  const width=420,height=112;
  const x=(at:string)=>Math.max(0,Math.min(width,(Date.parse(at)-start)/Math.max(1,end-start)*width));
  const y=(value:number)=>height-5-(height-10)*value/top;
  const segments:{x:number;y:number}[][]=[];
  let lastAt:number|undefined;
  for(const point of points){
    const at=Date.parse(point.at);
    if(!valid(point.value)||!Number.isFinite(at)){lastAt=undefined;continue}
    if(lastAt===undefined||at-lastAt>15000)segments.push([]);
    segments[segments.length-1].push({x:x(point.at),y:y(point.value)});
    lastAt=at;
  }
  return <div className="resource-plot">
    <div className="chart-scale"><span>{formatValue(top)}</span><span>{formatValue(top/2)}</span><span>{formatValue(0)}</span></div>
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={label+' — реальные замеры во времени'}>
      {[5,56,107].map(y=><path key={y} d={`M 0 ${y} H ${width}`} className="chart-gridline"/>)}
      {segments.map((segment,index)=>segment.length===1?<circle key={index} cx={segment[0].x} cy={segment[0].y} r={3} fill="currentColor"/>:
        <g key={index}><polygon points={`${segment[0].x},${height} ${segment.map(p=>p.x+','+p.y).join(' ')} ${segment.at(-1)!.x},${height}`}/><polyline points={segment.map(p=>p.x+','+p.y).join(' ')}/></g>)}
    </svg>
  </div>;
}
export function Diagnostics(){
  const {api,mode}=useApp();
  const data=useRemote(()=>mode==='orchestra'?api.request<Telemetry>('/orchestra/telemetry'):Promise.resolve(null),[api,mode],5000,true);
  const basic=useRemote(()=>mode!=='orchestra'?api.links():Promise.resolve([]),[api,mode],6000,true);
  const [range,setRange]=useState(5);
  const ordered=(data.value?.samples??[]).filter(s=>Number.isFinite(Date.parse(s.at))).slice().sort((a,b)=>Date.parse(a.at)-Date.parse(b.at));
  const last=ordered.at(-1);
  const end=last?Date.parse(last.at):Date.now();
  const start=end-range*60_000;
  const samples=ordered.filter(s=>Date.parse(s.at)>=start);
  const stale=!!last&&(Date.now()-end>20000||!!data.error);
  const points=(pick:(sample:Sample)=>number|null|undefined)=>samples.map(s=>({at:s.at,value:pick(s)}));
  const display=(value:number|null|undefined,formatValue:(v:number)=>string)=>valid(value)?formatValue(value):'—';
  const graphs=last?[
    {label:'CPU Оркестра',value:display(last.cpuPercent,v=>format(v)+'%'),points:points(x=>x.cpuPercent),display:(v:number)=>format(v)+'%',icon:Cpu,maximum:100},
    {label:'Память Оркестра',value:display(last.memoryBytes,mib),points:points(x=>x.memoryBytes),display:mib,icon:MemoryStick},
    {label:'Входящий трафик сервера',value:display(last.rxBytesPerSecond,v=>format(v/1024)+' КиБ/с'),points:points(x=>x.rxBytesPerSecond),display:(v:number)=>format(v/1024)+' КиБ/с',icon:Network},
    {label:'Исходящий трафик сервера',value:display(last.txBytesPerSecond,v=>format(v/1024)+' КиБ/с'),points:points(x=>x.txBytesPerSecond),display:(v:number)=>format(v/1024)+' КиБ/с',icon:Activity},
    ...(samples.some(s=>valid(s.hostCpuPercent))?[{label:'CPU сервера',value:display(last.hostCpuPercent,v=>format(v)+'%'),points:points(x=>x.hostCpuPercent),display:(v:number)=>format(v)+'%',icon:Cpu,maximum:100}]:[]),
    ...(samples.some(s=>valid(s.hostMemoryUsedBytes))?[{label:'Память сервера',value:display(last.hostMemoryUsedBytes,mib),points:points(x=>x.hostMemoryUsedBytes),display:mib,icon:MemoryStick,maximum:last.hostMemoryTotalBytes}]:[])
  ]:[];
  return <>
    <Header title="Диагностика" description={mode==='orchestra'?'Состояние сервера и связь между узлами сети':'Доступность подключений этого узла'}/>
    <ErrorBanner text={data.error||basic.error}/>
    {mode==='orchestra'&&<>
      <div className="diag-summary">
        <label className="chart-range">Интервал<select value={range} onChange={event=>setRange(Number(event.target.value))}>
         <option value={1}>1 мин</option><option value={5}>5 мин</option><option value={15}>15 мин</option></select></label>
        {data.value?.audit&&<span>Audit в сети · {data.value.audit.online} из {data.value.audit.enrolled}</span>}
        {last&&<><span><HardDrive size={17}/>{display(last.diskFreeBytes,v=>format(v/1073741824)+' ГиБ')} свободно</span>
        <span><Cpu size={17}/>{data.value?.processors} логических CPU</span>
        <span><Activity size={17}/>{display(last.uptimeSeconds,v=>format(v/60)+' мин')} в работе</span></>}
        {last&&<span className="monitor-freshness" data-stale={stale} role="status">{stale?'Данные устарели':'Последний замер'} · {new Date(last.at).toLocaleTimeString('ru-RU')}</span>}
      </div>
      {!last&&<Panel title="Телеметрия"><p className="monitor-empty" role="status">{data.error?'Не удалось получить телеметрию':data.loading?'Загружаем замеры…':'Сервер пока не передал замеры'}</p></Panel>}
      <div className="telemetry-grid">{graphs.map(graph=><Panel key={graph.label} title={graph.label} aside={<graph.icon size={18}/>}>
        <p className="telemetry-value">{graph.value}</p><ResourceChart points={graph.points} formatValue={graph.display} maximum={graph.maximum} label={graph.label} start={start} end={end}/>
        <div className="chart-times"><time dateTime={new Date(start).toISOString()}>{new Date(start).toLocaleTimeString('ru-RU')}</time><time dateTime={new Date(end).toISOString()}>{new Date(end).toLocaleTimeString('ru-RU')}</time></div>
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
        {!data.value?.links.length&&<p className="footnote">{data.error?'Не удалось получить состояние связей.':data.loading?'Загружаем связи…':'Замеры появятся после подключения узлов.'}</p>}
      </Panel>
      <InfrastructureSetup/>
    </>}
    {mode!=='orchestra'&&<Panel title="Подключения">{!basic.value?.length&&<p className="monitor-empty">{basic.error?'Данные недоступны':basic.loading?'Проверяем подключения…':'Нет зарегистрированных подключений'}</p>}{basic.value?.map(link=><div className="line" key={link.name}>
      <div className="grow"><strong>{link.name}</strong><small>{link.url}</small></div><Badge ok={link.reachable} text={link.reachable?format(link.ms)+' мс':'Недоступно'}/>
    </div>)}</Panel>}
  </>;
}
