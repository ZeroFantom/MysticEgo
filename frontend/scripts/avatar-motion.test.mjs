import test from 'node:test';
import assert from 'node:assert/strict';
import {createMotionVrma,createWaveVrma} from '../packages/ui/src/avatar/createWaveVrma.ts';

const motions=['wave','nod','walk','sit','perch','idle','dance'];
for(const motion of motions)test('VRMA binary structure: '+motion,async()=>{
 const file=createMotionVrma(motion,1.25);
 const bytes=await file.arrayBuffer(),view=new DataView(bytes);
 assert.equal(view.getUint32(0,true),0x46546c67);
 assert.equal(view.getUint32(4,true),2);
 assert.equal(view.getUint32(8,true),bytes.byteLength);
 const len=view.getUint32(12,true);
 const json=JSON.parse(new TextDecoder().decode(new Uint8Array(bytes,20,len)));
 const bin=20+len+8;
 assert.equal(view.getUint32(bin-4,true),0x004e4942);
 assert.equal(view.getUint32(bin-8,true),bytes.byteLength-bin);
 assert.equal(json.extensions.VRMC_vrm_animation.specVersion,'1.0');
 const animation=json.animations[0];
 for(const channel of animation.channels){
  const sampler=animation.samplers[channel.sampler],input=json.accessors[sampler.input],output=json.accessors[sampler.output];
  assert.equal(input.count,output.count);
  assert.equal(output.type,'VEC4');
  assert.ok(json.nodes[channel.target.node]);
  const offset=bin+json.bufferViews[output.bufferView].byteOffset;
  for(let i=0;i<output.count;i++){
   const q=Array.from({length:4},(_,j)=>view.getFloat32(offset+16*i+4*j,true));
   assert.ok(Math.abs(Math.hypot(...q)-1)<.00001);
  }
 }
});
test('reject invalid generator settings and preserve greeting API',()=>{
 assert.throws(()=>createMotionVrma('walk',0));
 assert.throws(()=>createMotionVrma('unknown'));
 assert.equal(createWaveVrma().name,'mysticego-greeting.vrma');
});
