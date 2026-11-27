export const palettes=[
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
export const appearanceDefaults:Style={interfaceStyle:'simplified',transition:'flow',uiMotion:'normal',theme:'dark',palette:'iris',primary:'#b49bf7',secondary:'#75d9c7',ambient:true,wave:false,grid:false,border:false,intensity:55,density:'comfortable',size:'normal'};
// Account responses are partial patches, not full replacements of local preferences.
export function mergeAppearance(base:Style,patch:Record<string,unknown>):Style{
 const next={...base};
 const choices={theme:['dark','light','system'],interfaceStyle:['simplified','immersive'],transition:['flow','fade','slide','scale'],uiMotion:['off','low','normal'],density:['compact','comfortable'],size:['normal','large'],palette:[...palettes.map(p=>p.id),'custom']};
 for(const [key,allowed] of Object.entries(choices))if(typeof patch[key]==='string'&&allowed.includes(patch[key] as string))Object.assign(next,{[key]:patch[key]});
 for(const key of ['ambient','wave','grid','border'] as const)if(typeof patch[key]==='boolean')next[key]=patch[key];
 for(const key of ['primary','secondary'] as const)if(typeof patch[key]==='string'&&/^#[0-9a-f]{6}$/i.test(patch[key]))next[key]=patch[key];
 if(typeof patch.intensity==='number'&&Number.isInteger(patch.intensity)&&patch.intensity>=10&&patch.intensity<=100)next.intensity=patch.intensity;
 return next;
}
