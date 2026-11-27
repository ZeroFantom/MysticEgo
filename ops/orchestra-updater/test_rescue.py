import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch, Mock

spec=importlib.util.spec_from_file_location("rescue",Path(__file__).with_name("rescue-request.py"))
rescue=importlib.util.module_from_spec(spec)
spec.loader.exec_module(rescue)

class RescueTests(unittest.TestCase):
    def setUp(self):
        folder=tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.root=Path(folder.name)
        self.inbox=self.root/"inbox"
        self.inbox.mkdir()
        self.state=self.root/"state"
        self.state.mkdir()
        self.request=self.inbox/"maintenance-rescue.json"
        for name,value in {"INBOX":self.inbox,"STATE":self.state}.items():
            p=patch.object(rescue,name,value);p.start();self.addCleanup(p.stop)

    def test_exact_request(self):
        self.request.write_text('{"confirmed":true}')
        self.assertTrue(rescue.take_request())
        self.assertFalse(self.request.exists())
        self.request.write_text('{"confirmed":true,"command":"anything"}')
        self.assertFalse(rescue.take_request())

    def test_symlink_rejected(self):
        other=self.root/"other"
        other.write_text('{"confirmed":true}')
        self.request.symlink_to(other)
        self.assertFalse(rescue.take_request())
        self.assertTrue(other.exists())
        self.assertFalse(self.request.is_symlink())

    @patch.object(rescue,"trusted",return_value=False)
    @patch.object(rescue.subprocess,"run")
    def test_untrusted_path_rejected(self,run,trust):
        self.request.write_text('{"confirmed":true}')
        rescue.recover()
        run.assert_not_called()
        self.assertEqual(json.loads((self.state/"rescue.json").read_text())["state"],"degraded")

    @patch.object(rescue,"trusted",return_value=True)
    @patch.object(rescue.urllib.request,"build_opener")
    @patch.object(rescue.subprocess,"run")
    def test_fixed_script_and_node(self,run,client,trust):
        self.request.write_text('{"confirmed":true}')
        node="12345678-1234-1234-1234-123456789abc"
        with patch.object(rescue.json,"load",return_value={"status":"alive","kind":"Orchestra","node":node}):
            rescue.recover()
        self.assertEqual(run.call_args.args[0],("bash",str(rescue.SCRIPT),node))
        self.assertEqual(json.loads((self.state/"rescue.json").read_text())["state"],"ready")

    @patch.object(rescue,"trusted",return_value=True)
    @patch.object(rescue.urllib.request,"build_opener")
    @patch.object(rescue.subprocess,"run")
    def test_wrong_node_type(self,run,client,trust):
        self.request.write_text('{"confirmed":true}')
        with patch.object(rescue.json,"load",return_value={"status":"alive","kind":"audit","node":"anything"}):
            rescue.recover()
        run.assert_not_called()
        self.assertEqual(json.loads((self.state/"rescue.json").read_text())["state"],"degraded")

if __name__=="__main__":
    unittest.main()
