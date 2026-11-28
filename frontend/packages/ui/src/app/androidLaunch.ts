// Client-side compatibility hint; never an authentication decision.
export const ANDROID_ORIGIN='https://orchestra.isdarkline.ru';
export const ANDROID_PACKAGE='ru.isdarkline.mysticego';
export const ANDROID_FALLBACK=ANDROID_ORIGIN+'/desktop-downloads#android';
export const ANDROID_ROUTES:Readonly<Record<string,string>>=Object.freeze({"home":"/audit-portal/","orchestra":"/","profile":"/profile","collective":"/collective","assistant":"/assistant","notifications":"/notifications","organizations":"/organizations","settings":"/settings","audit-profile":"/audit-portal/profile","audit-collective":"/audit-portal/collective","audit-assistant":"/audit-portal/assistant","audit-notifications":"/audit-portal/notifications","audit-organizations":"/audit-portal/organizations","audit-settings":"/audit-portal/settings"});
export function isAndroidBrowser(userAgent:string,platform=''){
 return (platform.toLowerCase()==='android'||/Android/i.test(userAgent))&&!/MysticEgoAndroid\/|Electron\//i.test(userAgent);
}
export function shouldOfferAndroid(origin:string,userAgent:string,platform='',pathname='/'){
 return origin===ANDROID_ORIGIN&&isAndroidBrowser(userAgent,platform)&&pathname.replace(/\/$/,'')!=='/desktop-downloads';
}
export function androidRoute(pathname:string){
 const path=pathname.replace(/\/$/,'')||'/';
 if(path==='/social'||path==='/directory')return 'collective';
 if(path==='/audit-portal/social'||path==='/audit-portal/directory')return 'audit-collective';
 return Object.keys(ANDROID_ROUTES).find(key=>ANDROID_ROUTES[key].replace(/\/$/,'')===path||ANDROID_ROUTES[key]===path)
   ??(path==='/audit-portal'||path.startsWith('/audit-portal/')?'home':'orchestra');
}
export function androidLaunch(pathname:string){
 return 'intent://open/'+androidRoute(pathname)+'#Intent;scheme=mysticego;package='+ANDROID_PACKAGE+';S.browser_fallback_url='+encodeURIComponent(ANDROID_FALLBACK)+';end';
}
