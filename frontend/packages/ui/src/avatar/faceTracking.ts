// On-device only: does not upload camera frames, landmarks or emotion labels.
// Requires explicit camera permission and a locally served MediaPipe model/wasm.
import {useCallback,useEffect,useRef,useState} from 'react';
import {FaceLandmarker,FilesetResolver} from '@mediapipe/tasks-vision';
export type FaceMotion={x:number;y:number;mouth:number;facePresent:boolean};
const empty:FaceMotion={x:0,y:0,mouth:0,facePresent:false};
const clamp=(n:number)=>Math.max(-1,Math.min(1,n));
export function useFaceTracking(){
 const [active,setActive]=useState(false),[error,setError]=useState(''),[motion,setMotion]=useState<FaceMotion>(empty);
 const controller=useRef<{stop:()=>void} | undefined>(undefined);
 const stop=useCallback(()=>{controller.current?.stop();controller.current=undefined;setMotion(empty);setActive(false)},[]);
 const start=useCallback(async()=>{
  stop();setError('');let stream:MediaStream|undefined,detector:FaceLandmarker|undefined,video:HTMLVideoElement|undefined,frame=0,disposed=false;
  try{
   if(!window.isSecureContext && location.hostname!=='localhost' && location.hostname!=='127.0.0.1')throw Error('Камера доступна только по HTTPS или localhost.');
   if(!navigator.mediaDevices?.getUserMedia)throw Error('Камера недоступна в этом браузере.');
   const visionBase=location.protocol==='file:'?new URL('../../runtime/vision/',location.href).href:'/vision/';
   const wasm=await FilesetResolver.forVisionTasks(visionBase+'wasm');
   detector=await FaceLandmarker.createFromOptions(wasm,{baseOptions:{modelAssetPath:visionBase+'face_landmarker.task',delegate:'CPU'},
    runningMode:'VIDEO',numFaces:1,outputFaceBlendshapes:false,outputFacialTransformationMatrixes:false});
   stream=await navigator.mediaDevices.getUserMedia({video:{facingMode:'user',width:640,height:480},audio:false});
   video=document.createElement('video');video.srcObject=stream;video.muted=true;video.playsInline=true;await video.play();
   let last=0;const tick=(now:number)=>{
    if(disposed)return;
    if(video!.readyState>=2 && now-last>65){last=now;
     const results=detector!.detectForVideo(video!,now);const landmarks=results.faceLandmarks[0];
     if(landmarks&&landmarks.length>473){
      // Horizontal/vertical iris offset relative to eye corners: approximate gaze direction.
      const left=landmarks[468],right=landmarks[473];const corners=[landmarks[33],landmarks[133],landmarks[362],landmarks[263]];
      const eyeMidX=(corners[0].x+corners[1].x+corners[2].x+corners[3].x)/4;
      const eyeMidY=(landmarks[159].y+landmarks[145].y+landmarks[386].y+landmarks[374].y)/4;
      const irisX=(left.x+right.x)/2,irisY=(left.y+right.y)/2;
      const eyeSpan=Math.max(.01,Math.abs(corners[3].x-corners[0].x));
      const mouth=Math.min(1,Math.max(0,(landmarks[14].y-landmarks[13].y)/Math.max(.01,landmarks[61].x-landmarks[291].x)*2));
      setMotion({x:clamp((irisX-eyeMidX)/eyeSpan*5),y:clamp((irisY-eyeMidY)*12),mouth,facePresent:true});
     }else setMotion(empty);
    }
    frame=requestAnimationFrame(tick);
   };frame=requestAnimationFrame(tick);
   controller.current={stop:()=>{disposed=true;cancelAnimationFrame(frame);stream?.getTracks().forEach(t=>t.stop());detector?.close();if(video){video.pause();video.srcObject=null}}};
   setActive(true);
  }catch(e){disposed=true;cancelAnimationFrame(frame);stream?.getTracks().forEach(t=>t.stop());detector?.close();setError(String(e));setActive(false);}
 },[stop]);
 useEffect(()=>()=>{controller.current?.stop()},[]);
 return {active,motion,error,start,stop};
}
