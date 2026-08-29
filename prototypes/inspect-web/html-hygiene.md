# HTML and JavaScript hygiene

Every gate in this project accounts for **files**. The TypeScript compiler builds a
program out of `.ts` files; oxlint is handed a list of paths. Script written *inside* a
document belongs to no file, so neither tool reads it and the browser runs it anyway.

That is not hypothetical. `index.html` carried a `<script type="module">` block that
dereferenced `document.querySelector("#app")` without checking it -- the exact defect
`no-unsafe-member-access` is enabled to catch -- and shipped that way for as long as the
file existed, because nothing read it. That is [issue #4783][4783], and
`src/bootstrap.ts` is where the code lives now.

This document records how that class of defect is kept out, and -- just as importantly --
which hazards are **not** covered by a tool and are therefore a review responsibility.

## The approach: keep script in files, and lint the documents

Two rules, in order of importance:

1. **Script belongs in a module under `src/`.** A document may *reference* script and
   should not *contain* any. `index.html` loads `src/bootstrap.ts` with `src=`, and that
   file is compiled and linted like any other source file.
2. **Maintained linters check the documents themselves**, in their standard
   configuration. They own general HTML validity, document structure, accessibility, and
   the presence of subresource integrity on cross-origin loads. They do **not** own URL
   policy or inline event handlers; see
   [Custom validation we would need](#custom-validation-we-would-need).

### Why linters rather than a bespoke gate

A hand-written gate stood here for six review rounds and was removed. It tokenized HTML
itself and enumerated the elements and attributes a document was allowed to contain, in
order to prove that no document could reach script by any route.

The instrument was wrong for the claim. Proving that property means reimplementing HTML
tokenization and, in the end, reconstructing a bundler's private behaviour from its
compiled output. Each review round found one more construct the enumeration had missed,
because a shape recognizer over someone else's semantics has no closure argument
available to it.

The replacement is not strictly larger, and it is worth being precise about the trade.
`require-sri` covers `<link rel="stylesheet">`, `rel="modulepreload"`, and `rel="preload"`
with `as="script"` or `as="style"` as well as `<script>`, none of which the bespoke gate
checked, and the presets bring validity, document-structure and accessibility rules it
never had. In exchange, inline event handlers and external-URL policy moved from a gate
that claimed them to the review list below. That is the honest position: a maintained tool
that covers less, plus a written record of the remainder, beats a bespoke gate whose
coverage claims did not survive review.

The bespoke gate also drifted across the trust boundary this repository actually defends.
It ended up decoding character references to catch a URL disguised inside our own
reviewed `index.html` -- which is to say, it modelled a contributor as an attacker.
[`AGENTS.md`][agents] rules that out: the boundary is untrusted internet-origin data, not
our own source.

## What the tools own

Both linters run in their **standard configuration**. That is a deliberate choice rather
than a starting point: the configuration carries exactly one option change, and every
rule it enables is one the upstream projects maintain.

| Tool | Config | Owns |
| --- | --- | --- |
| [html-validate][hv] | `.htmlvalidate.json` | The `standard`, `document`, and `a11y` presets: HTML validity, element and attribute conformance, document structure, and WCAG rules. Plus `require-sri`, which requires an `integrity` attribute on cross-origin `<script>` and on `<link>` with `rel="stylesheet"`, `rel="modulepreload"`, or `rel="preload"` with `as="script"` or `as="style"` |
| [htmlhint][hh] | `.htmlhintrc` | Its default ruleset, plus `inline-script-disabled` for `javascript:` URLs in link and source attributes |

The one option change is `require-sri`'s `target`, set to `crossorigin` rather than the
preset's `all`. Without it the rule asks for a digest on same-origin files such as
`/src/styles.css`, where there is no third party to pin.

A toolchain test hands each linter a specimen its configuration must reject, and requires
the rejection to name the expected rule. That is what keeps a linter that has stopped
running, lost its configuration, or been handed no documents from reading as a clean pass.

### Suppressions

`index.html` carries one `html-validate-disable-next` directive, on the
`<link rel="preload" id="webassembly">` element. That element is deliberately invalid: it
is an anchor the .NET Wasm publish step rewrites, injecting the runtime `href`, and CI and
both deploy workflows `grep` for it in `dist/index.html`. An empty `<link>` is exactly what
`element-required-attributes` exists to catch, and it is correct to catch it -- so the
exemption is narrowed to that one element and states its reason inline, rather than turning
the rule off across the project.

Prefer that shape for any future exemption. A directive at the element leaves the rule
working everywhere else and puts the justification where the next reader will be standing.

## What review owns

These are the hazards a standard linter configuration does not cover. None of them is
tool-enforced; each is something to look for in any change that touches a document.

### Inline `<script>` bodies

Neither linter rejects a `<script>` element with a body. This is the original defect
class from #4783, so it is the first thing to look for in a diff that touches an HTML
file.

> html-validate's `require-csp-nonce` will flag inline `<script>`, and was considered.
> It is declined deliberately: it reports a missing CSP nonce, which is not the reason we
> object, and it false-positives on the live `<script type="importmap">` placeholder. A
> rule that says the wrong thing for the right outcome is worse than a documented review
> item.

**In review, reject:** any `<script>` with content. Move it to a module under `src/` and
reference it with `src=`.

### `iframe srcdoc` and `data:` documents

`<iframe srcdoc="&lt;script&gt;...">` runs script on load and needs no interaction.
Neither linter flags it. The same is true of the other route to an inline active document,
`<iframe src="data:text/html,...">`: the markup is a *value*, so a linter sees only an
ordinary `src` attribute and the handler inside it is invisible to every gate here -- and
to TypeScript and oxlint, which is exactly the outcome this document exists to
prevent. Nothing here inspects `<iframe>` at all. There are no `<iframe>` elements in this
project today.

**In review, reject:** `srcdoc` outright, and any `data:` URL bearing an HTML or
script media type in an attribute that loads a document.

### A remote document base

`<base href="https://elsewhere.example/">` makes every relative URL in the document --
including the bundle Vite emits under `/assets/` -- resolve somewhere else, while each
`src` still *looks* local. `verify-site-artifact.ts` requires `<base href="/">` to appear
before any preload or import map, but nothing checks that a base is local.

**In review, reject:** any `<base>` whose `href` is not `/`.

### Inline event handlers

Neither linter rejects `onclick`, `onpointerdown`, or any other `on*` attribute in its
standard configuration. htmlhint's `inline-script-disabled` flags some of them, but it
enumerates event names against a list that predates the modern ones: `onpointerdown`,
`onbeforeinput`, `onanimationstart` and `ontoggle` all pass it.

**In review, reject:** any attribute whose name begins `on`, anywhere, including inside
SVG and MathML.

### Third-party bytes

`index.html` loads three Prism files from jsDelivr. Each is pinned to an exact version in
the URL and carries a subresource integrity digest, and `require-sri` is what keeps the
digest from being dropped. **Nothing enforces the version pin, the host, or the digest's
correctness** -- see [Custom validation we would need](#custom-validation-we-would-need).

A CDN is untrusted internet-origin data. That is the trust boundary [`AGENTS.md`][agents]
names, and it is the reason a floating version is a real hazard rather than a tidiness
concern: `@latest` resolves to whatever the CDN serves that day, and for elements SRI does
not cover -- `<img>` has no `integrity` -- the URL is the only pin available.

Do not read the current jsDelivr URLs as a safe-by-construction allow list. jsDelivr is a
*public* CDN that serves any package published to npm, plus arbitrary GitHub repositories
through its `/gh/` route, so "it is the same host we already use" bounds far less than it
appears to.

**In review, reject:** a new external origin; a URL without an exact `major.minor.patch`
version; a `/gh/` URL; any URL containing `..`; a cross-origin `<script>` or stylesheet
`<link>` without `integrity`; and any `integrity` value not copied from the CDN's own
published digest.

### Elements the linters do not check for external loads

`require-sri` covers `<script>` and three `<link>` relations. Nothing else in the standard
configuration looks at where bytes come from, so all of the following can load
unpinned third-party content with a clean lint run:

- `<video>`, `<audio>`, `<source>`, and `<track>`
- `srcset` on `<img>` and `<source>`, which the SRI mechanism does not cover at all
- `<use href>` and `<image href>` inside SVG
- `<iframe>`, `<embed>`, and `<object>`
- `@import` and `url()` inside a `<style>` block or a stylesheet
- `<link>` relations outside the three `require-sri` recognises, including `icon`,
  `manifest`, `prefetch`, `preconnect`, and `preload` with any other `as` value

**In review, reject:** any of these pointing at an origin this project does not already
depend on.

### `javascript:` URLs outside link and source attributes

htmlhint's `inline-script-disabled` covers the attributes it knows about. A
`javascript:` URL reaches script from other places too, including `<form action>`,
`<button formaction>`, and `xlink:href` inside SVG.

**In review, reject:** a `javascript:` URL anywhere.

### Script inside SVG

SVG loads external code through `<script href="...">`, not HTML's `src`, and
html-validate does not apply element rules to foreign-namespace content at all. Nothing
here inspects the inside of an SVG subtree.

There are no `.svg` files in this project today, and no inline SVG in `index.html`.

**In review, reject:** `<script>` inside SVG, whether it carries `href`, `xlink:href`, or
a body; any `on*` handler on any SVG or MathML element; and `<use>` or `<image>` fetching
a remote resource.

## Custom validation we would need

Everything in [What review owns](#what-review-owns) is unenforced by choice. This section
records what closing the largest of those gaps would actually take, so the next person to
consider it starts from evidence rather than from scratch.

The gap worth closing first is **URL policy**: requiring that every external reference
names an allow-listed host at an exact version. A previous revision of this branch tried
it with html-validate's `allowed-links` rule and an allow-list pattern, and six review
rounds found six ways past it. The reason is structural rather than a bad regex.
`allowed-links` first *classifies* a URL with a short heuristic -- roughly "does it start
with `scheme://` or `//`?" -- and applies policy only to what it labels external. Anything
the heuristic mislabels skips the allow list and `require-sri` together. Confirmed
spellings that the browser's URL parser resolves to a real external origin, and that the
heuristic classified as relative or absolute:

- a leading space or tab, `" https://host/x.js"`
- an upper-cased scheme, `"HTTPS://host/x.js"`
- a protocol-relative reference, `"//host/x.js"`
- backslash separators, `"https:\\host\x.js"`
- a tab *inside* the scheme, `"ht<TAB>tps://host/x.js"`, since URL parsing strips tab,
  LF and CR from anywhere in the input
- the same tab written as a character reference, `"h&#x09;ttps://host/x.js"`, which
  html-validate does not decode before matching
- `"/<TAB>/host/x.js"`, which takes the rule's *absolute* branch and never consults the
  relative allow list at all

Patching the classifier is the losing move, and it is the same enumeration failure that
removed the bespoke gate: each round closed the spellings that were known and the next
round found another.

The shape that works is **deny-by-default on the attribute value itself**, using
html-validate's `attribute-allowed-values` through element metadata. That rule matches the
raw attribute value with no classification step, so an unrecognised spelling fails because
it does not match the allow list, not because someone predicted it. A prototype of this
enumerating what is *allowed* -- this project's own relative paths, plus one pinned CDN
pattern -- rejected all seven spellings above along with a foreign host, a floating
`@latest` version, and a `..` traversal, across all four of `a href`, `img src`,
`link href`, and `script src`, while still accepting relative paths, fragments, the
committed Prism URLs, and jsDelivr's `+esm` form.

It was not adopted here because it is a bespoke security control with a real maintenance
cost, and this project's documents are small enough to read. Two things to keep in mind if
it is revisited:

- **Enumerate what is allowed, never what is forbidden.** The allow list is short and
  known; the set of hostile spellings is not.
- **Verify by running the linter, not by reading the regex.** html-validate merges a
  directory's `.htmlvalidate.json` on top of a `--config` file, so a probe can silently
  test the committed configuration instead of the candidate one. Set `root: true` in the
  candidate config to stop the cascade.

The other gaps -- inline event handlers, the unchecked elements, SVG subtree contents --
have no configuration-level answer in either linter today. Closing them means either a
custom html-validate rule or a different tool, and neither is worth it while the review
list above is this short.

## Adding a document

New `.html`, `.htm`, `.xhtml`, and `.svg` files are picked up without any config change.
The toolchain gate walks for those four extensions and hands everything it finds to both
linters, and `lint:html` globs the same four. Those two lists have to agree: when they
drifted, `npm test` rejected a bad `.htm` that `npm run lint` had just passed, which is
the shape of divergence that trains people to disbelieve the fast check. The script
strings are pinned by a toolchain test, so a change to one that skips the other fails
loudly.

Two structural defaults are worth preserving:

- **`publicDir: false`** in `vite.config.ts`. Vite copies `public/` into `dist/`
  verbatim, without the bundler, compiler, or lint reading any of it. That directory is a
  hole straight through every gate here, and #4783's own reproduction used it.
- **Script stays in `src/`**, where the compiler and lint already look.

[4783]: https://github.com/richlander/dotnet-inspect/issues/4783
[agents]: ../../AGENTS.md
[hv]: https://html-validate.org/
[hh]: https://htmlhint.com/
