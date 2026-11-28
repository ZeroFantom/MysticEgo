import {useEffect,useRef,useState} from 'react';
import {useApp} from './context';
import {useLocale} from './presentation';
import {browserAuthRequest} from './browserSession';
import {telegramAuthorization} from './ExternalSignIn';
import {Panel,ErrorBanner} from '../components/primitives';

type Key={provider:string;realm:string;subject:string};
type Identity=Key&{linkedUtc:string};
type Data={telegramLinkEnabled:boolean;vkLinkEnabled:boolean;identities:Identity[]};
type Audit={provider:string;realm:string;action:string;occurredUtc:string;revokedSessions:number};
type Pending={code:string;url:string;expires:number};
const providerName=(p:string)=>p==='telegram'?'Telegram':p==='vk'?'VK ID':p;
export function ExternalAccountsPanel(){
 const {session,preferenceScope,setSession}=useApp(),t=useLocale();
 const [data,setData]=useState<Data|null>(null),[audit,setAudit]=useState<Audit[]>([]);
 const [pending,setPending]=useState<Pending|null>(null),[candidate,setCandidate]=useState<Key|null>(null),[remove,setRemove]=useState<Identity|null>(null);
 const [password,setPassword]=useState(''),[busy,setBusy]=useState(false),[problem,setProblem]=useState('');
 const epoch=useRef(0),latest=useRef(session);latest.current=session;
 const eligible=location.protocol==='https:'&&session.authUrl.replace(/\/$/,'')===location.origin&&!!session.token&&!!preferenceScope;
 async function request<T>(route:string,body?:unknown,token=session.token):Promise<T>{
  const r=await fetch('/api/auth/external'+route,{method:body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',redirect:'error',
   headers:{Authorization:'Bearer '+token,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  if(!r.ok)throw Error('HTTP '+r.status);
  return r.status===204?undefined as T:await r.json() as T;
 }
 async function refresh(id:number){
  const next=await request<Data>('',undefined,latest.current.token);
  const rows=await request<Audit[]>('/audit',undefined,latest.current.token);
  if(id===epoch.current){setData(next);setAudit(rows)}
 }
 useEffect(()=>{
  const id=++epoch.current;setData(null);setAudit([]);setPending(null);setCandidate(null);setRemove(null);setPassword('');setProblem('');setBusy(false);
  if(eligible)void refresh(id).catch(()=>{});
  return()=>{epoch.current++};
 },[preferenceScope,eligible]);
 async function run(work:(id:number)=>Promise<void>){
  if(busy||!eligible)return;const id=epoch.current;
  setBusy(true);setProblem('');
  try{await work(id)}catch{if(id===epoch.current)setProblem(t('Операция недоступна. Проверьте пароль, состояние привязки и срок действия запроса.'))}
  finally{if(id===epoch.current){setPassword('');setBusy(false)}}
 }
 async function start(id:number){
  const result=await request<{linkCode:string;authorizationUrl:string;expiresInSeconds:number}>('/telegram/start',{password});
  const url=telegramAuthorization(result.authorizationUrl);
  if(!url||!/^[A-Za-z0-9_-]{43}$/.test(result.linkCode)||result.expiresInSeconds!==300)throw Error();
  if(id===epoch.current){setPending({code:result.linkCode,url,expires:Date.now()+300000});setCandidate(null)}
 }
 async function inspect(id:number){
  if(!pending||pending.expires<=Date.now())throw Error();
  const result=await request<Key>('/telegram/candidate',{linkCode:pending.code});
  if(result.provider!=='telegram'||typeof result.realm!=='string'||typeof result.subject!=='string')throw Error();
  if(id===epoch.current)setCandidate(result);
 }
 async function confirm(id:number){
  if(!pending||!candidate||pending.expires<=Date.now())throw Error();
  await request('/telegram/confirm',{linkCode:pending.code,...candidate,password});
  if(id===epoch.current){setPending(null);setCandidate(null);await refresh(id)}
 }
 async function unlink(id:number){
  if(!remove)return;
  await request('/unlink',{provider:remove.provider,realm:remove.realm,subject:remove.subject,password});
  if(id!==epoch.current)return;
  const previous=latest.current;
  if(previous.browserSession)await browserAuthRequest('logout').catch(()=>{});
  if(id===epoch.current)setSession({...previous,token:'',refreshToken:undefined,browserSession:undefined,username:undefined});
 }
 if(!eligible||!data||!data.telegramLinkEnabled&&!data.vkLinkEnabled&&!data.identities.length&&!audit.length)return null;
 return <Panel title={t('Подключённые аккаунты')}>
  <div className="stack">
   <ErrorBanner text={problem}/>
   <p>{t('Внешние аккаунты связаны с вашим существующим аккаунтом MysticEgo. Они не создают нового персонажа.')}</p>
   {data.identities.map(row=><article className="panel" key={JSON.stringify([row.provider,row.realm,row.subject])}><strong>{providerName(row.provider)}</strong><p>{row.subject}</p><small>{new Date(row.linkedUtc).toLocaleString()}</small><div><button className="btn secondary" disabled={busy||!!pending} onClick={()=>{setRemove(row);setPassword('')}}>{t('Отвязать')}</button></div></article>)}
   {!remove&&data.telegramLinkEnabled&&!data.identities.some(x=>x.provider==='telegram')&&<section className="panel stack">
    <strong>Telegram</strong>
    {!pending?<><label>{t('Текущий пароль')}<input type="password" autoComplete="current-password" value={password} onChange={e=>setPassword(e.target.value)}/></label><button className="btn" disabled={busy||!password} onClick={()=>void run(start)}>{t('Начать привязку')}</button></>:<>
     {!candidate?<><a className="btn secondary" href={pending.url} target="_blank" rel="noopener noreferrer">{t('Подтвердить в Telegram')}</a><button className="btn" disabled={busy} onClick={()=>void run(inspect)}>{t('Проверить подтверждение')}</button></>:<>
      <p>{t('Проверенный идентификатор Telegram')}: <strong>{candidate.subject}</strong></p>
      <p>{t('Подтверждайте только свой аккаунт. Если не можете проверить идентификатор, отмените привязку.')}</p>
      <label>{t('Текущий пароль')}<input type="password" autoComplete="current-password" value={password} onChange={e=>setPassword(e.target.value)}/></label>
      <button className="btn" disabled={busy||!password} onClick={()=>void run(confirm)}>{t('Подтвердить привязку')}</button></>}
     <button className="btn secondary" disabled={busy} onClick={()=>{setPending(null);setCandidate(null);setPassword('')}}>{t('Отмена')}</button></>}
   </section>}
   {remove&&<section className="panel stack"><strong>{t('Отвязать')} {providerName(remove.provider)}</strong><p>{t('Все сессии аккаунта будут отозваны. Войдите снова по паролю.')}</p><label>{t('Текущий пароль')}<input type="password" autoComplete="current-password" value={password} onChange={e=>setPassword(e.target.value)}/></label><button className="btn" disabled={busy||!password} onClick={()=>void run(unlink)}>{t('Подтвердить')}</button><button className="btn secondary" disabled={busy} onClick={()=>{setRemove(null);setPassword('')}}>{t('Отмена')}</button></section>}
   {audit.length>0&&<details><summary>{t('История безопасности внешних аккаунтов')}</summary>{audit.map((row,i)=><p key={i}>{new Date(row.occurredUtc).toLocaleString()} · {providerName(row.provider)} · {t(row.action==='linked'?'Привязка':row.action==='unlinked'?'Отвязка':row.action==='login'?'Вход':row.action)}{row.revokedSessions>0?' · '+row.revokedSessions:''}</p>)}</details>}
  </div>
 </Panel>
}
