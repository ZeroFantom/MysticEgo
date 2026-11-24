import {useEffect, useState} from 'react';
import {useApp} from '../app/context';
import {Panel, ErrorBanner} from '../components/primitives';

type Station = {id:string;name:string;roles:string[];enabled:boolean;report?:{seen:string;healthy:string[];error?:string}};
export function Stations(){
  const {api}=useApp();
  const [rows,setRows]=useState<Station[]>([]),[name,setName]=useState(''),[preset,setPreset]=useState('agent');
  const [localSsh,setLocalSsh]=useState(''),[globalSsh,setGlobalSsh]=useState('');
  const [error,setError]=useState(''),[busy,setBusy]=useState(false);
  useEffect(()=>{
    let active=true;
    const load=async()=>{try{const data=await api.request<Station[]>('/stations');if(active)setRows(data)}catch(e){if(active)setError(String(e))}};
    void load();const timer=setInterval(load,4000);return()=>{active=false;clearInterval(timer)};
  },[api]);
  async function create(){
    setBusy(true);setError('');
    try{
      const profile=await api.request('/stations',{method:'POST',body:JSON.stringify({name:name.trim(),preset,localSsh:preset==='agent'?localSsh.trim():null,globalSsh:globalSsh.trim()})});
      const url=URL.createObjectURL(new Blob([JSON.stringify(profile,null,2)],{type:'application/json'}));
      const link=document.createElement('a');link.href=url;link.download='station.json';link.click();
      setTimeout(()=>URL.revokeObjectURL(url),30000);setName('');
      setRows(await api.request<Station[]>('/stations'));
    }catch(e){setError(String(e))}finally{setBusy(false)}
  }
  async function toggle(row:Station){
    setBusy(true);setError('');
    try{await api.request('/stations/'+row.id,{method:'PUT',body:JSON.stringify({enabled:!row.enabled})});setRows(await api.request<Station[]>('/stations'))}
    catch(e){setError(String(e))}finally{setBusy(false)}
  }
  return <><ErrorBanner text={error}/><Panel title="Компьютеры стенда">
    {rows.length===0&&<p>Зарегистрированных компьютеров пока нет.</p>}
    {rows.map(row=><div className="social-row" key={row.id}><span><strong>{row.name} · {row.roles.join(', ')}</strong>
      <small>{!row.report?'Ожидается первый запуск':Date.now()-Date.parse(row.report.seen)>20000?'Связь прервана':
      row.report.error||('На связи · работают: '+(row.report.healthy.join(', ')||'—'))}</small></span>
      <button className="btn secondary" disabled={busy} onClick={()=>toggle(row)}>{row.enabled?'Остановить роли':'Запустить роли'}</button></div>)}
  </Panel><Panel title="Добавить компьютер"><div className="stack">
    <label>Имя ПК<input value={name} onChange={e=>setName(e.target.value)} maxLength={64} placeholder="Зал-02"/></label>
    <label>Назначение<select value={preset} onChange={e=>setPreset(e.target.value)}>
      <option value="agent">Рабочее место · Agent</option><option value="site">Площадка · Local и Agent</option>
      <option value="home">Домашний ПК · AuditOrchestra</option><option value="global">Узел организации · Global</option>
    </select></label>
    {['agent','site'].includes(preset)&&<details><summary>Родительские узлы на других ПК</summary>
      {preset==='agent'&&<label>SSH площадки Local<input value={localSsh} onChange={e=>setLocalSsh(e.target.value)} placeholder="user@site-pc"/></label>}
      <label>SSH узла Global<input value={globalSsh} onChange={e=>setGlobalSsh(e.target.value)} placeholder="user@global-pc"/></label>
      <p>Пустое поле означает, что родительская роль работает на центральном ПК.</p></details>}
    <button className="btn" disabled={busy||!name.trim()} onClick={create}>Выдать файл подключения</button>
    <p>Передайте station.json владельцу этого ПК по закрытому каналу. На ПК нужны архив решения и SSH-доступ к центру. Запуск описан в README.</p>
  </div></Panel></>;
}
