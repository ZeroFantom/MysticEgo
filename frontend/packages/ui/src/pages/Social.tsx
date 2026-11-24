import {useState,type FormEvent} from 'react';
import {MessageCircle,UserPlus,CheckCheck,Send} from 'lucide-react';
import {NodeApi} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner} from '../components/primitives';
type Friendship={id:string;sender:string;recipient:string;status:string;at:string};
type Message={id:string;sender:string;recipient:string;text:string;at:string};
export function SocialPage(){
 const {session}=useApp();const api=new NodeApi(import.meta.env.VITE_GLOBAL_URL||'http://127.0.0.1:5100',()=>session.token);
 const friends=useRemote(()=>api.request<Friendship[]>('/social/friends'),[session.token]);
 const messages=useRemote(()=>api.request<Message[]>('/social/messages'),[session.token]);
 const [recipient,setRecipient]=useState('');const [text,setText]=useState('');const [notice,setNotice]=useState('');const me=session.username||'';
 async function addFriend(e:FormEvent){e.preventDefault();setNotice('');try{await api.request('/social/friends/request',{method:'POST',body:JSON.stringify({recipient})});setNotice('Заявка отправлена.');friends.reload()}catch(ex){setNotice(String(ex))}}
 async function accept(id:string){try{await api.request(`/social/friends/${id}/accept`,{method:'POST'});friends.reload()}catch(ex){setNotice(String(ex))}}
 async function send(e:FormEvent){e.preventDefault();try{await api.request('/social/messages',{method:'POST',body:JSON.stringify({recipient,text})});setText('');messages.reload()}catch(ex){setNotice(String(ex))}}
 return <><Header title="Сообщество" description="Друзья и личные сообщения. Доступ к ПК друзей требует отдельного разрешения."/><ErrorBanner text={friends.error||messages.error||notice}/><div className="two-col"><Panel title="Друзья"><form className="stack" onSubmit={addFriend}><label>Логин пользователя<input required value={recipient} onChange={e=>setRecipient(e.target.value)} placeholder="Логин друга"/></label><button className="btn"><UserPlus size={16}/> Добавить в друзья</button></form><div className="social-list">{friends.value?.map(f=><div className="social-row" key={f.id}><span><strong>{f.sender===me?f.recipient:f.sender}</strong><small>{f.status==='accepted'?'Друзья':f.status==='pending'?'Ожидает подтверждения':f.status}</small></span>{f.status==='pending'&&f.recipient===me&&<button className="btn secondary" onClick={()=>accept(f.id)}><CheckCheck size={16}/> Принять</button>}</div>)}</div></Panel><Panel title="Переписка"><form className="stack" onSubmit={send}><label>Получатель<input required value={recipient} onChange={e=>setRecipient(e.target.value)}/></label><label>Сообщение<textarea required maxLength={2000} rows={3} value={text} onChange={e=>setText(e.target.value)}/></label><button className="btn"><Send size={16}/> Отправить</button></form><div className="social-list">{messages.value?.map(m=><div className="social-row" key={m.id}><MessageCircle size={17}/><span><strong>{m.sender} → {m.recipient}</strong><small>{m.text}</small></span></div>)}</div><p className="footnote">Переписка доступна только взаимно добавленным друзьям. Пока без push и сквозного шифрования.</p></Panel></div></>;
}
