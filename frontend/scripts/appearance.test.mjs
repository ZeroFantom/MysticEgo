import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const bytes=p=>readFileSync(new URL('../../'+p,import.meta.url));
const read=p=>bytes(p).toString('utf8');
const groups=[
 ['frontend/packages/ui/src/app/appearance.css','frontend/packages/ui/src/app/appearance-art','frontend/packages/ui/src/pages/Settings.tsx'],
 ['desktop/control/style.css','desktop/control/appearance-art','desktop/control/effects.js'],
 ['tools/MysticEgo.CloudDesk/src/style.css','tools/MysticEgo.CloudDesk/src/appearance-art','tools/MysticEgo.CloudDesk/src/Appearance.jsx']
];
for(const [css,assets,settings] of groups){
 const styles=read(css),controls=read(settings);
 for(const color of ['dark','light'])for(const style of ['immersive','simplified']){
  const name=style+'-'+color+'.webp';
  assert(styles.includes('[data-interface="'+style+'"][data-theme="'+color+'"]'),css+' missing '+style+'/'+color);
  const runtimeName=style==='simplified'&&color==='dark'?'simplified-dark.svg':name;
  assert(styles.includes(runtimeName),css+' asset missing');
  if(runtimeName.endsWith('.svg'))assert(read(assets+'/'+runtimeName).includes('<svg'));
  const art=bytes(assets+'/'+name);assert.equal(art.subarray(0,4).toString(),'RIFF');assert.equal(art.subarray(8,12).toString(),'WEBP');assert(art.length>4000);
 }
 for(const option of ['interfaceStyle','uiMotion','transition','system','simplified','immersive'])assert(controls.includes(option),settings+' missing '+option);
 assert(styles.includes('prefers-reduced-motion')&&styles.includes('data-ui-motion=off'),css+' motion override missing');
}
assert(read('desktop/local/index.html').includes('type="module" src="view.js"'));
assert(read('desktop/local/view.js').includes("initEffects($('design'))"));
assert(read('desktop/station/view.js').includes("initEffects($('design'))"));
assert(read('desktop/control/view.js').includes("initEffects($('design'))"));
assert(read('desktop/renderer/src/main.tsx').includes('overlay-appearance'));
assert(read('tools/MysticEgo.CloudDesk/update.sh').includes('src/appearance-art/simplified-light.webp'));
console.log('Appearance source contracts: four combinations, settings, motion, assets and desktop entrypoints');

for(const name of ['immersive-dark','immersive-light','simplified-light']){const media=bytes('frontend/packages/ui/src/app/appearance-art/'+name+'.webm');assert.equal(media.subarray(0,4).toString('hex'),'1a45dfa3');assert(media.length>10000)}
for(const role of ['orchestra','global','local','agent','audit']){const png=bytes('frontend/scripts/social-art/'+role+'.png');assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');assert.equal(png.readUInt32BE(16),1200);assert.equal(png.readUInt32BE(20),630)}

// Source-level guards for the shared web controls (visual browser checks remain necessary).
const web=read('frontend/packages/ui/src/app/appearance.css');
const settings=read('frontend/packages/ui/src/pages/Settings.tsx');
assert(settings.includes('className="appearance-art-preview"'));
const preview=web.match(/\.appearance-style-option>\.appearance-art-preview\{([^}]+)\}/)?.[1]??'';
assert(preview.includes('aspect-ratio:16/9')&&preview.includes('background-size:contain')&&preview.includes('background-repeat:no-repeat'));
assert(!/\.appearance-style-option>span\{height:/.test(web));
const quick=web.match(/:root \.appearance-quick>button\.icon-btn\{([^}]+)\}/)?.[1]??'';
assert(quick.includes('border-radius:50%')&&quick.includes('background:transparent'));
assert(web.includes('button.icon-btn[aria-pressed=true]'));
assert(web.includes('.account-avatar>img{display:block;width:100%;height:100%;object-fit:cover'));

const shell=read('frontend/packages/ui/src/app/Shell.tsx');
const workspace=read('frontend/packages/ui/src/app/workspace-polish.css');
assert(shell.includes("import './workspace-polish.css'"));
assert(shell.includes('aria-controls="workspace-navigation"')&&shell.includes('aria-expanded={drawerOpen}'));
assert(shell.includes('inert={narrow&&!drawerOpen}')&&shell.includes('inert={drawerOpen}'));
assert(shell.includes("event.key==='Escape'")&&shell.includes('closeDrawer.current?.focus()'));
assert(shell.includes("document.body.style.overflow=previousOverflow"));
assert(workspace.includes('object-fit:contain')&&workspace.includes('visibility:hidden')&&workspace.includes('visibility:visible'));
assert(workspace.includes('prefers-reduced-motion:reduce')&&workspace.includes('data-ui-motion=off'));
assert(workspace.includes('.auth-recovery')&&workspace.includes('grid-template-columns:minmax(0,1fr)'));

const profile=read('frontend/packages/ui/src/pages/Profile.tsx');
const profileCss=read('frontend/packages/ui/src/pages/profile-polish.css');
const profileDialog=read('frontend/packages/ui/src/components/ProfileDialog.tsx');
const collective=read('frontend/packages/ui/src/pages/Collective.tsx');
const chatCss=read('frontend/packages/ui/src/pages/collective-chat.css');
assert(profile.includes('profile-edit-preview')&&profile.includes('data-custom-cover')&&profile.includes('<ProfileDialog'));
assert(profileDialog.includes('element.showModal()')&&profileDialog.includes('onCancel='));
assert(profileCss.includes('background-size:cover')&&profileCss.includes('dialog.profile-dialog::backdrop'));
assert(collective.includes('chat-compose-tools')&&collective.includes('draftSnapshot.current')&&collective.includes('historyRequest.current!==request'));
assert(chatCss.includes('grid-template-columns:auto minmax(0,1fr) auto')&&chatCss.includes('chat-compose-tools:has(.voice-draft)'));
assert(chatCss.includes('data-ui-motion=off')&&chatCss.includes('prefers-reduced-motion:reduce'));

const desktopCss=read('desktop/control/style.css'),desktopEffects=read('desktop/control/effects.js');
const cloudCss=read('tools/MysticEgo.CloudDesk/src/style.css'),cloudControls=read('tools/MysticEgo.CloudDesk/src/Appearance.jsx');
const overlay=read('desktop/renderer/src/overlay.css'),updater=read('tools/MysticEgo.CloudDesk/update.sh');
assert(desktopEffects.includes('styleButtons.set(key,button)')&&desktopEffects.includes("button.setAttribute('aria-pressed'"));
assert(desktopEffects.includes('state={...defaults}')&&desktopEffects.includes('sync();'));
assert(cloudControls.includes('aria-pressed={look.interfaceStyle===value}')&&cloudControls.includes('useLayoutEffect'));
for(const css of [desktopCss,cloudCss]){
 assert(css.includes('aspect-ratio:16/9')&&css.includes('background-size:contain')&&css.includes('simplified-dark.svg'));
}
assert(desktopCss.includes('object-fit:contain')&&cloudCss.includes('aside .bottom{display:block'));
assert(overlay.includes('simplified-dark.svg')&&overlay.includes('border-radius:50%'));
assert(updater.includes('src/appearance-art/simplified-dark.svg')&&updater.includes('src/AuditPortal.jsx'));

const motion=read('frontend/packages/ui/src/app/motion.css');
const notifications=read('frontend/packages/ui/src/app/notifications.tsx');
const cinematic=read('frontend/packages/ui/src/app/CinematicPreview.tsx');
assert(shell.includes("import './motion.css'")&&shell.includes('data-motion-open={open} inert={!open}'));
assert(notifications.includes('data-motion-open={open} inert={!open}')&&notifications.includes('aria-controls={drawerId}'));
assert(motion.includes('@starting-style')&&motion.includes('allow-discrete')&&motion.includes('pointer-events:none'));
assert(motion.includes('var(--mystic-page-time) var(--mystic-ease) backwards')&&motion.includes('--motion-step:0ms'));
assert(motion.includes('data-ui-motion=off')&&motion.includes('prefers-reduced-motion:reduce'));
assert(cinematic.includes('IntersectionObserver')&&cinematic.includes('visibilitychange')&&cinematic.includes('element.pause()'));
assert(cinematic.includes('preload="metadata"')&&!cinematic.includes('autoPlay'));
assert(cinematic.includes('loading="lazy"')&&motion.includes('aspect-ratio:16/9')&&motion.includes('object-fit:contain'));
