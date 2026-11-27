import {useEffect,useState} from 'react';
import type {NodeMode} from '@mysticego/sdk';
import './release-highlights.css';

declare const __MYSTIC_BUILD_RELEASE__: {tag:string;notes:string}|undefined;
const build=typeof __MYSTIC_BUILD_RELEASE__==='undefined'?undefined:__MYSTIC_BUILD_RELEASE__;
const tagPattern=/^v[0-9][A-Za-z0-9._-]{0,80}$/;
function readSeen(key:string){try{return localStorage.getItem(key)}catch{return null}}
export function ReleaseHighlights({mode,portal}:{mode:NodeMode;portal:boolean}){
 const key='mysticego.release-highlights.'+mode+(portal?'.portal':'');
 const [seen,setSeen]=useState(()=>readSeen(key));
 const [available,setAvailable]=useState<string|null>(null);
 useEffect(()=>{
  if(!build)return;
  let active=true,running=false;
  const controller=new AbortController();
  async function check(){
   if(!active||running||document.hidden)return;
   running=true;
   try{
    const response=await fetch(portal?'/audit-portal/release-info.json':'/release-info.json',{cache:'no-store',credentials:'omit',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(10000)])});
    if(!response.ok||!response.headers.get('content-type')?.includes('json'))return;
    const info=await response.json();
    if(active&&typeof info.installed==='string'&&tagPattern.test(info.installed))setAvailable(info.installed===build?.tag?null:info.installed);
   }catch{}finally{running=false}
  }
  const sync=(event:StorageEvent)=>{if(event.key===key)setSeen(event.newValue)};
  void check();const timer=window.setInterval(()=>void check(),60000);
  window.addEventListener('focus',check);window.addEventListener('storage',sync);
  document.addEventListener('visibilitychange',check);
  return()=>{active=false;controller.abort();window.clearInterval(timer);window.removeEventListener('focus',check);window.removeEventListener('storage',sync);document.removeEventListener('visibilitychange',check)};
 },[key,portal]);
 if(!build)return null;
 const showNotes=seen!==build.tag&&!!build.notes.trim();
 if(!available&&!showNotes)return null;
 function dismiss(){if(!build)return;try{localStorage.setItem(key,build.tag)}catch{}setSeen(build.tag)}
 return <aside className="release-highlights-panel" aria-label="Обновления MysticEgo">
  {available?<div><strong>Панель обновлена до {available}</strong><p>Сохраните текущую работу и обновите страницу, чтобы открыть новую версию.</p><button className="btn" type="button" onClick={()=>location.reload()}>Обновить страницу</button></div>:null}
  {showNotes&&!available?<><div className="release-highlights-heading"><strong>Что нового · {build.tag}</strong><button className="btn secondary" type="button" onClick={dismiss}>Ознакомился</button></div><pre>{build.notes}</pre></>:null}
 </aside>;
}
