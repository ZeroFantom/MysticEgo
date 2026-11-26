import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const groups=[
 ['frontend/packages/ui/src/app/appearance.css','frontend/packages/ui/src/app/appearance-art','frontend/packages/ui/src/pages/Settings.tsx'],
 ['desktop/control/style.css','desktop/control/appearance-art','desktop/control/effects.js'],
 ['tools/MysticEgo.CloudDesk/src/style.css','tools/MysticEgo.CloudDesk/src/appearance-art','tools/MysticEgo.CloudDesk/src/Appearance.jsx']
];
for(const [css,assets,settings] of groups){
 const styles=read(css),controls=read(settings);
 for(const color of ['dark','light'])for(const style of ['immersive','simplified']){
  const name=style+'-'+color+'.svg';
  assert(styles.includes('[data-interface="'+style+'"][data-theme="'+color+'"]'),css+' missing '+style+'/'+color);
  assert(styles.includes(name),css+' asset missing');
  assert(read(assets+'/'+name).startsWith('<svg '));
 }
 for(const option of ['interfaceStyle','uiMotion','transition','system','simplified','immersive'])assert(controls.includes(option),settings+' missing '+option);
 assert(styles.includes('prefers-reduced-motion')&&styles.includes('data-ui-motion=off'),css+' motion override missing');
}
assert(read('desktop/local/index.html').includes('type="module" src="view.js"'));
assert(read('desktop/local/view.js').includes("initEffects($('design'))"));
assert(read('desktop/station/view.js').includes("initEffects($('design'))"));
assert(read('desktop/control/view.js').includes("initEffects($('design'))"));
assert(read('desktop/renderer/src/main.tsx').includes('overlay-appearance'));
assert(read('tools/MysticEgo.CloudDesk/update.sh').includes('src/appearance-art/simplified-light.svg'));
console.log('Appearance source contracts: four combinations, settings, motion, assets and desktop entrypoints');
