import {useEffect,useState} from 'react';
import {useApp} from '../app/context';
import {Panel,ErrorBanner} from './primitives';

type PackageStatus={installed:boolean;packageId:string};
type State={enabled:boolean;platform:string;installed:boolean;active:boolean;strategy?:string;warpInstalled:boolean;
 lastError?:string;strategies:string[];scores:Record<string,number>;extras?:{packages:Record<string,PackageStatus>;
 systemChangesAllowed:boolean;packageChangesAllowed:boolean;watchdogEnabled:boolean;backupAvailable:boolean;
 robloxHostsBackup:boolean;pubgQuarantine:boolean;testDomains:string[];strategyMap:string}};
type Action={action:string;strategy?:string;mode?:string;confirmed:boolean;domains?:string[]};
type Entry={action:string;label:string;mode?:string;danger?:boolean};
const sections:{title:string;description:string;actions:Entry[]}[]=[
 {title:'Zapret и диагностика',description:'Управление стратегиями, списками и диагностикой сети.',actions:[
  {action:'startup-maintenance',label:'Первичная настройка + выбор стратегии'},
  {action:'update-zapret',label:'Установить / обновить Zapret'},
  {action:'test-strategies',label:'Протестировать стратегии'},
  {action:'start-zapret',label:'Запустить выбранную стратегию'},
  {action:'stop-zapret',label:'Остановить Zapret'},
  {action:'strategy-map',label:'Карта стратегий'},
  {action:'update-lists',label:'Обновить доменные и IP-списки'},
  {action:'probe',label:'Проверить доступность доменов'},
  {action:'flush-dns',label:'Очистить DNS-кэш'},
  {action:'winsock-diagnostics',label:'Диагностика Winsock'}]},
 {title:'WARP и Telegram',description:'Режимы WARP и настройка прокси Telegram через ссылку в пользовательской сессии.',actions:[
  {action:'warp-install',label:'Установить WARP'},
  {action:'warp-on',mode:'warp',label:'WARP — системный режим',danger:true},
  {action:'warp-on',mode:'proxy',label:'WARP — Proxy',danger:true},
  {action:'warp-off',label:'Отключить WARP'},
  {action:'telegram-global',label:'Telegram: начать вход через WARP',danger:true},
  {action:'telegram-ips-update',label:'Telegram: обновить CIDR через WARP Proxy'},
  {action:'telegram-proxy',label:'Telegram: включить SOCKS5',danger:true},
  {action:'telegram-off',label:'Telegram: выключить WARP',danger:true}]},
 {title:'Игровые исправления',description:'Изменения DNS, hosts и файлов выполняются только при разрешении администратора и имеют отдельные команды отката.',actions:[
  {action:'game-roblox-hosts',label:'Roblox: обновить hosts',danger:true},
  {action:'game-roblox-hosts-restore',label:'Roblox: восстановить hosts'},
  {action:'game-roblox-install',label:'Roblox: установить'},
  {action:'game-roblox-reinstall',label:'Roblox: переустановить',danger:true},
  {action:'game-roblox-launch',label:'Roblox: найти исполняемый файл'},
  {action:'game-pubg-fortnite',label:'PUBG/Fortnite: домены + Zapret/WARP',danger:true},
  {action:'game-phasmophobia',label:'Phasmophobia: Photon + Zapret/WARP',danger:true},
  {action:'game-pubg-trailers',label:'PUBG: отправить трейлеры в карантин',danger:true},
  {action:'game-pubg-trailers-restore',label:'PUBG: вернуть трейлеры'},
  {action:'game-pubg-dns',label:'PUBG: DNS Яндекс',danger:true},
  {action:'game-warzone-dns',label:'Warzone: DNS Cloudflare',danger:true},
  {action:'dns-restore',label:'Восстановить прежние DNS',danger:true}]},
 {title:'SignalRGB и утилиты',description:'Управление приложениями через winget и опциональный watchdog SignalRGB.',actions:[
  {action:'signalrgb-watchdog-on',label:'Включить watchdog SignalRGB'},
  {action:'signalrgb-watchdog-off',label:'Выключить watchdog SignalRGB'}]},
 {title:'Игровые лаунчеры',description:'Steam, Epic Games Launcher, Battle.net: установка, обновление и проверка пути к клиенту.',actions:[
  {action:'launchers-all',label:'Проверить/обновить все лаунчеры'}]}
];
const applicationNames:Record<string,string>={signalrgb:'SignalRGB','msi-center':'MSI Center',ghub:'Logitech G HUB',fury:'Kingston FURY CTRL',steam:'Steam',epic:'Epic Games',battlenet:'Battle.net',roblox:'Roblox'};

export function NetworkRepairPanel(){
 const {api}=useApp();const [data,setData]=useState<State|null>(null);
 const [running,setRunning]=useState('');const [error,setError]=useState('');
 const [output,setOutput]=useState<unknown>(null);const [strategy,setStrategy]=useState('');
 const [domains,setDomains]=useState('discord.com\nwww.youtube.com\nredirector.googlevideo.com\nyoutu.be\ndiscord.gg');
 const [tab,setTab]=useState(0);
 useEffect(()=>{void refresh();},[api]);
 async function refresh(){try{setError('');const next=await api.request<State>('/network-repair/status');setData(next);
  if(next.extras?.testDomains?.length)setDomains(next.extras.testDomains.join('\n'));
 }catch(e){setError(String(e));}}
 async function execute(action:string,mode?:string){
  if(!window.confirm('Подтвердите операцию «'+action+'». Она может изменить сеть или ПО компьютера.'))return;
  setRunning(action+mode);setError('');setOutput(null);
  const input:Action={action,mode,strategy:action==='start-zapret'?(strategy||undefined):undefined,confirmed:true};
  if(action==='test-domains-set')input.domains=domains.split(/\r?\n/).map(x=>x.trim()).filter(Boolean);
  try{
   const response=await api.request('/network-repair/actions',{method:'POST',body:JSON.stringify(input)});
   setOutput(response);
   // Telegram deep link must be opened by the interactive user, never from the privileged Windows service.
   const result=response as {details?:{deepLink?:string}};
   if(result.details?.deepLink&&window.confirm('Открыть настройку прокси в Telegram?')){
    window.location.href=result.details.deepLink;
   }
   await refresh();
  }catch(e){setError(String(e));}finally{setRunning('');}
 }
 const ready=data?.enabled&&data?.platform==='windows';
 function actionButton(entry:Entry){return <button key={entry.action+entry.mode} type="button"
  className={'btn '+(entry.danger?'':'secondary')} disabled={!!running||!ready}
  onClick={()=>void execute(entry.action,entry.mode)}>{running===entry.action+entry.mode?'Выполняется…':entry.label}</button>}
 return <Panel title="Network Repair · Управление сетевыми профилями и приложениями">
  <p className="footnote">Операции выполняет .NET-служба Agent/Audit. Отказ любого внешнего инструмента не должен останавливать приложение.
   Установка пакетов и изменения DNS/hosts разрешаются администратором отдельно.</p>
  <button type="button" className="btn secondary" onClick={()=>void refresh()} disabled={!!running}>Обновить состояние</button>
  <ErrorBanner text={error}/>
  {data&&<div className="info-grid">
   <div><small>Модуль</small><strong>{data.enabled?'Включён':'Отключён'}</strong></div>
   <div><small>ОС</small><strong>{data.platform}</strong></div>
   <div><small>Zapret</small><strong>{data.active?'Активен':data.installed?'Установлен':'Не установлен'}</strong></div>
   <div><small>WARP</small><strong>{data.warpInstalled?'Установлен':'Не установлен'}</strong></div>
   <div><small>Системные изменения</small><strong>{data.extras?.systemChangesAllowed?'Разрешены':'Запрещены'}</strong></div>
   <div><small>Установка ПО</small><strong>{data.extras?.packageChangesAllowed?'Разрешена':'Запрещена'}</strong></div>
   <div><small>Watchdog</small><strong>{data.extras?.watchdogEnabled?'Включён':'Выключен'}</strong></div>
  </div>}
  {data?.lastError&&<p className="footnote">Последняя ошибка: {data.lastError}</p>}
  <div className="network-repair-tabs" role="tablist" aria-label="Раздел Network Repair">
   {sections.map((section,index)=><button key={section.title} type="button" role="tab" aria-selected={tab===index}
    className={'btn '+(tab===index?'':'secondary')} onClick={()=>setTab(index)}>{section.title}</button>)}
   <button type="button" role="tab" aria-selected={tab===sections.length} className={'btn '+(tab===sections.length?'':'secondary')}
    onClick={()=>setTab(sections.length)}>Приложения</button>
  </div>
  {tab<sections.length&&<div className="network-repair-section"><h3>{sections[tab].title}</h3>
   <p className="footnote">{sections[tab].description}</p>
   {tab===0&&<><label className="footnote" htmlFor="network-strategy">Стратегия</label>
    <select id="network-strategy" className="input" value={strategy} onChange={e=>setStrategy(e.target.value)}>
     <option value="">Сохранённая лучшая / general.bat</option>
     {data?.strategies.map(s=><option key={s} value={s}>{s} {data?.scores[s]===undefined?'':'('+data.scores[s]+')'}</option>)}
    </select>
    <label className="footnote" htmlFor="repair-domains">Домены тестирования, по одному на строку</label>
    <textarea id="repair-domains" className="input" rows={5} value={domains} onChange={e=>setDomains(e.target.value)}/>
    {actionButton({action:'test-domains-set',label:'Сохранить домены'})}</>}
   <div className="network-repair-actions">{sections[tab].actions.map(actionButton)}</div>
  </div>}
  {tab===sections.length&&<div className="network-repair-section"><h3>Управление приложениями</h3>
   <p className="footnote">Операции осуществляются через зарегистрированные пакеты winget. Путь запуска возвращается клиенту: служба не открывает окна в чужом сеансе.</p>
   <div className="network-repair-package-list">{Object.entries(applicationNames).map(([id,name])=><div className="network-repair-package" key={id}>
    <strong>{name}</strong><span>{data?.extras?.packages?.[id]?.installed?'Найдено':'Не найдено / нестандартный путь'}</span>
    <div className="network-repair-actions">
     {actionButton({action:'package-install-'+id,label:'Установить'})}
     {actionButton({action:'package-update-'+id,label:'Обновить'})}
     {actionButton({action:'package-uninstall-'+id,label:'Удалить',danger:true})}
     {actionButton({action:'package-launch-'+id,label:'Путь запуска'})}
     {actionButton({action:'package-autostart-'+id,label:'Автозапуск ВКЛ',mode:'on'})}
     {actionButton({action:'package-autostart-'+id,label:'Автозапуск ВЫКЛ',mode:'off'})}
    </div></div>)}</div>
  </div>}
  {output!==null&&<pre className="output">{JSON.stringify(output,null,2)}</pre>}
  <p className="footnote">Резервные копии DNS/hosts и карантин PUBG сохраняются локально. Не закрывайте доступ к ПК во время изменения сетевых маршрутов.
   В Linux функции Windows Network Repair недоступны, остальные сервисы MysticEgo продолжают работать.</p>
 </Panel>;
}
