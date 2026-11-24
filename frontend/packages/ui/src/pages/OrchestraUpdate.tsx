import {useEffect,useState} from 'react';
import {useApp} from '../app/context';

type Candidate={tag:string;digest:string;asset:string;id:number;size:number};
type Status={phase:string;current?:string;candidate?:Candidate;message?:string};
const phases:Record<string,string>={ready:'Доступно обновление',current:'Установлена актуальная версия',downloading:'Скачивание и проверка релиза',switching:'Перезапуск и проверка Orchestra',failed:'Обновление прервано',unavailable:'Релиз недоступен'};

export function OrchestraUpdate(){
  const {api}=useApp();
  const [status,setStatus]=useState<Status|null>(null);
  const [error,setError]=useState('');
  const [pending,setPending]=useState(false);
  useEffect(()=>{
    let stopped=false,timer:ReturnType<typeof setTimeout>;
    async function poll(){
      try{const next=await api.request<Status>('/orchestra/update');if(!stopped){setStatus(next);setError('');}}
      catch(e){if(!stopped){setStatus(null);setError(e instanceof Error?e.message:String(e));}}
      finally{if(!stopped)timer=setTimeout(poll,4000);}
    }
    void poll();
    return ()=>{stopped=true;clearTimeout(timer);};
  },[api]);
  const candidate=status?.candidate;
  useEffect(()=>{if(status&&!['ready','failed'].includes(status.phase))setPending(false);},[status]);
  async function approve(){
    if(!candidate||!window.confirm('Установить '+candidate.tag+'? Orchestra будет временно недоступен.'))return;
    setPending(true);
    try{
      await api.request('/orchestra/update',{method:'POST',body:JSON.stringify({tag:candidate.tag,digest:candidate.digest,confirmed:true})});
    }catch(e){setPending(false);setError(e instanceof Error?e.message:String(e));}
  }
  return <section className="panel"><h1>Обновление Orchestra</h1>
    {error&&<p role="alert">{error}</p>}
    {status&&<><p>Установленная версия · {status.current??'Не определена'}</p>
      <p role="status">{pending?'Подтверждено, ожидаем службу обновления':phases[status.phase]??status.phase}</p>
      {status.message&&<p>{status.message}</p>}
      {candidate&&<><p>Релиз GitHub · {candidate.tag}</p><p><code>{candidate.digest}</code></p></>}
      {candidate&&['ready','failed'].includes(status.phase)&&<button className="btn primary" disabled={pending} onClick={approve}>Подтвердить обновление</button>}
    </>}
  </section>;
}
