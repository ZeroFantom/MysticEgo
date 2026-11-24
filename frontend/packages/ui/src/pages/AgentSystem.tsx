import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {NetworkRepairPanel} from '../components/NetworkRepairPanel';
import {Panel,Header,ErrorBanner,spin} from '../components/primitives';

export function AgentSystem(){
  const {api}=useApp();
  const info=useRemote(()=>api.system(),[api]);
  const update=useRemote(()=>api.agentUpdateStatus(),[api]);
  const [checking,setChecking]=useState(false);
  const [error,setError]=useState('');

  async function check(){
    setChecking(true);
    setError('');
    try{await api.agentUpdateCheck();update.reload()}
    catch(problem){setError(String(problem))}
    finally{setChecking(false)}
  }

  return <>
    <Header title="Этот компьютер" description="Состояние Agent и клубные обновления"/>
    <ErrorBanner text={info.error}/>
    <Panel title="Системная информация">
      {info.loading?spin:<div className="info-grid">
        <div><small>Устройство</small><strong>{info.value?.host||'—'}</strong></div>
        <div><small>Операционная система</small><strong>{info.value?.os||'—'}</strong></div>
        <div><small>Время работы</small><strong>{info.value?.uptimeMs==null?'—':`${Math.floor(info.value.uptimeMs/60_000)} мин`}</strong></div>
      </div>}
    </Panel>
    <Panel title="Обновления Agent">
      <div className="info-grid">
        <div><small>Состояние</small><strong>{update.value?.status||'Ожидание проверки'}</strong></div>
        <div><small>Доступная версия</small><strong>{update.value?.version||'—'}</strong></div>
        <div><small>Последняя проверка</small><strong>{update.value?.checkedAt?new Date(update.value.checkedAt).toLocaleString():'—'}</strong></div>
      </div>
      <ErrorBanner text={error||update.error}/>
      <p className="footnote">AuditDesktop проверяет подпись релизов Local и Global. Установка ждёт завершения гостевого сеанса и снижения нагрузки на ПК.</p>
      <button className="btn secondary" disabled={checking} onClick={check}>{checking?'Проверяем…':'Проверить обновление сейчас'}</button>
    </Panel>
    <NetworkRepairPanel/>
  </>;
}
