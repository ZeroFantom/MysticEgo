import {useEffect} from 'react';
import {ThemeToggle} from './Settings';
import {CinematicPreview} from '../app/CinematicPreview';
import {
  ArrowDownRight, ArrowUpRight, Box, Building2, Compass, Github, Send, Users,
  Server, ShieldCheck, Workflow,
} from 'lucide-react';
import {StudioEconomics} from './StudioEconomics';
import editorialImage from './media/club-editorial.webp';
import './studio.css';

const repository = 'https://github.com/ZeroFantom/MysticEgo';

const roles = [
  {
    title: 'Backend и инфраструктура',
    description: 'Развивать C#-сервисы, API узлов, диагностику и сценарии развёртывания.',
  },
  {
    title: 'Интерфейсы и тестирование',
    description: 'Улучшать веб-панели и настольные приложения. Проверять сценарии на Windows и Linux.',
  },
  {
    title: 'Игровые системы и мир',
    description: 'Проектировать правила мира, поведение персонажей и цепочки событий для прототипа.',
  },
];

const partnerships = [
  {
    title: 'Пилот на площадке',
    description: 'Ищем клубы и команды, готовые проверить MysticEgo на своих рабочих станциях и дать обратную связь.',
  },
  {
    title: 'Реклама и партнёрство',
    description: 'Обсудим спонсорство технических материалов, совместные демонстрации и интеграции с оборудованием или сервисами.',
  },
  {
    title: 'Инвестиции',
    description: 'Финансирование помогло бы расширить тестирование на реальных площадках, улучшить инструменты развёртывания и собрать игровой прототип Ether And Chaos.',
  },
];

const stack = ['C#', '.NET 10', 'ASP.NET Core', 'SignalR', 'React', 'TypeScript', 'Electron', 'Linux / systemd', 'GitHub Actions', 'SSH', 'MCP', 'EF Core', 'PostgreSQL', 'MS SQL', 'Docker', 'WPF', 'Avalonia'];

function discussion(topic: string) {
  return repository + '/issues/new?' + new URLSearchParams({title: topic});
}

export function LicenseLink({className}: {className?: string}) {
  return <a className={className} href={repository + '/blob/main/LICENSE'} target="_blank" rel="noreferrer">
    PolyForm Noncommercial 1.0.0
  </a>;
}

function PublicNavigation({page,scope}: {page: 'studio' | 'developer';scope:string}) {
  useEffect(() => {
    const previous = document.title;
    document.title = page === 'studio' ? 'Студия MysticEgo' : 'Дмитрий Колыхалов · MysticEgo';
    return () => {
      document.title = previous;
    };
  }, [page]);

  return <header className="studio-nav">
    <a className="studio-brand" href="/studio"><Box size={21} aria-hidden="true"/>MysticEgo</a>
    <nav aria-label="Страницы MysticEgo">
      <a href="/studio" aria-current={page === 'studio' ? 'page' : undefined}>Студия</a>
      <a href="/developer" aria-current={page === 'developer' ? 'page' : undefined}>Разработчик</a>
      <a href="/">Вход в панель <ArrowUpRight size={15} aria-hidden="true"/></a><ThemeToggle scope={scope}/>
    </nav>
  </header>;
}

function PublicFooter() {
  return <footer className="studio-footer">
    <a href={repository}><Github size={17} aria-hidden="true"/>Исходный код</a>
    <span>Лицензия кода <LicenseLink/></span>
  </footer>;
}

export function StudioPage({scope=''}:{scope?:string}={}) {
  return <div className="studio-site studio-landing" data-landing="studio">
    <PublicNavigation page="studio" scope={scope}/>
    <main className="studio-content">
      <section className="studio-hero studio-hero-cover landing-hero" aria-labelledby="studio-title">
        <div className="landing-hero-copy">
          <p className="landing-kicker">MysticEgo / Независимая студия</p>
          <h1 id="studio-title">Студия<br/>MysticEgo</h1>
          <p>Инструменты для клубов, где десятки компьютеров должны работать как одна система. И отдельный игровой замысел о мире, который помнит решения своих жителей.</p>
          <div className="studio-actions">
            <a className="studio-button primary" href="#join">Присоединиться к проекту <ArrowDownRight size={17} aria-hidden="true"/></a>
            <a className="studio-button" href="#partners">Предложить партнёрство</a>
          </div>
          <a className="landing-scroll" href="#projects">Исследовать проекты <ArrowDownRight size={19} aria-hidden="true"/></a>
        </div>
        <span className="landing-index" aria-hidden="true">01 / 02</span>
      </section>

      <section id="projects" className="studio-section landing-projects" aria-labelledby="studio-direction">
        <h2 id="studio-direction">Над чем работаем</h2>
        <div className="studio-grid two landing-project-grid">
          <article className="studio-card landing-project">
            <img className="landing-project-image" src={editorialImage} alt="Рабочие станции компьютерного клуба" loading="lazy" decoding="async"/>
            <div className="landing-project-copy">
            <span className="landing-kicker">01 / Инфраструктура</span>
            <Server size={24} aria-hidden="true"/>
            <h3>Инфраструктура MysticEgo</h3>
            <p>Orchestra, организации Global, площадки Local и агенты на ПК. Ближайшая цель — сделать настройку, обновление и диагностику устойчивыми при развёртывании на нескольких площадках.</p>
            <a href={repository}>Исходный код <ArrowUpRight size={16} aria-hidden="true"/></a>
            </div>
          </article>
          <article className="studio-card landing-project">
            <div className="landing-world-image" aria-hidden="true"/>
            <div className="landing-project-copy">
            <span className="landing-kicker">02 / Игровой замысел</span>
            <Compass size={24} aria-hidden="true"/>
            <h3>Ether And Chaos</h3>
            <p>Концепция живого мира, где игроки и самостоятельные персонажи участвуют в общих событиях. Решения порождают локальные истории, а открытия развивают цивилизации в рамках законов мира.</p>
            <p>Одна из идей — непрерывная история мира с возможностью его перерождения после гибели.</p>
            </div>
          </article>
        </div>
      </section>

      <section className="studio-section landing-scene" aria-labelledby="scene-title">
        <div className="studio-section-heading">
          <p className="landing-kicker">Атмосфера / Оформление</p>
          <h2 id="scene-title">Мир MysticEgo</h2>
          <p>Живой пейзаж, свет и движение. Воспроизведение по желанию.</p>
        </div>
        <CinematicPreview/>
      </section>

      <section id="architecture" className="studio-section landing-band" aria-labelledby="studio-workflow">
        <div className="studio-section-heading">
          <h2 id="studio-workflow">От оператора до станции</h2>
          <p>Orchestra отвечает за установки и обновления. Global объединяет площадки, Local знает оборудование каждого клуба, Agent сообщает о состоянии конкретного ПК.</p>
        </div>
        <ol className="studio-steps">
          <li><strong>Orchestra</strong><span>Реестр, версии и состояние развёртывания</span></li>
          <li><strong>Global</strong><span>Доступ и управление сетью площадок</span></li>
          <li><strong>Local</strong><span>Регистрация и работа внутри площадки</span></li>
          <li><strong>Agent</strong><span>Диагностика и разрешённые операции на ПК</span></li>
        </ol>
        <p>Приоритет пилота — измерить время подключения станции, восстановления после сбоя и обновления нескольких площадок. Для Ether And Chaos ближайшая цель — небольшой игровой прототип, где событие возникает из действий участников, а его последствия сохраняются.</p>
      </section>

      <StudioEconomics/>

      <section id="join" className="studio-section" aria-labelledby="studio-recruitment">
        <h2 id="studio-recruitment">Присоединиться к разработке</h2>
        <p>Ищем участников для задач проекта и раннего прототипирования.</p>
        <div className="studio-grid">
          {roles.map(role => <article className="studio-card" key={role.title}>
            <h3>{role.title}</h3>
            <p>{role.description}</p>
          </article>)}
        </div>
        <div className="studio-actions">
          <a className="studio-button primary" href={discussion('Участие в разработке MysticEgo')}>Предложить участие <ArrowUpRight size={16} aria-hidden="true"/></a>
          <a className="studio-button" href={repository + '/blob/main/CONTRIBUTING.md'}>Как внести вклад</a>
        </div>
        <p className="studio-note">Тема в GitHub Issues будет публичной. Достаточно указать направление и ссылку на открытое портфолио.</p>
      </section>

      <section id="partners" className="studio-section" aria-labelledby="studio-partners">
        <h2 id="studio-partners">Партнёрство и финансирование</h2>
        <div className="studio-grid">
          {partnerships.map(item => <article className="studio-card" key={item.title}>
            <Building2 size={21} aria-hidden="true"/>
            <h3>{item.title}</h3>
            <p>{item.description}</p>
            <a href={discussion(item.title + ' · MysticEgo')}>Начать обсуждение <ArrowUpRight size={15} aria-hidden="true"/></a>
          </article>)}
        </div>
        <p className="studio-note">Для первого сообщения достаточно описать формат сотрудничества. Для коммерческих разрешений или непубличного обсуждения напишите <a href="mailto:zero.dark.lie@gmail.com">DarkLie:Zero</a>.</p>
      </section>
    </main>
    <PublicFooter/>
  </div>;
}

export function DeveloperPage({scope=''}:{scope?:string}={}) {
  return <div className="studio-site studio-landing" data-landing="developer">
    <PublicNavigation page="developer" scope={scope}/>
    <main className="studio-content developer-content">
      <section className="studio-hero developer-hero landing-hero" aria-labelledby="developer-title">
        <div className="landing-hero-copy">
          <p className="studio-role landing-kicker">Zero Fantom / C# · .NET</p>
          <h1 id="developer-title">Колыхалов<br/>Дмитрий<br/>Антонович</h1>
          <p className="developer-meta">22 года · Разработка серверных и настольных приложений</p>
          <p>Разработчик MysticEgo. Проектирую сервисы для управления парком компьютеров и связываю серверную архитектуру с интерфейсами для администраторов.</p>
          <div className="studio-actions">
            <a className="studio-button primary" href="https://github.com/ZeroFantom"><Github size={18} aria-hidden="true"/>Профиль GitHub</a>
            <a className="studio-button" href={repository}>Код MysticEgo <ArrowUpRight size={17} aria-hidden="true"/></a>
          </div>
          <a className="landing-scroll" href="#experience">Смотреть проекты <ArrowDownRight size={19} aria-hidden="true"/></a>
        </div>
        <span className="landing-index" aria-hidden="true">Zero / Developer</span>
      </section>
      <section id="developer-contact" className="studio-section landing-contact" aria-labelledby="contact-title">
        <div className="studio-section-heading">
          <p className="landing-kicker">Контакты / Стек</p>
          <h2 id="contact-title">На связи</h2>
          <div className="developer-links">
            <a href="https://t.me/ZeroPhantomLie"><Send size={18} aria-hidden="true"/><span><strong>Telegram</strong><small>@ZeroPhantomLie</small></span><ArrowUpRight size={16} aria-hidden="true"/></a>
            <a href="https://vk.com/id154351348"><Users size={18} aria-hidden="true"/><span><strong>ВКонтакте</strong><small>Профиль</small></span><ArrowUpRight size={16} aria-hidden="true"/></a>
          </div>
        </div>
        <div className="landing-tech">
          <h2>Технический стек</h2>
          <ul className="studio-stack">{stack.map(tech => <li key={tech}>{tech}</li>)}</ul>
        </div>
      </section>
      <section id="experience" className="studio-section landing-band landing-experience">
        <p className="landing-kicker">Практика / Разработка</p>
        <h2>Проекты и опыт</h2>
        <div className="studio-grid">
          <article className="studio-card">
            <Workflow size={24} aria-hidden="true"/>
            <h3>Распределённая система управления ПК</h3>
            <p>Спроектировал схему с агентами на рабочих станциях, локальным API площадки и глобальным API. Global агрегирует данные и при недоступности Local может обратиться к агентам напрямую.</p>
          </article>
          <article className="studio-card">
            <Server size={24} aria-hidden="true"/>
            <h3>MysticEgo CloudDesk</h3>
            <p>Настольный инструмент на C# и Electron для развёртывания, обновления и диагностики серверов. Работал с SSH, интеграцией GitHub Releases и журналами операций.</p>
          </article>
          <article className="studio-card">
            <ShieldCheck size={24} aria-hidden="true"/>
            <h3>Диагностика рабочих станций</h3>
            <p>Работал с проверкой устройств, журналированием, интеграциями OpenRGB и Logitech G HUB и диагностическими командами MCP для Audit.</p>
          </article>
        </div>
      </section>
      <section className="studio-section studio-ideas">
        <h2>Идеи, которые хочу развивать</h2>
        <p>Управление инфраструктурой с прозрачными правами доступа и понятными состояниями операций. В играх — живой мир с самостоятельными персонажами, местными событиями и последствиями, которые сохраняются после выхода игрока.</p>
        <a href="/studio">Подробнее о проектах студии <ArrowUpRight size={16} aria-hidden="true"/></a>
      </section>
    </main>
    <PublicFooter/>
  </div>;
}
