import React,{useEffect,useState} from 'react';
const key='mysticego.clouddesk.appearance.v1',defaults={theme:'dark',interfaceStyle:'simplified',uiMotion:'normal',transition:'flow'};
function read(){try{return {...defaults,...JSON.parse(localStorage.getItem(key)||'{}')}}catch{return defaults}}
export function AppearanceControls(){
 const [look,setLook]=useState(read);
 useEffect(()=>{const media=matchMedia('(prefers-color-scheme:light)');const apply=()=>{const root=document.documentElement;root.dataset.theme=look.theme==='system'?(media.matches?'light':'dark'):look.theme;root.dataset.interface=look.interfaceStyle;root.dataset.uiMotion=look.uiMotion;root.dataset.transition=look.transition;};apply();media.addEventListener('change',apply);try{localStorage.setItem(key,JSON.stringify(look))}catch{}return()=>media.removeEventListener('change',apply)},[look]);
 const field=(name,label,choices)=><label>{label}<select value={look[name]} onChange={e=>setLook(v=>({...v,[name]:e.target.value}))}>{choices.map(([v,t])=><option key={v} value={v}>{t}</option>)}</select></label>;
 return <details className="desk-appearance"><summary>Оформление</summary>{field('theme','Тема',[['dark','Тёмная'],['light','Светлая'],['system','Системная']])}{field('interfaceStyle','Стиль',[['simplified','Упрощённый'],['immersive','Насыщенный']])}{field('uiMotion','Анимации',[['normal','Обычные'],['low','Минимальные'],['off','Выключены']])}{field('transition','Переход',[['flow','Мягкий'],['fade','Растворение'],['slide','Скольжение'],['scale','Масштабирование']])}<button type="button" className="subtle" onClick={()=>setLook({...defaults})}>Сбросить</button></details>
}
