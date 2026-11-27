import assert from 'node:assert/strict';
import {test} from 'node:test';
import {appearanceDefaults,mergeAppearance} from '../packages/ui/src/app/appearancePreferences.ts';

test('empty or partial account responses preserve previously stored appearance',()=>{
 const saved=mergeAppearance(appearanceDefaults,{theme:'light',interfaceStyle:'immersive',intensity:70});
 assert.deepEqual(mergeAppearance(saved,{}),saved);
 const merged=mergeAppearance(saved,{theme:'dark'});
 assert.equal(merged.theme,'dark');
 assert.equal(merged.interfaceStyle,'immersive');
 assert.equal(merged.intensity,70);
 assert.equal(saved.theme,'light','must not mutate the original');
});
test('only valid typed appearance fields are applied',()=>{
 const patch=JSON.parse('{"theme":"invalid","intensity":101,"primary":"url(unsafe)","ambient":"false","size":null,"language":"ko","__proto__":{"theme":"light"}}');
 assert.deepEqual(mergeAppearance(appearanceDefaults,patch),appearanceDefaults);
 const valid=mergeAppearance(appearanceDefaults,{theme:'system',primary:'#123abc',secondary:'#ABCDEF',intensity:10,ambient:false,density:'compact',palette:'custom'});
 assert.equal(valid.theme,'system');assert.equal(valid.primary,'#123abc');assert.equal(valid.secondary,'#ABCDEF');assert.equal(valid.intensity,10);assert.equal(valid.ambient,false);assert.equal(valid.density,'compact');assert.equal(valid.palette,'custom');
});
