# Agent model mapping

This file maps names in current contributor guidance to dispatch model IDs.
[AGENTS.md](../AGENTS.md#how-many-reviewers-and-from-which-models) owns review
requirements; [Reviewer roster](round-orchestration.md#reviewer-roster) owns
seat selection and substitutions. This mapping does not change either policy.

## Model names and IDs

The concrete IDs below are advertised by Copilot CLI's `task` tool for its
`model` parameter. They are not portable provider API names or a guarantee of
availability in another session or agent host.

| Name in guidance | Display name | Dispatch model ID |
| --- | --- | --- |
| GPT-6 Astra | GPT-6 Astra | `gpt-6-astra` |
| Claude Opus | Claude Opus 5 | `claude-opus-5` |
| MAI-Code | MAI-Code 1.1 Flash | `mai-code-1.1-flash` |
| Gemini Pro | Resolve an available Pro model | Not pinned; use the runtime-advertised Pro ID. |

GPT-6 Astra replaces GPT-5.6 Sol in current guidance. Historical review
attributions keep their original model names.

## Resolving a dispatch

Use the exact ID accepted by the active dispatch tool. Confirm a mapped ID
against that tool's available-model list before invoking it; never construct
an ID by changing spaces, punctuation, capitalization, or version numbers in
a display name.

Claude Opus, Gemini Pro, and MAI-Code are family-level names. Where guidance
requests the highest available quality, resolve the appropriate current family
member from the runtime rather than treating this table as a permanent version
pin. Gemini Flash is not Gemini Pro. If the requested model or family is
unavailable, follow the roster's substitution policy and record the exact
substitute ID and reason on the PR.

When updating a mapping, copy the ID from the dispatch tool's advertised model
list. Keep current mappings here rather than duplicating tables across
guidance files.
