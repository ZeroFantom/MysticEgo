import {useEffect,useRef,useSyncExternalStore} from 'react';
import {catalog} from './localeCatalog';
export type Language='en'|'zh-CN'|'ko'|'ru';
export type Font='modern'|'editorial'|'system'|'mono';
export type Presentation={language:Language;font:Font};
export const languages=[['en','English'],['zh-CN','简体中文'],['ko','한국어'],['ru','Русский']] as const;
export const fonts=[['modern','Современный'],['editorial','Редакционный'],['system','Системный'],['mono','Моноширинный']] as const;
const defaults:Presentation={language:'en',font:'modern'};
function normalize(v:Record<string,unknown>):Presentation{return {
 language:languages.some(([id])=>id===v.language)?v.language as Language:defaults.language,
 font:fonts.some(([id])=>id===v.font)?v.font as Font:defaults.font
}}
function guest(){try{return normalize(JSON.parse(localStorage.getItem('mysticego.guest.presentation')||'{}'))}catch{return defaults}}
let current=guest(),problem='',revision=0;
const listeners=new Set<()=>void>();
const subscribe=(fn:()=>void)=>{listeners.add(fn);return()=>{listeners.delete(fn)}};
function publish(){document.documentElement.lang=current.language;document.documentElement.dataset.font=current.font;revision++;listeners.forEach(fn=>fn())}
export function t(source:string,language=current.language){const row=Object.hasOwn(catalog,source)?catalog[source]:undefined;return !row?source:language==='ru'?(row[3]??source):row[language==='en'?0:language==='zh-CN'?1:2]}
export function useLocale(){useSyncExternalStore(subscribe,()=>revision,()=>0);return t}
export function usePresentation(){useLocale();return {value:current,problem}}
let active: {send:(patch:Record<string,unknown>)=>void}|undefined;
export function changePresentation(patch:Partial<Presentation>){current=normalize({...current,...patch});publish();
 if(active)active.send(patch);else try{localStorage.setItem('mysticego.guest.presentation',JSON.stringify(current))}catch{}
}
export function syncAppearance(patch:Record<string,unknown>){active?.send(patch)}
export function useAccountPresentation(authUrl:string,token:string,scope:string,applyAppearance:(value:Record<string,unknown>)=>void){
 const tokenRef=useRef({token,scope,authUrl});tokenRef.current={token,scope,authUrl};
 useEffect(()=>{
  let alive=true;let dirty:Record<string,unknown>={};let chain=Promise.resolve();
  problem='';current=token?defaults:guest();publish();
  if(!token||!scope){active=undefined;return}
  const endpoint=authUrl.replace(/\/$/,'')+'/api/auth/presentation';
  const request=async(patch?:Record<string,unknown>)=>{
   if(tokenRef.current.scope!==scope||tokenRef.current.authUrl!==authUrl||!tokenRef.current.token)throw Error('Account changed');
   const response=await fetch(endpoint,{method:patch?'POST':'GET',headers:{Authorization:'Bearer '+tokenRef.current.token,...(patch?{'Content-Type':'application/json'}:{})},body:patch?JSON.stringify(patch):undefined,signal:AbortSignal.timeout(10000),redirect:'error'});
   if(!response.ok)throw Error('HTTP '+response.status);return await response.json() as Record<string,unknown>
  };
  const binding={send(patch:Record<string,unknown>){Object.assign(dirty,patch);chain=chain.then(async()=>{
   if(!alive||tokenRef.current.scope!==scope||!Object.keys(dirty).length)return;const batch=dirty;dirty={};
   try{await request(batch);if(alive){problem='';publish()}}catch{if(alive){dirty={...batch,...dirty};problem='Настройки не синхронизированы';publish()}}
  })}};
  active=binding;
  chain=request().then(data=>{if(!alive)return;const merged={...data,...dirty};current=normalize(merged);applyAppearance(merged);publish()}).catch(()=>{if(alive){problem='Настройки не синхронизированы';publish()}});
  return()=>{alive=false;if(active===binding)active=undefined}
 },[authUrl,scope,Boolean(token)]);
}
