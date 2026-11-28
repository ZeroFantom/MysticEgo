import {useEffect,useRef,useState} from 'react';
import {AvatarStage,type AvatarMotion} from './AvatarStage';
import {usePersonalAvatar,useAvatarMedia,defaultAppearance,accountAvatar} from './PersonalAvatar';
import {useApp} from '../app/context';
import './mobile-scene.css';
import {AvatarTransfer} from './AvatarTransfer';
import {useIncomingAvatar,type Snapshot} from './avatarTransferState';
const motions=['idle','wave','nod','dance','sit','walk'] as const satisfies readonly AvatarMotion[];
export function MobileScene(){
 const {mode}=useApp();
 const library=usePersonalAvatar(mode==='audit');
 const selection=library.value?.selection;
 const media=useAvatarMedia(selection?.assetId??'bundled',selection?.motions,library.value?.assets,(library.value?.path??accountAvatar)+'/asset/',undefined,!library.loading);
 const [active,setActive]=useState(false),[motion,setMotion]=useState<AvatarMotion>('idle'),[play,setPlay]=useState(0),[error,setError]=useState('');
 const [imported,setImported]=useState<Snapshot|null>(null);
 useIncomingAvatar('ar',value=>{setImported(value);setMotion(value.motion);setPlay(x=>x+1)});
 const playback=useRef(0);
 const [position,setPosition]=useState({x:50,y:52}),[scale,setScale]=useState(1);
 const video=useRef<HTMLVideoElement>(null),stream=useRef<MediaStream|null>(null),generation=useRef(0);
 function stop(){generation.current++;stream.current?.getTracks().forEach(t=>t.stop());stream.current=null;setActive(false);}
 useEffect(()=>{const hide=()=>{if(document.hidden)stop()};document.addEventListener('visibilitychange',hide);return()=>{document.removeEventListener('visibilitychange',hide);generation.current++;stream.current?.getTracks().forEach(t=>t.stop())}},[]);
 async function start(){if(!navigator.mediaDevices?.getUserMedia){setError('Камера недоступна.');return}const id=++generation.current;setError('');setActive(true);try{const camera=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'},width:{ideal:1280},height:{ideal:720}},audio:false});if(id!==generation.current){camera.getTracks().forEach(t=>t.stop());return}stream.current=camera;if(!video.current)throw Error();video.current.srcObject=camera;await video.current.play();}catch{if(id===generation.current){stop();setError('Не удалось открыть камеру. Проверьте разрешение.')}}}
 return <section className="panel stack"><h2>VRM · Камера (AR-превью)</h2><a className="btn secondary" href="mysticego://spatial">Пространственный AR в Chrome</a><p>Для пространственного режима нужен поддерживаемый ARCore телефон и Google Chrome; вход в браузере отдельный.</p><p>Персонаж отображается поверх изображения камеры. Кадры не отправляются на сервер. Это экранное размещение, без привязки к поверхности или пространственного трекинга.</p>
 {!active?<button className="btn" onClick={()=>void start()}>Открыть камеру</button>:<>
 <div className="mobile-ar" onPointerDown={e=>{const r=e.currentTarget.getBoundingClientRect();setPosition({x:Math.max(15,Math.min(85,(e.clientX-r.left)/r.width*100)),y:Math.max(20,Math.min(80,(e.clientY-r.top)/r.height*100))})}}>
 <video ref={video} playsInline muted autoPlay/><div className="mobile-ar-avatar" style={{left:position.x+'%',top:position.y+'%',width:Math.round(68*scale)+'%',height:Math.round(86*scale)+'%'}}><AvatarStage url={media.model?.url} mime={media.model?.mime} motion={motion} mood={imported?.mood??'neutral'} initialPhase={imported?.phaseSeconds??0} onPlayback={(_,seconds)=>{playback.current=seconds}} play={play} appearance={{...defaultAppearance,...selection?.appearance}} animations={media.clips}/></div>
 </div><label>Размер персонажа<input type="range" min={0.5} max={1.2} step={0.1} value={scale} onChange={e=>setScale(Number(e.target.value))}/></label>
 <div className="actions">{motions.map(m=><button className="btn secondary" key={m} aria-pressed={motion===m} onClick={()=>{setMotion(m);setPlay(x=>x+1)}}>{{idle:'Стоять',wave:'Приветствие',nod:'Кивок',dance:'Танец',sit:'Сесть',walk:'Шаги'}[m]}</button>)}</div><button className="btn secondary" onClick={stop}>Закрыть камеру</button></>}
 {!active&&<AvatarTransfer snapshot={()=>({motion:motion as Snapshot['motion'],mood:imported?.mood??'neutral',phaseSeconds:Math.min(3600,playback.current)})}/>}
 {error&&<p role="alert">{error}</p>}</section>;
}
