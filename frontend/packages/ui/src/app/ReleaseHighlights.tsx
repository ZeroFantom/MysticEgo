import {useEffect,useRef,useState} from 'react';
import {Sparkles, X, ArrowUpRight} from 'lucide-react';
import {useLocale,usePresentation} from './presentation';
import {useBrowserConsentReady} from './BrowserPreferences';
import type {NodeMode} from '@mysticego/sdk';
import './release-highlights.css';

declare const __MYSTIC_BUILD_RELEASE__: {tag:string;notes:string}|undefined;
const build=typeof __MYSTIC_BUILD_RELEASE__==='undefined'?undefined:__MYSTIC_BUILD_RELEASE__;
const tagPattern=/^v[0-9][A-Za-z0-9._-]{0,80}$/;
function readSeen(key:string){try{return localStorage.getItem(key)}catch{return null}}
export function ReleaseHighlights({mode,portal}:{mode:NodeMode;portal:boolean}){
 const t=useLocale(),{value}=usePresentation(),ready=useBrowserConsentReady();
 const dialog=useRef<HTMLDialogElement>(null);
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
 const showNotes=!!build&&seen!==build.tag&&!!build.notes.trim()&&!available&&ready;
 useEffect(()=>{const el=dialog.current;if(showNotes&&el&&!el.open)el.showModal();else if(!showNotes&&el?.open)el.close()},[showNotes]);
 if(!build)return null;
 function dismiss(){if(!build)return;try{localStorage.setItem(key,build.tag)}catch{}setSeen(build.tag)}
 return <>
 {available&&ready&&<aside className="release-update-toast" aria-label={t('Обновления MysticEgo')}><strong>{t('Доступна новая версия')} · {available}</strong><p>{t('Сохраните текущую работу и обновите страницу, чтобы открыть новую версию.')}</p><button className="btn" type="button" onClick={()=>location.reload()}>{t('Обновить страницу')}<ArrowUpRight size={16}/></button></aside>}
 <dialog ref={dialog} className="release-highlights-panel" aria-labelledby="release-title" onCancel={e=>{e.preventDefault();dismiss()}}>
 <header className="release-highlights-heading"><div className="release-emblem"><Sparkles size={28}/></div><button className="icon-btn release-dismiss" type="button" aria-label={t('Закрыть')} onClick={dismiss}><X size={22}/></button><span className="release-version">{build.tag}</span><h2 id="release-title">{t('Что нового')}</h2><p>{t('MysticEgo стал лучше. Узнайте, что изменилось в этой версии.')}</p></header>
 <div className="release-notes" tabIndex={0} aria-label={t('Изменения версии')}>
 {value.language!=='ru'&&<p className="release-original">{t('Описание релиза доступно на русском языке.')}</p>}
 <div lang="ru">{build.notes.split(/\r?\n/).filter(line=>line.trim()&&!/^#\s/.test(line)).map((line,i)=>line.startsWith('## ')?<h3 key={i}>{line.slice(3)}</h3>:line.startsWith('- ')?<div key={i} className="release-note-item"><span aria-hidden="true"/><p>{line.slice(2)}</p></div>:<p key={i}>{line}</p>)}</div>
 </div>
 <footer className="release-footer"><span>{t('Спасибо, что используете MysticEgo.')}</span><button className="btn" type="button" onClick={dismiss}>{t('Ознакомился')}</button></footer>
 </dialog></>;
}
