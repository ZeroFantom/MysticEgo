import {useState} from 'react';
import {useApp,useRemote} from '../app/context';

type Status={serverId:number;ipv4:string;status:string};
const actions={start:'Запустить',shutdown:'Выключить',reboot:'Перезагрузить'} as const;
export function GlobalTimeweb({nodeId}:{nodeId:string}){
  const {api}=useApp();
  const path='/managed/nodes/'+nodeId+'/timeweb';
  const data=useRemote(()=>api.request<Status>(path),[api,path],15000);
  const [busy,setBusy]=useState(false),[message,setMessage]=useState('');
  async function power(action:keyof typeof actions){
    if(!data.value||!window.confirm(actions[action]+' VPS #'+data.value.serverId+'?'))return;
    setBusy(true);setMessage('');
    try{
      await api.request(path+'/power',{method:'POST',body:JSON.stringify({action,confirmed:true})});
      setMessage('Timeweb принял запрос. Дождитесь обновления статуса.');
      data.reload();
    }catch(error){
      setMessage(error instanceof Error?error.message:String(error));data.reload();
    }finally{setBusy(false);}
  }
  return <section className="stack">
    <h3>VPS Timeweb</h3>
    {data.error&&<p>{data.error}</p>}
    {data.value&&<>
      <p>#{data.value.serverId} · {data.value.ipv4} · {data.value.status}</p>
      <div className="row-actions">{(Object.keys(actions) as (keyof typeof actions)[]).map(action=>
        <button className="btn secondary tiny" key={action}
          disabled={busy||!!data.error||(action==='start'?data.value?.status!=='off':data.value?.status!=='on')}
          onClick={()=>void power(action)}>{actions[action]}</button>)}</div>
    </>}
    {message&&<p role="status">{message}</p>}
  </section>;
}
