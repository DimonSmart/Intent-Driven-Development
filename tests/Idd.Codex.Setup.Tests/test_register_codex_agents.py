"""Black-box tests for the shipped Codex Factory role-registration helper."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "src/canonical/factory/register-codex-agents.py"
ROLES = ("idd-internal-factory-planner", "idd-internal-factory-worker")


class RegistrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="idd-role-setup-")
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.plugin = root / "plugin"
        scripts = self.plugin / "skills" / "idd-factory-configure" / "scripts"
        agents = self.plugin / "agents"
        scripts.mkdir(parents=True)
        agents.mkdir()
        self.installer = scripts / "register-codex-agents.py"
        shutil.copy2(SOURCE, self.installer)
        for role in ROLES:
            (agents / (role + ".toml")).write_text('name = "' + role + '"\n', encoding="utf-8")
        self.project = root / "project"
        self.project.mkdir()
        self.home = root / "codex-home"

    def run_setup(self, *args):
        return subprocess.run([sys.executable, str(self.installer), *args],
                              text=True, capture_output=True, check=False)

    def test_read_only_check_requires_registration(self):
        result = self.run_setup("--scope", "project", "--project", str(self.project), "--check")
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertFalse((self.project / ".codex").exists())

    def test_project_apply_is_idempotent_and_does_not_modify_trust(self):
        args = ("--scope", "project", "--project", str(self.project))
        self.assertEqual(0, self.run_setup(*args, "--apply").returncode)
        self.assertEqual(0, self.run_setup(*args, "--check").returncode)
        self.assertEqual(0, self.run_setup(*args, "--apply").returncode)
        self.assertFalse((self.project / ".codex" / "config.toml").exists())
        for role in ROLES:
            self.assertEqual((self.plugin / "agents" / (role + ".toml")).read_bytes(),
                             (self.project / ".codex" / "agents" / (role + ".toml")).read_bytes())

    def test_conflict_blocks_both_roles_without_overwrite(self):
        folder = self.project / ".codex" / "agents"
        folder.mkdir(parents=True)
        planner = folder / (ROLES[0] + ".toml")
        planner.write_text("unrelated-custom-role", encoding="utf-8")
        result = self.run_setup("--scope", "project", "--project", str(self.project), "--apply")
        self.assertEqual(3, result.returncode)
        self.assertEqual("unrelated-custom-role", planner.read_text())
        self.assertFalse((folder / (ROLES[1] + ".toml")).exists())

    def test_explicit_replace_preserves_original_backup(self):
        args = ("--scope", "project", "--project", str(self.project))
        self.assertEqual(0, self.run_setup(*args, "--apply").returncode)
        planner = self.project / ".codex" / "agents" / (ROLES[0] + ".toml")
        planner.write_text("old contents", encoding="utf-8")
        self.assertEqual(3, self.run_setup(*args, "--apply").returncode)
        result = self.run_setup(*args, "--apply", "--replace")
        self.assertEqual(0, result.returncode, result.stderr)
        backup = list(planner.parent.glob(planner.name + ".idd-backup-*.bak"))
        self.assertEqual(1, len(backup))
        self.assertEqual("old contents", backup[0].read_text())
        self.assertEqual(0, self.run_setup(*args, "--check").returncode)

    def test_user_scope_uses_isolated_codex_home(self):
        args = ("--scope", "user", "--codex-home", str(self.home))
        self.assertEqual(0, self.run_setup(*args, "--apply").returncode)
        self.assertEqual(0, self.run_setup(*args, "--check").returncode)
        self.assertTrue((self.home / "agents" / (ROLES[1] + ".toml")).is_file())

    def test_symlink_destination_is_rejected(self):
        folder = self.project / ".codex" / "agents"
        folder.mkdir(parents=True)
        outside = Path(self.temp.name) / "outside.toml"
        outside.write_text("outside", encoding="utf-8")
        try:
            (folder / (ROLES[0] + ".toml")).symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("Symlink creation not available")
        result = self.run_setup("--scope", "project", "--project", str(self.project), "--apply")
        self.assertEqual(1, result.returncode)
        self.assertEqual("outside", outside.read_text())


if __name__ == "__main__":
    unittest.main()
