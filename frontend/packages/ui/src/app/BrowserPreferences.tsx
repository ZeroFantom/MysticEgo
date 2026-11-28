import {createContext,useContext,useEffect,useRef,useState,type ReactNode} from 'react';
import {ShieldCheck} from 'lucide-react';
import {browserKind,browserLabels,readConsent,saveConsent} from './browserPolicy';
import {useLocale} from './presentation';
import './browser-preferences.css';
import {MobileAppOffer} from './MobileAppOffer';
const PrivacyContext=createContext({ready:true,open:()=>{},enabled:false});
export function useBrowserConsentReady(){return useContext(PrivacyContext).ready}
export function PrivacyLink(){const {open,enabled}=useContext(PrivacyContext);const t=useLocale();return enabled?<button type="button" className="browser-privacy-link" onClick={open}><ShieldCheck size={15} aria-hidden="true"/><span>{t('Настройки приватности')}</span></button>:null}
export function BrowserPreferences({children,publicPage=false}:{children:ReactNode;publicPage?:boolean}){
 const t=useLocale(),kind=browserKind();
 const [consent,setConsent]=useState(readConsent),[desktop,setDesktop]=useState(false),[editing,setEditing]=useState(false);
 const dialog=useRef<HTMLDialogElement>(null);
 useEffect(()=>{document.documentElement.dataset.browser=kind;const sync=()=>setConsent(readConsent());window.addEventListener('mystic-browser-consent',sync);window.addEventListener('storage',sync);return()=>{window.removeEventListener('mystic-browser-consent',sync);window.removeEventListener('storage',sync)}},[kind]);
 const desktopApp=kind==='desktop'||kind==='android',blocked=kind==='unsupported'&&!publicPage;
 const enabled=!desktopApp&&!blocked,show=enabled&&(!consent||editing);
 useEffect(()=>{const el=dialog.current;if(show&&el&&!el.open)el.showModal();else if(!show&&el?.open)el.close()},[show]);
 function save(value:boolean){setConsent(saveConsent(value));setEditing(false)}
 return <PrivacyContext.Provider value={{ready:!show,enabled:enabled&&!!consent,open:()=>{setDesktop(consent?.desktopDiscovery??false);setEditing(true)}}}>
 {(!show||blocked)&&<MobileAppOffer/>}
 {blocked?<main className="auth-experience auth-recovery"><section className="auth-form-side"><div className="auth-form-card"><h1>{t('Браузер не поддерживается')}</h1><p>{t('Вход доступен в Mozilla Firefox, Яндекс Браузере, Microsoft Edge и Google Chrome.')}</p><p>{t('Откройте этот адрес в одном из поддерживаемых браузеров.')}</p><a href="/share">{t('О проекте MysticEgo')}</a></div></section></main>:children}
 {enabled&&consent&&<footer className="browser-public-footer"><PrivacyLink/></footer>}
 {enabled&&<dialog ref={dialog} className="browser-consent" aria-labelledby="privacy-heading" onCancel={e=>{e.preventDefault();if(consent)setEditing(false)}}>
 <div className="browser-consent-icon"><ShieldCheck size={25}/></div><h2 id="privacy-heading">{t('Настройки приватности')}</h2>
 <p>{t('Для входа и сохранения настроек используются необходимые данные браузера. Рекламные и аналитические cookies не используются.')}</p>
 <label><input type="checkbox" checked={desktop} onChange={e=>setDesktop(e.target.checked)}/><span>{t('Разрешить проверку подключённых приложений MysticEgo на этом устройстве. Возможен системный запрос доступа к локальной сети.')}</span></label>
 <div className="actions"><button className="btn secondary" onClick={()=>save(false)}>{t('Только необходимые')}</button><button className="btn" onClick={()=>save(desktop)}>{t('Сохранить настройки')}</button>{consent&&<button className="btn secondary" onClick={()=>setEditing(false)}>{t('Отмена')}</button>}</div>
 </dialog>}
 </PrivacyContext.Provider>;
}
