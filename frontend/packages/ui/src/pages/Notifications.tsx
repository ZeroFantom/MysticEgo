import {useApp,useRemote} from '../app/context';
import {NodeApi} from '@mysticego/sdk';
import {Bell,Check} from 'lucide-react';
import {Panel,Header,ErrorBanner} from '../components/primitives';
type Notification={id:string;recipient:string;kind:string;text:string;read:boolean;at:string};
export function NotificationsPage(){const {session}=useApp();const api=new NodeApi(import.meta.env.VITE_GLOBAL_URL||'http://127.0.0.1:5100',()=>session.token);const notifications=useRemote(()=>api.request<Notification[]>('/social/notifications'),[session.token]);async function read(id:string){await api.request(`/social/notifications/${id}/read`,{method:'POST'});notifications.reload()}
 return <><Header title="Уведомления" description="Запросы дружбы, сообщения и важные события профиля."/><ErrorBanner text={notifications.error}/><Panel title="Мои события">{notifications.value?.map(n=><div className={'social-row '+(!n.read?'notice-new':'')} key={n.id}><Bell size={17}/><span><strong>{n.text}</strong><small>{new Date(n.at).toLocaleString('ru-RU')}</small></span>{!n.read&&<button className="btn secondary" onClick={()=>read(n.id)}><Check size={15}/> Прочитано</button>}</div>)}{!notifications.value?.length&&<p className="footnote">Пока нет уведомлений.</p>}</Panel></>}
