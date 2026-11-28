import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch, Mock

spec = importlib.util.spec_from_file_location("updater", Path(__file__).with_name("update-orchestra.py"))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)

class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        root = Path(self.folder.name)
        self.release = "v1.0.0-beta.7-r3.2.37"
        self.app = root / "app"
        self.state = root / "state"
        self.state.mkdir()
        self.inbox = root / "inbox"
        self.inbox.mkdir()
        self.report = root / "report.json"
        for name, value in {"APP":self.app,"STATE":self.state,"INBOX":self.inbox,
                            "MAINTENANCE_STATUS":self.report,"PREVIOUS":{}}.items():
            patcher = patch.object(updater, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)

    def status(self):
        return json.loads((self.state / "maintenance.json").read_text())

    @patch.object(updater, "trusted_file", return_value=True)
    @patch.object(updater, "maintenance_ready", side_effect=[False, True])
    @patch.object(updater, "command")
    def test_missing_install_is_repaired(self, command, ready, trust):
        self.assertTrue(updater.ensure_maintenance(self.release))
        command.assert_called_once_with("bash", str(self.app / "ops/infrastructure/install-local.sh"), timeout=90)
        self.assertEqual(self.status()["state"], "waiting")
        self.assertEqual(self.status()["release"], self.release)

    @patch.object(updater, "trusted_file", return_value=True)
    @patch.object(updater, "maintenance_ready", return_value=False)
    @patch.object(updater, "command", side_effect=subprocess.TimeoutExpired("bash", 90))
    def test_failure_retried_with_cooldown(self, command, ready, trust):
        self.assertFalse(updater.ensure_maintenance(self.release))
        self.assertEqual(self.status()["state"], "degraded")
        self.assertEqual(self.status()["reason"], "TimeoutExpired")
        self.assertFalse(updater.ensure_maintenance(self.release))
        command.assert_called_once()
        self.assertFalse(updater.ensure_maintenance(self.release, force=True))
        self.assertEqual(command.call_count, 2)

    @patch.object(updater, "trusted_file", return_value=False)
    @patch.object(updater, "command")
    def test_untrusted_package_not_executed(self, command, trust):
        self.assertFalse(updater.ensure_maintenance(self.release))
        command.assert_not_called()
        self.assertEqual(self.status()["reason"], "trusted-package-incomplete")

    @patch.object(updater, "trusted_file", return_value=True)
    @patch.object(updater, "maintenance_ready", return_value=True)
    @patch.object(updater, "command")
    def test_fresh_report_no_install(self, command, ready, trust):
        self.report.write_text('{"components":[{"name":"orchestra","state":"available"}]}')
        self.assertTrue(updater.ensure_maintenance(self.release))
        command.assert_not_called()
        self.assertEqual(self.status()["state"], "ready")

    @patch.object(updater, "trusted_file", return_value=True)
    @patch.object(updater, "maintenance_ready", return_value=True)
    @patch.object(updater, "system_ok", return_value=False)
    @patch.object(updater, "schedule_reconcile")
    def test_missing_report_starts_reconcile(self, schedule, system_ok, ready, trust):
        self.assertTrue(updater.ensure_maintenance(self.release))
        schedule.assert_called_once_with(self.release)
        self.assertEqual(self.status()["state"], "waiting")

    def test_corrupt_report_not_fresh(self):
        self.report.write_text("{}")
        self.assertFalse(updater.report_fresh())
        self.report.write_text("invalid json")
        self.assertFalse(updater.report_fresh())

    def test_updater_does_not_consume_work_while_infrastructure_locked(self):
        import fcntl
        deploy = self.state / "deploy.lock"
        infra = self.state / "infra.lock"
        with patch.object(updater, "DEPLOYMENT_LOCK", deploy), patch.object(updater, "INFRASTRUCTURE_LOCK", infra), patch.object(updater, "run_locked") as work:
            with infra.open("a") as held:
                fcntl.flock(held, fcntl.LOCK_EX)
                updater.main()
                work.assert_not_called()
            updater.main()
            work.assert_called_once()

    def test_reconcile_scheduled_after_locks_released_even_if_offline(self):
        import fcntl
        deploy = self.state / "deploy.lock"
        infra = self.state / "infra.lock"
        def work():
            updater.schedule_reconcile(self.release)
            raise SystemExit(1)
        def start(*args):
            with infra.open("a") as held:
                fcntl.flock(held, fcntl.LOCK_EX | fcntl.LOCK_NB)
            return True
        with patch.object(updater, "DEPLOYMENT_LOCK", deploy), patch.object(updater, "INFRASTRUCTURE_LOCK", infra), patch.object(updater, "run_locked", side_effect=work), patch.object(updater, "system_ok", side_effect=start) as system:
            with self.assertRaises(SystemExit):
                updater.main()
            system.assert_called_once_with("start", "--no-block", updater.RECONCILE_SERVICE)

    @patch.object(updater, "ensure_maintenance")
    @patch.object(updater, "versions", return_value=("v1.0.0-beta.7-r3.2.37", None))
    @patch.object(updater, "newest", return_value=(None, []))
    @patch.object(updater, "take_request", return_value=None)
    def test_idle_tick_and_manual_repair(self, request, newest, versions, ensure):
        (self.inbox / "maintenance-repair.json").write_text('{"confirmed":true}')
        updater.run_locked()
        ensure.assert_called_once_with(self.release, force=True)
        self.assertEqual(json.loads((self.state/"status.json").read_text())["phase"],"current")
        self.assertFalse((self.inbox / "maintenance-repair.json").exists())

    @patch.object(updater, "ensure_maintenance")
    @patch.object(updater, "versions", return_value=("v1.0.0-beta.7-r3.2.37", None))
    @patch.object(updater, "newest", side_effect=updater.GitHubUnavailable("offline"))
    def test_offline_tick_still_repairs(self, newest, versions, ensure):
        with self.assertRaises(SystemExit):
            updater.run_locked()
        ensure.assert_called_once_with(self.release, force=False)

    def test_repair_request_is_fixed_and_no_symlink(self):
        path = self.inbox / "maintenance-repair.json"
        path.write_text('{"command":"anything"}')
        self.assertFalse(updater.take_maintenance_request())
        target = self.state / "protected"
        target.write_text('{"confirmed":true}')
        path.symlink_to(target)
        self.assertFalse(updater.take_maintenance_request())
        self.assertTrue(target.exists())

    @patch.object(updater, "system_ok", return_value=False)
    def test_auto_default_and_no_retry(self, system_ok):
        candidate = {"tag": "v1.0.0-beta.7-r3.2.39"}
        self.assertEqual(updater.automatic_target(candidate, ["orchestra"], {}), "orchestra")
        (self.inbox / "auto-update.json").write_text('{"enabled":false}')
        self.assertEqual(updater.automatic_target(candidate, ["orchestra"], {}), "orchestra")
        self.assertIsNone(updater.automatic_target(candidate, ["audit"], {}))
        self.assertIsNone(updater.automatic_target(candidate, ["orchestra"], {"candidate": candidate, "phase": "failed"}))
        (self.state / "automatic-attempt.json").write_text(json.dumps(candidate))
        self.assertIsNone(updater.automatic_target(candidate, ["orchestra"], {}))
        (self.inbox / "auto-update.json").unlink()
        target = self.state / "unsafe"
        target.write_text('{"enabled":true}')
        (self.inbox / "auto-update.json").symlink_to(target)
        self.assertTrue(updater.automatic_enabled())

    @staticmethod
    def published_release(suffix, draft=False, digest="sha256:"+"a"*64):
        return {"tag_name":"v1.0.0-beta.7-r3.2."+suffix,"draft":draft,
                "published_at":"2026-01-01T00:00:00Z",
                "assets":[{"name":"MysticEgo-Orchestra-linux-x64.zip","state":"uploaded",
                           "size":123,"digest":digest,"id":int(suffix)}]}

    def test_recovery_choices_exact_and_ordered(self):
        releases=[self.published_release(n) for n in ("38","36","37","35")]
        releases += [self.published_release("39",draft=True),self.published_release("40",digest="bad")]
        options=updater.recovery_choices((self.release,None),releases)
        self.assertEqual([r["tag"] for r in options["cycle"]],
                         ["v1.0.0-beta.7-r3.2.36",self.release,"v1.0.0-beta.7-r3.2.38"])
        self.assertEqual(options["reinstall"],[options["cycle"][1]])
        latest=updater.recovery_choices(("v1.0.0-beta.7-r3.2.38",None),releases)
        self.assertEqual(latest["cycle"][1],latest["cycle"][2])
        self.assertEqual(updater.recovery_choices(("v1.0.0-beta.7-r3.2.99",None),releases),{})

    @patch.object(updater,"ensure_maintenance")
    @patch.object(updater,"ensure_rescue")
    @patch.object(updater,"system_ok",return_value=False)
    @patch.object(updater,"apply")
    @patch.object(updater,"newest",return_value=(None,[]))
    @patch.object(updater,"published_releases")
    @patch.object(updater,"versions",return_value=("v1.0.0-beta.7-r3.2.37",None))
    def test_cycle_requires_exact_plan_and_backup(self,versions,published,newest,apply,system_ok,rescue,maintenance):
        published.return_value=[self.published_release(n) for n in ("36","37","38")]
        steps=updater.recovery_choices(versions.return_value,published.return_value)["cycle"]
        request={"recovery":"cycle","installed":self.release,"releases":steps,"backupConfirmed":False}
        (self.inbox/"pending.json").write_text(json.dumps(request))
        updater.run_locked()
        apply.assert_not_called()
        request["backupConfirmed"]=True
        (self.inbox/"pending.json").write_text(json.dumps({**request,"releases":steps[::-1]}))
        updater.run_locked()
        apply.assert_not_called()
        (self.inbox/"pending.json").write_text(json.dumps(request))
        updater.run_locked()
        self.assertEqual([c.args[0] for c in apply.call_args_list],steps)
        self.assertEqual([c.kwargs["intermediate"] for c in apply.call_args_list],[True,True,False])
        result=json.loads((self.state/"recovery-run.json").read_text())
        self.assertEqual((result["phase"],result["completed"]),("success",3))

if __name__ == "__main__":
    unittest.main()
