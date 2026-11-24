import {useEffect,useRef,useState,type FormEvent} from 'react';
import {useSearchParams} from 'react-router-dom';
import {Check,Copy,Link2,MessageCircle,Search,Send,Smile,Sticker as StickerIcon,Sparkles,UserPlus} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner,Header,Panel} from '../components/primitives';
import {ProfileAvatar} from './Profile';
import {StickerImage,StickerPicker} from '../components/Stickers';

type Person={id:string;nickname:string;displayName:string;avatarUrl?:string;accent?:string};
type Contact={id:string;status:'pending'|'accepted';incoming:boolean;person:Person};
type Message={id:string;sender:string;recipient:string;text:string;at:string;stickerId?:string};
const emoji=['😀','😂','🥹','😍','😎','🤝','👍','🙌','💜','✨','🔥','🎮','🎉','👀','🤔','🚀','🫶','✅','🌙','💬'];
function decodeSearch(value:string):string{
  try{const url=new URL(value.trim());return url.searchParams.get('add')??url.hash.slice(1)}
  catch{return value.trim()}
}

export function Collective(){
  const {api,personal,portal}=useApp();
  const [params]=useSearchParams();
  const [input,setInput]=useState(params.get('add')??'');
  const [query,setQuery]=useState(params.get('add')??'');
  const [searching,setSearching]=useState(Boolean(params.get('add')));
  const [selected,setSelected]=useState<Person|null>(null);
  const [text,setText]=useState('');
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
  useEffect(()=>{if(lastId&&listRef.current)listRef.current.scrollTop=listRef.current.scrollHeight},[lastId,selected?.id]);
  useEffect(()=>{
    const id=params.get('add');
    if(id){setInput(id);setQuery(id);setSearching(true)}
  },[params]);

  function select(person:Person){
    setSelected(person);setOlder([]);setMore(true);setText('');setError('');setEmojiOpen(false);
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
    try{await api.request('/collective/messages/'+selected.id,{method:'POST',body:JSON.stringify({text:text.trim()})});setText('');messages.reload();composer.current?.focus()}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function sendSticker(id:string){
    if(!selected||busy)return;
    setBusy(true);setError('');
    try{
      await api.request('/collective/messages/'+selected.id,{method:'POST',body:JSON.stringify({stickerId:id})});
      messages.reload();setStickers(false);
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function polish(){
    if(!text.trim()||polishing)return;
    setPolishing(true);setError('');setSuggestion('');
    try{
      const reply=await api.request<{text:string}>('/assistant/polish',{method:'POST',body:JSON.stringify({text:text.trim()})});
      setSuggestion(reply.text);
    }catch(error){setError(String(error))}
    finally{setPolishing(false)}
  }
  async function previous(){
    const first=older[0]??messages.value?.[0];
    if(!selected||!first)return;
    try{
      const data=await api.request<Message[]>(`/collective/messages/${selected.id}?before=${encodeURIComponent(first.at)}`);
      const scrollHeight=listRef.current?.scrollHeight??0;
      setMore(data.length===60);setOlder(items=>[...data,...items]);
      requestAnimationFrame(()=>{if(listRef.current)listRef.current.scrollTop=listRef.current.scrollHeight-scrollHeight});
    }catch(error){setError(String(error))}
  }
  function insertEmoji(symbol:string){
    const start=composer.current?.selectionStart??text.length;
    const end=composer.current?.selectionEnd??text.length;
    setText(current=>(current.slice(0,start)+symbol+current.slice(end)).slice(0,2000));
    setEmojiOpen(false);
    requestAnimationFrame(()=>{composer.current?.focus();composer.current?.setSelectionRange(start+symbol.length,start+symbol.length)});
  }
  const thread=[...older,...(messages.value??[])].filter((item,index,all)=>all.findIndex(x=>x.id===item.id)===index);
  return <>
    <Header title="Коллектив" description="Контакты, поиск игроков и личные сообщения" action={mine&&<div className="row-actions">
      <button className="btn secondary" onClick={()=>void copy(false)}><Copy size={15}/> Мой ID</button>
      <button className="btn secondary" onClick={()=>void copy(true)}><Link2 size={16}/> Поделиться</button>
    </div>}/>
    <ErrorBanner text={error||contacts.error||people.error}/>
    {notice&&<p className="success collective-notice" role="status">{notice}</p>}
    <div className="collective-layout">
      <div className="collective-people">
        <Panel title="Найти игрока">
          <form className="stack" onSubmit={event=>{event.preventDefault();setQuery(decodeSearch(input));setSearching(true);people.reload()}}>
            <label>Никнейм, ID или ссылка<input value={input} onChange={event=>setInput(event.target.value)} placeholder="Игровой никнейм"/></label>
            <button className="btn secondary"><Search size={16}/> Найти</button>
          </form>
          {searching&&<div className="people-results">{people.value?.map(person=>{
            const existing=contacts.value?.find(x=>x.person.id===person.id);
            return <div className="line" key={person.id}><ProfileAvatar name={person.nickname} url={person.avatarUrl}/>
              <div className="grow"><strong>{person.nickname}</strong>{person.displayName&&person.displayName!==person.nickname&&<small>{person.displayName}</small>}</div>
              {existing?.status==='accepted'?<button className="icon-btn" aria-label={'Чат '+person.nickname} onClick={()=>select(person)}><MessageCircle size={17}/></button>:
                <button className="icon-btn" disabled={busy||Boolean(existing)} onClick={()=>void contact(person)} aria-label={'Добавить '+person.nickname}>
                  {existing?<Check size={17}/>:<UserPlus size={17}/>}</button>}
            </div>;
          })}{people.value?.length===0&&<p className="footnote">Игроки не найдены.</p>}</div>}
        </Panel>
        <Panel title="Контакты">
          {!contacts.value?.length&&<p className="footnote">Найдите игрока и отправьте запрос.</p>}
          {contacts.value?.map(row=><div className={'contact-row '+(selected?.id===row.person.id?'selected':'')} key={row.id}>
            <ProfileAvatar name={row.person.nickname} url={row.person.avatarUrl}/>
            {row.status==='accepted'?<button className="contact-name" onClick={()=>select(row.person)}>{row.person.nickname}</button>:
              <div className="grow"><strong>{row.person.nickname}</strong><small>{row.incoming?'Входящий запрос':'Запрос отправлен'}</small></div>}
            {row.status==='pending'&&row.incoming&&<button className="icon-btn" disabled={busy} onClick={()=>void accept(row)} aria-label={'Принять запрос '+row.person.nickname}><Check size={18}/></button>}
          </div>)}
        </Panel>
        {mine&&<p className="footnote collective-id">Ваш ID <code>{mine}</code></p>}
      </div>
      <section className="panel messenger">
        <header className="messenger-header">{selected?<><ProfileAvatar name={selected.nickname} url={selected.avatarUrl}/><h2>{selected.nickname}</h2></>:<><MessageCircle size={19}/><h2>Сообщения</h2></>}</header>
        <div className="message-list" ref={listRef}>
          {messages.error&&<ErrorBanner text={messages.error}/>}
          {!selected&&<div className="empty chat-idle"><MessageCircle size={32}/><p>Выберите контакт, чтобы открыть переписку.</p></div>}
          {selected&&more&&(older.length>0||messages.value?.length===60)&&<button className="btn secondary tiny" onClick={()=>void previous()}>Предыдущие сообщения</button>}
          {selected&&!thread.length&&!messages.loading&&!messages.error&&<div className="empty chat-idle"><p>Отправьте первое сообщение.</p></div>}
          {thread.map(item=><div key={item.id} className={'chat-message '+(item.sender===mine?'mine':'')}>
            {item.stickerId&&<StickerImage id={item.stickerId}/>} {item.text&&<p>{item.text}</p>}<time dateTime={item.at} title={new Date(item.at).toLocaleString('ru-RU')}>{new Date(item.at).toLocaleTimeString('ru-RU',{hour:'2-digit',minute:'2-digit'})}</time>
          </div>)}
        </div>
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
