const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
const {createLocalCenter,legacyData,validSettings}=require('../../tools/MysticEgo.CloudDesk/desktop/local-center.cjs');
async function fixture(t,extra={}){
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'mystic-local-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));
 const data=path.join(dir,'center'),root=path.join(dir,'bundle');
 for(const file of ['runtime/app/bin/MysticEgo.Installer.dll','runtime/app/bin/Api.Auth.dll','runtime/app/bin/Api.Orchestra.dll','runtime/app/web/orchestra/index.html','runtime/app/web/audit/index.html']){
  const target=path.join(root,file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.writeFile(target,'fixture');
 }
 let launched=0,stopped=0,args,env,copied='';
 const center=createLocalCenter({root,data,dotnet:'dotnet',home:dir,env:{MYSTICEGO_CENTER_ORCHESTRA_URL:'https://remote.example'},probeService:async()=>false,isOccupied:async()=>false,openExternal:async()=>{},openPath:async()=>'',copy:v=>copied=v,spawnService:(exe,a,cwd,log,e)=>{launched++;args=a;env=e;let finish;const done=new Promise(r=>finish=r);const child={exitCode:null,stdin:{writable:true,end:()=>{stopped++;child.stdin.writable=false;child.exitCode=0;finish()}}};return {done,child}},...extra});
 return {center,data,root,state:()=>({launched,stopped,args,env,copied})};
}
test('legacy data and explicit network settings',()=>{
 assert.equal(legacyData('/home/test',{XDG_DATA_HOME:'/data'}),path.join('/data','MysticEgo','BETA7-Lab','center'));
 assert.throws(()=>validSettings({host:'bad host',lan:true}));assert.throws(()=>validSettings({host:'192.168.1.2',lan:false}));
 assert.deepEqual(validSettings({}),{host:'',lan:false,localModel:false});
});
test('offline start, no auto-start, no duplicate, no model download by default, owned stop',async t=>{
 const {center,state}=await fixture(t);assert.equal((await center.snapshot()).installed,true);assert.equal(state().launched,0);
 await Promise.all([center.start(),center.start()]);assert.equal(state().launched,1);
 for(const arg of ['--lab','--no-lm','--parent-stdin'])assert(state().args.includes(arg));
 assert.equal(state().env.MYSTICEGO_ALLOW_LAN_HTTP,'false');assert(!state().env.MYSTICEGO_CENTER_ORCHESTRA_URL);
 await assert.rejects(center.save({lan:true,host:'192.168.1.2'}));
 await center.stop();assert.equal(state().stopped,1);await center.save({lan:true,host:'192.168.1.2',localModel:true});assert.equal((await center.snapshot()).settings.host,'192.168.1.2');
});
test('external center cannot be stopped',async t=>{const {center,state}=await fixture(t,{probeService:async()=>true});await center.start();await center.stop();assert.equal(state().launched,0);assert.equal(state().stopped,0);assert.equal((await center.snapshot()).owned,false)});
test('occupied port blocks launch',async t=>{const {center,state}=await fixture(t,{isOccupied:async()=>true});await assert.rejects(center.start(),/заняты/);assert.equal(state().launched,0)});
test('password preserved and never included in snapshot',async t=>{const {center,data,state}=await fixture(t);await center.snapshot();await fs.writeFile(path.join(data,'owner-password.txt'),'synthetic-secret');assert.equal(await center.credentials('show'),'synthetic-secret');await center.credentials('password');assert.equal(state().copied,'synthetic-secret');assert(!JSON.stringify(await center.snapshot()).includes('synthetic-secret'))});
test('incomplete runtime produces actionable error',async t=>{const {center,root}=await fixture(t);await fs.rm(path.join(root,'runtime/app/web/audit/index.html'));assert.equal((await center.snapshot()).installed,false);await assert.rejects(center.start(),/runtime\/app/)});
