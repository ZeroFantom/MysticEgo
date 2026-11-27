import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {catalog} from '../packages/ui/src/app/localeCatalog.ts';
const desktop=await readFile(new URL('../../desktop/control/presentation.js',import.meta.url),'utf8');
const nativeCatalog=JSON.parse(desktop.slice(desktop.indexOf('={')+1,desktop.indexOf(';\nexport const languages')));
test('Every catalog entry has three nonempty translations plus Russian source',()=>{
 for(const dictionary of [catalog,nativeCatalog])for(const [key,values] of Object.entries(dictionary)){
  assert.ok(key.trim());assert.equal(values.length,3,key);
  assert.ok(values.every(value=>typeof value==='string'&&value.trim()),key);
 }
});
test('Web and desktop shared labels agree',()=>{
 for(const key of ['Язык','Шрифт','Настройки','Войти','Выйти','Тема','Палитра','Ассистент']){
  assert.deepEqual(catalog[key],nativeCatalog[key],key);
 }
});
