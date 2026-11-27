// Static IPC contract checks complement the real offline C# selftests.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const ipc=read('tools/MysticEgo.CloudDesk/desktop/main.cjs');
const backend=read('tools/MysticEgo.CloudDesk/backend/Program.cs');
const updater=read('tools/MysticEgo.CloudDesk/backend/Reupload.cs');
const ui=read('tools/MysticEgo.CloudDesk/src/main.jsx');
for(const action of ['deploy.inspectOrchestra','deploy.recoverMaintenance','deploy.restoreOrchestra','deploy.maintenancePlan','deploy.executeMaintenance']){
 assert(ipc.match(/const allowed=new Set\(\[([^\]]+)\]\)/)?.[1].includes("'"+action+"'"),action+' missing IPC permission');
 assert(backend.includes('case "'+action+'":'),action+' missing backend handler');
}
assert(ipc.match(/if\(\[([^\]]+)\]\.includes\(p\.action\)/)?.[1].includes("'deploy.recoverMaintenance'"),'Missing recovery progress');
assert(!updater.includes('bash bootstrap-r31.sh'),'Legacy partial deployment still reachable');
assert(updater.includes('new{tag,digest,confirmed=true,target="orchestra"}'));
assert(ui.includes("runSSH('deploy.executeMaintenance'"));
assert(ui.includes("candidateTag:candidate?.tag??'',candidateDigest:candidate?.digest??''"));
assert(updater.includes('/api/v1/orchestra/update/plan'));
console.log('CloudDesk recovery IPC and update approval contracts passed.');

const reports=read('tools/MysticEgo.CloudDesk/src/MaintenanceReports.jsx');
const reportCss=read('tools/MysticEgo.CloudDesk/src/maintenance-reports.css');
assert(ui.includes('<MaintenanceReports plan={maintenancePlan}'));
assert(reports.includes('maintenance-card-grid')&&reports.includes('onReport(s.unit)')&&reports.includes('onReport(c.name)'));
assert(reportCss.includes('minmax(min(100%,245px),1fr)')&&reportCss.includes('overflow-wrap:anywhere'));

const reset=read('tools/MysticEgo.CloudDesk/backend/FactoryReset.cs');
const resetScript=read('tools/MysticEgo.CloudDesk/profiles/orchestra-reset.sh');
for(const action of ['deploy.resetPreview','deploy.factoryReset']){assert(ipc.includes("'"+action+"'"));assert(backend.includes('case "'+action+'":'));assert(ui.includes("runSSH('"+action+"'"));}
assert(reset.includes('ownerOnly:true')&&reset.includes('release!=approval.Release')&&reset.includes('resetApproval=null'));
assert(ipc.includes('defaultId:0,cancelId:0,message:'));
assert(resetScript.indexOf('sha256sum -c -')<resetScript.indexOf('CLOUDDESK_RESET_STEP:delete'));
assert(resetScript.includes('PostgreSQL configuration present')&&resetScript.includes('custom/shared Caddy configuration'));

assert(backend.includes('progress?.Invoke("identity")')&&backend.includes('progress("access")')&&backend.includes('progress?.Invoke("ready")'));
assert(ipc.includes("'ssh-progress'")&&ipc.includes('flowId:p.flowId'));
assert(ui.includes('onSshProgress?.')&&ui.includes("advance('access');advance('ready')")&&ui.includes('Подключение установлено'));
assert(ui.includes('<DesktopUpdate/>')&&ipc.includes('registerUpdateIpc'));
