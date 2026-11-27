import React,{useEffect,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {AvatarStage,type AvatarMotion} from '../../../frontend/packages/ui/src/avatar/AvatarStage';
import type {ClipAsset} from '../../../frontend/packages/ui/src/avatar/AvatarClips';
import type {AvatarAppearance} from '../../../frontend/packages/ui/src/avatar/PersonalAvatar';
import {useFaceTracking} from '../../../frontend/packages/ui/src/avatar/faceTracking';
import {inferVisualMood,useAvatarVoice,useMusicBeat} from '../../../frontend/packages/ui/src/avatar/avatarAudio';
import {startVoiceInput} from '../../../frontend/packages/ui/src/avatar/avatarSpeechInput';
import './overlay.css';

type Service='audit'|'agent';
type SystemWindow={title:string;x:number;y:number;width:number;height:number};
type Proposal={id:string;type:string;argument:string};
type Metadata={assetId?:string;motions?:Record<string,string>;motionStyle?:string;appearance?:AvatarAppearance};
type Asset={id:string;extension:string};
type Persona={voice?:boolean;moveDesktop?:boolean;music?:'auto'|'dance'|'listen';mood?:string};
type Reply={ok:boolean;status:number;data:Uint8Array};
declare global {
 interface Window{mysticDesktop?:{
  context:()=>Promise<{api:string;scope:Service;handoff?:{token:string;motion:'walk'|'perch';horizontal:number}|null}>;
  handoffReady:()=>Promise<boolean>;
  login:(login:string,password:string)=>Promise<string>;
  request:(options:{api:string;path:string;token?:string;guest?:string;method?:string;body?:string})=>Promise<Reply>;
  taskbar:()=>Promise<boolean>;windows:()=>Promise<SystemWindow[]>;anchor:(title:string)=>Promise<boolean>;
  walk:(enabled:boolean)=>Promise<boolean>;onEdge:(handler:()=>void)=>()=>void;
  openLink:(url:string)=>Promise<void>;clickThrough:(value:boolean)=>Promise<boolean>;close:()=>Promise<boolean>
 }}
}
const service=window.mysticDesktop;
const decode=<T,>(result:Reply):T=>JSON.parse(new TextDecoder().decode(result.data)) as T;

type OverlayLook={theme:'dark'|'light'|'system';interfaceStyle:'simplified'|'immersive';uiMotion:'normal'|'low'|'off';transition:'flow'|'fade'|'slide'|'scale'};
const overlayDefaults:OverlayLook={theme:'dark',interfaceStyle:'simplified',uiMotion:'normal',transition:'flow'};
function readOverlayLook():OverlayLook{try{return {...overlayDefaults,...JSON.parse(localStorage.getItem('mysticego.overlay.appearance')||'{}')}}catch{return overlayDefaults}}
function Overlay(){
 const [look,setLook]=useState<OverlayLook>(readOverlayLook);
 useEffect(()=>{const media=matchMedia('(prefers-color-scheme:light)');const apply=()=>{const root=document.documentElement;root.dataset.theme=look.theme==='system'?(media.matches?'light':'dark'):look.theme;root.dataset.interface=look.interfaceStyle;root.dataset.uiMotion=look.uiMotion;root.dataset.transition=look.transition;};apply();media.addEventListener('change',apply);try{localStorage.setItem('mysticego.overlay.appearance',JSON.stringify(look))}catch{}return()=>media.removeEventListener('change',apply)},[look]);
 const [handoff,setHandoff]=useState<'walk'|'perch'|null>(null);
 const [scope,setScope]=useState<Service>('audit'),[api,setApi]=useState('http://127.0.0.1:5350');
 const [login,setLogin]=useState(''),[password,setPassword]=useState(''),[token,setToken]=useState(''),[guest,setGuest]=useState('');
 const [model,setModel]=useState<{url?:string;mime:string}>({mime:'.vrm'});
 const [animations,setAnimations]=useState<Partial<Record<AvatarMotion,ClipAsset>>>({});
 const [persona,setPersona]=useState<Persona>({});
 const [motionStyle,setMotionStyle]=useState('calm');
 const [appearance,setAppearance]=useState<AvatarAppearance>();
 const [error,setError]=useState(''),[windows,setWindows]=useState<SystemWindow[]>([]),[anchor,setAnchor]=useState('');
 const [motion,setMotion]=useState<AvatarMotion>('idle'),[text,setText]=useState(''),[answer,setAnswer]=useState('');
 const [settings,setSettings]=useState(true),[walking,setWalking]=useState(false),[busy,setBusy]=useState(false),[proposals,setProposals]=useState<Proposal[]>([]);
 const voice=useAvatarVoice(),music=useMusicBeat(),face=useFaceTracking();

 useEffect(()=>{void service?.context().then(value=>{if(value.api){setApi(value.api);setScope(value.scope)}if(value.handoff){setModel({mime:'.vrm'});setToken(value.handoff.token);setHandoff(value.handoff.motion);setSettings(false)}})},[]);
 useEffect(()=>{
  const dispose=service?.onEdge(()=>{setMotion('bump');window.setTimeout(()=>setMotion(current=>current==='bump'?'walk':current),780)});
  return()=>dispose?.();
 },[]);
 useEffect(()=>{
  if(!handoff||!model.url||!token||!service)return;
  if(persona.moveDesktop===false){setError('Движение на рабочем столе отключено.');return;}
  const next=handoff;
  setHandoff(null);setMotion(next);setWalking(next==='walk');setAnchor('');
  void service.handoffReady().then(ok=>{if(ok&&next==='walk')return service.walk(true);return undefined}).catch(error=>setError(String(error)));
 },[handoff,model.url,token,persona.moveDesktop]);
 useEffect(()=>{
  if(!anchor)return;
  const interval=window.setInterval(()=>void service?.anchor(anchor),1400);
  return()=>clearInterval(interval);
 },[anchor]);
 useEffect(()=>{
  if(!service||!api)return;
  let cancelled=false;const objects:string[]=[];
  const request=(path:string)=>service.request({api,path,token,guest});
  async function media(path:string){
   const response=await request(path);
   if(!response.ok||cancelled)return;
   const url=URL.createObjectURL(new Blob([new Uint8Array(response.data)]));
   objects.push(url);return url;
  }
  void (async()=>{
   let meta:Metadata|undefined,assets:Asset[]=[],organization=false,library='/api/v1/avatars/personal';
   if(token){
    if(scope==='audit'){
     const account=await request('/api/v1/avatars/account/profile');
     if(account.ok){const data=decode<{selection?:Metadata;assets:Asset[]}>(account);if(data.selection){meta=data.selection;assets=data.assets;library='/api/v1/avatars/account'}}
    }
    if(!meta){
     const personal=await request(library+'/profile');
     if(personal.ok){const data=decode<{selection?:Metadata;assets:Asset[]}>(personal);meta=data.selection;assets=data.assets}
    }
    if(!meta&&scope==='agent'){
     const response=await request('/api/v1/avatars/theme');
     if(response.ok){
      const data=decode<{theme?:{modelId:string};assets:Asset[]}>(response);
      if(data.theme){organization=true;meta={assetId:data.theme.modelId};assets=data.assets}
     }
    }
    const response=await request('/api/v1/assistant/persona');
    if(response.ok&&!cancelled)setPersona(decode<Persona>(response));
   }
   const id=meta?.assetId;
   const route=organization?'/api/v1/avatars/theme/assets/':library+'/asset/';
   const url=id&&id!=='bundled'?await media(route+id):await media('/bundled/mysticego.vrm');
   if(cancelled)return;
   setModel({url,mime:assets.find(item=>item.id===id)?.extension||'.vrm'});
   setMotionStyle(meta?.motionStyle??'calm');
   setAppearance(meta?.appearance);setMotion(meta?.appearance?.idleMotion??'idle');
   const tracks=await Promise.all(Object.entries(meta?.motions??{}).map(async ([slot,asset])=>{
    const file=await media(library+'/asset/'+asset);
    const item=assets.find(candidate=>candidate.id===asset);
    return file&&item?[[slot,{url:file,extension:item.extension}]]:[];
   }));
   if(!cancelled)setAnimations(Object.fromEntries(tracks.flat()));
  })().catch(error=>{if(!cancelled)setError(String(error))});
  return()=>{cancelled=true;objects.forEach(URL.revokeObjectURL)};
 },[api,token,guest,scope]);
 async function signIn(){
  try{const access=await service?.login(login,password);setToken(access??'');setGuest('');setPassword('');setError('')}
  catch(error){setError(String(error))}
 }
 async function connectGuest(){
  const r=await service!.request({api,path:'/api/v1/guest/login',method:'POST',body:JSON.stringify({nickname:login.trim()||'Гость'})});
  if(!r.ok){setError('Гостевой сеанс доступен в сети клуба.');return}
  setGuest(decode<{token:string}>(r).token);setError('');
 }
 async function query(){
  if(!text.trim()||busy||!service)return;
  setBusy(true);setError('');setProposals([]);
  try{
   const response=await service.request({api,path:guest?'/api/v1/guest/assistant':'/api/v1/assistant/workbench/chat',
    token,guest,method:'POST',body:JSON.stringify({text:text.trim()})});
   const data=decode<{answer?:string;text?:string;error?:string;proposals?:Proposal[]}>(response);
   if(!response.ok)throw Error(data.error||'Ассистент недоступен, HTTP '+response.status);
   const reply=data.answer??data.text??'';
   setAnswer(reply);setProposals(data.proposals??[]);setMotion('talk');voice.say(reply,persona.voice!==false);
  }catch(error){setError(String(error))}
  finally{setBusy(false)}
 }
 async function confirm(proposal:Proposal){
  try{
   const response=await service!.request({api,token,path:'/api/v1/assistant/workbench/proposals/'+proposal.id+'/confirm',method:'POST'});
   const data=decode<{url?:string;output?:string;structuredOutput?:unknown;error?:string}>(response);
   if(!response.ok)throw Error(data.error||'Не удалось подтвердить действие.');
   if(data.url)await service!.openLink(data.url);
   else setAnswer(data.output||JSON.stringify(data.structuredOutput??data,null,2));
   setProposals(previous=>previous.filter(item=>item.id!==proposal.id));
  }catch(error){setError(String(error))}
 }
 function patrol(){
  if(persona.moveDesktop===false)return;
  setAnchor('');setWalking(!walking);setMotion(walking?'idle':'walk');void service?.walk(!walking);
 }
 function perch(){
  setWalking(false);setAnchor('');setMotion('perch');void service?.taskbar();
 }
 async function listen(source:'mic'|'shared'){
  try{await music.start(source);setMotion(persona.music==='listen'?'listen':'dance')}
  catch(error){setError(String(error))}
 }
 function stop(){
  void service?.walk(false);setWalking(false);setMotion('idle');music.stop();voice.stop();
 }
 async function logout(){
  if(guest)await service?.request({api,guest,path:'/api/v1/guest/logout',method:'POST'}).catch(()=>{});
  setToken('');setGuest('');setProposals([]);setAnswer('');stop();
 }
 return <div className="overlay-root">
  <div className="floating-controls">
   <button title="Настройки" onClick={()=>setSettings(value=>!value)}>⚙</button>
   <button title="На панель задач" onClick={perch}>▁</button>
   <button title="Пройтись по рабочему столу" onClick={patrol}>{walking?'◼':'↔'}</button>
   <button title="Двигаться в такт" onClick={()=>void listen('mic')}>♫</button>
   <button title="Стоп" onClick={stop}>■</button>
   <button title="Закрыть" onClick={()=>void service?.close()}>×</button>
  </div>
  <AvatarStage url={model.url} mime={model.mime} motion={motion==='talk'&&!voice.speaking?appearance?.idleMotion??'idle':motion} mood={inferVisualMood(answer)} speech={voice.speech} viseme={voice.viseme}
   beat={music.beat} gaze={face.motion} animations={animations} appearance={appearance} intensity={motionStyle==='lively'?1.1:motionStyle==='calm'?.5:.75}/>
  {settings&&<section className="settings">
   <details className="overlay-appearance"><summary>Оформление</summary><label>Тема<select value={look.theme} onChange={e=>setLook(v=>({...v,theme:e.target.value as OverlayLook['theme']}))}><option value="dark">Тёмная</option><option value="light">Светлая</option><option value="system">Системная</option></select></label><label>Стиль<select value={look.interfaceStyle} onChange={e=>setLook(v=>({...v,interfaceStyle:e.target.value as OverlayLook['interfaceStyle']}))}><option value="simplified">Упрощённый</option><option value="immersive">Насыщенный</option></select></label><label>Анимации<select value={look.uiMotion} onChange={e=>setLook(v=>({...v,uiMotion:e.target.value as OverlayLook['uiMotion']}))}><option value="normal">Обычные</option><option value="low">Минимальные</option><option value="off">Выкл.</option></select></label><label>Переход<select value={look.transition} onChange={e=>setLook(v=>({...v,transition:e.target.value as OverlayLook['transition']}))}><option value="flow">Мягкий</option><option value="fade">Растворение</option><option value="slide">Скольжение</option><option value="scale">Масштаб</option></select></label></details>
   {!token&&!guest?<><label>Логин<input value={login} onChange={event=>setLogin(event.target.value)} autoComplete="username"/></label>
   <label>Пароль<input type="password" value={password} onChange={event=>setPassword(event.target.value)} autoComplete="current-password"/></label>
   <div className="line"><button onClick={()=>void signIn()}>Войти</button>{scope==='agent'&&<button onClick={()=>void connectGuest()}>Гость клуба</button>}</div></>:
   <><div className="line"><input value={text} onChange={event=>setText(event.target.value)} placeholder="Спросить ассистента"
    onKeyDown={event=>{if(event.key==='Enter')void query()}}/><button onClick={()=>startVoiceInput(setText,setError)}>🎙</button>
    <button disabled={busy} onClick={()=>void query()}>➤</button></div>
    {answer&&<p className="voice-answer">{answer}</p>}
    {proposals.map(item=><div className="line" key={item.id}><small>{item.argument}</small><button onClick={()=>void confirm(item)}>Подтвердить</button></div>)}
    <button onClick={()=>void logout()}>Выйти</button>
   </>}
   <div className="line"><button onClick={async()=>setWindows(await service?.windows()??[])}>Обновить окна</button>
    <select value={anchor} onChange={event=>{const next=event.target.value;setWalking(false);setAnchor(next);setMotion(next?'lean':'perch');
     if(next)void service?.anchor(next);else void service?.taskbar()}}><option value="">Панель задач</option>
     {windows.filter(item=>item.title).map((item,index)=><option key={item.title+index}>{item.title}</option>)}</select></div>
   <div className="line"><button onClick={()=>void listen('shared')}>Звуковая вкладка</button><button onClick={()=>face.active?face.stop():void face.start()}>{face.active?'Камера выкл.':'Включить камеру'}</button></div>
   {error&&<small className="error">{error}</small>}
  </section>}
 </div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><Overlay/></React.StrictMode>);
