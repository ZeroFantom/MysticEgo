import {useEffect,useState} from 'react';
import {Check,Moon,Palette,Sparkles,Sun} from 'lucide-react';
import {useApp} from '../app/context';
import {preferenceKey} from '../app/panelPreferences';
import {changePresentation,fonts,languages,syncAppearance,useLocale,usePresentation} from '../app/presentation';
import {Header,Panel} from '../components/primitives';

const palettes=[
 {id:'iris',name:'Ирис',colors:['#b49bf7','#60449a','#75d9c7']},
 {id:'jade',name:'Нефрит',colors:['#87e0d1','#3b7770','#af9bf7']},
 {id:'amber',name:'Янтарь',colors:['#f4b47f','#915f4d','#a6a6fa']},
 {id:'arctic',name:'Арктика',colors:['#83c6ff','#315b87','#81e7eb']},
 {id:'rose',name:'Роза',colors:['#f4adc9','#824161','#bc9df3']},
 {id:'lime',name:'Лайм',colors:['#c2e78d','#557538','#64e2b9']},
 {id:'indigo',name:'Индиго',colors:['#aaaafa','#4e4b98','#78dafd']},
 {id:'coral',name:'Коралл',colors:['#ffa99a','#985047','#ffe2ab']},
 {id:'steel',name:'Сталь',colors:['#c6d5eb','#53657c','#a5cccf']}
];
export type Style={interfaceStyle:'immersive'|'simplified';transition:'flow'|'fade'|'slide'|'scale';uiMotion:'off'|'low'|'normal';theme:'dark'|'light'|'system';palette:string;primary:string;secondary:string;ambient:boolean;wave:boolean;grid:boolean;border:boolean;intensity:number;density:'compact'|'comfortable';size:'normal'|'large'};
const volatileStyles=new Map<string,Style>();
const defaults:Style={interfaceStyle:'simplified',transition:'flow',uiMotion:'normal',theme:'dark',palette:'iris',primary:'#b49bf7',secondary:'#75d9c7',ambient:true,wave:false,grid:false,border:false,intensity:55,density:'comfortable',size:'normal'};
function current(scope:string):Style{
 if(volatileStyles.has(scope))return volatileStyles.get(scope)!;
 try{const saved={...defaults,...JSON.parse(localStorage.getItem(preferenceKey(scope,'appearance'))??'{}')};return {...saved,theme:['dark','light','system'].includes(saved.theme)?saved.theme:defaults.theme,interfaceStyle:saved.interfaceStyle==='immersive'?'immersive':'simplified',transition:['flow','fade','slide','scale'].includes(saved.transition)?saved.transition:'flow',uiMotion:['off','low','normal'].includes(saved.uiMotion)?saved.uiMotion:'normal'}}
 catch{return defaults}
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
export function applyAccountAppearance(scope:string,value:Record<string,unknown>){const next={...defaults};for(const key of Object.keys(defaults) as (keyof Style)[]){if(Object.hasOwn(value,key))Object.assign(next,{[key]:value[key]})}save(scope,next,false)}
export function initPalette(scope:string){
 const media=matchMedia('(prefers-color-scheme:light)');
 const update=()=>applyStyle(current(scope));
 update();media.addEventListener('change',update);window.addEventListener('storage',update);
 return()=>{media.removeEventListener('change',update);window.removeEventListener('storage',update)};
}
export function ThemeToggle({scope=''}:{scope?:string}){
 const t=useLocale();
 const [style,setStyle]=useState(()=>current(scope));
 useEffect(()=>{const sync=()=>setStyle(current(scope));sync();window.addEventListener('mystic-appearance',sync);window.addEventListener('storage',sync);const media=matchMedia('(prefers-color-scheme:light)');media.addEventListener('change',sync);return()=>{window.removeEventListener('mystic-appearance',sync);window.removeEventListener('storage',sync);media.removeEventListener('change',sync)}},[scope]);
 const light=style.theme==='light'||style.theme==='system'&&matchMedia('(prefers-color-scheme:light)').matches;
 return <span className="appearance-quick"><LanguageSelector/><button type="button" className="icon-btn" title={light?t("Тёмная тема"):t("Светлая тема")} aria-label={light?t("Тёмная тема"):t("Светлая тема")} onClick={()=>save(scope,{...current(scope),theme:light?'dark':'light'})}>{light?<Moon size={19}/>:<Sun size={19}/>}</button>
 <button type="button" className="icon-btn" title={t("Насыщенный стиль")} aria-label={t("Насыщенный стиль")} aria-pressed={style.interfaceStyle==='immersive'} onClick={()=>save(scope,{...current(scope),interfaceStyle:style.interfaceStyle==='immersive'?'simplified':'immersive'})}><Sparkles size={19} aria-hidden="true"/></button></span>;
}
export function LanguageSelector(){const t=useLocale();const {value,problem}=usePresentation();return <span className="presentation-quick"><select aria-label={t('Язык')} value={value.language} onChange={e=>changePresentation({language:e.target.value as typeof value.language})}>{languages.map(([id,label])=><option key={id} value={id}>{label}</option>)}</select>{problem&&<small role="status">{t(problem)}</small>}</span>}
export function Settings(){
 const t=useLocale();
 const {preferenceScope:scope}=useApp();
 const [style,setStyle]=useState(()=>current(scope));
 useEffect(()=>{const sync=()=>setStyle(current(scope));sync();window.addEventListener('mystic-appearance',sync);window.addEventListener('storage',sync);return()=>{window.removeEventListener('mystic-appearance',sync);window.removeEventListener('storage',sync)}},[scope]);
 const {value:presentation,problem}=usePresentation();
 const update=<K extends keyof Style>(field:K,value:Style[K])=>{const next={...current(scope),[field]:value};save(scope,next);setStyle(next)};
 return <><Header title={t("Настройки")} description={t("Тема, палитра и эффекты оформления")} action={<button className="btn secondary" onClick={()=>{save(scope,{...defaults});setStyle({...defaults})}}>{t("Сбросить оформление")}</button>}/>
  <Panel title={t('Язык')+' / '+t('Шрифт')}><div className="stack"><LanguageSelector/><label>{t('Шрифт')}<select value={presentation.font} onChange={e=>changePresentation({font:e.target.value as typeof presentation.font})}>{fonts.map(([id,name])=><option key={id} value={id}>{t(name)}</option>)}</select></label><p className="font-preview">{t("MysticEgo · 人工智能 · 인공지능 · Искусственный интеллект")}</p>{problem&&<p role="status">{t(problem)}</p>}</div></Panel>
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
