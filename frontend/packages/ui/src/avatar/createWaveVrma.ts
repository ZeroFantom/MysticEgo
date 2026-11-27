import {Euler, Quaternion} from 'three';

type Frame=[number,number,number];
export type GeneratedMotion='wave'|'nod'|'walk'|'sit'|'perch'|'idle'|'dance';
const joints = {
  hips:0,spine:1,chest:2,rightUpperArm:3,rightLowerArm:4,
  neck:5,head:6,leftUpperLeg:7,leftLowerLeg:8,leftFoot:9,
  rightUpperLeg:10,rightLowerLeg:11,rightFoot:12,leftUpperArm:13,leftLowerArm:14
} as const;
type Joint=keyof typeof joints;
const extension='VRMC_vrm_animation';
const x=(v:number):Frame=>[v,0,0];
const z=(v:number):Frame=>[0,0,v];
type Plan={times:number[];tracks:Partial<Record<Joint,Frame[]>>};
const plans:Record<GeneratedMotion,Plan>={
 wave:{times:[0,.42,.66,.88,1.1,1.32,1.58,2.05],tracks:{
  rightUpperArm:[[0,0,62],[0,-15,-40],[0,-15,-40],[0,-15,-40],[0,-15,-40],[0,-15,-40],[0,-15,10],[0,0,62]],
  rightLowerArm:[0,-57,-25,-60,-25,-58,-20,0].map(z)}},
 nod:{times:[0,.3,.65,1,1.3],tracks:{head:[0,18,-7,12,0].map(x)}},
 walk:{times:[0,.25,.5,.75,1],tracks:{
  leftUpperLeg:[25,0,-25,0,25].map(x),rightUpperLeg:[-25,0,25,0,-25].map(x),
  leftLowerLeg:[0,35,5,10,0].map(x),rightLowerLeg:[5,10,0,35,5].map(x),
  leftUpperArm:[[-20,0,-65],[0,0,-65],[20,0,-65],[0,0,-65],[-20,0,-65]],
  rightUpperArm:[[20,0,65],[0,0,65],[-20,0,65],[0,0,65],[20,0,65]]}},
 sit:{times:[0,.5,1],tracks:{leftUpperLeg:[-80,-80,-80].map(x),rightUpperLeg:[-80,-80,-80].map(x),leftLowerLeg:[90,90,90].map(x),rightLowerLeg:[90,90,90].map(x),spine:[8,8,8].map(x)}},
 perch:{times:[0,.5,1,1.5,2],tracks:{leftUpperLeg:[-78,-78,-78,-78,-78].map(x),rightUpperLeg:[-78,-78,-78,-78,-78].map(x),leftLowerLeg:[45,65,45,30,45].map(x),rightLowerLeg:[45,30,45,65,45].map(x)}},
 idle:{times:[0,.8,1.6,2.4,3.2],tracks:{spine:[0,2,0,-2,0].map(z),head:[0,2,0,-2,0].map(x)}},
 dance:{times:[0,.35,.7,1.05,1.4],tracks:{spine:[0,10,0,-10,0].map(z),leftUpperArm:[-65,-40,-65,-85,-65].map(z),rightUpperArm:[65,85,65,40,65].map(z),leftUpperLeg:[0,15,0,-15,0].map(x),rightUpperLeg:[0,-15,0,15,0].map(x)}}
};
function rotations(frames:Frame[],strength:number){
 const q=new Quaternion();const rad=Math.PI/180;
 return frames.flatMap(([a,b,c])=>q.setFromEuler(new Euler(a*rad*strength,b*rad*strength,c*rad*strength)).toArray());
}
/** Original procedural motion exported as real self-contained VRMA (not AI motion capture). */
export function createMotionVrma(motion:GeneratedMotion,tempo=1):File{
 const plan=Object.hasOwn(plans,motion)?plans[motion]:undefined;
 if(!plan||!Number.isFinite(tempo)||tempo<.5||tempo>2)throw Error('Некорректные параметры движения');
 const times=plan.times.map(t=>t/tempo);
 const tracks=Object.entries(plan.tracks) as [Joint,Frame[]][];
 const values=[times,...tracks.map(([,frames])=>rotations(frames,1))];
 const binary=new ArrayBuffer(values.reduce((size,value)=>size+value.length*4,0));
 const floats=new DataView(binary);let offset=0;
 const bufferViews=values.map(value=>{
  const view={buffer:0,byteOffset:offset,byteLength:value.length*4};
  for(const frame of value){floats.setFloat32(offset,frame,true);offset+=4}
  return view;
 });
 const nodes=[
  {name:'hips',translation:[0,1,0],children:[1,7,10]},
  {name:'spine',translation:[0,.2,0],children:[2]},
  {name:'chest',translation:[0,.24,0],children:[3,5,13]},
  {name:'rightUpperArm',translation:[-.2,.04,0],children:[4]},
  {name:'rightLowerArm',translation:[-.29,0,0]},
  {name:'neck',translation:[0,.15,0],children:[6]},
  {name:'head',translation:[0,.14,0]},
  {name:'leftUpperLeg',translation:[.09,-.05,0],children:[8]},
  {name:'leftLowerLeg',translation:[0,-.44,0],children:[9]},
  {name:'leftFoot',translation:[0,-.43,.04]},
  {name:'rightUpperLeg',translation:[-.09,-.05,0],children:[11]},
  {name:'rightLowerLeg',translation:[0,-.44,0],children:[12]},
  {name:'rightFoot',translation:[0,-.43,.04]},
  {name:'leftUpperArm',translation:[.2,.04,0],children:[14]},
  {name:'leftLowerArm',translation:[.29,0,0]}
 ];
 const document={
  asset:{version:'2.0',generator:'MysticEgo'},
  extensionsUsed:[extension],extensions:{[extension]:{specVersion:'1.0',humanoid:{humanBones:Object.fromEntries(Object.entries(joints).map(([name,node])=>[name,{node}]))}}},
  scene:0,scenes:[{nodes:[0]}],nodes,buffers:[{byteLength:binary.byteLength}],bufferViews,
  accessors:values.map((_,bufferView)=>({bufferView,componentType:5126,count:times.length,type:bufferView===0?'SCALAR':'VEC4',...(bufferView===0?{min:[times[0]],max:[times.at(-1)]}:{})})),
  animations:[{name:'MysticEgo '+motion,samplers:tracks.map((_,i)=>({input:0,output:i+1,interpolation:'LINEAR'})),channels:tracks.map(([joint],sampler)=>({sampler,target:{node:joints[joint],path:'rotation'}}))}]
 };
 const encoded=new TextEncoder().encode(JSON.stringify(document));
 const jsonLength=Math.ceil(encoded.byteLength/4)*4;
 const jsonChunk=new Uint8Array(jsonLength).fill(0x20);jsonChunk.set(encoded);
 const header=new DataView(new ArrayBuffer(20));
 header.setUint32(0,0x46546c67,true);header.setUint32(4,2,true);header.setUint32(8,20+jsonLength+8+binary.byteLength,true);
 header.setUint32(12,jsonLength,true);header.setUint32(16,0x4e4f534a,true);
 const binHeader=new DataView(new ArrayBuffer(8));binHeader.setUint32(0,binary.byteLength,true);binHeader.setUint32(4,0x004e4942,true);
 return new File([header.buffer,jsonChunk,binHeader.buffer,binary],motion==='wave'?'mysticego-greeting.vrma':'mysticego-'+motion+'.vrma',{type:'model/gltf-binary'});
}
export function createWaveVrma():File{return createMotionVrma('wave')}
