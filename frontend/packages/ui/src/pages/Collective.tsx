import {useEffect,useRef,useState,type FormEvent} from 'react';
import {useSearchParams} from 'react-router-dom';
import {ArrowLeft,Check,Copy,Link2,MessageCircle,Search,Send,Smile,Sticker as StickerIcon,Sparkles,UserPlus,X} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Header} from '../components/primitives';
import {ProfileAvatar} from './Profile';
import {StickerImage,StickerPicker} from '../components/Stickers';
import './collective-chat.css';

type Person={id:string;nickname:string;displayName:string;avatarUrl?:string;accent?:string};
type Contact={id:string;status:'pending'|'accepted';incoming:boolean;person:Person;
  last?:{text:string;at:string;stickerId?:string;mine:boolean}};
type Message={id:string;sender:string;recipient:string;text:string;at:string;stickerId?:string};
const dateLabel=(value:string)=>new Date(value).toLocaleDateString('ru-RU',{day:'numeric',month:'long'});
const timeLabel=(value:string)=>new Date(value).toLocaleTimeString('ru-RU',{hour:'2-digit',minute:'2-digit'});
const dayKey=(value:string)=>new Date(value).toDateString();
const emoji=['😀','😂','🥹','😍','😎','🤝','👍','🙌','💜','✨','🔥','🎮','🎉','👀','🤔','🚀','🫶','✅','🌙','💬'];
function decodeSearch(value:string):string{
  try{const url=new URL(value.trim());return url.searchParams.get('add')??url.hash.slice(1)}
  catch{return value.trim()}
}

export function Collective(){
  const {api,personal}=useApp();
  const [params]=useSearchParams();
  const [input,setInput]=useState(params.get('add')??'');
  const [query,setQuery]=useState(params.get('add')??'');
  const [searching,setSearching]=useState(Boolean(params.get('add')));
  const [selected,setSelected]=useState<Person|null>(null);
  const [drafts,setDrafts]=useState<Record<string,string>>({});
  const text=selected?drafts[selected.id]??'':'';
  const setText=(next:string|((current:string)=>string))=>{
    if(!selected)return;
    setDrafts(items=>({...items,[selected.id]:typeof next==='string'?next(items[selected.id]??''):next}));
  };
  const activePeer=useRef<string|null>(null);
  const nearBottom=useRef(true);
  const [jump,setJump]=useState(false);
  const [emojiOpen,setEmojiOpen]=useState(false),[stickers,setStickers]=useState(false),[suggestion,setSuggestion]=useState(''),[polishing,setPolishing]=useState(false);
  const [older,setOlder]=useState<Message[]>([]);
  const [more,setMore]=useState(true);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [notice,setNotice]=useState('');
  const listRef=useRef<HTMLDivElement>(null);
  const composer=useRef<HTMLTextAreaElement>(null);
  const contacts=useRemote(()=>api.request<Contact[]>('/collective/contacts'),[api],5000);
  const people=useRemote(()=>searching?api.request<Person[]>('/collective/people?q='+encodeURIComponent(query)):Promise.resolve([]),[api,query,searching]);
  const central=useRemote(()=>api.request<{orchestraUrl:string}>('/managed/context'),[api]);
  const messages=useRemote(()=>selected?api.request<Message[]>('/collective/messages/'+selected.id):Promise.resolve([]),[api,selected?.id],3000);
  const mine=personal?.game.id;
  const lastId=messages.value?.at(-1)?.id;
  function scrollDown(){
    if(listRef.current)listRef.current.scrollTop=listRef.current.scrollHeight;
    nearBottom.current=true;setJump(false);
  }
  useEffect(()=>{if(lastId){if(nearBottom.current)scrollDown();else setJump(true)}},[lastId,selected?.id]);
  useEffect(()=>{if(!notice)return;const timer=setTimeout(()=>setNotice(''),3500);return()=>clearTimeout(timer)},[notice]);
  useEffect(()=>{
    const id=params.get('add');
    if(id){setInput(id);setQuery(id);setSearching(true)}
  },[params]);

  function select(person:Person|null){
    if(activePeer.current===(person?.id??null))return;
    activePeer.current=person?.id??null;
    nearBottom.current=true;setJump(false);
    setSelected(person);setOlder([]);setMore(true);setError('');setEmojiOpen(false);setStickers(false);setSuggestion('');
  }
  async function copy(isLink:boolean){
    if(!mine)return;
    try{
      const url=new URL((central.value?.orchestraUrl||location.origin).replace(/\/$/,'')+'/audit-portal/collective');
      url.searchParams.set('add',mine);
      await navigator.clipboard.writeText(isLink?url.toString():mine);setNotice(isLink?'Ссылка скопирована':'ID скопирован')
    }catch{setError('Не удалось скопировать. Выделите ID и скопируйте вручную.')}
  }
  async function contact(person:Person){
    setBusy(true);setNotice('');setError('');
    try{await api.request('/collective/contacts',{method:'POST',body:JSON.stringify({id:person.id})});contacts.reload();setNotice('Запрос отправлен')}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function accept(row:Contact){
    setBusy(true);setNotice('');setError('');
    try{await api.request(`/collective/contacts/${row.id}/accept`,{method:'POST'});contacts.reload();select(row.person)}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function send(event:FormEvent){
    event.preventDefault();if(!selected||!text.trim()||busy)return;
    setBusy(true);setError('');
    try{
      const peer=selected.id;
      await api.request('/collective/messages/'+peer,{method:'POST',body:JSON.stringify({text:text.trim()})});
      setDrafts(items=>items[peer]===text?{...items,[peer]:''}:items);
      if(activePeer.current===peer){nearBottom.current=true;messages.reload();composer.current?.focus()}
    }
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function sendSticker(id:string){
    if(!selected||busy)return;
    setBusy(true);setError('');
    try{
      await api.request('/collective/messages/'+selected.id,{method:'POST',body:JSON.stringify({stickerId:id})});
      if(activePeer.current===selected.id){nearBottom.current=true;messages.reload();setStickers(false)}
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function polish(){
    if(!text.trim()||polishing)return;
    setPolishing(true);setError('');setSuggestion('');
    try{
      const reply=await api.request<{text:string}>('/assistant/polish',{method:'POST',body:JSON.stringify({text:text.trim()})});
      if(activePeer.current===selected?.id)setSuggestion(reply.text);
    }catch(error){setError(String(error))}
    finally{setPolishing(false)}
  }
  async function previous(){
    const first=older[0]??messages.value?.[0];
    if(!selected||!first)return;
    try{
      const data=await api.request<Message[]>(`/collective/messages/${selected.id}?before=${encodeURIComponent(first.at)}`);
      if(activePeer.current!==selected.id)return;
      const scrollHeight=listRef.current?.scrollHeight??0;
      const top=listRef.current?.scrollTop??0;
      setMore(data.length===60);setOlder(items=>[...data,...items]);
      requestAnimationFrame(()=>{if(listRef.current)listRef.current.scrollTop=listRef.current.scrollHeight-scrollHeight+top});
    }catch(error){setError(String(error))}
  }
  function insertEmoji(symbol:string){
    const start=composer.current?.selectionStart??text.length;
    const end=composer.current?.selectionEnd??text.length;
    setText(current=>(current.slice(0,start)+symbol+current.slice(end)).slice(0,2000));
    setEmojiOpen(false);
    requestAnimationFrame(()=>{composer.current?.focus();composer.current?.setSelectionRange(start+symbol.length,start+symbol.length)});
  }
  const thread=[...new Map([...older,...(messages.value??[])].map(item=>[item.id,item])).values()];
  const rows=contacts.value??[];
  const chats=rows.filter(row=>row.status==='accepted')
    .sort((a,b)=>(b.last?Date.parse(b.last.at):0)-(a.last?Date.parse(a.last.at):0));
  const requests=rows.filter(row=>row.status==='pending');
  return <>
    <Header title="Коллектив" description="Чаты и контакты MysticEgo" action={mine&&<div className="row-actions">
      <button className="btn secondary" onClick={()=>void copy(false)}><Copy size={15}/> Мой ID</button>
      <button className="btn secondary" onClick={()=>void copy(true)}><Link2 size={16}/> Поделиться</button>
    </div>}/>
    <ErrorBanner text={error||contacts.error||people.error}/>
    {notice&&<p className="success collective-notice" role="status">{notice}</p>}
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
                <small>{row.last?(row.last.mine?'Вы: ':'')+(row.last.text||'Стикер'):'Начать переписку'}</small>
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
          <ProfileAvatar name={selected.nickname} url={selected.avatarUrl}/><h2>{selected.nickname}</h2></>:<><MessageCircle size={19}/><h2>Сообщения</h2></>}</header>
        <div className="message-list" ref={listRef} onScroll={event=>{const area=event.currentTarget;nearBottom.current=area.scrollHeight-area.scrollTop-area.clientHeight<64;if(nearBottom.current)setJump(false)}}>
          {selected&&messages.error&&<ErrorBanner text={messages.error}/>}
          {!selected&&<div className="empty chat-idle"><MessageCircle size={34}/><p>Выбери чат, чтобы открыть переписку.</p></div>}
          {selected&&more&&(older.length>0||messages.value?.length===60)&&<button className="btn secondary tiny" onClick={()=>void previous()}>Предыдущие сообщения</button>}
          {selected&&!thread.length&&!messages.loading&&!messages.error&&<div className="empty chat-idle"><p>Отправь первое сообщение.</p></div>}
          {selected&&thread.filter(item=>item.sender===selected.id||item.recipient===selected.id).map((item,index,items)=><div key={item.id} className="chat-message-row">
            {(index===0||dayKey(items[index-1].at)!==dayKey(item.at))&&<div className="chat-day">{dateLabel(item.at)}</div>}
            <div className={'chat-message '+(item.sender===mine?'mine':'')}>
              {item.stickerId&&<StickerImage id={item.stickerId}/>} {item.text&&<p>{item.text}</p>}
              <time dateTime={item.at} title={new Date(item.at).toLocaleString('ru-RU')}>{timeLabel(item.at)}</time>
            </div>
          </div>)}
        </div>
        {selected&&jump&&<button type="button" className="btn secondary chat-jump" onClick={scrollDown}>К новым сообщениям ↓</button>}
        {selected&&suggestion&&<div className="chat-suggestion"><p>{suggestion}</p><button className="btn secondary" onClick={()=>{setText(suggestion);setSuggestion('')}}>Вставить</button>
          <button className="icon-btn" onClick={()=>setSuggestion('')} aria-label="Отклонить правку">×</button></div>}
        {selected&&<form className="chat-compose" onSubmit={send}>
          <div className="emoji-control"><button type="button" className="icon-btn" aria-label="Эмодзи" aria-expanded={emojiOpen} onClick={()=>setEmojiOpen(value=>!value)}><Smile size={22}/></button>
            {emojiOpen&&<div className="emoji-menu">{emoji.map(symbol=><button type="button" key={symbol} aria-label={'Вставить '+symbol} onClick={()=>insertEmoji(symbol)}>{symbol}</button>)}</div>}</div>
          <div className="emoji-control"><button type="button" className="icon-btn" aria-label="Стикеры" aria-expanded={stickers} onClick={()=>{setEmojiOpen(false);setStickers(value=>!value)}}><StickerIcon size={22}/></button>
           {stickers&&<StickerPicker send={id=>void sendSticker(id)}/>}</div>
          <button type="button" className="icon-btn" aria-label="Улучшить черновик" disabled={polishing||!text.trim()} onClick={()=>void polish()}><Sparkles size={17}/></button>
          <textarea ref={composer} rows={2} maxLength={2000} aria-label="Сообщение" placeholder="Написать сообщение" value={text}
            onChange={event=>setText(event.target.value)} onKeyDown={event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing){event.preventDefault();event.currentTarget.form?.requestSubmit()}}}/>
          <button className="btn" disabled={busy||!text.trim()} aria-label="Отправить"><Send size={18}/></button>
        </form>}
      </section>
    </div>
  </>;
}
