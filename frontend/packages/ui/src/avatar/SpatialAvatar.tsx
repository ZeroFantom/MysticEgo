import {useEffect,useRef,useState} from 'react';
import * as THREE from 'three';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {VRMLoaderPlugin,VRMUtils,type VRM} from '@pixiv/three-vrm';
import {accountAvatar,usePersonalAvatar,useAvatarMedia} from './PersonalAvatar';
import './spatial-avatar.css';
import {walkDistance,walkProgress} from './spatialMotion';

type Motion='idle'|'wave'|'nod';
type SessionAnchor={anchorSpace:XRSpace;delete:()=>void};
type AnchoredHit=XRHitTestResult&{createAnchor?:()=>Promise<SessionAnchor>};
type PlacementMode='place'|'walk';
type Runtime={renderer:THREE.WebGLRenderer;scene:THREE.Scene;camera:THREE.PerspectiveCamera;reticle:THREE.Mesh;avatar:THREE.Group;vrm:VRM;restArm?:THREE.Quaternion;restHead?:THREE.Quaternion;dead:boolean;starting:boolean;session?:XRSession;hit?:XRHitTestSource;dispose:()=>void};

export function SpatialAvatar(){
 const library=usePersonalAvatar();
 const selection=library.value?.selection;
 const media=useAvatarMedia(selection?.assetId??'bundled',undefined,library.value?.assets,(library.value?.path??accountAvatar)+'/asset/',undefined,!library.loading);
 const host=useRef<HTMLDivElement>(null),overlay=useRef<HTMLDivElement>(null),runtime=useRef<Runtime|null>(null);
 const motion=useRef<Motion>('idle'),phase=useRef(0);
 const [support,setSupport]=useState(false),[ready,setReady]=useState(false),[running,setRunning]=useState(false),[error,setError]=useState(''),[selected,setSelected]=useState<Motion>('idle');
 const [placed,setPlaced]=useState(false),[tracking,setTracking]=useState<'searching'|'local'|'anchored'|'lost'>('searching');
 const placement=useRef<PlacementMode>('place'),[placementMode,setPlacementMode]=useState<PlacementMode>('place');
 const [notice,setNotice]=useState('');
 function choosePlacement(value:PlacementMode){placement.current=value;setPlacementMode(value);setNotice('')}
 useEffect(()=>{let active=true;void navigator.xr?.isSessionSupported('immersive-ar').then(value=>{if(active)setSupport(value)}).catch(()=>{if(active)setSupport(false)});return()=>{active=false}},[]);
 useEffect(()=>{
  setReady(false);setPlaced(false);setError('');setTracking('searching');
  if(!support||!media.model?.url||!host.current)return;
  const element=host.current;let disposed=false,renderer:THREE.WebGLRenderer;
  try{renderer=new THREE.WebGLRenderer({alpha:true,antialias:true,powerPreference:'low-power'})}catch{setError('WebGL недоступен.');return}
  renderer.xr.enabled=true;renderer.xr.setReferenceSpaceType('local');
  renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));renderer.setSize(320,220);renderer.setClearColor(0x000000,0);
  element.append(renderer.domElement);
  const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(60,320/220,.01,30);
  scene.add(new THREE.HemisphereLight(0xffffff,0x7788bb,2.5));
  const light=new THREE.DirectionalLight(0xffffff,2);light.position.set(1,3,2);scene.add(light);
  const ring=new THREE.RingGeometry(.09,.12,32).rotateX(-Math.PI/2);
  const reticle=new THREE.Mesh(ring,new THREE.MeshBasicMaterial({color:0xb9a2ff,side:THREE.DoubleSide}));
  reticle.matrixAutoUpdate=false;reticle.visible=false;scene.add(reticle);
  const avatar=new THREE.Group();avatar.visible=false;scene.add(avatar);
  let model:VRM|undefined;
  const dispose=()=>{if(disposed)return;disposed=true;const r=runtime.current;if(r?.renderer===renderer){r.dead=true;r.hit?.cancel();if(r.session)void r.session.end().catch(()=>{});runtime.current=null}renderer.setAnimationLoop(null);if(model)VRMUtils.deepDispose(model.scene);ring.dispose();reticle.material.dispose();renderer.dispose();renderer.domElement.remove()};
  const source=media.model.url;
  const manager=new THREE.LoadingManager();
  // Uploaded avatar data may not initiate arbitrary external resource requests.
  manager.setURLModifier(url=>url===source||url.startsWith('blob:')||url.startsWith('data:')?url:'data:application/octet-stream,');
  const loader=new GLTFLoader(manager);loader.register(parser=>new VRMLoaderPlugin(parser));
  void loader.loadAsync(source).then(gltf=>{
   if(disposed){VRMUtils.deepDispose(gltf.scene);return}
   model=gltf.userData.vrm as VRM|undefined;if(!model){VRMUtils.deepDispose(gltf.scene);setError('Для пространственного AR нужна модель VRM.');dispose();return}
   VRMUtils.rotateVRM0(model);const bounds=new THREE.Box3().setFromObject(model.scene);const size=bounds.getSize(new THREE.Vector3()),center=bounds.getCenter(new THREE.Vector3());
   if(!Number.isFinite(size.y)||size.y<=.01){setError('Некорректный размер VRM.');dispose();return}
   const scale=.48/size.y;model.scene.scale.multiplyScalar(scale);model.scene.position.set(-center.x*scale,-bounds.min.y*scale,-center.z*scale);avatar.add(model.scene);
   runtime.current={renderer,scene,camera,reticle,avatar,vrm:model,restArm:model.humanoid.getNormalizedBoneNode('rightUpperArm')?.quaternion.clone(),restHead:model.humanoid.getNormalizedBoneNode('head')?.quaternion.clone(),dead:false,starting:false,dispose};setReady(true);
  }).catch(()=>{if(!disposed){setError('Не удалось загрузить VRM.');dispose()}});
  return dispose;
 },[support,media.model?.url]);
 async function start(){
  const r=runtime.current;if(!r||r.dead||r.starting||r.session||!navigator.xr||!overlay.current)return;
  r.starting=true;setError('');setNotice('');setPlaced(false);setTracking('searching');choosePlacement('place');setRunning(true);
  let session:XRSession|undefined;
  try{
   // Request only from the explicit user click; no camera frame access or upload.
   session=await navigator.xr.requestSession('immersive-ar',{requiredFeatures:['hit-test'],optionalFeatures:['dom-overlay','anchors'],domOverlay:{root:overlay.current}});
   if(r.dead){await session.end();return}r.session=session;
   let anchor:SessionAnchor|undefined,anchorGeneration=0;
   const dropAnchor=()=>{anchorGeneration++;anchor?.delete();anchor=undefined};
   const end=()=>{dropAnchor();r.hit?.cancel();r.hit=undefined;r.session=undefined;r.starting=false;r.reticle.visible=false;r.avatar.visible=false;r.renderer.setAnimationLoop(null);if(!r.dead){setRunning(false);setPlaced(false);setTracking('searching')}};
   session.addEventListener('end',end,{once:true});
   await r.renderer.xr.setSession(session);
   if(r.dead||r.session!==session)return;
   const viewer=await session.requestReferenceSpace('viewer');
   const hit=await session.requestHitTestSource?.({space:viewer});if(!hit)throw Error('hit test');
   if(r.dead||r.session!==session){hit.cancel();return}r.hit=hit;
   const up=new THREE.Vector3(),point=new THREE.Vector3(),rotation=new THREE.Quaternion();
   const scale=new THREE.Vector3(),pose=new THREE.Quaternion(),euler=new THREE.Euler();
   let previous=0,queued=false,hasPlacement=false,lastTracking='searching',walkTime=0,walkDuration=0;
   const origin=new THREE.Vector3(),offset=new THREE.Vector3(),walkFrom=new THREE.Vector3(),walkTo=new THREE.Vector3();
   const restLegs=(['leftUpperLeg','rightUpperLeg','leftLowerLeg','rightLowerLeg'] as const).map(name=>{
    const bone=r.vrm.humanoid.getNormalizedBoneNode(name);return {bone,rest:bone?.quaternion.clone()};
   });
   const showTracking=(value:'searching'|'local'|'anchored'|'lost')=>{if(lastTracking!==value){lastTracking=value;setTracking(value)}};
   const choose=()=>{if(!r.dead&&!queued)queued=true};
   function placeAt(hit:AnchoredHit,position:THREE.Vector3){
    dropAnchor();const generation=anchorGeneration;origin.copy(position);offset.set(0,0,0);walkDuration=0;hasPlacement=true;r.avatar.position.copy(origin);
    const world=r.renderer.xr.getCamera().getWorldPosition(new THREE.Vector3());
    r.avatar.rotation.set(0,Math.atan2(world.x-position.x,world.z-position.z),0);
    r.avatar.visible=true;setPlaced(true);setNotice('');showTracking('local');
    if(typeof hit.createAnchor==='function'){
     try{void hit.createAnchor().then(value=>{
      if(r.dead||r.session!==session||generation!==anchorGeneration){value.delete();return}
      anchor=value;showTracking('anchored');
     }).catch(()=>{if(!r.dead&&r.session===session&&generation===anchorGeneration)setNotice('Якорь недоступен: используется локальная система координат сессии.')})}
     catch{setNotice('Якорь недоступен: используется локальная система координат сессии.')}
    }
   }
   session.addEventListener('select',choose);
   const suppress=(event:Event)=>event.preventDefault();
   const controls=overlay.current;controls?.addEventListener('beforexrselect',suppress);
   session.addEventListener('end',()=>{session?.removeEventListener('select',choose);controls?.removeEventListener('beforexrselect',suppress)},{once:true});
   r.renderer.setAnimationLoop((time,frame)=>{
    if(r.dead||!frame)return;const dt=previous?Math.min(.05,(time-previous)/1000):0;previous=time;phase.current+=dt;
    const space=r.renderer.xr.getReferenceSpace();r.reticle.visible=false;
    if(!space||!frame.getViewerPose(space)){
     queued=false;r.avatar.visible=false;if(hasPlacement)showTracking('lost');return;
    }
    const anchorPose=anchor?frame.getPose(anchor.anchorSpace,space):undefined;
    const tracked=!anchor||!!anchorPose;
    if(anchorPose){const p=anchorPose.transform.position;origin.set(p.x,p.y,p.z)}
    if(hasPlacement){r.avatar.visible=tracked;showTracking(tracked?(anchor?'anchored':'local'):'lost')}
    const hitResult=r.hit?frame.getHitTestResults(r.hit)[0]:undefined;
    const hitPose=hitResult?.getPose(space);
    if(hitPose){r.reticle.matrix.fromArray(hitPose.transform.matrix);r.reticle.matrix.decompose(point,rotation,scale);up.set(0,1,0).applyQuaternion(rotation);r.reticle.visible=up.y>.8}
    if(queued){
     queued=false;
     if(!hitResult||!r.reticle.visible)setNotice('Нет подходящей горизонтальной поверхности.');
     else if(placement.current==='place'||!hasPlacement)placeAt(hitResult,point);
     else if(!tracked)setNotice('Подождите восстановления трекинга.');
     else{
      const from=origin.clone().add(offset),distance=walkDistance(from,point);
      if(distance===null)setNotice('Выберите точку на той же высоте в пределах 1 м.');
      else{
       walkFrom.copy(offset);walkTo.copy(point).sub(origin);walkTo.y=offset.y;
       r.avatar.rotation.y=Math.atan2(point.x-from.x,point.z-from.z);
       walkTime=0;walkDuration=distance/.35;setNotice('');
       if(matchMedia('(prefers-reduced-motion: reduce)').matches){offset.copy(walkTo);walkDuration=0}
      }
     }
    }
    if(tracked&&hasPlacement){
     if(walkDuration>0){walkTime+=dt;const progress=walkProgress(walkTime,walkDuration);offset.lerpVectors(walkFrom,walkTo,progress);if(progress>=1)walkDuration=0}
     r.avatar.position.copy(origin).add(offset);
    }
    const walking=tracked&&walkDuration>0;
    const arm=r.vrm.humanoid.getNormalizedBoneNode('rightUpperArm'),head=r.vrm.humanoid.getNormalizedBoneNode('head');
    const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
    if(arm&&r.restArm){euler.set(0,0,!reduced&&motion.current==='wave'?-1.9+Math.sin(phase.current*6)*.22:-.95);arm.quaternion.copy(r.restArm).multiply(pose.setFromEuler(euler))}
    if(head&&r.restHead){euler.set(!reduced&&motion.current==='nod'?Math.sin(phase.current*3)*.13:0,0,0);head.quaternion.copy(r.restHead).multiply(pose.setFromEuler(euler))}
    for(const [index,leg] of restLegs.entries()){if(leg.bone&&leg.rest){const swing=!reduced&&walking?Math.sin(walkTime*7+(index%2)*Math.PI):0;euler.set(index<2?swing*.28:Math.max(0,-swing)*.32,0,0);leg.bone.quaternion.copy(leg.rest).multiply(pose.setFromEuler(euler))}}
    r.vrm.update(dt);r.renderer.render(r.scene,r.camera);
   });r.starting=false;
  }catch{if(session)await session.end().catch(()=>{});r.starting=false;if(!r.dead){setRunning(false);setError('Не удалось запустить пространственный AR. Проверьте ARCore, разрешения и поддержку hit-test.')}} 
 }
 function chooseMotion(value:Motion){motion.current=value;phase.current=0;setSelected(value)}
 function end(){const r=runtime.current;if(r?.session)void r.session.end().catch(()=>{});}
 useEffect(()=>{const hide=()=>{if(document.hidden)end()};document.addEventListener('visibilitychange',hide);return()=>document.removeEventListener('visibilitychange',hide)},[]);
 return <section className="panel stack"><h2>VRM · Пространственный AR</h2>
  <p>Экспериментальный WebXR-режим. Наведите камеру на горизонтальную поверхность, дождитесь кольца и коснитесь сцены для размещения персонажа. Держите телефон в безопасном положении.</p>
  {!support&&<p role="status">Immersive AR недоступен в этом браузере. Нужны совместимый Android, Chrome и Google Play Services for AR.</p>}
  <div className="spatial-render-host" ref={host}/>
  <button className="btn" disabled={!support||!ready||running} onClick={()=>void start()}>Запустить пространственный AR</button>
  <div ref={overlay} className="spatial-overlay">
   {running&&<><p>{tracking==='lost'?'Трекинг потерян: персонаж скрыт до восстановления.':placed?tracking==='anchored'?'Персонаж закреплён якорем текущей сессии.':'Персонаж размещён в локальной системе координат.':'Наведите камеру на поверхность и коснитесь кольца.'}{notice&&' '+notice}</p>
   {placed&&<div className="actions"><button className="btn secondary" aria-pressed={placementMode==='place'} onClick={()=>choosePlacement('place')}>Переставить</button><button className="btn secondary" aria-pressed={placementMode==='walk'} onClick={()=>choosePlacement('walk')}>Идти к точке (до 1 м)</button></div>}
   <div className="actions">{(['idle','wave','nod'] as const).map(value=><button key={value} aria-pressed={selected===value} onClick={()=>chooseMotion(value)} className="btn secondary">{{idle:'Стоять',wave:'Приветствие',nod:'Кивок'}[value]}</button>)}<button className="btn" onClick={end}>Закрыть AR</button></div></>}
  </div>
  {(error||media.error||library.error)&&<p role="alert">{error||media.error||library.error}</p>}
  <p>При поддержке WebXR Anchors используется якорь текущей сессии; иначе — локальные координаты. Короткая ходьба приблизительная, без карты препятствий. Постоянные якоря, окклюзия и сохранение сцены ещё не реализованы.</p>
 </section>;
}
