import {useEffect,useRef,useState,type FormEvent} from 'react';
import {useSearchParams} from 'react-router-dom';
import {ArrowLeft,Check,Copy,Link2,MessageCircle,MoreHorizontal,Search,Send,Smile,Sticker as StickerIcon,Sparkles,UserPlus,X} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {useNoticeFeed} from '../app/notifications';
import {ErrorBanner,Header} from '../components/primitives';
import {ProfileAvatar} from './Profile';
import {StickerImage,StickerPicker} from '../components/Stickers';
import {VoiceMessage,VoiceRecorder} from './ChatVoice';
import './collective-chat.css';

type Person={id:string;nickname:string;displayName:string;avatarUrl?:string;accent?:string;statusText?:string};
type Contact={id:string;status:'pending'|'accepted';incoming:boolean;person:Person;
  last?:{text:string;at:string;stickerId?:string;voice?:boolean;mine:boolean}};
type Message={id:string;sender:string;recipient:string;text:string;at:string;stickerId?:string;replyToId?:string;forwarded?:boolean;editedAt?:string;deletedAt?:string;voiceMime?:string;transcript?:string};
const preview=(item:Message|undefined)=>!item?'Сообщение':item.deletedAt?'Сообщение удалено':
  item.text||(item.voiceMime?'Голосовое сообщение':'Стикер');
const dateLabel=(value:string)=>new Date(value).toLocaleDateString('ru-RU',{day:'numeric',month:'long'});
const timeLabel=(value:string)=>new Date(value).toLocaleTimeString('ru-RU',{hour:'2-digit',minute:'2-digit'});
const dayKey=(value:string)=>new Date(value).toDateString();
const emoji=['😀','😂','🥹','😍','😎','🤝','👍','🙌','💜','✨','🔥','🎮','🎉','👀','🤔','🚀','🫶','✅','🌙','💬'];
function decodeSearch(value:string):string{
  try{const url=new URL(value.trim());return url.searchParams.get('add')??url.hash.slice(1)}
  catch{return value.trim()}
}

export function Collective(){
  const {api,personal,session}=useApp();
  const [params]=useSearchParams();
  const [input,setInput]=useState(params.get('add')??'');
  const [query,setQuery]=useState(params.get('add')??'');
  const [searching,setSearching]=useState(Boolean(params.get('add')));
  const [selected,setSelected]=useState<Person|null>(null);
  const [drafts,setDrafts]=useState<Record<string,string>>({});
  const text=selected?drafts[selected.id]??'':'';
  const setText=(next:string|((current:string)=>string))=>{
    if(!selected)return;
    setDrafts(items=>({...items,[selected.id]:typeof next==='string'?next:next(items[selected.id]??'')}));
  };
  const activePeer=useRef<string|null>(null);
  const nearBottom=useRef(true);
  const [jump,setJump]=useState(false);
  const [emojiOpen,setEmojiOpen]=useState(false),[stickers,setStickers]=useState(false),[suggestion,setSuggestion]=useState(''),[polishing,setPolishing]=useState(false);
  const [reply,setReply]=useState<Message|null>(null);
  const [editing,setEditing]=useState<Message|null>(null);
  const [editText,setEditText]=useState('');
  const [forward,setForward]=useState<Message|null>(null);
  const [target,setTarget]=useState('');
  const [menu,setMenu]=useState('');
  const [transcripts,setTranscripts]=useState<Record<string,string>>({});
  const composed=editing?editText:text;
  const setComposed=editing?setEditText:setText;
  const [older,setOlder]=useState<Message[]>([]);
  const [more,setMore]=useState(true);
  const historyRequest=useRef(0);
  const [loadingHistory,setLoadingHistory]=useState(false);
  const draftSnapshot=useRef({peer:selected?.id,text:composed,editing:editing?.id});
  draftSnapshot.current={peer:selected?.id,text:composed,editing:editing?.id};
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const notify=useNoticeFeed()?.notify;
  const listRef=useRef<HTMLDivElement>(null);
  const composer=useRef<HTMLTextAreaElement>(null);
  const messageActions=useRef<HTMLDivElement>(null);
  const composerTools=useRef<HTMLDivElement>(null);
  useEffect(()=>{
    if(!menu&&!emojiOpen&&!stickers)return;
    const outside=(event:PointerEvent)=>{
      if(!(event.target instanceof Node))return;
      if(menu&&!messageActions.current?.contains(event.target))setMenu('');
      if(!composerTools.current?.contains(event.target)){setEmojiOpen(false);setStickers(false)}
    };
    const escape=(event:KeyboardEvent)=>{
      if(event.key!=='Escape')return;
      if(menu){messageActions.current?.querySelector('button')?.focus();setMenu('')}
      else if(emojiOpen||stickers){const open=composerTools.current?.querySelector<HTMLButtonElement>('button[aria-expanded=true]');setEmojiOpen(false);setStickers(false);open?.focus()}
    };
    document.addEventListener('pointerdown',outside);document.addEventListener('keydown',escape);
    return()=>{document.removeEventListener('pointerdown',outside);document.removeEventListener('keydown',escape)};
  },[menu,emojiOpen,stickers]);
  const contacts=useRemote(()=>api.request<Contact[]>('/collective/contacts'),[api],5000,true);
  const people=useRemote(()=>searching?api.request<Person[]>('/collective/people?q='+encodeURIComponent(query)):Promise.resolve([]),[api,query,searching]);
  const central=useRemote(()=>api.request<{orchestraUrl:string}>('/managed/context'),[api]);
  const messages=useRemote(()=>selected?api.request<Message[]>('/collective/messages/'+selected.id):Promise.resolve([]),[api,selected?.id],3000,true);
  const mine=personal?.game.id;
  const newest=messages.value?.at(-1);
  const lastId=newest&&(newest.sender===selected?.id||newest.recipient===selected?.id)?newest.id:undefined;
  function scrollDown(){
    if(listRef.current)listRef.current.scrollTop=listRef.current.scrollHeight;
    nearBottom.current=true;setJump(false);
  }
  useEffect(()=>{if(lastId){if(nearBottom.current)scrollDown();else setJump(true)}},[lastId]);
  useEffect(()=>{
    const id=params.get('add');
    if(id){setInput(id);setQuery(id);setSearching(true)}
  },[params]);

  function select(person:Person|null){
    if(activePeer.current===(person?.id??null))return;
    activePeer.current=person?.id??null;
    nearBottom.current=true;setJump(false);
    setReply(null);setEditing(null);setForward(null);setMenu('');setTranscripts({});
    historyRequest.current++;setLoadingHistory(false);
    setSelected(person);setOlder([]);setMore(true);setError('');setEmojiOpen(false);setStickers(false);setSuggestion('');
  }
  async function copy(isLink:boolean){
    if(!mine)return;
    try{
      const url=new URL((central.value?.orchestraUrl||location.origin).replace(/\/$/,'')+'/audit-portal/collective');
      url.searchParams.set('add',mine);
      await navigator.clipboard.writeText(isLink?url.toString():mine);notify?.(isLink?'Ссылка скопирована':'ID скопирован','success')
    }catch{setError('Не удалось скопировать. Выделите ID и скопируйте вручную.')}
  }
  async function contact(person:Person){
    setBusy(true);setError('');
    try{await api.request('/collective/contacts',{method:'POST',body:JSON.stringify({id:person.id})});contacts.reload();notify?.('Запрос отправлен','success')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function accept(row:Contact){
    setBusy(true);setError('');
    try{await api.request(`/collective/contacts/${row.id}/accept`,{method:'POST'});contacts.reload();select(row.person)}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  function sent(peer:string){
    if(activePeer.current!==peer)return;
    nearBottom.current=true;setReply(null);setStickers(false);messages.reload();contacts.reload();composer.current?.focus();
  }
  function update(item:Message){
    setOlder(items=>items.map(previous=>previous.id===item.id?item:previous));
    messages.reload();contacts.reload();
  }
  async function send(event:FormEvent){
    event.preventDefault();if(!selected||!composed.trim()||busy)return;
    const peer=selected.id;
    setBusy(true);setError('');
    try{
      if(editing){
        const item=await api.request<Message>('/collective/messages/'+editing.id,{
          method:'PUT',body:JSON.stringify({text:editText.trim()}),
        });
        if(activePeer.current===peer){update(item);setEditing(null);composer.current?.focus()}
      }else{
        await api.request('/collective/messages/'+peer,{method:'POST',body:JSON.stringify({text:text.trim(),replyToId:reply?.id})});
        setDrafts(items=>items[peer]===text?{...items,[peer]:''}:items);
        sent(peer);
      }
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function sendSticker(id:string){
    if(!selected||busy||editing)return;
    setBusy(true);setError('');
    try{
      await api.request('/collective/messages/'+selected.id,{method:'POST',body:JSON.stringify({stickerId:id,replyToId:reply?.id})});
      sent(selected.id);
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function sendVoice(clip:Blob){
    if(!selected||busy||editing)throw Error('Отправка пока недоступна.');
    const peer=selected.id;
    setBusy(true);setError('');
    try{
      const path=session.url.replace(/\/$/,'')+'/api/v1/collective/voice/'+peer+(reply?'?replyTo='+reply.id:'');
      const result=await fetch(path,{method:'POST',signal:AbortSignal.timeout(30000),
        headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/octet-stream'},body:clip});
      if(!result.ok){
        const body=await result.json().catch(()=>({}));
        throw Error(body.error||'Не удалось отправить голосовое сообщение.');
      }
      sent(peer);
    }finally{setBusy(false)}
  }
  async function remove(item:Message){
    if(busy||!window.confirm('Удалить сообщение у обоих участников?'))return;
    const peer=selected?.id;
    setBusy(true);setError('');setMenu('');
    try{
      const deleted=await api.request<Message>('/collective/messages/'+item.id,{method:'DELETE'});
      if(activePeer.current!==peer)return;
      if(editing?.id===item.id)setEditing(null);
      if(reply?.id===item.id)setReply(null);
      update(deleted);
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function sendForward(event:FormEvent){
    event.preventDefault();if(!forward||!target||busy)return;
    setBusy(true);setError('');
    try{
      await api.request('/collective/messages/'+forward.id+'/forward',{method:'POST',body:JSON.stringify({id:target})});
      setForward(null);contacts.reload();notify?.('Сообщение переслано','success');
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  function focusComposer(){requestAnimationFrame(()=>composer.current?.focus())}
  async function polish(){
    if(!composed.trim()||polishing)return;
    const snapshot={peer:selected?.id,text:composed,editing:editing?.id};
    setPolishing(true);setError('');setSuggestion('');
    try{
      const response=await api.request<{text:string}>('/assistant/polish',{method:'POST',body:JSON.stringify({text:snapshot.text.trim()})});
      const current=draftSnapshot.current;
      if(activePeer.current===snapshot.peer&&current.peer===snapshot.peer&&current.text===snapshot.text&&current.editing===snapshot.editing)setSuggestion(response.text);
    }catch(error){setError(String(error))}
    finally{setPolishing(false)}
  }
  async function previous(){
    const first=older[0]??messages.value?.[0];
    if(!selected||!first||loadingHistory)return;
    const request=++historyRequest.current;
    setLoadingHistory(true);
    try{
      const data=await api.request<Message[]>(`/collective/messages/${selected.id}?before=${encodeURIComponent(first.at)}`);
      if(activePeer.current!==selected.id||historyRequest.current!==request)return;
      const scrollHeight=listRef.current?.scrollHeight??0;
      const top=listRef.current?.scrollTop??0;
      setMore(data.length===60);setOlder(items=>[...data,...items]);
      requestAnimationFrame(()=>{if(listRef.current&&activePeer.current===selected.id&&historyRequest.current===request)listRef.current.scrollTop=listRef.current.scrollHeight-scrollHeight+top});
    }catch(error){if(historyRequest.current===request)setError(String(error))}
    finally{if(historyRequest.current===request)setLoadingHistory(false)}
  }
  function insertEmoji(symbol:string){
    const start=composer.current?.selectionStart??composed.length;
    const end=composer.current?.selectionEnd??composed.length;
    setComposed(current=>(current.slice(0,start)+symbol+current.slice(end)).slice(0,2000));
    setEmojiOpen(false);
    requestAnimationFrame(()=>{composer.current?.focus();composer.current?.setSelectionRange(start+symbol.length,start+symbol.length)});
  }
  const thread=selected?[...new Map([...older,...(messages.value??[])]
    .filter(item=>item.sender===selected.id||item.recipient===selected.id)
    .map(item=>[item.id,item])).values()]:[];
  const rows=contacts.value??[];
  const chats=rows.filter(row=>row.status==='accepted')
    .sort((a,b)=>(b.last?Date.parse(b.last.at):0)-(a.last?Date.parse(a.last.at):0));
  const byId=new Map(thread.map(item=>[item.id,item]));
  const currentPerson=chats.find(row=>row.person.id===selected?.id)?.person??selected;
  const otherChats=chats.filter(row=>row.person.id!==selected?.id);
  const requests=rows.filter(row=>row.status==='pending');
  return <>
    <Header title="Коллектив" description="Чаты и контакты MysticEgo" action={mine&&<div className="row-actions">
      <button className="btn secondary" onClick={()=>void copy(false)}><Copy size={15}/> Мой ID</button>
      <button className="btn secondary" onClick={()=>void copy(true)}><Link2 size={16}/> Поделиться</button>
    </div>}/>
    <ErrorBanner text={error||contacts.error||people.error}/>
    <div className="collective-layout mystic-chats" data-chat-open={!!selected}>
      <aside className="chat-sidebar" aria-label="Чаты и контакты">
        <div className="chat-sidebar-top">
          <h2>Чаты</h2>
          <form className="chat-search-row" onSubmit={event=>{event.preventDefault();const next=decodeSearch(input);setQuery(next);setSearching(!!next);people.reload()}}>
            <div className="mystic-search"><Search size={18} aria-hidden="true"/>
              <input type="search" aria-label="Никнейм, ID или ссылка" value={input} onChange={event=>{setInput(event.target.value);if(!event.target.value)setSearching(false)}}
                placeholder="Никнейм, ID или ссылка"/>
              {input&&<button type="button" aria-label="Очистить поиск" onClick={()=>{setInput('');setQuery('');setSearching(false)}}><X size={17}/></button>}
            </div>
            <button className="btn secondary" aria-label="Найти игрока" title="Найти игрока" disabled={!input.trim()}><Search size={17}/></button>
          </form>
        </div>
        <div className="chat-threads">
          {searching?<section className="chat-results" aria-label="Результаты поиска">
            {people.loading&&<p className="footnote">Ищем игроков…</p>}
            {people.value?.map(person=>{
              const existing=rows.find(x=>x.person.id===person.id);
              return <div className="chat-thread" key={person.id}><ProfileAvatar name={person.nickname} url={person.avatarUrl}/>
                <div className="chat-thread-info"><strong>{person.nickname}</strong><small>{person.displayName}</small></div>
                {existing?.status==='accepted'?<button className="icon-btn" aria-label={'Чат '+person.nickname} onClick={()=>select(person)}><MessageCircle size={17}/></button>:
                  <button className="icon-btn" disabled={busy||Boolean(existing)} onClick={()=>void contact(person)} aria-label={'Добавить '+person.nickname}>
                    {existing?<Check size={17}/>:<UserPlus size={17}/>}</button>}
              </div>;
            })}
            {people.value?.length===0&&<p className="footnote">Игроки не найдены.</p>}
          </section>:<>
            {!chats.length&&!contacts.loading&&<p className="chat-sidebar-empty">Найди игрока по никнейму, ID или ссылке и отправь запрос.</p>}
            {chats.map(row=><button type="button" className="chat-thread" aria-current={selected?.id===row.person.id?'true':undefined} key={row.id} onClick={()=>select(row.person)}>
              <ProfileAvatar name={row.person.nickname} url={row.person.avatarUrl}/>
              <span className="chat-thread-info">
                <strong>{row.person.nickname}</strong>
                <small>{row.last?(row.last.mine?'Вы: ':'')+(row.last.text||(row.last.voice?'Голосовое сообщение':'Стикер')):'Начать переписку'}</small>
              </span>
              {row.last&&<time dateTime={row.last.at}>{dayKey(row.last.at)===dayKey(new Date().toISOString())?timeLabel(row.last.at):dateLabel(row.last.at)}</time>}
            </button>)}
            {requests.length>0&&<details className="chat-requests" open={requests.some(row=>row.incoming)||undefined}>
              <summary>Запросы в контакты · {requests.length}</summary>
              {requests.map(row=><div className="chat-thread" key={row.id}>
                <ProfileAvatar name={row.person.nickname} url={row.person.avatarUrl}/>
                <div className="chat-thread-info"><strong>{row.person.nickname}</strong><small>{row.incoming?'Входящий запрос':'Запрос отправлен'}</small></div>
                {row.incoming&&<button className="icon-btn" disabled={busy} onClick={()=>void accept(row)} aria-label={'Принять запрос '+row.person.nickname}><Check size={18}/></button>}
              </div>)}
            </details>}
          </>}
        </div>
      </aside>
      <section className="panel messenger chat-room" aria-label={selected?'Чат '+selected.nickname:'Сообщения'}>
        <header className="messenger-header">{selected?<><button type="button" className="icon-btn chat-back" onClick={()=>select(null)} aria-label="К списку чатов"><ArrowLeft size={20}/></button>
          <ProfileAvatar name={currentPerson?.nickname??selected.nickname} url={currentPerson?.avatarUrl??selected.avatarUrl}/><div className="chat-heading"><h2>{currentPerson?.nickname??selected.nickname}</h2>{currentPerson?.statusText&&<p className="social-status">{currentPerson.statusText}</p>}</div></>:<><MessageCircle size={19}/><h2>Сообщения</h2></>}</header>
        <div className="message-list" ref={listRef} onScroll={event=>{const area=event.currentTarget;nearBottom.current=area.scrollHeight-area.scrollTop-area.clientHeight<64;if(nearBottom.current)setJump(false)}}>
          {selected&&messages.error&&<ErrorBanner text={messages.error}/>}
          {!selected&&<div className="empty chat-idle"><MessageCircle size={34}/><p>Выбери чат, чтобы открыть переписку.</p></div>}
          {selected&&more&&(older.length>0||messages.value?.length===60)&&<button className="btn secondary tiny" disabled={loadingHistory} onClick={()=>void previous()}>{loadingHistory?'Загрузка…':'Предыдущие сообщения'}</button>}
          {selected&&!thread.length&&!messages.loading&&!messages.error&&<div className="empty chat-idle"><p>Отправь первое сообщение.</p></div>}
          {selected&&thread.map((item,index,items)=>{
            const own=item.sender===mine;
            const original=item.replyToId?byId.get(item.replyToId):undefined;
            return <div key={item.id} id={'chat-message-'+item.id} className="chat-message-row">
              {(index===0||dayKey(items[index-1].at)!==dayKey(item.at))&&<div className="chat-day">{dateLabel(item.at)}</div>}
              <div className={'chat-message '+(own?'mine':'')} data-deleted={Boolean(item.deletedAt)}>
                {item.deletedAt?<p className="chat-message-meta">Сообщение удалено</p>:<>
                  {item.forwarded&&<small className="chat-message-meta">Переслано</small>}
                  {item.replyToId&&(original?<button type="button" className="chat-quote" onClick={()=>
                    document.getElementById('chat-message-'+item.replyToId)?.scrollIntoView({block:'nearest'})}>{preview(original)}</button>:
                    <small className="chat-quote">Ответ на сообщение</small>)}
                  {item.stickerId&&<StickerImage id={item.stickerId}/>}
                  {item.text&&<p>{item.text}</p>}
                  {item.voiceMime&&<VoiceMessage id={item.id} transcript={item.transcript??transcripts[item.id]}
                    onTranscript={value=>setTranscripts(current=>({...current,[item.id]:value}))} onError={setError}/>}
                </>}
                <div className="chat-message-footer">
                  {!item.deletedAt&&item.editedAt&&<small>изменено</small>}
                  <time dateTime={item.at} title={new Date(item.at).toLocaleString('ru-RU')}>{timeLabel(item.at)}</time>
                  {!item.deletedAt&&<div className="chat-actions" ref={menu===item.id?messageActions:undefined}>
                    <button type="button" className="icon-btn" aria-label="Действия с сообщением" aria-expanded={menu===item.id}
                      onClick={()=>{setMenu(menu===item.id?'':item.id);setEmojiOpen(false);setStickers(false)}}><MoreHorizontal size={17}/></button>
                    {menu===item.id&&<div className="chat-action-menu" aria-label="Действия с сообщением">
                      <button type="button" disabled={busy} onClick={()=>{setEditing(null);setReply(item);setMenu('');setSuggestion('');focusComposer()}}>Ответить</button>
                      {own&&item.text&&!item.forwarded&&!item.stickerId&&!item.voiceMime&&<button type="button" disabled={busy} onClick={()=>{
                        setEditing(item);setEditText(item.text);setReply(null);setMenu('');setSuggestion('');setStickers(false);focusComposer();
                      }}>Редактировать</button>}
                      {otherChats.length>0&&<button type="button" disabled={busy} onClick={()=>{
                        setForward(item);setTarget(otherChats[0].person.id);setMenu('');
                      }}>Переслать</button>}
                      {own&&<button type="button" className="chat-delete-action" disabled={busy} onClick={()=>void remove(item)}>Удалить</button>}
                    </div>}
                  </div>}
                </div>
              </div>
            </div>;
          })}
        </div>
        {selected&&jump&&<button type="button" className="btn secondary chat-jump" onClick={scrollDown}>К новым сообщениям ↓</button>}
        {selected&&suggestion&&<div className="chat-suggestion"><p>{suggestion}</p><button className="btn secondary" onClick={()=>{setComposed(suggestion);setSuggestion('')}}>Вставить</button>
          <button className="icon-btn" onClick={()=>setSuggestion('')} aria-label="Отклонить правку">×</button></div>}
        {selected&&(reply||editing)&&<div className="chat-suggestion chat-compose-context">
          <div><strong>{editing?'Редактирование':'Ответ'}</strong><p>{preview(editing??reply??undefined)}</p></div>
          <button type="button" className="icon-btn" aria-label="Отменить" onClick={()=>{setEditing(null);setReply(null);setSuggestion('')}}><X size={17}/></button>
        </div>}
        {selected&&forward&&<form className="chat-suggestion chat-forward" onSubmit={sendForward}>
          <label>Переслать в чат<select value={target} onChange={event=>setTarget(event.target.value)}>
            {otherChats.map(row=><option key={row.id} value={row.person.id}>{row.person.nickname}</option>)}
          </select></label>
          <button className="btn secondary" disabled={busy||!target}>Переслать</button>
          <button type="button" className="icon-btn" aria-label="Отменить пересылку" onClick={()=>setForward(null)}><X size={17}/></button>
        </form>}
        {selected&&<form className="chat-compose" data-editing={Boolean(editing)} onSubmit={send}>
          <div className="chat-compose-tools" ref={composerTools}>
          <div className="emoji-control"><button type="button" className="icon-btn" aria-label="Эмодзи" aria-expanded={emojiOpen} onClick={()=>{setStickers(false);setMenu('');setEmojiOpen(value=>!value)}}><Smile size={22}/></button>
            {emojiOpen&&<div className="emoji-menu">{emoji.map(symbol=><button type="button" key={symbol} aria-label={'Вставить '+symbol} onClick={()=>insertEmoji(symbol)}>{symbol}</button>)}</div>}</div>
          <div className="emoji-control"><button type="button" className="icon-btn" aria-label="Стикеры" disabled={Boolean(editing)} aria-expanded={stickers} onClick={()=>{setEmojiOpen(false);setMenu('');setStickers(value=>!value)}}><StickerIcon size={22}/></button>
           {stickers&&<StickerPicker send={id=>void sendSticker(id)}/>}</div>
          <button type="button" className="icon-btn" aria-label="Улучшить черновик" disabled={polishing||!composed.trim()} onClick={()=>void polish()}><Sparkles size={17}/></button>
          {!editing&&<VoiceRecorder key={selected.id} disabled={busy} send={sendVoice} onError={setError}/>}
          </div>
          <textarea ref={composer} rows={2} maxLength={2000} aria-label="Сообщение" placeholder="Написать сообщение" value={composed}
            onChange={event=>setComposed(event.target.value)} onKeyDown={event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing){event.preventDefault();event.currentTarget.form?.requestSubmit()}}}/>
          <button className="btn" disabled={busy||!composed.trim()} aria-label={editing?"Сохранить изменения":"Отправить"}>{editing?<Check size={18}/>:<Send size={18}/>}</button>
        </form>}
      </section>
    </div>
  </>;
}
