import {DashboardHero} from '../components/DashboardHero';
import {useEffect,useState,type FormEvent} from 'react';
import {Link,useSearchParams} from 'react-router-dom';
import {ArrowRight,Check,Cloud,Download,HardDrive,House,Server,Terminal} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner,Badge} from '../components/primitives';
import {useNetwork,kindNames,online,type Kind,type NodeStatus,type NetworkContext} from './Network';

type Application={id:string;name:string;applicant:string;description:string;status:'pending'|'approved'|'rejected';decisionNote?:string;submittedAt:string;address?:string;contact?:string;website?:string};
type Job={id:string;nodeId:string;name:string;status:string;stage:string;at:string};
type Preflight={ssh:boolean;docker:boolean;detail:string;checkedAt:string};
type Target={host:string;user:string;port:number;os:'linux'|'windows'};
const ports={global:5100,local:5200,agent:5300,audit:5350,orchestra:5500};
const roleNames={global:'Тестовый клуб',local:'Основная площадка',agent:'Игровой ПК',audit:'Личный ПК',orchestra:'Оркестр'};
const jobNames:Record<string,string>={queued:'В очереди',running:'В процессе',succeeded:'Готово',failed:'Ошибка',interrupted:'Прервано'};
const applicationNames={pending:'На рассмотрении',approved:'Одобрена',rejected:'Отклонена'};

export function OrganizationRequests({overview=false}:{overview?:boolean}){
  const {api,mode}=useApp();
  const data=useRemote(()=>api.request<Application[]>('/managed/applications'),[api],8000);
  const {topology}=useNetwork();
  const [name,setName]=useState('');
  const [description,setDescription]=useState('');
  const [address,setAddress]=useState(''),[contact,setContact]=useState(''),[website,setWebsite]=useState('');
  const [message,setMessage]=useState('');
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const globals=topology.value?.filter(row=>row.node.kind==='global')??[];
  async function send(event:FormEvent){
    event.preventDefault();setError('');setMessage('');setBusy(true);
    try{await api.request('/managed/applications',{method:'POST',body:JSON.stringify({name,description,address,contact,website})});setName('');setDescription('');data.reload();setMessage('Заявка отправлена в Оркестр.')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  return <>{overview&&<DashboardHero title="Личное пространство MysticEgo" eyebrow="AUDIT · ОБЗОР" description="Личный аккаунт и доступ к организациям MysticEgo." action={<Link className="btn secondary" to="/profile">Мой профиль</Link>}/>}
    {!overview&&<Header title="Организации" description="Подайте заявку на Global и откройте панель своей организации."/>}
    <ErrorBanner text={error||data.error}/>
    <div className="two-col">
      <Panel title="Мои Global">
        {globals.length===0?<p className="footnote">Здесь появятся организации, к которым вы получили доступ.</p>:globals.map(row=><div className="line" key={row.node.id}><Cloud size={19}/>
          <div className="grow"><strong>{row.node.name}</strong><small>{row.sites} Local · {row.people} участников</small></div>
          {online(row)&&row.node.publicUrl?<a className="btn secondary" href={row.node.publicUrl} target="_blank" rel="noreferrer">Открыть <ArrowRight size={15}/></a>:<Badge ok={false} text="Подготовка"/>}</div>)}
      </Panel>
      {mode==='audit'&&<Panel title="Заявка на создание Global">
        <form className="stack" onSubmit={send}>
          <label>Название организации<input required maxLength={64} value={name} onChange={event=>setName(event.target.value)}/></label>
          <label>Адрес офиса<input value={address} maxLength={250} onChange={event=>setAddress(event.target.value)}/></label>
          <div className="form-pair"><label>Контакт<input value={contact} maxLength={150} onChange={event=>setContact(event.target.value)}/></label>
          <label>Сайт<input type="url" value={website} maxLength={240} onChange={event=>setWebsite(event.target.value)}/></label></div>
          <label>Чем занимается организация<textarea rows={3} maxLength={1500} value={description} onChange={event=>setDescription(event.target.value)}/></label>
          <button className="btn" disabled={busy}>Отправить в Оркестр</button>{message&&<span className="success" role="status">{message}</span>}
        </form>
      </Panel>}
    </div>
    <Panel title="Мои заявки">{data.value?.length?data.value.map(item=><div className="line" key={item.id}>
      <div className="grow"><strong>{item.name}</strong><small>{item.decisionNote||new Date(item.submittedAt).toLocaleDateString('ru-RU')}</small></div>
      <Badge ok={item.status==='approved'} text={applicationNames[item.status]}/>
    </div>):<p className="footnote">{data.loading?'Загружаем…':'Заявок пока нет.'}</p>}</Panel>
  </>;
}

function RemoteLaunch({selected,refresh}:{selected:NodeStatus;refresh:()=>void}){
  const {api,session}=useApp();
  const {node}=selected;
  const [target,setTarget]=useState<Target>({host:node.publicUrl?new URL(node.publicUrl).hostname:'',user:'',port:22,os:'linux'});
  const [monitor,setMonitor]=useState(false);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('');
  const [error,setError]=useState('');
  const isComplete=Boolean(target.host&&target.user&&target.port>0);
  const check=useRemote(()=>monitor&&isComplete?api.request<Preflight>(`/managed/nodes/${node.id}/preflight`,{method:'POST',body:JSON.stringify(target)}):Promise.resolve(null),
    [api,node.id,target,monitor,isComplete],monitor?8000:0);
  async function download(){
    setError('');setBusy(true);
    try{
      const response=await fetch(session.url.replace(/\/$/,'')+`/api/v1/managed/nodes/${node.id}/profile`,{headers:{Authorization:'Bearer '+session.token}});
      if(!response.ok)throw new Error('Профиль недоступен, HTTP '+response.status);
      const file=URL.createObjectURL(await response.blob());
      const link=document.createElement('a');link.href=file;link.download=`mysticego-${node.kind}-${node.id}.json`;link.click();window.setTimeout(()=>URL.revokeObjectURL(file),5000);
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function deploy(event:FormEvent){
    event.preventDefault();setError('');setBusy(true);
    try{await api.request(`/managed/nodes/${node.id}/deploy`,{method:'POST',body:JSON.stringify(target)});setMessage('Задание принято. Ход запуска отображается ниже.');refresh()}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  function update<K extends keyof Target>(key:K,value:Target[K]){
    setTarget(current=>({...current,[key]:value}));setMonitor(false);setMessage('');
  }
  return <Panel title={'Запуск · '+node.name} aside={<Badge ok={online(selected)} text={online(selected)?'В сети':'Ожидает отчёта'}/>}>
    <ErrorBanner text={error||check.error}/>
    <div className="deployment-methods">
      {node.mode==='native'&&<div className="stack install-option">
        <h3>{node.kind==='local'?'Через LocalDesktop':'Профиль для нативного запуска'}</h3><p className="footnote">{node.kind==='local'?
          'На компьютере площадки откройте LocalDesktop, укажите HTTPS-адрес этого Global и выберите площадку. Адрес Local должен быть доступен из сети клуба.':
          'Передайте конфигурацию на целевой компьютер для нативного запуска.'}</p>
        <button className="btn secondary" disabled={busy} onClick={()=>void download()}><Download size={17}/> Скачать конфигурацию узла</button>
      </div>}
      {node.kind==='global'&&<form className="stack install-option" onSubmit={deploy}>
        <h3><Terminal size={17}/> Через SSH</h3>
        <div className="form-pair"><label>Хост<input value={target.host} required onChange={event=>update('host',event.target.value)}/></label>
          <label>Порт<input type="number" min={1} max={65535} required value={target.port||''} onChange={event=>update('port',Number(event.target.value))}/></label></div>
        <div className="form-pair"><label>Пользователь<input value={target.user} required onChange={event=>update('user',event.target.value)}/></label>
          <label>Система<select value={target.os} onChange={event=>update('os',event.target.value as Target['os'])}>
            <option value="linux">Linux x64</option>{node.mode==='native'&&<option value="windows">Windows x64</option>}</select></label></div>
        <small className="footnote">Используются SSH-ключ Оркестра и отпечаток сервера из known_hosts.</small>
        <div className="row-actions"><button type="button" className="btn secondary" disabled={!isComplete} onClick={()=>setMonitor(value=>!value)}>
          {monitor?'Остановить проверку':'Проверять SSH и Docker'}</button>
          <button className="btn" disabled={busy||!check.value?.ssh||node.mode==='docker'&&!check.value?.docker}>Развернуть</button>
        </div>
        {monitor&&isComplete&&<div className="row-actions" role="status">
          <Badge ok={check.value?.ssh===true} text={check.loading?'Проверяем SSH…':check.value?.ssh?'SSH отвечает':'SSH недоступен'}/>
          <Badge ok={check.value?.docker===true} text={check.loading?'Docker…':check.value?.docker?'Docker доступен':'Docker не обнаружен'}/>
          {check.value&&<p className="footnote">{check.value.detail}</p>}
        </div>}
        {message&&<span className="success">{message}</span>}
      </form>}
    </div>
  </Panel>;
}

export function Deployment(){
  const {api,mode}=useApp();
  const {context,topology}=useNetwork();
  const [params,setParams]=useSearchParams();
  const applications=useRemote(()=>mode==='orchestra'?api.request<Application[]>('/managed/applications'):Promise.resolve([]),[api,mode],8000);
  const jobs=useRemote(()=>api.request<Job[]>('/managed/jobs'),[api],2500);
  const runtime=useRemote(()=>api.request<{nodeId:string}>('/public/runtime'),[api]);
  const defaultKind:Kind=mode==='global'?'local':mode==='local'?'agent':'global';
  const [kind,setKind]=useState<Kind>(defaultKind);
  const [name,setName]=useState(roleNames[defaultKind]);
  const [application,setApplication]=useState(params.get('application')??'');
  const [siteAddress,setSiteAddress]=useState(''),[siteDescription,setSiteDescription]=useState('');
  const [method,setMethod]=useState<'native'|'docker'>('native');
  const [host,setHost]=useState('');
  const [owner,setOwner]=useState('');
  const [secure,setSecure]=useState(mode!=='global');
  const [port,setPort]=useState(ports[defaultKind]);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const rows=topology.value??[];
  const selected=rows.find(row=>row.node.id===params.get('node'));
  const parents=rows.filter(row=>row.node.kind===(kind==='local'?'global':'local')&&row.node.enabled&&
    (mode==='orchestra'||row.node.id===runtime.value?.nodeId));
  const [parent,setParent]=useState('');
  const parentChoice=parent||parents.find(row=>row.node.id===runtime.value?.nodeId)?.node.id||parents[0]?.node.id||'';
  const developer=context.value?.developer===true;
  const chosenApplication=applications.value?.find(item=>item.id===application);
  const pending=applications.value?.filter(item=>item.status==='pending')??[];
  const approved=applications.value?.filter(item=>item.status==='approved'&&!rows.some(row=>row.node.applicationId===item.id))??[];
  useEffect(()=>{
    if(context.value?.lan)setSecure(false);
  },[context.value?.lan]);
  function chooseKind(next:Kind){
    setKind(next);setPort(ports[next]);setName(roleNames[next]);setApplication('');setParent('');
  }
  async function create(event:FormEvent){
    event.preventDefault();setError('');setBusy(true);
    try{
      const url=new URL(`${secure?'https':'http'}://${host.trim()}`);
      url.port=String(port);
      if(url.pathname!=='/'||url.search||url.hash||url.username)throw new Error('Укажите только имя сервера или IP в поле адреса.');
      const node=await api.request<{id:string}>('/managed/nodes',{method:'POST',body:JSON.stringify({
        kind,name,mode:method,publicUrl:url.origin,parentId:kind==='local'||kind==='agent'?parentChoice:null,applicationId:application||null,
        ownerId:owner.trim()||null,address:siteAddress,description:siteDescription,allowLanHttp:!secure&&context.value?.lan===true
      })});
      setParams({node:node.id});topology.reload();applications.reload();
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function decide(id:string,approved:boolean){
    setBusy(true);setError('');
    try{const item=await api.request<Application>(`/managed/applications/${id}/decision`,{method:'POST',body:JSON.stringify({approved})});
      if(approved){chooseKind('global');setApplication(item.id);setName(item.name);setParams({application:item.id})}
      applications.reload();
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  const launchable=rows.filter(row=>(developer||row.node.ownerId===context.value?.actor)&&row.node.enabled&&
    (mode==='orchestra'||row.node.parentId===runtime.value?.nodeId));
  if(mode==='local')return <ClubDistribution/>;
  return <>
    <Header title={mode==='orchestra'?'Развёртывание Global':'Площадки Local'} description={mode==='orchestra'?'Заявки организаций и запуск Global на отдельном сервере.':'Сформируйте конфигурацию клуба для его сервера или ПК администратора.'}/>
    <ErrorBanner text={error||context.error||topology.error}/>
    {mode==='orchestra'&&developer&&pending.length>0&&<Panel title="Заявки на Global">
      {pending.map(item=><div key={item.id} className="line request-row"><div className="grow"><strong>{item.name}</strong><small>{item.applicant} · {item.description}</small></div>
        <button className="btn secondary" disabled={busy} onClick={()=>void decide(item.id,false)}>Отклонить</button>
        <button className="btn" disabled={busy} onClick={()=>void decide(item.id,true)}><Check size={15}/> Одобрить</button></div>)}
    </Panel>}
    {chosenApplication&&<Panel title={chosenApplication.name} aside={<Badge ok={true} text="Одобрена"/>}>
      <dl className="request-details">
        <div><dt>Создатель</dt><dd>{chosenApplication.applicant}</dd></div><div><dt>Адрес</dt><dd>{chosenApplication.address||'—'}</dd></div>
        <div><dt>Контакт</dt><dd>{chosenApplication.contact||'—'}</dd></div><div><dt>Сайт</dt><dd>{chosenApplication.website||'—'}</dd></div>
      </dl><p>{chosenApplication.description}</p><p className="footnote">После запуска Global появится в Audit создателя.</p>
    </Panel>}
    <div className="deployment-grid">
      {(mode!=='orchestra'||(context.value?.owner??developer))&&<Panel title="Новый узел">
        <form className="stack" onSubmit={create}>
          {kind==='global'&&approved.length>0&&<label>Одобренная заявка<select value={application} onChange={event=>{setApplication(event.target.value);const item=approved.find(x=>x.id===event.target.value);if(item)setName(item.name)}}>
            <option value="">Создать без заявки</option>{approved.map(item=><option key={item.id} value={item.id}>{item.name} · {item.applicant}</option>)}
          </select></label>}
          <label>Название<input value={name} onChange={event=>setName(event.target.value)} required maxLength={64}/></label>
          {(kind==='local'||kind==='agent')&&<label>Родительский узел<select value={parentChoice} onChange={event=>setParent(event.target.value)} required>
            <option value="">Выберите узел</option>{parents.map(row=><option key={row.node.id} value={row.node.id}>{row.node.name}</option>)}
          </select></label>}
          {mode==='global'&&<><label>Адрес клуба<input value={siteAddress} maxLength={250} onChange={event=>setSiteAddress(event.target.value)}/></label>
            <label>Описание и информация для ассистента гостя<textarea rows={3} value={siteDescription} maxLength={1500} onChange={event=>setSiteDescription(event.target.value)}/></label></>}
          <div className="form-pair"><label>DNS-имя или IP сервера<input value={host} onChange={event=>setHost(event.target.value)} placeholder="Адрес целевого компьютера" required/></label>
            <label>Порт панели<input min={1} max={65535} type="number" value={port||''} required onChange={event=>setPort(Number(event.target.value))}/></label></div>
          <label>Протокол<select value={secure?'https':'http'} onChange={event=>setSecure(event.target.value==='https')}>
            <option value="https">HTTPS через прокси</option><option value="http">{context.value?.lan||mode==='global'?'HTTP · локальная сеть':'HTTP · localhost'}</option>
          </select><small className="footnote">{secure?`Прокси передаёт запросы на локальный порт роли ${ports[kind]}.`:'Укажите адрес, который видят другие узлы.'}</small></label>
          {developer&&!application&&(kind==='global'||kind==='audit')&&<label>ID владельца <small className="footnote">Публичный ID из Коллектива. Для тестового Global поле можно оставить пустым.</small>
            <input value={owner} onChange={event=>setOwner(event.target.value)}/></label>}
          {mode==='orchestra'&&<label>Среда запуска<select value={method} onChange={event=>setMethod(event.target.value as typeof method)}>
            <option value="native">Без Docker · Windows или Linux</option><option value="docker">Docker · Linux</option>
          </select></label>}
          <button className="btn" disabled={busy||!context.value}><Server size={17}/> Создать конфигурацию {kindNames[kind]}</button>
        </form>
      </Panel>}
      <Panel title="Подготовленные узлы"><div className="deployment-node-list">
        {launchable.length===0&&!topology.loading&&<p className="footnote">Создайте профиль, чтобы загрузить его в AuditDesktop или запустить узел по SSH.</p>}
        {launchable.map(row=><button className={'deployment-node '+(selected?.node.id===row.node.id?'selected':'')} key={row.node.id} onClick={()=>setParams({node:row.node.id})}>
          <HardDrive size={19}/><span><strong>{row.node.name}</strong><small>{kindNames[row.node.kind]} · {row.node.mode==='native'?'Без Docker':'Docker'}</small></span>
          <i className={online(row)?'network-dot online':'network-dot'}/>
        </button>)}
      </div></Panel>
    </div>
    {selected&&(developer||selected.node.ownerId===context.value?.actor)&&<RemoteLaunch key={selected.node.id} selected={selected} refresh={jobs.reload}/>}
    <Panel title="Ход развёртывания">
      {jobs.value?.length?jobs.value.map(job=><div className="line" key={job.id}><div className="grow"><strong>{job.name}</strong><small>{job.stage} · {new Date(job.at).toLocaleTimeString('ru-RU')}</small></div>
        <Badge ok={job.status==='succeeded'} text={jobNames[job.status]||job.status}/></div>):<p className="footnote">Задания SSH появятся после запуска.</p>}
      <ErrorBanner text={jobs.error}/>
    </Panel>
    <Link className="text-link" to="/nodes">Перейти к узлам сети <ArrowRight size={15}/></Link>
  </>;
}


function ClubDistribution(){
  const {api,session}=useApp();
  const runtime=useRemote(()=>api.request<{nodeId:string}>('/public/runtime'),[api]);
  const {topology}=useNetwork();
  const [platform,setPlatform]=useState('windows'),[busy,setBusy]=useState(false),[error,setError]=useState(''),[status,setStatus]=useState('');
  const agents=topology.value?.filter(item=>item.node.kind==='agent'&&item.node.parentId===runtime.value?.nodeId)??[];
  const [invitation,setInvitation]=useState('');
  async function download(network=false){
    const format=network?'assignment':platform;
    setInvitation('');
    setBusy(true);setError('');setStatus('Формируем комплект Agent…');
    try{
      const response=await fetch(session.url.replace(/\/$/,'')+'/api/v1/club/distribution/'+format,{
        method:'POST',headers:{Authorization:'Bearer '+session.token}});
      if(!response.ok){const details=await response.json().catch(()=>({}));throw Error(details.error||'Не удалось подготовить комплект, HTTP '+response.status)}
      if(network){setInvitation(await response.text());setStatus('Приглашение готово.');return;}
      const url=URL.createObjectURL(await response.blob());
      const link=document.createElement('a');link.href=url;link.download=platform==='assignment'?'club-assignment.json':'MysticEgo-Agent-'+platform+'.zip';
      link.click();setTimeout(()=>URL.revokeObjectURL(url),5000);setStatus('Комплект готов.');
    }catch(error){setError(String(error));setStatus('')}
    finally{setBusy(false)}
  }
  return <><Header title="Комплект Agent" description="Local выпускает комплект рабочих мест, уже связанный с клубом и его Global."/>
    <ErrorBanner text={error||runtime.error||topology.error}/>
    <div className="two-col">
      <Panel title="Распространить по ПК клуба"><div className="stack">
        <label>Комплект<select value={platform} onChange={event=>setPlatform(event.target.value)}><option value="windows">Windows x64</option>
          <option value="linux">Linux x64</option><option value="assignment">Файл приглашения для AgentDesktop</option></select></label>
        <p className="footnote">На ПК в сети клуба откройте AgentDesktop и вставьте приглашение. Приложение получит профиль у Local и запустит Agent.</p>
        <button className="btn" disabled={busy} onClick={()=>void download()}><Download size={17}/>{busy?'Подготовка…':'Скачать комплект'}</button>
        <button className="btn secondary" disabled={busy} onClick={()=>void download(true)}>Создать сетевое приглашение</button>
        {invitation&&<label>Приглашение для AgentDesktop<textarea readOnly rows={3} value={invitation} onFocus={event=>event.currentTarget.select()}/></label>}
        {status&&<span className="success" role="status">{status}</span>}
      </div></Panel>
      <Panel title="Рабочие места" aside={<span className="muted">{agents.length}</span>}>
        {agents.map(row=><div className="line" key={row.node.id}><HardDrive size={17}/><div className="grow"><strong>{row.node.name}</strong></div>
          <Badge ok={online(row)} text={online(row)?'В сети':'Ожидает подключения'}/></div>)}
        {!agents.length&&<p className="footnote">Agent появится после первого запуска комплекта в сети клуба.</p>}
      </Panel>
    </div>
    <div className="row-actions"><Link className="btn secondary" to="/updates">Опубликовать обновление для Agent</Link><Link className="text-link" to="/nodes">Открыть узлы</Link></div>
  </>;
}
