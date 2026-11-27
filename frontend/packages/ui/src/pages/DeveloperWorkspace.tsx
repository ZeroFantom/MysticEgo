import {useEffect,useState,type FormEvent} from 'react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import './developer-workspace.css';

type Kind='issue'|'discussion';
type Tab='internal'|Kind;
type Comment={author:string;body:string;createdAt:string};
type Work={kind:Kind;number:number;title:string;body:string;state:string;author:string;assignees:string[];labels:string[];updatedAt:string;url:string;category?:string;commentCount:number};
type Internal={id:string;kind:Kind;title:string;body:string;state:string;creatorId:string;creator:string;assigneeId?:string;assignee?:string;updatedAt:string;comments:Comment[];publication:'local'|'pending'|'published';gitHubNumber?:number};
type Page={items:Work[];page:number;hasMore:boolean;cursor?:string;fetchedAt:string};
type Detail={item:Work;comments:Comment[];moreComments:boolean};
type Connection={github:{connected:boolean;writable:boolean;githubLogin?:string;repository:string};owner:boolean;actorId:string};
type Category={id:string;name:string};
const base='/managed/developer-workspace';
const status=(state:string,assignee:boolean)=>state==='closed'?'Done':assignee?'In progress':'Backlog';
const date=(v:string)=>new Date(v).toLocaleString();
function Comments({items}:{items:Comment[]}){
 return <div className="dw-comments">{items.map((c,i)=><article key={c.createdAt+'-'+i}><header><strong>{c.author||'Unknown'}</strong><small>{date(c.createdAt)}</small></header><p>{c.body}</p></article>)}</div>;
}

export function DeveloperWorkspace(){
 const {api,preferenceScope}=useApp();
 const [tab,setTab]=useState<Tab>('internal'),[page,setPage]=useState(1),[cursors,setCursors]=useState<(string|undefined)[]>([undefined]);
 const [search,setSearch]=useState(''),[filter,setFilter]=useState('all'),[selected,setSelected]=useState<string|null>(null);
 const [kind,setKind]=useState<Kind>('issue'),[title,setTitle]=useState(''),[body,setBody]=useState(''),[comment,setComment]=useState('');
 const [category,setCategory]=useState(''),[reconcileNumber,setReconcileNumber]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [publicationConfirm,setPublicationConfirm]=useState(false),[commentPage,setCommentPage]=useState(1);
 const connection=useRemote(()=>api.request<Connection>(base+'/connection'),[api],30000,true);
 const local=useRemote(()=>tab==='internal'?api.request<Internal[]>(base+'/internal'):Promise.resolve([]),[api,tab],15000,true);
 const github=useRemote(()=>tab!=='internal'&&connection.value?.github.connected?api.request<Page>(base+'/github/'+tab+'?page='+page+(tab==='discussion'&&cursors[page-1]?'&cursor='+encodeURIComponent(cursors[page-1]!):'')):Promise.resolve(null),[api,tab,page,cursors[page-1],connection.value?.github.connected],15000,true);
 const current=tab==='internal'?local.value?.find(i=>i.id===selected):undefined;
 const detail=useRemote(()=>tab!=='internal'&&selected?api.request<Detail>(base+'/github/'+tab+'/'+selected+'?page='+commentPage):Promise.resolve(null),[api,tab,selected,commentPage],15000,true);
 const categories=useRemote(()=>publicationConfirm&&current?.kind==='discussion'?api.request<Category[]>(base+'/github/categories'):Promise.resolve([]),[api,publicationConfirm,current?.kind]);
 useEffect(()=>{setSelected(null);setPublicationConfirm(false);setComment('');setError('');setPage(1);setCursors([undefined]);setTitle('');setBody('')},[preferenceScope]);
 function switchTab(next:Tab){setTab(next);setSelected(null);setPage(1);setCursors([undefined]);setCommentPage(1);setPublicationConfirm(false);setComment('');setCategory('');setError('')}
 function select(id:string){setSelected(id);setComment('');setCommentPage(1);setPublicationConfirm(false);setCategory('');setReconcileNumber('');setError('')}
 function refresh(){connection.reload();local.reload();github.reload();detail.reload()}
 async function action(path:string,input:unknown){
  setBusy(true);setError('');
  try{await api.request(base+path,{method:'POST',body:JSON.stringify(input)});refresh();return true}
  catch(e){setError(e instanceof Error?e.message:String(e));refresh();return false}
  finally{setBusy(false)}
 }
 async function create(event:FormEvent){event.preventDefault();if(await action('/internal',{kind,title,body})){setTitle('');setBody('');switchTab('internal')}}
 async function change(actionName:string,text?:string){if(!selected)return;const path=tab==='internal'?'/internal/'+selected:'/github/'+tab+'/'+selected;if(await action(path,{action:actionName,body:text}))setComment('')}
 const matches=(t:string,b:string,state:string,assigned:boolean)=> (filter==='all'||filter===status(state,assigned))&&(!search.trim()||(t+' '+b).toLowerCase().includes(search.trim().toLowerCase()));
 const cfg=connection.value;
 const item=detail.value?.item;
 const writable=!!cfg?.github.writable;
 const localEditable=current?.publication==='local';
 const canPublish=current&&(cfg?.owner||current.creatorId===cfg?.actorId);
 return <div className="developer-workspace">
  <Header title="Development workspace" description="Private team tasks, GitHub Issues and Discussions in one workspace."/>
  <div className="dw-toolbar"><nav aria-label="Workspace sections">{([['internal','Internal workspace'],['issue','GitHub Issues'],['discussion','GitHub Discussions']] as const).map(([id,label])=><button key={id} className={'btn '+(tab===id?'':'secondary')} aria-pressed={tab===id} onClick={()=>switchTab(id)}>{label}</button>)}</nav><button className="btn secondary" disabled={busy} onClick={refresh}>Refresh</button></div>
  <p className="dw-connection">{cfg?.github.connected?'GitHub connected':'GitHub not configured'} · {writable?'Write enabled':'Read only / local mode'} · {cfg?.github.githubLogin?'GitHub: '+cfg.github.githubLogin:'GitHub account mapping not configured'}</p>
  <ErrorBanner text={error||connection.error||(tab==='internal'?local.error:github.error||detail.error)}/>
  {tab==='internal'&&<Panel title="New internal task or discussion"><form className="stack dw-create" onSubmit={create}>
   <label>Type<select value={kind} onChange={e=>setKind(e.target.value as Kind)}><option value="issue">Task</option><option value="discussion">Discussion</option></select></label>
   <label>Title<input value={title} maxLength={200} required onChange={e=>setTitle(e.target.value)}/></label>
   <label>Description<textarea value={body} maxLength={19500} onChange={e=>setBody(e.target.value)} rows={4}/></label>
   <p className="footnote">Saved privately in Orchestra. Publishing to GitHub is a separate action.</p><button className="btn" disabled={busy||!title.trim()}>Create</button>
  </form></Panel>}
  <div className="dw-toolbar"><input aria-label="Search displayed items" placeholder="Search current page" value={search} onChange={e=>setSearch(e.target.value)}/><select aria-label="Filter status" value={filter} onChange={e=>setFilter(e.target.value)}><option value="all">All statuses</option>{['Backlog','In progress','Done'].map(s=><option key={s}>{s}</option>)}</select></div>
  <div className="dw-grid">
   <Panel title={tab==='internal'?'Team items':tab==='issue'?'Issues':'Discussions'}>
    <div className="dw-list">
    {tab==='internal'?local.value?.filter(x=>matches(x.title,x.body,x.state,!!x.assignee)).map(x=><button key={x.id} aria-pressed={selected===x.id} onClick={()=>select(x.id)}><span className="dw-status">{x.publication==='local'?status(x.state,!!x.assignee):x.publication}</span><strong>{x.title}</strong><small>{x.kind} · {x.assignee||x.creator} · {date(x.updatedAt)}</small></button>):
     github.value?.items.filter(x=>matches(x.title,x.body,x.state,!!x.assignees.length)).map(x=><button key={x.number} aria-pressed={selected===String(x.number)} onClick={()=>select(String(x.number))}><span className="dw-status">{status(x.state,!!x.assignees.length)}</span><strong>#{x.number} {x.title}</strong><small>{x.assignees.join(', ')||x.author} · {x.commentCount} comments</small><small>{x.labels.join(' · ')||x.category}</small></button>)}
    </div>
    {tab!=='internal'&&<div className="dw-toolbar"><button className="btn secondary" disabled={page===1||busy} onClick={()=>{setPage(p=>p-1);setSelected(null)}}>Previous</button><small>Page {page}</small><button className="btn secondary" disabled={!github.value?.hasMore||busy} onClick={()=>{setCursors(old=>[...old.slice(0,page),github.value?.cursor]);setPage(p=>p+1);setSelected(null)}}>Next</button></div>}
    {tab!=='internal'&&github.value&&<small>Fetched {date(github.value.fetchedAt)}</small>}
   </Panel>
   <Panel title="Details">
    {tab==='internal'&&current?<div className="stack">
      <h2>{current.title}</h2><small>{current.kind} · {current.creator} · {current.publication}</small><p className="dw-body">{current.body}</p>
      {current.gitHubNumber&&<a href={'https://github.com/ZeroFantom/MysticEgo/'+(current.kind==='issue'?'issues':'discussions')+'/'+current.gitHubNumber} target="_blank" rel="noreferrer">Published #{current.gitHubNumber} on GitHub</a>}
      {localEditable&&<div className="dw-toolbar">
       {current.kind==='issue'&&current.state==='open'&&!current.assigneeId&&<button className="btn" disabled={busy} onClick={()=>void change('claim')}>Take task</button>}
       {current.assigneeId&&(current.assigneeId===cfg?.actorId||cfg?.owner)&&<button className="btn secondary" disabled={busy} onClick={()=>void change('release')}>Release</button>}
       {(cfg?.owner||current.creatorId===cfg?.actorId||current.assigneeId===cfg?.actorId)&&<button className="btn secondary" disabled={busy} onClick={()=>void change(current.state==='closed'?'reopen':'close')}>{current.state==='closed'?'Reopen':'Complete'}</button>}
       {canPublish&&current.state==='open'&&<button className="btn secondary" disabled={busy||!writable} onClick={()=>setPublicationConfirm(true)}>Publish to GitHub</button>}
      </div>}
      {publicationConfirm&&localEditable&&<div className="dw-confirm"><strong>Publish the title and description to GitHub?</strong><p>Review for confidential data. Internal comments and assignees stay private. GitHub becomes authoritative after publication.</p>
       {current.kind==='discussion'&&<label>Category<select value={category} onChange={e=>setCategory(e.target.value)}><option value="">Select category</option>{categories.value?.map(c=><option key={c.id} value={c.id}>{c.name}</option>)}</select><ErrorBanner text={categories.error}/></label>}
       <button className="btn" disabled={busy||current.kind==='discussion'&&!category} onClick={async()=>{await action('/internal/'+current.id+'/publish',{confirmed:true,category:category||null});setPublicationConfirm(false)}}>Confirm publication</button> <button className="btn secondary" onClick={()=>setPublicationConfirm(false)}>Cancel</button>
      </div>}
      {current.publication==='pending'&&<div className="dw-confirm"><p>Publication outcome is uncertain. Check GitHub for the item marker before retrying. Automatic retry is disabled.</p>{cfg?.owner&&<><input aria-label="Existing GitHub item number" type="number" min={1} value={reconcileNumber} onChange={e=>setReconcileNumber(e.target.value)}/><button className="btn secondary" disabled={busy||!Number.isInteger(Number(reconcileNumber))||Number(reconcileNumber)<1} onClick={()=>void action('/internal/'+current.id+'/reconcile',{number:Number(reconcileNumber)})}>Verify and link</button></>}</div>}
      <Comments items={current.comments}/>
    </div>:tab!=='internal'&&item&&String(item.number)===selected?<div className="stack">
      <h2>#{item.number} {item.title}</h2><small>{item.author} · {item.state} · {item.assignees.join(', ')}</small><a href={item.url} target="_blank" rel="noreferrer">Open on GitHub</a><p className="dw-body">{item.body}</p>
      <div className="dw-toolbar">{tab==='issue'&&item.state==='open'&&!item.assignees.length&&<button className="btn" disabled={busy||!writable||!cfg?.github.githubLogin} onClick={()=>void change('claim')}>Take task</button>}
      {tab==='issue'&&cfg?.owner&&<button className="btn secondary" disabled={busy||!writable} onClick={()=>void change(item.state==='closed'?'reopen':'close')}>{item.state==='closed'?'Reopen':'Complete'}</button>}</div>
      <Comments items={detail.value?.comments??[]}/>
      {tab==='issue'&&<div className="dw-toolbar"><button className="btn secondary" disabled={commentPage===1} onClick={()=>setCommentPage(p=>p-1)}>Previous comments</button><button className="btn secondary" disabled={!detail.value?.moreComments} onClick={()=>setCommentPage(p=>p+1)}>Next comments</button></div>}
      {tab==='discussion'&&detail.value?.moreComments&&<p>More comments and replies are available on GitHub.</p>}
    </div>:<p>Select an item.</p>}
    {selected&&(tab==='internal'?localEditable:!!item&&writable)&&<form className="stack" onSubmit={e=>{e.preventDefault();void change('comment',comment)}}><label>Comment<textarea value={comment} maxLength={tab==='internal'?2000:10000} required onChange={e=>setComment(e.target.value)}/></label><button className="btn" disabled={busy||!comment.trim()}>Send comment</button></form>}
   </Panel>
  </div>
 </div>;
}
