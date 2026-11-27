import {Activity,ArrowUpRight,BrainCircuit,Container,Database,Mic,Radio,ServerCog,ShieldCheck,RefreshCw} from 'lucide-react';
import {Link,NavLink} from 'react-router-dom';
import {useState} from 'react';
import {useApp,useRemote} from '../app/context';
import {ServiceMaintenance,ReportDownload} from './ServiceMaintenance';
import {ErrorBanner,Panel} from '../components/primitives';
import {InfrastructureSetup} from './InfrastructureSetup';
import {labels,names,useMaintenance,workerMessage,type Maintenance} from './MaintenanceStatus';
import './infrastructure-workspace.css';

export type InfrastructureSection='services'|'ai'|'database'|'metrics';
const sections={
  services:{title:'Сервисы',number:'01',icon:Container,description:'Локальные системные службы, Docker и механизм самостоятельного обслуживания.',components:['orchestra','docker','worker'],actions:['docker'],initial:'docker',actionTitle:'Установка Docker и Compose'},
  ai:{title:'AI и речь',number:'02',icon:BrainCircuit,description:'Вычислительные модели, LM Studio и локальное распознавание голосовых сообщений.',components:['lmstudio','voice'],actions:['lmstudio','model-connect'],initial:'lmstudio',actionTitle:'Развёртывание и подключение моделей'},
  database:{title:'Базы данных',number:'03',icon:Database,description:'Текущее хранилище, PostgreSQL и подготовка безопасной миграции.',components:['database','postgres-service'],actions:['postgres','postgres-native','prepare-postgres','auth-postgres'],initial:'postgres-native',actionTitle:'Установка и подключение PostgreSQL'},
  metrics:{title:'Мониторинг',number:'04',icon:Radio,description:'Источники метрик, Prometheus и Grafana. Настройка сбора телеметрии отдельно от диагностики.',components:[],actions:['monitoring','metrics-source'],initial:'monitoring',actionTitle:'Настройка сбора метрик'}
} satisfies Record<InfrastructureSection,{title:string;number:string;icon:typeof Activity;description:string;components:string[];actions:string[];initial:string;actionTitle:string}>;
const icons:Record<string,typeof Activity>={orchestra:ServerCog,docker:Container,worker:ShieldCheck,lmstudio:BrainCircuit,voice:Mic,database:Database,'postgres-service':Database};
type Component=Maintenance['components'][number];

function StatusCard({component,name}:{component?:Component;name:string}){
  const Icon=icons[name]??Activity;
  const status=component?.state??'checking';
  return <article className="infra-status-card" data-state={status}>
    <div className="infra-card-top"><span className="infra-status-icon"><Icon size={23}/></span><span className="infra-state"><i/>{labels[status]??status}</span></div>
    <h3>{names[name]??name}</h3>
    <p>{component?.message??'Пока нет подтверждённого замера этого компонента.'}</p>
    <small>{component?'Проверено '+new Date(component.checkedAt).toLocaleString('ru-RU'):'Ожидание отчёта сервера'}</small>
    <ReportDownload scope={name}/>
  </article>;
}

export function InfrastructureWorkspace({section}:{section:InfrastructureSection}){
  const page=sections[section];
  const {api}=useApp();
  const state=useMaintenance();
  const data=state.value;
  const status=data?.components??[];
  const worker=status.find(c=>c.name==='worker');
  const Icon=page.icon;
  return <div className="infra-workspace" data-infra-section={section}>
    <header className="infra-hero">
      <div className="infra-hero-copy"><span className="infra-eyebrow">Orchestra / Инфраструктура / {page.number}</span><h1>{page.title}</h1><p>{page.description}</p></div>
      <Icon className="infra-hero-icon" size={72} strokeWidth={1} aria-hidden="true"/>
    </header>
    <nav className="infra-tabs" aria-label="Разделы инфраструктуры">{(Object.keys(sections) as InfrastructureSection[]).map(id=>{const item=sections[id];return <NavLink key={id} to={'/infrastructure/'+id}><item.icon size={17}/>{item.title}</NavLink>})}</nav>
    <ErrorBanner text={state.error}/>
    {section==='services'&&<div className="infra-worker-banner" data-state={data?.installed&&!worker?'available':'checking'}>
      <ShieldCheck size={24}/><div><strong>{data?.installed&&!worker?'Служба обслуживания передаёт данные':worker?'Служба требует внимания':'Состояние службы обслуживания'}</strong>
        <p>{worker?.message??(data?.installed?'Отчёт получен '+(data.source==='host-socket'?'через локальный сокет':'из каталога VPS')+'.':data?workerMessage(data):'Проверяем доступность службы обслуживания…')}</p>
      </div><button className="btn secondary" onClick={state.reload} disabled={state.loading}><RefreshCw size={15}/> Проверить</button>
    </div>}
    {!!page.components.length&&<div className="infra-status-grid">{page.components.filter(name=>name!=='worker'||!!worker).map(name=><StatusCard key={name} name={name} component={status.find(c=>c.name===name)}/>)}</div>}
    {section==='metrics'&&<div className="infra-status-grid"><article className="infra-status-card"><Radio size={24}/><h3>Prometheus</h3><p>Сбор метрик Orchestra. Локальный endpoint на VPS: порт 9090.</p></article><article className="infra-status-card"><Activity size={24}/><h3>Grafana</h3><p>Панели наблюдения. Локальный endpoint на VPS: порт 3000.</p></article></div>}
    <div className="infra-actions-grid">
      <InfrastructureSetup key={section} initial={page.initial} allowed={page.actions} title={page.actionTitle}/>
      <Panel title={section==='services'?'Автоматическое обслуживание':section==='ai'?'Вычислительный контур':section==='database'?'Безопасность данных':'Просмотр диагностики'}>
        {section==='services'&&<ServiceMaintenance/>}
        {section==='ai'&&<><p>Распознавание речи устанавливается автоматически при достаточных ресурсах VPS. Явно настроенный внешний сервис сохраняется.</p><Link className="infra-link" to="/assistant?tab=models">Вычислительные узлы и тест модели <ArrowUpRight size={16}/></Link></>}
        {section==='database'&&<><p>Существующая база и её учётные данные сохраняются. Перенос SQLite в PostgreSQL доступен только как автономная подготовка; переключение рабочей базы пока не автоматизировано.</p><p className="footnote">Не создавайте пустую замену действующей базе. Требуется проверяемая резервная копия и отдельное подтверждение.</p></>}
        {section==='metrics'&&<><p>Здесь настраиваются источники и системы сбора. Текущая нагрузка, графики ресурсов и состояние сетевых связей отображаются отдельно.</p><Link className="infra-link" to="/diagnostics">Открыть диагностику <ArrowUpRight size={16}/></Link></>}
      </Panel>
    </div>
  </div>;
}
