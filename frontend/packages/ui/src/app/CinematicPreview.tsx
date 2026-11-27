import {useEffect,useId,useRef,useState,type CSSProperties} from 'react';
import darkPoster from './appearance-art/immersive-dark-hero.webp';
import lightPoster from './appearance-art/immersive-light-hero.webp';
import simplePoster from './appearance-art/simplified-light-hero.webp';
import simpleDarkPoster from './appearance-art/simplified-dark-hero.webp';

function useVariant(){
 const read=()=>{const d=document.documentElement.dataset;return {theme:d.theme??'dark',style:d.interface??'simplified',motion:d.uiMotion??'normal',reduced:matchMedia('(prefers-reduced-motion: reduce)').matches}};
 const [variant,setVariant]=useState(read);
 useEffect(()=>{const refresh=()=>setVariant(read());const observer=new MutationObserver(refresh);observer.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme','data-interface','data-ui-motion']});const media=matchMedia('(prefers-reduced-motion: reduce)');media.addEventListener('change',refresh);refresh();return()=>{observer.disconnect();media.removeEventListener('change',refresh)}},[]);
 return variant;
}
const particles=Array.from({length:18},(_,i)=>({
 x:18+(i*37)%78,y:12+(i*29)%72,dx:-(18+(i*13)%64),dy:16+(i*19)%52,
 size:3+(i%4),delay:(i%6)*.55
}));
function Scene({poster,immersive,light,allowed}:{poster:string;immersive:boolean;light:boolean;allowed:boolean}){
 const mediaId=useId();
 const frame=useRef<HTMLDivElement>(null);
 const [state,setState]=useState<'idle'|'playing'|'paused'|'finished'>('idle');
 const [visible,setVisible]=useState(false);
 const [pageVisible,setPageVisible]=useState(()=>!document.hidden);
 useEffect(()=>{
   const refresh=()=>setPageVisible(!document.hidden);
   document.addEventListener('visibilitychange',refresh);
   const observer=typeof IntersectionObserver==='undefined'?null:new IntersectionObserver(entries=>setVisible(!!entries[0]?.isIntersecting),{threshold:.05});
   if(frame.current)observer?.observe(frame.current);
   if(!observer)setVisible(true);
   return()=>{observer?.disconnect();document.removeEventListener('visibilitychange',refresh)};
 },[]);
 useEffect(()=>setState('idle'),[poster,allowed]);
 const active=allowed&&(state==='playing'||state==='paused');
 const running=active&&state==='playing'&&visible&&pageVisible;
 const status=!allowed?'Статический пейзаж · анимации ограничены':state==='finished'?'Сцена завершена':active?(running?'Воспроизведение':'Пауза'):'12 секунд · свет и движение частиц';
 return <section className="cinematic-preview" aria-label="Синематика оформления">
   <div><p>Живой пейзаж · MysticEgo</p>{allowed&&<div className="cinematic-controls">
     <button type="button" aria-controls={mediaId} onClick={()=>setState(s=>s==='playing'?'paused':s==='paused'?'playing':'playing')}>{state==='playing'?'Пауза':state==='paused'?'Продолжить':state==='finished'?'Повторить сцену':'Смотреть сцену'}</button>
     {active&&<button type="button" onClick={()=>setState('idle')}>Остановить</button>}
   </div>}</div>
   <div id={mediaId} ref={frame} className="cinematic-media cinematic-live" data-playing={active} data-running={running} data-light={light} data-immersive={immersive}>
     <img src={poster} loading="lazy" decoding="async" alt="Пейзаж оформления MysticEgo"/>
     {active&&<div className="cinematic-effects" aria-hidden="true">
       <div className="cinematic-haze" onAnimationEnd={event=>{if(event.animationName==='mystic-scene-haze')setState('finished')}}/>
       <div className="cinematic-glow"/>
       {particles.slice(0,immersive?18:8).map((p,i)=><i key={i} className="cinematic-petal" style={{left:p.x+'%',top:p.y+'%','--petal-x':p.dx+'px','--petal-y':p.dy+'px','--petal-size':p.size+'px','--petal-delay':p.delay+'s'} as CSSProperties}/>)}
     </div>}
   </div>
   <small className="cinematic-motion-note" role="status">{status}</small>
 </section>;
}
export function CinematicPreview(){
 const variant=useVariant();
 const immersive=variant.style==='immersive',light=variant.theme==='light';
 const poster=immersive?(light?lightPoster:darkPoster):(light?simplePoster:simpleDarkPoster);
 const allowed=!variant.reduced&&variant.motion==='normal';
 return <Scene poster={poster} immersive={immersive} light={light} allowed={allowed}/>;
}
