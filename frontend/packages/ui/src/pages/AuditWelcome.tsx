import {useState,type FormEvent} from 'react';
import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {ShieldCheck,Sparkles,BookOpen} from 'lucide-react';

type Assessment={nickname:string;rank:string;level:number;answersJson:string;achievementsJson:string;updatedAt:string};
const questions=[
 {id:'teamwork',text:'Мне комфортно сотрудничать с другими людьми'},
 {id:'curiosity',text:'Мне интересно осваивать новые навыки'},
 {id:'planning',text:'Я предпочитаю планировать действия заранее'},
 {id:'creativity',text:'Мне нравится придумывать необычные решения'},
 {id:'persistence',text:'Я продолжаю разбираться со сложными задачами'}
];
export function AuditWelcome(){
 const {api}=useApp();
 const record=useRemote(()=>api.request<Assessment|null>('/audit-orchestra/onboarding/me'),[api]);
 const [nickname,setNickname]=useState('');const [achievementText,setAchievementText]=useState('');
 const [answers,setAnswers]=useState<Record<string,number>>({});const [busy,setBusy]=useState(false);const [error,setError]=useState('');const [message,setMessage]=useState('');
 const save=async(e:FormEvent)=>{
  e.preventDefault();setBusy(true);setError('');setMessage('');
  try{await api.request('/audit-orchestra/onboarding/me',{method:'PUT',body:JSON.stringify({nickname,answers,achievements:achievementText.split('\n').map(s=>s.trim()).filter(Boolean)})});
   setMessage('Профиль сохранён. Уровень 1 и ранг F остаются неизменными.');record.reload();
  }catch(x){setError(String(x))}finally{setBusy(false)}
 };
 return <><Header title="AuditOrchestra · Первые шаги" description="Создание личного профиля, добровольная самооценка и достижения"/>
 <div className="metrics"><div className="metric"><ShieldCheck/><div className="metric-value">1</div><div className="metric-label">Начальный уровень</div></div><div className="metric"><Sparkles/><div className="metric-value">F</div><div className="metric-label">Начальный ранг</div></div><div className="metric"><BookOpen/><div className="metric-value">{record.value?'Создан':'Новый'}</div><div className="metric-label">Профиль</div></div></div>
 <ErrorBanner text={error||record.error}/>{message&&<Panel title="Результат"><p>{message}</p></Panel>}
 {record.value&&<Panel title="Текущий профиль"><p>{record.value.nickname} · уровень {record.value.level}, ранг {record.value.rank}</p><p className="footnote">Самооценка обновлена: {record.value.updatedAt}</p></Panel>}
 <Panel title="Личный профиль"><form className="stack" onSubmit={save}>
 <label>Никнейм<input required minLength={2} maxLength={60} value={nickname} onChange={e=>setNickname(e.target.value)}/></label>
 <p className="footnote">Отметь, насколько согласен с утверждениями: 1 — совсем не согласен, 5 — полностью согласен. Пропускать вопросы разрешено.</p>
 {questions.map(q=><label key={q.id}>{q.text}<select value={answers[q.id]||''} onChange={e=>setAnswers(s=>({...s,[q.id]:Number(e.target.value)||0}))}><option value="">Не отвечать</option>{[1,2,3,4,5].map(x=><option value={x} key={x}>{x}</option>)}</select></label>)}
 <label>Достижения (одно в строке)<textarea rows={5} value={achievementText} onChange={e=>setAchievementText(e.target.value)}/></label>
 <button className="btn" disabled={busy}>{busy?'Сохранение…':'Сохранить профиль'}</button>
 <p className="footnote">Это не психодиагностика и не оценка личности со стороны системы. Самооценка и достижения не изменяют игровой уровень, ранг или разрешения. Игровой прогресс Ether and Chaos синхронизируется отдельно.</p>
 </form></Panel></>;
}
