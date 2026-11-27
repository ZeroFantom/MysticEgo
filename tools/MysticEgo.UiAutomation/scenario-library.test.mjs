import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { ScenarioLibrary } from './scenario-library.mjs';

const scenario = {version:1,target:{kind:'web',id:'https://example.test/'},steps:[{name:'ui_click',args:{role:'button',name:'Overview'},delayMs:0}]};
test('library persists validated scenario across instances and rejects path traversal',async () => {
  const dir = await mkdtemp(join(tmpdir(),'mysticego-scenarios-'));
  try {
    const library = new ScenarioLibrary(dir);
    const id = await library.save('Overview test',scenario);
    const reopened = new ScenarioLibrary(dir);
    assert.deepEqual(await reopened.load(id),scenario);
    const items = await reopened.list();
    assert.equal(items.length,1);
    assert.equal(items[0].id,id);
    assert.equal(items[0].steps,1);
    await assert.rejects(reopened.load('../outside'),/Invalid scenario ID/);
    await assert.rejects(reopened.save('bad',{...scenario,steps:[]}));
    await reopened.remove(id);
    assert.deepEqual(await reopened.list(),[]);
  } finally { await rm(dir,{recursive:true,force:true}); }
});
test('library rejects corrupt and oversized files',async () => {
  const dir = await mkdtemp(join(tmpdir(),'mysticego-scenarios-'));
  try {
    const library = new ScenarioLibrary(dir);
    const id = await library.save('Overview test',scenario);
    await writeFile(join(dir,id+'.json'),'x'.repeat(65537));
    await assert.rejects(library.load(id),/Invalid scenario file/);
  } finally { await rm(dir,{recursive:true,force:true}); }
});
