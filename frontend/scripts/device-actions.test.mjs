import assert from 'node:assert/strict';
import {test} from 'node:test';
import {readFileSync} from 'node:fs';
import {advertisedActions,isAction,nativeActionLink,navigationActions} from '../packages/ui/src/app/deviceActionPolicy.ts';
test('only bounded actions advertised and Android native links match Java allowlist',()=>{
 assert.equal(isAction('shell'),false);assert.equal(isAction('__proto__'),false);
 assert.deepEqual(advertisedActions(false),[...Object.keys(navigationActions),'signal-device']);
 assert.equal(nativeActionLink('open-app-settings',false),null);
 const java=readFileSync(new URL('../../mobile/android/app/src/main/java/ru/isdarkline/mysticego/MobileLocalActions.java',import.meta.url),'utf8');
 for(const action of advertisedActions(true)){const link=nativeActionLink(action,true);if(link)assert.ok(java.includes('"'+link+'"'))}
 assert.equal(nativeActionLink('open-app-settings',true),'mysticego://local-action/open-app-settings');
 assert.equal(nativeActionLink('open-assistant',true),null);
});
