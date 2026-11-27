import {useEffect,useRef,useState} from 'react';
import type {Session} from './context';
import {browserKind,readConsent} from './browserPolicy';
type Product='AuditDesktop'|'AgentDesktop'|'LocalDesktop'|'CloudDesk';
const ports:Record<Product,number>={AuditDesktop:19471,AgentDesktop:19472,LocalDesktop:19473,CloudDesk:19474};
const products=Object.keys(ports) as Product[];
const hex=(bytes:Uint8Array)=>Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');
const secret=/^[a-f0-9]{64}$/;
function localEndpoint(product:Product,path:string){return 'http://127.0.0.1:'+ports[product]+path}
export function DesktopSignIn({session,authUrl,onLogin,disabled,onBusy}:{session:Session;authUrl:string;onLogin:(s:Session)=>void;disabled:boolean;onBusy:(value:boolean)=>void}){
 const [offers,setOffers]=useState<Product[]>([]),[consent,setConsent]=useState(readConsent),[message,setMessage]=useState('');
 const [pending,setPending]=useState(false);
 const active=useRef<AbortController|null>(null);
 const eligible=consent?.desktopDiscovery===true&&location.protocol==='https:'&&browserKind()!=='desktop'&&browserKind()!=='unsupported'&&
  authUrl.replace(/\/$/,'')===location.origin&&session.url.replace(/\/$/,'')===location.origin;
 useEffect(()=>{const sync=()=>setConsent(readConsent());window.addEventListener('mystic-browser-consent',sync);window.addEventListener('storage',sync);return()=>{window.removeEventListener('mystic-browser-consent',sync);window.removeEventListener('storage',sync)}},[]);
 useEffect(()=>{
  setOffers([]);setMessage('');
  if(!eligible)return;
  const controller=new AbortController();
  const timer=window.setTimeout(async()=>{
   const results=await Promise.all(products.map(async product=>{
    try{
     const r=await fetch(localEndpoint(product,'/availability'),{method:'GET',mode:'cors',credentials:'omit',cache:'no-store',redirect:'error',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(1800)])});
     if(!r.ok)return null;
     const data=await r.json();return data?.available===true&&data.product===product?product:null;
    }catch{return null}
   }));
   if(!controller.signal.aborted)setOffers(results.filter((p):p is Product=>p!==null));
  },5000);
  return()=>{clearTimeout(timer);controller.abort();active.current?.abort()};
 },[eligible]);
 async function pair(product:Product){
  if(disabled||pending||!eligible)return;
  const controller=new AbortController();active.current=controller;setPending(true);onBusy(true);setMessage('Подтвердите вход в '+product+'.');
  try{
   const verifier=hex(crypto.getRandomValues(new Uint8Array(32)));
   const challenge=hex(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(verifier))));
   const response=await fetch(localEndpoint(product,'/approve'),{method:'POST',mode:'cors',credentials:'omit',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json'},body:JSON.stringify({challenge}),signal:AbortSignal.any([controller.signal,AbortSignal.timeout(75000)])});
   if(!response.ok)throw Error('Подтверждение не получено.');
   const {code}=await response.json();
   if(typeof code!=='string'||!secret.test(code))throw Error('Некорректный ответ Desktop.');
   if(!readConsent()?.desktopDiscovery||controller.signal.aborted)return;
   const exchange=await fetch(location.origin+'/api/auth/desktop/redeem',{method:'POST',credentials:'omit',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json'},body:JSON.stringify({code,verifier}),signal:AbortSignal.any([controller.signal,AbortSignal.timeout(15000)])});
   if(!exchange.ok)throw Error('Одноразовый вход истёк или сеанс Desktop изменился.');
   const result=await exchange.json();
   if(typeof result.accessToken!=='string'||!result.accessToken||typeof result.refreshToken!=='string'||!result.refreshToken||typeof result.user?.login!=='string'||!result.user.login)throw Error('Сервер вернул неполный сеанс.');
   if(controller.signal.aborted||!readConsent()?.desktopDiscovery||!window.confirm('Войти в браузере под аккаунтом '+result.user.login+'?')){
    void fetch(location.origin+'/api/auth/logout',{method:'POST',credentials:'omit',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json'},body:JSON.stringify({refreshToken:result.refreshToken}),signal:AbortSignal.timeout(5000)}).catch(()=>{});return;
   }
   onLogin({...session,authUrl:location.origin,token:result.accessToken,refreshToken:result.refreshToken,username:result.user.login});
  }catch(e){if(!controller.signal.aborted)setMessage(e instanceof Error?e.message:'Не удалось подтвердить вход.')}
  finally{if(active.current===controller){active.current=null;setPending(false);onBusy(false)}}
 }
 if(!eligible||!offers.length)return null;
 return <section className="panel" aria-label="Вход из Desktop"><h3>Продолжить из Desktop</h3><p>На этом устройстве доступно приложение с активным сеансом.</p>{offers.map(p=><button type="button" className="btn secondary" key={p} disabled={disabled||pending} onClick={()=>void pair(p)}>Войти через {p}</button>)}{message&&<p role="status">{message}</p>}</section>
}
