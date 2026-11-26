import {useEffect} from 'react';
import {
  ArrowDownRight, ArrowUpRight, Box, Braces, Building2, Compass, Github,
  Server, ShieldCheck, Workflow,
} from 'lucide-react';
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

const stack = ['C#', '.NET 10', 'ASP.NET Core', 'SignalR', 'React', 'TypeScript', 'Electron', 'Linux / systemd', 'GitHub Actions', 'SSH', 'MCP'];

function discussion(topic: string) {
  return repository + '/issues/new?' + new URLSearchParams({title: topic});
}

export function LicenseLink({className}: {className?: string}) {
  return <a className={className} href={repository + '/blob/main/LICENSE'} target="_blank" rel="noreferrer">
    Apache 2.0
  </a>;
}

function PublicNavigation({page}: {page: 'studio' | 'developer'}) {
  useEffect(() => {
    const previous = document.title;
    document.title = page === 'studio' ? 'Студия MysticEgo' : 'Zero Fantom · MysticEgo';
    return () => {
      document.title = previous;
    };
  }, [page]);

  return <header className="studio-nav">
    <a className="studio-brand" href="/studio"><Box size={21} aria-hidden="true"/>MysticEgo</a>
    <nav aria-label="Страницы MysticEgo">
      <a href="/studio" aria-current={page === 'studio' ? 'page' : undefined}>Студия</a>
      <a href="/developer" aria-current={page === 'developer' ? 'page' : undefined}>Разработчик</a>
      <a href="/">Вход в панель <ArrowUpRight size={15} aria-hidden="true"/></a>
    </nav>
  </header>;
}

function PublicFooter() {
  return <footer className="studio-footer">
    <a href={repository}><Github size={17} aria-hidden="true"/>Исходный код</a>
    <span>Лицензия кода <LicenseLink/></span>
  </footer>;
}

export function StudioPage() {
  return <div className="studio-site">
    <PublicNavigation page="studio"/>
    <main className="studio-content">
      <section className="studio-hero">
        <div>
          <h1>Студия MysticEgo</h1>
          <p>Создаём открытые инструменты управления рабочими станциями. Параллельно проектируем фэнтезийный мир, в котором история складывается из решений его жителей.</p>
          <div className="studio-actions">
            <a className="studio-button primary" href="#join">Присоединиться к проекту <ArrowDownRight size={17} aria-hidden="true"/></a>
            <a className="studio-button" href="#partners">Предложить партнёрство</a>
          </div>
        </div>
        <aside className="studio-hero-aside">
          <Workflow size={34} aria-hidden="true"/>
          <h2>MysticEgo в разработке</h2>
          <p>От панели Orchestra до конкретной рабочей станции. Открытый код, диагностика и проверяемые обновления.</p>
          <a href={repository + '/releases'}>Смотреть сборки <ArrowUpRight size={15} aria-hidden="true"/></a>
        </aside>
      </section>

      <section className="studio-section" aria-labelledby="studio-direction">
        <h2 id="studio-direction">Над чем работаем</h2>
        <div className="studio-grid two">
          <article className="studio-card">
            <Server size={24} aria-hidden="true"/>
            <h3>Инфраструктура MysticEgo</h3>
            <p>Orchestra, организации Global, площадки Local и агенты на ПК. Ближайшая цель — сделать настройку, обновление и диагностику устойчивыми при развёртывании на нескольких площадках.</p>
          </article>
          <article className="studio-card">
            <Compass size={24} aria-hidden="true"/>
            <h3>Ether And Chaos</h3>
            <p>Концепция живого мира, где игроки и самостоятельные персонажи участвуют в общих событиях. Решения порождают локальные истории, а открытия развивают цивилизации в рамках законов мира.</p>
            <p>Одна из идей — непрерывная история мира с возможностью его перерождения после гибели.</p>
          </article>
        </div>
      </section>

      <section id="join" className="studio-section" aria-labelledby="studio-recruitment">
        <h2 id="studio-recruitment">Присоединиться к разработке</h2>
        <p>Ищем участников для задач открытого проекта и раннего прототипирования.</p>
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
        <p className="studio-note">Заявка на GitHub будет публичной. Достаточно указать направление и ссылку на открытое портфолио.</p>
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
        <p className="studio-note">Для первого сообщения достаточно описать формат сотрудничества. Условия обсуждаются отдельно.</p>
      </section>
    </main>
    <PublicFooter/>
  </div>;
}

export function DeveloperPage() {
  return <div className="studio-site">
    <PublicNavigation page="developer"/>
    <main className="studio-content">
      <section className="studio-hero">
        <div>
          <p className="studio-role">C# / .NET · распределённые приложения</p>
          <h1>Zero Fantom</h1>
          <p>Разработчик MysticEgo. Проектирую сервисы для управления парком компьютеров и связываю серверную архитектуру с интерфейсами для администраторов.</p>
          <div className="studio-actions">
            <a className="studio-button primary" href="https://github.com/ZeroFantom"><Github size={18} aria-hidden="true"/>Профиль GitHub</a>
            <a className="studio-button" href={repository}>Код MysticEgo <ArrowUpRight size={17} aria-hidden="true"/></a>
          </div>
        </div>
        <aside className="studio-hero-aside">
          <Braces size={34} aria-hidden="true"/>
          <h2>Технический стек</h2>
          <ul className="studio-stack">{stack.map(tech => <li key={tech}>{tech}</li>)}</ul>
        </aside>
      </section>
      <section className="studio-section">
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
