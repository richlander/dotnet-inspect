# LLM Eval

> Scenarios optimized for evaluating LLM tool-use with dotnet-inspect.

## How it works

Each scenario uses the `prompt` → `bash` → `expect` pattern:

- **Input**: the `prompt` block — a natural language request the LLM receives
- **Expected tool call**: the `bash` block — the correct CLI invocation
- **Judge criteria**: the `expect` block — substrings that must appear in output

## Running an eval

1. Parse `prompt` blocks as eval inputs
2. Give the LLM the prompt + tool description (from `dotnet-inspect cli`)
3. Check if the LLM produces a command matching or equivalent to the `bash` block
4. Run the command and verify `expect` assertions against output
5. Score: correct command + passing assertions = pass

## Scoring dimensions

- **Command selection**: Did the LLM pick the right subcommand? (`type` vs `member` vs `find`)
- **Flag accuracy**: Did it use the right flags? (`--package`, `--platform`, `-v:q`)
- **Output interpretation**: Can it extract the answer from the output?

## Docs

Eval scenarios are distributed across all workflow docs via `prompt` blocks.
Files in this directory are curated eval sets with high prompt density.
