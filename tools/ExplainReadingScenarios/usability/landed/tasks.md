# Trial tasks and reproduction

Each task was assigned to fresh GPT-6 Sol at high reasoning with no conversation
history. The Query task was assigned once to direct JSON and once to HAL with
each guide; Style was assigned once to HAL with each guide. Participants could
read only their assigned guide and recorded CLI output. All source, designs,
prewritten filters, other trials, network, and inspection execution were
excluded. They wrote only their own scratch answers. This is a cooperative
protocol, not a sandbox.

## Query task

Prepare two CLI commands without running them for exact package
`Microsoft.Azure.SignalR` with JSON output:

1. Implementation-library decoded literal contains `https://`, net8.0.
2. Preferred exact dependency predicate for `Newtonsoft.Json` with maximum
   declaration-edge depth 3, dependency target net8.0.

Distinguish direct hits from depth-2-or-greater legacy syntax. Explain
normalization/matching rules and prerequisite gestures, whether 600 astral
Unicode characters is an accepted literal operand, accepted depth values,
whether depth 1 or target `all` is legal for the second command, and how to
bound candidate work versus returned rows for `Microsoft.Azure.*` with at most
two final rows. State whether empty listed literal values means unrestricted
acceptance. Preserve completeness and unavailable-data caveats. Do not invent
domain rules.

## Style task

Find every registered C# style choice that preserves IL bytes and is endorsed
by either the declared oracle or corpus. Return ID, option/value, tier name,
and tier description by joining local data. Return every declared conflict
group and member ID; explain the limit of applying those groups to arbitrary
configuration keys.

Explain omitted Boolean interpretation for a known choice in complete data,
an unknown ID, incomplete data, and an unavailable property. Preserve scope
and states in filtered evidence. Determine whether tier descriptions or the
whole task require another fetch or schema/contract fetch. Do not invent
invocation behavior missing from the data.

## Running another participant

Use the [existing recorder](../skill-informed/runner.py) with a separate empty
scratch directory per participant. Put the assigned guidance in `guide.md` and
write `config.json` with absolute `cli`, matching `dotnet_root`, and a
`guide_label`. Full guidance is the pinned root and query skill bytes joined
by two newline bytes; experimental guidance is `reading-guide.txt`.

Require all product reads through `guide`, `request`, and `view`:

```bash
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-reading-trial guide
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-reading-trial request -- explain literal --json
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-reading-trial view response-01.out --jq 'keys'
```

Ask participants to copy discovered resource paths, use their assigned `.data`
or `.hal` for supported selected resources, follow hrefs unchanged, and write
their own jq projections. Supply no entry resource or answer. Require
`answer.json` with results or unrun argv arrays, explanations, response/path
provenance, uncertainties, and qualitative friction. Keep a sufficient budget
at both inner and outer tool layers; a full guide needed 20,000+ output tokens
at the outer layer in this harness. Record corrections rather than deleting
failed attempts. Pin the executable before the trial and verify it afterward.
