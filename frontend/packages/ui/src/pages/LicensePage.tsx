import {useLocale} from '../app/presentation';
import {useEffect} from 'react';
import ReactMarkdown from 'react-markdown';
import {ArrowUpRight, BookOpen, Download, Github, Scale, ShieldCheck} from 'lucide-react';
import license from '../../../../../LICENSE?raw';
import {ThemeToggle} from './Settings';
import './studio.css';
import './license.css';

const repository='https://github.com/ZeroFantom/MysticEgo';
const official='https://polyformproject.org/licenses/noncommercial/1.0.0';
const slug=(title:string)=>title.toLowerCase().trim().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'');
const sections=Array.from(license.matchAll(/^## (.+)$/gm),match=>({title:match[1].trim(),id:slug(match[1])}));
const summary=[
 {title:'Личное использование',text:'Некоммерческое изучение, эксперименты, тестирование, хобби и личные проекты.'},
 {title:'Изменение и распространение',text:'Разрешено в пределах условий лицензии. При распространении необходимо сохранить текст условий или ссылку на него и обязательные уведомления.'},
 {title:'Коммерческое применение',text:'Если предполагаемое применение не покрывается лицензией, требуется отдельное письменное разрешение правообладателя.'},
];
function downloadLicense(){
 const url=URL.createObjectURL(new Blob([license],{type:'text/plain;charset=utf-8'}));
 const link=document.createElement('a');link.href=url;link.download='MysticEgo-LICENSE.txt';link.click();
 window.setTimeout(()=>URL.revokeObjectURL(url),1000);
}
export function LicensePage({scope=''}:{scope?:string}){
 const t=useLocale();
 useEffect(()=>{const old=document.title;document.title='Лицензия · MysticEgo';return()=>{document.title=old}},[]);
 return <div className="studio-site studio-landing license-site" data-landing="license">
  <header className="studio-nav">
   <a className="studio-brand" href="/studio"><Scale size={21} aria-hidden="true"/>MysticEgo</a>
   <nav aria-label="Страницы MysticEgo">
    <a href="/studio">{t("Студия")}</a><a href="/developer">{t("Разработчик")}</a><a href="/license" aria-current="page">{t("Лицензия")}</a>
    <a href="/">{t("Вход в панель")} <ArrowUpRight size={15} aria-hidden="true"/></a><ThemeToggle scope={scope}/>
   </nav>
  </header>
  <main className="studio-content license-content">
   <section className="license-hero" aria-labelledby="license-title">
    <div className="license-hero-copy">
     <p className="landing-kicker">{t("MysticEgo / Правовая информация")}</p>
     <h1 id="license-title">{t("Лицензия")}</h1>
     <h2>PolyForm Noncommercial 1.0.0</h2>
     <p>Условия использования исходного кода текущей редакции MysticEgo, принадлежащего DarkLie:Zero. Исходный код доступен, но не все способы коммерческого использования разрешены автоматически.</p>
     <div className="studio-actions">
      <a className="studio-button primary" href="#license-text"><BookOpen size={17} aria-hidden="true"/>{t("Читать лицензию")}</a>
      <button type="button" className="studio-button" onClick={downloadLicense}><Download size={17} aria-hidden="true"/>{t("Скачать текст")}</button>
      <a className="studio-button" href={repository+'/blob/main/LICENSE'} target="_blank" rel="noreferrer"><Github size={17} aria-hidden="true"/>GitHub</a>
     </div>
    </div>
    <div className="license-emblem" aria-hidden="true"><Scale size={80} strokeWidth={1}/><span>POLYFORM</span><small>NONCOMMERCIAL / 1.0.0</small></div>
   </section>
   <section className="studio-section" aria-labelledby="license-summary">
    <h2 id="license-summary">{t("Основные условия")}</h2>
    <div className="studio-grid">{summary.map(item=><article key={item.title} className="studio-card"><ShieldCheck size={24} aria-hidden="true"/><h3>{t(item.title)}</h3><p>{item.text}</p></article>)}</div>
    <p className="studio-note">Это краткое пояснение на русском, не юридический перевод и не замена оригинальным условиям. Лицензия предусматривает также разрешённые категории некоммерческих организаций, обязательные уведомления, ограничения ответственности и другие положения.</p>
   </section>
   <section className="studio-section license-commercial" aria-labelledby="license-commercial-title">
    <h2 id="license-commercial-title">{t("Коммерческое использование")}</h2>
    <p>Для применения, не покрываемого текущими условиями, запросите письменное разрешение. Укажите организацию, версию MysticEgo, способ использования и предполагаемый масштаб. Объём прав, срок и стоимость согласуются отдельно.</p>
    <div className="studio-actions"><a className="studio-button primary" href="mailto:zero.dark.lie@gmail.com?subject=MysticEgo%20-%20commercial%20license">{t("Запросить разрешение")}</a><a className="studio-button" href={repository+'/blob/main/docs/legal/commercial.md'} target="_blank" rel="noreferrer">{t("Условия сотрудничества")} <ArrowUpRight size={15} aria-hidden="true"/></a></div>
   </section>
   <section className="studio-section" aria-labelledby="mobile-use-title"><h2 id="mobile-use-title">Использование мобильного приложения</h2><p>При первой инициализации MysticEgo Mobile показывает технический профиль устройства и назначение разрешений. Условия сервиса и уведомление об обработке данных принимаются отдельно при регистрации. Разрешения Android выдаёт и отзывает пользователь; лицензия исходного кода не является согласием на сбор данных или управление устройством.</p><p>Камера используется для добровольного сканирования QR входа и контактов. QR не содержит паролей или токенов. Приложение не требует доступа к контактам, SMS, геопозиции, микрофону или всем файлам для обычного входа.</p></section>
   <section id="license-text" className="studio-section" aria-labelledby="license-text-title">
    <h2 id="license-text-title">{t("Полный оригинальный текст")}</h2>
    <p>Английский текст из файла LICENSE этой сборки. <a href={official} target="_blank" rel="noreferrer">Официальная публикация PolyForm <ArrowUpRight size={14} aria-hidden="true"/></a></p>
    <div className="license-document-layout">
     <nav className="license-toc" aria-label="Содержание оригинальной лицензии">
      <strong>{t("Содержание")}</strong>{sections.map(item=><a key={item.id} href={'#'+item.id}>{item.title}</a>)}
     </nav>
     <article className="license-document" lang="en">
      <ReactMarkdown components={{
       h1:({children})=><h3 className="license-document-title">{children}</h3>,
       h2:({children})=><h4 id={slug(String(children))}>{children}</h4>
      }}>{license}</ReactMarkdown>
     </article>
    </div>
   </section>
   <section className="studio-section license-previous" aria-labelledby="license-previous-title">
    <h2 id="license-previous-title">{t("Предыдущие версии и сторонние компоненты")}</h2>
    <p>Ранее полученные версии под Apache 2.0 сохраняют условия этой лицензии. Сторонние компоненты распространяются на условиях собственных лицензий. Эта страница не изменяет их права.</p>
    <a href={repository+'/blob/main/docs/legal/LICENSE-APACHE-2.0.txt'} target="_blank" rel="noreferrer">Apache 2.0 для применимых версий <ArrowUpRight size={15} aria-hidden="true"/></a>
   </section>
  </main>
  <footer className="studio-footer"><a href={repository}><Github size={17} aria-hidden="true"/>{t("Исходный код")}</a><span>DarkLie:Zero · MysticEgo</span><a href="/studio">{t("Студия")}</a></footer>
 </div>;
}
