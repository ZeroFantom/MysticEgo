import {useState,type CSSProperties,type FormEvent} from 'react';
import {Link} from 'react-router-dom';
import {Clock3,Pencil,Shield,Lock,Layers3,Sparkles,UserRound,Check} from 'lucide-react';
import {useApp,useRemote,type AccountProfile,type PersonalAccount,type Personality} from '../app/context';
import {Panel,Header,ErrorBanner} from '../components/primitives';
import {ProfileDialog} from '../components/ProfileDialog';
import './profile-polish.css';

export function bannerStyle(url?:string):CSSProperties|undefined{
  if(!url)return undefined;
  return {backgroundImage:'linear-gradient(100deg,#111019e8 5%,#10121970 67%,#171320df),url('+JSON.stringify(url)+')'};
}
export function ProfileAvatar({url,name,large=false}:{url?:string;name:string;large?:boolean}){
  const [failed,setFailed]=useState('');
  return <span className={large?'profile-avatar account-avatar':'account-avatar'}>
    {url&&failed!==url?<img src={url} alt="" referrerPolicy="no-referrer" onError={()=>setFailed(url)}/>:
      (name.trim().slice(0,1).toLocaleUpperCase('ru-RU')||<UserRound size={18}/>)}
  </span>;
}
function PetalChart({data}:{data:Personality}){
  const cx=158,cy=155;
  const point=(angle:number,radius:number)=>[cx+Math.cos(angle)*radius,cy+Math.sin(angle)*radius].map(v=>v.toFixed(2)).join(' ');
  return <svg viewBox="0 0 316 310" className={'petal-chart '+(data.completed?'is-complete':'')} role="img"
      aria-label={data.completed?'Диаграмма теста '+data.axes.map(x=>x.label+' '+x.value+' из 5').join(', '):'Пустая восьмиосевая диаграмма'}>
    <defs><radialGradient id="petal-glow"><stop stopColor="#e3d3ff"/><stop offset="1" stopColor="#7f59e8"/></radialGradient></defs>
    {[22,44,66,88,110].map(r=><circle key={r} cx={cx} cy={cy} r={r} className="petal-grid"/>)}
    {data.axes.map((axis,index)=>{
      const angle=index*Math.PI/4-Math.PI/2;
      const radius=(axis.value??0)*22;
      const path='M '+cx+' '+cy+' Q '+point(angle-.33,radius*.72)+' '+point(angle,radius)+' Q '+point(angle+.33,radius*.72)+' '+cx+' '+cy;
      const [x,y]=point(angle,128).split(' ');
      const abbreviations:Record<string,string>={emotionality:'Эмоции',independence:'Самостоятельность'};
      return <g key={axis.id}><path d={`M ${cx} ${cy} L ${point(angle,110)}`} className="petal-axis"/>
        {radius>0&&<path d={path} fill="url(#petal-glow)" className="petal-fill"><title>{axis.label} · {axis.value}/5</title></path>}
        <text x={x} y={y} dominantBaseline="middle" textAnchor="middle">{abbreviations[axis.id]??axis.label}</text>
      </g>;
    })}
    <circle cx={cx} cy={cy} r="5" className="petal-core"/>
  </svg>;
}

function Attributes({stats,progress}:{stats:PersonalAccount['stats'];progress:PersonalAccount['progress']}){
  const {api,reloadProfile}=useApp();
  const [draft,setDraft]=useState<Record<string,number>>({});
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const spent=Object.values(draft).reduce((sum,points)=>sum+points,0);
  const remaining=(progress?.availablePoints??0)-spent;
  const groups=[['mind','Разум и восприятие'],['social','Эмоции и взаимодействие'],['body','Физические характеристики']] as const;
  function change(id:string,delta:number){setDraft(previous=>({...previous,[id]:Math.max(0,(previous[id]??0)+delta)}))}
  async function apply(){
    if(!progress||spent===0||busy)return;
    setBusy(true);setError('');
    try{
      await api.request('/collective/shard/attributes',{method:'POST',body:JSON.stringify({points:Object.fromEntries(Object.entries(draft).filter(([,value])=>value>0)),expectedSpent:progress.spentPoints})});
      setDraft({});reloadProfile();
    }catch(problem){setError(String(problem))}
    finally{setBusy(false)}
  }
  return <Panel title="Характеристики" aside={progress&&<span className="inline-chip">Свободно · {remaining}</span>}>
    <p className="footnote">Задания развивают навыки. Каждый новый уровень даёт 3 очка на распределение.</p>
    <ErrorBanner text={error}/>
    {groups.map(([group,title])=><section className="attribute-section" key={group}>
      <h3>{title}</h3><div className="stat-grid">{stats.filter(stat=>stat.group===group).map(stat=>{
        const proposed=draft[stat.id]??0;
        return <article className={'stat-tile attribute-tile'+(stat.locked?' locked':'')} key={stat.id}>
          <div><span>{stat.label}</span><strong>{stat.locked?<Lock size={17} aria-label="Закрыто до воплощения"/>:stat.value}</strong></div>
          <small>{stat.locked?stat.hint:stat.grade}</small>
          {!stat.locked&&group!=='body'&&<>
            <small className="attribute-hint">{stat.hint}</small>
            <small>База {stat.base} · распределено {stat.allocated}</small>
            {(progress?.availablePoints??0)>0&&<div className="attribute-stepper">
              <button type="button" className="icon-btn" aria-label={'Убрать очко: '+stat.label} disabled={busy||!proposed} onClick={()=>change(stat.id,-1)}>−</button>
              <span>+{proposed}</span><button type="button" className="icon-btn" aria-label={'Добавить очко: '+stat.label} disabled={busy||remaining<1||(stat.value??0)+proposed>=100} onClick={()=>change(stat.id,1)}>+</button>
            </div>}
          </>}
        </article>;
      })}</div>
    </section>)}
    {spent>0&&<div className="row-actions">
      <button className="btn" disabled={busy||remaining<0} onClick={()=>void apply()}>Распределить {spent}</button>
      <button className="btn secondary" disabled={busy} onClick={()=>setDraft({})}>Сбросить выбор</button>
    </div>}
    {progress&&<details className="derived-traits" open><summary>Производные характеристики</summary>
      {progress.traits.map(item=><div className={'line '+(item.locked?'muted':'')} key={item.id}><div className="grow"><strong>{item.title}</strong><small>{item.formula}</small></div>
        {item.value!=null?<strong>{new Intl.NumberFormat('ru-RU',{maximumFractionDigits:2}).format(item.value)}{item.unit}</strong>:<Lock size={15}/>}</div>)}
      {progress.concealed>0&&<p className="footnote">Скрытых характеристик до раскрытия · {progress.concealed}</p>}
    </details>}
  </Panel>;
}

export function ProfilePage(){
  const {api,mode,personal,profileError,reloadProfile}=useApp();
  const [draft,setDraft]=useState<AccountProfile|null>(null);
  const [quiz,setQuiz]=useState(false);
  const [answers,setAnswers]=useState<Record<string,number>>({});
  const [consent,setConsent]=useState(false);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [page,setPage]=useState(0);
  const [quest,setQuest]=useState(''),[questAnswer,setQuestAnswer]=useState(''),[questStatus,setQuestStatus]=useState('');
  type QuestResult={passed:boolean;delta:number;grade:string;covered?:number;required?:number};
  const quests=useRemote(()=>api.request<{id:string;title:string;npc:string;prompt:string;experience:number;difficulty:number;stat:string;attempt?:QuestResult}[]>('/collective/shard/quests'),[api]);
  if(!personal)return <><Header title="Мой профиль"/><ErrorBanner text={profileError}/>{!profileError&&<div className="empty">Загружаем профиль…</div>}</>;
  const {profile,game,stats,personality,progress}=personal;
  const questions=personality.questions??personality.axes.map(axis=>({id:axis.id,axis:axis.id,text:axis.question}));
  const portion=6,totalPages=Math.ceil(questions.length/portion);
  const visible=questions.slice(page*portion,(page+1)*portion);
  const currentQuest=quests.value?.find(item=>item.id===quest);
  const number=(value:number)=>new Intl.NumberFormat('ru-RU',{maximumFractionDigits:2}).format(value);
  async function finishQuest(event:FormEvent){
    event.preventDefault();if(!currentQuest||busy)return;setBusy(true);setQuestStatus('');
    try{
      const answer=await api.request<QuestResult>('/collective/shard/quests',{method:'POST',body:JSON.stringify({questId:quest,answer:questAnswer})});
      const reviewed=answer.required?` · критерии ${answer.covered}/${answer.required}`:'';
      const attribute=stats.find(item=>item.id===currentQuest.stat)?.label;
      setQuestStatus(answer.passed?`${answer.grade} · +${currentQuest.experience} XP${attribute?` · ${attribute} +${answer.delta}`:''}${reviewed}`:
        `${answer.grade}${reviewed}. Попробуйте добавить причину и конкретное действие.`);
      setQuestAnswer('');quests.reload();reloadProfile();
    }catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  const name=profile.displayName||game.nickname;
  const update=(key:keyof AccountProfile,value:string|boolean)=>setDraft(current=>current?{...current,[key]:value}:current);
  async function save(event:FormEvent){
    event.preventDefault();setBusy(true);setError('');
    try{await api.request('/collective/me',{method:'PUT',body:JSON.stringify(draft)});setDraft(null);reloadProfile()}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  async function submit(event:FormEvent){
    event.preventDefault();setBusy(true);setError('');
    try{await api.request('/collective/personality',{method:'POST',body:JSON.stringify({version:personality.version,answers,consent})});setQuiz(false);reloadProfile()}
    catch(error){setError(String(error))}
    finally{setBusy(false)}
  }
  const accent=/^#[\da-f]{6}$/i.test(profile.accent)?profile.accent:undefined;
  return <div className="profile-page" data-custom-background={Boolean(profile.backgroundUrl)} style={{...bannerStyle(profile.backgroundUrl),'--profile-accent':accent} as CSSProperties}>
    <Header title="Мой профиль" action={<div className="row-actions">{(mode==='audit'||mode==='agent')&&<Link className="btn secondary" to="/avatars">Студия VRM</Link>}
      <button className="btn secondary" onClick={()=>setDraft(profile)}><Pencil size={16}/> Оформление</button></div>}/>
    <ErrorBanner text={error}/>
    <section className="profile-hero" data-custom-cover={Boolean(profile.coverUrl)} style={bannerStyle(profile.coverUrl)}>
      <div className="profile-identity"><ProfileAvatar url={profile.avatarUrl} name={name} large/><div>
        <h2>{name}</h2><p>{game.nickname===name?'Ether and Chaos':game.nickname}</p>
      </div></div>
      {profile.statusText&&<p className="social-status">{profile.statusText}</p>}
      {profile.bio&&<p className="profile-bio">{profile.bio}</p>}
      <div className="profile-facts">
        <div className="game-rank"><Shield size={20}/><span>Ранг</span><strong>{game.rank}</strong></div>
        <div><Layers3 size={18}/><span>Уровень</span><strong>{game.level}</strong></div>
        <div><Clock3 size={18}/><span>Срок жизни</span><strong>{game.immortal?'Бессмертен':game.expiresAt?new Date(game.expiresAt).toLocaleDateString('ru-RU'):'По правилам мира'}</strong></div>
        <div><Sparkles size={18}/><span>Статус персонажа</span><strong>{game.state==='EgoShard'?'Осколок Эго':game.state}</strong></div>
      </div>
      {progress&&<div className="xp-track">
        <div><strong>{number(progress.experience)} XP</strong><span>{progress.toNext?number(progress.inLevel)+' / '+number(progress.toNext)+' · '+number(progress.percent)+'%':'Максимальный уровень'}</span></div>
        <progress aria-label="Опыт до следующего уровня" max={100} value={progress.percent}/>
      </div>}
    </section>
    {progress&&<div className="resource-meters">{progress.resources.map(item=><article key={item.id} className={'meter '+item.id+(item.locked?' locked':'')}>
      <div><span>{item.title}</span><strong>{item.current==null||item.maximum==null?<Lock size={15} aria-label="Ресурс закрыт"/>:`${number(item.current)} / ${number(item.maximum)}`}</strong></div>
      {item.current!=null&&item.maximum!=null?<progress max={item.maximum} value={item.current} aria-label={item.title}/>:<div className="locked-meter" aria-hidden="true"/>}<small>{item.formula}</small>
    </article>)}</div>}
    <div className="profile-grid">
      <Attributes stats={stats} progress={progress}/>
      <Panel title="Тест личности" aside={personality.completed?<span className="inline-chip"><Check size={13}/> Пройден</span>:undefined}>
        <PetalChart data={personality}/>
        {personality.completed&&<dl className="personality-values">{personality.axes.map(axis=><div key={axis.id}><dt>{axis.label}</dt><dd>{axis.value==null?'—':number(axis.value)+' / 5'}</dd></div>)}</dl>}
        <div className="center-actions"><button className="btn" onClick={()=>{setAnswers({});setPage(0);setConsent(false);setQuiz(true)}}>{personality.completed?'Пройти заново':'Пройти тест'}</button>
          <p className="footnote">{personality.completedAt?`Результат от ${new Date(personality.completedAt).toLocaleDateString('ru-RU')}`:'24 утверждения по восьми игровым осям'}</p>
        </div>
      </Panel>
    </div>
    <Panel title="Задания осколка">
      <div className="quest-options">{quests.value?.map(item=><button className={'btn secondary '+(quest===item.id?'selected':'')} key={item.id}
        onClick={()=>{setQuest(item.id);setQuestAnswer('');setQuestStatus('')}}>{item.title} · {item.attempt?item.attempt.passed?'выполнено':'ответ сохранён':item.experience+' XP'}</button>)}</div>
      {currentQuest&&<form className="stack" onSubmit={finishQuest}>
        <div><p>{currentQuest.npc} · {currentQuest.prompt}</p>
          <p className="footnote">Сложность {currentQuest.difficulty} · {currentQuest.experience} XP · {stats.find(item=>item.id===currentQuest.stat)?.label}</p>
          {currentQuest.attempt&&<p className="footnote">{currentQuest.attempt.grade}{currentQuest.attempt.required?` · критерии ${currentQuest.attempt.covered}/${currentQuest.attempt.required}`:''}</p>}
        </div>
        {!currentQuest.attempt?.passed&&<><label>Ваш ответ<textarea value={questAnswer} onChange={event=>setQuestAnswer(event.target.value)} required maxLength={3000}/></label>
        <button className="btn" disabled={busy}>Ответить</button></>}
      </form>}
      {questStatus&&<p className="success">{questStatus}</p>}
    </Panel>
    {draft&&<ProfileDialog label="Оформление профиля" busy={busy} onClose={()=>setDraft(null)}>
      <h2>Оформление профиля</h2><form className="stack" onSubmit={save}>
        <div className="profile-edit-preview" aria-label="Предпросмотр оформления" data-custom-cover={Boolean(draft.coverUrl)} style={bannerStyle(draft.coverUrl)}>
          <ProfileAvatar url={draft.avatarUrl} name={draft.displayName||game.nickname} large/><div><strong>{draft.displayName||game.nickname}</strong>{draft.statusText&&<p>{draft.statusText}</p>}</div>
        </div>
        <label>Отображаемое имя<input maxLength={60} value={draft.displayName} onChange={event=>update('displayName',event.target.value)}/></label>
        <label>Статус<input maxLength={100} value={draft.statusText??''} placeholder="Например, «На связи после 18:00»" onChange={event=>update('statusText',event.target.value)}/></label>
        <label>О себе<textarea rows={3} maxLength={600} value={draft.bio} onChange={event=>update('bio',event.target.value)}/></label>
        {([['avatarUrl','Аватар'],['coverUrl','Баннер профиля и панели'],['backgroundUrl','Фон профиля']] as const).map(([key,label])=>
          <label key={key}>{label}<input type="url" placeholder="https://…" value={draft[key]} onChange={event=>update(key,event.target.value)}/></label>)}
        <label>Акцент карточки<input type="color" value={draft.accent} onChange={event=>update('accent',event.target.value)}/></label>
        <label className="check-row"><input type="checkbox" checked={draft.discoverable} onChange={event=>update('discoverable',event.target.checked)}/> Доступен поиск по игровому никнейму</label>
        <ErrorBanner text={error}/><div className="row-actions"><button className="btn" disabled={busy}>Сохранить</button><button type="button" className="btn secondary" disabled={busy} onClick={()=>setDraft(null)}>Закрыть</button></div>
      </form></ProfileDialog>}
    {quiz&&<ProfileDialog label="Тест личности" busy={busy} onClose={()=>setQuiz(false)} wide>
      <h2>Тест личности</h2><p className="footnote">Оцените каждое утверждение от 1 (совсем не согласен) до 5 (полностью согласен). Ответы заполняют лепестки игрового стиля.</p>
      <form className="stack" onSubmit={submit}>
        <div className="quiz-progress"><span>Шаг {page+1} / {totalPages}</span><progress aria-label="Ответы теста" max={questions.length} value={Object.keys(answers).length}/></div>
        {visible.map((question,index)=><fieldset className="quiz-question" key={question.id}>
          <legend>{page*portion+index+1}. {question.text}</legend>
          <div className="quiz-scale">{[1,2,3,4,5].map(value=><label key={value}><input name={question.id} type="radio" value={value}
            required checked={answers[question.id]===value} onChange={()=>setAnswers(current=>({...current,[question.id]:value}))}/><span>{value}</span></label>)}</div>
        </fieldset>)}
        {page===totalPages-1&&<label className="check-row"><input required type="checkbox" checked={consent} onChange={event=>setConsent(event.target.checked)}/> Я согласен сохранить ответы в игровом профиле</label>}
        <ErrorBanner text={error}/>
        <div className="row-actions">
          {page>0&&<button type="button" className="btn secondary" onClick={()=>setPage(page-1)}>Назад</button>}
          {page<totalPages-1?<button type="button" className="btn" disabled={visible.some(item=>!answers[item.id])} onClick={()=>setPage(page+1)}>Далее</button>:
          <button className="btn" disabled={busy||!consent||Object.keys(answers).length!==questions.length}>Сохранить результат</button>}
          <button type="button" className="btn secondary" disabled={busy} onClick={()=>setQuiz(false)}>Отмена</button>
        </div>
      </form>
    </ProfileDialog>}
  </div>;
}
