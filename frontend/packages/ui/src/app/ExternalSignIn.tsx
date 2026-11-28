import {useEffect,useRef,useState} from 'react';
import type {Session} from './context';
import {browserAuthRequest} from './browserSession';
import {useLocale} from './presentation';

export type ExternalProvider='telegram'|'vk';
const path=(provider:ExternalProvider)=>'/api/auth/browser/external/'+provider+'/';
const marker='mysticego-external-login-pending';
const eligibleOrigin=(session:Session,authUrl:string)=>location.protocol==='https:'&&authUrl.replace(/\/$/,'')===location.origin&&session.url.replace(/\/$/,'')===location.origin;
export function providerAuthorization(provider:ExternalProvider,value:unknown):string|null{
 if(typeof value!=='string')return null;
 try{const u=new URL(value);return u.origin===(provider==='vk'?'https://id.vk.com':'https://oauth.telegram.org')&&u.pathname===(provider==='vk'?'/authorize':'/auth')&&!u.username&&!u.password&&!u.hash?u.href:null}catch{return null}
}
function takeReturn(){
 const u=new URL(location.href);
 if(u.searchParams.get('externalLogin')!=='1')return false;
 u.searchParams.delete('externalLogin');history.replaceState(history.state,'',u.pathname+u.search+u.hash);
 try{const stamp=Number(sessionStorage.getItem(marker));sessionStorage.removeItem(marker);return stamp>0&&Date.now()>=stamp&&Date.now()-stamp<=360000}catch{return false}
}
export function ExternalSignIn({session,authUrl,onLogin,disabled,onBusy}:{session:Session;authUrl:string;onLogin:(s:Session)=>void;disabled:boolean;onBusy:(value:boolean)=>void}){
 const t=useLocale(),[enabled,setEnabled]=useState<ExternalProvider[]>([]),[pending,setPending]=useState(false),[problem,setProblem]=useState('');
 const [returned,setReturned]=useState(false),started=useRef(false),active=useRef(true);
 const eligible=eligibleOrigin(session,authUrl)&&!session.token&&!session.refreshToken&&!session.browserSession;
 useEffect(()=>{
  active.current=true;
  return()=>{active.current=false};
 },[]);
 useEffect(()=>{
  if(eligible&&!started.current){started.current=true;setReturned(takeReturn())}
  setEnabled([]);
  if(!eligible)return;
  const c=new AbortController();
  for(const provider of ['telegram','vk'] as const)void fetch(path(provider)+'status',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:AbortSignal.any([c.signal,AbortSignal.timeout(8000)])})
   .then(async r=>{if(r.ok){const d=await r.json();if(!c.signal.aborted&&d?.enabled===true)setEnabled(old=>old.includes(provider)?old:[...old,provider])}}).catch(()=>{});
  return()=>c.abort();
 },[eligible]);
 async function run(work:()=>Promise<void>){
  if(disabled||pending||!eligible)return;
  setPending(true);onBusy(true);setProblem('');
  try{await work()}catch{if(active.current)setProblem(t('Внешний вход недоступен или истёк. Войдите по паролю либо начните заново.'))}
  finally{if(active.current){setPending(false);onBusy(false)}}
 }
 async function start(provider:ExternalProvider){
  const r=await fetch(path(provider)+'start',{method:'POST',credentials:'same-origin',redirect:'error',cache:'no-store',headers:{'Content-Type':'application/json','X-Mystic-Browser':'1'},body:'{}',signal:AbortSignal.timeout(12000)});
  if(!r.ok)throw Error();
  const url=providerAuthorization(provider,(await r.json()).authorizationUrl);
  if(!url||!active.current)throw Error();
  // This is only a tab-local UX marker, not authentication evidence or a secret.
  sessionStorage.setItem(marker,String(Date.now()));
  location.assign(url);
 }
 async function resume(){
  const r=await browserAuthRequest('refresh');
  if(!r.ok)throw Error();
  const d=await r.json();
  if(typeof d.accessToken!=='string'||!d.accessToken||typeof d.user?.login!=='string'||!d.user.login||'refreshToken' in d)throw Error();
  if(!active.current||!window.confirm(t('Войти в MysticEgo под аккаунтом')+' '+d.user.login+'?')){
   await browserAuthRequest('logout').catch(()=>{});if(active.current)setReturned(false);return;
  }
  onLogin({...session,authUrl:location.origin,token:d.accessToken,refreshToken:undefined,browserSession:true,username:d.user.login});
 }
 if(!eligible||!enabled.length&&!returned)return null;
 return <section className="panel stack" aria-label={t('Внешний вход')}>
  {returned?<><p>{t('Авторизация завершена. Подтвердите вход в свой аккаунт.')}</p><button type="button" className="btn" disabled={disabled||pending} onClick={()=>void run(resume)}>{t('Продолжить вход')}</button></>:
   <>{(['telegram','vk'] as const).filter(p=>enabled.includes(p)).map(provider=><button key={provider} type="button" className="btn secondary" disabled={disabled||pending} onClick={()=>void run(()=>start(provider))}>{t(provider==='vk'?'Войти через VK ID':'Войти через Telegram')}</button>)}</>}
  {problem&&<p role="alert">{problem}</p>}
 </section>
}
