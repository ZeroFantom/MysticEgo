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
for(const step of ['verify-identity','verify-storage','complete'])assert(resetScript.includes('CLOUDDESK_RESET_STEP:'+step));
assert(resetScript.indexOf('CLOUDDESK_RESET_STEP:verify-identity')<resetScript.indexOf('CLOUDDESK_RESET_STEP:verify-storage'));
assert(resetScript.indexOf('CLOUDDESK_RESET_STEP:verify-storage')<resetScript.indexOf('CLOUDDESK_RESET_STEP:complete'));
assert(resetScript.indexOf('CLOUDDESK_RESET_STEP:complete')<resetScript.lastIndexOf('FACTORY_RESET_OK'));
assert(resetScript.includes('post-install Auth SQLite store missing')&&resetScript.includes('post-install Orchestra SQLite store missing'));
assert(!resetScript.includes('if systemctl is-enabled --quiet "$u"'));


assert(backend.includes('progress?.Invoke("identity")')&&backend.includes('progress("access")')&&backend.includes('progress?.Invoke("ready")'));
assert(ipc.includes("'ssh-progress'")&&ipc.includes('flowId:p.flowId'));
assert(ui.includes('onSshProgress?.')&&ui.includes("advance('access');advance('ready')")&&ui.includes('Подключение установлено'));
assert(ui.includes('<DesktopUpdate/>')&&ipc.includes('registerUpdateIpc'));

const install=read('tools/MysticEgo.CloudDesk/profiles/orchestra-r3.sh');
const recovery=read('ops/orchestra-updater/recover-maintenance.sh');
assert(backend.includes('role=="Orchestra"?await LatestResetRelease()'),'Fresh Orchestra must not use the historical bundle');
assert(backend.includes('MYSTICEGO_INSTALL_LOCK_FD=9')&&recovery.includes('--inherited-deploy-lock'),'Deployment must not reacquire its own lock');
assert(install.includes('libatomic1 libgomp1')&&resetScript.includes('libatomic1 libgomp1'));
assert(resetScript.indexOf('flock -n 8')<resetScript.indexOf('CLOUDDESK_RESET_STEP:stop'),'Local provisioning lock must be checked before stopping services');
assert(install.indexOf('CLOUDDESK_STEP:verify-storage')<install.indexOf('CLOUDDESK_STEP:maintenance'));
assert(install.indexOf('CLOUDDESK_STEP:maintenance')<install.lastIndexOf('ORCHESTRA_INSTALL_OK'));
assert(install.includes('for attempt in 1 2')&&install.includes('CLOUDDESK_MAINTENANCE_PENDING')&&install.includes('CLOUDDESK_MAINTENANCE_SCHEDULED'));
assert(factoryMarkerCheck());
function factoryMarkerCheck(){return reset.includes('InstallIdentity(output,"FACTORY_RESET_OK")')&&reset.includes('InstallIdentity(output,"ORCHESTRA_INSTALL_OK")!=newNode')}
assert(ui.includes('maintenance-pending')&&ui.includes('maintenance-scheduled')&&ui.includes('Восстановить службы через SSH'));
assert(updater.includes('action is "legacy-repair" or "unavailable"')&&updater.includes('ownerNode!=node'));
assert(reports.includes('c.nextRetryAt')&&reports.includes('c.reason'));
console.log('CloudDesk verified install, maintenance recovery and pending-state contracts passed.');
