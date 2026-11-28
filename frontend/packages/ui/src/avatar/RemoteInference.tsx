import {useEffect,useRef,useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {ANDROID_ORIGIN} from '../app/androidLaunch';
export function RemoteInference(){
 const {session}=useApp();
 const supported=location.origin===ANDROID_ORIGIN&&session.url.replace(/\/$/,'')===location.origin;
 const providers=useRemote(()=>supported?fetch('/api/v1/assistant/mobile/providers',{headers:{Authorization:'Bearer '+session.token},cache:'no-store',redirect:'error',credentials:'omit'}).then(async r=>r.ok?await r.json() as {cloud:boolean;speech:boolean}:null):Promise.resolve(null),[session.token,supported]);
 const [text,setText]=useState(''),[answer,setAnswer]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false),[recording,setRecording]=useState(false);
 const [cloudConsent,setCloudConsent]=useState(false),[speechConsent,setSpeechConsent]=useState(false);
 const stream=useRef<MediaStream|null>(null),recorder=useRef<MediaRecorder|null>(null),timer=useRef<number|undefined>(undefined),epoch=useRef(0),request=useRef<AbortController|null>(null);
 function cancel(){epoch.current++;request.current?.abort();window.clearTimeout(timer.current);if(recorder.current?.state==='recording'){recorder.current.onstop=null;recorder.current.stop()}stream.current?.getTracks().forEach(t=>t.stop());stream.current=null;recorder.current=null;setRecording(false);setBusy(false)}
 useEffect(()=>{const hide=()=>{if(document.hidden)cancel()};document.addEventListener('visibilitychange',hide);return()=>{document.removeEventListener('visibilitychange',hide);cancel()}},[session.token]);
 async function post(path:string,body:BodyInit,headers:Record<string,string>){
  const controller=new AbortController();request.current=controller;
  const response=await fetch('/api/v1/assistant/mobile/'+path,{method:'POST',body,headers:{Authorization:'Bearer '+session.token,...headers},credentials:'omit',redirect:'error',cache:'no-store',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(65000)])});
  if(!response.ok)throw Error('Провайдер недоступен или превышен лимит.');
  const data=await response.json();if(typeof data.text!=='string')throw Error('Некорректный ответ');return data.text as string;
 }
 async function chat(){if(!cloudConsent||!text.trim()||busy)return;setBusy(true);setError('');const id=epoch.current;try{const result=await post('cloud',JSON.stringify({text,consent:true}),{'Content-Type':'application/json'});if(id===epoch.current)setAnswer(result)}catch(e){if(id===epoch.current)setError(String(e))}finally{if(id===epoch.current)setBusy(false)}}
 async function record(){
  if(!speechConsent||busy||recording)return;const id=++epoch.current;setError('');setBusy(true);try{
   const media=await navigator.mediaDevices.getUserMedia({audio:true,video:false});if(id!==epoch.current){media.getTracks().forEach(t=>t.stop());return}stream.current=media;
   const chunks:Blob[]=[];const r=new MediaRecorder(media);recorder.current=r;r.ondataavailable=e=>{if(e.data.size)chunks.push(e.data)};r.onstop=async()=>{media.getTracks().forEach(t=>t.stop());stream.current=null;setRecording(false);if(id!==epoch.current)return;try{const blob=new Blob(chunks,{type:r.mimeType});if(blob.size>5*1024*1024)throw Error('Запись слишком велика');const result=await post('speech',blob,{'Content-Type':blob.type||'application/octet-stream','X-Mystic-Speech-Consent':'1'});if(id===epoch.current)setText(result.slice(0,4000))}catch(e){if(id===epoch.current)setError(String(e))}finally{if(id===epoch.current)setBusy(false)}};
   r.start();setRecording(true);timer.current=window.setTimeout(()=>{if(r.state==='recording')r.stop()},30000);
  }catch(e){if(id===epoch.current){cancel();setError('Не удалось записать звук: '+String(e))}}
 }
 if(!supported)return null;
 return <section className="panel stack"><h2>Вычисления на сервере / в облаке</h2><p>Обычный чат MysticEgo использует модель серверного узла. Дополнительный внешний провайдер выбирается явно; история диалога, файлы и инструменты устройств не отправляются.</p>
 <p>Облачная модель: {providers.value?.cloud?'настроена':'не настроена'} · Речь: {providers.value?.speech?'настроена':'не настроена'}</p>
 <label><input type="checkbox" checked={cloudConsent} onChange={e=>setCloudConsent(e.target.checked)}/> Разрешаю передачу введённого текста настроенному внешнему провайдеру</label>
 <textarea maxLength={4000} value={text} onChange={e=>setText(e.target.value)} placeholder="Сообщение или распознанный текст"/>
 <button className="btn" disabled={!providers.value?.cloud||!cloudConsent||busy||!text.trim()} onClick={()=>void chat()}>Отправить облачной модели</button>
 <label><input type="checkbox" checked={speechConsent} onChange={e=>setSpeechConsent(e.target.checked)}/> Разрешаю отправку этой голосовой записи сервису распознавания</label>
 {recording?<button className="btn" onClick={()=>{window.clearTimeout(timer.current);recorder.current?.stop()}}>Остановить и распознать</button>:<button className="btn secondary" disabled={!providers.value?.speech||!speechConsent||busy} onClick={()=>void record()}>Записать голос (до 30 секунд)</button>}
 {busy&&<button className="btn secondary" onClick={cancel}>Отмена</button>}{answer&&<p>{answer}</p>}{error&&<p role="alert">{error}</p>}</section>;
}
