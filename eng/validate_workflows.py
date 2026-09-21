#!/usr/bin/env python3
"""Validate the repository's workflow YAML locally.

A workflow that only fails on GitHub costs a push to discover. Everything that can be
checked without a runner is checked here:

  * each file parses as YAML
  * every job has a runner and at least one step
  * every step has either `uses` or `run`, never both and never neither (a step with
    neither is a no-op that silently does nothing)
  * every `uses:` action is pinned to a major version, not a branch
  * every `run:` that invokes a script under eng/ names a file that exists
  * every `${{ steps.<id>.outputs.<name> }}` reference is written by an earlier step, and
    every `${{ env.<name> }}` reference is declared in `env:` - a reference to an output
    that is never set expands to an empty string, so the command fails somewhere else with
    no clue why
  * every shell variable expanded inside a `run:` is assigned in that same step

Usage:
    python3 eng/validate_workflows.py
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parent.parent

# An action must be pinned to a version tag, never a branch: `actions/checkout@main`
# means a third party's next commit runs in this repository's CI without review.
UNPINNED = re.compile(r"^[\w.-]+/[\w.-]+@(main|master|HEAD)$")

SCRIPT_REFERENCE = re.compile(r"python3\s+([\w./-]+\.py)")

STEP_OUTPUT = re.compile(r"\$\{\{\s*steps\.([\w-]+)\.outputs\.([\w-]+)\s*\}\}")
ENV_REFERENCE = re.compile(r"\$\{\{\s*env\.([\w-]+)\s*\}\}")

# A shell variable expansion, excluding the GitHub expressions (${{ ... }}) which are
# expanded by the runner rather than by the shell.
SHELL_VARIABLE = re.compile(r"\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?")

# Variables the runner provides, which a step legitimately reads without assigning.
RUNNER_PROVIDED = {
    "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH", "GITHUB_STEP_SUMMARY", "GITHUB_SHA",
    "GITHUB_REF", "GITHUB_REF_NAME", "GITHUB_REF_TYPE", "GITHUB_WORKSPACE", "GITHUB_TOKEN",
    "GITHUB_RUN_NUMBER", "GITHUB_REPOSITORY", "GITHUB_ACTOR", "HOME", "PATH", "PWD",
}

# Assigned by the shell itself, or by a construct this check cannot see.
SHELL_PROVIDED = {"?", "#", "@", "*", "0", "1", "!"}


def fail(problems: list[str], message: str) -> None:
    problems.append(message)
    print(f"::error::{message}")


def _triggers(document: dict) -> list[str]:
    """Trigger names, tolerating PyYAML parsing `on:` as the boolean True."""
    raw = document.get("on", document.get(True))
    if isinstance(raw, str):
        return [raw]
    if isinstance(raw, list):
        return [str(item) for item in raw]
    if isinstance(raw, dict):
        return list(raw.keys())
    return []


def _outputs_written(script: str) -> set[str]:
    """`name=value` keys a step appends to $GITHUB_OUTPUT, as `id.output` names."""
    written: set[str] = set()
    for match in re.finditer(r"""echo\s+["']?([\w-]+)=.*?>>\s*"?\$GITHUB_OUTPUT""", script):
        written.add(match.group(1))
    return written


def _assigned_variables(script: str) -> set[str]:
    """Shell variables the script assigns, including loop variables."""
    assigned: set[str] = set()
    assigned |= set(re.findall(r"^\s*([A-Za-z_][A-Za-z0-9_]*)=", script, re.MULTILINE))
    assigned |= set(re.findall(r"\bfor\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b", script))
    return assigned


def _validate_run(script: str, label: str, file: str, job: str, problems: list[str]) -> None:
    """Checks inside one `run:` block."""
    assigned = _assigned_variables(script)

    # A shell variable read but never assigned in this step expands to empty. The command
    # usually still runs, with a silently wrong value, which is worse than failing.
    for line in script.splitlines():
        # Drop GitHub expressions first: the runner owns those, not the shell.
        without_expressions = re.sub(r"\$\{\{.*?\}\}", "", line)
        stripped = without_expressions.strip()

        # Only inspect lines that read a variable, and skip comments.
        if stripped.startswith("#"):
            continue
        if not re.search(r"\$\{?[A-Za-z_]", without_expressions):
            continue
        # A pure assignment line defines a variable rather than reading one.
        if re.match(r"^\s*[A-Za-z_][A-Za-z0-9_]*=", without_expressions):
            continue

        for name in SHELL_VARIABLE.findall(without_expressions):
            if name in assigned or name in RUNNER_PROVIDED or name in SHELL_PROVIDED:
                continue
            fail(
                problems,
                f"{file}/{job}: '{label}' reads ${name}, which is not assigned in this step. "
                "It would expand to an empty string.",
            )


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate the repository's workflow YAML.")
    parser.add_argument("--root", type=Path, default=ROOT, help="Repository root to validate.")
    args = parser.parse_args()

    root: Path = args.root.resolve()
    workflows = root / ".github" / "workflows"
    problems: list[str] = []

    if not workflows.is_dir():
        fail(problems, f"{workflows} does not exist.")
        return 1

    files = sorted(workflows.glob("*.yml")) + sorted(workflows.glob("*.yaml"))
    if not files:
        fail(problems, "No workflow files found.")
        return 1

    for path in files:
        print(f"\n{path.relative_to(root)}")

        try:
            document = yaml.safe_load(path.read_text())
        except yaml.YAMLError as ex:
            fail(problems, f"{path.name}: does not parse ({ex}).")
            continue

        if not isinstance(document, dict):
            fail(problems, f"{path.name}: top level is not a mapping.")
            continue

        if not _triggers(document):
            fail(problems, f"{path.name}: declares no trigger.")

        declared_env = set((document.get("env") or {}).keys())

        jobs = document.get("jobs") or {}
        if not jobs:
            fail(problems, f"{path.name}: declares no jobs.")
            continue

        for job_name, job in jobs.items():
            if not isinstance(job, dict):
                fail(problems, f"{path.name}/{job_name}: job is not a mapping.")
                continue

            if "runs-on" not in job and "uses" not in job:
                fail(problems, f"{path.name}/{job_name}: declares neither runs-on nor uses.")

            job_env = declared_env | set((job.get("env") or {}).keys())

            steps = job.get("steps") or []
            if not steps:
                fail(problems, f"{path.name}/{job_name}: has no steps.")
                continue

            # Outputs produced by earlier steps, for the reference check below.
            available_outputs: dict[str, set[str]] = {}

            for index, step in enumerate(steps, start=1):
                if not isinstance(step, dict):
                    fail(problems, f"{path.name}/{job_name} step {index}: is not a mapping.")
                    continue

                has_uses = "uses" in step
                has_run = "run" in step
                label = step.get("name") or step.get("uses") or f"step {index}"

                if has_uses and has_run:
                    fail(problems, f"{path.name}/{job_name}: '{label}' has both uses and run.")
                elif not has_uses and not has_run:
                    fail(problems, f"{path.name}/{job_name}: '{label}' has neither uses nor run.")

                if has_uses:
                    action = str(step["uses"])
                    if UNPINNED.match(action):
                        fail(problems, f"{path.name}/{job_name}: '{action}' is pinned to a branch.")

                if has_run:
                    script = str(step["run"])

                    # `with:` and `env:` on the step are visible to the script too.
                    script_env = job_env | set((step.get("env") or {}).keys())

                    for referenced in SCRIPT_REFERENCE.findall(script):
                        if not (root / referenced).exists():
                            fail(
                                problems,
                                f"{path.name}/{job_name}: '{label}' runs {referenced}, which does not exist.",
                            )

                    # An output read before it is written expands to empty.
                    for step_id, output_name in STEP_OUTPUT.findall(script):
                        if output_name not in available_outputs.get(step_id, set()):
                            fail(
                                problems,
                                f"{path.name}/{job_name}: '{label}' reads "
                                f"steps.{step_id}.outputs.{output_name}, which no earlier step sets.",
                            )

                    for env_name in ENV_REFERENCE.findall(script):
                        if env_name not in script_env:
                            fail(
                                problems,
                                f"{path.name}/{job_name}: '{label}' reads env.{env_name}, which is not declared.",
                            )

                    _validate_run(script, str(label), path.name, str(job_name), problems)

                    if "id" in step:
                        written = _outputs_written(script)
                        available_outputs.setdefault(str(step["id"]), set()).update(written)

        print(f"  triggers : {', '.join(_triggers(document))}")
        print(f"  jobs     : {', '.join(jobs.keys())}")
        print(f"  steps    : {sum(len(j.get('steps') or []) for j in jobs.values() if isinstance(j, dict))}")

    print()
    if problems:
        print(f"Workflow validation FAILED ({len(problems)} problem(s)).")
        return 1

    print(f"Workflow validation passed: {len(files)} workflow(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
