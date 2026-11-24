import {useMemo,useState,type FormEvent} from 'react';
import {FileUp,Play,Square} from 'lucide-react';
import type {DispatchResult} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {ResultCard} from './CommandCenter';

type Click={x:number;y:number;atMs:number;button:'left'|'right'};
type Scenario={targetProcess:string;coordinateSpace?:'client'|'screen';clicks:Click[]};
function scenarioFrom(raw:string):Scenario{
  const data=JSON.parse(raw);
  const targetProcess=data.targetProcess;
  if(typeof targetProcess!=='string'||!/^[a-z0-9_.-]{2,80}$/i.test(targetProcess)||
    !Array.isArray(data.clicks)||data.clicks.length<1||data.clicks.length>200)
    throw Error('Укажите targetProcess и от 1 до 200 кликов.');
  // Older recorders wrote Click record members with PascalCase.
  const clicks=data.clicks.map((entry:Record<string,unknown>)=>({x:entry.x??entry.X,y:entry.y??entry.Y,atMs:entry.atMs??entry.AtMs,button:entry.button??entry.Button}));
  let last=0;
  for(const click of clicks){
    if(!Number.isSafeInteger(click.x)||!Number.isSafeInteger(click.y)||
      !Number.isSafeInteger(click.atMs)||click.atMs<last||click.atMs>110000||!['left','right'].includes(click.button))
      throw Error('Проверьте порядок, время, координаты и кнопки кликов.');
    last=click.atMs;
  }
  return {targetProcess,clicks,coordinateSpace:data.coordinateSpace==='client'?'client':'screen'};
}
export function AutomationPage(){
  const {api,mode}=useApp();
  const canSave=['orchestra','global','local'].includes(mode);
  const agents=useRemote(()=>api.agents(),[api],5000);
  const nodes=useRemote(()=>api.request<{node:{id:string;name:string}}[]>('/managed/topology'),[api]);
  const saved=useRemote(()=>canSave?api.records('automations'):Promise.resolve([]),[api,canSave]);
  const [targets,setTargets]=useState<string[]|null>(null);
  const [raw,setRaw]=useState('');
  const [name,setName]=useState('');
  const [delay,setDelay]=useState(5);
  const [confirmed,setConfirmed]=useState(false);
  const [running,setRunning]=useState(false);
  const [stopping,setStopping]=useState(false);
  const [results,setResults]=useState<Record<string,DispatchResult>>({});
  const [error,setError]=useState('');
  const [message,setMessage]=useState('');
  const selected=targets??(agents.value?.length===1&&agents.value[0].online?[agents.value[0].clientId]:[]);
  const parsed=useMemo(()=>{
    if(!raw.trim())return {scenario:null,error:''};
    try{return {scenario:scenarioFrom(raw),error:''}}catch(error){return {scenario:null,error:String(error)}}
  },[raw]);
  const maxMs=(parsed.scenario?.clicks.at(-1)?.atMs??0)+delay*1000;
  const agentName=(id:string)=>nodes.value?.find(x=>x.node.id===id)?.node.name||agents.value?.find(x=>x.clientId===id)?.host||id.slice(0,12);
  async function upload(file?:File){
    if(!file)return;
    if(file.size>100000){setError('Размер сценария должен быть меньше 100 КиБ.');return}
    setRaw(await file.text());setName(file.name.replace(/\.json$/i,''));setConfirmed(false);setError('');
  }
  async function save(){
    if(!parsed.scenario||!name.trim())return;
    try{await api.create('automations',{name:name.trim(),data:parsed.scenario});saved.reload();setMessage('Сценарий сохранён.')}
    catch(error){setError(String(error))}
  }
  async function play(event:FormEvent){
    event.preventDefault();
    if(!parsed.scenario||!confirmed||running||maxMs>110000)return;
    setRunning(true);setResults({});setError('');
    try{
      const requests=await Promise.allSettled(selected.map(async id=>{
        const result=await api.dispatch({targetId:id,commandId:'automation.play',rawArgs:JSON.stringify({...parsed.scenario,startDelayMs:delay*1000}),
          timeoutSeconds:Math.min(120,Math.max(15,Math.ceil(maxMs/1000)+5)),confirmedByHuman:true});
        setResults(current=>({...current,[id]:result}));
      }));
      const failed=requests.flatMap((x,index)=>x.status==='rejected'?[agentName(selected[index])+': '+String(x.reason)]:[]);
      if(failed.length)setError(failed.join(' · '));
    }finally{setRunning(false);setConfirmed(false)}
  }
  async function stop(){
    setStopping(true);
    try{
      const result=await Promise.allSettled(selected.map(id=>api.dispatch({targetId:id,commandId:'automation.stop',timeoutSeconds:10})));
      const failed=result.filter(item=>item.status==='rejected'||!item.value.success).length;
      setMessage(failed?'Часть компьютеров не подтвердила остановку. На целевом ПК доступна клавиша Esc.':'Запрос остановки передан.');
    }finally{setStopping(false)}
  }
  return <>
    <Header title="Автоматизация" description="Запись, проверка и запуск сценариев кликов на Windows"/>
    <ErrorBanner text={error||agents.error}/>
    <div className="two-col">
      <Panel title="Сценарий">
        <div className="stack">
          <p className="footnote">Запишите клики в AuditDesktop и загрузите JSON. Для воспроизведения включите разрешение на выбранном компьютере и держите нужное приложение на переднем плане.</p>
          <label className="file-pick"><FileUp size={19}/> Загрузить сценарий<input type="file" accept=".json,application/json" disabled={running} onChange={event=>void upload(event.target.files?.[0])}/></label>
          {saved.value?.length? <label>Сохранённые сценарии<select defaultValue="" disabled={running} onChange={event=>{const item=saved.value?.find(row=>row.id===event.target.value);if(item){setRaw(JSON.stringify(item.data,null,2));setName(item.name);setConfirmed(false)}}}>
            <option value="">Выберите сценарий</option>{saved.value.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}
          </select></label>:null}
          <details><summary>Просмотреть или изменить JSON</summary><label>Содержимое<textarea rows={8} value={raw} disabled={running} onChange={event=>{setRaw(event.target.value);setConfirmed(false)}}/></label></details>
          {parsed.error&&<ErrorBanner text={parsed.error}/>}
          {parsed.scenario&&<div className="scenario-preview"><strong>{parsed.scenario.targetProcess}</strong>
            <span>{parsed.scenario.clicks.length} кликов · {((parsed.scenario.clicks.at(-1)?.atMs??0)/1000).toFixed(1)} с</span>
            <div className="table-wrap"><table><thead><tr><th>Время</th><th>Точка</th><th>Кнопка</th></tr></thead>
              <tbody>{parsed.scenario.clicks.slice(0,12).map((click,index)=><tr key={index}><td>{click.atMs} мс</td><td>{click.x}, {click.y}</td><td>{click.button==='left'?'Левая':'Правая'}</td></tr>)}</tbody>
            </table></div>
          </div>}
          {canSave&&parsed.scenario&&<div className="form-pair"><label>Название<input value={name} maxLength={160} onChange={event=>setName(event.target.value)}/></label>
            <button className="btn secondary" disabled={!name.trim()} onClick={()=>void save()}>Сохранить сценарий</button></div>}
        </div>
      </Panel>
      <Panel title="Запуск">
        <form className="stack" onSubmit={play}>
          <fieldset className="automation-targets"><legend>Компьютеры</legend>
            {agents.value?.map(agent=><label className="check-row" key={agent.clientId}><input type="checkbox" checked={selected.includes(agent.clientId)}
              disabled={!agent.online||running} onChange={event=>{setTargets(event.target.checked?[...selected,agent.clientId]:selected.filter(id=>id!==agent.clientId));setConfirmed(false)}}/>
              {agentName(agent.clientId)} <small>{agent.online?'в сети':'офлайн'}</small></label>)}
            {!agents.value?.length&&<p className="footnote">Ожидаем подключённых исполнителей.</p>}
          </fieldset>
          <label>Отсчёт перед запуском, секунд<input type="number" min={0} max={15} required value={delay} disabled={running} onChange={event=>setDelay(Number(event.target.value))}/></label>
          {maxMs>110000&&<p className="footnote">Уменьшите отсчёт или укоротите сценарий до 110 секунд.</p>}
          <label className="check-row"><input type="checkbox" checked={confirmed} disabled={running} onChange={event=>setConfirmed(event.target.checked)}/> Подтверждаю запуск на выбранных ПК</label>
          <div className="row-actions"><button className="btn" disabled={running||!confirmed||!parsed.scenario||!selected.length||selected.length>10||maxMs>110000}><Play size={16}/>{running?'Выполняется…':'Воспроизвести'}</button>
            {running&&<button className="btn secondary" type="button" disabled={stopping} onClick={()=>void stop()}><Square size={15}/> Остановить</button>}</div>
          <p className="footnote">Аварийная остановка на целевом ПК — клавиша Esc.</p>
          {message&&<span className="success" role="status">{message}</span>}
        </form>
        {Object.entries(results).map(([id,result])=><div key={id}><h3>{agentName(id)}</h3><ResultCard result={result}/></div>)}
      </Panel>
    </div>
  </>;
}
