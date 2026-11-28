import {useEffect,useRef,useState} from 'react';
import {QRCodeSVG} from 'qrcode.react';
import {QrCode} from 'lucide-react';
import {useLocale} from './presentation';
import type {Session} from './context';
import {browserAuthRequest} from './browserSession';
import {ANDROID_ORIGIN} from './androidLaunch';
import './qr-sign-in.css';

export type SessionDevice={kind:string;os:string;version?:string;client:string;model?:string};
type ApprovalView={id:string;expires:string;device:SessionDevice};
type RequestView=ApprovalView&{comparison:string};
const secret=/^[a-f0-9]{64}$/;
const hex=(bytes:Uint8Array)=>Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');
const approvalPath='/audit-portal/account-security';
function requestLink(id:string){return ANDROID_ORIGIN+approvalPath+'#qr='+id}
function parseId(raw:string){
 const value=raw.trim();if(secret.test(value))return value;
 try{const url=new URL(value);const id=url.hash.slice(4);return url.origin===ANDROID_ORIGIN&&url.pathname===approvalPath&&!url.search&&!url.username&&!url.password&&url.hash.startsWith('#qr=')&&secret.test(id)?id:''}catch{return ''}
}
function fromHash(){return parseId(location.origin+approvalPath+location.hash)}
async function post(path:string,body:unknown,token?:string,signal?:AbortSignal){
 return fetch('/api/auth/qr/'+path,{method:'POST',credentials:'omit',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:JSON.stringify(body),signal:signal?AbortSignal.any([signal,AbortSignal.timeout(10000)]):AbortSignal.timeout(10000)});
}
export function deviceLabel(device:SessionDevice|undefined,ua:string|undefined,t:(source:string)=>string){
 if(!device&&ua){const android=/Android/i.test(ua),ios=/iPhone|iPad|iPod/i.test(ua);if(android||ios)device={kind:/iPad/i.test(ua)||android&&!/Mobile/i.test(ua)?'tablet':'mobile',os:android?'Android':'iOS',client:/MysticEgoAndroid\//.test(ua)?'MysticEgo Mobile':/Firefox|FxiOS/i.test(ua)?'Firefox':/Chrome|CriOS/i.test(ua)?'Chrome':'unknown'};}
 if(!device||device.kind==='unknown')return t('Неизвестное устройство');
 const type=t(device.kind==='mobile'?'Мобильное устройство':device.kind==='tablet'?'Планшет':'Компьютер');
 return [type,device.os!=='unknown'?device.os:'',device.version||'',device.model||'',device.client!=='unknown'?device.client:''].filter(Boolean).join(' · ');
}
export function QrReceive({session,authUrl,disabled,onLogin,onBusy}:{session:Session;authUrl:string;disabled:boolean;onLogin:(session:Session)=>void;onBusy:(busy:boolean)=>void}){
 const t=useLocale(),[view,setView]=useState<RequestView|null>(null),[message,setMessage]=useState(''),[busy,setBusy]=useState(false),[exchanging,setExchanging]=useState(false);
 const active=useRef<{controller:AbortController;id?:string;verifier?:string}|null>(null);
 const eligible=location.origin===ANDROID_ORIGIN&&session.url.replace(/\/$/,'')===location.origin&&authUrl.replace(/\/$/,'')===location.origin;
 function stop(cancel=true){
  const current=active.current;active.current=null;current?.controller.abort();
  if(cancel&&current?.id&&current.verifier)void post('cancel',{id:current.id,verifier:current.verifier}).catch(()=>{});
 }
 useEffect(()=>()=>{stop();onBusy(false)},[]);
 useEffect(()=>{if(!eligible){stop();setView(null);setBusy(false);onBusy(false)}},[eligible]);
 async function start(){
  if(!eligible||active.current||disabled)return;
  const controller=new AbortController(),attempt:{controller:AbortController;id?:string;verifier?:string}={controller};active.current=attempt;setBusy(true);onBusy(true);setView(null);setMessage('');
  try{
   const status=await fetch('/api/auth/browser/status',{credentials:'same-origin',cache:'no-store',redirect:'error',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(8000)])});
   if(!status.ok||(await status.json()).enabled!==true)throw Error('unavailable');
   const verifier=hex(crypto.getRandomValues(new Uint8Array(32)));attempt.verifier=verifier;
   const challenge=hex(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(verifier))));
   const response=await post('start',{challenge},undefined,controller.signal);
   if(!response.ok)throw Error('unavailable');
   const data:RequestView=await response.json();if(!secret.test(data.id)||!/^\d{6}$/.test(data.comparison)||!Number.isFinite(Date.parse(data.expires)))throw Error('unavailable');
   attempt.id=data.id;if(controller.signal.aborted){void post('cancel',{id:data.id,verifier}).catch(()=>{});return}
   setView(data);setMessage('Ожидаем подтверждение…');
   while(!controller.signal.aborted&&Date.now()<Date.parse(data.expires)){
    await new Promise<void>(resolve=>setTimeout(resolve,3500));if(controller.signal.aborted)break;
    setExchanging(true);
    let redeem:Response;
    try{redeem=await browserAuthRequest('qr/redeem',{id:data.id,verifier})}finally{if(active.current===attempt)setExchanging(false)}
    if(redeem.status===202)continue;
    if(!redeem.ok)throw Error('expired');
    const result=await redeem.json();
    if(controller.signal.aborted||active.current!==attempt)return;
    if(typeof result.accessToken!=='string'||!result.accessToken||typeof result.user?.login!=='string'||!result.user.login)throw Error('unavailable');
    stop(false);setView(null);onBusy(false);onLogin({...session,authUrl:location.origin,token:result.accessToken,refreshToken:undefined,browserSession:true,username:result.user.login});return;
   }
   if(!controller.signal.aborted)throw Error('expired');
  }catch(error){if(!controller.signal.aborted)setMessage(error instanceof Error&&error.message==='expired'?'Запрос истёк или отклонён. Создайте новый QR-код.':'QR-вход недоступен. Используйте обычный вход.')}
  finally{if(active.current===attempt){stop();setBusy(false);onBusy(false);setView(null)}}
 }
 if(!eligible)return null;
 return <section className="qr-sign-in" aria-label={t('Вход по QR-коду')}>
  <button type="button" className="btn secondary" disabled={disabled||busy} onClick={()=>void start()}><QrCode size={19}/>{t('Вход по QR-коду')}</button>
  {view&&<div className="qr-receive"><div className="qr-paper"><QRCodeSVG value={requestLink(view.id)} size={192} level="M" marginSize={4}/></div>
    <p>{t('Откройте безопасность аккаунта на другом авторизованном устройстве и отсканируйте код или вставьте ссылку.')}</p>
    <strong>{t('Код сравнения')}: <span className="qr-comparison">{view.comparison}</span></strong>
    <label>{t('Скопируйте ссылку запроса')}<input readOnly value={requestLink(view.id)} onFocus={event=>event.target.select()}/></label>
    <button className="btn secondary" type="button" disabled={exchanging} onClick={()=>{stop();setView(null);setBusy(false);onBusy(false);setMessage('')}}>{t('Отмена')}</button>
  </div>}
  {message&&<p role="status">{t(message)}</p>}
 </section>;
}

type Detector={detect(source:HTMLVideoElement):Promise<{rawValue:string}[]>};
type DetectorCtor=new(options:{formats:string[]})=>Detector;
export function QrApprove({session}:{session:Session}){
 const t=useLocale(),[raw,setRaw]=useState(fromHash),[view,setView]=useState<ApprovalView|null>(null),[comparison,setComparison]=useState(''),[message,setMessage]=useState(''),[busy,setBusy]=useState(false),[scanning,setScanning]=useState(false);
 const video=useRef<HTMLVideoElement>(null),stream=useRef<MediaStream|null>(null),scanGeneration=useRef(0);
 const eligible=location.origin===ANDROID_ORIGIN&&session.authUrl.replace(/\/$/,'')===location.origin;
 function stopScan(){scanGeneration.current++;stream.current?.getTracks().forEach(track=>track.stop());stream.current=null;setScanning(false);}
 useEffect(()=>{const sync=()=>{const id=fromHash();if(id){setRaw(id);setView(null);setComparison('');}};window.addEventListener('hashchange',sync);return()=>{window.removeEventListener('hashchange',sync);scanGeneration.current++;stream.current?.getTracks().forEach(track=>track.stop());}},[]);
 useEffect(()=>{setView(null);setComparison('');setMessage('')},[session.username]);
 async function inspect(){
  const id=parseId(raw);if(!id){setMessage('Запрос истёк или отклонён. Создайте новый QR-код.');return}
  setBusy(true);setMessage('');setView(null);setComparison('');
  try{const r=await post('inspect',{id},session.token);if(!r.ok)throw Error();setView(await r.json())}catch{setMessage('Запрос истёк или отклонён. Создайте новый QR-код.')}finally{setBusy(false)}
 }
 async function approve(){
  if(!view||!/^\d{6}$/.test(comparison)||busy)return;
  setBusy(true);setMessage('');
  try{const r=await post('approve',{id:view.id,comparison},session.token);if(!r.ok)throw Error();setView(null);setRaw('');setComparison('');setMessage('Вход подтверждён.');if(location.hash.startsWith('#qr='))history.replaceState(history.state,'',location.pathname+location.search)}catch{setView(null);setMessage('Запрос истёк или отклонён. Создайте новый QR-код.')}finally{setBusy(false)}
 }
 async function scan(){
  const Detector=(window as unknown as {BarcodeDetector?:DetectorCtor}).BarcodeDetector;
  if(!Detector||!navigator.mediaDevices?.getUserMedia){setMessage('Сканер недоступен. Вставьте ссылку вручную.');return}
  const generation=++scanGeneration.current;setScanning(true);setMessage('');
  try{
   const camera=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'}},audio:false});
   if(generation!==scanGeneration.current){camera.getTracks().forEach(track=>track.stop());return}
   stream.current=camera;
   const detector=new Detector({formats:['qr_code']});
   const end=Date.now()+60000;
   while(generation===scanGeneration.current&&Date.now()<end){
    const el=video.current;if(el&&el.srcObject!==camera){el.srcObject=camera;await el.play()}
    if(el&&el.readyState>=2){const result=await detector.detect(el);const id=result.map(x=>parseId(x.rawValue)).find(Boolean);if(id&&generation===scanGeneration.current){setRaw(id);setView(null);setComparison('');break}}
    await new Promise<void>(resolve=>setTimeout(resolve,300));
   }
  }catch{if(generation===scanGeneration.current)setMessage('Сканер недоступен. Вставьте ссылку вручную.')}
  finally{if(generation===scanGeneration.current)stopScan()}
 }
 if(!eligible)return null;
 return <section className="panel qr-approve"><h2>{t('Вход по QR-коду')}</h2>
  <p>{t('Подтверждайте только свой запрос. Сравните код с экраном входа.')}</p>
  <label>{t('Вставьте ссылку или идентификатор запроса')}<input value={raw} onChange={e=>{setRaw(e.target.value);setView(null);setComparison('')}} autoComplete="off" maxLength={300}/></label>
  <div className="actions"><button className="btn secondary" disabled={busy||!raw} onClick={()=>void inspect()}>{t('Проверить запрос')}</button>
  <button className="btn secondary" disabled={busy||scanning} onClick={()=>void scan()}><QrCode size={17}/>{t('Сканировать QR')}</button>
  {scanning&&<button className="btn secondary" onClick={stopScan}>{t('Отмена')}</button>}</div>
  {scanning&&<video ref={video} muted playsInline autoPlay className="qr-camera"/>}
  {view&&<div className="qr-confirm"><p>{deviceLabel(view.device,undefined,t)}</p><p>{t('Тип устройства определяется по данным клиента.')}</p>
   <label>{t('Введите код сравнения')}<input value={comparison} onChange={e=>setComparison(e.target.value.replace(/\D/g,'').slice(0,6))} inputMode="numeric" maxLength={6} autoComplete="off"/></label>
   <button className="btn" disabled={busy||!/^\d{6}$/.test(comparison)} onClick={()=>void approve()}>{t('Разрешить вход')}</button>
   <button className="btn secondary" disabled={busy} onClick={()=>{setView(null);setComparison('')}}>{t('Отмена')}</button>
  </div>}
  {message&&<p role="status">{t(message)}</p>}
 </section>;
}
