import {useCallback,useState} from 'react';
import {QRCodeSVG} from 'qrcode.react';
import {Link} from 'react-router-dom';
import {NodeApi} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner} from '../components/primitives';

type Request={id:string;region:string;reason:string;status:string;createdAt:string;hasSubscription:boolean;userId?:string;panelId?:string;clientEmail?:string};
type PanelNode={id:string;label:string;baseUrl:string;region:string;status:string};
export function VpnAccess(){
 const {session,mode}=useApp();
 // Audit clients request access from Global; no private 3x-ui panel URL is exposed.
 const endpoint=['audit','local','agent'].includes(mode)?(import.meta.env.VITE_GLOBAL_URL||'http://127.0.0.1:5100'):session.url;
 const api=new NodeApi(endpoint,()=>session.token);
 const requests=useRemote(()=>api.request<Request[]>('/vpn/me'),[session.token,endpoint]);
 const admin=mode==='orchestra'||mode==='global';
 const pending=useRemote(()=>admin?api.request<Request[]>('/vpn/admin/requests'):Promise.resolve([]),[admin,session.token,endpoint]);
 const panels=useRemote(()=>admin?api.request<PanelNode[]>('/vpn/admin/panels'):Promise.resolve([]),[admin,session.token,endpoint]);
 const [region,setRegion]=useState('auto'),[reason,setReason]=useState(''),[msg,setMsg]=useState('');
 const [subscription,setSubscription]=useState('');
 const [panelName,setPanelName]=useState(''),[panelUrl,setPanelUrl]=useState(''),[panelRegion,setPanelRegion]=useState('');
 const [selected,setSelected]=useState(''),[decisionUrl,setDecisionUrl]=useState('');
 const [decisionPanel,setDecisionPanel]=useState(''),[clientEmail,setClientEmail]=useState('');
 const [deployTargets,setDeployTargets]=useState(''),[deployAdminUrl,setDeployAdminUrl]=useState('');
 const refresh=()=>{requests.reload();pending.reload();panels.reload()};
 const invoke=useCallback(async (action:()=>Promise<unknown>,done:string)=>{try{await action();setMsg(done);refresh()}catch(e){setMsg(String(e))}},[api]);
 async function reveal(id:string){
  try {const result=await api.request<{url:string}>('/vpn/requests/'+encodeURIComponent(id)+'/subscription');
   setSubscription(result.url);setMsg('Ссылка открыта. Не пересылай её другим пользователям.');
  }catch(e){setMsg(String(e))}
 }
 const status=(s:string)=>({pending:'На рассмотрении',approved:'Активно',rejected:'Отказ',revoked:'Отозвано',revocation_pending:'Отзыв на сервере не подтверждён'} as Record<string,string>)[s]||s;
 const statusColor=(s:string)=>s==='approved'?'var(--success, #31b68a)':s==='pending'?'var(--warning, #dba750)':s==='revocation_pending'?'#ff7869':'var(--muted, #a0a8b5)';
 return <div className="vpn-access"><Header title="VPN · подключения" description="Заявка, статус и персональная подписка для Happ. Доступ выдаётся только после одобрения."/>
 <ErrorBanner text={requests.error||pending.error||panels.error}/>
 <div className="two-col"><Panel title="Новая заявка"><div className="stack"><label>Регион<input maxLength={48} value={region} onChange={e=>setRegion(e.target.value)}/></label>
 <label>Для чего требуется доступ<textarea maxLength={500} rows={3} value={reason} onChange={e=>setReason(e.target.value)}/></label>
 <button className="btn" onClick={()=>invoke(()=>api.request('/vpn/requests',{method:'POST',body:JSON.stringify({region,reason})}),'Заявка отправлена')}>Отправить заявку</button></div></Panel>
 <Panel title="Мои подключения">{requests.value?.length?requests.value.map(r=><div key={r.id} className="stack" style={{marginBottom:16}}><strong>{r.region} · <span style={{color:statusColor(r.status)}}>{status(r.status)}</span></strong><small>{new Date(r.createdAt).toLocaleString()}</small>
 {r.hasSubscription&&<button className="btn secondary" onClick={()=>void reveal(r.id)} type="button">Показать QR и ссылку Happ</button>}</div>):<p className="footnote">Пока заявок нет.</p>}</Panel></div>
 {subscription&&<Panel title="Персональный QR-код Happ"><div className="stack"><p className="footnote">🔒 QR содержит секретную ссылку подписки. Не публикуй его и не отправляй другим. Закрой карточку после импорта.</p>
 <div style={{background:'white',padding:16,width:'max-content',borderRadius:12}}><QRCodeSVG value={subscription} size={216} includeMargin/></div>
 <label>Подписка <input readOnly value={subscription} autoComplete="off"/></label><div><button className="btn" onClick={()=>void navigator.clipboard.writeText(subscription)}>Копировать</button>{' '}<button className="btn secondary" onClick={()=>{setSubscription('')}}>Скрыть</button></div>
 <p className="footnote">Открой Happ и импортируй подписку по QR или ссылке. Режим системного прокси / TUN выбирается в клиенте Happ, если поддерживается твоей ОС.</p></div></Panel>}
 {admin&&<><Panel title="Администрирование · заявки">{pending.value?.map(r=><div className="stack" key={r.id} style={{marginBottom:18,borderBottom:'1px solid #5555',paddingBottom:16}}><strong>{r.userId} · {status(r.status)}</strong><small>{r.region} · {r.reason}</small>
 {r.status==='pending'&&<>
 <label>Управляемый сервер 3x-ui<select value={selected===r.id?decisionPanel:''} onChange={e=>{setSelected(r.id);setDecisionPanel(e.target.value)}}><option value="">Выбери сервер</option>{panels.value?.map(p=><option key={p.id} value={p.id}>{p.label} · {p.region}</option>)}</select></label>
 <label>Идентификатор клиента в 3x-ui<input autoComplete="off" placeholder="mysticego_user_01" maxLength={128} value={selected===r.id?clientEmail:''} onChange={e=>{setSelected(r.id);setClientEmail(e.target.value)}}/></label>
 <label>Персональная HTTPS-подписка<input type="url" autoComplete="off" placeholder="https://vpn.example/sub/..." value={selected===r.id?decisionUrl:''} onChange={e=>{setSelected(r.id);setDecisionUrl(e.target.value)}}/></label>
 <div><button className="btn" disabled={selected!==r.id||!decisionPanel||!clientEmail||!decisionUrl.startsWith('https://')} onClick={()=>invoke(()=>api.request('/vpn/admin/requests/'+r.id,{method:'PUT',body:JSON.stringify({status:'approved',subscriptionUrl:decisionUrl,panelId:decisionPanel,clientEmail})}),'Клиент проверен на 3x-ui, заявка одобрена')}>Проверить клиента и одобрить</button>{' '}<button className="btn secondary" onClick={()=>invoke(()=>api.request('/vpn/admin/requests/'+r.id,{method:'PUT',body:JSON.stringify({status:'rejected',subscriptionUrl:null,panelId:null})}),'Заявка отклонена')}>Отклонить</button></div></>}
 {(r.status==='approved'||r.status==='revocation_pending')&&<div className="stack">
 {r.clientEmail&&<small>Клиент 3x-ui: {r.clientEmail}</small>}
 {r.status==='revocation_pending'&&<p className="footnote" style={{color:'#ff7869'}}>Подписка скрыта от пользователя, но отключение на удалённом сервере пока не подтверждено. Проверь сервер и повтори действие.</p>}
 <button className="btn secondary" onClick={()=>invoke(()=>api.request('/vpn/admin/requests/'+r.id,{method:'PUT',body:JSON.stringify({status:'revoked',subscriptionUrl:null,panelId:null})}),'3x-ui подтвердил удаление клиента, доступ отозван')}>{r.status==='revocation_pending'?'Повторить удаление в 3x-ui':'Отозвать с удалением клиента в 3x-ui'}</button></div>}</div>)}</Panel>
 <Panel title="План развёртывания VPN на нескольких серверах"><div className="stack"><label>SSH-хосты (по одному на строке, пользователь@сервер)<textarea rows={4} value={deployTargets} onChange={e=>setDeployTargets(e.target.value)} placeholder={'deploy@vpn1.example.org\ndeploy@vpn2.example.org'}/></label>
 <label>HTTPS URL Global или Orchestra<input value={deployAdminUrl} type="url" onChange={e=>setDeployAdminUrl(e.target.value)} placeholder="https://global.example.org"/></label>
 <button className="btn secondary" type="button" onClick={async()=>{try{
   const hosts=deployTargets.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
   const data=await api.request<unknown>('/vpn/admin/deployments/plan',{method:'POST',body:JSON.stringify({nodes:hosts.map(host=>({host,region:panelRegion,globalUrl:mode==='global'?deployAdminUrl:null,orchestraUrl:mode==='orchestra'?deployAdminUrl:null}))})});
   const blob=new Blob([JSON.stringify(data,null,2)],{type:'application/json'});const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='mysticego-vpn-deployment.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);setMsg('План экспортирован. Запусти единый установщик с --manifest.');
  }catch(e){setMsg(String(e))}}}>Экспортировать план для единого установщика</button>
 <p className="footnote">Экспорт не запускает SSH из браузера. Установщик проверяет зависимости до развёртывания, после чего оператор настраивает HTTPS, входящие порты и клиентов 3x-ui.</p></div></Panel>
 <Panel title="Серверы 3x-ui"><div className="stack">{panels.value?.map(x=><p key={x.id}>{x.label} · {x.region} · {x.status}</p>)}
 <label>Название<input value={panelName} onChange={e=>setPanelName(e.target.value)}/></label>
 <label>Адрес защищённой панели<input value={panelUrl} onChange={e=>setPanelUrl(e.target.value)}/></label>
 <label>Регион<input value={panelRegion} onChange={e=>setPanelRegion(e.target.value)}/></label>
 <button className="btn" onClick={()=>invoke(()=>api.request('/vpn/admin/panels',{method:'POST',body:JSON.stringify({label:panelName,baseUrl:panelUrl,region:panelRegion})}),'Узел зарегистрирован')}>Зарегистрировать</button>
 <p className="footnote">Развёртывание Linux-сервера выполняется одним установщиком MysticEgo с ролью vpn. Создание клиентов выполняется в 3x-ui. Перед одобрением MysticEgo проверяет наличие клиента через API; при отзыве выполняется удаление. Для каждого зарегистрированного сервера требуется секретный токен <code>Vpn:PanelTokens:&lt;GUID&gt;</code> на бэкенде.</p></div></Panel></>}
 {msg&&<p className="footnote" role="status">{msg}</p>}<p><Link to="/profile">← Назад к профилю</Link></p></div>;
}
