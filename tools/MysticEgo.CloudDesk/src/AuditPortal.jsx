import React,{useState} from 'react';

const dnsName=/^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{1,62}$/;

export default function AuditPortal({monitor,servers,validTarget,runSSH,busy}) {
  const [mode,setMode]=useState('embedded');
  const [host,setHost]=useState('');
  const [port,setPort]=useState('22');
  const [name,setName]=useState('');
  const [result,setResult]=useState(null);
  const central=monitor.origin ? new URL(monitor.origin).hostname : '';
  const domain=mode==='embedded' ? central : name.trim().toLowerCase();
  const target={host:host.trim(),port,user:'root'};
  const ready=monitor.registry.authenticated && validTarget(target) &&
    domain.length<=253 && dnsName.test(domain) && (mode==='embedded' || domain!==central);

  function deploy() {
    if(!ready || !confirm(`Развернуть Audit на ${target.host} по адресу https://${domain}/audit-portal/?`))return;
    setResult(null);
    runSSH('deploy.auditPortal',{...target,mode,domain,origin:monitor.origin,confirm:'DEPLOY_AUDIT'},setResult);
  }

  return <section>
    <h2>Веб-панель Audit</h2>
    <p>Регистрация и вход аудиторов работают через Auth подключённой Orchestra.</p>
    <div className="fields">
      <label>Размещение<select value={mode} onChange={e=>{setMode(e.target.value);setResult(null)}}>
        <option value="embedded">В составе Orchestra</option>
        <option value="site">На отдельном домене</option>
      </select></label>
      <label>Сервер Timeweb<select value={servers.some(server=>server.ip===host)?host:''}
        onChange={e=>{setHost(e.target.value);setPort('22')}}>
        <option value="">Указать SSH-адрес вручную</option>
        {servers.map(server=><option key={server.id} value={server.ip}>{server.name} · {server.ip}</option>)}
      </select></label>
      <label>SSH-адрес<input value={host} onChange={e=>setHost(e.target.value)}/></label>
      <label>SSH-порт<input inputMode="numeric" value={port} onChange={e=>setPort(e.target.value)}/></label>
      {mode==='site'&&<label>Домен Audit<input value={name} placeholder="audit.example.org"
        onChange={e=>setName(e.target.value)}/></label>}
    </div>
    <p>{central?'Orchestra · '+central:'Подключи Orchestra под главным разработчиком.'}</p>
    {mode==='site'&&<p>Направь DNS домена Audit на выбранный VPS. На VPS нужны запущенный Caddy и порты 80 и 443.</p>}
    <div className="actions"><button className="primary" disabled={busy||!ready} onClick={deploy}>Развернуть Audit</button></div>
    {result&&<p role="status">Адрес панели · <code>{result.url}</code></p>}
  </section>;
}
