import {usePanelPreference} from '../app/panelPreferences';
import {GlobalTimeweb} from "./GlobalTimeweb";
import {useMemo,useState,type FormEvent,type CSSProperties} from 'react';
import {Link,useNavigate,useSearchParams} from 'react-router-dom';
import {Activity,ArrowUpRight,Building2,Cloud,HardDrive,House,Laptop,Network,Plus,Rows3,ShieldCheck,UserRound} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner,Badge} from '../components/primitives';
import './monitoring.css';
import {DashboardHero} from '../components/DashboardHero';
import {ResourceChart,type Telemetry} from './Diagnostics';
import {isMeasurement} from './telemetryGeometry';

export type Kind='orchestra'|'global'|'local'|'agent'|'audit';
export type NodeInfo={id:string;kind:Kind;name:string;parentId:string;ownerId?:string;publicUrl?:string;mode:string;enabled:boolean;applicationId?:string;members?:string[]};
export type NodeStatus={node:NodeInfo;report?:{seen:string;healthy:string[];error?:string;link?:{latencyMs?:number;mbps?:number;measuredAt:string}};people:number;sites:number};
export type NetworkContext={rootId:string;rootName:string;orchestraUrl:string;authUrl:string;developer:boolean;owner?:boolean;actor:string;lan:boolean};
export const kindNames:Record<Kind,string>={orchestra:'Оркестр',global:'Global',local:'Local',agent:'Agent',audit:'Audit'};
const icons={orchestra:ShieldCheck,global:Cloud,local:HardDrive,agent:Laptop,audit:House};
export const online=(row:NodeStatus)=>row.node.enabled&&(row.node.kind==='orchestra'||row.report?.healthy.includes(row.node.kind)===true);
const nodeState=(row:NodeStatus)=>!row.node.enabled?'disabled':online(row)?'online':row.report?'offline':'pending';
const nodeLabel=(row:NodeStatus)=>({disabled:'Отключён',online:'В сети',offline:'Нет связи',pending:'Нет отчёта'}[nodeState(row)]);
export function useNetwork(){
  const {api}=useApp();
  const context=useRemote(()=>api.request<NetworkContext>('/managed/context'),[api]);
  const topology=useRemote(()=>api.request<NodeStatus[]>('/managed/topology'),[api],5000,true);
  return {context,topology};
}

export function OrchestraOverview(){
  const {mode}=useApp();
  const navigate=useNavigate();
  const {context,topology}=useNetwork();
  const globals=topology.value?.filter(x=>x.node.kind==='global')??[];
  const people=globals.reduce((sum,x)=>sum+x.people,0);
  const sites=globals.reduce((sum,x)=>sum+x.sites,0);
  const up=globals.filter(online).length;
  const maximum=Math.max(1,...globals.flatMap(x=>[x.people,x.sites]));
  return <>
    <DashboardHero title={mode==='orchestra'?'Оркестр MysticEgo':mode==='global'?'Обзор организации':'Обзор площадки'} eyebrow={mode==='orchestra'?'ORCHESTRA · ОБЗОР':mode.toUpperCase()+' · ОБЗОР'}
      description={mode==='orchestra'?'Организации, площадки и состояние сети в едином пространстве.':'Доступные организации и площадки сети MysticEgo.'}
      status={<span role="status">{topology.error?'Обновление недоступно':topology.value?'Топология получена':'Загрузка топологии…'}</span>}
      action={<><Link className="btn secondary" to="/nodes"><Network size={17}/> Схема сети</Link>{(context.value?.owner??context.value?.developer)&&<Link className="btn" to="/setup"><Plus size={17}/> Развернуть Global</Link>}</>}/>
    <ErrorBanner text={topology.error}/>
    {topology.value&&topology.error&&<p className="monitor-empty">Показаны последние полученные данные; обновление не удалось.</p>}
    <div className="metrics summary-grid dashboard-metrics">
      {[{icon:Cloud,count:globals.length,label:'Организации Global'},{icon:UserRound,count:people,label:'Участники'},
        {icon:Building2,count:sites,label:'Площадки Local'},{icon:Activity,count:up,label:'Global в сети'}].map(tile=>
        <article className="metric summary-metric" key={tile.label}><tile.icon size={20}/><strong>{topology.value?tile.count:topology.loading?'…':'—'}</strong><span>{tile.label}</span></article>)}
    </div>
    <div className="dashboard-monitor-grid">
      <Panel title="Топология сети" aside={<Link className="text-link" to="/nodes">Открыть <ArrowUpRight size={15}/></Link>}>
        <div className="dashboard-network">{context.value&&topology.value?<TreeGraph root={context.value.rootId} selected="" choose={row=>navigate('/nodes?node='+encodeURIComponent(row.node.id))}
          rows={[{node:{id:context.value.rootId,kind:'orchestra',name:context.value.rootName||'Оркестр',parentId:'',mode:'native',enabled:true},people:0,sites:0},...topology.value.filter(row=>row.node.id!==context.value!.rootId)]}/>:<p className="monitor-empty">{topology.error||context.error?'Топология недоступна':'Загружаем топологию…'}</p>}</div>
      </Panel>
      {mode==='orchestra'?<DashboardTelemetry/>:<Panel title="Состояние организаций">
        <div className="dashboard-health-list">{globals.length?globals.map(row=><div className="line" key={row.node.id}><Cloud size={18}/><div className="grow"><strong>{row.node.name}</strong></div><Badge ok={online(row)} text={nodeLabel(row)}/></div>):<p className="monitor-empty">{topology.value?'Нет организаций':'Данные недоступны'}</p>}</div>
      </Panel>}
    </div>
    <div className="overview-grid">
      <Panel title="Организации" aside={<span className="muted">{topology.value?globals.length:'—'} Global</span>}>
        {!topology.value?<p className="monitor-empty">{topology.error?'Данные организаций недоступны':'Загружаем организации…'}</p>:globals.length===0?<div className="network-empty"><Cloud size={28}/><p>Здесь появятся созданные Global</p>
          {(context.value?.owner??context.value?.developer)&&<Link to="/setup" className="btn secondary">Подготовить первую организацию</Link>}</div>:
          <div className="table-wrap"><table><thead><tr><th>Название</th><th>Участники</th><th>Local</th><th>Состояние</th></tr></thead>
            <tbody>{globals.map(row=><tr key={row.node.id}><td><Link className="text-link" to={'/nodes?node='+row.node.id}><Cloud size={15}/>{row.node.name}</Link></td>
              <td>{row.people}</td><td>{row.sites}</td><td><Badge ok={online(row)} text={nodeLabel(row)}/></td></tr>)}</tbody></table></div>}
      </Panel>
      <Panel title="Участники и площадки" aside={<span className="chart-legend"><i/>Люди<i/>Local</span>}>
        <div className="organization-chart">{!topology.value?<p className="monitor-empty">Нет доступных данных</p>:globals.length===0?<p className="footnote">Диаграмма появится после создания Global.</p>:
          globals.map(row=><div key={row.node.id} className="organization-bar">
            <strong>{row.node.name}</strong><div><span style={{width:`${row.people/maximum*100}%`}}/><b>{row.people}</b></div>
            <div><span style={{width:`${row.sites/maximum*100}%`}}/><b>{row.sites}</b></div>
          </div>)}</div>
      </Panel>
    </div>
    <Link className="text-link" to="/nodes"><Network size={17}/> Открыть схему сети <ArrowUpRight size={15}/></Link>
  </>;
}

function DashboardTelemetry(){
  const {api}=useApp();
  const data=useRemote(()=>api.request<Telemetry>('/orchestra/telemetry'),[api],5000,true);
  const ordered=(data.value?.samples??[]).filter(s=>Number.isFinite(Date.parse(s.at))).sort((a,b)=>Date.parse(a.at)-Date.parse(b.at));
  const last=ordered.at(-1),end=last?Date.parse(last.at):Date.now(),start=end-60_000;
  const points=ordered.filter(s=>Date.parse(s.at)>=start).map(s=>({at:s.at,value:s.cpuPercent}));
  const stale=!!last&&(Date.now()-end>20000||!!data.error);
  return <Panel title="CPU Оркестра · 1 мин" aside={<Link className="text-link" to="/diagnostics">Диагностика <ArrowUpRight size={15}/></Link>}>
    {last?<><p className="telemetry-value">{isMeasurement(last.cpuPercent)?new Intl.NumberFormat('ru',{maximumFractionDigits:1}).format(last.cpuPercent)+'%':'—'}</p>
      <ResourceChart points={points} maximum={100} start={start} end={end} label="CPU Оркестра" formatValue={v=>Math.round(v)+'%'}/>
      <div className="chart-times"><time dateTime={new Date(start).toISOString()}>{new Date(start).toLocaleTimeString('ru-RU')}</time><time dateTime={new Date(end).toISOString()}>{new Date(end).toLocaleTimeString('ru-RU')}</time></div>
      <p className="dashboard-note" role="status">{stale?'Последний замер устарел':'Последний замер'} · {new Date(end).toLocaleTimeString('ru-RU')}</p></>:<p className="monitor-empty">{data.error?'Телеметрия недоступна':data.loading?'Загружаем замеры…':'Нет замеров'}</p>}
  </Panel>;
}

function TreeGraph({rows,root,selected,choose}:{rows:NodeStatus[];root:string;selected:string;choose:(row:NodeStatus)=>void}){
  const columns=useMemo(()=>{
    const byParent=new Map<string,NodeStatus[]>();
    for(const row of rows){
      if(row.node.id===root)continue;
      const key=row.node.parentId||root;
      byParent.set(key,[...(byParent.get(key)??[]),row]);
    }
    const cards:{row:NodeStatus;x:number;y:number}[]=[];
    const edges:{x1:number;y1:number;x2:number;y2:number;id:string}[]=[];
    const visited=new Set<string>();
    let leaves=0;
    function visit(row:NodeStatus,depth:number):{row:NodeStatus;x:number;y:number}{
      visited.add(row.node.id);
      const x=24+depth*216;
      const placed:{row:NodeStatus;x:number;y:number}[]=[];
      for(const child of byParent.get(row.node.id)??[]){if(!visited.has(child.node.id))placed.push(visit(child,depth+1))}
      const y=placed.length?(placed[0].y+placed[placed.length-1].y)/2:24+(leaves++)*94;
      const result={row,x,y};cards.push(result);
      for(const child of placed)edges.push({x1:x+182,y1:y+34,x2:child.x,y2:child.y+34,id:child.row.node.id});
      return result;
    }
    const base=rows.find(x=>x.node.id===root);
    if(base)visit(base,0);
    for(const row of rows)if(!visited.has(row.node.id))visit(row,1);
    return {cards,edges,width:Math.max(240,...cards.map(x=>x.x+204)),height:Math.max(140,...cards.map(x=>x.y+92))};
  },[rows,root]);
  return <div className="network-graph-scroll" tabIndex={0} role="region" aria-label="Граф узлов, прокрутка по горизонтали и вертикали"><div className="network-graph" style={{width:columns.width,height:columns.height}}>
    <svg width={columns.width} height={columns.height} className="network-edges" aria-hidden="true">
      {columns.edges.map(edge=><path key={edge.id} d={`M ${edge.x1} ${edge.y1} C ${edge.x1+23} ${edge.y1}, ${edge.x2-23} ${edge.y2}, ${edge.x2} ${edge.y2}`}/>)}
    </svg>
    {columns.cards.map(({row,x,y})=>{
      const Icon=icons[row.node.kind];
      return <button className={'network-card '+(selected===row.node.id?'selected':'')} style={{left:x,top:y}} key={row.node.id}
        onClick={()=>choose(row)} aria-pressed={selected===row.node.id} data-node-state={nodeState(row)} aria-label={row.node.name+' · '+kindNames[row.node.kind]+' · '+nodeLabel(row)}>
        <span className="network-node-icon"><Icon size={18}/></span><span><strong title={row.node.name}>{row.node.name}</strong><small>{kindNames[row.node.kind]} · {nodeLabel(row)}</small></span>
        <i aria-hidden="true" className={online(row)?'network-dot online':'network-dot'}/>
      </button>;
    })}
  </div></div>;
}

export function NetworkPage(){
  const {api,mode}=useApp();
  const {context,topology}=useNetwork();
  const [params,setParams]=useSearchParams();
  const [view,setView]=usePanelPreference<'graph'|'table'>('network.view','graph',(v):v is 'graph'|'table'=>v==='graph'||v==='table');
  const [filter,setFilter]=usePanelPreference('network.filter','',(v):v is string=>typeof v==='string'&&v.length<=200);
  const [zoom,setZoom]=usePanelPreference('network.zoom',100,(v):v is number=>typeof v==='number'&&Number.isFinite(v)&&v>=50&&v<=200);
  const [name,setName]=useState<string|null>(null);
  const [enabled,setEnabled]=useState<boolean|null>(null);
  const [member,setMember]=useState('');
  const [message,setMessage]=useState('');
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const root=context.value?.rootId??'';
  const rows=useMemo(()=>{
    if(!root)return [];
    const config=context.value!;
    return [{node:{id:root,kind:'orchestra',name:config.rootName||'Оркестр',parentId:'',mode:'native',enabled:true,publicUrl:config.orchestraUrl},people:0,sites:0} as NodeStatus,...(topology.value??[]).filter(item=>item.node.id!==root)];
  },[root,context.value,topology.value]);
  const visible=useMemo(()=>{
    const term=filter.trim().toLowerCase();
    if(!term)return rows;
    const included=new Set<string>([root]);
    for(const row of rows.filter(item=>(item.node.name+' '+kindNames[item.node.kind]).toLowerCase().includes(term))){
      let next:NodeStatus|undefined=row;
      while(next&&!included.has(next.node.id)){included.add(next.node.id);next=rows.find(item=>item.node.id===next?.node.parentId)}
    }
    return rows.filter(row=>included.has(row.node.id));
  },[rows,filter,root]);
  const selected=rows.find(row=>row.node.id===params.get('node'))??rows[0];
  const canEdit=selected?.node&&selected.node.kind!=='orchestra'&&selected.node.kind!=='audit'&&(context.value?.developer||selected.node.ownerId===context.value?.actor);
  const selectedId=selected?.node.id;
  const audit=useRemote(()=>selectedId&&mode==='orchestra'?api.request<{items:{id:string;at:string;action:string;detail?:string}[]}>('/audit/page?size=5&target='+selectedId):Promise.resolve(null),[api,selectedId,mode]);
  function choose(row:NodeStatus){
    setParams({node:row.node.id});setName(null);setEnabled(null);setMessage('');setError('');setMember('');
  }
  async function edit(event:FormEvent){
    event.preventDefault();if(!selected||busy||!(name??selected.node.name).trim())return;setBusy(true);setError('');setMessage('');
    try{await api.request('/managed/nodes/'+selected.node.id,{method:'PUT',body:JSON.stringify({name:(name??selected.node.name).trim(),enabled:enabled??selected.node.enabled})});
      setMessage('Узел обновлён');topology.reload();audit.reload()}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function changeMember(id:string,allowed:boolean){
    if(!selected||!id)return;setBusy(true);setError('');setMessage('');
    try{await api.request(`/managed/nodes/${selected.node.id}/members/${encodeURIComponent(id)}`,{method:'PUT',body:JSON.stringify({allowed})});
      topology.reload();setMember('');setMessage(allowed?'Доступ участника выдан':'Доступ участника отозван')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  return <>
    <Header title="Узлы сети" description="Оркестр, организации, площадки и рабочие станции" action={
      <Link to="/setup" className="btn"><Plus size={17}/>{mode==='local'?'Комплект Agent':mode==='global'?'Создать Local':'Развернуть Global'}</Link>}/>
    <ErrorBanner text={context.error||topology.error||error}/>
    {topology.value&&topology.error&&<p className="monitor-empty">Показана последняя полученная топология; обновление не удалось.</p>}
    <div className="topology-layout">
      <Panel title="Топология" aside={<div className="segmented">
        <button type="button" aria-pressed={view==='graph'} className={view==='graph'?'active':''} onClick={()=>setView('graph')}><Network size={16}/> Граф</button>
        <button type="button" aria-pressed={view==='table'} className={view==='table'?'active':''} onClick={()=>setView('table')}><Rows3 size={16}/> Таблица</button>
      </div>}>
        <label className="search"><input aria-label="Фильтр узлов" placeholder="Название или тип узла" value={filter} onChange={event=>setFilter(event.target.value)}/></label>
        {!context.value||!topology.value?<p className="monitor-empty">{context.error||topology.error?'Топология недоступна':'Загружаем топологию…'}</p>:view==='graph'?<><label className="graph-zoom">Масштаб {zoom}%<input type="range" min={50} max={200} step={5} value={zoom} onChange={event=>setZoom(Number(event.target.value))}/></label>
          <div className="network-zoomed" style={{'--network-zoom':zoom/100} as CSSProperties}><TreeGraph rows={visible} root={root} selected={selectedId??''} choose={choose}/></div></>:
          <div className="table-wrap"><table><thead><tr><th>Узел</th><th>Роль</th><th>Родитель</th><th>Статус</th></tr></thead><tbody>
            {visible.map(row=><tr key={row.node.id} className={selectedId===row.node.id?'selected-row':''}>
              <td><button className="text-link table-button" onClick={()=>choose(row)}>{row.node.name}</button></td>
              <td>{kindNames[row.node.kind]}</td><td>{rows.find(x=>x.node.id===row.node.parentId)?.node.name||'—'}</td>
              <td><Badge ok={online(row)} text={nodeLabel(row)}/></td>
            </tr>)}
          </tbody></table></div>}
      </Panel>
      <Panel title={selected?.node.name??'Выберите узел'}>
        {selected&&<div className="stack node-details">
          <Badge ok={online(selected)} text={nodeLabel(selected)}/>
          {mode==="orchestra"&&context.value?.developer&&selected.node.kind==="global"&&<GlobalTimeweb key={selected.node.id} nodeId={selected.node.id}/>}
          <dl><dt>Роль</dt><dd>{kindNames[selected.node.kind]}</dd><dt>ID</dt><dd className="mono">{selectedId}</dd>
            <dt>Адрес панели</dt><dd>{selected.node.publicUrl||'—'}</dd>
            {selected.report?.seen&&<><dt>Последний отчёт</dt><dd>{new Date(selected.report.seen).toLocaleString('ru-RU')}</dd></>}
            {selected.report?.error&&<><dt>Ошибка</dt><dd>{selected.report.error}</dd></>}
          </dl>
          <div className="row-actions">
            {selected.node.kind!=='orchestra'&&selected.node.publicUrl&&<a className="btn secondary" href={selected.node.publicUrl} target="_blank" rel="noreferrer">Открыть панель <ArrowUpRight size={15}/></a>}
            {selected.node.kind==='agent'&&<Link className="btn secondary" to={'/commands?target='+selectedId}>Команды</Link>}
            {canEdit&&selected.node.kind!=='agent'&&<Link className="btn secondary" to={'/setup?node='+selectedId}>Конфигурация</Link>}
          </div>
          {canEdit&&<form className="stack" onSubmit={edit}>
            <label>Название<input value={name??selected.node.name} required onChange={event=>setName(event.target.value)} maxLength={64}/></label>
            <label className="check-row"><input type="checkbox" checked={enabled??selected.node.enabled} onChange={event=>setEnabled(event.target.checked)}/> Узел включён</label>
            <button className="btn" disabled={busy}>Сохранить изменения</button>
          </form>}
          {canEdit&&selected.node.kind==='global'&&<form className="stack node-members" onSubmit={event=>{event.preventDefault();void changeMember(member.trim(),true)}}>
            <label>Участники Global<input value={member} placeholder="Публичный ID из Коллектива" onChange={event=>setMember(event.target.value)}/></label>
            <button className="btn secondary" disabled={busy||!member.trim()}>Добавить участника</button>
            {selected.node.members?.map(id=><div className="row-actions" key={id}><span className="mono">{id}</span>
              <button type="button" className="text-link table-button" disabled={busy} onClick={()=>void changeMember(id,false)}>Отозвать</button></div>)}
          </form>}
          {message&&<span className="success" role="status">{message}</span>}
          {mode==='orchestra'&&<div className="node-history"><strong>Последние действия</strong>
            {audit.value?.items.map(item=><div className="line" key={item.id}><div><strong>{item.action}</strong><small>{item.detail||new Date(item.at).toLocaleString('ru-RU')}</small></div></div>)}
            {selected.node.kind!=='orchestra'&&<Link className="text-link" to={'/audit?target='+selectedId}>Открыть журнал</Link>}
          </div>}
        </div>}
      </Panel>
    </div>
  </>;
}
