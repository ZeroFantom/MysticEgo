import {useEffect,useRef,useState} from 'react';
import {useApp} from './context';
import {Panel,ErrorBanner} from '../components/primitives';

type Proposal={id:string;alias:string;path:string;state:string;expires:string};
export function LocalMobileLaunchPanel(){
 const {mode,session,api,preferenceScope}=useApp();
 const eligible=(mode==='agent'||mode==='audit')&&['localhost','127.0.0.1','[::1]'].includes(location.hostname)&&session.url.replace(/\/$/,'')===location.origin;
 const [rows,setRows]=useState<Proposal[]|null>(null),[selected,setSelected]=useState<Proposal|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const epoch=useRef(0);
 useEffect(()=>{epoch.current++;setRows(null);setSelected(null);setBusy(false);setError('');return()=>{epoch.current++}},[session.token,preferenceScope]);
 async function run(work:(id:number)=>Promise<void>){const id=epoch.current;setBusy(true);setError('');try{await work(id)}catch{if(id===epoch.current)setError('Локальные предложения недоступны. Проверьте настройки владельца ПК.')}finally{if(id===epoch.current)setBusy(false)}}
 async function refresh(id:number){const data=await api.request<Proposal[]>('/managed/mobile-launch',{cache:'no-store',redirect:'error'});if(id===epoch.current)setRows(data)}
 if(!eligible)return null;
 return <Panel title="Локальное подтверждение запуска"><p>Запрос с телефона не запускает программу автоматически. Проверьте псевдоним и путь. Подтвердить можно только в локальной панели ПК.</p>
 <ErrorBanner text={error}/><button className="btn secondary" disabled={busy} onClick={()=>void run(refresh)}>Обновить предложения</button>
 {rows?.map(row=><article className="panel" key={row.id}><strong>{row.alias}</strong><p>{row.path}</p><p>{row.state}</p>{row.state==='pending'&&<button className="btn secondary" disabled={busy} onClick={()=>setSelected(row)}>Рассмотреть запрос</button>}</article>)}
 {selected&&<div className="panel stack"><strong>Запустить {selected.alias}?</strong><p>{selected.path}</p><p>Подтверждение одноразовое; программа будет запущена с правами текущего локального процесса.</p>
 {([true,false] as const).map(approve=><button className={approve?'btn':'btn secondary'} key={String(approve)} disabled={busy} onClick={()=>void run(async id=>{if(document.hidden||!document.hasFocus())return;await api.request('/managed/mobile-launch/'+selected.id+'/decide',{method:'POST',body:JSON.stringify({approve,alias:selected.alias}),redirect:'error'});if(id===epoch.current){setSelected(null);await refresh(id)}})}>{approve?'Подтвердить запуск':'Отклонить'}</button>)}
 <button className="btn secondary" disabled={busy} onClick={()=>setSelected(null)}>Закрыть</button></div>}
 </Panel>;
}
