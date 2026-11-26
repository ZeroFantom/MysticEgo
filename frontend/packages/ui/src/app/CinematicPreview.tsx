import {useEffect,useState} from 'react';
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
 const [opened,setOpened]=useState(false);
 useEffect(()=>setOpened(false),[source,allowed]);
 if(!allowed||!source)return null;
 return <section className="cinematic-preview" aria-label="Синематика оформления">
 <div><p>Короткая синематика · MysticEgo</p><button type="button" aria-expanded={opened} onClick={()=>setOpened(v=>!v)}>{opened?'Скрыть видео':'Смотреть видео'}</button></div>
 {opened&&<video key={source} controls muted playsInline preload="metadata" poster={poster} src={source} aria-label="Анимированный пейзаж MysticEgo"/>}
 </section>;
}
