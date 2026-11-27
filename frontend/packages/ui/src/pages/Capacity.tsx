import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {ResourceChart} from './Diagnostics';
type Assessment={nodeId:string;role:string;state:string;cpuPercent?:number;memoryPercent?:number;networkPercent?:number;availableSlots?:number;activeSlots?:number;pendingJobs?:number;reasons:string[]};
type Sample={nodeId:string;at:string;role:string;rxBytesPerSecond?:number;txBytesPerSecond?:number;monthlyCost?:number;pools:{id:string;model:string;location:string;slots:number;active:number;available:boolean}[]};
type Point={at:string;cpuPercent?:number;memoryPercent?:number;rxBytesPerSecond?:number;txBytesPerSecond?:number;activeSlots?:number;availableSlots?:number;pendingJobs?:number};
type View={assessment:{at:string;nodes:Assessment[];freshNodes:number;staleNodes:number;additionalPools?:number;knownMonthlyCost?:number;projectedMonthlyCost?:number;withinBudget?:boolean;currency:string;warnings:string[]};samples:Sample[];history:Point[]};
const number=(v?:number)=>v==null?'—':new Intl.NumberFormat(undefined,{maximumFractionDigits:1}).format(v);
export function Capacity(){
 const {api}=useApp();
 const remote=useRemote(()=>api.request<View>('/orchestra/capacity'),[api],5000,true);
 const view=remote.value,report=view?.assessment;
 const end=report?Date.parse(report.at):Date.now(),start=end-15*60*1000;
 const money=(n?:number)=>n==null?'Unknown':number(n)+' '+report?.currency+'/month';
 return <>
  <Header title="Capacity & resilience" description="Measured resources, model availability, backlog and advisory capacity budget."/>
  <ErrorBanner text={remote.error}/>
  {!report?<p role="status">Waiting for telemetry…</p>:<>
   <Panel title="Current assessment" aside={<button className="btn secondary" onClick={remote.reload}>Refresh</button>}>
    <div className="infra-status-grid">
      <article className="infra-status-card"><small>Reporting nodes</small><h3>{report.freshNodes}</h3><p>Stale / missing: {report.staleNodes}</p></article>
      <article className="infra-status-card"><small>Additional inference pools (advisory)</small><h3>{number(report.additionalPools)}</h3><p>Not a hardware forecast.</p></article>
      <article className="infra-status-card"><small>Current / projected monthly cost</small><h3>{money(report.knownMonthlyCost)}</h3><p>{money(report.projectedMonthlyCost)} · {report.withinBudget==null?'Budget unknown':report.withinBudget?'Within configured budget':'Over budget'}</p></article>
    </div>
    <p>Updated {new Date(report.at).toLocaleString()}</p>
    {report.warnings.map(w=><p key={w} className="footnote">{w}</p>)}
   </Panel>
   <Panel title="Primary Orchestra · 15-minute history">
    <div className="two-col">
      {([{key:'cpuPercent',label:'Host CPU',unit:'%',max:100},{key:'memoryPercent',label:'Host memory',unit:'%',max:100},
      {key:'rxBytesPerSecond',label:'Inbound traffic',unit:' MiB/s'},{key:'txBytesPerSecond',label:'Outbound traffic',unit:' MiB/s'},
      {key:'activeSlots',label:'Active inference slots',unit:''},{key:'pendingJobs',label:'Pending jobs',unit:''}] as const).map(item=><div key={item.key}><h3>{item.label}</h3><ResourceChart points={view.history.map(p=>({at:p.at,value:p[item.key]==null?null:item.unit===' MiB/s'?p[item.key]!/1048576:p[item.key]!}))} label={item.label} formatValue={v=>number(v)+item.unit} start={start} end={end} maximum={'max' in item?item.max:undefined}/></div>)}
    </div>
   </Panel>
   <Panel title="Nodes and model pools"><div className="stack">{report.nodes.map(row=><article key={row.nodeId} className="panel"><h3>{row.role} · {row.state}</h3><small>{row.nodeId}</small>
      <p>CPU {number(row.cpuPercent)}% · RAM {number(row.memoryPercent)}% · Network {number(row.networkPercent)}%</p>
      <p>Inference slots {number(row.activeSlots)} / {number(row.availableSlots)} · Queue {number(row.pendingJobs)}</p>
      {row.reasons.map(reason=><p key={reason}>{reason}</p>)}
      {row.state!=='stale'&&view.samples.find(s=>s.nodeId===row.nodeId)?.pools.map(pool=><p key={pool.id}>{pool.location} · {pool.model} · {pool.available?'Discovered / available':'Unavailable'} · {pool.slots===0?'Capacity not measured':pool.active+'/'+pool.slots}</p>)}
   </article>)}</div></Panel>
  </>}
 </>;
}
