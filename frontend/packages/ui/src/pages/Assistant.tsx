import {useState,type FormEvent} from 'react';
import {ArrowUpRight,Bot,Braces,MessageSquare,Send,Server,Sparkles} from 'lucide-react';
import {Link,useSearchParams} from 'react-router-dom';
import {useApp,useRemote} from '../app/context';
import {Panel,ErrorBanner} from '../components/primitives';
import {ModelWorkers} from './ModelWorkers';
import './assistant-workspace.css';

import {AssistantChat,type AssistantStatus,type Character} from '../avatar/AssistantChat';

const actions:Record<string,{path:string;label:string}>={
  overview:{path:'/',label:'Обзор'},nodes:{path:'/nodes',label:'Узлы сети'},
  deployment:{path:'/setup',label:'Развёртывание'},branches:{path:'/project-branches',label:'Ветки GitHub'},
  team:{path:'/development-team',label:'Команда разработки'},models:{path:'/assistant?tab=models',label:'Модели'},
};
type DeveloperReply={text:string;view?:string;toolResult?:string};

function DeveloperChat(){
  const {api}=useApp();
  const [prompt,setPrompt]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [turns,setTurns]=useState<{question:string;reply:DeveloperReply}[]>([]);
  async function send(event:FormEvent){
    event.preventDefault();
    const question=prompt.trim();
    if(!question||busy)return;
    setBusy(true);setError('');
    try{
      const reply=await api.request<DeveloperReply>('/managed/panel-assistant',{method:'POST',
        body:JSON.stringify({text:question}),signal:AbortSignal.timeout(150000)});
      setTurns(items=>[...items,{question,reply}]);setPrompt(current=>current===prompt?'':current);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  return <section className="panel assistant-dev-chat">
    <div className="assistant-dev-output" aria-live="polite">
      {!turns.length&&<p className="footnote">Спроси о релизах, ветках GitHub или навигации по Orchestra.</p>}
      {turns.map(({question,reply},index)=>{
        const action=reply.view&&Object.hasOwn(actions,reply.view)?actions[reply.view]:null;
        return <div className="assistant-dev-turn" key={index}>
          <p className="assistant-dev-question">{question}</p>
          <p className="assistant-dev-answer">{reply.text}</p>
          {reply.toolResult&&<details><summary>Данные панели</summary><pre className="output">{reply.toolResult}</pre></details>}
          {action&&<Link className="btn secondary" to={action.path}>{action.label}<ArrowUpRight size={16}/></Link>}
        </div>;
      })}
    </div>
    <ErrorBanner text={error}/>
    <form onSubmit={send} className="assistant-dev-compose">
      <textarea aria-label="Запрос разработчика" rows={2} maxLength={3000} value={prompt} onChange={e=>setPrompt(e.target.value)} placeholder="Спросить об Orchestra"/>
      <button className="btn" disabled={busy||!prompt.trim()}><Send size={16}/>{busy?'Отвечает…':'Отправить'}</button>
    </form>
  </section>;
}

export function AssistantPage(){
  const {api,session,mode,portal}=useApp();
  const [params,setParams]=useSearchParams();
  const access=useRemote(()=>mode==='orchestra'?api.request<{developer:boolean;owner:boolean}>('/managed/panel-assistant/access'):Promise.resolve(null),[api,mode]);
  const tabs=[
    {id:'chat',label:'Чат',icon:MessageSquare},
    ...(access.value?.developer?[{id:'developer',label:'Разработка',icon:Braces}]:[]),
    ...(access.value?.owner?[{id:'models',label:'Модели',icon:Server}]:[]),
    {id:'persona',label:'Характер',icon:Sparkles},
  ];
  const requested=params.get('tab');
  const active=tabs.some(tab=>tab.id===requested)?requested:'chat';
  const status=useRemote(()=>api.request<AssistantStatus>('/assistant/workbench'),[api],15000);
  const [description,setDescription]=useState('');
  const [draft,setDraft]=useState<Character|null>(null);
  const [error,setError]=useState(''),[busy,setBusy]=useState(false),[message,setMessage]=useState('');
  const character=draft??status.value?.character;
  function change<K extends keyof Character>(key:K,value:Character[K]){
    if(character)setDraft({...character,[key]:value});
  }
  async function generate(){
    setBusy(true);setError('');setMessage('');
    try{const next=await api.request<Character>('/assistant/persona/generate',{method:'POST',body:JSON.stringify({text:description})});setDraft(next);setMessage('Черновик готов. Проверьте настройки и сохраните.')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function save(event:FormEvent){
    event.preventDefault();if(!character)return;setBusy(true);setError('');
    try{await api.request('/assistant/persona',{method:'PUT',body:JSON.stringify(character)});setDraft(null);status.reload();setMessage('Character V2 сохранён.')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  return <div className="assistant-workspace">
    <header className="assistant-workspace-header">
      <div className="assistant-workspace-icon"><Bot size={25} aria-hidden="true"/></div>
      <div><h1>Ассистент</h1><p>{mode==='orchestra'?'Диалоги, помощь с Orchestra и локальные модели':'Диалог и персональный характер'}</p></div>
      <span className="assistant-workspace-status" data-ready={!!status.value?.service.ready}>
        {status.value?.service.ready?status.value.service.message:status.loading?'Проверяем подключение…':'Модель недоступна'}
      </span>
    </header>
    <nav className="assistant-workspace-nav" aria-label="Разделы ассистента">
      {tabs.map(({id,label,icon:Icon})=><button key={id} className="assistant-workspace-tab" type="button"
        aria-pressed={active===id} onClick={()=>setParams(id==='chat'?{}:{tab:id})}><Icon size={17} aria-hidden="true"/>{label}</button>)}
    </nav>
    <ErrorBanner text={error||status.error||(mode==='orchestra'?access.error:'')}/>
    {mode==='orchestra'&&status.value&&!status.value.service.ready&&<p className="assistant-workspace-notice">
      После первого развёртывания LM Studio загружает модель в фоне. На VPS проверьте журнал службы <code>mysticego-default-model</code>.
    </p>}
    {active==='chat'&&<Panel title="Личный чат"><AssistantChat status={status.value}/></Panel>}
    {access.value?.developer&&<div hidden={active!=='developer'}><DeveloperChat/></div>}
    {active==='models'&&access.value?.owner&&<ModelWorkers/>}
    {active==='persona'&&<div className="assistant-persona">
      <Panel title="Характер ассистента" aside={<Sparkles size={18}/>}>
        <div className="stack">
          <label>Образ ассистента<textarea value={description} onChange={event=>setDescription(event.target.value)} rows={2} maxLength={1200}
            placeholder="Например, спокойный помощник, который кратко объясняет настройки."/></label>
          <button className="btn secondary" disabled={busy||!description.trim()} onClick={()=>void generate()}>Подготовить черновик моделью</button>
          {message&&<span className="success" role="status">{message}</span>}
        </div>
        {character&&<form className="stack character-form" onSubmit={save}>
          <label>Имя<input required value={character.name} maxLength={48} onChange={event=>change('name',event.target.value)}/></label>
          <div className="form-pair"><label>Тон<select value={character.tone} onChange={event=>change('tone',event.target.value)}>
            <option value="friendly">Дружелюбный</option><option value="formal">Деловой</option><option value="neutral">Нейтральный</option></select></label>
          <label>Ответы<select value={character.detail} onChange={event=>change('detail',event.target.value)}>
            <option value="brief">Краткие</option><option value="balanced">Средние</option><option value="detailed">Подробные</option></select></label></div>
          <label>Интересы<input maxLength={300} value={character.interests} onChange={event=>change('interests',event.target.value)}/></label>
          <label>Инициативность · {character.initiative}<input type="range" min={1} max={5} value={character.initiative} onChange={event=>change('initiative',Number(event.target.value))}/></label>
          <div className="form-pair"><label>Характер<select value={character.mood} onChange={event=>change('mood',event.target.value)}>
            <option value="calm">Спокойный</option><option value="lively">Живой</option><option value="focused">Собранный</option></select></label>
          <label>Музыка<select value={character.music} onChange={event=>change('music',event.target.value)}>
            <option value="auto">По ритму</option><option value="dance">Танцевать</option><option value="listen">Слушать</option></select></label></div>
          <label className="check-row"><input type="checkbox" checked={character.voice} onChange={event=>change('voice',event.target.checked)}/> Озвучивать ответы</label>
          <label className="check-row"><input type="checkbox" checked={character.moveDesktop} onChange={event=>change('moveDesktop',event.target.checked)}/> Движение по рабочему столу</label>
          <button className="btn" disabled={busy}>Сохранить Character V2</button>
        </form>}
      </Panel>
    {!portal&&<Panel title="MCP и браузер">
      <div className="stack"><code>{session.url.replace(/\/$/,'')}/mcp</code>
        <p className="footnote">Подключение через HTTPS или локальный API. Используйте токен текущей учётной записи. tools/list показывает доступные операции.</p>
        {['agent','audit'].includes(mode)&&<div className="row-actions">
          <a className="btn secondary" target="_blank" rel="noreferrer" href="https://one.one.one.one/">Cloudflare WARP</a>
          <a className="btn secondary" target="_blank" rel="noreferrer" href="https://github.com/bol-van/zapret2">zapret</a>
        </div>}
      </div>
    </Panel>}
    </div>}
  </div>;
}
