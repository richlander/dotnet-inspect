# Browser front-end security posture

`prototypes/inspect-web` ships a browser front-end: HTML, TypeScript bundled by
Vite, a .NET Wasm engine, and a small number of third-party runtime libraries.
This document owns how that delivery is secured — which protections exist, which
component enforces each one, and, equally, which protections this repository
deliberately does not attempt.

It exists because the reasoning behind those choices was otherwise spread across
pull request descriptions, where it is not discoverable by the next person to
touch the front-end. Several of the rules below were learned by getting them
wrong first; the failures are recorded with them, because a rule without its
counterexample tends to be re-litigated.

## Responsibility and boundaries

**This document owns:** the security posture of the browser front-end as
delivered — dependency containment, static analysis of front-end sources,
response headers, and the principles that decide whether a proposed protection
belongs here at all.

**Immediate boundaries.** It consumes the linter and bundler contracts of the
JavaScript ecosystem tools named below, and the response-header contract of the
static host. It does not redefine them.

**Non-claims.** This document does not own:

- The untrusted-artifact threat model. Harm caused by a hostile package, PDB, or
  SourceLink payload — the risk that inspected input escapes a boundary or
  causes unbounded work — belongs to
  [Untrusted data threat model](untrusted-data-threat-model.md).
- Any product path outside `prototypes/inspect-web`.
- Application behavior, presentation, or interaction, which
  [Inspect Web UI](inspect-web-ui.md) and the component designs own.
- Any guarantee. This is a description of posture and reasoning, not a promise
  that the front-end is secure.

## Principles

### 1. Prefer off-the-shelf tools in their standard configuration

Use the linters the web ecosystem already maintains, configured the way their
authors intend, before writing anything bespoke.

The motivating failure is concrete, and its lesson is narrower than "bespoke is
bad."

This repository has a hand-written gate that classifies URLs in HTML to decide
which need integrity attributes — around 462 lines of
`prototypes/inspect-web/test/toolchain.test.ts`, by the count of the abandoned
pull request that tried to delete it. That gate is still present and still runs,
because it has a property no preset offers: it is **fail-closed**. An element or
attribute nobody has classified fails the gate, so the next HTML feature capable
of running script is rejected for being unrecognized rather than admitted for
not matching a known-bad pattern.

What failed was the attempt to replace it with hand-tuned linter configuration.
That pull request ran six review rounds, and each one found another URL spelling
the configuration did not cover. The enumeration problem did not go away when it
moved from code into config, because the defect was never the language the rules
were written in — it was that a human was enumerating spellings at all.

The resolution was to adopt the ecosystem linters *alongside* the existing gate
rather than in place of it. An ecosystem linter is maintained by people who watch
the whole ecosystem's attack surface, and its standard presets encode spellings
no single repository would think to enumerate. Take that breadth from the
preset; keep a bespoke check only where it supplies something a preset
structurally cannot, such as failing closed on the unrecognized.

Where a preset is genuinely too noisy to adopt, record the measurement rather
than the impression: `stylelint`'s `standard` preset reports 939 findings
against this codebase and has not been adopted for that reason.

What is configured today:

- `html-validate` extending `html-validate:standard`, `html-validate:document`,
  and `html-validate:a11y`, plus `require-sri` for cross-origin subresources.
- `oxlint` with the `correctness` and `suspicious` categories as errors, the
  `typescript`, `unicorn`, `oxc`, `import`, `jsdoc`, and `promise` plugins, and
  type-aware analysis enabled.
- `knip` for unused files, exports, and dependencies.

### 2. Prefer containment by the build system over gates that describe

A gate that inspects what is present can only report on the shapes it knows how
to look at. A build system that decides what may be present does not have that
failure mode.

The motivating failure again came from this repository. Three runtime libraries
— `mermaid`, `marked`, and `dompurify`, the sanitizer — were loaded by
cross-origin dynamic `import()` of CDN URLs. Both the `require-sri` rule and a
bespoke SRI freshness checker reported clean, and both were right within their
own terms: each reads `<script>` and `<link>` elements, and a dynamic `import()`
is not markup. The libraries had no integrity checking of any kind, and both
gates were structurally incapable of noticing.

The cost was not theoretical. Because the libraries were URLs rather than
lockfile entries, they were invisible to every ecosystem vulnerability tool.
Auditing the pinned versions afterwards reported two vulnerable packages
carrying 24 advisories — 19 against that DOMPurify build, several of which
defeat sanitization outright. A code comment asserting that DOMPurify made
package Markdown safe had been resting on that build.

Making the libraries ordinary npm dependencies bundled by Vite replaced the
question "did the gate recognize this reference?" with "there is no such
reference." That is the preferred shape: containment by construction, with the
build system as the boundary.

### 3. A claim must name its gate, and must not outrun it

Every assertion in a comment, a README, or a CI step should be traceable to the
check that enforces it, and must not describe a stronger property than that
check delivers.

The `npm audit` step in CI took three review rounds to get right, and each round
found the same shape of error:

- `--audit-level=high` reads as a severity filter over advisories. npm applies
  it to *packages*, bucketing each by its highest severity. Both vulnerable
  packages bucketed as moderate, so the flag returned success on all 24
  advisories, including all 19 sanitizer bypasses.
- `--omit=dev` reads as "audit only what ships." It filters by where a package
  is *declared*. Vite is a devDependency and its helper code is in the shipped
  bundle, so the split was never the boundary it resembled.
- Removing the flags entirely reads as "no filter." npm falls back to `low`
  (`options.auditLevel || 'low'` in `npm-audit-report`), which still passes a
  package whose advisories are all `info`.

The step is now `npm audit --audit-level=info`, which is the only setting that
fails on any advisory, and it has to be asked for explicitly.

The transferable lesson is the last one: **the absence of a flag is not the
absence of a policy.** A tool's defaults are policy, and a claim that rests on
"we didn't configure anything" needs the same verification as one that rests on
a setting.

Two habits follow. Prefer widening a gate to match a simple claim over narrowing
prose to match a complicated gate — narrowed prose tends to grow a new overclaim
somewhere else. And confirm that a gate fails when it should: each check here
should have a known input that makes it exit non-zero, or it is only assumed to
work.

### 4. Parse markup with a parser

Do not use regular expressions to answer structural questions about HTML.

This repository has arrived at that rule twice, independently, which is the best
evidence it is real.

The HTML gate in `test/toolchain.test.ts` records it first, in a comment above
its own tokenizer: reading markup with a regular expression is how its previous
two versions failed, and the second failure was worse than the first. The reason
is stated precisely there — a pattern that matches whole tags *skips* what it
cannot match, so markup it does not understand silently becomes markup it does
not check.

The SRI freshness checker then repeated it. One review round produced five
findings that were all a single mistake: a regex-based check over `<script>`
elements, where four of the five failure modes were silent. An earlier round had
already patched that check by adding more regex, which is the tell. The
replacement reads the document with the parser `html-validate` already provides.

The general form: when a check keeps missing spellings, replace the approach
rather than enumerate the next case. A related habit from the same gate is worth
copying — it deliberately does not strip comments before running, because doing
so would put a second, disagreeing parser between the gate and the truth.

### 5. Keep enforcement proportional to the threat model

Protection has a cost in code, review attention, and future maintenance, and
that cost should be visible against what it actually defends.

A security-hardening change in this repository once reached 525 insertions of
which the genuine protection was 6 lines. The rest had accumulated from review
feedback that was individually reasonable and collectively outside the stated
threat model. Splitting it restored the ratio.

Related, and worth stating because it is easy to get backwards: the SRI
freshness check is **not** a security control. The browser enforces Subresource
Integrity; if a pinned digest no longer matches, the asset fails to load and the
feature that depended on it breaks. The check detects that staleness before a
user does. Describing it as a security control overstates it.

### 6. Do not enforce beyond common practice

There is no goal to enforce properties that no other web development team
addresses. The bar for adopting a check is that mainstream web teams do it, not
that it is conceivable or that a reviewer can construct a scenario in which it
would help.

This principle is what keeps the previous five from ratcheting. Each of them can
be pushed toward a stricter configuration by an argument that sounds correct in
isolation, and the sum of such arguments is a front-end whose analysis
configuration is unlike anyone else's, expensive to maintain, and no better
defended. Novelty in a security configuration is a cost, not an achievement: it
means no other team's experience applies, and no upstream maintainer is
carrying the burden of keeping it current.

Review feedback proposing a protection beyond common practice is a scope
proposal, not a defect. Record it, decline it, and say why.

## What is enforced today

| Protection | Enforced by | Notes |
| --- | --- | --- |
| Script-capable HTML rejected unless classified inert | Bespoke fail-closed gate in `test/toolchain.test.ts` | Fails on unrecognized elements and attributes |
| HTML validity, accessibility, SRI on cross-origin subresources | `html-validate` in `npm run lint` | Standard presets plus `require-sri` |
| TypeScript correctness and unsafe-value rules | `oxlint`, type-aware | `correctness` and `suspicious` as errors |
| Unused files, exports, dependencies | `knip` | |
| Runtime library provenance | npm lockfile plus Vite bundling | No cross-origin runtime `import()` |
| Known advisories in shipped dependencies | `npm audit --audit-level=info` in CI | Fails on any advisory at any severity |
| Markdown sanitization | DOMPurify with an explicit allow list | Config in `src/data.ts` |
| Response headers | `staticwebapp.config.json` `globalHeaders` | `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`, `Strict-Transport-Security` |
| Pinned subresource staleness | Weekly `inspect-web-sri.yml` | A staleness check, not a security control |

## Known gaps

Recorded so they are not rediscovered as findings:

- **No Content-Security-Policy.** This is the most significant gap: nothing
  contains an injection that gets past sanitization.
- **Response headers do not apply to `/api/*`.** The static host does not apply
  `globalHeaders` to API routes. Tracked as #5119.
- **`stylelint` is not adopted**, at 939 findings against the `standard` preset.

## Conventions for changing this posture

- Adopt the standard configuration of an established tool before writing a
  bespoke check, and measure a preset's real output before rejecting it.
- Prefer eliminating a class of reference over detecting it.
- State what a check does not cover, in the same place that describes what it
  does.
- Give every new check an input that makes it fail, and keep that evidence.
- Decline enforcement beyond common practice, and record the decision.
