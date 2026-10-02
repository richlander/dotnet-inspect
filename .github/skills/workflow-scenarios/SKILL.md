---
name: workflow-scenarios
description: Use when authoring or validating dotnet-inspect workflow scenarios, including exact-build execution, performance gates, profiling, and network observation.
---

# Workflow scenarios

This is a repo-local contributor skill for the executable scenario documents
under `docs/workflows/`. It is not embedded in the dotnet-inspect binary.

Choose the focused reference for the task:

- [Writing workflows](writing-workflows.md) defines the scenario structure,
  semantic fences, assertions, prompts, and authoring conventions.
- [Validating workflows](validating-workflows.md) defines exact-build
  preconditions, execution semantics, evaluation, and full-suite operation.
- [Performance testing](performance-testing.md) defines the NativeAOT
  pre-ship gate and repository profiling and trace-correlation procedures.
- [Network observation](network-guard.md) defines Debug request-start
  diagnostics and product offline enforcement.

Keep workflow documents user-scenario-focused. Put contributor mechanics in
this skill rather than product skills under `skills/`.
