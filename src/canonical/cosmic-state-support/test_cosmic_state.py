"""Real deterministic mechanics tests; COSMIC semantics remain a live-LLM task."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys
import json

spec = importlib.util.spec_from_file_location(
    "cosmic_state", Path(__file__).with_name("cosmic_state.py"))
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class StateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.project = self.root / "project"
        self.intent = self.project / ".idd" / "intent"
        self.intent.mkdir(parents=True)
        self.state = self.root / "state"
        self.state.mkdir()
        for i in (1, 2):
            (self.intent / f"IDD-{i:04d}.spec-p{i}.md").write_text(
                f"# Process {i}\n", encoding="utf8")
        strategy = self.state / "strategy.md"
        strategy.write_text(
            "Boundary system; user clerk; layer application; granularity functional process",
            encoding="utf8")
        self.data = {
            "strategy_file": str(strategy), "strategy_sha256": mod.digest(strategy),
            "schema": 1, "protocol": mod.VERSION, "project": str(self.project),
            "boundary": "system", "state_dir": str(self.state),
            "sources": {
                name: {"sha256": sha, "status": "Pending",
                       "packet": None, "packet_sha256": None}
                for name, sha in mod.inventory(self.project).items()
            },
        }
        mod.write_atomic(self.state / "run.json", self.data)

    def complete_fixture(self):
        for name in self.data["sources"]:
            p = self.state / name
            p.write_text(f"Source: {name}\nCoverage: Read in full\n", encoding="utf8")
            self.data["sources"][name].update(
                status="Completed", packet=name, packet_sha256=mod.digest(p))
        records = self.state / "records.txt"
        records.write_text(
            "OWNER | IDD-0001#P1 | IDD-0001.spec-p1.md | # Process 1\n"
            "OWNER | IDD-0002#P1 | IDD-0002.spec-p2.md | # Process 2\n"
            "LINK | IDD-0002#P1 | IDD-0001#P1 | IDD-0002.spec-p2.md | # Process 2\n"
            "MOVE | IDD-0001#P1 | E | Input | # Process 1\n"
            "MOVE | IDD-0001#P1 | X | Output | # Process 1\n"
            "MEASURE | IDD-0001#P1 | system | E=1,X=1,R=0,W=0 | CFP=2\n"
            "MOVE | IDD-0002#P1 | E | Input | # Process 2\n"
            "MOVE | IDD-0002#P1 | W | Record | # Process 2\n"
            "MEASURE | IDD-0002#P1 | system | E=1,X=0,R=0,W=1 | CFP=2\n",
            encoding="utf8")
        return records

    def test_cfp_is_independent_of_connector_fan_in(self):
        records = self.complete_fixture()
        result = mod.audit(self.data, records, True)
        self.assertTrue(result["valid"], result)
        self.assertEqual(4, result["total_cfp"])
        self.assertEqual(1, result["coupling"]["IDD-0001#P1"]["confirmed_callers"])
        self.assertEqual(1, result["coupling"]["IDD-0001#P1"]["referring_specs"])

    def test_resume_invalidates_changed_owner_and_dependents(self):
        records = self.complete_fixture()
        (self.intent / "IDD-0001.spec-p1.md").write_text("# Changed\n", encoding="utf8")
        bad = mod.stale(self.data)
        self.assertEqual("changed", bad["IDD-0001.spec-p1.md"])
        affected = mod.change_dependent_sources(self.data, records)
        self.assertEqual({"IDD-0001.spec-p1.md", "IDD-0002.spec-p2.md"}, affected)

    def test_bad_cfp_is_rejected(self):
        records = self.complete_fixture()
        records.write_text(
            records.read_text(encoding="utf8").replace("CFP=2", "CFP=3", 1),
            encoding="utf8")
        result = mod.audit(self.data, records, True)
        self.assertFalse(result["valid"])
        self.assertIn("cfp-arithmetic:IDD-0001#P1", result["errors"])

    def test_duplicate_owner_id_is_rejected_without_semantic_dedup(self):
        records = self.state / "records.txt"
        line = "OWNER | IDD-0001#P1 | IDD-0001.spec-p1.md | # Process 1\n"
        records.write_text(line + line, encoding="utf8")
        with self.assertRaisesRegex(ValueError, "duplicate owner"):
            mod.parse_records(records)

    def test_no_implicit_zero_size_complete(self):
        records = self.state / "records.txt"
        records.write_text("", encoding="utf8")
        result = mod.audit(self.data, records, True)
        self.assertIn("empty-scope-requires-separate-semantic-proof", result["errors"])

    def test_rule_10c_checked_mechanically(self):
        records = self.complete_fixture()
        content = records.read_text(encoding="utf8")
        content = content.replace(
            "MOVE | IDD-0001#P1 | X | Output | # Process 1\n", "")
        content = content.replace(
            "E=1,X=1,R=0,W=0 | CFP=2", "E=1,X=0,R=0,W=0 | CFP=1")
        records.write_text(content, encoding="utf8")
        self.assertIn("cosmic-rule-10c:IDD-0001#P1",
                      mod.audit(self.data, records, True)["errors"])

    def test_one_hundred_inbound_links_do_not_inflate_owner_cfp(self):
        records = self.state / "many-links.txt"
        lines = [
            "OWNER | IDD-0001#P1 | IDD-0001.spec-p1.md | # Main",
            "MOVE | IDD-0001#P1 | E | Request | # Main",
            "MOVE | IDD-0001#P1 | W | Record | # Main",
            "MEASURE | IDD-0001#P1 | system | E=1,X=0,R=0,W=1 | CFP=2",
        ]
        for n in range(1, 101):
            caller = f"IDD-0002#P{n}"
            lines.append(f"OWNER | {caller} | IDD-0002.spec-p2.md | # Caller {n}")
            lines.append(
                f"LINK | {caller} | IDD-0001#P1 | IDD-0002.spec-p2.md | # Caller {n}")
        records.write_text("\n".join(lines) + "\n", encoding="utf8")
        result = mod.audit(self.data, records, complete=False)
        self.assertTrue(result["valid"], result)
        self.assertEqual(2, result["total_cfp"])
        self.assertEqual(100, result["coupling"]["IDD-0001#P1"]["confirmed_callers"])
        self.assertEqual(1, result["coupling"]["IDD-0001#P1"]["referring_specs"])

    def test_retry_is_bounded_and_resume_state_is_durable(self):
        script = Path(__file__).with_name("cosmic_state.py")
        source = "IDD-0001.spec-p1.md"
        for attempt in range(1, 4):
            result = subprocess.run([
                sys.executable, str(script), "fail", "--state-dir", str(self.state),
                "--source", source, "--reason", "incomplete reader packet",
            ], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            info = json.loads(result.stdout)
            self.assertEqual(attempt, info["failed_attempts"])
        saved = mod.load(self.state)
        self.assertEqual("Blocked", saved["sources"][source]["status"])
        self.assertEqual(3, saved["sources"][source]["failed_attempts"])
        again = subprocess.run([
            sys.executable, str(script), "fail", "--state-dir", str(self.state),
            "--source", source, "--reason", "repeat",
        ], capture_output=True, text=True)
        self.assertNotEqual(0, again.returncode)

    def test_changed_strategy_blocks_reuse(self):
        records = self.complete_fixture()
        Path(self.data["strategy_file"]).write_text("new boundary", encoding="utf8")
        result = mod.audit(self.data, records, True)
        self.assertIn("stale:@strategy:changed-or-unavailable", result["errors"])

    def test_undocumented_connector_target_rejected(self):
        records = self.complete_fixture()
        with records.open("a", encoding="utf8") as stream:
            stream.write("LINK | IDD-0002#P1 | IDD-0099#P1 | IDD-0002.spec-p2.md | # Missing\n")
        result = mod.audit(self.data, records, True)
        self.assertIn("unresolved-link-target:IDD-0099#P1", result["errors"])


if __name__ == "__main__":
    unittest.main()
