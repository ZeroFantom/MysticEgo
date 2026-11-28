import assert from 'node:assert/strict';
import {test} from 'node:test';
import {readFileSync} from 'node:fs';
import {ANDROID_ORIGIN,ANDROID_PACKAGE,ANDROID_FALLBACK,ANDROID_ROUTES,androidLaunch,androidRoute,isAndroidBrowser,shouldOfferAndroid} from '../packages/ui/src/app/androidLaunch.ts';
import {classifyBrowser} from '../packages/ui/src/app/browserPolicy.ts';
const ua='Mozilla/5.0 (Linux; Android 15; Pixel) Chrome/130.0 Mobile Safari/537.36';
test('Android hint only for the canonical HTTPS site, never inside the installed app',()=>{
 assert.equal(shouldOfferAndroid(ANDROID_ORIGIN,ua),true);
 assert.equal(shouldOfferAndroid(ANDROID_ORIGIN,ua+' MysticEgoAndroid/1'),false);
 assert.equal(shouldOfferAndroid('http://orchestra.isdarkline.ru',ua),false);
 assert.equal(shouldOfferAndroid('https://orchestra.isdarkline.ru.example',ua),false);
 assert.equal(shouldOfferAndroid(ANDROID_ORIGIN,'Mozilla iPhone Safari'),false);
 assert.equal(shouldOfferAndroid(ANDROID_ORIGIN,'Mozilla desktop','Android','/desktop-downloads'),false);
 assert.equal(isAndroidBrowser('Mozilla desktop','Android'),true);
 assert.equal(classifyBrowser(ua+' MysticEgoAndroid/1'),'android');
});
test('launch links preserve only known routes and fixed fallback',()=>{
 assert.equal(androidRoute('/audit-portal/assistant'),'audit-assistant');
 assert.equal(androidRoute('/social'),'collective');
 assert.equal(androidRoute('/audit-portal/directory'),'audit-collective');
 for(const path of ['/agents/private-id','/assistant?token=private-marker','https://evil.invalid','/../secret','/audit-portal/secret/private-id']){
  const url=androidLaunch(path);assert.ok(!url.includes('private-marker')&&!url.includes('evil.invalid')&&!url.includes('private-id'));
  assert.match(url,/^intent:\/\/open\/(orchestra|home)#Intent;/);
 }
 assert.ok(androidLaunch('/assistant').includes('package='+ANDROID_PACKAGE+';'));
 assert.ok(androidLaunch('/assistant').includes('S.browser_fallback_url='+encodeURIComponent(ANDROID_FALLBACK)));
 const java=readFileSync(new URL('../../mobile/android/app/src/main/java/ru/isdarkline/mysticego/MobileRoutes.java',import.meta.url),'utf8');
 for(const [key,value] of Object.entries(ANDROID_ROUTES))assert.ok(java.includes('routes.put("'+key+'", "'+value+'")'),key);
});
