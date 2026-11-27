import {useId,useRef,useState,type PointerEvent,type KeyboardEvent} from 'react';
import {usePanelPreference} from '../app/panelPreferences';
import './control-graph.css';
export type GraphNode={id:string;title:string;subtitle:string;state:string;x:number;y:number};
export type GraphEdge={from:string;to:string;label?:string;kind?:'configured'|'observed'|'business'};
type Point={x:number;y:number};
const W=200,H=76;
const valid=(v:unknown):v is Record<string,Point>=>!!v&&typeof v==='object'&&!Array.isArray(v)&&Object.keys(v).length<=500&&Object.entries(v).every(([key,p])=>key.length<=200&&!!p&&typeof p==='object'&&Number.isFinite(p.x)&&Number.isFinite(p.y)&&p.x>=0&&p.y>=0&&p.x<=30000&&p.y<=30000);
const clamp=(n:number)=>Math.max(0,Math.min(30000,n));
export function ControlGraph({nodes,edges,scope,selected,choose,zoom=100,compact=false}:{nodes:GraphNode[];edges:GraphEdge[];scope:string;selected?:string;choose?:(id:string)=>void;zoom?:number;compact?:boolean}){
 const [saved,setSaved]=usePanelPreference<Record<string,Point>>('graph.positions.'+scope,{},valid);
 const [moving,setMoving]=useState<{id:string;point:Point}|null>(null);
 const drag=useRef<{id:string;pointer:number;startX:number;startY:number;point:Point;latest:Point;didMove:boolean}|null>(null);
 const suppress=useRef(false),surface=useRef<HTMLDivElement>(null),marker=useId().replaceAll(':','');
 const placed=nodes.slice(0,250).map(n=>({...n,...(moving?.id===n.id?moving.point:saved[n.id]??{x:n.x,y:n.y})}));
 const byId=new Map(placed.map(n=>[n.id,n]));
 const width=Math.max(320,...placed.map(n=>n.x+W+32)),height=Math.max(180,...placed.map(n=>n.y+H+32));
 function down(e:PointerEvent<HTMLButtonElement>,n:GraphNode){
  if(compact||e.button!==0||!surface.current)return;
  const p={x:n.x,y:n.y};drag.current={id:n.id,pointer:e.pointerId,startX:e.clientX,startY:e.clientY,point:p,latest:p,didMove:false};
  suppress.current=false;e.currentTarget.setPointerCapture(e.pointerId);
 }
 function move(e:PointerEvent<HTMLButtonElement>){
  const d=drag.current;if(!d||d.pointer!==e.pointerId)return;
  const rect=surface.current?.getBoundingClientRect();const scale=rect&&width?rect.width/width:1;
  if(Math.abs(e.clientX-d.startX)+Math.abs(e.clientY-d.startY)>5)d.didMove=true;
  if(!d.didMove)return;
  d.latest={x:clamp(d.point.x+(e.clientX-d.startX)/Math.max(.1,scale)),y:clamp(d.point.y+(e.clientY-d.startY)/Math.max(.1,scale))};
  setMoving({id:d.id,point:d.latest});
 }
 function finish(e:PointerEvent<HTMLButtonElement>,cancel=false){
  const d=drag.current;if(!d||d.pointer!==e.pointerId)return;
  suppress.current=d.didMove;
  if(d.didMove&&!cancel)setSaved(old=>({...old,[d.id]:d.latest}));
  drag.current=null;setMoving(null);
  if(e.currentTarget.hasPointerCapture(e.pointerId))e.currentTarget.releasePointerCapture(e.pointerId);
 }
 function key(e:KeyboardEvent<HTMLButtonElement>,n:GraphNode){
  if(compact)return;
  const delta:Record<string,[number,number]>={ArrowLeft:[-20,0],ArrowRight:[20,0],ArrowUp:[0,-20],ArrowDown:[0,20]};
  const d=delta[e.key];if(!d)return;e.preventDefault();setSaved(old=>({...old,[n.id]:{x:clamp(n.x+d[0]),y:clamp(n.y+d[1])}}));
 }
 return <div className="control-graph-wrap">
  {!compact&&<div className="control-graph-tools"><small>Перетащите карточку или используйте стрелки клавиатуры. Связи перестраиваются автоматически.</small><button type="button" className="btn secondary" onClick={()=>setSaved({})}>Сбросить расположение</button></div>}
  {nodes.length>250&&<p className="footnote">Показаны первые 250 узлов. Используйте фильтр бизнес-графа для остальных.</p>}
  <div className="control-graph-scroll" tabIndex={0} role="region" aria-label="Граф зависимостей"><div style={{width:width*zoom/100,height:height*zoom/100}}><div ref={surface} className="control-graph-surface" style={{width,height,transform:'scale('+zoom/100+')'}}>
   <svg className="control-graph-edges" width={width} height={height} aria-hidden="true"><defs><marker id={marker} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0 0 L8 4 L0 8 Z" fill="currentColor"/></marker></defs>
    {edges.map((edge,i)=>{const a=byId.get(edge.from),b=byId.get(edge.to);if(!a||!b)return null;
     const right=b.x>=a.x;const x1=a.x+(right?W:0),x2=b.x+(right?0:W),y1=a.y+H/2,y2=b.y+H/2,bend=Math.max(40,Math.abs(x2-x1)*.45),direction=right?1:-1;
     return <g key={edge.from+'|'+edge.to+'|'+i}><path className="control-graph-line" data-kind={edge.kind??'configured'} markerEnd={'url(#'+marker+')'} d={`M ${x1} ${y1} C ${x1+bend*direction} ${y1}, ${x2-bend*direction} ${y2}, ${x2} ${y2}`}/>{edge.label&&<text x={(x1+x2)/2} y={(y1+y2)/2-9} textAnchor="middle">{edge.label}</text>}</g>})}
   </svg>
   {placed.map(n=><button key={n.id} type="button" className={'control-graph-node network-card '+(selected===n.id?'selected':'')} data-node-state={n.state} data-dragging={moving?.id===n.id} style={{left:n.x,top:n.y,width:W,height:H}} aria-label={n.title+' · '+n.subtitle} aria-pressed={selected===n.id} onPointerDown={e=>down(e,n)} onPointerMove={move} onPointerUp={e=>finish(e)} onPointerCancel={e=>finish(e,true)} onLostPointerCapture={e=>finish(e,true)} onKeyDown={e=>key(e,n)} onClick={()=>{if(suppress.current){suppress.current=false;return}choose?.(n.id)}}><span className="control-graph-dot"/><span><strong title={n.title}>{n.title}</strong><small>{n.subtitle}</small></span></button>)}
  </div></div></div>
 </div>
}
