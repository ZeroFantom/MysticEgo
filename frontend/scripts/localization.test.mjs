import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {catalog} from '../packages/ui/src/app/localeCatalog.ts';
const desktop=await readFile(new URL('../../desktop/control/presentation.js',import.meta.url),'utf8');
const nativeCatalog=JSON.parse(desktop.slice(desktop.indexOf('={')+1,desktop.indexOf(';\nexport const languages')));
test('Every catalog entry has English, Chinese, Korean and a Russian source or explicit translation',()=>{
 for(const dictionary of [catalog,nativeCatalog])for(const [key,values] of Object.entries(dictionary)){
  assert.ok(key.trim());assert.ok(values.length===3||values.length===4,key);if(key.startsWith('permission.'))assert.equal(values.length,4,key);
  assert.ok(values.every(value=>typeof value==='string'&&value.trim()),key);
 }
});
test('Web and desktop shared labels agree',()=>{
 for(const key of ['Язык','Шрифт','Настройки','Войти','Выйти','Тема','Палитра','Ассистент','permission.avatar-desktop','permission.screen-capture','permission.replay','permission.vendor-updates']){
  assert.deepEqual(catalog[key],nativeCatalog[key],key);
 }
});

test('Shared shell and dialogs have complete translation keys',()=>{for(const key of ['Настройки приватности','Только необходимые','Сохранить настройки','Что нового','Ознакомился','Закрыть','Бизнес-граф','Development','Tasks & discussions','AI и речь','Мониторинг','Макро контроль','Микро контроль','Автотесты','Capacity & resilience'])assert.ok(catalog[key]?.slice(0,3).every(Boolean),key)})
