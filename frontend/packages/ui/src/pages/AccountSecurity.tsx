import {useEffect,useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {useLocale,usePresentation} from '../app/presentation';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {ExternalAccountsPanel} from '../app/ExternalAccountsPanel';

type SessionRow={id?:string;current:boolean;createdUtc:string;lastSeenUtc:string;expiresUtc:string;userAgent?:string;ip?:string;legacy:boolean};
type Doc={version:string;url:string};
type Grant={deviceId:string;grantId:string;scope:string;policyVersion:string;grantedUtc:string};
type Consents={terms:Doc;privacy:Doc;license:Doc;permissionPolicyVersion:string;scopes:string[];enabledScopes:string[];accepted:boolean;grants:Grant[]};
export function AccountSecurity(){
 const {session,preferenceScope}=useApp(),t=useLocale(),{value}=usePresentation();
 const [problem,setProblem]=useState(''),[busy,setBusy]=useState(false),[confirm,setConfirm]=useState<string|null>(null);
 const [enabled,setEnabled]=useState<string[]>([]),[accepted,setAccepted]=useState(false);
 async function request<T>(route:string,body?:unknown):Promise<T>{
  const res=await fetch(session.authUrl.replace(/\/$/,'')+'/api/auth/'+route,{
   headers:{Authorization:'Bearer '+session.token,...(body!==undefined?{'Content-Type':'application/json'}:{})},method:body!==undefined?'POST':'GET',
   body:body!==undefined?JSON.stringify(body):undefined,signal:AbortSignal.timeout(12000),redirect:'error',cache:'no-store'
  });
  if(!res.ok)throw Error('HTTP '+res.status);
  return res.status===204?undefined as T:await res.json() as T;
 }
 const sessions=useRemote(()=>request<SessionRow[]>('sessions'),[preferenceScope,session.token],30000,true);
 const consent=useRemote(()=>request<Consents>('consents'),[preferenceScope,session.token]);
 useEffect(()=>{setEnabled(consent.value?.enabledScopes??[]);setAccepted(consent.value?.accepted??false)},[consent.value]);
 useEffect(()=>{setConfirm(null);setProblem('')},[preferenceScope]);
 const date=(stamp:string)=>new Intl.DateTimeFormat(value.language,{dateStyle:'medium',timeStyle:'short'}).format(new Date(stamp));
 async function run(action:()=>Promise<unknown>){
  setBusy(true);setProblem('');
  try{await action();sessions.reload();consent.reload();setConfirm(null)}
  catch(e){setProblem(String(e))}finally{setBusy(false)}
 }
 return <>
  <Header title={t('Безопасность аккаунта')} description={t('Сессии, документы и разрешения устройств')}/>
  <ErrorBanner text={problem}/>
  <Panel title={t('Активные сессии')} aside={<button className="btn secondary" disabled={busy} onClick={()=>sessions.reload()}>{t('Обновить')}</button>}>
   <ErrorBanner text={sessions.error}/>
   <p>{t('Сессия считается активной до выхода, отзыва или истечения срока. Последняя активность — время обновления токена.')}</p>
   <div className="stack">
   {sessions.value?.map((row,index)=><article className="panel" key={row.id??'legacy-'+index}>
    <strong>{row.current?t('Текущая сессия'):row.legacy?t('Старая сессия'):t('Другая сессия')}</strong>
    <p>{row.userAgent||t('Неизвестное устройство')}</p>
    <p>{row.ip||'—'} · {t('Последняя активность')}: {date(row.lastSeenUtc)}</p>
    <small>{t('Создана')}: {date(row.createdUtc)} · {t('Истекает')}: {date(row.expiresUtc)}</small>
    {!row.current&&row.id&&<div><button className="btn secondary" disabled={busy} onClick={()=>setConfirm(row.id!)}>{t('Завершить сессию')}</button></div>}
   </article>)}
   <button className="btn secondary" disabled={busy||!sessions.value?.some(x=>!x.current)} onClick={()=>setConfirm('others')}>{t('Завершить все остальные сессии')}</button>
   {confirm&&<div role="alert" className="panel"><p>{t(confirm==='others'?'Все остальные устройства выйдут из аккаунта. Текущая сессия сохранится.':'Подтвердите завершение выбранной сессии.')}</p><div className="actions"><button className="btn" disabled={busy} onClick={()=>void run(()=>request(confirm==='others'?'sessions/revoke-others':'sessions/'+confirm+'/revoke',{}))}>{t('Подтвердить')}</button><button className="btn secondary" disabled={busy} onClick={()=>setConfirm(null)}>{t('Отмена')}</button></div></div>}
   </div>
  </Panel>
  <ExternalAccountsPanel/>
  <Panel title={t('Документы и разрешения аккаунта')}>
   <ErrorBanner text={consent.error}/>
   {consent.value&&<div className="stack">
    <p>{t(consent.value.accepted?'Актуальные документы приняты.':'Необходимо ознакомиться с актуальными документами.')}</p>
    <div className="actions">{([['Условия',consent.value.terms],['Обработка данных',consent.value.privacy],['Лицензия',consent.value.license]] as const).map(([label,doc])=><a key={label} href={doc.url} target="_blank" rel="noreferrer">{t(label)} · {doc.version}</a>)}</div>
    <label><input type="checkbox" checked={accepted} onChange={e=>setAccepted(e.target.checked)}/>{t('Подтверждаю ознакомление с условиями, политикой обработки данных и лицензией.')}</label>
    <p>{t('Выберите возможности. Разрешение конкретного устройства потребуется отдельно.')}</p>
    {consent.value.scopes.map(scope=><label key={scope}><input type="checkbox" checked={enabled.includes(scope)} onChange={e=>setEnabled(old=>e.target.checked?[...old,scope]:old.filter(x=>x!==scope))}/>{t('permission.'+scope)}</label>)}
    <button className="btn" disabled={!accepted||busy} onClick={()=>{const c=consent.value!;void run(()=>request('consents',{termsVersion:c.terms.version,privacyVersion:c.privacy.version,licenseVersion:c.license.version,permissionPolicyVersion:c.permissionPolicyVersion,enabledScopes:enabled}))}}>{t('Сохранить')}</button>
   </div>}
  </Panel>
  <Panel title={t('Разрешения устройств')}>
   {consent.value?.grants.map(grant=><article className="panel" key={grant.deviceId+grant.scope}><strong>{t('permission.'+grant.scope)}</strong><p>{grant.deviceId}</p><small>{date(grant.grantedUtc)}</small><div><button className="btn secondary" disabled={busy} onClick={()=>void run(()=>request('device-grants/revoke',{deviceId:grant.deviceId,scope:grant.scope}))}>{t('Отозвать разрешение')}</button></div></article>)}
   {consent.value&&!consent.value.grants.length&&<p>{t('Нет подтверждённых разрешений устройств.')}</p>}
  </Panel>
 </>;
}
