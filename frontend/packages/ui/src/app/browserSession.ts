import {useEffect,useRef,useState} from 'react';
import type {Session} from './context';

type StoredSession=Pick<Session,'url'|'authUrl'|'username'>&{refreshToken?:string;browserSession?:boolean};

function address(value:unknown){
 if(typeof value!=='string')return false;
 try{
  const url=new URL(value);
  return (url.protocol==='https:'||url.protocol==='http:')&&!url.username&&!url.password&&!url.search&&!url.hash;
 }catch{return false;}
}

function read(key:string):StoredSession|null{
 try{
  const saved=JSON.parse(sessionStorage.getItem(key)??'null') as StoredSession|null;
  return saved&&address(saved.authUrl)&&address(saved.url)&&(saved.browserSession===true&&saved.authUrl===location.origin&&location.protocol==='https:'||typeof saved.refreshToken==='string'&&saved.refreshToken.length>0)?saved:null;
 }catch{return null;}
}

function save(key:string,session:Session){
 try{
  if(!session.refreshToken&&!session.browserSession){sessionStorage.removeItem(key);return;}
  const {url,authUrl,username,refreshToken,browserSession}=session;
  sessionStorage.setItem(key,JSON.stringify({url,authUrl,username,browserSession,refreshToken:browserSession?undefined:refreshToken}));
 }catch{/* Browsers may disable storage. */}
}

export async function browserAuthRequest(action:'login'|'refresh'|'logout',body:unknown={}){
 const run=()=>fetch('/api/auth/browser/'+action,{method:'POST',credentials:'same-origin',redirect:'error',cache:'no-store',headers:{'Content-Type':'application/json','X-Mystic-Browser':'1'},body:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
 return navigator.locks?navigator.locks.request('mysticego-browser-auth',run):run();
}
async function revoke(session:Session){
 if(session.browserSession){await browserAuthRequest('logout').catch(()=>{});return;}
 if(!session.refreshToken)return;
 await fetch(session.authUrl.replace(/\/$/,'')+'/api/auth/logout',{
  method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({refreshToken:session.refreshToken}),signal:AbortSignal.timeout(10000)
 }).catch(()=>{});
}

export function useBrowserSession(key:string,initial:Session){
 const [session,update]=useState<Session>(()=>({...initial,...read(key)}));
 const current=useRef(session);
 const [retry,setRetry]=useState(0);
 const [restoreError,setRestoreError]=useState('');
 function setSession(next:Session){
  current.current=next;
  save(key,next);
  update(next);
 }
 function logout(){
  const previous=current.current;
  setRestoreError('');
  setSession({...previous,token:'',refreshToken:undefined,browserSession:undefined,username:undefined});
  void revoke(previous);
 }
 useEffect(()=>{
  if(!session.refreshToken&&!session.browserSession)return;
  let active=true;
  const restoreStarted=Date.now();
  let failures=0;
  let timer:number;
  const schedule=(delay:number)=>{timer=window.setTimeout(refresh,delay);};
  async function refresh(){
   try{
    const response=session.browserSession?await browserAuthRequest('refresh'):await fetch(session.authUrl.replace(/\/$/,'')+'/api/auth/refresh',{
     method:'POST',headers:{'Content-Type':'application/json'},
     body:JSON.stringify({refreshToken:session.refreshToken}),signal:AbortSignal.timeout(12000)
    });
    if([400,401,403].includes(response.status)){
     if(active&&current.current===session)logout();
     return;
    }
    if(!response.ok)throw Error('Сервер входа временно недоступен.');
    const data=await response.json();
    if(typeof data.accessToken!=='string'||!data.accessToken||(!session.browserSession&&(typeof data.refreshToken!=='string'||!data.refreshToken)))
     throw Error('Сервер вернул неполную сессию.');
    const next={...session,token:data.accessToken,refreshToken:session.browserSession?undefined:data.refreshToken,username:data.user?.login??session.username};
    if(!active||current.current!==session){if(!session.browserSession)void revoke(next);return;}
    setRestoreError('');
    setSession(next);
   }catch{
    if(!active||current.current!==session)return;
    if(session.token){schedule(30000);return;}
    if(Date.now()-restoreStarted<120000){
     schedule(Math.min(10000,2000*2**failures++));
     return;
    }
    setRestoreError('Не удалось восстановить вход за две минуты. Повторите проверку, когда сервер будет доступен.');
   }
  }
  setRestoreError('');
  schedule(session.token?9*60*1000:0);
  return ()=>{active=false;window.clearTimeout(timer);};
 },[session,retry,key]);
 return {session,setSession,logout,restoring:!!(session.refreshToken||session.browserSession)&&!session.token,restoreError,retryRestore:()=>setRetry(n=>n+1)};
}

export function accountScope(session:Session,mode:string,portal:boolean){
 if(!session.token)return '';
 try{
  const part=session.token.split('.')[1];
  const payload=JSON.parse(atob(part.replace(/-/g,'+').replace(/_/g,'/')));
  if(typeof payload.sub==='string'&&payload.sub.length>0&&payload.sub.length<160)
   return JSON.stringify([session.authUrl.replace(/\/$/,''),session.url.replace(/\/$/,''),mode,portal,payload.sub]);
 }catch{/* Token claims only namespace local preferences. */}
 return '';
}
