import {useState} from 'react';
import {useSearchParams} from 'react-router-dom';
import {ChevronLeft,ChevronRight,Search} from 'lucide-react';
import type {AuditEntry} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,Badge,ErrorBanner} from '../components/primitives';

type AuditPage={items:AuditEntry[];total:number;page:number;pageSize:number};
export function Audit(){
  const {api}=useApp();
  const [params,setParams]=useSearchParams();
  const target=params.get('target')??'';
  const [query,setQuery]=useState('');
  const [search,setSearch]=useState('');
  const [filter,setFilter]=useState('');
  const [page,setPage]=useState(1);
  const [size,setSize]=useState(25);
  const remote=useRemote(()=>api.request<AuditPage>('/audit/page?'+new URLSearchParams({
    page:String(page),size:String(size),q:search,target,...(filter?{success:filter}:{})
  })),[api,page,size,target,search,filter]);
  const last=Math.max(1,Math.ceil((remote.value?.total??0)/size));
  return <>
    <Header title="Журнал действий" description="Команды, выдача доступа и история развёртываний"/>
    <ErrorBanner text={remote.error}/>
    <Panel title="События" aside={<span className="muted">Всего {remote.value?.total??0}</span>}>
      <form className="audit-toolbar row-actions" onSubmit={event=>{event.preventDefault();setSearch(query.trim());setPage(1)}}>
        <label className="search"><Search size={16}/><input placeholder="Поиск по журналу" value={query} onChange={event=>setQuery(event.target.value)}/></label>
        <label>Результат<select value={filter} onChange={event=>{setFilter(event.target.value);setPage(1)}}>
          <option value="">Все</option><option value="true">Успешные</option><option value="false">Ошибки</option>
        </select></label>
        <label>Строк на странице<select value={size} onChange={event=>{setSize(Number(event.target.value));setPage(1)}}>
          {[10,25,50,100].map(x=><option value={x} key={x}>{x}</option>)}
        </select></label>
        <button className="btn secondary">Применить</button>
        {target&&<button className="btn secondary" type="button" onClick={()=>{setPage(1);setParams({})}}>Снять фильтр узла</button>}
      </form>
      <div className="table-wrap"><table><thead><tr><th>Время</th><th>Оператор</th><th>Действие</th><th>Цель</th><th>Результат</th><th>Подробности</th></tr></thead>
        <tbody>{remote.value?.items.map(entry=><tr key={entry.id}><td><time dateTime={entry.at}>{new Date(entry.at).toLocaleString('ru-RU')}</time></td>
          <td>{entry.actor}</td><td className="mono">{entry.action}</td><td className="mono audit-target">{entry.target}</td>
          <td><Badge ok={entry.success} text={entry.success?'Успешно':'Ошибка'}/></td><td className="audit-detail">{entry.detail||'—'}</td></tr>)}</tbody>
      </table>{!remote.value?.items.length&&<div className="empty">{remote.loading?'Загружаем события…':'Событий по выбранным условиям нет.'}</div>}</div>
      <div className="pagination"><button className="icon-btn" onClick={()=>setPage(value=>value-1)} disabled={page<=1} aria-label="Предыдущая страница"><ChevronLeft size={20}/></button>
        <span>Страница {page} из {last}</span><button className="icon-btn" onClick={()=>setPage(value=>value+1)} disabled={page>=last} aria-label="Следующая страница"><ChevronRight size={20}/></button></div>
    </Panel>
  </>;
}
