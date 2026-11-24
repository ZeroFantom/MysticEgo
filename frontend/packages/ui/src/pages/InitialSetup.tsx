import {useState, type FormEvent} from 'react';
import {ShieldCheck,Network,Users,HardDrive} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner} from '../components/primitives';

type State={node:string;kind:string;configured:boolean;parentUrl?:string;level:number;rank:string};
type Organization={name:string;founderLogin:string;departments:{name:string;members:number}[];sites:{name:string;address:string;computers:number}[];storageDatabase:string;storageCloud:string;storageLocal:string};
export function InitialSetup(){
 const {api,mode}=useApp();
 const state=useRemote(()=>api.request<State>('/setup/state'),[api]);
 const [org,setOrg]=useState('');const [founder,setFounder]=useState('');const [departments,setDepartments]=useState('Разработка|1');const [sites,setSites]=useState('Главный филиал|Адрес|10');
 const [db,setDb]=useState('');const [cloud,setCloud]=useState('');const [local,setLocal]=useState('');
 const [childKind,setChildKind]=useState(mode==='orchestra'?'global':mode==='global'?'local':'agent');
 const [invite,setInvite]=useState('');const [code,setCode]=useState('');const [error,setError]=useState('');const [message,setMessage]=useState('');const [busy,setBusy]=useState(false);
 const createOrg=async(e:FormEvent)=>{e.preventDefault();setError('');setMessage('');setBusy(true);try{
  const departmentItems=departments.split('\n').filter(Boolean).map(s=>{const [name,n]=s.split('|');return {name:name.trim(),members:Number(n)}});
  const siteItems=mode==='global'?sites.split('\n').filter(Boolean).map(s=>{const [name,address,n]=s.split('|');return {name:name.trim(),address:address.trim(),computers:Number(n)}}):[];
  const payload:Organization={name:org,founderLogin:founder,departments:departmentItems,sites:siteItems,storageDatabase:db,storageCloud:cloud,storageLocal:local};
  await api.request('/setup/organization',{method:'POST',body:JSON.stringify(payload)});setMessage('Организация зарегистрирована. Учётную запись основателя необходимо отдельно подтвердить в Auth API.');state.reload();
 }catch(x){setError(String(x))}finally{setBusy(false)}};
 const issue=async()=>{setBusy(true);setError('');try{const r=await api.request<{code:string;validUntil:string}>('/setup/invitations',{method:'POST',body:JSON.stringify({childKind,validMinutes:30})});setInvite(r.code);setMessage('Код действителен до '+r.validUntil+' и показывается только сейчас.');}catch(x){setError(String(x))}finally{setBusy(false)}};
 const join=async()=>{setBusy(true);setError('');try{const r=await api.request<{accepted:boolean;parentNodeId:string}>('/setup/parent/connect',{method:'POST',body:JSON.stringify({code})});setMessage('Родительский узел '+r.parentNodeId+' принял запрос. Для работы узла также нужна отдельная регистрация идентичности в Auth API.');setCode('')}catch(x){setError(String(x))}finally{setBusy(false)}};
 return <><Header title="Первоначальная настройка" description="Организация, отделы, филиалы и подключение дочерних узлов через одноразовые приглашения"/>
 <ErrorBanner text={error||state.error}/>
 <div className="metrics"><div className="metric"><ShieldCheck/><div className="metric-value">{state.value?.rank??'F'}</div><div className="metric-label">Начальный ранг</div></div><div className="metric"><Users/><div className="metric-value">{state.value?.level??1}</div><div className="metric-label">Начальный уровень</div></div><div className="metric"><Network/><div className="metric-value">{state.value?.kind??mode}</div><div className="metric-label">Тип узла</div></div><div className="metric"><HardDrive/><div className="metric-value">{state.value?.configured?'Готово':'Новый'}</div><div className="metric-label">Организация</div></div></div>
 {message&&<Panel title="Результат"><p>{message}</p></Panel>}
 {(mode==='orchestra'||mode==='global')&&!state.value?.configured&&<Panel title="1. Создание организации"><form className="stack" onSubmit={createOrg}>
  <label>Название {mode==='orchestra'?'команды разработки':'сети/офиса'}<input value={org} required onChange={e=>setOrg(e.target.value)}/></label>
  <label>Логин основателя в Auth API<input value={founder} required onChange={e=>setFounder(e.target.value)}/></label>
  <label>Отделы — название|количество сотрудников<textarea rows={4} value={departments} onChange={e=>setDepartments(e.target.value)}/></label>
  {mode==='global'&&<label>Филиалы — название|адрес|количество ПК<textarea rows={4} value={sites} onChange={e=>setSites(e.target.value)}/></label>}
  <label>Путь до БД<input value={db} required onChange={e=>setDb(e.target.value)}/></label><label>Путь для облачных файлов<input value={cloud} required onChange={e=>setCloud(e.target.value)}/></label><label>Путь локальных файлов<input value={local} required onChange={e=>setLocal(e.target.value)}/></label>
  <button className="btn" disabled={busy}>Сохранить структуру</button>
  <p className="footnote">Пароли и привилегии не хранятся в этой структуре. Роли сотрудников назначаются администратором Auth API.</p>
 </form></Panel>}
 {(mode==='orchestra'||mode==='global'||mode==='local')&&<Panel title="2. Пригласить дочерний узел"><div className="stack"><label>Тип подключаемого узла<select value={childKind} onChange={e=>setChildKind(e.target.value)}>{(mode==='orchestra'?['global','audit']:mode==='global'?['local']:['agent']).map(x=><option key={x} value={x}>{x}</option>)}</select></label>
 <button className="btn secondary" disabled={busy} onClick={issue}>Создать одноразовое приглашение (30 минут)</button>{invite&&<><code style={{overflowWrap:'anywhere'}}>{invite}</code><button className="btn secondary" onClick={()=>navigator.clipboard.writeText(invite)}>Копировать код</button></>}</div></Panel>}
 {mode!=='orchestra'&&<Panel title="3. Подключиться к родительскому узлу"><p className="footnote">Адрес родительского сервера: {state.value?.parentUrl||'Не настроен в конфигурации Node'}</p><div className="stack"><label>Одноразовый код приглашения<input value={code} onChange={e=>setCode(e.target.value)}/></label><button className="btn" disabled={busy||code.length!==64} onClick={join}>Проверить и подключиться</button></div></Panel>}
 </>;
}
