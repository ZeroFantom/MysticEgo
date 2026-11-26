import {useEffect,useId,useRef,useState} from 'react';
import dark from './appearance-art/immersive-dark.webm';
import light from './appearance-art/immersive-light.webm';
import simple from './appearance-art/simplified-light.webm';
import darkPoster from './appearance-art/immersive-dark-entry.webp';
import lightPoster from './appearance-art/immersive-light-entry.webp';
import simplePoster from './appearance-art/simplified-light.webp';
function useVariant(){
 const read=()=>{const d=document.documentElement.dataset;return {theme:d.theme??'dark',style:d.interface??'simplified',motion:d.uiMotion??'normal',reduced:matchMedia('(prefers-reduced-motion: reduce)').matches}};
 const [variant,setVariant]=useState(read);
 useEffect(()=>{const refresh=()=>setVariant(read());const observer=new MutationObserver(refresh);observer.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme','data-interface','data-ui-motion']});const media=matchMedia('(prefers-reduced-motion: reduce)');media.addEventListener('change',refresh);refresh();return()=>{observer.disconnect();media.removeEventListener('change',refresh)}},[]);
 return variant;
}
export function CinematicPreview(){
 const variant=useVariant();
 const source=variant.style==='immersive'?(variant.theme==='light'?light:dark):variant.theme==='light'?simple:null;
 const poster=variant.style==='immersive'?(variant.theme==='light'?lightPoster:darkPoster):simplePoster;
 const allowed=!!source&&!variant.reduced&&variant.motion==='normal';
 const [openSource,setOpenSource]=useState<string|null>(null);
 const opened=allowed&&source===openSource;
 const video=useRef<HTMLVideoElement>(null);
 const mediaId=useId();
 useEffect(()=>{setOpenSource(null)},[source,allowed]);
 useEffect(()=>{
  if(!opened)return;
  const element=video.current;
  if(!element)return;
  const pauseHidden=()=>{if(document.hidden)element.pause()};
  document.addEventListener('visibilitychange',pauseHidden);
  const observer=typeof IntersectionObserver==='undefined'?null:new IntersectionObserver(entries=>{if(!entries[0]?.isIntersecting)element.pause()},{threshold:0.05});
  observer?.observe(element);
  return()=>{observer?.disconnect();document.removeEventListener('visibilitychange',pauseHidden);element.pause()};
 },[opened]);
 if(!source)return null;
 return <section className="cinematic-preview" aria-label="Синематика оформления">
 <div><p>Короткая синематика · MysticEgo</p>{allowed&&<button type="button" aria-controls={mediaId} aria-expanded={opened} onClick={()=>setOpenSource(opened?null:source)}>{opened?'Скрыть видео':'Смотреть видео'}</button>}</div>
 <div id={mediaId} className="cinematic-media">
 {opened?<video ref={video} key={source} controls muted playsInline preload="metadata" poster={poster} src={source} aria-label="Анимированный пейзаж MysticEgo"/>:<img src={poster} loading="lazy" decoding="async" alt="Статический пейзаж оформления MysticEgo"/>}
 </div>
 {!allowed&&<small className="cinematic-motion-note">Статический кадр · анимации отключены или ограничены</small>}
 </section>;
}
