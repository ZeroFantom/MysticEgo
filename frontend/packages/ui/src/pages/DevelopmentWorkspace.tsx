import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
type Item={id:string;kind:'issue'|'discussion';title:string;body:string;status:string;assignee?:string;creator:string;githubNumber?:number;syncState:string;updatedAt:string};
type View={items:Item[];connected:boolean;writesEnabled:boolean;lastSync:string;syncError?:string};
export function DevelopmentWorkspace(){
 const {api}=useApp();
 const remote=useRemote(()=>api.request<View>('/managed/development-workspace'),[api],10000,true);
 const [kind,setKind]=useState<'issue'|'discussion'>('issue'),[title,setTitle]=useState(''),[body,setBody]=useState('');
 const [search,setSearch]=useState(''),[filter,setFilter]=useState('all'),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [publish,setPublish]=useState<string|null>(null);
 async function action(path:string,data:unknown={}){
  setBusy(true);setError('');
  try{await api.request('/managed/development-workspace'+path,{method:'POST',body:JSON.stringify(data)});remote.reload();return true}
  catch(e){setError(String(e));return false}finally{setBusy(false);setPublish(null)}
 }
 const rows=(remote.value?.items??[]).filter(x=>x.kind===kind&&(filter==='all'||x.status===filter)&&(x.title+' '+x.body).toLowerCase().includes(search.toLowerCase()));
 return <>
  <Header title="Development workspace" description="Team tasks, discussions and GitHub synchronization"/>
  <ErrorBanner text={error||remote.error||remote.value?.syncError}/>
  <Panel title="Repository · ZeroFantom/MysticEgo">
   <p>{remote.value?.connected?'GitHub connected · synchronization every two minutes':'Local workspace · GitHub not configured'} · {remote.value?.writesEnabled?'Publishing enabled':'Publishing disabled'}</p>
   <a href="https://github.com/ZeroFantom/MysticEgo" target="_blank" rel="noreferrer">Open GitHub</a>
   <div className="actions"><button className="btn secondary" onClick={()=>setKind('issue')}>Tasks</button><button className="btn secondary" onClick={()=>setKind('discussion')}>Discussions</button></div>
   <form className="stack" onSubmit={async e=>{e.preventDefault();if(await action('',{kind,title,body})){setTitle('');setBody('')}}}>
    <label>Title<input required maxLength={200} value={title} onChange={e=>setTitle(e.target.value)}/></label>
    <label>Description<textarea maxLength={16000} value={body} onChange={e=>setBody(e.target.value)}/></label>
    <button className="btn" disabled={busy}>Create local {kind==='issue'?'task':'discussion'}</button>
   </form>
  </Panel>
  <Panel title={kind==='issue'?'Task board':'Discussion board'}>
   <div className="actions"><input placeholder="Search" value={search} onChange={e=>setSearch(e.target.value)}/><select value={filter} onChange={e=>setFilter(e.target.value)}><option value="all">All statuses</option><option value="open">Open</option><option value="in-progress">In progress</option><option value="closed">Closed</option></select><button className="btn secondary" onClick={remote.reload}>Refresh</button></div>
   <div className="stack">{rows.map(item=><article className="panel" key={item.id}>
    <h3>{item.title}</h3><small>{item.status} · {item.syncState} · {new Date(item.updatedAt).toLocaleString()}</small>
    <p style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{item.body}</p>
    {item.assignee&&<p>Assignee: {item.assignee}</p>}
    {item.githubNumber&&<a href={'https://github.com/ZeroFantom/MysticEgo/'+(item.kind==='issue'?'issues':'discussions')+'/'+item.githubNumber} target="_blank" rel="noreferrer">GitHub #{item.githubNumber}</a>}
    <div className="actions">
     {item.syncState==='local'&&item.kind==='issue'&&item.status==='open'&&<button className="btn secondary" disabled={busy} onClick={()=>void action('/'+item.id+'/take')}>Take task</button>}
     {item.syncState==='local'&&item.status==='in-progress'&&<><button className="btn secondary" disabled={busy} onClick={()=>void action('/'+item.id+'/release')}>Release</button><button className="btn" disabled={busy} onClick={()=>void action('/'+item.id+'/complete')}>Complete</button></>}
     {item.syncState==='local'&&remote.value?.writesEnabled&&<button className="btn secondary" disabled={busy} onClick={()=>setPublish(item.id)}>Publish to GitHub</button>}
    </div>
    {publish===item.id&&<div role="alert"><p>Publish this title and description to the configured GitHub repository? Check for private information first.</p><button className="btn" disabled={busy} onClick={()=>void action('/'+item.id+'/publish')}>Confirm publication</button> <button className="btn secondary" onClick={()=>setPublish(null)}>Cancel</button></div>}
   </article>)}</div>
   {!rows.length&&<p>No matching items.</p>}
  </Panel>
 </>;
}
