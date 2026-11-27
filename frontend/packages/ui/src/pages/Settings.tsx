import {useEffect,useState} from 'react';
import {Check,ChevronDown,Globe2,Moon,Palette,Sparkles,Sun} from 'lucide-react';
import {useApp} from '../app/context';
import {preferenceKey} from '../app/panelPreferences';
import {changePresentation,fonts,languages,syncAppearance,useLocale,usePresentation} from '../app/presentation';
import {Header,Panel} from '../components/primitives';

import {appearanceDefaults as defaults,mergeAppearance,palettes,type Style} from '../app/appearancePreferences';
export type {Style} from '../app/appearancePreferences';

const volatileStyles=new Map<string,Style>();
function current(scope:string):Style{
 if(volatileStyles.has(scope))return volatileStyles.get(scope)!;
 try{return mergeAppearance(defaults,JSON.parse(localStorage.getItem(preferenceKey(scope,'appearance'))??'{}')??{})}
 catch{return {...defaults}}
}
function useAppearance(scope:string){
 const [style,setStyle]=useState(()=>current(scope));
 useEffect(()=>{
  const refresh=()=>setStyle(current(scope));
  refresh();
  const media=matchMedia('(prefers-color-scheme:light)');
  window.addEventListener('mystic-appearance',refresh);window.addEventListener('storage',refresh);media.addEventListener('change',refresh);
  return()=>{window.removeEventListener('mystic-appearance',refresh);window.removeEventListener('storage',refresh);media.removeEventListener('change',refresh)};
 },[scope]);
 return [style,setStyle] as const;
}
export function applyStyle(style:Style){
 const root=document.documentElement;
 root.dataset.theme=style.theme==='system'?(matchMedia('(prefers-color-scheme:light)').matches?'light':'dark'):style.theme;
 root.dataset.interface=style.interfaceStyle;root.dataset.transition=style.transition;root.dataset.uiMotion=style.uiMotion;
 root.dataset.palette=style.palette;
 root.dataset.ambient=String(style.ambient&&style.uiMotion==='normal'&&style.interfaceStyle==='immersive');
 const effects=style.uiMotion==='normal'&&style.interfaceStyle==='immersive';root.dataset.neonWave=String(style.wave&&effects);root.dataset.neonGrid=String(style.grid&&effects);root.dataset.neonBorder=String(style.border&&effects);
 root.dataset.density=style.density;root.dataset.textSize=style.size;
 const palette=palettes.find(item=>item.id===style.palette);
 const safe=(value:string,fallback:string)=>/^#[\da-f]{6}$/i.test(value)?value:fallback;
 const accent=palette?.colors[0]??safe(style.primary,defaults.primary);
 const alt=palette?.colors[2]??safe(style.secondary,defaults.secondary);
 root.style.setProperty('--accent',root.dataset.theme==='light'?`color-mix(in srgb,${accent} 54%,#39206e)`:accent);
 root.style.setProperty('--accent-deep',palette?.colors[1]??'color-mix(in srgb,var(--accent) 62%,var(--canvas))');
 root.style.setProperty('--alt',alt);
 root.style.setProperty('--accent-light','color-mix(in srgb,var(--accent) 70%,var(--ink))');
 root.style.setProperty('--neon-strength',String(Math.max(.05,Math.min(1,style.intensity/100))));
}
function save(scope:string,style:Style,sync=true){const before=current(scope);applyStyle(style);try{localStorage.setItem(preferenceKey(scope,'appearance'),JSON.stringify(style));volatileStyles.delete(scope)}catch{volatileStyles.set(scope,style)}window.dispatchEvent(new Event('mystic-appearance'));if(sync&&scope){const patch=Object.fromEntries(Object.entries(style).filter(([key,value])=>before[key as keyof Style]!==value));if(Object.keys(patch).length)syncAppearance(patch)}}
export function applyAccountAppearance(scope:string,value:Record<string,unknown>){save(scope,mergeAppearance(current(scope),value),false)}
export function initPalette(scope:string){
 const media=matchMedia('(prefers-color-scheme:light)');
 const update=()=>applyStyle(current(scope));
 update();media.addEventListener('change',update);window.addEventListener('storage',update);
 return()=>{media.removeEventListener('change',update);window.removeEventListener('storage',update)};
}
export function ThemeToggle({scope=''}:{scope?:string}){
 const t=useLocale();
 const [style]=useAppearance(scope);
 const light=style.theme==='light'||style.theme==='system'&&matchMedia('(prefers-color-scheme:light)').matches;
 return <span className="appearance-quick"><LanguageSelector compact/><button type="button" className="icon-btn" title={light?t("Тёмная тема"):t("Светлая тема")} aria-label={light?t("Тёмная тема"):t("Светлая тема")} onClick={()=>save(scope,{...current(scope),theme:light?'dark':'light'})}>{light?<Moon size={19}/>:<Sun size={19}/>}</button>
 <button type="button" className="icon-btn" title={t("Насыщенный стиль")} aria-label={t("Насыщенный стиль")} aria-pressed={style.interfaceStyle==='immersive'} onClick={()=>save(scope,{...current(scope),interfaceStyle:style.interfaceStyle==='immersive'?'simplified':'immersive'})}><Sparkles size={19} aria-hidden="true"/></button></span>;
}
export function LanguageSelector({compact=false}:{compact?:boolean}){
 const t=useLocale();const {value,problem}=usePresentation();
 const label=languages.find(([id])=>id===value.language)?.[1]??'English';
 return <span className={'presentation-quick'+(compact?' presentation-compact':'')}><span className="presentation-language" title={problem?t(problem):t('Язык')+': '+label}><Globe2 size={16} aria-hidden="true"/><select aria-label={t('Язык')} value={value.language} onChange={e=>changePresentation({language:e.target.value as typeof value.language})}>{languages.map(([id,name])=><option key={id} value={id} lang={id}>{name}</option>)}</select><ChevronDown className="presentation-chevron" size={14} aria-hidden="true"/></span>{problem&&<small className={compact?'presentation-sync-warning':undefined} role="status" title={t(problem)}>{compact?'!':t(problem)}</small>}</span>
}
export function Settings(){
 const t=useLocale();
 const {preferenceScope:scope}=useApp();
 const [style,setStyle]=useAppearance(scope);
 const {value:presentation,problem}=usePresentation();
 const update=<K extends keyof Style>(field:K,value:Style[K])=>{const next={...current(scope),[field]:value};save(scope,next);setStyle(next)};
 return <><Header title={t("Настройки")} description={t("Тема, палитра и эффекты оформления")} action={<button className="btn secondary" onClick={()=>{save(scope,{...defaults});setStyle({...defaults})}}>{t("Сбросить оформление")}</button>}/>
  <Panel title={t('Язык')+' / '+t('Шрифт')}><div className="stack"><div className="presentation-settings-language"><span>{t('Язык')}</span><LanguageSelector/></div><label>{t('Шрифт')}<select value={presentation.font} onChange={e=>changePresentation({font:e.target.value as typeof presentation.font})}>{fonts.map(([id,name])=><option key={id} value={id}>{t(name)}</option>)}</select></label><p className="font-preview">{t("MysticEgo · 人工智能 · 인공지능 · Искусственный интеллект")}</p>{problem&&<p role="status">{t(problem)}</p>}</div></Panel>
  <div className="two-col settings-layout">
   <Panel title={t("Тема и интерфейс")}><div className="stack">
    <label>{t("Тема")}<select aria-label={t("Тема")} value={style.theme} onChange={event=>update('theme',event.target.value as Style['theme'])}><option value="dark">{t("Тёмная")}</option><option value="light">{t("Светлая")}</option><option value="system">{t("Как в системе")}</option></select></label>
    <div className="appearance-style-options" role="group" aria-label={t("Стиль интерфейса")}>{(['simplified','immersive'] as const).map(item=><button key={item} type="button" className="appearance-style-option" data-preview={item} aria-pressed={style.interfaceStyle===item} onClick={()=>update('interfaceStyle',item)}><span className="appearance-art-preview" aria-hidden="true"/><strong>{item==='immersive'?t("Насыщенный"):t("Упрощённый")}</strong><small>{item==='immersive'?t("Пейзажи, глубина и мягкое свечение"):t("Чистые поверхности и спокойная сетка")}</small></button>)}</div>
    <label>{t("Анимации")}<select aria-label={t("Анимации")} value={style.uiMotion} onChange={event=>update('uiMotion',event.target.value as Style['uiMotion'])}><option value="normal">{t("Обычные")}</option><option value="low">{t("Минимальные")}</option><option value="off">{t("Выключены")}</option></select></label>
    <label>{t("Переходы")}<select aria-label={t("Переходы")} value={style.transition} onChange={event=>update('transition',event.target.value as Style['transition'])}><option value="flow">{t("Мягкое появление")}</option><option value="fade">{t("Растворение")}</option><option value="slide">{t("Скольжение")}</option><option value="scale">{t("Масштабирование")}</option></select></label>
    <label>{t("Плотность")}<select aria-label={t("Плотность")} value={style.density} onChange={event=>update('density',event.target.value as Style['density'])}><option value="comfortable">{t("Комфортная")}</option><option value="compact">{t("Компактная")}</option></select></label>
    <label>{t("Текст")}<select aria-label={t("Текст")} value={style.size} onChange={event=>update('size',event.target.value as Style['size'])}><option value="normal">{t("Обычный")}</option><option value="large">{t("Крупный")}</option></select></label>
   </div></Panel>
   <div className="settings-side"><Panel title={t("Неоновые эффекты (насыщенный стиль)")} aside={<Sparkles size={18}/>}><div className="stack">
    {([['ambient','Анимация фонового свечения'],['wave','Неоновая волна'],['grid','Градиент по затухающей сетке'],['border','Свет по контуру кнопок']] as const).map(([field,label])=>
     <label className="check-row" key={field}><input type="checkbox" checked={style[field]} onChange={event=>update(field,event.target.checked)}/>{t(label)}</label>)}
    <label>{t('Яркость')} · {style.intensity}%<input type="range" min={10} max={100} value={style.intensity} onChange={event=>update('intensity',Number(event.target.value))}/></label>
    <button type="button" className="btn neon-preview">{t("Предпросмотр кнопки")}</button>
   </div></Panel>
  <Panel title={t("Палитра")} aside={<Palette size={18}/>}>
   <div className="palette-options">{[...palettes,{id:'custom',name:'Своя палитра',colors:[style.primary,'#444057',style.secondary]}].map(item=>
    <button className={'palette-option '+(style.palette===item.id?'selected':'')} key={item.id} onClick={()=>update('palette',item.id)} aria-pressed={style.palette===item.id}>
     <div className="palette-swatches">{item.colors.map((color,index)=><i style={{background:color}} key={index}/>)}</div><strong>{t(item.name)}</strong>{style.palette===item.id&&<Check size={17}/>}
    </button>)}</div>
   {style.palette==='custom'&&<div className="form-pair stack custom-colors"><label>{t("Основной цвет")}<input type="color" value={style.primary} onChange={event=>update('primary',event.target.value)}/></label>
    <label>{t("Второй цвет")}<input type="color" value={style.secondary} onChange={event=>update('secondary',event.target.value)}/></label></div>}
  </Panel></div>
  </div>
 </>;
}
