#!/usr/bin/env python3
"""GATE 2 for CI: sums the NUnit results XMLs in one directory and enforces the floors.

game-ci's `testMode: all` writes one `<mode>-results.xml` per mode, so reading a single file would gate on
half the run. The counts are summed over every top-level `*.xml` whose root is `<test-run>`; subdirectories
are not read, so a stray nested copy cannot be counted twice.

Usage: check-test-results.py --results-dir DIR [--modes editmode,playmode] [--min-total N]
                             [--min-sandbox N] [--min-conformance N] [--min-plugin-only N] [--label TEXT]
Exit 0 = pass. Fails when a listed mode has no results file, when failed/inconclusive/skipped is non-zero,
or when a floor is not met.
"""

import argparse
import pathlib
import sys
import xml.etree.ElementTree as ET

COUNTS = ("total", "passed", "failed", "inconclusive", "skipped")
# Sub-floors matched on a test-case attribute: (name, attribute, prefix).
PREFIX_GROUPS = (
    ("sandbox", "classname", "KhrCharacterTestbed"),
    ("conformance", "fullname", "KhrCharacterTestbed.Conformance."),
)
TESTBED_ASSEMBLY_PREFIX = "Sandbox."


def count(root: ET.Element) -> dict:
    row = {k: int(root.get(k, "0") or 0) for k in COUNTS}
    cases = list(root.iter("test-case"))
    for name, attr, prefix in PREFIX_GROUPS:
        row[name] = sum(1 for c in cases if c.get(attr, "").startswith(prefix))
    # plugin-only: test cases in assemblies that are not the testbed's own.
    row["plugin-only"] = sum(
        sum(1 for _ in s.iter("test-case"))
        for s in root.iter("test-suite")
        if s.get("type") == "Assembly"
        and not s.get("name", "").startswith(TESTBED_ASSEMBLY_PREFIX)
    )
    return row


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--results-dir", required=True, type=pathlib.Path)
    ap.add_argument(
        "--modes",
        default="",
        help="comma-separated modes whose <mode>-results.xml must exist",
    )
    ap.add_argument("--min-total", type=int, default=0)
    ap.add_argument("--min-sandbox", type=int, default=0)
    ap.add_argument("--min-conformance", type=int, default=0)
    ap.add_argument("--min-plugin-only", type=int, default=0)
    ap.add_argument("--label", default="GATE 2")
    a = ap.parse_args()
    floors = {
        "sandbox": a.min_sandbox,
        "conformance": a.min_conformance,
        "plugin-only": a.min_plugin_only,
    }

    def fail(msg: str) -> int:
        print(f"::error title={a.label} failed::{msg}")
        return 1

    files = sorted(a.results_dir.glob("*.xml")) if a.results_dir.is_dir() else []
    missing = [
        m
        for m in filter(None, a.modes.split(","))
        if not any(f.name == f"{m}-results.xml" for f in files)
    ]
    if missing:
        return fail(
            f"no results XML for mode(s) {', '.join(missing)} in {a.results_dir} (hang/crash?)"
        )

    sums = dict.fromkeys(COUNTS + tuple(floors), 0)
    runs = 0
    for f in files:
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError as e:
            return fail(f"{f}: unreadable results XML ({e})")
        if root.tag != "test-run":
            print(f"skip {f}: root is <{root.tag}>, not <test-run>")
            continue
        runs += 1
        row = count(root)
        print(f"{f}: " + " ".join(f"{k}={v}" for k, v in row.items()))
        for k, v in row.items():
            sums[k] += v
    if runs == 0:
        return fail(f"no <test-run> results XML in {a.results_dir} (hang/crash?)")

    print(
        f"SUM over {runs} file(s): "
        + " ".join(f"{k}={v}" for k, v in sums.items())
        + f" (floor={a.min_total}, "
        + ", ".join(f"{k}-floor={v}" for k, v in floors.items())
        + ")"
    )
    if sums["failed"] or sums["inconclusive"] or sums["skipped"]:
        return fail("failed/inconclusive/skipped must all be 0.")
    if sums["total"] < a.min_total:
        return fail(f"(floor) ran {sums['total']}, need >= {a.min_total}.")
    for name, floor in floors.items():
        if sums[name] < floor:
            return fail(
                f"({name} sub-floor) ran {sums[name]}, need >= {floor}; suite hollow?"
            )
    print(f"{a.label} PASS.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
