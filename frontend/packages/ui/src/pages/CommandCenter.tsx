import {useMemo,useState,type FormEvent} from 'react';
import {useSearchParams} from 'react-router-dom';
import {Activity,Bookmark,CheckCircle2,CircleAlert,Clipboard,Play,Search,Sparkles} from 'lucide-react';
import type {Capability,DispatchResult,Parameter} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner,Badge} from '../components/primitives';

const presetDefaults=[
  {id:'computer',name:'Сведения о ПК',commandId:'system.info',rawArgs:''},
  {id:'health',name:'Проверка ресурсов',commandId:'system.details',rawArgs:''},
  {id:'integrations',name:'Проверка интеграций',commandId:'integrations.status',rawArgs:''},
  {id:'network',name:'Сетевые адаптеры',commandId:'network.interfaces',rawArgs:''},
  {id:'color',name:'Фиолетовая подсветка',commandId:'rgb.set',rawArgs:'#8B5CF6'}
];
const sensitive=new Set(['process.terminate','app.launch','system.power','automation.play','rgb.set']);
const resultLabels:Record<string,string>={host:'Компьютер',os:'Операционная система',version:'Версия',processors:'Процессоры',uptime:'Время работы',
  platform:'Платформа',name:'Название',available:'Доступен',total:'Всего',free:'Свободно',used:'Используется',duration:'Длительность',clicks:'Клики'};

function JsonCards({value,depth=0}:{value:unknown;depth?:number}){
  if(value===null||value===undefined)return <span className="muted">—</span>;
  if(typeof value==='boolean')return <Badge ok={value} text={value?'Да':'Нет'}/>;
  if(typeof value!=='object')return <span className="json-leaf">{String(value)}</span>;
  if(depth>=5)return <span className="muted">Подробнее в JSON</span>;
  if(Array.isArray(value)){
    if(!value.length)return <span className="muted">Список пуст</span>;
    const visible=value.slice(0,40);
    return <div className="json-array">{visible.map((item,index)=><div className="json-item" key={index}><JsonCards value={item} depth={depth+1}/></div>)}
      {visible.length<value.length&&<p className="footnote">Показано {visible.length} из {value.length}. Полный список доступен в JSON.</p>}</div>;
  }
  return <dl className="json-fields">{Object.entries(value).map(([key,child])=><div key={key}><dt>{resultLabels[key]??key}</dt><dd><JsonCards value={child} depth={depth+1}/></dd></div>)}</dl>;
}

export function ResultCard({result}:{result:DispatchResult}){
  const [copy,setCopy]=useState(false);
  const formatted=JSON.stringify(result,null,2);
  const Icon=result.success?CheckCircle2:CircleAlert;
  return <section className={'result-card '+(result.success?'':'result-error')} aria-label="Результат команды">
    <div className="result-heading"><Icon size={21}/><div><strong>{result.success?'Выполнено':'Ошибка выполнения'}</strong>
      <small>{[result.commandId,result.duration].filter(Boolean).join(' · ')}</small></div>
      <button type="button" className="icon-btn" aria-label="Копировать JSON" title={copy?'Скопировано':'Копировать JSON'} onClick={async()=>{try{await navigator.clipboard.writeText(formatted);setCopy(true)}catch{setCopy(false)}}}>{copy?<CheckCircle2 size={17}/>:<Clipboard size={17}/>}</button></div>
    {result.output&&<p className="result-text">{result.output}</p>}
    {result.errorMessage&&<p className="result-text">{result.errorMessage}</p>}
    {result.errorCode&&<code>{result.errorCode}</code>}
    {result.structuredOutput!==null&&result.structuredOutput!==undefined&&<JsonCards value={result.structuredOutput}/>}
    <details className="result-json"><summary>Исходный JSON</summary><pre className="output">{formatted}</pre></details>
  </section>;
}

function Argument({param,value,onChange}:{param:Parameter;value:string;onChange:(value:string)=>void}){
  const label=param.description||param.name;
  return <label>{label}{
    param.values?.length?<select value={value} required={param.required} onChange={event=>onChange(event.target.value)}>
      <option value="">Выберите значение</option>{param.values.map(item=><option key={item} value={item}>{item}</option>)}
    </select>:param.format==='color'?<div className="color-input">
      <input type="color" aria-label="Палитра" value={/^#[\da-f]{6}$/i.test(value)?value:'#8b5cf6'} onChange={event=>onChange(event.target.value)}/>
      <input type="text" aria-label="HEX-цвет" value={value} required={param.required} pattern="#?[0-9A-Fa-f]{6}" onChange={event=>onChange(event.target.value)}/>
    </div>:param.format==='json'?<textarea value={value} rows={5} required={param.required} onChange={event=>onChange(event.target.value)} placeholder={param.example}/>:
      param.clrType==='System.Boolean'?<select value={value} onChange={event=>onChange(event.target.value)}><option value="">По умолчанию</option><option value="true">Да</option><option value="false">Нет</option></select>:
      <input type={/Int|Double|Decimal|Single/.test(param.clrType)?'number':'text'} step="any" value={value} required={param.required} onChange={event=>onChange(event.target.value)} placeholder={param.example}/>
  }</label>;
}

export function CommandCenter(){
  const {api,mode}=useApp();
  const [params]=useSearchParams();
  const agents=useRemote(()=>api.agents(),[api],5000);
  const nodes=useRemote(()=>api.request<{node:{id:string;name:string}}[]>('/managed/topology'),[api]);
  const catalog=useRemote(()=>api.request<Capability[]>('/commands/catalog'),[api]);
  const saved=useRemote(()=>['agent','audit'].includes(mode)?Promise.resolve([]):api.records('presets'),[api,mode]);
  const [target,setTarget]=useState(params.get('target')??'');
  const effective=target||(agents.value?.length===1?agents.value[0].clientId:'');
  const [id,setId]=useState('system.info');
  const [query,setQuery]=useState('');
  const [values,setValues]=useState<Record<string,string>>({});
  const [presetName,setPresetName]=useState('');
  const [result,setResult]=useState<DispatchResult|null>(null);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [confirmed,setConfirmed]=useState(false);
  const capabilities=useRemote(()=>effective?api.capabilities(effective):Promise.resolve([]),[api,effective]);
  const commands=useMemo(()=>{
    const byId=new Map((catalog.value??[]).map(command=>[command.id,command]));
    for(const command of capabilities.value??[]){
      const source=byId.get(command.id);
      byId.set(command.id,{...command,parameters:command.parameters?.length?command.parameters:source?.parameters??[]})
    }
    return Array.from(byId.values()).filter(x=>!x.id.startsWith('automation.'));
  },[catalog.value,capabilities.value]);
  const current=commands.find(command=>command.id===id);
  const fields=current?.parameters??[];
  const supported=capabilities.value?.some(command=>command.id===id)===true;
  const needsConfirm=sensitive.has(id)||current?.tags?.includes('approval-required')===true;
  const presets=[...presetDefaults,...(saved.value??[]).flatMap(row=>{
    const data=row.data as {commandId?:string;rawArgs?:string}|null;
    return data?.commandId?[{id:row.id,name:row.name,commandId:data.commandId,rawArgs:data.rawArgs??''}]:[];
  })];
  function pick(commandId:string,raw?:string){
    setId(commandId);setValues(raw===undefined?{}:{_raw:raw});setResult(null);setConfirmed(false);setError('');
  }
  function argumentsFor(){
    const inputs=fields.map(field=>[field,values[field.name]??values._raw??field.default??''] as const);
    const namedArgs=Object.fromEntries(inputs.filter(([,value])=>value!=='').map(([field,value])=>
      [field.name,/Int|Double|Decimal|Single/.test(field.clrType)?Number(value):field.clrType==='System.Boolean'?value==='true':value]));
    const rawArgs=inputs.length>1?JSON.stringify(namedArgs):inputs[0]?.[1]??values._raw??'';
    return {rawArgs,namedArgs};
  }
  async function execute(event:FormEvent){
    event.preventDefault();setError('');setResult(null);setBusy(true);
    try{setResult(await api.dispatch({targetId:effective,commandId:id,...argumentsFor(),confirmedByHuman:confirmed,timeoutSeconds:45}))}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function save(){
    setError('');
    try{await api.create('presets',{name:presetName.trim(),data:{commandId:id,rawArgs:argumentsFor().rawArgs}});setPresetName('');saved.reload()}
    catch(error){setError(String(error))}
  }
  return <><Header title="Быстрые команды" description="Выберите компьютер и действие из каталога возможностей."/>
    <ErrorBanner text={error||agents.error||catalog.error}/>
    <div className="command-layout orchestra-commands">
      <div>
        <Panel title="Каталог команд" aside={<Sparkles size={18}/>}>
          <label className="search"><Search size={16}/><input value={query} placeholder="Найти команду" onChange={event=>setQuery(event.target.value)}/></label>
          <div className="command-catalog">{commands.filter(command=>[command.id,command.displayName,command.description].join(' ').toLowerCase().includes(query.toLowerCase())).map(command=>
            <button type="button" className={'example-card '+(command.id===id?'selected':'')} key={command.id} onClick={()=>pick(command.id)}>
              <span className="example-icon"><Activity size={16}/></span><span><strong>{command.displayName||command.id}</strong><small>{command.id}</small></span>
            </button>)}</div>
        </Panel>
        <Panel title="Пресеты"><div className="preset-buttons">{presets.map(preset=><button className="btn secondary" key={preset.id} onClick={()=>pick(preset.commandId,preset.rawArgs)}>
          <Bookmark size={14}/>{preset.name}</button>)}</div></Panel>
      </div>
      <Panel title={current?.displayName||'Параметры команды'}>
        <form className="stack" onSubmit={execute}>
          <label>Исполнитель<select value={effective} onChange={event=>{setTarget(event.target.value);setConfirmed(false);setResult(null)}} required>
            <option value="">Выберите компьютер</option>{agents.value?.map(agent=><option key={agent.clientId} value={agent.clientId}>
              {(nodes.value?.find(row=>row.node.id===agent.clientId)?.node.name||agent.host||agent.clientId.slice(0,12))+' · '+(agent.online?'в сети':'офлайн')}</option>)}
          </select></label>
          {current?.description&&<p className="footnote">{current.description}</p>}
          {fields.map(field=><Argument key={field.name} param={field} value={values[field.name]??values._raw??field.default??''}
            onChange={value=>setValues(current=>({...current,[field.name]:value}))}/>)}
          {current&&fields.length===0&&<p className="footnote">Команда запускается без аргументов.</p>}
          {effective&&!capabilities.loading&&!supported&&<p className="footnote">У выбранного исполнителя эта команда недоступна.</p>}
          {capabilities.error&&<ErrorBanner text={capabilities.error}/>}
          {needsConfirm&&<label className="check-row"><input type="checkbox" checked={confirmed} onChange={event=>setConfirmed(event.target.checked)}/> Подтверждаю изменение на выбранном компьютере</label>}
          <button className="btn" disabled={busy||!effective||!supported||needsConfirm&&!confirmed}><Play size={16}/>{busy?'Выполняется…':'Выполнить'}</button>
        </form>
        {!['agent','audit'].includes(mode)&&current&&<details className="command-preset-save"><summary>Сохранить как пресет</summary>
          <div className="row-actions"><input value={presetName} maxLength={160} onChange={event=>setPresetName(event.target.value)} placeholder="Название пресета"/>
            <button className="btn secondary" disabled={!presetName.trim()} onClick={()=>void save()}>Сохранить</button></div>
        </details>}
        {result&&<ResultCard key={result.executionId} result={result}/>}
      </Panel>
    </div>
  </>;
}
