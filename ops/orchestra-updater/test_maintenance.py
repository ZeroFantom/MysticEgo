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
        self.report.write_text("{}")
        self.assertTrue(updater.ensure_maintenance(self.release))
        command.assert_not_called()
        self.assertEqual(self.status()["state"], "ready")

    @patch.object(updater, "trusted_file", return_value=True)
    @patch.object(updater, "maintenance_ready", return_value=True)
    @patch.object(updater, "system_ok", side_effect=[False, True])
    def test_missing_report_starts_reconcile(self, system_ok, ready, trust):
        self.assertTrue(updater.ensure_maintenance(self.release))
        system_ok.assert_called_with("start", "--no-block", "mysticego-infrastructure-reconcile.service")

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

if __name__ == "__main__":
    unittest.main()
