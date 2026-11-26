import {useState,type FormEvent} from 'react';
import {ArrowRight,Boxes,Eye,EyeOff,KeyRound,Network,Shield,Sparkles,UserRound} from 'lucide-react';
import type {Session} from '../app/context';
import {useRemote} from '../app/context';
import {GuestEntry} from './GuestPortal';
import {ErrorBanner} from '../components/primitives';
import {ThemeToggle} from './Settings';
import {PanelLoginLinks} from './PanelDirectory';
import {LicenseLink} from './Studio';

export type AuthLogin={accessToken:string;refreshToken:string;user?:{login?:string};expiresAt:string};
export async function signIn(authUrl:string,login:string,password:string):Promise<AuthLogin>{
 const response=await fetch(authUrl.replace(/\/$/, '')+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login,password})});
 if(!response.ok)throw new Error(response.status===401?'Проверьте логин и пароль.':'Вход недоступен, код '+response.status);
 return response.json();
}
const labels:Record<string,[string,string]>={orchestra:['Оркестр','Управление экосистемой'],global:['Global','Панель организации'],local:['Local','Панель клуба'],agent:['Agent','Рабочая станция клуба'],audit:['Audit','Личный профиль MysticEgo']};
export function SignIn({session,onLogin,mode='audit',onGuest}:{session:Session;onLogin:(s:Session)=>void;mode?:string;onGuest?:(token:string)=>void}){
 const [authUrl,setAuthUrl]=useState(session.authUrl);
 const [login,setLogin]=useState(''),[password,setPassword]=useState(''),[nickname,setNickname]=useState('');
 const [error,setError]=useState(''),[busy,setBusy]=useState(false),[register,setRegister]=useState(false),[reveal,setReveal]=useState(false);
 const [guest,setGuest]=useState(false);
 const available=useRemote(async()=>{
  if(mode!=='agent')return false;
  try{const response=await fetch(session.url+'/api/v1/guest/availability');return response.ok&&(await response.json()).available===true}catch{return false}
 },[mode,session.url],6000);
 const [title,subtitle]=labels[mode]??labels.audit;
 function selectMode(create:boolean){
  setRegister(create);
  setReveal(false);
  setError('');
 }
 async function submit(event:FormEvent){
  event.preventDefault();setBusy(true);setError('');
  try{
   if(register){
    const response=await fetch(authUrl.replace(/\/$/,'')+'/api/auth/audit/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login,password,nickname})});
    if(!response.ok){const result=await response.json().catch(()=>({}));throw Error(result.error||'Регистрация недоступна, код '+response.status)}
   }
   const result=await signIn(authUrl,login,password);
   onLogin({...session,authUrl,token:result.accessToken,refreshToken:result.refreshToken,username:result.user?.login||login});
   setPassword('');
  }catch(error){setError(error instanceof Error?error.message:String(error))}
  finally{setBusy(false)}
 }
 return <main className="auth-experience">
  <section className="auth-visual">
   <div className="auth-brand"><Boxes size={24}/> MysticEgo</div>
   <div className="auth-orbit" aria-hidden="true"><i/><i/><i/><span className="auth-emblem"><Shield size={48}/></span></div>
   <div className="auth-role"><span><Network size={16}/>{title}</span><h1>{subtitle}</h1></div>
  </section>
  <section className="auth-form-side">
   <ThemeToggle/>
   <div className="auth-form-card">
    <p className="auth-eyeline"><Sparkles size={17}/> {title}</p>
    <h2>{guest?'Гостевой сеанс':register?'Создать аккаунт':'Войти в MysticEgo'}</h2>
    {mode==='orchestra'&&<PanelLoginLinks/>}
    {mode==='audit'&&<div className="auth-tabs" role="group" aria-label="Режим авторизации" data-register={register}>
      <button type="button" aria-pressed={!register} onClick={()=>selectMode(false)}>Вход</button>
      <button type="button" aria-pressed={register} onClick={()=>selectMode(true)}>Регистрация</button>
    </div>}
    {guest&&onGuest&&available.value?<GuestEntry apiUrl={session.url} onLogin={onGuest}/>:<form className="stack auth-form" onSubmit={submit}>
     <div className="auth-field">
      <label htmlFor="auth-login">Логин</label>
      <div className="auth-control"><UserRound size={17} aria-hidden="true"/><input id="auth-login" name="login" value={login} onChange={e=>setLogin(e.target.value)} required autoComplete="username"/></div>
     </div>
     {register&&<div className="auth-field auth-field-new">
      <label htmlFor="auth-nickname">Игровой никнейм</label>
      <div className="auth-control"><Sparkles size={17} aria-hidden="true"/><input id="auth-nickname" name="nickname" required minLength={2} maxLength={60} autoComplete="nickname" value={nickname} onChange={e=>setNickname(e.target.value)}/></div>
     </div>}
     <div className="auth-field">
      <label htmlFor="auth-password">Пароль</label>
      <div className="auth-control auth-password"><KeyRound size={17} aria-hidden="true"/>
       <input id="auth-password" name="password" type={reveal?'text':'password'} value={password} onChange={e=>setPassword(e.target.value)} required minLength={register?12:1} autoComplete={register?'new-password':'current-password'} aria-describedby={register?'auth-password-hint':undefined}/>
       <button className="icon-btn" type="button" aria-label={reveal?'Скрыть пароль':'Показать пароль'} onClick={()=>setReveal(value=>!value)}>{reveal?<EyeOff size={17}/>:<Eye size={17}/>}</button>
      </div>
      {register&&<small id="auth-password-hint">Минимум 12 символов</small>}
     </div>
     <ErrorBanner text={error}/>
     <button className="btn auth-submit" type="submit" disabled={busy}>{busy?'Подождите…':register?'Создать аккаунт':'Войти'}<ArrowRight size={17}/></button>
     <details className="auth-connection"><summary>Адрес авторизации</summary><div className="auth-field">
      <label htmlFor="auth-url">API авторизации</label><div className="auth-control"><Network size={17} aria-hidden="true"/>
       <input id="auth-url" type="url" value={authUrl} onChange={e=>setAuthUrl(e.target.value)} required/>
      </div></div></details>
    </form>}
    {available.value&&onGuest&&<button className="btn secondary" onClick={()=>setGuest(value=>!value)}>{guest?'Войти в аккаунт':'Временный аккаунт гостя'}</button>}
    <footer className="auth-project">
      {mode === 'orchestra' && <a href="/studio">Студия и команда</a>}
      <LicenseLink/>
    </footer>
   </div>
  </section>
 </main>;
}
