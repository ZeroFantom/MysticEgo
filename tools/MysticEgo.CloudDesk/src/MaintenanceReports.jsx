import React from 'react';
import './maintenance-reports.css';

const labels={orchestra:'Orchestra',worker:'Служба обслуживания',docker:'Docker / Compose',lmstudio:'LM Studio',voice:'Распознавание речи','postgres-service':'PostgreSQL',database:'База данных'};
const states={available:'Готово',active:'Активна',degraded:'Требует внимания',repairing:'Восстановление',checking:'Проверяется',failed:'Ошибка',inactive:'Неактивна',external:'Внешняя конфигурация',disabled:'Отключено'};
const number=(v,div=1,suffix='')=>Number.isFinite(v)?(v/div).toLocaleString(undefined,{maximumFractionDigits:1})+suffix:'Нет замера';
const time=value=>value?new Date(value).toLocaleTimeString():'—';
function State({state}){return <span className="maintenance-state" data-state={state||'unknown'}><i/>{states[state]??state??'Нет данных'}</span>}
export default function MaintenanceReports({plan,format,onFormat,exporting,error,onReport}){
 if(!plan)return null;
 const components=plan.components??[],services=plan.services??[];
 return <section className="maintenance-reports" aria-label="Диагностические отчёты">
  <header className="maintenance-report-heading"><div><h2>Диагностические отчёты</h2><p>Состояние компонентов, ресурсы и системные службы выбранного Orchestra.</p></div>
   {!plan.legacy&&<div className="maintenance-report-tools"><label>Формат отчёта<select aria-label="Формат отчёта" value={format} onChange={e=>onFormat(e.target.value)}><option value="xlsx">XLSX</option><option value="json">JSON</option><option value="txt">TXT</option></select></label><button data-interface-id="report-all" disabled={exporting} onClick={()=>void onReport('all')}>Сводный отчёт</button></div>}
  </header>
  {error&&<p className="warning" role="alert">{error}</p>}
  <h3>Компоненты <small>{components.length}</small></h3>
  <div className="maintenance-card-grid">{components.map(c=><article className="maintenance-report-card" key={c.name}>
    <div className="maintenance-card-title"><span className="maintenance-card-icon" aria-hidden="true">◈</span><State state={c.state}/></div>
    <h4>{labels[c.name]??c.name}</h4><small>{time(c.checkedAt)}</small>
    {c.message&&<p>{c.message}</p>}
    {c.reason&&<small>Причина: {c.reason}</small>}
    {c.state==='degraded'&&c.nextRetryAt&&<small>Следующая попытка: {new Date(c.nextRetryAt).toLocaleString()}</small>}
    {!plan.legacy&&<button data-interface-id={/^[a-z][a-z0-9-]{0,26}$/.test(c.name)?'report-'+c.name:undefined} disabled={exporting} onClick={()=>void onReport(c.name)}>Скачать отчёт</button>}
  </article>)}</div>
  {!components.length&&<p>Нет отчёта о компонентах.</p>}
  {!plan.legacy&&<><h3>Системные службы <small>{services.length}</small></h3>
  <div className="maintenance-card-grid">{services.map(s=><article className="maintenance-report-card" key={s.unit}>
    <div className="maintenance-card-title"><span className="maintenance-card-icon" aria-hidden="true">▤</span><State state={s.activeState}/></div>
    <h4>{s.unit}</h4><small>{s.subState??'—'} · {time(s.at)}</small>
    {s.measurement==='not-applicable'?<p>Таймер / сокет / путь</p>:<div className="maintenance-card-metrics"><span>CPU<strong>{number(s.cpuPercent,1,'%')}</strong></span><span>RAM<strong>{number(s.memoryBytes,1048576,' MiB')}</strong></span></div>}
    {s.type==='oneshot'&&<div className="maintenance-last-run"><small>Последнее выполнение</small><strong>{s.lastResult??s.result??'—'}</strong><small>{number(s.lastDurationMilliseconds,1000,' с')}</small></div>}
    <button disabled={exporting} onClick={()=>void onReport(s.unit)}>Скачать отчёт</button>
  </article>)}</div>{!services.length&&<p>Нет данных о системных службах.</p>}</>}
 </section>
}
