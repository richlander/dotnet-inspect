#!/usr/bin/env python3

import os
from pathlib import Path
import subprocess
import tempfile

import yaml


OUTPUTS = (
    "code",
    "csharpdiff",
    "decompiler",
    "docs",
    "ildiff",
    "ilroundtrip",
    "packaging",
    "shipped",
)


def load_detection_body(repository: Path) -> str:
    workflow = repository / ".github" / "workflows" / "ci.yml"
    with workflow.open(encoding="utf-8") as stream:
        steps = yaml.safe_load(stream)["jobs"]["changes"]["steps"]

    return next(step["run"] for step in steps if step.get("name") == "Detect changes")


def run_detection(
    repository: Path,
    body: str,
    event_name: str,
    files: str,
    *,
    resolution_succeeds: bool = True,
) -> dict[str, str]:
    before = "1" * 40
    sha = "2" * 40
    rendered = (
        body.replace("${{ github.event_name }}", event_name)
        .replace("${{ github.event.pull_request.number }}", "3704")
        .replace("${{ github.event.before }}", before)
        .replace("${{ github.sha }}", sha)
    )

    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        output = root / "github-output"
        binaries = root / "bin"
        binaries.mkdir()

        fake_command = """#!/bin/sh
if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
  exit 1
fi
if [ "$1" = "cat-file" ]; then
  exit 0
fi
printf '%s' "$CHANGED_FILES"
"""
        for name in ("gh", "git"):
            command = binaries / name
            command.write_text(fake_command, encoding="utf-8")
            command.chmod(0o755)

        environment = os.environ.copy()
        environment.update(
            {
                "CHANGED_FILES": files,
                "GITHUB_OUTPUT": str(output),
                "PATH": f"{binaries}{os.pathsep}{environment['PATH']}",
                "RESOLUTION_SUCCEEDS": str(resolution_succeeds).lower(),
            }
        )
        result = subprocess.run(
            ["bash", "-c", rendered],
            cwd=repository,
            env=environment,
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            raise AssertionError(
                f"change detection exited {result.returncode}\n"
                f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}"
            )

        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        if set(values) != set(OUTPUTS):
            raise AssertionError(f"expected outputs {OUTPUTS}, got {values}")
        return values


def assert_all(values: dict[str, str], expected: str) -> None:
    if any(value != expected for value in values.values()):
        raise AssertionError(f"expected every output to be {expected}, got {values}")


def main() -> None:
    repository = Path(__file__).resolve().parents[1]
    body = load_detection_body(repository)

    assert_all(run_detection(repository, body, "pull_request", ""), "true")
    assert_all(run_detection(repository, body, "push", ""), "false")
    assert_all(
        run_detection(
            repository,
            body,
            "pull_request",
            "README.md",
            resolution_succeeds=False,
        ),
        "true",
    )

    readme = run_detection(repository, body, "pull_request", "README.md")
    if readme["code"] != "false" or readme["docs"] != "true":
        raise AssertionError(f"README.md canary did not discriminate: {readme}")

    source = run_detection(
        repository,
        body,
        "pull_request",
        "src/dotnet-inspect/Program.cs",
    )
    if source["code"] != "true":
        raise AssertionError(f"source canary did not select code: {source}")

    print("CI change detection fail-safe and path canaries passed.")


if __name__ == "__main__":
    main()
