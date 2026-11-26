import {useId,useState} from 'react';
import {Activity,Cpu,HardDrive,MemoryStick,Network} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,Badge,ErrorBanner} from '../components/primitives';
import {InfrastructureSetup} from './InfrastructureSetup';
import './monitoring.css';
import {chartGeometry,smoothEdges,gapPath,isMeasurement as valid,type SamplePoint as Point} from './telemetryGeometry';

type Sample={at:string;cpuPercent:number;memoryBytes:number;managedBytes:number;memoryLimitBytes:number;rxBytesPerSecond:number;txBytesPerSecond:number;
  diskFreeBytes:number;diskTotalBytes:number;uptimeSeconds:number;hostCpuPercent?:number;hostMemoryUsedBytes?:number;hostMemoryTotalBytes?:number};
type Connection={from:string;to:string;name:string;parentName:string;online:boolean;measurement?:{latencyMs?:number;mbps?:number;measuredAt:string}};
type Telemetry={os:string;processors:number;samples:Sample[];links:Connection[];observabilityAvailable:boolean;audit?:{enrolled:number;online:number}};
const format=(value:number)=>new Intl.NumberFormat('ru',{maximumFractionDigits:1}).format(value);
const mib=(value:number)=>format(value/1048576)+' МиБ';
function ResourceChart({points,formatValue,maximum,label,start,end}:{points:Point[];formatValue:(v:number)=>string;maximum?:number;label:string;start:number;end:number}){
  const id='telemetry-'+useId().replace(/[^a-zA-Z0-9_-]/g,'-');
  const actual=points.filter(p=>valid(p.value)&&Number.isFinite(Date.parse(p.at)));
  if(!actual.length)return <div className="chart-loading">Нет доступных замеров</div>;
  const top=Math.max(1,valid(maximum)?maximum:0,...actual.map(p=>p.value!));
  const width=420,height=112;
  const {segments,gaps}=chartGeometry(points,start,end,top,width,height);
  const latest=segments.at(-1)?.at(-1);
  const trailing=latest&&end-latest.at>15000;
  const leading=segments[0]?.[0]&&segments[0][0].at-start>15000;
  return <div className="telemetry-chart">
    <div className="resource-plot">
      <div className="chart-scale"><span>{formatValue(top)}</span><span>{formatValue(top/2)}</span><span>{formatValue(0)}</span></div>
      <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={label+' — реальные замеры во времени; янтарные пунктирные переходы обозначают отсутствие измерений'}>
        <defs>
          <linearGradient id={id+'-area'} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="currentColor" stopOpacity=".22"/><stop offset="100%" stopColor="currentColor" stopOpacity="0"/>
          </linearGradient>
          {gaps.map(gap=><linearGradient key={gap.from.at+'-'+gap.to.at} id={id+'-gap-'+gap.from.at+'-'+gap.to.at} gradientUnits="userSpaceOnUse" x1={gap.from.x} y1="0" x2={gap.to.x} y2="0">
            <stop offset="0%" stopColor="currentColor"/><stop offset="22%" stopColor="var(--chart-gap-color)"/><stop offset="78%" stopColor="var(--chart-gap-color)"/><stop offset="100%" stopColor="currentColor"/>
          </linearGradient>)}
        </defs>
        {[5,56,107].map(y=><path key={y} d={`M 0 ${y} H ${width}`} className="chart-gridline"/>)}
        {leading&&<rect x="0" y="5" width={segments[0][0].x} height={height-10} className="chart-gap-region"/>}
        {trailing&&<rect x={latest.x} y="5" width={width-latest.x} height={height-10} className="chart-gap-region"/>}
        {gaps.map(gap=><rect key={'region-'+gap.from.at+'-'+gap.to.at} x={gap.from.x} y="5" width={gap.to.x-gap.from.x} height={height-10} className="chart-gap-region"/>)}
        {segments.map(segment=>{
          const first=segment[0],last=segment.at(-1)!;
          const edges=smoothEdges(segment);
          const begin=`M ${first.x} ${first.y}`;
          const newest=last.at===latest?.at;
          const animate=newest&&edges.length>0;
          const area=begin+' '+edges.map(edge=>edge.command).join(' ')+` L ${last.x} ${height} L ${first.x} ${height} Z`;
          return <g key={first.at}>
            {edges.length>0&&<path d={area} fill={`url(#${id}-area)`}/>}
            {edges.length>0&&<path className="chart-line" d={begin+' '+(animate?edges.slice(0,-1):edges).map(edge=>edge.command).join(' ')}/>}
            {animate&&<path key={last.at} className="chart-line chart-new-edge" pathLength={1} d={`M ${edges.at(-1)!.from.x} ${edges.at(-1)!.from.y} ${edges.at(-1)!.command}`}/>}
            {edges.length===0&&<circle className={newest?'chart-new-point':''} cx={first.x} cy={first.y} r={2.5} fill="currentColor"/>}
          </g>;
        })}
        {gaps.map(gap=><path key={gap.from.at+'-'+gap.to.at} className="chart-gap-bridge" d={gapPath(gap)} stroke={`url(#${id}-gap-${gap.from.at}-${gap.to.at})`}><title>Нет замеров: {new Date(gap.from.at).toLocaleTimeString('ru-RU')} — {new Date(gap.to.at).toLocaleTimeString('ru-RU')}</title></path>)}
      </svg>
    </div>
    <div className="chart-data-legend"><span><i className="chart-legend-solid"/>Замеры</span><span><i className="chart-legend-gap"/>Нет данных · условный переход</span></div>
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
