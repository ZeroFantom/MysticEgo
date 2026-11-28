import test from 'node:test';
import assert from 'node:assert/strict';
import {walkDistance,walkProgress} from '../packages/ui/src/avatar/spatialMotion.ts';
test('bounded same-height walk hints',()=>{
 const from={x:0,y:0,z:0};
 assert.equal(walkDistance(from,{x:.6,y:.01,z:.8}),1);
 assert.equal(walkDistance(from,{x:0,y:0,z:0}),null);
 assert.equal(walkDistance(from,{x:1.01,y:0,z:0}),null);
 assert.equal(walkDistance(from,{x:.5,y:.1,z:0}),null);
 assert.equal(walkDistance(from,{x:NaN,y:0,z:0}),null);
 assert.equal(walkDistance(from,{x:.5,y:0,z:Infinity}),null);
});
test('walk interpolation stays bounded',()=>{
 assert.equal(walkProgress(-1,2),0);
 assert.equal(walkProgress(1,2),.5);
 assert.equal(walkProgress(3,2),1);
 assert.equal(walkProgress(1,0),0);
 assert.equal(walkProgress(NaN,2),0);
});
