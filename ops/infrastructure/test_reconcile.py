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

if __name__ == "__main__":
    unittest.main()
