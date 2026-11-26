import {LicenseLink} from './Studio';
import {useEffect,useState} from 'react';
import {ArrowLeft,CloudCog,Download,Laptop,Monitor,Server,ShieldCheck} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import './desktop-downloads.css';

type Downloads=Partial<Record<'windows'|'linux',{tag:string;url:string}>>;
const publicApps=[
  {name:'AuditDesktop',icon:ShieldCheck,tone:'audit',description:'Личное рабочее пространство аудитора. Войдите в MysticEgo, чтобы запустить личный Audit и открыть его панель прямо из приложения.',
    features:['Личная панель Audit','Доступ к организациям','Заявка в команду разработки'],windows:'AuditDesktop.exe',linux:'./audit-desktop'},
  {name:'AgentDesktop',icon:Monitor,tone:'agent',description:'Приложение для компьютеров клуба. Получает профиль рабочей станции по локальной сети и запускает Agent.',
    features:['Подключение по приглашению Local','Панель рабочей станции','Статус связи с клубом'],windows:'AgentDesktop.cmd',linux:'./agent-desktop'},
  {name:'LocalDesktop',icon:Server,tone:'local',description:'Приложение администратора площадки. Загружает профиль у указанного Global и запускает Local в сети клуба.',
    features:['Получение профиля через Global','Запуск Local и журнал','Сетевые приглашения для Agent через панель Local'],windows:'LocalDesktop.cmd',linux:'./local-desktop'}
];
const cloudDesk={name:'CloudDesk',icon:CloudCog,tone:'cloud',description:'Рабочее приложение разработчика для обслуживания инфраструктуры MysticEgo.',
  features:['Инвентарь VPS Timeweb и DNS','Развёртывание через SSH','Обновление Orchestra и Global с журналом этапов'],
  windows:'tools\\MysticEgo.CloudDesk\\run-windows.cmd',linux:'bash tools/MysticEgo.CloudDesk/run.sh'};

function AppCard({app,downloads,loading}:{app:typeof cloudDesk;downloads?:Downloads;loading:boolean}){
  const Icon=app.icon;
  return <article className="desktop-download-card" data-product={app.tone}>
    <span className="desktop-download-icon"><Icon size={24} aria-hidden="true"/></span>
    <h2>{app.name}</h2>
    <p>{app.description}</p>
    <ul>{app.features.map(feature=><li key={feature}>{feature}</li>)}</ul>
    <div className="desktop-download-platforms">
      {(['windows','linux'] as const).map(platform=>{
        const asset=downloads?.[platform];
        const os=platform==='windows'?'Windows x64':'Linux x64';
        return <div key={platform}>
          {asset?<a className="btn secondary" href={asset.url} rel="noreferrer" aria-label={`Скачать ${app.name} для ${os} на GitHub`}><Download size={16} aria-hidden="true"/>{os}</a>:
            <span className="desktop-download-os">{os}</span>}
          <small>{asset?.tag??(loading?'Проверяем GitHub…':'Сборка недоступна')}</small>
          <code>{app[platform]}</code>
        </div>;
      })}
    </div>
  </article>;
}

export function PublicDesktopDownloads(){
  const [downloads,setDownloads]=useState<Downloads>();
  const [error,setError]=useState(false);
  const [revision,retry]=useState(0);
  useEffect(()=>{
    const controller=new AbortController();
    setError(false);
    fetch('/api/v1/public/desktop-downloads',{
      credentials:'omit',cache:'no-store',redirect:'error',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(12000)])
    }).then(async response=>{
      if(!response.ok)throw Error('GitHub');
      const data:Downloads=await response.json();
      if(!controller.signal.aborted)setDownloads(data);
    }).catch(()=>{if(!controller.signal.aborted)setError(true)});
    return ()=>controller.abort();
  },[revision]);
  return <main className="desktop-downloads desktop-downloads-public">
    <a className="desktop-download-back" href="/"><ArrowLeft size={17} aria-hidden="true"/> К авторизации</a>
    <header className="desktop-download-hero"><Laptop size={29} aria-hidden="true"/><h1>Приложения MysticEgo</h1>
      <p>Один комплект для каждой ОС включает AuditDesktop, AgentDesktop и LocalDesktop. Распакуйте ZIP и запустите нужное приложение.</p>
    </header>
    {error&&<p role="status">Не удалось получить актуальные ссылки GitHub. <button className="btn secondary" onClick={()=>retry(value=>value+1)}>Повторить</button></p>}
    <div className="desktop-download-grid">{publicApps.map(app=><AppCard key={app.name} app={app} downloads={downloads} loading={!downloads&&!error}/>)}</div>
    <p className="desktop-download-footnote">Лицензия исходного кода <LicenseLink/>.</p>
  </main>;
}

export function CloudDeskDownloads(){
  const {api}=useApp();
  const releases=useRemote(()=>api.request<Downloads>('/managed/clouddesk-downloads'),[api]);
  return <div className="desktop-downloads">
    <header className="desktop-download-hero"><CloudCog size={29} aria-hidden="true"/><h1>Скачать CloudDesk</h1>
      <p>Выберите сборку на GitHub, распакуйте ZIP и выполните команду для своей ОС.</p></header>
    {releases.error&&<p role="status">Не удалось получить ссылки. <button className="btn secondary" onClick={releases.reload}>Повторить</button></p>}
    <AppCard app={cloudDesk} downloads={releases.value??undefined} loading={releases.loading}/>
  </div>;
}
