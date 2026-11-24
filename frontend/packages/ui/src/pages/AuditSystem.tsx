import {NetworkRepairPanel} from '../components/NetworkRepairPanel';
import {useState} from 'react';
import {Cpu,RefreshCw,ShieldCheck} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
const quick=[['system.info','Состояние ПК'],['system.details','Система и накопители'],['process.list','Запущенные процессы'],['audio.devices','Аудиоустройства'],['rgb.status','OpenRGB'],['network.interfaces','Сеть']];
export function AuditSystem(){
 const {api}=useApp();const host=useRemote(()=>api.request<{device:string;os:string;uptimeMs:number}>('/audit-orchestra/system'),[api]);
 const [result,setResult]=useState<unknown>(null);const [busy,setBusy]=useState('');const [error,setError]=useState('');
 async function run(id:string){setBusy(id);setError('');try{setResult(await api.request('/audit-orchestra/execute',{method:'POST',body:JSON.stringify({commandId:id})}))}catch(e){setError(String(e))}finally{setBusy('')}}
 return <><Header title="Мой домашний компьютер" description="Локальная диагностика владельца через атрибуты команд MysticEgo Core" action={<button className="btn secondary" onClick={host.reload}><RefreshCw size={16}/> Обновить</button>}/>
 <ErrorBanner text={host.error||error}/><div className="two-col"><Panel title="Информация"><Cpu size={21}/><p><strong>{host.value?.device??'—'}</strong></p><p className="muted">{host.value?.os??'Данные загружаются'}</p><p className="footnote">Только владелец, назначенный при настройке домашнего узла, может запускать локальные команды. Гости не получают доступа.</p></Panel><Panel title="Быстрые проверки">{quick.map(([id,label])=><div className="line" key={id}><span className="grow"><strong>{label}</strong><small>{id}</small></span><button className="btn secondary tiny" disabled={!!busy} onClick={()=>run(id)}><ShieldCheck size={14}/>{busy===id?'Выполнение…':'Запустить'}</button></div>)}</Panel></div>
 {result!==null&&<Panel title="Результат команды"><pre className="output">{JSON.stringify(result,null,2)}</pre></Panel>}<NetworkRepairPanel/></>
}
