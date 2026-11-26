import {readFile} from 'node:fs/promises';
import {deflateSync} from 'node:zlib';
import {mkdir,writeFile} from 'node:fs/promises';
import {join} from 'node:path';

export const roles={
  orchestra:{title:'Orchestra',description:'Центр разработки и управления экосистемой MysticEgo.',tone:[151,126,255]},
  global:{title:'Global',description:'Панель управления организацией и сетью площадок MysticEgo.',tone:[89,202,230]},
  local:{title:'Local',description:'Управление площадкой, оборудованием и рабочими станциями MysticEgo.',tone:[94,216,172]},
  agent:{title:'Agent',description:'Рабочая станция и инструменты диагностики MysticEgo.',tone:[240,181,109]},
  audit:{title:'Audit',description:'Личное пространство, профиль и инструменты MysticEgo.',tone:[224,142,222]}
};
export const publicPages={
  studio:{title:'Студия MysticEgo',description:'Разработка инфраструктуры MysticEgo и концепция мира Ether And Chaos.',image:'orchestra'},
  developer:{title:'Разработчик MysticEgo',description:'Проектирование серверных и настольных приложений, разработка MysticEgo.',image:'orchestra'},
  'desktop-downloads':{title:'Приложения MysticEgo',description:'Настольные приложения и инструменты подключения к экосистеме MysticEgo.',image:'agent'}
};
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export function metadata(role,page='root'){
  if(!Object.hasOwn(roles,role))throw Error('Unknown panel role: '+role);
  if(page!=='root'&&page!=='share'&&!(role==='orchestra'&&Object.hasOwn(publicPages,page)))throw Error('Unknown public page');
  const info=roles[role],extra=role==='orchestra'?publicPages[page]:undefined;
  const title=extra?.title??'MysticEgo · '+info.title;
  const description=extra?.description??info.description;
  const path=page==='root'?'/':'/'+page;
  const image='/og/'+(extra?.image??role)+'.png';
  const meta=(property,value,attr='property')=>'<meta '+attr+'="'+escape(property)+'" content="'+escape(value)+'"/>';
  return ['<!-- mysticego-social-start -->',
    '<link rel="canonical" href="'+path+'"/>',
    meta('description',description,'name'),meta('og:type','website'),meta('og:site_name','MysticEgo'),
    meta('og:title',title),meta('og:description',description),meta('og:url',path),meta('og:locale','ru_RU'),
    meta('og:image',image),meta('og:image:type','image/png'),meta('og:image:width','1200'),meta('og:image:height','630'),
    meta('og:image:alt','MysticEgo — '+info.title),meta('twitter:card','summary_large_image','name'),
    meta('twitter:title',title,'name'),meta('twitter:description',description,'name'),meta('twitter:image',image,'name'),
    ...(!extra?[meta('robots','noindex','name')]:[]),'<!-- mysticego-social-end -->'].join('\n');
}
export function panelHtml(template,role,page='root'){
  if(!template.includes('data-role="orchestra"')||!template.includes('<title>MysticEgo</title>')||!template.includes('</head>'))
    throw Error('Shared Vite HTML template changed');
  const title=(role==='orchestra'?publicPages[page]?.title:undefined)??'MysticEgo · '+roles[role].title;
  return template.replace('data-role="orchestra"','data-role="'+role+'"')
    .replace('<title>MysticEgo</title>','<title>'+escape(title)+'</title>')
    .replace('</head>',metadata(role,page)+'\n</head>');
}
// Original, deterministic, dependency-free concept art. PNG, not SVG, for link crawlers.
const glyphs={
 A:['01110','10001','10001','11111','10001','10001','10001'],B:['11110','10001','10001','11110','10001','10001','11110'],
 C:['01111','10000','10000','10000','10000','10000','01111'],D:['11110','10001','10001','10001','10001','10001','11110'],
 E:['11111','10000','10000','11110','10000','10000','11111'],G:['01111','10000','10000','10111','10001','10001','01111'],
 H:['10001','10001','10001','11111','10001','10001','10001'],I:['111','010','010','010','010','010','111'],
 L:['10000','10000','10000','10000','10000','10000','11111'],M:['10001','11011','10101','10101','10001','10001','10001'],
 N:['10001','11001','10101','10011','10001','10001','10001'],O:['01110','10001','10001','10001','10001','10001','01110'],
 R:['11110','10001','10001','11110','10100','10010','10001'],S:['01111','10000','10000','01110','00001','00001','11110'],
 T:['11111','00100','00100','00100','00100','00100','00100'],U:['10001','10001','10001','10001','10001','10001','01110'],
 K:['10001','10010','10100','11000','10100','10010','10001'],W:['10001','10001','10001','10101','10101','11011','10001'],
 Y:['10001','10001','01010','00100','00100','00100','00100']
};
const clamp=v=>Math.max(0,Math.min(255,Math.round(v)));
const mix=(a,b,t)=>a.map((v,i)=>v+(b[i]-v)*t);
const hash=(x,y)=>{let n=Math.imul(x+71,374761393)^Math.imul(y+193,668265263);n=Math.imul(n^(n>>>13),1274126177);return ((n^(n>>>16))>>>0)/4294967295};
const crc32=buffer=>{let crc=0xffffffff;for(const b of buffer){crc^=b;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0)}return (crc^0xffffffff)>>>0};
function chunk(type,data){
  const key=Buffer.from(type),head=Buffer.alloc(4),tail=Buffer.alloc(4);
  head.writeUInt32BE(data.length);tail.writeUInt32BE(crc32(Buffer.concat([key,data])));
  return Buffer.concat([head,key,data,tail]);
}
export function coverPng(role){
  const info=roles[role];if(!info)throw Error('Unknown cover');
  const w=1200,h=630,stride=w*3+1,raw=Buffer.alloc(stride*h),tone=info.tone;
  const pixel=(x,y,color)=>{if(x<0||x>=w||y<0||y>=h)return;const i=y*stride+1+x*3;for(let k=0;k<3;k++)raw[i+k]=clamp(color[k])};
  const blend=(x,y,color,a)=>{if(x<0||x>=w||y<0||y>=h)return;const i=y*stride+1+x*3;for(let k=0;k<3;k++)raw[i+k]=clamp(raw[i+k]*(1-a)+color[k]*a)};
  const ridge=(x,level)=>{const f=x/1200;return [414,463,519,574][level]+Math.sin(f*(12+level*4)+level*1.7)*(28+level*5)+Math.sin(f*31+level*3)*14+Math.sin(f*71+level)*7};
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
    const u=x/w,v=y/h,d=Math.hypot((x-915)/520,(y-240)/310);
    const halo=Math.max(0,1-d)**2,sky=mix([10,13,28],[35,42,67],v);
    let c=sky.map((n,k)=>n+tone[k]*halo*.34);
    const aurora=Math.exp(-Math.pow((y-(180+55*Math.sin(x/155)))/34,2))*(.3+.7*u);
    c=c.map((n,k)=>n+tone[k]*aurora*.12);
    const moon=Math.hypot(x-930,y-193);
    if(moon<68)c=mix(c,[229,231,240],Math.min(1,(68-moon)/2)*.84);
    for(let l=0;l<4;l++){
      const line=ridge(x,l);
      if(y>line){const shade=[.58,.37,.24,.15][l];c=mix([9,12,23],tone,shade*(.35+.65*u));c=c.map(n=>n*(.83+.17*Math.min(1,(y-line)/85)))}
    }
    const star=hash(x,y);
    if(y<360&&moon>75&&star>.99915)c=c.map(n=>n+(star-.99915)*68000);
    const vignette=.64+.36*Math.max(0,1-Math.hypot((u-.58)/.92,(v-.48)/.95));
    const leftShade=.63+.37*Math.min(1,u*1.55);
    pixel(x,y,c.map(n=>n*vignette*leftShade+(hash(x+99,y+13)-.5)*5));
  }
  function line(x0,y0,x1,y1,color,alpha=1){
    const steps=Math.max(Math.abs(x1-x0),Math.abs(y1-y0),1);
    for(let n=0;n<=steps;n++)blend(Math.round(x0+(x1-x0)*n/steps),Math.round(y0+(y1-y0)*n/steps),color,alpha);
  }
  const nodes=[[780,255],[848,145],[930,193],[1012,280],[1080,182],[865,345]];
  for(const [a,b] of [[0,1],[1,2],[2,3],[2,4],[0,5],[5,3]])line(...nodes[a],...nodes[b],tone,.55);
  for(const [x,y] of nodes){for(let dy=-9;dy<=9;dy++)for(let dx=-9;dx<=9;dx++){const d=Math.hypot(dx,dy);if(d<9)blend(x+dx,y+dy,tone,(1-d/9)**2);if(d<2.5)blend(x+dx,y+dy,[240,245,255],.96)}}
  for(let x=76;x<1124;x++){blend(x,50,tone,.35);blend(x,579,tone,.35)}
  for(let y=50;y<=579;y++){blend(76,y,tone,.35);blend(1123,y,tone,.35)}
  function label(text,x,y,scale,color){
    for(const ch of text){const g=glyphs[ch];if(!g){x+=scale*4;continue}for(let r=0;r<7;r++)for(let col=0;col<g[r].length;col++)if(g[r][col]==='1')for(let dy=0;dy<scale;dy++)for(let dx=0;dx<scale;dx++)pixel(x+col*scale+dx,y+r*scale+dy,color);x+=(g[0].length+1)*scale;}
  }
  label('MYSTICEGO',126,190,5,[237,239,251]);
  label(info.title.toUpperCase(),126,262,9,tone);
  for(let y=348;y<351;y++)for(let x=126;x<392;x++)pixel(x,y,tone);
  label('CONTROL NETWORK',126,377,3,[192,202,225]);
  const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(w);ihdr.writeUInt32BE(h,4);ihdr[8]=8;ihdr[9]=2;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',deflateSync(raw,{level:6})),chunk('IEND',Buffer.alloc(0))]);
}
// Caddy evaluates only these trusted generated HTML pages, using a fixed site variable.
export function auditCaddyHtml(template,page='root'){
  const html=panelHtml(template,'audit',page);
  return html.replace(/<!-- mysticego-social-start -->[\s\S]*?<!-- mysticego-social-end -->/,block=>
    block.replace(/(content|href)="(\/[a-zA-Z0-9/_.-]*)"/g,(_all,attribute,path)=>
      attribute+'="{{placeholder "http.vars.mystic_public_origin"}}/audit-portal'+path+'"'));
}
export async function writeSocialAssets(root){
  const folder=join(root,'og');await mkdir(folder,{recursive:true});
  for(const role of Object.keys(roles))await writeFile(join(folder,role+'.png'),await readFile(new URL('./social-art/'+role+'.png',import.meta.url)));
}
