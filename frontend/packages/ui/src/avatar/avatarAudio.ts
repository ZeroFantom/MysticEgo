import {useCallback,useEffect,useRef,useState} from 'react';
import type {AvatarMood,Viseme} from './AvatarStage';
// Mood is an explicit visual hint; heuristic fallback is not a psychological diagnosis.
export function inferVisualMood(text:string):AvatarMood {
 const lower=text.toLocaleLowerCase('ru');
 if(/ура|рад|отличн|поздрав|прекрасн|happy|great/.test(lower))return 'happy';
 if(/жаль|груст|сожале|печаль|sorry|sad/.test(lower))return 'sad';
 if(/сердит|злит|раздраж|angry/.test(lower))return 'angry';
 if(/невероят|удив|сюрприз|wow/.test(lower))return 'surprised';
 return 'neutral';
}
export function useAvatarVoice(){
 const [speaking,setSpeaking]=useState(false),[speech,setSpeech]=useState(0),[viseme,setViseme]=useState<Viseme>('aa');
 const timer=useRef<number|undefined>(undefined),generation=useRef(0);
 const clear=useCallback(()=>{window.clearInterval(timer.current);setSpeaking(false);setSpeech(0)},[]);
 const stop=useCallback(()=>{generation.current++;if('speechSynthesis'in window)window.speechSynthesis.cancel();clear()},[clear]);
 useEffect(()=>()=>stop(),[stop]);
 const say=useCallback((input:string,audible=true)=>{
  stop();const text=input.slice(0,3000);if(!text)return false;
  const request=generation.current;
  let position=0,wordPosition=0,tick=0;
  const animate=()=>{
   if(request!==generation.current)return;
   const char=(text[position]??' ').toLowerCase();tick++;
   setViseme(/[оёo]/.test(char)?'oh':/[уюuw]/.test(char)?'ou':/[иеée]/.test(char)?'ee':/[ыийi]/.test(char)?'ih':'aa');
   setSpeech(/[ .,!?:;]/.test(char)?0.035:/[аеёиоуыэюяaeiou]/.test(char)?.69+Math.sin(tick*.98)*.16:.21);
   if(++position>=text.length&&!audible)clear();
  };
  const start=()=>{if(request!==generation.current)return;window.clearInterval(timer.current);setSpeaking(true);timer.current=window.setInterval(animate,68)};
  if(!audible||!('speechSynthesis'in window)){audible=false;start();return true}
  const utterance=new SpeechSynthesisUtterance(text);utterance.lang='ru-RU';utterance.rate=1;
  utterance.onstart=start;
  utterance.onboundary=event=>{if(event.charIndex>=wordPosition){wordPosition=event.charIndex;position=event.charIndex}};
  utterance.onend=()=>{if(request===generation.current)clear()};
  utterance.onerror=()=>{if(request===generation.current){audible=false;start()}};
  window.speechSynthesis.speak(utterance);return true;
 },[stop,clear]);
 return {say,stop,speaking,speech,viseme};
}
export function useMusicBeat(){
 const [beat,setBeat]=useState(0);const ref=useRef<()=>void>(()=>{});
 const stop=useCallback(()=>{ref.current();ref.current=()=>{};setBeat(0)},[]);
 useEffect(()=>()=>stop(),[stop]);
 const start=useCallback(async(sourceType:'mic'|'shared'='mic')=>{
  stop();if(!navigator.mediaDevices?.getUserMedia)throw new Error('Аудиовход недоступен');
  const stream=sourceType==='shared'?await navigator.mediaDevices.getDisplayMedia({audio:true,video:true}):await navigator.mediaDevices.getUserMedia({audio:true});
  if(!stream.getAudioTracks().length){stream.getTracks().forEach(track=>track.stop());throw Error('В источнике нет общего звука. Выберите вкладку и включите передачу аудио.')}

  const context=new AudioContext();const source=context.createMediaStreamSource(stream);
  const analyser=context.createAnalyser();analyser.fftSize=512;source.connect(analyser);
  const data=new Uint8Array(analyser.frequencyBinCount);let frame=0;let active=true;
  const render=()=>{if(!active)return;analyser.getByteFrequencyData(data);const bass=data.slice(1,25).reduce((a,b)=>a+b,0)/24/255;setBeat(Math.max(0,Math.min(1,bass*2.2)));frame=requestAnimationFrame(render)};render();
  ref.current=()=>{active=false;cancelAnimationFrame(frame);stream.getTracks().forEach(t=>t.stop());source.disconnect();void context.close()};
 },[stop]);
 return {beat,start,stop};
}
