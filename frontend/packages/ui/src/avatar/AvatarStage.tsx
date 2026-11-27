import {useEffect,useRef,useState} from 'react';
import * as THREE from 'three';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {OBJLoader} from 'three/addons/loaders/OBJLoader.js';
import {VRMLoaderPlugin, VRMUtils, type VRM} from '@pixiv/three-vrm';
import type {FaceMotion} from './faceTracking';
import {avatarClip,type ClipAsset} from './AvatarClips';
import type {AvatarAppearance} from './PersonalAvatar';

export type AvatarMood='neutral'|'happy'|'sad'|'angry'|'surprised';
export type AvatarMotion='idle'|'wave'|'nod'|'dance'|'talk'|'sit'|'walk'|'lean'|'perch'|'bump'|'listen';
export type Viseme='aa'|'ih'|'ou'|'ee'|'oh';
export function AvatarStage({url,mime,motion='idle',mood='neutral',speech=0,viseme='aa',beat=0,gaze,animations,intensity=1,appearance,play=0,initialPhase=0,onPlayback,onReady}: 
  {url?:string;mime?:string;motion?:AvatarMotion;mood?:AvatarMood;speech?:number;viseme?:Viseme;beat?:number;gaze?:FaceMotion;
    animations?:Partial<Record<AvatarMotion,ClipAsset>>;intensity?:number;appearance?:AvatarAppearance;play?:number;initialPhase?:number;onPlayback?:(motion:AvatarMotion,seconds:number)=>void;onReady?:()=>void}){
 const mount=useRef<HTMLDivElement>(null);
 const [notice,setNotice]=useState('');
 const [phase,setPhase]=useState('waiting');
 const [imported,setImported]=useState(false);
 const state=useRef({motion,mood,speech,viseme,beat,gaze,intensity,appearance,play,initialPhase,onPlayback,onReady});
 useEffect(()=>{state.current={motion,mood,speech,viseme,beat,gaze,intensity,appearance,play,initialPhase,onPlayback,onReady}},[motion,mood,speech,viseme,beat,gaze,intensity,appearance,play,initialPhase,onPlayback,onReady]);
 useEffect(()=>{
  const host=mount.current;if(!host)return;
  setNotice('');
  setPhase(url?'loading':'waiting');
  setImported(false);
  if(!url)return;
  const scene=new THREE.Scene();scene.background=null;
  const camera=new THREE.PerspectiveCamera(35,1,.01,100);
  let renderer:THREE.WebGLRenderer;
  try{renderer=new THREE.WebGLRenderer({alpha:true,antialias:true,powerPreference:'low-power'})}
  catch{setPhase('error');setNotice('В этом окне недоступен WebGL.');return}
  renderer.setPixelRatio(Math.min(2,window.devicePixelRatio||1));renderer.outputColorSpace=THREE.SRGBColorSpace;
  host.append(renderer.domElement);
  const light=new THREE.HemisphereLight(0xffffff,0x6677ab,2.2);scene.add(light);
  const directional=new THREE.DirectionalLight(0xffffff,2);directional.position.set(2,4,4);scene.add(directional);
  let subject:THREE.Object3D|undefined,vrm:VRM|undefined,dead=false;
  const pivot=new THREE.Group();scene.add(pivot);
  let mixer:THREE.AnimationMixer|undefined,currentClip:THREE.AnimationAction|undefined;
  const clips=new Map<AvatarMotion,THREE.AnimationAction>();
  const trackedBones=new WeakMap<THREE.AnimationAction,Set<string>>();
  const reduced=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const bones=new Map<string,{node:THREE.Object3D,rest:THREE.Quaternion}>();
  const movement=new THREE.Quaternion();const pose=new THREE.Euler();
  let leftArm=-1.04,rightArm=1.04;
  const mouth={aa:0,ih:0,ou:0,ee:0,oh:0};
  function loadBones(model:VRM){
   for(const name of ['leftUpperArm','rightUpperArm','leftUpperLeg','rightUpperLeg','leftLowerLeg','rightLowerLeg','head','spine'] as const){
    const node=model.humanoid.getNormalizedBoneNode(name);if(node)bones.set(name,{node,rest:node.quaternion.clone()});
   }
   const elbow=model.humanoid.getNormalizedBoneNode('leftLowerArm');
   if(elbow&&Math.abs(elbow.position.x)>.001){leftArm=-Math.sign(elbow.position.x)*1.04;rightArm=-leftArm}
  }
  function rotateBone(name:string,x:number,y:number,z:number){
   const bone=bones.get(name);
   if(!bone||currentClip&&trackedBones.get(currentClip)?.has(bone.node.name))return;
   pose.set(x,y,z);movement.setFromEuler(pose);bone.node.quaternion.copy(bone.rest).multiply(movement);
  }
  const resize=()=>{if(!host.isConnected)return;const w=Math.max(1,host.clientWidth),h=Math.max(1,host.clientHeight);camera.aspect=w/h;camera.updateProjectionMatrix();renderer.setSize(w,h,false)};
  const ro=new ResizeObserver(resize);ro.observe(host);resize();
  const failed=()=>{if(!dead){setPhase('error');setNotice('Не удалось открыть 3D-модель. Проверьте формат и содержимое файла.')}};
  const loaded=(object:THREE.Object3D)=>{
   if(dead){VRMUtils.deepDispose(object);return}
   subject=object;pivot.add(object);fit(object);setPhase('ready');
  };
  if((mime||'').includes('obj')||url.toLowerCase().split('?')[0].endsWith('.obj'))new OBJLoader().load(url,loaded,undefined,failed);
   else {
    const loader=new GLTFLoader();loader.register(parser=>new VRMLoaderPlugin(parser));
    loader.load(url,gltf=>{
     if(dead){VRMUtils.deepDispose(gltf.scene);return}
     vrm=gltf.userData.vrm as VRM|undefined;
     if(mime==='.vrm'&&!vrm){VRMUtils.deepDispose(gltf.scene);failed();return}
     if(vrm){
      VRMUtils.rotateVRM0(vrm);
      loadBones(vrm);
      mixer=new THREE.AnimationMixer(vrm.scene);
      const model=vrm;
      for(const [name,clip] of Object.entries(animations??{})){
       if(!clip)continue;
       void avatarClip(clip,model).then(animation=>{
        if(dead||!mixer)return;
        if(animation){
         const action=mixer.clipAction(animation);clips.set(name as AvatarMotion,action);
         trackedBones.set(action,new Set(animation.tracks.map(track=>THREE.PropertyBinding.parseTrackName(track.name).nodeName)));
        }
        else setNotice('Для одного из клипов используется встроенное движение. Проверьте кости и формат анимации.');
       }).catch(()=>{if(!dead)setNotice('Для одного из клипов используется встроенное движение. Проверьте кости и формат анимации.')});
      }
     }else if(Object.keys(animations??{}).length){
      setNotice('Назначенные движения требуют модели VRM с гуманоидным скелетом.');
     }
     loaded(vrm?.scene??gltf.scene);
    },undefined,failed);
   }
  function fit(root:THREE.Object3D){
   const bounds=new THREE.Box3().setFromObject(root);
   const size=bounds.getSize(new THREE.Vector3());const center=bounds.getCenter(new THREE.Vector3());
   if(size.lengthSq()>0){const scale=1.6/Math.max(size.y,size.x*.68,.01);root.scale.multiplyScalar(scale);root.position.set(-center.x*scale,-bounds.min.y*scale,-center.z*scale)}
  }
  let frame=0,t=0,previous=performance.now(),lastPlay=-1,lastMotion:AvatarMotion|undefined;
  let readyReported=false;
  const tick=(now=performance.now())=>{if(dead)return;const s=state.current;
   const dt=Math.min((now-previous)/1000,.05);previous=now;
   if(s.motion!==lastMotion||s.play!==lastPlay)t=lastMotion===undefined?Math.max(0,s.initialPhase):0;
   t+=dt*(s.appearance?.speed??1);
   const center=.84+(s.appearance?.vertical??0);
   camera.position.set(0,center,3.45*Math.max(1,.72/camera.aspect)/(s.appearance?.zoom??1));camera.lookAt(0,center,0);
   light.intensity=2.2*(s.appearance?.light??1);directional.intensity=2*(s.appearance?.light??1);
   const walk=s.motion==='walk',sit=['sit','perch','listen'].includes(s.motion);
   const dance=!reduced&&s.motion==='dance',lean=s.motion==='lean',bump=!reduced&&s.motion==='bump';
   const force=reduced?0:Math.max(.25,Math.min(1.4,s.intensity));
   const desired=reduced?undefined:clips.get(s.motion);
   if(desired!==currentClip||s.play!==lastPlay||s.motion!==lastMotion){
    currentClip?.stop();
    lastPlay=s.play;lastMotion=s.motion;
    currentClip=desired;
    setImported(Boolean(currentClip));
    if(currentClip){
     currentClip.reset();
     if(s.motion==='wave'||s.motion==='bump'){currentClip.setLoop(THREE.LoopOnce,1);currentClip.clampWhenFinished=true}
     else currentClip.setLoop(THREE.LoopRepeat,Infinity);
     currentClip.play();
     const duration=currentClip.getClip().duration;
     if(duration>0)currentClip.time=s.motion==='wave'||s.motion==='bump'?Math.min(t,duration):t%duration;
    }
   }
   if(subject){
    const motionX=walk?Math.sin(t*1.7)*.13:lean?.13:bump?.08+Math.sin(t*10)*.031:0;
    pivot.position.set(motionX*force,(sit?-.29:0)+force*(Math.sin(t*(dance?7:1.8))*(dance?.08:.013)+(dance?s.beat*.07:0)),0);
    pivot.rotation.y=THREE.MathUtils.degToRad(s.appearance?.yaw??0)+force*Math.sin(t*(dance?3.2:1.05))*(dance?.24:.05);
    pivot.rotation.z=force*(lean?-.13:bump?Math.sin(t*10)*.07:s.motion==='wave'?Math.sin(t*5)*.065:0);
   }
   if(vrm){
    if(currentClip)mixer?.update(dt*(s.appearance?.speed??1));
     const stride=walk||dance;
     rotateBone('leftUpperLeg',sit?-.86:stride?Math.sin(t*5)*.37*force:0,0,0);
     rotateBone('rightUpperLeg',sit?-.86:stride?-Math.sin(t*5)*.37*force:0,0,0);
     rotateBone('leftLowerLeg',s.motion==='perch'?.67+Math.sin(t*3)*.24*force:sit?1.12:0,0,0);
     rotateBone('rightLowerLeg',s.motion==='perch'?.69-Math.sin(t*3)*.24*force:sit?1.12:0,0,0);
     rotateBone('rightUpperArm',s.motion==='wave'?-0.2:stride?Math.sin(t*5)*.38*force:0,0,
       rightArm*(s.motion==='wave'?-.85+Math.sin(t*6)*.17*force:lean?.64:dance?.58:1));
     rotateBone('leftUpperArm',stride?-Math.sin(t*5)*.38*force:0,0,leftArm*(dance?.58:1));
     rotateBone('head',(bump?.42+Math.sin(t*12)*.17:s.motion==='nod'?Math.sin(t*3)*.15:s.motion==='talk'?Math.sin(t*2.3)*.045:0)*force+(s.gaze?.facePresent?s.gaze.y*.12:0),
      s.gaze?.facePresent?s.gaze.x*.22:0,s.motion==='listen'?.12:0);
     rotateBone('spine',sit?.16:dance?Math.sin(t*5)*.1*force:0,0,lean?-.11:0);
    const face=vrm.expressionManager;
    if(face){
     for(const key of ['happy','sad','angry','surprised'])face.setValue(key,key===s.mood?0.75:0);
     for(const vowel of ['aa','ih','ou','ee','oh'] as const){
      const target=vowel===s.viseme?Math.max(0,Math.min(1,s.speech)):0;
      const response=1-Math.exp(-dt*(target>mouth[vowel]?23:15));
      mouth[vowel]+=(target-mouth[vowel])*response;
      face.setValue(vowel,mouth[vowel]);
     }
     face.setValue('blink',Math.sin(t*1.7)>0.985?1:0);
    }
    vrm.update(dt);
   }
   renderer.render(scene,camera);
   if(subject){s.onPlayback?.(s.motion,t);if(!readyReported){readyReported=true;s.onReady?.()}}
   frame=requestAnimationFrame(tick);
  };tick();
  return ()=>{dead=true;cancelAnimationFrame(frame);ro.disconnect();mixer?.stopAllAction();VRMUtils.deepDispose(pivot);renderer.dispose();renderer.domElement.remove();};
 },[url,mime,animations]);
 return <div className="mystic-avatar-stage" data-phase={phase} data-motion-source={imported?'import':'built-in'} aria-label="Трёхмерный ассистент">
   <div ref={mount} className="avatar-canvas-host"/>
   {(notice||phase!=='ready')&&<span className="avatar-stage-notice" role="status">{notice||(phase==='loading'?'Загружаем 3D-модель…':'Готовим сцену…')}</span>}
  </div>;
}
