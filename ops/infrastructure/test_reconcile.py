import unittest
from unittest.mock import Mock, patch
import reconcile

class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        reconcile.attempts = {}
        reconcile.rows = {}

    @patch.object(reconcile, "probe")
    def test_downloaded_model_not_ready(self, probe):
        probe.return_value = {"models": [{"type": "llm", "loaded_instances": []}]}
        self.assertFalse(reconcile.lm_ready())
        probe.return_value = {"models": [{"type": "llm", "loaded_instances": [{"id": "local-model"}]}]}
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

class ModelDependencyTests(unittest.TestCase):
    @patch.object(reconcile.install, "system_packages")
    @patch.object(reconcile.install.shutil, "which", return_value="/usr/bin/curl")
    @patch.object(reconcile.install.subprocess, "run")
    def test_missing_libatomic_is_installed(self, run, which, packages):
        run.return_value = Mock(returncode=1, stdout="")
        reconcile.install.lmstudio_dependencies()
        packages.assert_called_once_with("libatomic1")

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
        packages.assert_called_once_with("libatomic1", "curl", "ca-certificates")


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
