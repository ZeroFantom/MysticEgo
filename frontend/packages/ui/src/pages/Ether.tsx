import {useState,type FormEvent} from 'react';
import {Sparkles,Shield,BookOpen,ChartNoAxesColumn} from 'lucide-react';
import {NodeApi} from '@mysticego/sdk';
import {useApp,useRemote} from '../app/context';
import {Panel,Header,ErrorBanner} from '../components/primitives';
export function EtherPage(){
 const {session}=useApp();const api=new NodeApi(import.meta.env.VITE_GLOBAL_URL||'http://127.0.0.1:5100',()=>session.token);
 const q=useRemote(()=>api.ether(),[session.token]);const [nick,setNick]=useState('');const [error,setError]=useState('');const [busy,setBusy]=useState(false);
 async function create(e:FormEvent){e.preventDefault();setBusy(true);setError('');try{await api.etherRegister(nick);q.reload()}catch(ex){setError(String(ex))}finally{setBusy(false)}}
 const a=q.value;
 return <><Header title="Ether and Chaos" description="Игровой аккаунт, прогресс и персонажи. Персонаж создаётся только в игре."/><ErrorBanner text={q.error||error}/>
 {!a?<div className="two-col"><Panel title="Создать игровой аккаунт"><form className="stack" onSubmit={create}><label>Игровой никнейм<input minLength={3} maxLength={40} required value={nick} onChange={e=>setNick(e.target.value)} /></label><button className="btn" disabled={busy}><Sparkles size={16}/> Создать игровой аккаунт</button></form></Panel><Panel title="Важно"><p className="footnote">Этот шаг создаёт запись в MysticEgo. Игровой персонаж, класс и его характеристики появляются после создания персонажа в Ether and Chaos и синхронизации с сервером игры. Здесь нельзя вручную выдавать себе уровни или предметы.</p></Panel></div>
 :<><div className="metrics"><div className="metric"><BookOpen/><div className="metric-value">{a.nickname}</div><div className="metric-label">Никнейм</div></div><div className="metric"><ChartNoAxesColumn/><div className="metric-value">{a.level}</div><div className="metric-label">Уровень (начальное значение)</div></div><div className="metric"><Shield/><div className="metric-value">{a.rank}</div><div className="metric-label">Игровой ранг</div></div><div className="metric"><Sparkles/><div className="metric-value">{a.experience}</div><div className="metric-label">Опыт (не синхронизирован)</div></div></div><Panel title="Состояние персонажа"><p>{a.characterStatus}</p><p className="footnote">Статистика пока не подтверждается игровым сервером. Изменение прогресса через браузер недоступно.</p></Panel></>}
 </>;
}
