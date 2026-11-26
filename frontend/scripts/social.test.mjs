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
    assert(html.includes('content="1200"')&&html.includes('content="630"'));
  }
  const png=coverPng(role);
  assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');
  assert.equal(png.readUInt32BE(16),1200);assert.equal(png.readUInt32BE(20),630);
  const length=png.readUInt32BE(33);assert.equal(png.toString('ascii',37,41),'IDAT');
  assert.equal(inflateSync(png.subarray(41,41+length)).length,(1200*3+1)*630);
  assert(png.length<2_000_000);
}
assert.throws(()=>metadata('bad'));assert.throws(()=>metadata('global','studio'));assert.throws(()=>panelHtml('bad','audit'));
for(const page of ['root','share']){
  const html=auditCaddyHtml(template,page).replaceAll('{{placeholder "http.vars.mystic_public_origin"}}','https://audit.example.test');
  assert(html.includes('content="https://audit.example.test/audit-portal/og/audit.png"'));
  assert(html.includes('content="https://audit.example.test/audit-portal/'+(page==='share'?'share':'')+'"'));
  assert(!html.includes('{{placeholder'));
}
console.log('Social HTML and five PNG cover contracts passed');
