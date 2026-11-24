import {useEffect,useState,type FormEvent} from 'react';
import {useNavigate} from 'react-router-dom';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

type Release={tag:string;publishedAt?:string;prerelease?:boolean};
type ReleaseInfo={installed?:string;releaseOrigin?:string};
const views:Record<string,{path:string;label:string}>={
  overview:{path:'/',label:'Обзор'},nodes:{path:'/nodes',label:'Узлы сети'},
  deployment:{path:'/setup',label:'Развёртывание'},branches:{path:'/project-branches',label:'Ветки GitHub'},
  team:{path:'/development-team',label:'Команда разработки'},models:{path:'/model-workers',label:'Модели LM Studio'}
};

export function ReleaseStatus(){
  const {session,mode}=useApp();
  const [installed,setInstalled]=useState(''),[latest,setLatest]=useState<Release|null>(null),[failed,setFailed]=useState(false);
  useEffect(()=>{
    let active=true;
    const signal=AbortSignal.timeout(12000);
    async function load(){
      const info:ReleaseInfo=await fetch('/release-info.json',{signal}).then(r=>r.ok?r.json():{}).catch(()=>({}));
      if(!active)return;
      setInstalled(info.installed??'');setLatest(null);setFailed(false);
      const origins=[mode==='orchestra'?session.url:session.authUrl,info.releaseOrigin].filter((x):x is string=>!!x);
      for(const origin of new Set(origins)){
        try{
          const url=new URL('/api/v1/public/project-release',origin);
          const response=await fetch(url,{signal,credentials:'omit',redirect:'error'});
          if(!response.ok)continue;
          const data=await response.json() as {latest:Release|null};
          if(active)setLatest(data.latest);
          return;
        }catch{/* try the central release authority */}
      }
      if(active)setFailed(true);
    }
    void load();
    return ()=>{active=false};
  },[session.url,session.authUrl,mode]);
  return <div className="release-status" aria-label="Версии MysticEgo">
    <small>Установлено <strong>{installed||'не указано'}</strong></small>
    <small>GitHub <strong>{latest?.tag||(failed?'недоступен':'—')}</strong>{latest?.prerelease?' · предварительный':''}</small>
  </div>;
}

type Branch={name:string;sha:string;isProtected:boolean};
export function ProjectBranches(){
  const {api}=useApp();
  const data=useRemote(()=>api.request<{branches:Branch[];truncated:boolean}>('/managed/project-branches'),[api]);
  const [filter,setFilter]=useState('');
  const branches=data.value?.branches.filter(branch=>branch.name.toLowerCase().includes(filter.toLowerCase()))??[];
  return <>
    <Header title="Ветки GitHub" description="ZeroFantom/MysticEgo" action={<button className="btn secondary" onClick={data.reload}>Обновить</button>}/>
    <ErrorBanner text={data.error}/>
    <Panel title="Ветки проекта">
      <label>Найти ветку<input value={filter} onChange={event=>setFilter(event.target.value)}/></label>
      <div className="table-wrap"><table><thead><tr><th>Ветка</th><th>Коммит</th><th>Защита</th></tr></thead><tbody>
        {branches.map(branch=><tr key={branch.name}><td><a href={'https://github.com/ZeroFantom/MysticEgo/tree/'+encodeURIComponent(branch.name)} target="_blank" rel="noreferrer">{branch.name}</a></td>
          <td><code>{branch.sha.slice(0,8)}</code></td><td>{branch.isProtected?'Защищена':'—'}</td></tr>)}
      </tbody></table></div>
      {data.value?.truncated&&<p>Показаны первые 100 веток.</p>}
      {data.value&&!branches.length&&<p>Ветки не найдены.</p>}
    </Panel>
  </>;
}

type AssistantReply={text:string;view?:string;toolResult?:string};
export function DeveloperAssistant(){
  const {api}=useApp();
  const navigate=useNavigate();
  const [prompt,setPrompt]=useState(''),[reply,setReply]=useState<AssistantReply|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function submit(event:FormEvent){
    event.preventDefault();setBusy(true);setError('');setReply(null);
    try{
      setReply(await api.request<AssistantReply>('/managed/panel-assistant',{method:'POST',body:JSON.stringify({text:prompt}),signal:AbortSignal.timeout(60000)}));
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  const view=reply?.view&&Object.hasOwn(views,reply.view)?views[reply.view]:null;
  return <>
    <Header title="Помощник разработчика" description="Локальная модель на сервере Orchestra"/>
    <ErrorBanner text={error}/>
    <Panel title="Запрос">
      <form className="stack" onSubmit={submit}>
        <label>Что нужно сделать?<textarea required maxLength={3000} value={prompt} onChange={event=>setPrompt(event.target.value)}/></label>
        <button className="btn" disabled={busy||!prompt.trim()}>{busy?'Обработка…':'Отправить'}</button>
      </form>
      {reply&&<div className="stack"><p style={{whiteSpace:'pre-wrap'}}>{reply.text}</p>
        {reply.toolResult&&<pre className="output">{reply.toolResult}</pre>}
        {view&&<button className="btn secondary" onClick={()=>navigate(view.path)}>Открыть «{view.label}»</button>}
      </div>}
    </Panel>
  </>;
}
