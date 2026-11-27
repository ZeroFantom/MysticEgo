import {browserAuthRequest} from '../app/browserSession';
import {useLocale} from '../app/presentation';
import {useState,type FormEvent} from 'react';
import {browserKind,browserLabels} from '../app/browserPolicy';
import {DesktopSignIn} from '../app/DesktopSignIn';
import {ArrowRight,Boxes,Eye,EyeOff,KeyRound,Network,Shield,Sparkles,UserRound} from 'lucide-react';
import type {Session} from '../app/context';
import {useRemote} from '../app/context';
import {GuestEntry} from './GuestPortal';
import {ErrorBanner} from '../components/primitives';
import {ThemeToggle} from './Settings';
import {NoticeBell} from '../app/notifications';
import {PanelLoginLinks} from './PanelDirectory';
import {LicenseLink} from './Studio';
import {readRegistrationDocuments} from './registration-documents';

export type AuthLogin={accessToken:string;refreshToken?:string;browserSession?:boolean;user?:{login?:string};expiresAt:string};
export async function signIn(authUrl:string,login:string,password:string):Promise<AuthLogin>{
 let protectedBrowser=false;
 if(location.protocol==='https:'&&authUrl.replace(/\/$/,'')===location.origin){
  const policy=await fetch('/api/auth/browser/status',{cache:'no-store',credentials:'same-origin',redirect:'error',signal:AbortSignal.timeout(8000)});
  if(!policy.ok&&policy.status!==404)throw Error('Browser session policy unavailable');
  protectedBrowser=policy.ok&&(await policy.json()).enabled===true;
 }
 const response=protectedBrowser?await browserAuthRequest('login',{login,password}):await fetch(authUrl.replace(/\/$/, '')+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login,password})});
 if(!response.ok)throw new Error(response.status===401?'Проверьте логин и пароль.':'Вход недоступен, код '+response.status);
 return {...await response.json(),browserSession:protectedBrowser};
}
const labels:Record<string,[string,string]>={orchestra:['Оркестр','Управление экосистемой'],global:['Global','Панель организации'],local:['Local','Панель клуба'],agent:['Agent','Рабочая станция клуба'],audit:['Audit','Личный профиль MysticEgo']};
export function SignIn({session,onLogin,mode='audit',onGuest}:{session:Session;onLogin:(s:Session)=>void;mode?:string;onGuest?:(token:string)=>void}){
 const t=useLocale();
 const [authUrl,setAuthUrl]=useState(session.authUrl);
 const [login,setLogin]=useState(''),[password,setPassword]=useState(''),[nickname,setNickname]=useState('');
 const [error,setError]=useState(''),[busy,setBusy]=useState(false),[register,setRegister]=useState(false),[reveal,setReveal]=useState(false);
 const [guest,setGuest]=useState(false);
 const [pairingBusy,setPairingBusy]=useState(false);
 const [acceptance,setAcceptance]=useState('');
 const [enabledScopes,setEnabledScopes]=useState<string[]>([]);
 const documents=useRemote(()=>register?readRegistrationDocuments(authUrl):Promise.resolve(null),[register,authUrl]);
 const policy=documents.value?.required?documents.value:null;
 const policyKey=policy?JSON.stringify([authUrl,policy.terms.version,policy.privacy.version,policy.license.version,policy.permissionPolicyVersion]):'';
 const available=useRemote(async()=>{
  if(mode!=='agent')return false;
  try{const response=await fetch(session.url+'/api/v1/guest/availability');return response.ok&&(await response.json()).available===true}catch{return false}
 },[mode,session.url],6000);
 const [title,subtitle]=labels[mode]??labels.audit;
 function selectMode(create:boolean){
  setRegister(create);
  setAcceptance('');setEnabledScopes([]);
  setReveal(false);
  setError('');
 }
 async function submit(event:FormEvent){
  event.preventDefault();if(pairingBusy)return;if(browserKind()==='unsupported'){setError('Браузер не поддерживается.');return;}setBusy(true);setError('');
  try{
   if(register){
    if(!documents.value||documents.loading||documents.error||policyKey&&acceptance!==policyKey)
     throw Error('Сначала ознакомьтесь с документами регистрации.');
    const response=await fetch(authUrl.replace(/\/$/,'')+'/api/auth/audit/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login,password,nickname,termsVersion:policy?.terms.version,privacyVersion:policy?.privacy.version,licenseVersion:policy?.license.version,permissionPolicyVersion:policy?.permissionPolicyVersion,enabledScopes})});
    if(!response.ok){const result=await response.json().catch(()=>({}));throw Error(result.error||'Регистрация недоступна, код '+response.status)}
   }
   const result=await signIn(authUrl,login,password);
   onLogin({...session,authUrl:result.browserSession?location.origin:authUrl,token:result.accessToken,refreshToken:result.browserSession?undefined:result.refreshToken,browserSession:result.browserSession,username:result.user?.login||login});
   setPassword('');
  }catch(error){setError(error instanceof Error?error.message:String(error))}
  finally{setBusy(false)}
 }
 return <main className="auth-experience">
  <section className="auth-visual">
   <div className="auth-brand"><Boxes size={24}/> MysticEgo</div>
   <div className="auth-orbit" aria-hidden="true"><i/><i/><i/><span className="auth-emblem"><Shield size={48}/></span></div>
   <div className="auth-role"><span><Network size={16}/>{t(title)}</span><h1>{t(subtitle)}</h1></div>
  </section>
  <section className="auth-form-side">
   <div className="auth-top-actions"><ThemeToggle/><NoticeBell placement="header"/></div>
   <div className="auth-form-card">
    <p className="auth-eyeline"><Sparkles size={17}/> {title}</p>
    <small>{t('Браузер')}: {browserLabels[browserKind()]}</small>
    <h2>{guest?t("Гостевой сеанс"):register?t("Создать аккаунт"):t("Войти в MysticEgo")}</h2>
    {mode==='orchestra'&&<PanelLoginLinks/>}
    <DesktopSignIn session={session} authUrl={authUrl} onLogin={onLogin} disabled={busy||register||guest} onBusy={setPairingBusy}/>
    {mode==='audit'&&<div className="auth-tabs" role="group" aria-label={t("Режим авторизации")} data-register={register}>
      <button type="button" aria-pressed={!register} disabled={pairingBusy} onClick={()=>selectMode(false)}>{t("Вход")}</button>
      <button type="button" aria-pressed={register} disabled={pairingBusy} onClick={()=>selectMode(true)}>{t("Регистрация")}</button>
    </div>}
    {guest&&onGuest&&available.value?<GuestEntry apiUrl={session.url} onLogin={onGuest}/>:<form className="stack auth-form" onSubmit={submit}>
     <div className="auth-field">
      <label htmlFor="auth-login">{t("Логин")}</label>
      <div className="auth-control"><UserRound size={17} aria-hidden="true"/><input id="auth-login" name="login" value={login} onChange={e=>setLogin(e.target.value)} required autoComplete="username"/></div>
     </div>
     {register&&<div className="auth-field auth-field-new">
      <label htmlFor="auth-nickname">{t("Игровой никнейм")}</label>
      <div className="auth-control"><Sparkles size={17} aria-hidden="true"/><input id="auth-nickname" name="nickname" required minLength={2} maxLength={60} autoComplete="nickname" value={nickname} onChange={e=>setNickname(e.target.value)}/></div>
     </div>}
     <div className="auth-field">
      <label htmlFor="auth-password">{t("Пароль")}</label>
      <div className="auth-control auth-password"><KeyRound size={17} aria-hidden="true"/>
       <input id="auth-password" name="password" type={reveal?'text':'password'} value={password} onChange={e=>setPassword(e.target.value)} required minLength={register?15:1} autoComplete={register?'new-password':'current-password'} aria-describedby={register?'auth-password-hint':undefined}/>
       <button className="icon-btn" type="button" aria-label={reveal?t("Скрыть пароль"):t("Показать пароль")} onClick={()=>setReveal(value=>!value)}>{reveal?<EyeOff size={17}/>:<Eye size={17}/>}</button>
      </div>
      {register&&<small id="auth-password-hint">{t("Минимум 15 символов")}</small>}
     </div>
     {register&&policy&&<div className="registration-documents">
       <label><input type="checkbox" checked={acceptance===policyKey} required
          onChange={event=>setAcceptance(event.target.checked?policyKey:'')}/>
        <span>{t("Принимаю")} <a href={policy.terms.url} target="_blank" rel="noreferrer">{t("условия использования")}</a>.</span>
       </label>
       <a href={policy.privacy.url} target="_blank" rel="noreferrer">{t("Как обрабатываются данные")}</a>
       <a href={policy.license.url} target="_blank" rel="noreferrer">{t("Лицензия")}</a>
       <p>{t("Подтверждаю ознакомление с условиями, политикой обработки данных и лицензией.")}</p>
       <fieldset><legend>{t("Разрешения ассистента")}</legend>
        <p>{t("Выберите возможности. Разрешение конкретного устройства потребуется отдельно.")}</p>
        {policy.scopes.map(scope=><label key={scope}><input type="checkbox" checked={enabledScopes.includes(scope)} onChange={e=>setEnabledScopes(old=>e.target.checked?[...old,scope]:old.filter(x=>x!==scope))}/>{t('permission.'+scope)}</label>)}
       </fieldset>
     </div>}
     {register&&documents.loading&&<p className="footnote">{t("Загружаем документы регистрации…")}</p>}
     <ErrorBanner text={error||(register?documents.error:'')}/>
     <button className="btn auth-submit" type="submit" disabled={busy||pairingBusy||register&&(!documents.value||documents.loading||!!documents.error||!!policyKey&&acceptance!==policyKey)}>{busy?t("Подождите…"):register?t("Создать аккаунт"):t("Войти")}<ArrowRight size={17}/></button>
     <details className="auth-connection"><summary>{t("Адрес авторизации")}</summary><div className="auth-field">
      <label htmlFor="auth-url">{t("API авторизации")}</label><div className="auth-control"><Network size={17} aria-hidden="true"/>
       <input id="auth-url" type="url" disabled={pairingBusy} value={authUrl} onChange={e=>setAuthUrl(e.target.value)} required/>
      </div></div></details>
    </form>}
    {available.value&&onGuest&&<button className="btn secondary" disabled={pairingBusy} onClick={()=>setGuest(value=>!value)}>{guest?t("Войти в аккаунт"):t("Временный аккаунт гостя")}</button>}
    <footer className="auth-project">
      {mode === 'orchestra' && <a href="/studio">{t("Студия и команда")}</a>}
      <LicenseLink/>
    </footer>
   </div>
  </section>
 </main>;
}
