import {useState,type FormEvent} from 'react';
import {useApp,useRemote} from '../app/context';
import {Panel,ErrorBanner} from '../components/primitives';
import {Link} from 'react-router-dom';
import type {Maintenance} from './MaintenanceStatus';

type Worker={id:string;role:string;model:string;slots:number;active:number;circuitOpen:boolean};
type Result={workerId:string;model:string;text:string};
const labels:Record<string,string>={world:'Мир',npc:'NPC',evaluation:'Оценка',assistant:'Ассистент','panel-data':'Данные панелей'};
export function ModelWorkers(){
  const {api}=useApp();
  const workers=useRemote(()=>api.request<Worker[]>('/world-ai/workers/status'),[api],15000);
  const maintenance=useRemote(()=>api.request<Maintenance>('/managed/infrastructure-maintenance'),[api],10000);
  const lm=maintenance.value?.components.find(item=>item.name==='lmstudio');
  const [role,setRole]=useState('panel-data'),[prompt,setPrompt]=useState('');
  const [pending,setPending]=useState(false),[result,setResult]=useState<Result|null>(null),[error,setError]=useState('');
  async function submit(event:FormEvent){
    event.preventDefault();
    setPending(true);setError('');setResult(null);
    try{
      setResult(await api.request<Result>('/world-ai/chat',{method:'POST',body:JSON.stringify({role,prompt,maxTokens:400})}));
      workers.reload();
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setPending(false);}
  }
  const local=workers.value?.filter(worker=>worker.id.startsWith('local-'))??[];
  const remote=workers.value?.filter(worker=>!worker.id.startsWith('local-'))??[];
  return <>
    <ErrorBanner text={error||workers.error}/>
    <Panel title="Вычислительные узлы">
      {local.length>0&&<div className="assistant-worker">
        <div><small>Локальная LM Studio</small><strong>{local[0].model}</strong></div>
        <div><small>Назначения</small><span>{local.map(w=>labels[w.role]??w.role).join(', ')}</span></div>
        <div><small>Вычисления</small><span>{local[0].active}/{local[0].slots}</span></div>
      </div>}
      {remote.map(w=><div className="assistant-worker" key={w.id}>
        <div><small>Внешняя модель</small><strong>{w.model}</strong></div>
        <div><small>Назначение</small><span>{labels[w.role]??w.role}</span></div>
        <div><small>{w.circuitOpen?'Временно недоступна':'Вычисления'}</small><span>{w.active}/{w.slots}</span></div>
      </div>)}
      {workers.value?.length===0&&<p>{lm?.state==='repairing'?'Локальная LM Studio разворачивается на VPS Orchestra.':lm?.state==='available'?'API LM Studio отвечает; ожидается доступная загруженная модель.':lm?.message??'Локальные модели пока недоступны. Проверьте раздел «AI и речь».'}</p>}
    </Panel>
    <Link className="btn secondary" to="/infrastructure/ai">Управление AI-инфраструктурой и речью</Link>
    <Panel title="Запрос к модели">
      <form className="stack" onSubmit={submit}>
        <label>Роль<select value={role} onChange={e=>setRole(e.target.value)}><option value="panel-data">Данные панелей</option><option value="world">Правила мира</option></select></label>
        <label>Запрос и данные<textarea maxLength={5000} value={prompt} onChange={e=>setPrompt(e.target.value)} required/></label>
        <button className="btn" disabled={pending||!prompt.trim()}>{pending?'Обработка…':'Отправить'}</button>
      </form>
      {result&&<div><p>{result.model} · {result.workerId}</p><pre className="output">{result.text}</pre></div>}
    </Panel>
  </>;
}
