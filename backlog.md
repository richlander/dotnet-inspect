# Backlog

## Smarter defaults for agent and human users

The tool's defaults were designed for human terminal use, but most callers are now agents. Two ideas to reduce guidance overhead by making the defaults better:

### Auto-detect agent context and default to oneline

If the tool detects it's being called by an agent (piped output, non-TTY, or an env var), default to `--oneline` output. Humans at a terminal keep the current full output. This matches how agents actually work — scan a compact list first, then drill into specific items. Every agent transcript shows this pattern. The win: shorter SKILL.md/llms.txt guidance because the tool does the right thing by default.

### Make `--shape` more visible on `type`

A human user was shown `type` output and liked it, but was wowed by `--shape` and said "this should be the default." Consider making `--shape` the default for `type`, or at least more prominent. The shape view (showing inheritance, interfaces, member categories) gives an immediate structural understanding that the flat type list doesn't. This parallels the oneline discussion — the best default is the one that answers the most common first question.
