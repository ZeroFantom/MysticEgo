import React,{useState} from 'react';

const components=[
  ['monitoring','Prometheus и Grafana'],['metrics-source','Источник метрик Orchestra'],['docker','Docker и Compose'],['web-runner','Подготовить управление из Orchestra'],
  ['lmstudio','LM Studio и модель'],['model-connect','Подключить модель к Orchestra'],['postgres','PostgreSQL · Docker'],['postgres-native','PostgreSQL · обычная установка'],['prepare-postgres','Подготовить подключение новой панели к PostgreSQL'],['auth-postgres','Подключить Auth к PostgreSQL'],
];
export default function InfrastructureSetup({servers,validTarget,runSSH,busy}){
  const [component,setComponent]=useState('monitoring'),[host,setHost]=useState(''),[port,setPort]=useState('22');
  const [url,setUrl]=useState(''),[secret,setSecret]=useState(''),[bind,setBind]=useState('127.0.0.1');
  const [cidr,setCidr]=useState('127.0.0.1/32'),[role,setRole]=useState('panel-data');
  const needsKey=component==='metrics-source'||component==='auth-postgres'||component==='monitoring'&&!!url.trim();
  const showKey=needsKey||['postgres','postgres-native','prepare-postgres'].includes(component);
  const target={host:host.trim(),port,user:'root'};

  function install(event){
    event.preventDefault();
    if(!validTarget(target)||busy||!confirm(`Установить «${components.find(([id])=>id===component)?.[1]}» на ${host}?`))return;
    setSecret('');
    runSSH('deploy.infrastructure',{...target,component,url,secret:showKey?secret:'',bind,cidr,role,
      domain:component,confirm:'PROVISION_INFRASTRUCTURE'});
  }

  return <section>
    <h2>Развёртывание инфраструктуры</h2>
    <form onSubmit={install}>
      <div className="fields">
        <label>Сервер Timeweb<select value={servers.some(server=>server.ip===host)?host:''}
          onChange={e=>{setHost(e.target.value);setPort('22')}}>
          <option value="">Указать SSH-адрес вручную</option>
          {servers.map(server=><option key={server.id} value={server.ip}>{server.name} · {server.ip}</option>)}
        </select></label>
        <label>SSH-адрес<input value={host} onChange={e=>setHost(e.target.value)} required/></label>
        <label>SSH-порт<input inputMode="numeric" value={port} onChange={e=>setPort(e.target.value)} required/></label>
        <label>Компонент<select value={component} onChange={e=>{setComponent(e.target.value);setSecret('');setUrl('');setRole(e.target.value==='prepare-postgres'?'orchestra':'panel-data')}}>
          {components.map(([id,label])=><option key={id} value={id}>{label}</option>)}
        </select></label>
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
        {showKey&&<label>{['postgres','postgres-native','prepare-postgres','auth-postgres'].includes(component)?'Пароль PostgreSQL (для установки — Auth; для подключения — выбранная роль)':'Ключ метрик'}
          <input type="password" value={secret} onChange={e=>setSecret(e.target.value)} autoComplete="off"
            pattern="[a-f0-9]{64}" placeholder="64 строчных hex-символа" required={needsKey}/>
        </label>}
      </div>
      <div className="actions"><button className="primary" disabled={busy||!validTarget(target)}>Подтвердить установку</button></div>
    </form>
  </section>;
}
