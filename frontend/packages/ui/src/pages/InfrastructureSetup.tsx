import {useState,type FormEvent} from 'react';
import {useApp,useRemote} from '../app/context';
import {Panel,ErrorBanner} from '../components/primitives';

type Setup={
  targets:string[];
  localAvailable?:boolean;
  run?:{target:string;component:string;status:'running'|'done'|'failed';message:string};
};
const items=[
  ['monitoring','Prometheus и Grafana'],['metrics-source','Источник метрик Orchestra'],
  ['docker','Docker и Compose'],['lmstudio','LM Studio и модель'],['model-connect','Подключить модель к Orchestra'],
  ['postgres','PostgreSQL · Docker'],['postgres-native','PostgreSQL · обычная установка'],['prepare-postgres','Подготовить подключение новой панели к PostgreSQL'],['auth-postgres','Подключить Auth к PostgreSQL'],
] as const;

export function InfrastructureSetup({initial='monitoring',allowed,title='Развёртывание'}:{initial?:string;allowed?:readonly string[];title?:string}){
  const options=items.filter(([id])=>!allowed||allowed.includes(id));
  const {api}=useApp();
  const endpoint='/managed/infrastructure-setup';
  const state=useRemote(()=>api.request<Setup>(endpoint),[api],4000);
  const [component,setComponent]=useState(initial),[target,setTarget]=useState('');
  const [url,setUrl]=useState(''),[secret,setSecret]=useState('');
  const [bind,setBind]=useState('127.0.0.1'),[cidr,setCidr]=useState('127.0.0.1/32');
  const [role,setRole]=useState(initial==='prepare-postgres'?'orchestra':'panel-data'),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const needsSecret=component==='metrics-source'||component==='auth-postgres'||component==='monitoring'&&!!url.trim();
  const showSecret=needsSecret||['postgres','postgres-native','prepare-postgres'].includes(component);
  const candidates=[...(state.value?.localAvailable&&['monitoring','metrics-source','docker','lmstudio','postgres','postgres-native'].includes(component)?['@local']:[]),...(state.value?.targets??[])];
  const selected=candidates.includes(target)?target:candidates[0]??'';
  const targetName=(value:string)=>value==='@local'?'Этот VPS Orchestra':value;

  async function install(event:FormEvent){
    event.preventDefault();
    if(selected==='@local'&&['postgres','postgres-native'].includes(component)&&(bind!=='127.0.0.1'||cidr!=='127.0.0.1/32'||secret)){setError('Локальный установщик использует loopback и автоматически генерирует пароли. Для другой сети используйте доверенный SSH.');return;}
    if(selected==='@local'&&component==='monitoring'&&url.trim()){setError('Для локального мониторинга оставьте адрес пустым. Внешний источник настраивается через доверенный SSH.');return;}
    if(!selected||busy||!window.confirm(`Установить «${items.find(([id])=>id===component)?.[1]}» на ${targetName(selected)}?`))return;
    setBusy(true);setError('');
    try {
      await api.request(endpoint,{method:'POST',body:JSON.stringify({
        component,target:selected,confirmed:true,url,secret:showSecret?secret:'',bind,cidr,role,
      })});
      setSecret('');state.reload();
    } catch(e){setError(e instanceof Error?e.message:String(e));}
    finally {setBusy(false);}
  }

  if(!state.value)return state.error?<Panel title="Развёртывание инфраструктуры">
    <p>Развёртывание доступно главному разработчику.</p><ErrorBanner text={state.error}/>
  </Panel>:null;

  return <Panel title={title}>
    <form className="stack" onSubmit={install}>
      <label>Компонент<select value={component} onChange={e=>{setComponent(e.target.value);setSecret('');setUrl('');setRole(e.target.value==='prepare-postgres'?'orchestra':'panel-data')}}>
        {options.map(([id,label])=><option key={id} value={id}>{label}</option>)}</select></label>
      <label>Сервер<select value={selected} onChange={e=>setTarget(e.target.value)} disabled={!candidates.length}>
        {!candidates.length&&<option value="">Нет доступного сервера</option>}
        {candidates.map(host=><option key={host} value={host}>{targetName(host)}</option>)}</select></label>
      {!candidates.length&&<p className="footnote">{['monitoring','metrics-source','docker','lmstudio','postgres','postgres-native'].includes(component)
        ?'Установите актуальную версию Orchestra через встроенное обновление. Если привилегированный worker отсутствует, понадобится однократная настройка на VPS.'
        :'Добавьте доверенный SSH-сервер для этого компонента.'}</p>}
      {['monitoring','model-connect'].includes(component)&&<label>{component==='monitoring'?'HTTPS-адрес Orchestra':'Локальный адрес или SSH-туннель к LM Studio'}
        <input type="url" placeholder={component==='monitoring'?'Пусто для Orchestra на том же сервере':'http://127.0.0.1:1234'} value={url} onChange={e=>setUrl(e.target.value)}/>
      </label>}
      {['postgres','postgres-native','prepare-postgres','auth-postgres'].includes(component)&&<label>IPv4 PostgreSQL
        <input value={bind} onChange={e=>setBind(e.target.value)} required/>
      </label>}
      {['postgres','postgres-native'].includes(component)&&<label>CIDR клиентов PostgreSQL
        <input value={cidr} onChange={e=>setCidr(e.target.value)} required/>
      </label>}
      {component==='prepare-postgres'&&<><label>Роль новой панели<select value={['auth','orchestra','global','local','agent','audit'].includes(role)?role:'orchestra'} onChange={e=>setRole(e.target.value)}>{['auth','orchestra','global','local','agent','audit'].map(item=><option key={item} value={item}>{item}</option>)}</select></label><label className="check"><input type="checkbox" checked={url==='private-vpn'} onChange={e=>setUrl(e.target.checked?'private-vpn':'')}/>Удалённый IPv4 находится в защищённой частной сети/VPN</label><p>На машине панели подготовьте подключение до первого запуска. На том же VPS пароль можно оставить пустым; для удалённой базы нужен пароль роли. Для новой Orchestra подготовьте Auth и Orchestra отдельно. Существующие базы не переключаются автоматически.</p></>}
      {['lmstudio','model-connect'].includes(component)&&<label>Профиль модели<select value={role} onChange={e=>setRole(e.target.value)}>
        <option value="panel-data">Данные панелей · Qwen3 1.7B</option><option value="world">Мир · Qwen3 4B</option>
      </select></label>}
      {showSecret&&<label>{['postgres','postgres-native','prepare-postgres','auth-postgres'].includes(component)?'Пароль PostgreSQL (для установки — Auth; для подключения — выбранная роль)':'Ключ метрик'}
        <input type="password" autoComplete="off" pattern="[a-f0-9]{64}" placeholder="64 строчных hex-символа"
          required={needsSecret} value={secret} onChange={e=>setSecret(e.target.value)}/>
      </label>}
      <button className="btn" disabled={!selected||busy||state.loading||state.value.run?.status==='running'}>Подтвердить установку</button>
    </form>
    {state.value.run&&(!allowed||allowed.includes(state.value.run.component))&&<div role="status"><p>{state.value.run.target} · {{
      running:'Установка выполняется',done:'Установлено',failed:'Ошибка установки',
    }[state.value.run.status]}</p><pre className="output">{state.value.run.message}</pre></div>}
    <ErrorBanner text={error||state.error}/>
  </Panel>;
}

