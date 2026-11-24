import {useState,type FormEvent} from 'react';
import {UserRound,LogOut} from 'lucide-react';import {Panel,Header,ErrorBanner} from '../components/primitives';
export function GuestPortal({apiUrl,token,onExit}:{apiUrl:string;token:string;onExit:()=>void}){
 const [question,setQuestion]=useState(''),[answer,setAnswer]=useState(''),[busy,setBusy]=useState(false);
 const [guest,setGuest]=useState<{id:string;nickname:string;expires:string}|null>(null);const [error,setError]=useState('');
 async function load(){try{const r=await fetch(apiUrl+'/api/v1/guest/me',{headers:{'X-Guest-Session':token}});if(!r.ok)throw Error('Гостевая сессия завершена');setGuest(await r.json())}catch(e){setError(String(e))}}
 async function exit(){try{await fetch(apiUrl+'/api/v1/guest/logout',{method:'POST',headers:{'X-Guest-Session':token}})}finally{onExit()}}
 async function ask(event:FormEvent){
  event.preventDefault();setBusy(true);setError('');
  try{
   const response=await fetch(apiUrl+'/api/v1/guest/assistant',{method:'POST',headers:{'X-Guest-Session':token,'Content-Type':'application/json'},body:JSON.stringify({text:question})});
   const result=await response.json();if(!response.ok)throw Error(result.error||'Ассистент пока недоступен');setAnswer(result.text);
  }catch(error){setError(String(error))}finally{setBusy(false)}
 }
 return <div className="guest-portal"><Header title="Временный профиль гостя" description="Этот профиль существует только пока активна локальная сессия клуба"/><ErrorBanner text={error}/><Panel title="Сессия на данном компьютере"><p><UserRound size={16}/> {guest?.nickname||'Гость'}</p><div className="quick-actions"><button className="btn secondary" onClick={load}>Показать мою сессию</button><button className="btn" onClick={exit}><LogOut size={16}/> Завершить и удалить</button></div><p className="footnote">Гостевой профиль не является постоянным аккаунтом Audit и не даёт прав на команды компьютера, управление инфраструктурой или друзей. Для этих функций войдите через единый Auth.</p></Panel><Panel title="Помощник клуба"><form className="stack" onSubmit={ask}><label>Вопрос<textarea value={question} maxLength={1200} onChange={event=>setQuestion(event.target.value)} required rows={2}/></label><button className="btn" disabled={busy}>Спросить</button></form>{answer&&<p className="assistant-reply">{answer}</p>}</Panel></div>
}
export function GuestEntry({apiUrl,onLogin}:{apiUrl:string;onLogin:(token:string)=>void}){const [name,setName]=useState('');const [error,setError]=useState('');async function submit(e:FormEvent){e.preventDefault();try{const r=await fetch(apiUrl+'/api/v1/guest/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({nickname:name})});if(!r.ok)throw Error('Не удалось создать временную сессию');const data=await r.json();onLogin(data.token)}catch(e){setError(String(e))}}return <Panel title="Временный вход для гостя клуба"><form className="stack" onSubmit={submit}><label>Никнейм<input minLength={2} maxLength={30} required value={name} onChange={e=>setName(e.target.value)}/></label><button className="btn">Войти на время сеанса</button><ErrorBanner text={error}/></form><p className="footnote">Постоянный аккаунт Audit можно использовать на Agent через основной вход выше.</p></Panel>}
