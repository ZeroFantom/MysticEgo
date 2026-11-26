import {useEffect,useRef,useState} from 'react';
import {Mic, Square, X} from 'lucide-react';
import {useApp} from '../app/context';

type Props = {disabled: boolean; send: (clip: Blob) => Promise<void>; onError: (text: string) => void};
const maxSize = 5 * 1024 * 1024;
const formats = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4'];

function useObjectUrl(blob: Blob | null){
  const [value,setValue]=useState('');
  useEffect(()=>{
    if(!blob){setValue('');return}
    const url=URL.createObjectURL(blob);
    setValue(url);
    return ()=>URL.revokeObjectURL(url);
  },[blob]);
  return value;
}

export function VoiceRecorder({disabled,send,onError}:Props){
  const live=useRef(true);
  const recorder=useRef<MediaRecorder|null>(null);
  const stream=useRef<MediaStream|null>(null);
  const timer=useRef<number|undefined>(undefined);
  const [recording,setRecording]=useState(false);
  const [working,setWorking]=useState(false);
  const [clip,setClip]=useState<Blob|null>(null);
  const url=useObjectUrl(clip);

  function release(){
    window.clearTimeout(timer.current);
    stream.current?.getTracks().forEach(track=>track.stop());
    stream.current=null;
    recorder.current=null;
  }

  useEffect(()=>{
    live.current=true;
    return ()=>{
      live.current=false;
      if(recorder.current)recorder.current.onstop=null;
      if(recorder.current?.state==='recording')recorder.current.stop();
      release();
    };
  },[]);

  async function start(){
    if(working||recorder.current)return;
    if(typeof MediaRecorder==='undefined'||!navigator.mediaDevices?.getUserMedia){
      onError('Запись доступна в браузере с доступом к микрофону по HTTPS.');return;
    }
    setWorking(true);
    try{
      const captured=await navigator.mediaDevices.getUserMedia({audio:true});
      if(!live.current){captured.getTracks().forEach(track=>track.stop());return}
      stream.current=captured;
      const mimeType=formats.find(format=>MediaRecorder.isTypeSupported(format));
      const active=new MediaRecorder(captured,mimeType?{mimeType,audioBitsPerSecond:32000}:undefined);
      const chunks:Blob[]=[];
      let bytes=0;
      active.ondataavailable=event=>{
        bytes+=event.data.size;
        if(bytes<=maxSize&&event.data.size)chunks.push(event.data);
        if(bytes>maxSize&&active.state==='recording')active.stop();
      };
      active.onerror=()=>{
        active.onstop=null;release();
        if(live.current){setRecording(false);onError('Не удалось записать звук.')}
      };
      active.onstop=()=>{
        release();
        if(!live.current)return;
        setRecording(false);
        if(!chunks.length||bytes>maxSize){onError('Запись пуста или превышает 5 МиБ.');return}
        setClip(new Blob(chunks,{type:active.mimeType}));
      };
      recorder.current=active;
      active.start(1000);
      timer.current=window.setTimeout(()=>{if(active.state==='recording')active.stop()},90000);
      setRecording(true);
    }catch{
      release();
      if(live.current)onError('Не удалось открыть микрофон. Проверьте разрешение браузера.');
    }finally{if(live.current)setWorking(false)}
  }

  async function submit(){
    if(!clip||working)return;
    setWorking(true);
    try{await send(clip);if(live.current)setClip(null)}
    catch(error){if(live.current)onError(error instanceof Error?error.message:String(error))}
    finally{if(live.current)setWorking(false)}
  }

  return <div className="chat-voice-record">
    {!clip&&(recording?
      <button type="button" className="icon-btn chat-recording" title="Остановить запись" aria-label="Остановить запись"
        onClick={()=>recorder.current?.stop()}><Square size={18}/></button>:
      <button type="button" className="icon-btn" title="Записать голосовое" aria-label="Записать голосовое"
        disabled={disabled||working} onClick={()=>void start()}><Mic size={19}/></button>)}
    {clip&&<div className="voice-draft" aria-label="Черновик голосового сообщения">
      <audio src={url||undefined} controls aria-label="Прослушать запись"/>
      <button type="button" className="btn secondary tiny" disabled={disabled||working} onClick={()=>void submit()}>Отправить голосовое</button>
      <button type="button" className="icon-btn" aria-label="Удалить запись" disabled={working} onClick={()=>setClip(null)}><X size={16}/></button>
    </div>}
  </div>;
}

export function VoiceMessage({id, transcript, onTranscript, onError}:{
  id:string; transcript?:string|null; onTranscript:(text:string)=>void; onError:(text:string)=>void;
}){
  const {session}=useApp();
  const [audio,setAudio]=useState<Blob|null>(null);
  const [loading,setLoading]=useState(false);
  const [transcribing,setTranscribing]=useState(false);
  const [reading,setReading]=useState(false);
  const url=useObjectUrl(audio);
  const endpoint=session.url.replace(/\/$/,'')+'/api/v1/collective/voice/'+id;
  const requests=useRef(new AbortController());
  useEffect(()=>{
    const controller=new AbortController();
    requests.current=controller;
    setLoading(false);setTranscribing(false);
    return ()=>controller.abort();
  },[endpoint,session.token]);

  async function load(){
    setLoading(true);
    const signal=requests.current.signal;
    try{
      const result=await fetch(endpoint,{signal,headers:{Authorization:'Bearer '+session.token}});
      if(!result.ok)throw Error('Не удалось загрузить аудио.');
      const value=await result.blob();
      if(!signal.aborted)setAudio(value);
    }catch(error){if(!signal.aborted)onError(error instanceof Error?error.message:String(error))}
    finally{if(!signal.aborted)setLoading(false)}
  }

  async function read(){
    if(transcript){setReading(value=>!value);return}
    setTranscribing(true);
    const signal=requests.current.signal;
    try{
      const result=await fetch(endpoint+'/transcript',{
        method:'POST',signal:AbortSignal.any([signal,AbortSignal.timeout(115000)]),
        headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/json'},body:'{}',
      });
      const data=await result.json().catch(()=>({}));
      if(!result.ok||typeof data.transcript!=='string')throw Error(data.error||'Не удалось распознать речь.');
      if(!signal.aborted){onTranscript(data.transcript);setReading(true)}
    }catch(error){if(!signal.aborted)onError(error instanceof Error?error.message:String(error))}
    finally{if(!signal.aborted)setTranscribing(false)}
  }

  return <div className="voice-message">
    {audio?<audio src={url||undefined} controls aria-label="Голосовое сообщение"/>:
      <button type="button" className="btn secondary tiny" disabled={loading} onClick={()=>void load()}>{loading?'Загрузка…':'Загрузить аудио'}</button>}
    <button type="button" className="btn secondary tiny" disabled={transcribing} aria-expanded={reading} onClick={()=>void read()}>
      {transcribing?'Распознаём речь…':reading?'Скрыть расшифровку':'Прочитать'}
    </button>
    {reading&&transcript&&<p className="voice-transcript">{transcript}</p>}
  </div>;
}
