import {useMemo,useState} from 'react';
import {Link,NavLink} from 'react-router-dom';
import {useApp,useRemote} from '../app/context';
import {ControlGraph,type GraphNode,type GraphEdge} from '../components/ControlGraph';
import {Panel,Header,ErrorBanner} from '../components/primitives';
import {useNetwork,kindNames,online,type NodeStatus} from './Network';
import {usePanelPreference} from '../app/panelPreferences';
import './infrastructure-workspace.css';

type Plan={components:{name:string;state:string;checkedAt:string}[];services:{unit:string;activeState?:string;loadState?:string;type?:string;result?:string;cpuPercent?:number;memoryBytes?:number;at:string}[]};
type Graph={nodes:GraphNode[];edges:GraphEdge[]};
const fresh=(at?:string,seconds=30)=>!!at&&Number.isFinite(Date.parse(at))&&Date.now()-Date.parse(at)<=seconds*1000&&Date.parse(at)<=Date.now()+5000;
const remoteState=(row:NodeStatus)=>!row.node.enabled?'disabled':fresh(row.report?.seen)?online(row)?'online':'offline':'pending';
function hostGraph(root:string,rootName:string,rows:NodeStatus[],reachable:boolean):Graph{
 const nodes:GraphNode[]=[{id:root,title:rootName,subtitle:'Orchestra · зарегистрированный центр',state:reachable?'online':'pending',x:24,y:36}],edges:GraphEdge[]=[];
 // The station registry proves central reports; registered parent links are configured, not observed traffic.
 const unique=rows.filter(r=>r.node.id!==root).slice(0,100);
 const visible=new Set([root,...unique.map(r=>r.node.id)]);
 for(const [i,row] of unique.entries()){
  nodes.push({id:row.node.id,title:row.node.name,subtitle:kindNames[row.node.kind]+' · '+(remoteState(row)==='online'?'Отчёт актуален':remoteState(row)==='disabled'?'Отключён':'Нет свежего подтверждения'),state:remoteState(row),x:340+Math.floor(i/8)*290,y:36+i%8*116});
  if(row.node.enabled&&fresh(row.report?.seen))edges.push({from:row.node.id,to:root,kind:'observed',label:'check-in'});
  const parent=row.node.parentId;if(parent&&parent!==row.node.id&&visible.has(parent))edges.push({from:row.node.id,to:parent,kind:'configured',label:'upstream'});
 }
 return {nodes,edges};
}
const definitions=[
 ['proxy','Caddy / HTTPS','caddy.service',24,24],
 ['web','Веб-панели Orchestra / Audit','mysticego-orchestra.service',24,144],
 ['api','Orchestra API','mysticego-orchestra.service',320,144],
 ['auth','Auth API','mysticego-orchestra.service',616,24],
 ['db','Хранилище данных','database',912,24],
 ['model','LM Studio','lmstudio',616,144],
 ['voice','Voice Service','voice',616,264],
 ['docker','Docker','docker',912,264],
 ['updater','Updater','mysticego-orchestra-updater.service',320,384],
 ['update-timer','Updater timer','mysticego-orchestra-updater.timer',24,384],
 ['reconcile','Infrastructure reconcile','mysticego-infrastructure-reconcile.service',616,384],
 ['reconcile-timer','Reconcile timer','mysticego-infrastructure-reconcile.timer',24,504],
 ['socket','Local setup socket','mysticego-local-setup.socket',320,504],
] as const;
const localEdges:GraphEdge[]=[
 {from:'web',to:'proxy',label:'HTTPS'},{from:'proxy',to:'api',label:'reverse proxy'},
 {from:'api',to:'auth',label:'авторизация'},{from:'auth',to:'db',label:'данные'},
 {from:'api',to:'db',label:'хранилище'},{from:'api',to:'model',label:'AI API'},
 {from:'api',to:'voice',label:'речь'},{from:'update-timer',to:'updater',label:'запуск'},
 {from:'updater',to:'api',label:'релиз / health'},{from:'api',to:'socket',label:'обслуживание'},
 {from:'reconcile-timer',to:'reconcile',label:'запуск'},{from:'reconcile',to:'docker',label:'подготовка'},
 {from:'reconcile',to:'model',label:'проверка'},{from:'reconcile',to:'voice',label:'проверка'},
];
function microGraph(root:string,rows:NodeStatus[],plan?:Plan|null):Graph{
 const nodes:GraphNode[]=definitions.map(([id,title,service,x,y])=>{
  const s=plan?.services.find(s=>s.unit===service),c=plan?.components.find(c=>c.name===service);
  const confirmed=s&&fresh(s.at,20),checked=c&&fresh(c.checkedAt,180);
  const state=confirmed?s.loadState==='not-found'?'pending':s.activeState==='failed'?'offline':s.activeState==='active'||s.type==='oneshot'&&s.result==='success'?'online':'pending':checked?c.state==='available'?'online':c.state==='disabled'?'disabled':c.state==='degraded'?'offline':'pending':'pending';
  const subtitle=confirmed?s.type==='oneshot'&&s.activeState==='inactive'?'Ожидает запуска':s.activeState??'Нет данных':checked?c.state:'Нет свежего замера';
  return {id,title,subtitle,state,x,y};
 });
 const edges=[...localEdges];
 const visible=rows.filter(r=>r.node.id!==root).slice(0,80);
 for(const [i,row] of visible.entries()){
  nodes.push({id:'remote:'+row.node.id,title:row.node.name,subtitle:kindNames[row.node.kind]+' · удалённый API',state:remoteState(row),x:1210+Math.floor(i/8)*280,y:24+i%8*110});
  if(row.node.enabled&&fresh(row.report?.seen))edges.push({from:'remote:'+row.node.id,to:'api',kind:'observed',label:'check-in'});
  edges.push({from:'remote:'+row.node.id,to:'auth',kind:'configured',label:'Auth'});
 }
 return {nodes,edges};
}
export function ControlGraphs({kind}:{kind:'macro'|'micro'}){
 const {api}=useApp(),{context,topology}=useNetwork();
 const plan=useRemote(()=>kind==='micro'?api.request<Plan>('/orchestra/update/plan'):Promise.resolve(null),[api,kind],5000,true);
 const [zoom,setZoom]=usePanelPreference('control.'+kind+'.zoom',100,(v):v is number=>typeof v==='number'&&v>=50&&v<=160);
 const [selected,setSelected]=useState('');
 const graph=useMemo(()=>context.value&&topology.value?kind==='macro'?hostGraph(context.value.rootId,context.value.rootName,topology.error?topology.value.map(r=>({...r,report:undefined})):topology.value,!topology.error):microGraph(context.value.rootId,topology.error?topology.value.map(r=>({...r,report:undefined})):topology.value,plan.error?null:plan.value):null,[kind,context.value,topology.value,topology.error,plan.value,plan.error]);
 const node=graph?.nodes.find(n=>n.id===selected);
 return <div className="control-graph-page"><Header title={kind==='macro'?'Макро контрольный граф':'Микро контрольный граф'} description={kind==='macro'?'Связи зарегистрированных серверов и актуальные отчёты связи':'Службы Orchestra, веб-панели и подключённые удалённые API на одной схеме'}/>
 <nav className="control-graph-nav"><NavLink className="btn secondary" to="/infrastructure/graphs/macro">Макро</NavLink><NavLink className="btn secondary" to="/infrastructure/graphs/micro">Микро</NavLink><Link className="btn secondary" to="/nodes">Бизнес-логика</Link><Link className="btn secondary" to="/infrastructure/services">Службы и нагрузка</Link></nav>
 <ErrorBanner text={context.error||topology.error||plan.error}/>
 <div className="control-graph-legend"><span>Сплошная зелёная линия — свежий отчёт узла</span><span>Пунктир — архитектурная связь, не измерение трафика</span><span>Серый / жёлтый — отключён / нет подтверждения</span></div>
 <Panel title={kind==='macro'?'Сетевой контур':'Сервисный контур'}><label className="graph-zoom">Масштаб {zoom}%<input type="range" min={50} max={160} step={5} value={zoom} onChange={e=>setZoom(Number(e.target.value))}/></label>
 {graph?<ControlGraph key={kind} nodes={graph.nodes} edges={graph.edges} scope={kind} selected={selected} choose={setSelected} zoom={zoom}/>:<p>Ожидаем зарегистрированные узлы.</p>}
 {node&&<div className="infra-status-card"><h3>{node.title}</h3><p>{node.subtitle}</p><small>Состояние: {node.state}</small></div>}</Panel>
 <p className="footnote">Физическая сеть и межсервисный трафик не сканируются. Отдельные процессы удалённых серверов пока не передают собственные системные счётчики; удалённый API показан как один узел.</p>
 </div>
}
