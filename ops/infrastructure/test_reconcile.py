import unittest
import os
import tempfile
from pathlib import Path
from unittest.mock import Mock, patch
import reconcile

class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        reconcile.attempts = {}
        reconcile.rows = {}
        reconcile.history = []

    @patch.object(reconcile, "lm_role", return_value="panel-data")
    @patch.object(reconcile, "probe")
    def test_downloaded_model_not_ready(self, probe, role):
        probe.return_value = {"models": [{"type": "llm", "loaded_instances": []}]}
        self.assertFalse(reconcile.lm_ready())
        probe.return_value = {"models": [{"type": "llm", "loaded_instances": [{"id": "unrelated-model"}]}]}
        self.assertFalse(reconcile.lm_ready())
        probe.return_value = {"models": [{"type": "llm", "loaded_instances": [{"id": "mysticego-panel-data"}]}]}
        self.assertTrue(reconcile.lm_ready())

    @patch.object(reconcile, "publish")
    def test_healthy_never_runs_installer(self, publish):
        action = Mock()
        reconcile.repair("docker", lambda: True, action, "ready")
        action.assert_not_called()
        publish.assert_called_once_with("docker", "available", "ready")

    @patch.object(reconcile, "publish")
    def test_repair_rechecks(self, publish):
        action = Mock()
        test = Mock(side_effect=[False, True])
        reconcile.repair("docker", test, action, "ready")
        action.assert_called_once()
        self.assertEqual(publish.call_args.args, ("docker", "available", "ready"))
        self.assertNotIn("docker", reconcile.attempts)

    @patch.object(reconcile, "publish")
    def test_failure_has_cooldown(self, publish):
        action = Mock(side_effect=RuntimeError())
        reconcile.repair("docker", lambda: False, action, "ready")
        reconcile.repair("docker", lambda: False, action, "ready")
        action.assert_called_once()
        self.assertEqual(publish.call_args.args[1], "degraded")

    @patch.object(reconcile, "publish")
    @patch.object(reconcile, "repair")
    @patch.object(reconcile.install, "ROOT")
    def test_no_database_provider_switch(self, root, repair, publish):
        (root / "postgres/method").is_file.return_value = False
        reconcile.postgres()
        repair.assert_not_called()
        self.assertEqual(publish.call_args.args[:2], ("postgres-service", "external"))

class RecoveryPolicyTests(unittest.TestCase):
    def setUp(self):
        reconcile.attempts = {}
        reconcile.rows = {}
        reconcile.history = []

    @patch.object(reconcile, "publish")
    def test_probe_error_does_not_mutate(self, publish):
        action = Mock()
        reconcile.repair("lmstudio", Mock(side_effect=OSError()), action, "ready")
        action.assert_not_called()
        self.assertEqual(publish.call_args.args[3], "OSError")

    def test_retry_dates_and_history_only_on_transition(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(reconcile, "STATUS", Path(folder)):
            reconcile.attempts["lmstudio"] = 1000
            reconcile.publish("lmstudio", "degraded", "retry", "CalledProcessError")
            reconcile.publish("voice", "available", "ready")
            reconcile.publish("lmstudio", "degraded", "retry", "CalledProcessError")
            self.assertEqual(len(reconcile.history), 2)
            report = __import__("json").loads((Path(folder) / "status.json").read_text())
            model = next(c for c in report["components"] if c["name"] == "lmstudio")
            self.assertEqual(model["nextRetryAt"], "1970-01-01T00:31:40+00:00")
            reconcile.attempts.clear()
            reconcile.publish("lmstudio", "available", "ready")
            self.assertNotIn("nextRetryAt", reconcile.rows["lmstudio"])
            self.assertEqual(len(reconcile.history), 3)

    @patch.object(reconcile.install, "lmstudio")
    @patch.object(reconcile.install, "run")
    @patch.object(reconcile.install, "lmstudio_dependencies")
    @patch.object(reconcile.install, "ensure_lm_home")
    @patch.object(reconcile.install, "lm_cache_present", return_value=False)
    @patch.object(reconcile.Path, "is_symlink", return_value=False)
    @patch.object(reconcile.Path, "is_file", return_value=True)
    @patch.object(reconcile, "lm_role", return_value="panel-data")
    def test_retained_unit_with_missing_model_reprovisions(self, role, is_file, symlink, cache, home, deps, run, setup):
        reconcile.lm_repair()
        run.assert_called_once_with("systemctl", "stop", "mysticego-lmstudio.service", timeout=120)
        setup.assert_called_once_with({"role": "panel-data"})

    @patch.object(reconcile.install, "lmstudio")
    @patch.object(reconcile.install, "run")
    @patch.object(reconcile.install, "lmstudio_dependencies")
    @patch.object(reconcile.install, "ensure_lm_home")
    @patch.object(reconcile.install, "lm_cache_present", return_value=True)
    @patch.object(reconcile.Path, "is_symlink", return_value=False)
    @patch.object(reconcile.Path, "is_file", return_value=True)
    @patch.object(reconcile, "lm_role", return_value="world")
    def test_intact_artifacts_restart_without_reinstall(self, role, is_file, symlink, cache, home, deps, run, setup):
        reconcile.lm_repair()
        setup.assert_not_called()
        self.assertEqual([c.args[:2] for c in run.call_args_list],
                         [("systemctl", "reset-failed"), ("systemctl", "restart")])


class ModelResourceTests(unittest.TestCase):
    @patch.object(reconcile.install.shutil, "disk_usage")
    @patch.object(reconcile.install.Path, "read_text", return_value="MemAvailable: 1048576 kB\n")
    def test_low_memory_defers_provisioning(self, read, disk):
        disk.return_value = Mock(free=10 * 1024**3)
        with self.assertRaises(reconcile.install.ResourceDeferred):
            reconcile.install.lm_provisioning_capacity("panel-data")

    @patch.object(reconcile.install.shutil, "disk_usage")
    @patch.object(reconcile.install.Path, "read_text", return_value="MemAvailable: 3145728 kB\n")
    def test_capacity_is_profile_specific(self, read, disk):
        disk.return_value = Mock(free=4 * 1024**3)
        reconcile.install.lm_provisioning_capacity("panel-data")
        with self.assertRaises(reconcile.install.ResourceDeferred):
            reconcile.install.lm_provisioning_capacity("world")

    @patch.object(reconcile.install.socket, "socket")
    def test_occupied_endpoint_detects_any_listener(self, sock):
        connection = sock.return_value.__enter__.return_value
        connection.connect_ex.return_value = 0
        self.assertTrue(reconcile.install.lm_port_occupied())
        connection.connect_ex.return_value = 111
        self.assertFalse(reconcile.install.lm_port_occupied())
        connection.connect_ex.assert_called_with(("127.0.0.1", 1234))


class ModelHomeTests(unittest.TestCase):
    def test_retained_account_recreates_deleted_home(self):
        with tempfile.TemporaryDirectory() as temp:
            home = Path(temp) / "lm-home"
            uid, gid = os.getuid(), os.getgid()
            entry = Mock(pw_uid=uid, pw_gid=gid, pw_dir=str(home))
            with patch.object(reconcile.install, "LM_HOME", home), \
                 patch.object(reconcile.install, "account", return_value=uid), \
                 patch.object(reconcile.install.pwd, "getpwnam", return_value=entry), \
                 patch.object(reconcile.install.os, "chown") as chown:
                self.assertEqual(reconcile.install.ensure_lm_home(), uid)
                self.assertTrue(home.is_dir())
                self.assertEqual(home.stat().st_mode & 0o777, 0o700)
                chown.assert_called_once_with(home, uid, gid)
                (home / "model.gguf").write_text("cached")
                reconcile.install.ensure_lm_home()
                self.assertEqual((home / "model.gguf").read_text(), "cached")
                chown.assert_called_once()

    def test_symlink_home_is_rejected_without_touching_target(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "target"
            target.mkdir()
            home = Path(temp) / "lm-home"
            home.symlink_to(target, target_is_directory=True)
            uid = os.getuid()
            entry = Mock(pw_uid=uid, pw_gid=os.getgid(), pw_dir=str(home))
            with patch.object(reconcile.install, "LM_HOME", home), \
                 patch.object(reconcile.install, "account", return_value=uid), \
                 patch.object(reconcile.install.pwd, "getpwnam", return_value=entry), \
                 patch.object(reconcile.install.os, "chown") as chown:
                with self.assertRaises(ValueError):
                    reconcile.install.ensure_lm_home()
                chown.assert_not_called()
                self.assertTrue(home.is_symlink())


class ModelDependencyTests(unittest.TestCase):
    @patch.object(reconcile.install, "system_packages")
    @patch.object(reconcile.install.shutil, "which", return_value="/usr/bin/curl")
    @patch.object(reconcile.install.subprocess, "run")
    def test_missing_libatomic_is_installed(self, run, which, packages):
        run.return_value = Mock(returncode=1, stdout="")
        reconcile.install.lmstudio_dependencies()
        packages.assert_called_once_with("libatomic1", "libgomp1")

    @patch.object(reconcile.install, "system_packages")
    @patch.object(reconcile.install.shutil, "which", return_value="/usr/bin/curl")
    @patch.object(reconcile.install.subprocess, "run")
    def test_installed_dependencies_skip_apt(self, run, which, packages):
        run.return_value = Mock(returncode=0, stdout="install ok installed")
        reconcile.install.lmstudio_dependencies()
        packages.assert_not_called()

    @patch.object(reconcile.install, "system_packages")
    @patch.object(reconcile.install.shutil, "which", return_value=None)
    @patch.object(reconcile.install.subprocess, "run")
    def test_minimal_host_dependencies_installed_together(self, run, which, packages):
        run.return_value = Mock(returncode=1, stdout="")
        reconcile.install.lmstudio_dependencies()
        packages.assert_called_once_with("libatomic1", "libgomp1", "curl", "ca-certificates")


class VoiceRecoveryTests(unittest.TestCase):
    @patch.object(reconcile, "publish")
    def test_cooldown_preserves_safe_reason(self, publish):
        with patch.object(reconcile, "rows", {"voice": {"reason": "RuntimeError"}}), patch.object(reconcile, "attempts", {"voice": reconcile.time.time()}):
            action = Mock()
            reconcile.repair("voice", lambda: False, action, "ready")
            action.assert_not_called()
            self.assertEqual(publish.call_args.args[3], "RuntimeError")

    @patch.object(reconcile, "voice_ready", return_value=True)
    @patch.object(reconcile, "voice_dependencies_ready", return_value=False)
    @patch.object(reconcile.Path, "is_file", return_value=True)
    @patch.object(reconcile.install, "run")
    def test_existing_voice_with_broken_import_repairs_dependencies(self, run, unit, dependencies, ready):
        reconcile.voice_repair()
        self.assertEqual(run.call_count, 1)
        self.assertEqual(run.call_args.args[0], "/bin/bash")
        self.assertTrue(run.call_args.args[1].endswith("install-voice.sh"))

    @patch.object(reconcile, "voice_ready", return_value=True)
    @patch.object(reconcile, "voice_dependencies_ready", return_value=True)
    @patch.object(reconcile.Path, "is_file", return_value=True)
    @patch.object(reconcile.install, "run")
    def test_existing_voice_with_valid_import_restarts(self, run, unit, dependencies, ready):
        reconcile.voice_repair()
        self.assertEqual([call.args for call in run.call_args_list], [
            ("systemctl", "reset-failed", "mysticego-voice.service"),
            ("systemctl", "restart", "mysticego-voice.service"),
        ])


if __name__ == "__main__":
    unittest.main()
