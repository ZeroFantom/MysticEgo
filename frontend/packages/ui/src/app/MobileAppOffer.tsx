import {useState} from 'react';
import {Smartphone,X} from 'lucide-react';
import {useLocale} from './presentation';
import {androidLaunch,ANDROID_FALLBACK,ANDROID_ORIGIN,isAndroidBrowser,shouldOfferAndroid} from './androidLaunch';
import './mobile-app-offer.css';

const dismissalKey='mysticego.android-offer.dismissed.v1';
function platform(){return (navigator as Navigator&{userAgentData?:{platform?:string}}).userAgentData?.platform??'';}
function dismissed(){try{return sessionStorage.getItem(dismissalKey)==='1'}catch{return false}}
function openCurrent(event:React.MouseEvent<HTMLAnchorElement>){
 // Preserve only a known route, never location.search/hash, credentials, IDs or arbitrary origins.
 event.currentTarget.href=androidLaunch(location.pathname);
}
export function MobileAppOffer(){
 const t=useLocale();
 const [hidden,setHidden]=useState(dismissed);
 function close(){setHidden(true);try{sessionStorage.setItem(dismissalKey,'1')}catch{/* storage may be disabled */}}
 if(hidden||!shouldOfferAndroid(location.origin,navigator.userAgent,platform(),location.pathname))return null;
 return <aside className="mobile-app-offer" aria-label={t('Приложение для Android')}>
  <Smartphone size={24} aria-hidden="true"/>
  <div><strong>{t('Открыть MysticEgo Mobile')}</strong><p>{t('Если приложение уже установлено, откройте его. Или продолжайте в браузере.')}</p>
   <div className="mobile-app-actions">
    <a className="btn" href={androidLaunch(location.pathname)} onClick={openCurrent}>{t('Открыть приложение')}</a>
    <a className="btn secondary" href={ANDROID_FALLBACK}>{t('Установка Android')}</a>
    <button className="btn secondary" type="button" onClick={close}>{t('Продолжить в браузере')}</button>
   </div>
  </div>
  <button className="mobile-app-close" type="button" onClick={close} aria-label={t('Закрыть')}><X size={20} aria-hidden="true"/></button>
 </aside>;
}
export function AndroidDownloadCard(){
 const t=useLocale();
 const canOpen=location.origin===ANDROID_ORIGIN&&isAndroidBrowser(navigator.userAgent,platform());
 return <article id="android" className="desktop-download-card" data-product="android">
  <span className="desktop-download-icon"><Smartphone size={24} aria-hidden="true"/></span>
  <h2>MysticEgo Mobile · Android</h2>
  <p>{t('Android-клиент использует существующий личный кабинет и авторизацию MysticEgo.')}</p>
  <p role="status">{t('Публичная подписанная APK пока не выпущена. Проверяйте официальные релизы.')}</p>
  {canOpen&&<a className="btn secondary" href={androidLaunch('/audit-portal/')}>{t('Открыть приложение')}</a>}
  <a className="btn secondary" href="https://github.com/ZeroFantom/MysticEgo/releases" rel="noreferrer">{t('Официальные релизы')}</a>
 </article>;
}
