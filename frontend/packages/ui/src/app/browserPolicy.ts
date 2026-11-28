export type BrowserKind='firefox'|'yandex'|'edge'|'chrome'|'desktop'|'android'|'unsupported';
/** Compatibility hint, not an authentication or authorization control. */
export function classifyBrowser(ua:string,brands:string[]=[],brave=false):BrowserKind{
 if(/Android/i.test(ua)&&/MysticEgoAndroid\/1\b/.test(ua))return 'android';
 if(/Electron\//i.test(ua))return 'desktop';
 if(/YaBrowser\//i.test(ua)||brands.some(x=>/yandex/i.test(x)))return 'yandex';
 if(/Edg(?:A|iOS)?\//i.test(ua)||brands.some(x=>/Microsoft Edge/i.test(x)))return 'edge';
 if(/Waterfox|Floorp|PaleMoon|SeaMonkey|LibreWolf|TorBrowser/i.test(ua))return 'unsupported';
 if(/Firefox\/|FxiOS\//i.test(ua))return 'firefox';
 if(brave||/OPR\/|Opera|Vivaldi|SamsungBrowser|DuckDuckGo|UCBrowser|HuaweiBrowser|Brave|Chromium\//i.test(ua))return 'unsupported';
 if(brands.length&&!brands.some(x=>x==='Google Chrome')&&!/CriOS\//i.test(ua))return 'unsupported';
 return /Chrome\/|CriOS\//i.test(ua)?'chrome':'unsupported';
}
export function browserKind():BrowserKind{
 const nav=navigator as Navigator&{userAgentData?:{brands?:{brand:string}[]};brave?:unknown};
 return classifyBrowser(nav.userAgent,nav.userAgentData?.brands?.map(x=>x.brand)??[],!!nav.brave);
}
export const browserLabels:Record<BrowserKind,string>={firefox:'Mozilla Firefox',yandex:'Яндекс Браузер',edge:'Microsoft Edge',chrome:'Google Chrome',desktop:'MysticEgo Desktop',android:'MysticEgo Mobile',unsupported:'Неподдерживаемый браузер'};
export type BrowserConsent={version:1;desktopDiscovery:boolean;at:string};
const key='mysticego.browser-consent.v1';
let memory:BrowserConsent|null=null;
export function readConsent():BrowserConsent|null{
 try{const value=JSON.parse(localStorage.getItem(key)??'null');if(value?.version===1&&typeof value.desktopDiscovery==='boolean'&&typeof value.at==='string')return value;return null;}catch{return memory;}
}
export function saveConsent(desktopDiscovery:boolean){
 const value:BrowserConsent={version:1,desktopDiscovery,at:new Date().toISOString()};
 memory=value;try{localStorage.setItem(key,JSON.stringify(value))}catch{}
 window.dispatchEvent(new Event('mystic-browser-consent'));return value;
}
export function clearConsent(){memory=null;try{localStorage.removeItem(key)}catch{}window.dispatchEvent(new Event('mystic-browser-consent'))}
