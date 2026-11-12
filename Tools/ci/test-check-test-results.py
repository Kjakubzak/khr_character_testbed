#!/usr/bin/env python3
"""Edge cases for check-test-results.py: summing, sub-floors and files that must not be counted."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check-test-results.py")


def results(assemblies, failed=0, skipped=0):
    """NUnit3 XML with one Assembly suite per (name, [(classname, method), ...])."""
    total = sum(len(cases) for _, cases in assemblies)
    suites = "".join(
        f'<test-suite type="Assembly" name="{name}">'
        + "".join(
            f'<test-case name="{m}" fullname="{c}.{m}" classname="{c}"/>'
            for c, m in cases
        )
        + "</test-suite>"
        for name, cases in assemblies
    )
    return (
        f'<?xml version="1.0"?><test-run total="{total}" passed="{total - failed - skipped}" '
        f'failed="{failed}" inconclusive="0" skipped="{skipped}">{suites}</test-run>'
    )


def cases(classname, n):
    return [(classname, f"m{i}") for i in range(n)]


SANDBOX = ("Sandbox.Tests.dll", cases("KhrCharacterTestbed.SandboxRigTests", 10))
CONFORMANCE = (
    "Sandbox.Conformance.Tests.dll",
    cases("KhrCharacterTestbed.Conformance.ConformanceTests", 4),
)
PLUGIN = (
    "UnityGLTF.KhrCharacter.Tests.dll",
    cases("UnityGLTF.KhrCharacter.Tests.X", 6),
)


class GateTests(unittest.TestCase):
    def run_gate(self, files, *args):
        with tempfile.TemporaryDirectory() as d:
            for rel, text in files.items():
                p = Path(d, rel)
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(text)
            r = subprocess.run(
                [sys.executable, str(SCRIPT), "--results-dir", d, *args],
                capture_output=True,
                text=True,
            )
        return r.returncode, r.stdout

    def both(self, editmode, playmode):
        return {
            "editmode-results.xml": results(editmode),
            "playmode-results.xml": results(playmode),
        }

    def test_sums_both_modes_and_every_sub_floor(self):
        rc, out = self.run_gate(
            self.both([CONFORMANCE], [SANDBOX, PLUGIN]),
            "--modes",
            "editmode,playmode",
            "--min-total",
            "20",
            "--min-sandbox",
            "14",
            "--min-conformance",
            "4",
            "--min-plugin-only",
            "6",
        )
        self.assertEqual(rc, 0, out)
        self.assertIn("total=20", out)

    def test_nested_copy_is_not_counted(self):
        files = self.both([CONFORMANCE], [SANDBOX])
        files["copy/playmode-results.xml"] = files["playmode-results.xml"]
        rc, out = self.run_gate(files, "--min-sandbox", "15")
        self.assertEqual(rc, 1, out)
        self.assertIn("sandbox=14", out)

    def test_missing_mode_fails(self):
        rc, out = self.run_gate(
            {"playmode-results.xml": results([SANDBOX])}, "--modes", "editmode,playmode"
        )
        self.assertEqual(rc, 1)
        self.assertIn("editmode", out)

    def test_hollow_editmode_fails_conformance_floor(self):
        rc, out = self.run_gate(self.both([], [SANDBOX]), "--min-conformance", "4")
        self.assertEqual(rc, 1)
        self.assertIn("conformance sub-floor", out)

    def test_conformance_prefix_needs_the_dot(self):
        near_miss = (
            "Sandbox.Tests.dll",
            cases("KhrCharacterTestbed.ConformanceHelpersTests", 1),
        )
        rc, out = self.run_gate(self.both([CONFORMANCE, near_miss], [SANDBOX]))
        self.assertEqual(rc, 0, out)
        self.assertIn("conformance=4", out)

    def test_plugin_only_excludes_testbed_assemblies(self):
        rc, out = self.run_gate(
            self.both([CONFORMANCE], [SANDBOX]), "--min-plugin-only", "1"
        )
        self.assertEqual(rc, 1)
        self.assertIn("plugin-only=0", out)

    def test_skipped_fails(self):
        files = self.both([CONFORMANCE], [SANDBOX])
        files["playmode-results.xml"] = results([SANDBOX], skipped=1)
        self.assertEqual(self.run_gate(files)[0], 1)

    def test_non_test_run_xml_is_ignored_and_truncated_xml_fails(self):
        files = self.both([CONFORMANCE], [SANDBOX])
        files["coverage.xml"] = "<coverage/>"
        rc, out = self.run_gate(files, "--min-total", "14")
        self.assertEqual(rc, 0, out)
        files["coverage.xml"] = "<test-run total="
        self.assertEqual(self.run_gate(files)[0], 1)

    def test_empty_directory_fails(self):
        self.assertEqual(self.run_gate({})[0], 1)


if __name__ == "__main__":
    unittest.main()
