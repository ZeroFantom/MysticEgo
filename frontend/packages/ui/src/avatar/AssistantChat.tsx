import {useEffect,useRef,useState,type FormEvent} from 'react';
import {Link} from 'react-router-dom';
import {Bot,Send,ShieldCheck,Trash2,Volume2} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner} from '../components/primitives';
import {AvatarStage,type AvatarMotion} from './AvatarStage';
import {defaultAppearance,deviceAvatar,intensityByStyle,useAvatarMedia,usePersonalAvatar,type AvatarAsset} from './PersonalAvatar';
import {inferVisualMood,useAvatarVoice,useMusicBeat} from './avatarAudio';
import {AvatarTransfer} from './AvatarTransfer';
import {useIncomingAvatar,type Snapshot} from './avatarTransferState';

export type Proposal={id:string;type:string;argument:string};
export type Character={name:string;tone:string;detail:string;initiative:number;interests:string;mood:string;music:string;voice:boolean;moveDesktop:boolean;version:string};
export type AssistantStatus={service:{ready:boolean;message:string};character:Character;model:string};
type Message={id:string;role:'user'|'assistant';text:string;at:string};
type Reply={sent:Message;received:Message;proposals:Proposal[]};
type Handoff={id:string;status:'pending'|'claimed'|'started'|'declined';expiresAt:string};
const actions:Record<string,string>={launch_application:'Запустить приложение',web_search:'Открыть поиск',youtube_search:'Открыть YouTube',computer_status:'Проверить компьютер'};
const time=new Intl.DateTimeFormat('ru-RU',{hour:'2-digit',minute:'2-digit'});

export function AssistantChat({status,compact=false,patrol=false}:{status?:AssistantStatus|null;compact?:boolean;patrol?:boolean}){
  const {api,mode,portal,preferenceScope}=useApp();
  const localHandoff=(mode==='agent'||mode==='audit')&&!portal&&['127.0.0.1','localhost','[::1]'].includes(location.hostname);
  const hasAvatar=mode==='audit'||mode==='agent';
  const library=usePersonalAvatar(hasAvatar);
  const club=useRemote(()=>mode==='agent'?api.request<{theme?:{modelId:string};assets:AvatarAsset[]}>('/avatars/theme').catch(()=>null):Promise.resolve(null),[api,mode]);
  const selection=library.value?.selection;
  const clubTheme=selection?undefined:club.value?.theme;
  const view={...defaultAppearance,...selection?.appearance};
  const visible=hasAvatar&&view.showOnSite;
  const media=useAvatarMedia(selection?.assetId??clubTheme?.modelId??'bundled',selection?.motions,
    clubTheme?club.value?.assets:library.value?.assets,clubTheme?'/avatars/theme/assets/':(library.value?.path??deviceAvatar)+'/asset/',undefined,visible&&!library.loading);
  const history=useRemote(()=>api.request<Message[]>('/assistant/workbench/history'),[api]);
  const [stored,setStored]=useState<Message[]|null>(null);
  const [text,setText]=useState(''),[pending,setPending]=useState(''),[clearing,setClearing]=useState(false);
  const [error,setError]=useState(''),[proposals,setProposals]=useState<Proposal[]>([]),[result,setResult]=useState('');
  const [manualMotion,setManualMotion]=useState<AvatarMotion>(),[expression,setExpression]=useState('');
  const [importedAvatar,setImportedAvatar]=useState<Snapshot|null>(null);
  useEffect(()=>{setImportedAvatar(null);setManualMotion(undefined)},[preferenceScope]);
  useIncomingAvatar('chat',value=>{setImportedAvatar(value);setManualMotion(value.motion)},!compact&&hasAvatar);
  const voice=useAvatarVoice(),music=useMusicBeat();
  const [handoff,setHandoff]=useState<Handoff|null>(null),[handoffBusy,setHandoffBusy]=useState(false),[onDesktop,setOnDesktop]=useState(false);
  const scene=useRef<HTMLDivElement>(null);
  const playback=useRef<{motion:AvatarMotion;seconds:number}>({motion:'idle',seconds:0});
  const list=useRef<HTMLDivElement>(null);
  const editor=useRef<HTMLTextAreaElement>(null);
  const messages=stored??history.value??[];
  useEffect(()=>{if(list.current)list.current.scrollTop=list.current.scrollHeight},[messages.length,pending]);
  const name=status?.character.name??'Эхо';
  const busy=Boolean(pending);

  async function transfer(motion:'walk'|'perch'='walk'){
    if(!localHandoff||handoffBusy||handoff||onDesktop)return;
    if(status?.character.moveDesktop===false){setError('Движение по рабочему столу отключено в характере ассистента.');return;}
    setHandoffBusy(true);setError('');setResult('');
    try{
      const rect=scene.current?.getBoundingClientRect();
      const horizontal=rect?Math.max(0,Math.min(1,(rect.left+rect.width/2)/Math.max(1,window.innerWidth))):0.5;
      setHandoff(await api.request<Handoff>('/managed/avatar-handoff/request',{method:'POST',body:JSON.stringify({motion,horizontal,phaseSeconds:playback.current.motion===motion?Math.min(3600,playback.current.seconds):0,mood:inferVisualMood(expression)})}));
    }catch(problem){setError('Не удалось передать аватар локальному Agent/Audit: '+String(problem));}
    finally{setHandoffBusy(false);}
  }
  useEffect(()=>{
    if(!handoff)return;
    let active=true,timer:number|undefined;
    async function poll(){
      try{
        if(Date.parse(handoff!.expiresAt)<=Date.now())throw Error('Запрос истёк. Откройте настольное приложение и повторите.');
        const next=await api.request<Handoff>('/managed/avatar-handoff/'+handoff!.id);
        if(!active)return;
        if(next.status==='started'){voice.stop();music.stop();setManualMotion(undefined);setOnDesktop(true);setHandoff(null);return;}
        if(next.status==='declined'){setHandoff(null);setError('Переход не подтверждён или настольный аватар уже запущен.');return;}
        timer=window.setTimeout(()=>void poll(),1200);
      }catch(problem){if(active){setHandoff(null);setError(String(problem));}}
    }
    void poll();
    return()=>{active=false;if(timer!==undefined)window.clearTimeout(timer)};
  },[api,handoff]);
  function speak(answer:string){
    setExpression(answer);
    voice.say(answer,true);
  }
  async function send(event?:FormEvent){
    event?.preventDefault();
    const message=text.trim();if(!message||busy||clearing||status?.service.ready===false)return;
    if(localHandoff&&/^(?:пожалуйста[ ,]+)?(?:выйди из браузера|выйди на рабочий стол|пройдись по рабочему столу|погуляй по рабочему столу|перейди на рабочий стол|сядь на панель задач)[.! ]*$/iu.test(message)){
      setText('');await transfer(/панель задач/iu.test(message)?'perch':'walk');return;
    }
    setText('');setPending(message);setError('');setResult('');setProposals([]);
    try {
      const reply=await api.request<Reply>('/assistant/workbench/chat',{method:'POST',body:JSON.stringify({text:message})});
      setStored(previous=>[...(previous??history.value??[]).slice(-38),reply.sent,reply.received]);
      setProposals(reply.proposals??[]);setExpression(reply.received.text);
      if(view.animateReplies||status?.character.voice)voice.say(reply.received.text,status?.character.voice===true);
    }catch(problem){setText(value=>value||message);setError(problem instanceof Error?problem.message:String(problem))}
    finally{setPending('');editor.current?.focus()}
  }
  async function clear(){
    setError('');
    try{await api.request('/assistant/workbench/history',{method:'DELETE'});setStored([]);setClearing(false);setProposals([]);setResult('');setExpression('');voice.stop()}
    catch(problem){setError(String(problem))}
  }
  async function confirm(proposal:Proposal){
    setError('');
    try{
      const action=await api.request<{url?:string;structuredOutput?:unknown;output?:string}>(`/assistant/workbench/proposals/${proposal.id}/confirm`,{method:'POST'});
      setProposals(items=>items.filter(item=>item.id!==proposal.id));
      if(action.url){const link=new URL(action.url);if(link.protocol==='https:'&&['www.google.com','www.youtube.com'].includes(link.host))setResult(link.href)}
      else setResult(action.output??JSON.stringify(action.structuredOutput??action,null,2));
    }catch(problem){setError(String(problem))}
  }
  const moving=busy?'listen':voice.speaking&&view.animateReplies?'talk':manualMotion??(patrol?'walk':view.idleMotion);
  return <div className={'assistant-chat'+(compact?' compact':'')}>
    <div className="assistant-chat-header">
      <span className="chat-bot-mark"><Bot size={19}/></span><div className="grow"><strong>{name}</strong>
        <small className={status?.service.ready?'model-ready':''}>{status?.service.ready?status.service.message:status?.service.message??'Проверяем LM Studio…'}</small></div>
      <button type="button" className="icon-btn" aria-label="Очистить историю" disabled={busy||!messages.length} onClick={()=>setClearing(value=>!value)}><Trash2 size={17}/></button>
    </div>
    {visible&&!onDesktop&&<div className="assistant-scene" ref={scene}>
      <AvatarStage key={importedAvatar?'handoff-'+importedAvatar.motion+'-'+importedAvatar.phaseSeconds:'default'} url={media.model?.url} mime={media.model?.mime} motion={moving} mood={expression?inferVisualMood(expression):importedAvatar?.mood??'neutral'} initialPhase={importedAvatar?.phaseSeconds??0} play={importedAvatar?1:0} onPlayback={(motion,seconds)=>{playback.current={motion,seconds}}}
        speech={view.animateReplies?voice.speech:0} viseme={voice.viseme} beat={music.beat} animations={media.clips}
        appearance={view} intensity={intensityByStyle[selection?.motionStyle??'calm']}/>
      <div className="assistant-scene-tools"><Link to="/avatars" className="btn secondary tiny">Студия VRM</Link>
        <button type="button" className="btn secondary tiny" onClick={()=>{
          if(manualMotion){music.stop();setManualMotion(undefined)}else {setManualMotion('dance');void music.start('mic').catch(problem=>{setManualMotion(undefined);setError(String(problem))})}
        }}>{manualMotion?'Стоп':'Музыка'}</button>
        {localHandoff&&<button type="button" className="btn secondary tiny" disabled={handoffBusy||!!handoff} onClick={()=>void transfer('walk')}>На рабочий стол</button>}</div>
    </div>}
    {!compact&&visible&&<AvatarTransfer snapshot={()=>({motion:(['idle','wave','nod','walk','sit','dance'].includes(playback.current.motion)?playback.current.motion:'idle') as Snapshot['motion'],mood:expression?inferVisualMood(expression):importedAvatar?.mood??'neutral',phaseSeconds:Math.min(3600,playback.current.seconds)})}/>}
    {importedAvatar&&!compact&&<button className="btn secondary tiny" onClick={()=>{setImportedAvatar(null);setManualMotion(undefined)}}>Сбросить перенесённое состояние</button>}
    {handoff&&<p role="status">Подтвердите переход VRM в AgentDesktop или AuditDesktop на этом компьютере.</p>}
    {onDesktop&&<div className="line"><p role="status">Настольный аватар запущен. Для завершения прогулки используйте его кнопку «Стоп» или закройте окно.</p><button className="btn secondary tiny" type="button" onClick={()=>setOnDesktop(false)}>Показать VRM в браузере</button></div>}
    {clearing&&<div className="assistant-clear"><span>Очистить историю этого диалога?</span><button className="btn tiny" onClick={()=>void clear()}>Очистить</button>
      <button className="btn tiny secondary" onClick={()=>setClearing(false)}>Отмена</button></div>}
    <div className="assistant-thread" ref={list} role="log" aria-label="История диалога">
      {!messages.length&&!pending&&!history.loading&&<p className="footnote">Напишите сообщение, чтобы начать разговор.</p>}
      {messages.map(item=><article key={item.id} className={'assistant-message '+item.role}>
        <header><span>{item.role==='assistant'?name:'Вы'}</span><time dateTime={item.at}>{time.format(new Date(item.at))}</time></header>
        <p>{item.text}</p>{item.role==='assistant'&&<button type="button" className="icon-btn" aria-label="Озвучить ответ" onClick={()=>speak(item.text)}><Volume2 size={15}/></button>}
      </article>)}
      {pending&&<article className="assistant-message user"><header>Вы</header><p>{pending}</p></article>}
      {pending&&<p className="assistant-thinking" role="status"><i/><i/><i/> Ассистент отвечает</p>}
    </div>
    <ErrorBanner text={error||history.error||media.error||library.error}/>
    {proposals.map(proposal=><div className="line assistant-proposal" key={proposal.id}><div className="grow"><strong>{actions[proposal.type]??proposal.type}</strong><small>{proposal.argument}</small></div>
      <button className="btn tiny secondary" onClick={()=>void confirm(proposal)}><ShieldCheck size={15}/> Подтвердить</button></div>)}
    {result&&(result.startsWith('https://')?<a className="btn secondary" href={result} target="_blank" rel="noreferrer">Открыть в браузере</a>:<pre className="output">{result}</pre>)}
    <form className="assistant-compose" onSubmit={send}>
      <textarea ref={editor} rows={2} required maxLength={4000} value={text} aria-label="Сообщение ассистенту" placeholder="Сообщение ассистенту"
        onChange={event=>setText(event.target.value)} onKeyDown={event=>{
          if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing){event.preventDefault();void send()}
        }}/>
      <button className="btn" type="submit" aria-label="Отправить ассистенту" disabled={busy||clearing||!text.trim()||status?.service.ready===false}><Send size={17}/></button>
    </form>
  </div>;
}
