import {useState,type FormEvent} from 'react';
import {Bot,Sparkles} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';

import {AssistantChat,type AssistantStatus,type Character} from '../avatar/AssistantChat';

export function AssistantPage(){
  const {api,session,mode,portal}=useApp();
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
  return <>
    <Header title="Ассистент" description="Диалог и настройки Character V2"/>
    <ErrorBanner text={status.error||error}/>
    <div className="two-col assistant-layout">
      <Panel title="Чат" aside={<Bot size={19}/>}><AssistantChat status={status.value}/></Panel>
      <Panel title="Character V2" aside={<Sparkles size={18}/>}>
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
    </div>
    {!portal&&<Panel title="MCP и браузер">
      <div className="stack"><code>{session.url.replace(/\/$/,'')}/mcp</code>
        <p className="footnote">Подключение через HTTPS или локальный API. Используйте токен текущей учётной записи. tools/list показывает доступные операции.</p>
        {['agent','audit'].includes(mode)&&<div className="row-actions">
          <a className="btn secondary" target="_blank" rel="noreferrer" href="https://one.one.one.one/">Cloudflare WARP</a>
          <a className="btn secondary" target="_blank" rel="noreferrer" href="https://github.com/bol-van/zapret2">zapret</a>
        </div>}
      </div>
    </Panel>}
  </>;
}
