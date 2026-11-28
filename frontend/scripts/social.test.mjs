import {readFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {inflateSync} from 'node:zlib';
import {roles,publicPages,metadata,panelHtml,auditCaddyHtml,coverPng} from './social.mjs';
const template='<html data-role="orchestra"><head><title>MysticEgo</title></head><body><div id="root"></div></body></html>';
for(const role of Object.keys(roles)){
  for(const page of ['root','share',...(role==='orchestra'?Object.keys(publicPages):[])]){
    const html=panelHtml(template,role,page);
    assert(html.includes('data-role="'+role+'"'));
    for(const tag of ['og:title','og:description','og:image','og:url','og:image:width','twitter:card'])assert(html.includes(tag),role+' '+tag);
    assert.equal((html.match(/property="og:image"/g)||[]).length,1);
    assert(html.includes('<!-- mysticego-social-start -->'));
    assert(html.includes('<h1>')&&html.includes('<meta name="description"'),'Initial HTML must contain text without JavaScript');
    if(role==='orchestra'&&Object.hasOwn(publicPages,page)){assert(html.includes('<h1>'));assert(html.includes('index,follow,max-image-preview:large'));assert(html.includes('href="/desktop-downloads"'));}
    else {
      assert(html.includes('noindex,follow'));
      assert.match(html,/<div id="root"><div class="mystic-panel-boot"[^>]*><span[^>]*><\/span><\/div><\/div><noscript>/);
      assert.match(html,/<noscript>[\s\S]*?<h1>[\s\S]*?<\/noscript>/);
      assert(!html.split('<noscript>')[0].includes('<h1>'),'No visible social text before JavaScript');
      assert(html.includes('prefers-reduced-motion:reduce'));
    }
    assert(html.includes('content="1200"')&&html.includes('content="630"'));
  }
  const actual=await readFile(new URL('./social-art/'+role+'.png',import.meta.url));
  assert.equal(actual.subarray(0,8).toString('hex'),'89504e470d0a1a0a');
  assert.equal(actual.readUInt32BE(16),1200);assert.equal(actual.readUInt32BE(20),630);
  assert(actual.length>1000&&actual.length<5_000_000,'Published cover must be a valid, compact PNG: '+role);
  const png=coverPng(role);
  assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');
  assert.equal(png.readUInt32BE(16),1200);assert.equal(png.readUInt32BE(20),630);
  const length=png.readUInt32BE(33);assert.equal(png.toString('ascii',37,41),'IDAT');
  assert.equal(inflateSync(png.subarray(41,41+length)).length,(1200*3+1)*630);
  assert(png.length<2_000_000);
}
const licensePage=panelHtml(template,'orchestra','license');
assert(licensePage.includes('Лицензия MysticEgo'));
assert(licensePage.includes('PolyForm Noncommercial 1.0.0'));
assert(licensePage.includes('href="/license"'));
assert(!licensePage.includes('content="noindex"'));
assert.throws(()=>metadata('bad'));assert.throws(()=>metadata('global','studio'));assert.throws(()=>panelHtml('bad','audit'));
for(const page of ['root','share']){
  const html=auditCaddyHtml(template,page).replaceAll('{{placeholder "http.vars.mystic_public_origin"}}','https://audit.example.test');
  assert(html.includes('content="https://audit.example.test/audit-portal/og/audit.png"'));
  assert(html.includes('content="https://audit.example.test/audit-portal/'+(page==='share'?'share':'')+'"'));
  assert(!html.includes('{{placeholder'));
}
console.log('Social HTML and five PNG cover contracts passed');
