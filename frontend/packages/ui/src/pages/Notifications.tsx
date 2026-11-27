import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {Bell,Check,RefreshCw} from 'lucide-react';
import {Panel,Header,ErrorBanner} from '../components/primitives';
type Notification={id:string;recipient:string;kind:string;text:string;read:boolean;at:string};
export function NotificationsPage(){
 const {api}=useApp();
 const notifications=useRemote(()=>api.request<Notification[]>('/collective/notifications'),[api],10000,true);
 const [error,setError]=useState(''),[busy,setBusy]=useState('');
 async function read(id:string){if(busy)return;setBusy(id);setError('');try{await api.request('/collective/notifications/'+encodeURIComponent(id)+'/read',{method:'POST'});notifications.reload()}catch(e){setError(String(e))}finally{setBusy('')}}
 return <><Header title="Уведомления" description="Запросы дружбы, сообщения и важные события профиля." action={<button className="btn secondary" onClick={notifications.reload}><RefreshCw size={16}/> Повторить проверку</button>}/><ErrorBanner text={notifications.error||error}/><Panel title="Мои события">{notifications.value?.map(n=><div className={'social-row '+(!n.read?'notice-new':'')} key={n.id}><Bell size={17}/><span><strong>{n.text}</strong><small>{new Date(n.at).toLocaleString('ru-RU')}</small></span>{!n.read&&<button className="btn secondary" disabled={!!busy} onClick={()=>void read(n.id)}><Check size={15}/> Прочитано</button>}</div>)}{notifications.loading&&!notifications.value&&<p>Загрузка…</p>}{notifications.value?.length===0&&!notifications.error&&<p className="footnote">Пока нет уведомлений.</p>}</Panel></>
}
