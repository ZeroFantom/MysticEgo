import {spawnSync} from 'node:child_process';
import assert from 'node:assert/strict';
const [dotnet,dll]=process.argv.slice(2);
const actions=['platform.selftest','github.selftest','update.candidate.selftest','topology.selftest','catalog.selftest','dns.selftest','naming.selftest','timeweb.selftest','order.selftest','binding.selftest','dns.delete.selftest'];
const input=actions.map(action=>JSON.stringify({id:action,action,args:{}})).join('\n')+'\n';
const r=spawnSync(dotnet,[dll],{input,encoding:'utf8',timeout:120000,windowsHide:true});
assert.equal(r.status,0,r.error?.message||r.stderr);
const results=r.stdout.trim().split('\n').map(JSON.parse);
for(const action of actions){const response=results.find(x=>x.id===action&&typeof x.ok==='boolean');assert(response?.ok,action+': '+response?.error);}
console.log('CloudDesk IPC regressions passed on '+process.platform);
