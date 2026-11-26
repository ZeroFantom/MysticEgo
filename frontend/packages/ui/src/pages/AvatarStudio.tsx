import {useEffect,useMemo,useState,type FormEvent} from 'react';
import {Link} from 'react-router-dom';
import {Bot,Download,Play,Sparkles} from 'lucide-react';
import {Panel,Header,ErrorBanner} from '../components/primitives';
import {useApp,useRemote} from '../app/context';
import {AvatarStage,type AvatarMotion} from '../avatar/AvatarStage';
import {createWaveVrma} from '../avatar/createWaveVrma';
import {createStarterVrm} from '../avatar/createStarterVrm';
import {inferVisualMood,useAvatarVoice,useMusicBeat} from '../avatar/avatarAudio';
import {startVoiceInput} from '../avatar/avatarSpeechInput';
import {useFaceTracking} from '../avatar/faceTracking';
import {accountAvatar,avatarSaved,defaultAppearance,deviceAvatar,intensityByStyle,motions,styles,useAvatarMedia,usePersonalAvatar,
  type AvatarAppearance,type AvatarAsset,type Library,type MotionStyle,type Selection} from '../avatar/PersonalAvatar';

const sliders=[['zoom','Масштаб',.75,1.35,.01],['yaw','Поворот',-180,180,1],['vertical','Высота кадра',-.25,.25,.01],
  ['light','Освещение',.5,1.5,.05],['speed','Скорость анимации',.5,1.75,.05]] as const;
const idleMotions=Object.entries(motions).filter(([id])=>!['wave','talk','bump'].includes(id));
const assetName=(item:AvatarAsset)=>item.name||item.extension.slice(1).toUpperCase()+' · '+item.id.slice(0,8);

function saveFile(file:Blob,name:string){
 const url=URL.createObjectURL(file);
 const anchor=document.createElement('a');anchor.href=url;anchor.download=name;anchor.click();setTimeout(()=>URL.revokeObjectURL(url),5000);
}

function saveJson(value:unknown,name:string){
 saveFile(new Blob([JSON.stringify(value,null,2)],{type:'application/json'}),name);
}

export function AvatarStudio(){
 const {session,mode,portal,api}=useApp();
 const base=session.url.replace(/\/$/,'');
 const organization=mode==='global';
 const personal=usePersonalAvatar(!organization,false);
 const corporate=useRemote<Library|null>(async()=>{
   if(!organization)return null;
   const source=await api.request<{theme?:{modelId:string;animationIds:string[]};assets:AvatarAsset[]}>('/avatars/organization');
   return {assets:source.assets,selection:source.theme?{assetId:source.theme.modelId,motions:Object.fromEntries(
     source.theme.animationIds.slice(0,Object.keys(motions).length).map((id,index)=>[Object.keys(motions)[index],id]))}:undefined};
 },[api,organization]);
 const venue=useRemote(()=>mode==='agent'?api.request<{theme?:{modelId:string};assets?:AvatarAsset[]}>('/avatars/theme').catch(()=>null):Promise.resolve(null),[api,mode]);
 const library=organization?corporate.value:personal.value;
 const path=personal.value?.path??(mode==='audit'?accountAvatar:deviceAvatar);
 const legacy=mode==='audit'&&!portal&&path===deviceAvatar;
 const [added,setAdded]=useState<AvatarAsset[]>([]);
 const assets=useMemo(()=>{
   const entries=new Map((library?.assets??[]).map(item=>[item.id,item]));
   added.forEach(item=>entries.set(item.id,item));return [...entries.values()];
 },[library?.assets,added]);
 const [selection,setSelection]=useState('bundled'),[mapping,setMapping]=useState<NonNullable<Selection['motions']>>({});
 const [appearance,setAppearance]=useState(defaultAppearance),[style,setStyle]=useState<MotionStyle>('calm');
 const [motion,setMotion]=useState<AvatarMotion>('idle'),[message,setMessage]=useState('Привет! Чем могу помочь?'),[play,setPlay]=useState(0);
 const [error,setError]=useState(''),[status,setStatus]=useState(''),[busy,setBusy]=useState(false);
 const voice=useAvatarVoice(),music=useMusicBeat(),face=useFaceTracking();
 const media=useAvatarMedia(selection==='org'?venue.value?.theme?.modelId:selection,mapping,
   selection==='org'?venue.value?.assets:assets,selection==='org'?'/avatars/theme/assets/':organization?'/avatars/assets/':path+'/asset/');

 useEffect(()=>{
  if(!library)return;
  const saved=library.selection;
  setSelection(saved?.assetId??(organization?'':venue.value?.theme?.modelId?'org':'bundled'));
  setMapping(saved?.motions??{});setStyle(saved?.motionStyle??'calm');
  const view={...defaultAppearance,...saved?.appearance};setAppearance(view);setMotion(view.idleMotion);
 },[library,organization,venue.value?.theme?.modelId]);

 async function postAsset(file:File|Blob, filename:string,kind:string,scope=path){
   const form=new FormData();form.append('kind',kind);form.append('file',file,filename);
   const route=organization?'/avatars/organization/assets':scope+'/asset';
   const response=await fetch(base+'/api/v1'+route,{method:'POST',headers:{Authorization:'Bearer '+session.token},body:form});
   const content=await response.json().catch(()=>({}));if(!response.ok)throw Error(content.error||'Не удалось импортировать файл.');
   return content as AvatarAsset;
 }
 async function upload(file:File|undefined|(()=>Promise<File>),kind:'model'|'animation',slot=motion){
   if(!file||busy)return;
   setBusy(true);setError('');setStatus('');
   try{
     const source=typeof file==='function'?await file():file;
     const asset=await postAsset(source,source.name,kind);setAdded(previous=>[...previous,asset]);
     if(kind==='model')setSelection(asset.id);
     else setMapping(previous=>({...previous,[slot]:asset.id}));
     setStatus(kind==='model'?'Модель добавлена в предпросмотр. Сохраните настройки.':`Клип назначен на «${motions[slot]}». Сохраните настройки.`);
   }catch(problem){setError(String(problem))}
   finally{setBusy(false)}
 }
 const assignments=()=>Object.fromEntries(Object.entries(mapping).filter(([,id])=>id));
 async function apply(event:FormEvent){
   event.preventDefault();setBusy(true);setError('');setStatus('');
   try{
     if(organization)await api.request('/avatars/organization/theme',{method:'PUT',body:JSON.stringify({
       modelId:selection,animationIds:[...new Set(Object.values(assignments()))],proceduralAnimations:Object.keys(motions)
     })});
     else await api.request(path+'/motion-pack',{method:'PUT',body:JSON.stringify({assetId:selection,style,motions:assignments(),appearance})});
     setStatus('Модель и движения сохранены.');
     if(!organization)window.dispatchEvent(new Event(avatarSaved));
   }catch(problem){setError(String(problem))}
   finally{setBusy(false)}
 }
 async function migrate(){
   setBusy(true);setError('');setStatus('');
   try{
     const replacements=new Map<string,string>();
     for(const id of new Set([selection,...Object.values(assignments())])){
       if(id==='bundled')continue;
       const asset=assets.find(item=>item.id===id);if(!asset)throw Error('Найдены отсутствующие файлы в настройках.');
       const response=await fetch(base+'/api/v1'+deviceAvatar+'/asset/'+id,{headers:{Authorization:'Bearer '+session.token}});
       if(!response.ok)throw Error('Не удалось прочитать локальный файл.');
       const copy=await postAsset(await response.blob(),asset.name??id+asset.extension,asset.kind,accountAvatar);
       replacements.set(id,copy.id);
     }
     await api.request(accountAvatar+'/motion-pack',{method:'PUT',body:JSON.stringify({assetId:replacements.get(selection)??selection,
       style,appearance,motions:Object.fromEntries(Object.entries(assignments()).map(([slot,id])=>[slot,replacements.get(id)]))})});
     setAdded([]);personal.reload();setStatus('Аватар скопирован в аккаунт Audit.');window.dispatchEvent(new Event(avatarSaved));
   }catch(problem){setError(String(problem))}
   finally{setBusy(false)}
 }
 async function generate(){
   setBusy(true);setError('');
   try{
     const pack=organization?{style,intensity:intensityByStyle[style],clips:Object.keys(motions)}:
       await api.request(path+'/motion-pack/generate',{method:'POST',body:JSON.stringify({style})});
     saveJson(pack,'MysticEgo-'+style+'-motions.json');setStatus('Процедурный пакет сохранён в файл.');
   }catch(problem){setError(String(problem))}
   finally{setBusy(false)}
 }

 return <><Header title={organization?'Тема VRM':'Студия VRM'} description={organization?'Аватар организации':
   legacy?'Настройки аватара на этом компьютере':mode==='audit'?'Личный аватар аккаунта Audit':'Аватар рабочей станции'}
   action={!organization?<Link className="btn secondary" to="/assistant"><Bot size={17}/> Чат с ассистентом</Link>:undefined}/>
   <ErrorBanner text={error||(organization?corporate.error:personal.error)||media.error||face.error||''}/>{status&&<p className="success" role="status">{status}</p>}
   <div className="two-col avatar-studio-layout">
     <Panel title="Сцена">
       <div className="avatar-preview"><AvatarStage url={media.model?.url} mime={media.model?.mime} motion={motion} mood={inferVisualMood(message)}
         speech={voice.speech} viseme={voice.viseme} beat={music.beat} gaze={face.motion} animations={media.clips} intensity={intensityByStyle[style]} appearance={appearance} play={play}/></div>
       <div className="stack">
         <div className="avatar-motion-picker"><label>Движение<select aria-label="Движение" value={motion} onChange={event=>setMotion(event.target.value as AvatarMotion)}>
           {Object.entries(motions).map(([id,label])=><option key={id} value={id}>{label}</option>)}</select></label>
           <button className="btn secondary" onClick={()=>setPlay(value=>value+1)}><Play size={15}/> Повторить</button></div>
         <label>Фраза для проверки губ<textarea rows={2} value={message} maxLength={500} onChange={event=>setMessage(event.target.value)}/></label>
         <div className="row-actions"><button className="btn" onClick={()=>{setMotion('talk');voice.say(message)}}>Озвучить</button>
           <button className="btn secondary" onClick={()=>startVoiceInput(setMessage,setError)}>Распознать голос</button>
           <button className="btn secondary" onClick={()=>face.active?face.stop():void face.start()}>{face.active?'Выключить камеру':'Следить за лицом'}</button>
           <button className="btn secondary" onClick={()=>{voice.stop();music.stop();face.stop();setMotion(appearance.idleMotion)}}>Остановить</button></div>
         <details><summary>Реакция на музыку</summary><div className="row-actions motion-actions">
           <button className="btn secondary" onClick={()=>{setMotion('dance');void music.start('mic').catch(problem=>setError(String(problem)))}}>Танец под микрофон</button>
           <button className="btn secondary" onClick={()=>{setMotion('listen');void music.start('shared').catch(problem=>setError(String(problem)))}}>Слушать звуковую вкладку</button>
         </div></details>
       </div>
     </Panel>
     <Panel title="Модель и движения" aside={<Sparkles size={18}/> }>
       <form className="stack" onSubmit={apply}>
         <label>Загрузить модель VRM / GLB / OBJ<input type="file" accept=".vrm,.glb,.obj" disabled={busy||!library}
           onChange={event=>{const file=event.target.files?.[0];event.target.value='';void upload(file,'model')}}/></label>
         <label>Загрузить анимацию VRMA / GLB / FBX<input type="file" accept=".vrma,.glb,.fbx" disabled={busy||!library}
           onChange={event=>{const file=event.target.files?.[0];event.target.value='';void upload(file,'animation')}}/></label>
         <label>Модель<select aria-label="Модель" value={selection} onChange={event=>setSelection(event.target.value)}>
           {organization?<option value="">Выберите файл</option>:<option value="bundled">VRM из комплекта</option>}
           {venue.value?.theme?.modelId&&<option value="org">Тема организации</option>}
           {assets.filter(asset=>asset.kind==='model').map(asset=><option key={asset.id} value={asset.id}>{assetName(asset)}</option>)}
         </select></label>
         <label>Стиль движений<select value={style} onChange={event=>setStyle(event.target.value as MotionStyle)}>
           {Object.entries(styles).map(([id,label])=><option key={id} value={id}>{label}</option>)}</select></label>
         {!organization&&<details open><summary>Вид и поведение на сайте</summary><div className="avatar-appearance">
           {sliders.map(([key,label,min,max,step])=><label key={key}>{label} <span>{appearance[key]}</span><input aria-label={label} type="range" min={min} max={max} step={step}
             value={appearance[key]} onChange={event=>setAppearance(previous=>({...previous,[key]:Number(event.target.value)}))}/></label>)}
           <label>Движение в ожидании<select aria-label="Движение в ожидании" value={appearance.idleMotion} onChange={event=>{
             const idleMotion=event.target.value as AvatarMotion;setAppearance(value=>({...value,idleMotion}));setMotion(idleMotion);
           }}>{idleMotions.map(([id,label])=><option key={id} value={id}>{label}</option>)}</select></label>
           <label className="check-row"><input type="checkbox" checked={appearance.showOnSite} onChange={event=>setAppearance(value=>({...value,showOnSite:event.target.checked}))}/> Показывать VRM в чате сайта</label>
           <label className="check-row"><input type="checkbox" checked={appearance.animateReplies} onChange={event=>setAppearance(value=>({...value,animateReplies:event.target.checked}))}/> Анимировать ответы ассистента</label>
           <button className="btn secondary tiny" type="button" onClick={()=>{setAppearance(defaultAppearance);setMotion(defaultAppearance.idleMotion)}}>Сбросить вид</button>
         </div></details>}
         <details><summary>Назначения анимаций</summary><div className="motion-slots">{Object.entries(motions).map(([key,label])=><label key={key}>{label}
           <select value={mapping[key as AvatarMotion]??''} onChange={event=>setMapping(previous=>({...previous,[key]:event.target.value}))}>
             <option value="">Встроенное движение</option>{assets.filter(asset=>asset.kind==='animation').map(asset=><option key={asset.id} value={asset.id}>{assetName(asset)}</option>)}
           </select></label>)}</div></details>
         <button className="btn" disabled={busy||!library||!selection||selection==='org'}>{organization?'Опубликовать тему':'Сохранить аватар'}</button>
       </form>
       <details className="avatar-generator">
         <summary>Локальная модель «Скаут»</summary>
         <p className="footnote">Базовый стилизованный робот с гуманоидными суставами. Собирается в браузере из шаблона.</p>
         <div className="row-actions motion-actions">
           <button className="btn secondary" disabled={busy||!library}
             onClick={()=>void upload(createStarterVrm,'model')}><Sparkles size={16}/> Создать VRM</button>
           <button className="btn secondary" disabled={busy} onClick={async()=>{
             setBusy(true);setError('');setStatus('');
             try{const file=await createStarterVrm();saveFile(file,file.name);}
             catch(problem){setError(String(problem))}
             finally{setBusy(false)}
           }}><Download size={16}/> Скачать VRM</button>
         </div>
       </details>
       <details className="avatar-generator">
         <summary>Локальная анимация «Приветствие»</summary>
         <p className="footnote">Создаёт клип VRMA в браузере. Для применения нужен гуманоидный VRM.</p>
         <div className="row-actions motion-actions">
           {!organization&&<button className="btn secondary" disabled={busy||!library||selection==='org'} onClick={()=>{
             setMotion('wave');void upload(createWaveVrma(),'animation','wave');
           }}><Sparkles size={16}/> Добавить в приветствие</button>}
           <button className="btn secondary" onClick={()=>{
             const file=createWaveVrma();saveFile(file,file.name);
           }}><Download size={16}/> Скачать VRMA</button>
         </div>
       </details>
       <div className="row-actions avatar-export">
         {legacy&&<button className="btn secondary" disabled={busy} onClick={()=>void migrate()}>Скопировать в аккаунт Audit</button>}
         <button className="btn secondary" disabled={busy||!library} onClick={()=>void generate()}><Download size={16}/> Скачать описание движений (JSON)</button>
         <button className="btn secondary" onClick={()=>saveJson({version:'mysticego-motion-1',model:selection,style,appearance,motions:assignments()},'MysticEgo-avatar.json')}>Экспорт настроек</button>
       </div>
     </Panel>
   </div>
 </>;
}
