import {useState,type FormEvent} from 'react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

type Worker={id:string;role:string;model:string;slots:number;active:number;circuitOpen:boolean};
type Result={workerId:string;model:string;text:string};
export function ModelWorkers(){
  const {api}=useApp();
  const workers=useRemote(()=>api.request<Worker[]>('/world-ai/workers/status'),[api],15000);
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
  return <>
    <Header title="Модели мира и панелей" description="Статус LM Studio и ручной запрос к выбранной роли."/>
    <ErrorBanner text={error||workers.error}/>
    <Panel title="Вычислительные узлы">
      {workers.value?.map(w=><div className="line" key={w.id}><strong>{w.model}</strong><span>{w.role} · {w.active}/{w.slots} · {w.circuitOpen?'Временно недоступен':'В пуле'}</span></div>)}
      {workers.value?.length===0&&<p>Узлы LM Studio ещё не настроены.</p>}
    </Panel>
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
