import {ThemeToggle} from './Settings';
import {LicenseLink} from './Studio';
import {useEffect,useState} from 'react';
import {ArrowLeft,CloudCog,Download,Laptop,Monitor,Server,ShieldCheck} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import './desktop-downloads.css';

type DownloadAsset={tag:string;url:string;fileName?:string;format?:'exe'|'run'|'zip'};
type Downloads=Partial<Record<string,DownloadAsset>>;
const publicApps=[
  {name:'AuditDesktop',icon:ShieldCheck,tone:'audit',description:'Личное рабочее пространство аудитора. Войдите в MysticEgo, чтобы запустить личный Audit и открыть его панель прямо из приложения.',
    features:['Личная панель Audit','Доступ к организациям','Заявка в команду разработки'],windows:'AuditDesktop.exe',linux:'./audit-desktop'},
  {name:'AgentDesktop',icon:Monitor,tone:'agent',description:'Приложение для компьютеров клуба. Получает профиль рабочей станции по локальной сети и запускает Agent.',
    features:['Подключение по приглашению Local','Панель рабочей станции','Статус связи с клубом'],windows:'AgentDesktop.cmd',linux:'./agent-desktop'},
  {name:'LocalDesktop',icon:Server,tone:'local',description:'Приложение администратора площадки. Загружает профиль у указанного Global и запускает Local в сети клуба.',
    features:['Получение профиля через Global','Запуск Local и журнал','Сетевые приглашения для Agent через панель Local'],windows:'LocalDesktop.cmd',linux:'./local-desktop'}
];
const cloudDesk={name:'CloudDesk',icon:CloudCog,tone:'cloud',description:'Единое приложение разработчика: локальный центр и удалённая инфраструктура MysticEgo.',
  features:['Локальные Auth, Orchestra и Audit без облачной авторизации','Инвентарь VPS Timeweb и DNS','Развёртывание через SSH','Обновление Orchestra и Global с журналом этапов'],
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
        const direct=downloads?.[app.tone+'-'+platform];
        const asset=direct??downloads?.[platform];
        const format=asset?.format??(direct?(platform==='windows'?'exe':'run'):'zip');
        const os=platform==='windows'?'Windows x64':'Linux x64';
        return <div key={platform}>
          {asset?<a className="btn secondary" href={asset.url} rel="noreferrer" aria-label={`Скачать ${app.name} для ${os} на GitHub`}><Download size={16} aria-hidden="true"/>{os} · {format.toUpperCase()}</a>:
            <span className="desktop-download-os">{os}</span>}
          <small>{asset?.tag??(loading?'Проверяем GitHub…':'Сборка недоступна')}</small>
          {asset&&<code>{format==='zip'?app[platform]:format==='run'?`bash ${asset.fileName??'установщик.run'}`:'Запустите установщик EXE'}</code>}
          {asset&&format==='zip'&&<small>Резервный ZIP-комплект</small>}
        </div>;
      })}
    </div>
  </article>;
}

export function PublicDesktopDownloads({scope=''}:{scope?:string}={}){
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
    <div className="appearance-public-bar"><ThemeToggle scope={scope}/></div>
    <a className="desktop-download-back" href="/"><ArrowLeft size={17} aria-hidden="true"/> К авторизации</a>
    <header className="desktop-download-hero"><Laptop size={29} aria-hidden="true"/><h1>Приложения MysticEgo</h1>
      <p>Выберите нужное приложение и ОС. Windows: запустите EXE. Linux x64: запустите скачанный RUN-файл через bash. Среды выполнения включены. Если установщика пока нет, доступен прежний ZIP.</p>
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
      <p>Выберите установщик или резервный ZIP. CloudDesk поддерживает Windows 10/11 x64 и Linux x64. Windows-установщик включает клиентские инструменты SSH и GitHub CLI; в Linux нужны системные SSH, GitHub CLI и хранилище секретов.</p></header>
    {releases.error&&<p role="status">Не удалось получить ссылки. <button className="btn secondary" onClick={releases.reload}>Повторить</button></p>}
    <AppCard app={cloudDesk} downloads={releases.value??undefined} loading={releases.loading}/>
  </div>;
}
