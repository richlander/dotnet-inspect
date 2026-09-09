# Agent model mapping

This file maps names in current contributor guidance to dispatch model IDs.
[AGENTS.md](../AGENTS.md#how-many-reviewers-and-from-which-models) owns review
requirements; [Reviewer roster](round-orchestration.md#reviewer-roster) owns
the single review seat and its substitutions. This mapping does not change
either policy.

## Model names and IDs

The concrete IDs below are advertised by Copilot CLI's `task` tool for its
`model` parameter. They are not portable provider API names or a guarantee of
availability in another session or agent host.

| Name in guidance | Display name | Dispatch model ID |
| --- | --- | --- |
| GPT-6 Astra | GPT-6 Astra | `gpt-6-astra` |
| GPT-5.6 Sol | GPT-5.6 Sol | `gpt-5.6-sol` |
| GPT-5.6 Sol Fast | GPT-5.6 Sol Fast | `gpt-5.6-sol-fast` |
| GPT-5.6 Terra | GPT-5.6 Terra | `gpt-5.6-terra` |
| GPT-5.6 Luna | GPT-5.6 Luna | `gpt-5.6-luna` |

This policy applies to agent harnesses that advertise GPT models. In those
harnesses, only OpenAI GPT models may be used; all other models are prohibited.
GPT-5.6 Sol is the coding default and GPT-6 Astra holds the sole review seat.
Any advertised GPT-5.6 model, such as GPT-5.6 Terra, or another advertised GPT
model may be selected when the agent judges it sufficient for the task.
Historical review attributions keep their original model names.

## Resolving a dispatch

Use the exact ID accepted by the active dispatch tool. Confirm a mapped ID
against that tool's available-model list before invoking it; never construct
an ID by changing spaces, punctuation, capitalization, or version numbers in
a display name. If the preferred GPT model is unavailable, follow the roster's
GPT-only substitution policy and record the exact substitute ID and reason on
the PR.

When updating a mapping, copy the ID from the dispatch tool's advertised model
list. Keep current mappings here rather than duplicating tables across
guidance files.
