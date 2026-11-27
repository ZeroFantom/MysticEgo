import {useApp,useRemote} from '../app/context';
import {Header,Panel,ErrorBanner} from '../components/primitives';
import {ReportDownload} from './ServiceMaintenance';
type Check={name:string;category:string;result:string;reason:string;at:string;latencyMs?:number};
type Run={at:string;checks:Check[]};
type History={at:string;passed:number;failed:number;unknown:number;skipped:number};
export function RuntimeTests(){
 const {api}=useApp();const data=useRemote(()=>api.request<{latest?:Run;history:History[]}>('/orchestra/self-tests'),[api],5000,true);
 const latest=data.value?.latest,history=data.value?.history??[];
 return <><Header title="Автотесты и самодиагностика" description="Безопасные проверки работающих служб каждые 15 секунд. Не заменяют интеграционные тесты CI."/>
 <ErrorBanner text={data.error}/>
 <Panel title="Результаты"><ReportDownload selfTests label="Отчёт автотестов"/>
 <p>{latest?'Последний цикл: '+new Date(latest.at).toLocaleString():'Ожидается первый цикл'}</p>
 <div className="infra-status-grid">{['pass','fail','unknown','skip'].map(result=><article className="infra-status-card" key={result}><h3>{({pass:'Пройдено',fail:'Ошибка',unknown:'Нет данных',skip:'Пропущено'} as Record<string,string>)[result]}</h3><strong>{latest?.checks.filter(c=>c.result===result).length??'—'}</strong></article>)}</div>
 <div className="stack">{latest?.checks.map(c=><article className="panel" key={c.name}><strong>{c.name} · {c.result}</strong><p>{c.reason} · {c.category} · {new Date(c.at).toLocaleTimeString()}{c.latencyMs!=null?' · '+c.latencyMs+' ms':''}</p></article>)}</div>
 </Panel><Panel title="История · до 240 циклов"><div className="stack">{history.slice(-30).reverse().map(r=><p key={r.at}>{new Date(r.at).toLocaleTimeString()} · OK {r.passed} · FAIL {r.failed} · ? {r.unknown} · SKIP {r.skipped}</p>)}</div></Panel></>;
}
