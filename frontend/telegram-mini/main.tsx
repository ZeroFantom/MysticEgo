import React,{useEffect,useRef,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {Sparkles,ShieldCheck,Compass,MessagesSquare,Users,Backpack,ScrollText,BookOpen,Landmark,LogOut,Lock} from 'lucide-react';
import {exchange,status,logout,soul,MiniRequestError,type SoulResponse} from './mini-api';
import './mini.css';

type TelegramWebApp={
  initData?:string; colorScheme?:'light'|'dark'; ready?:()=>void; expand?:()=>void;
  close?:()=>void; onEvent?:(event:string,handler:()=>void)=>void; offEvent?:(event:string,handler:()=>void)=>void;
};
const telegram=()=>(window as Window&{Telegram?:{WebApp?:TelegramWebApp}}).Telegram?.WebApp;
type Stage='launch'|'loading'|'linked'|'denied'|'expired'|'unavailable'|'closed';
const sections=[
  {name:'Духовная связь',detail:'Личные диалоги',icon:MessagesSquare},
  {name:'Окружение',detail:'Доступные персонажи',icon:Compass},
  {name:'Друзья',detail:'Связи между игроками',icon:Users},
  {name:'Инвентарь',detail:'Предметы воплощения',icon:Backpack},
  {name:'Задания',detail:'Текущие цели',icon:ScrollText},
  {name:'Хроники',detail:'События мира',icon:BookOpen},
  {name:'Сообщества',detail:'Духовные круги',icon:Landmark}
];
function App(){
  const [stage,setStage]=useState<Stage>('launch');
  const [theme,setTheme]=useState<'dark'|'light'>(telegram()?.colorScheme==='light'?'light':'dark');
  const [available,setAvailable]=useState(false);
  const [seconds,setSeconds]=useState(0);
  const [soulData,setSoulData]=useState<SoulResponse|null>(null);
  const [soulState,setSoulState]=useState<'idle'|'loading'|'ready'|'unavailable'>('idle');
  const soulBusy=useRef(false);
  const secret=useRef<string|null>(null);
  const deadline=useRef(0);
  const busy=useRef(false);
  const checkBusy=useRef(false);
  const mounted=useRef(false);
  const attempted=useRef(false);

  useEffect(()=>{
    mounted.current=true;
    const tg=telegram();
    const update=()=>setTheme(tg?.colorScheme==='light'?'light':'dark');
    tg?.ready?.();tg?.expand?.();
    tg?.onEvent?.('themeChanged',update);
    return()=>{mounted.current=false;tg?.offEvent?.('themeChanged',update);secret.current=null;};
  },[]);
  const clear=(next:Stage)=>{
    secret.current=null;deadline.current=0;setSeconds(0);setAvailable(false);setSoulData(null);setSoulState('idle');setStage(next);
  };
  async function connect(){
    if(busy.current||attempted.current)return;
    const raw=telegram()?.initData;
    if(!raw||location.protocol!=='https:'){clear('denied');return}
    attempted.current=true;busy.current=true;setStage('loading');
    try{
      const session=await exchange(raw);
      // The initial exchange is one-use. Do not auto retry, even after network uncertainty.
      if(!mounted.current){void logout(session.sessionToken).catch(()=>{});return}
      secret.current=session.sessionToken;deadline.current=Date.now()+session.expiresInSeconds*1000;
      const verified=await status(session.sessionToken);
      if(!mounted.current||secret.current!==session.sessionToken)return;
      setSeconds(session.expiresInSeconds);setAvailable(verified.worldAvailable);setStage('linked');
    }catch(error){
      const token=secret.current;
      if(token)void logout(token).catch(()=>{});
      if(mounted.current)clear(error instanceof MiniRequestError&&error.status===401?'denied':'unavailable');
    }finally{busy.current=false}
  }
  useEffect(()=>{
    if(stage!=='linked')return;
    const tick=()=>{
      const remaining=Math.max(0,Math.ceil((deadline.current-Date.now())/1000));
      if(remaining===0){clear('expired');return}
      setSeconds(remaining);
    };
    const check=async()=>{
      const token=secret.current;
      if(!token||checkBusy.current)return;
      checkBusy.current=true;
      try{
        const result=await status(token);
        if(mounted.current&&secret.current===token)setAvailable(result.worldAvailable);
      }catch(error){
        if(mounted.current&&secret.current===token){
          void logout(token).catch(()=>{});
          clear(error instanceof MiniRequestError&&error.status===401?'expired':'unavailable');
        }
      }finally{checkBusy.current=false}
    };
    const visibility=()=>{if(document.visibilityState==='visible'){tick();void check()}};
    const timer=window.setInterval(tick,1000);
    const poll=window.setInterval(()=>{if(document.visibilityState==='visible')void check()},30000);
    document.addEventListener('visibilitychange',visibility);
    return()=>{window.clearInterval(timer);window.clearInterval(poll);document.removeEventListener('visibilitychange',visibility)};
  },[stage]);
  async function loadSoul(){
    const token=secret.current;
    if(!token||soulBusy.current)return;
    soulBusy.current=true;setSoulData(null);setSoulState('loading');
    try{
      const data=await soul(token);
      if(mounted.current&&secret.current===token){setSoulData(data);setSoulState('ready')}
    }catch(error){
      if(mounted.current&&secret.current===token){
        if(error instanceof MiniRequestError&&error.status===401)clear('expired');
        else setSoulState('unavailable');
      }
    }finally{soulBusy.current=false}
  }
  async function disconnect(){
    const token=secret.current;
    clear('closed');
    if(token)try{await logout(token)}catch{/* local capability is already cleared; expires server-side */}
  }
  const inside=Boolean(telegram()?.initData)&&location.protocol==='https:';
  const close=()=>telegram()?.close?.();
  return <main className="mini-shell" data-theme={theme}>
    <header className="mini-header"><div className="mini-brand"><Sparkles size={22}/><span>MYSTIC EGO<small>ЗЕРКАЛО ДУШИ</small></span></div><span className="mini-tag">PREVIEW</span></header>
    <section className="mini-hero">
      <div className="mini-sigil" aria-hidden="true"><span/><span/><span/><Sparkles size={45}/></div>
      <h1>Зеркало души</h1>
      <p>Твоя связь с миром Mystic Ego.</p>
    </section>
    <section className="mini-panel" aria-live="polite">
      {stage==='linked'?<>
        <h2><ShieldCheck size={20}/> Связь подтверждена</h2>
        <p>Привязка Telegram проверена сервером. {available?'Игровые сервисы подключены, интерфейс ещё не готов.':'Игровые сервисы пока не подключены к Mini App.'}</p>
        <div className="mini-session"><span>Сессия</span><strong>{Math.floor(seconds/60)}:{String(seconds%60).padStart(2,'0')}</strong></div>
        <button className="mini-secondary" onClick={()=>void disconnect()}><LogOut size={17}/> Завершить сессию</button>
      </>:stage==='launch'||stage==='loading'?<>
        <h2>{stage==='loading'?'Проверяем связь…':'Установить связь'}</h2>
        <p>{inside?'Войти можно с уже привязанным чат-ботом Telegram. Пароль здесь не требуется.':'Открой приложение из официального бота Mystic Ego в Telegram.'}</p>
        <button className="mini-primary" disabled={!inside||stage==='loading'} onClick={()=>void connect()}>{stage==='loading'?'Проверяем…':'Подтвердить вход'}</button>
      </>:<>
        <h2>{stage==='expired'?'Сессия завершилась':stage==='closed'?'Сессия закрыта':'Связь недоступна'}</h2>
        <p>{stage==='denied'?'Проверь привязку чат-бота в настройках безопасности Mystic Ego. Затем открой Mini App заново.':'Открой Mini App заново через официального бота. Данные запуска используются только один раз.'}</p>
        <button className="mini-primary" onClick={close} disabled={!telegram()?.close}>Закрыть Mini App</button>
      </>}
    </section>
    {stage==='linked'&&<section className="mini-panel" aria-labelledby="soul-heading">
      <h2 id="soul-heading"><Sparkles size={20}/> Состояние души</h2>
      {soulState==='loading'?<p>Получаем игровой профиль…</p>:soulState==='unavailable'?<p>Сервис души сейчас недоступен.</p>:
        soulData?.exists&&soulData.soul?<div className="mini-soul">
          <p>{soulData.soul.state==='EgoShard'?'Осколок Эго':'Цифровая душа'} · ранг {soulData.soul.rank}</p>
          <div className="mini-session"><span>Уровень</span><strong>{soulData.soul.level}</strong></div>
          <div className="mini-session"><span>Опыт</span><strong>{soulData.soul.experience} XP</strong></div>
          {soulData.soul.toNext>0&&<progress aria-label="Прогресс уровня" max={soulData.soul.toNext} value={Math.min(soulData.soul.inLevel,soulData.soul.toNext)}/>}
          <div className="mini-session"><span>Свободные очки</span><strong>{soulData.soul.availablePoints}</strong></div>
          {soulData.soul.attributes.map(a=><div className="mini-session" key={a.id}><span>{a.label}</span><strong>{a.locked?'Закрыто':a.value}</strong></div>)}
          <p className="mini-note">Игровые характеристики. Текущая область и активное воплощение пока не подтверждаются.</p>
        </div>:soulData?.exists===false?<p>Душа ещё не создана. Создание доступно в основном приложении.</p>:<p>Загрузи существующий игровой профиль. Данные доступны только для чтения.</p>}
      <button className="mini-secondary" disabled={soulState==='loading'} onClick={()=>void loadSoul()}>{soulState==='loading'?'Загрузка…':'Обновить состояние души'}</button>
    </section>}
    {stage==='linked'&&<section aria-labelledby="sections"><div className="mini-section-heading"><h2 id="sections">Твой мир</h2><small>В разработке</small></div>
      <div className="mini-grid">{sections.map(({name,detail,icon:Icon})=><article key={name} className="mini-tile" aria-label={name+' — пока недоступно'}>
        <div><Icon size={23}/><Lock size={13}/></div><h3>{name}</h3><p>{detail}</p></article>)}</div>
      <p className="mini-note">Разделы откроются после подключения игровых сервисов. Здесь нет вымышленных данных персонажа.</p>
    </section>}
    <footer>Единая личность · единый мир</footer>
  </main>;
}
// Avoid double-running one-use authentication. Exchange starts only on a real button click.
createRoot(document.getElementById('root')!).render(<App/>);
