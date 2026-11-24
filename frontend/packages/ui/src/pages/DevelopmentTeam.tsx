import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

type Membership={id:string;login:string;status:'pending'|'approved'|'rejected'|'revoked'};
type Team={owner:boolean;membership:Membership|null;requests:Membership[]};
const labels={pending:'На рассмотрении',approved:'Доступ выдан',rejected:'Отклонена',revoked:'Доступ отозван'};
const route='/managed/development-team';

export function DevelopmentTeam(){
  const {api,mode}=useApp();
  const data=useRemote(()=>api.request<Team>(route),[api],8000);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  async function submit(path:string,status?:Membership['status']){
    setBusy(true);setError('');
    try{
      await api.request(route+path,{method:'POST',body:status?JSON.stringify({status}):undefined});
      data.reload();
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  const membership=data.value?.membership;
  return <>
    <Header title="Команда разработки" description="Доступ к работе в Orchestra выдаёт главный разработчик."/>
    <ErrorBanner text={error||data.error}/>
    {data.value?.owner?<Panel title="Заявки и участники">
      {!data.value.requests.length&&<p>Заявок пока нет.</p>}
      {data.value.requests.map(item=><div className="capability" key={item.id}>
        <span><strong>{item.login}</strong><small>{labels[item.status]} · {item.id}</small></span>
        {item.status==='pending'&&<>
          <button className="btn tiny" disabled={busy} onClick={()=>void submit('/'+item.id+'/decision','approved')}>Одобрить</button>
          <button className="btn secondary tiny" disabled={busy} onClick={()=>void submit('/'+item.id+'/decision','rejected')}>Отклонить</button>
        </>}
        {item.status==='approved'&&<button className="btn secondary tiny" disabled={busy} onClick={()=>{
          if(window.confirm('Отозвать доступ для '+item.login+'?'))void submit('/'+item.id+'/decision','revoked');
        }}>Отозвать доступ</button>}
      </div>)}
    </Panel>:<Panel title="Мой доступ">
      <p>{membership?labels[membership.status]:'Заявка ещё не отправлена.'}</p>
      {mode==='audit'&&data.value&&(!membership||['rejected','revoked'].includes(membership.status))&&
        <button className="btn" disabled={busy} onClick={()=>void submit('/apply')}>Подать заявку</button>}
      {membership?.status==='approved'&&<p>Войди в Orchestra под тем же личным аккаунтом MysticEgo.</p>}
    </Panel>}
  </>;
}
