import React from 'react';
export default function DomainPicker({form,domains,now,onChange,managed=true,suggestion={}}){
 const rows=domains.data?.domains??[],usable=rows.filter(d=>d.usable),fresh=!!domains.at&&!domains.error&&now-domains.at<90000;
 const zone=usable.find(d=>d.fqdn===form.dnsZone);
 return <div className="dns-picker"><div className="sectiontop"><b>Домен и DNS</b><span className={'sync '+(!fresh?'stale':'')}><i/>{domains.error?'Ошибка API':!domains.at?'Загрузка…':!fresh?'Данные устарели':`${usable.length} активных · автоматически`}</span></div>
 <label>Домен Timeweb<select value={form.dnsZone||''} onChange={e=>onChange({dnsZone:e.target.value,domain:''})}><option value="">Внешний DNS (ручной режим)</option>{form.dnsZone&&!zone&&<option value={form.dnsZone} disabled>{form.dnsZone} · не подтверждён</option>}{rows.map(d=><option key={d.fqdn} value={d.fqdn} disabled={!d.usable}>{d.fqdn}{d.usable?'':` · ${d.status||'статус неизвестен'}`}</option>)}</select></label>
 {domains.error&&<small className="warning">{domains.error}</small>}
 {zone?<><div className="dns-preview"><small>АВТОМАТИЧЕСКОЕ ИМЯ · {form.role==='Orchestra'?'orchestra':'global-001, global-002, …'}</small><code>{form.domain||'Подбираем свободное имя…'}</code></div>{suggestion.error&&<p className="warning">{suggestion.error}</p>}<small>{managed?'Свободное имя подбирается по реальным DNS и поддоменам. Проверка конфликта повторяется перед заказом.':'DNS-записи автоматически создаются через Quick Deploy во вкладке «Инфраструктура».'}</small></>:<label>Полное DNS-имя (только внешний DNS)<input value={form.domain} onChange={e=>onChange({domain:e.target.value,dnsZone:''})} placeholder="orchestra.example.org"/></label>}
 </div>
}
