import {useMemo,useState,type FormEvent} from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import {BookOpen,Pencil,Search,Sparkles} from 'lucide-react';
import type {Capability} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Header,Panel} from '../components/primitives';

type Article={slug:string;title:string;category:string;markdown:string;updatedAt:string};
type ApiCapability={id:string;description:string;path?:string;scope:string;requiresConfirmation:boolean};
type FaqData={articles:Article[];canEdit:boolean;commands:Capability[]};
export function Faq(){
  const {api}=useApp();
  const data=useRemote(()=>api.request<FaqData>('/faq'),[api]);
  const methods=useRemote(()=>api.request<ApiCapability[]>('/ecosystem/capabilities'),[api],30000);
  const [query,setQuery]=useState('');
  const [selected,setSelected]=useState('start');
  const [editing,setEditing]=useState<Article|null>(null);
  const [error,setError]=useState('');
  const [saving,setSaving]=useState(false);
  const terms=query.trim().toLowerCase();
  const matches=(text:string)=>text.toLowerCase().includes(terms);
  const visible=useMemo(()=>data.value?.articles.filter(x=>[x.title,x.category,x.markdown].some(value=>value.toLowerCase().includes(terms)))??[],[data.value,terms]);
  const article=visible.find(x=>x.slug===selected)??visible[0];
  async function save(event:FormEvent){
    event.preventDefault();if(!editing)return;setSaving(true);setError('');
    try{await api.request('/faq/'+editing.slug,{method:'PUT',body:JSON.stringify(editing)});setEditing(null);data.reload()}
    catch(error){setError(String(error))}
    finally{setSaving(false)}
  }
  return <>
    <Header title="FAQ" description="Справка по экосистеме, игре и доступным методам"/>
    <ErrorBanner text={error||data.error}/>
    <label className="search faq-search"><Search size={18}/><input value={query} onChange={event=>setQuery(event.target.value)} placeholder="Поиск по статьям и командам"/></label>
    <div className="faq-layout">
      <Panel title="Содержание">
        {visible.map(item=><button key={item.slug} className={'faq-choice '+(selected!=='catalog'&&article?.slug===item.slug?'selected':'')}
          onClick={()=>{setSelected(item.slug);setEditing(null)}}><BookOpen size={17}/><span><strong>{item.title}</strong><small>{item.category}</small></span></button>)}
        <button className={'faq-choice '+(selected==='catalog'?'selected':'')} onClick={()=>{setSelected('catalog');setEditing(null)}}><Sparkles size={17}/><strong>Команды и методы</strong></button>
      </Panel>
      {selected==='catalog'?<Panel title="Команды и методы">
        <div className="faq-catalog">{data.value?.commands.filter(command=>matches([command.id,command.displayName,command.description].join(' '))).map(command=>
          <details key={command.id}><summary>{command.displayName} <code>{command.id}</code></summary><p>{command.description}</p>
            {command.parameters.map(parameter=><div key={parameter.name} className="line"><strong>{parameter.name}</strong>
              <span>{parameter.description} {parameter.values?.length?'· '+parameter.values.join(', '):''}</span></div>)}
          </details>)}</div>
        <h3>Методы текущего сервиса</h3><ErrorBanner text={methods.error}/>
        <div className="faq-catalog">{methods.value?.filter(method=>matches(method.id+' '+method.description)).map((method,index)=>
          <details key={method.id+index}><summary>{method.description}</summary><p><code>{method.path||method.id}</code></p>
            {method.requiresConfirmation&&<p className="footnote">Действие требует подтверждения.</p>}</details>)}</div>
      </Panel>:article?<Panel title={article.title} aside={data.value?.canEdit&&<button className="btn secondary" onClick={()=>setEditing(article)}><Pencil size={15}/> Редактировать</button>}>
        {editing?<form className="stack" onSubmit={save}>
          <label>Заголовок<input required value={editing.title} maxLength={100} onChange={event=>setEditing({...editing,title:event.target.value})}/></label>
          <label>Текст Markdown<textarea required rows={18} maxLength={50000} value={editing.markdown} onChange={event=>setEditing({...editing,markdown:event.target.value})}/></label>
          <div className="row-actions"><button className="btn" disabled={saving}>Сохранить</button><button className="btn secondary" type="button" onClick={()=>setEditing(null)}>Отмена</button></div>
        </form>:<article className="markdown"><ReactMarkdown remarkPlugins={[remarkGfm]} skipHtml>{article.markdown}</ReactMarkdown></article>}
      </Panel>:<Panel title="Справка"><p className="footnote">{data.loading?'Загружаем справку…':'По вашему запросу статьи не найдены.'}</p></Panel>}
    </div>
  </>;
}
