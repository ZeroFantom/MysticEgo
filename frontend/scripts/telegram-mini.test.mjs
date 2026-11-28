import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const read=name=>readFile(new URL('../telegram-mini/'+name,import.meta.url),'utf8');
const [html,app,api,css]=await Promise.all(['index.html','main.tsx','mini-api.ts','mini.css'].map(read));
assert(html.includes('https://telegram.org/js/telegram-web-app.js'));
assert(html.includes('no-referrer')&&html.includes('noindex'));
assert(app.includes('attempted.current=true')&&app.includes('onClick={()=>void connect()}'));
assert(app.includes('secret.current=null')&&app.includes('30'+'000'));
assert(api.includes("credentials:'omit'")&&api.includes("redirect:'error'")&&api.includes('X-Mystic-Mini-Session'));
assert(api.includes('/api/auth/messenger/telegram/mini/'));
for(const source of [app,api]){
  assert(!/localStorage|sessionStorage|initDataUnsafe|console\.(log|error)|document\.cookie/.test(source));
}
assert(css.includes('prefers-reduced-motion')&&css.includes('tg-content-safe-area-inset'));
console.log('Telegram Mini App isolated frontend contract passed');
