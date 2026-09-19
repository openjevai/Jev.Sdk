#!/usr/bin/env python3
"""Coverage gate for Jev.Sdk.

Reads the Cobertura report produced by `dotnet test --collect:"XPlat Code Coverage"`, applies the
documented exemption list, and fails the build when coverage drops below the floors.

Two line-coverage floors are enforced, and a branch floor for the DI package:

  Jev.Sdk                    >= 89%
  Jev.Sdk.DependencyInjection >= 98%

The DI package gets the higher floor because every one of its members is reachable from a host,
so there is no defensive guard left over. The core library carries a handful of guards that
cannot be reached through HttpClient's documented behaviour; those are listed in EXEMPT_LINES
below with the reason, rather than excluded by a blanket attribute, so each one has to be
justified in review.

Usage:
    python3 eng/coverage_gate.py artifacts/test-results/**/coverage.cobertura.xml
"""

from __future__ import annotations

import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Line-coverage floors, per assembly. Keep these honest: raise them when coverage rises, and never
# lower one to make a build pass without recording why.
#
# These numbers describe hand-written code only. Generated serialization metadata and generated
# logging methods are excluded (see GENERATED_PATH_MARKERS), because including them would measure
# the compiler rather than the library.
LINE_FLOORS = {
    "Jev.Sdk": 99.0,
    "Jev.Sdk.DependencyInjection": 100.0,
}

BRANCH_FLOORS = {
    "Jev.Sdk": 75.0,
    "Jev.Sdk.DependencyInjection": 90.0,
}

# Lines that cannot be reached through the library's public surface or through HttpClient's
# documented behaviour. Each entry needs a reason, and a reason that would survive review.
EXEMPT_LINES = {
    "Jev.Sdk/Http/HttpTypeSafeTransport.cs": {
        192: "HttpClient always supplies a non-null Content on a response; this null check guards against that changing.",
    },
    "Jev.Sdk/Http/HttpTypeSafeTransport.Retry.cs": {
        167: "RetryConditionHeaderValue permits both members to be null, but no code path produces such a header.",
    },
    "Jev.Sdk/Questions/StructuredValue.cs": {
        132: "Serializing a non-null value never yields JsonValueKind.Undefined, so the default arm is unreachable.",
    },
}

# Path fragments identifying generated code. Source-generated serialization metadata and generated
# logging methods are not hand-written, so counting them would measure the compiler rather than the
# library, and would make the number move whenever the generator changed.
GENERATED_PATH_MARKERS = (
    "/obj/",
    "LoggerMessage.g.cs",
    ".JsonSourceGenerator/",
)


def find_report(pattern: str) -> Path:
    """Returns the most recent coverage report matching the pattern."""
    matches = sorted(glob.glob(pattern, recursive=True))

    if not matches:
        print(f"::error::No coverage report found matching {pattern!r}")
        sys.exit(1)

    return Path(matches[-1])


def count_lines(package: ET.Element) -> tuple[int, int]:
    """Returns (covered, total) lines for a package, honouring the exemption list."""
    covered = 0
    total = 0
    exempt_hits = 0

    for cls in package.iter("class"):
        filename = (cls.get("filename") or "").replace("\\", "/")

        if any(marker in filename for marker in GENERATED_PATH_MARKERS):
            continue

        exempt = EXEMPT_LINES.get(filename, {})

        for line in cls.iter("line"):
            number = int(line.get("number") or 0)
            hits = int(line.get("hits") or 0)

            if hits > 0:
                covered += 1
                total += 1
                continue

            if number in exempt:
                exempt_hits += 1
                continue

            total += 1

    if exempt_hits:
        print(f"    (exempted {exempt_hits} documented guard line(s))")

    return covered, total


def main() -> int:
    pattern = sys.argv[1] if len(sys.argv) > 1 else "artifacts/test-results/**/coverage.cobertura.xml"
    report = find_report(pattern)

    print(f"Coverage report: {report}")

    root = ET.parse(report).getroot()
    failures: list[str] = []

    print()
    print(f"{'assembly':40s} {'lines':>8s} {'line %':>8s} {'branch %':>9s}")
    print("-" * 70)

    for package in root.iter("package"):
        name = package.get("name") or "(unnamed)"
        covered, total = count_lines(package)
        line_pct = (covered / total * 100.0) if total else 0.0
        branch_pct = float(package.get("branch-rate") or 0.0) * 100.0

        print(f"{name:40s} {covered:4d}/{total:<3d} {line_pct:7.2f}% {branch_pct:8.2f}%")

        if name in LINE_FLOORS and line_pct < LINE_FLOORS[name]:
            failures.append(f"{name}: line coverage {line_pct:.2f}% is below the {LINE_FLOORS[name]:.2f}% floor")

        if name in BRANCH_FLOORS and branch_pct < BRANCH_FLOORS[name]:
            failures.append(f"{name}: branch coverage {branch_pct:.2f}% is below the {BRANCH_FLOORS[name]:.2f}% floor")

    print()

    if failures:
        for failure in failures:
            print(f"::error::{failure}")

        print(f"Coverage gate FAILED ({len(failures)} issue(s)).")
        return 1

    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
