import {useEffect,useState,type ReactNode} from 'react';
import {browserKind,browserLabels,readConsent,saveConsent,clearConsent} from './browserPolicy';
import './browser-preferences.css';
export function BrowserPreferences({children,publicPage=false}:{children:ReactNode;publicPage?:boolean}){
 const kind=browserKind();
 const [consent,setConsent]=useState(readConsent),[desktop,setDesktop]=useState(false);
 useEffect(()=>{document.documentElement.dataset.browser=kind;const sync=()=>setConsent(readConsent());window.addEventListener('mystic-browser-consent',sync);window.addEventListener('storage',sync);return()=>{window.removeEventListener('mystic-browser-consent',sync);window.removeEventListener('storage',sync)}},[kind]);
 const desktopApp=kind==='desktop',blocked=kind==='unsupported'&&!publicPage;
 return <>
 {blocked?<main className="auth-experience auth-recovery"><section className="auth-form-side"><div className="auth-form-card"><h1>Браузер не поддерживается</h1><p>Вход доступен в Mozilla Firefox, Яндекс Браузере, Microsoft Edge и Google Chrome.</p><p>Откройте этот адрес в одном из поддерживаемых браузеров.</p><a href="/share">О проекте MysticEgo</a></div></section></main>:children}
 {!desktopApp&&!consent&&!blocked&&<section className="browser-consent" aria-label="Настройки приватности"><h2>Настройки приватности</h2><p>Для входа и сохранения настроек используются необходимые данные браузера. Рекламные и аналитические cookies не используются.</p><label><input type="checkbox" checked={desktop} onChange={e=>setDesktop(e.target.checked)}/> Разрешить проверку подключённых приложений MysticEgo на этом устройстве. Возможен системный запрос доступа к локальной сети.</label><div className="actions"><button className="btn" onClick={()=>saveConsent(false)}>Только необходимые</button><button className="btn secondary" onClick={()=>saveConsent(desktop)}>Сохранить настройки</button></div></section>}
 {!desktopApp&&consent&&<button type="button" className="browser-privacy-link" onClick={()=>{setDesktop(consent.desktopDiscovery);clearConsent()}} title={browserLabels[kind]}>Настройки приватности</button>}
 </>;
}
