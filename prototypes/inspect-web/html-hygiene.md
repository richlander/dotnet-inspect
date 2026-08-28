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
2. **Maintained linters check the documents themselves.** They own subresource integrity,
   external-reference policy, inline event handlers, `javascript:` URLs, and general HTML
   validity.

### Why linters rather than a bespoke gate

A hand-written gate stood here for six review rounds and was removed. It tokenized HTML
itself and enumerated the elements and attributes a document was allowed to contain, in
order to prove that no document could reach script by any route.

The instrument was wrong for the claim. Proving that property means reimplementing HTML
tokenization and, in the end, reconstructing a bundler's private behaviour from its
compiled output. Each review round found one more construct the enumeration had missed,
because a shape recognizer over someone else's semantics has no closure argument
available to it. The linters answer more of the question than the bespoke gate ever did:
`require-sri` covers `<link rel="stylesheet|preload|modulepreload">` as well as
`<script>`, which the bespoke gate never checked.

The bespoke gate also drifted across the trust boundary this repository actually defends.
It ended up decoding character references to catch a URL disguised inside our own
reviewed `index.html` -- which is to say, it modelled a contributor as an attacker.
[`AGENTS.md`][agents] rules that out: the boundary is untrusted internet-origin data, not
our own source.

## What the tools own

Both linters run in `npm run lint`, which CI invokes through `npm run analyze`.

| Tool | Configuration | What it covers |
| --- | --- | --- |
| [html-validate][hv] | `.htmlvalidate.json`, `html-elements.json` | HTML validity and the `recommended` preset; `attr-pattern` rejecting every `on*` event handler attribute; `require-sri` for cross-origin `<script>` and `<link>`; the SRI digest grammar, declared as element metadata; `allowed-links` restricting external references to an allow list |
| [htmlhint][hh] | `.htmlhintrc` | `inline-script-disabled` -- `javascript:` URLs in link and source attributes |

Inline event handlers are owned by html-validate's `attr-pattern`, configured with a
pattern that rejects any attribute name beginning `on`. That is deliberately a *shape*
rather than a list. htmlhint's `inline-script-disabled` also flags event handlers, but it
enumerates event names and its list predates the modern ones -- `onpointerdown`,
`onbeforeinput`, `onanimationstart` and `ontoggle` all pass it. A rule that has to be
extended every time the platform grows an event is the same losing position this project
just left, so the pattern does that work and htmlhint is kept only for `javascript:` URLs.

Read that second column narrowly. `inline-script-disabled` inspects `href` and `src`; a
`javascript:` URL reached through any other executable attribute is not covered, and
[review owns it](#javascript-urls-outside-href-and-src).

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

`toolchain.test.ts` owns the wiring rather than the analysis. It hands both linters every
document the project owns and requires a clean report, then requires the same committed
configuration to reject a specimen *by name of the rule that must reject it*. A linter
that stops running, loses its rules, or is never handed a document fails there instead of
going quietly green.

## What review owns

The linters do not cover everything the bespoke gate attempted. These are the hazards to
watch for when reviewing any change that touches a document. None of them is
tool-enforced.

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

### `iframe srcdoc`

`<iframe srcdoc="&lt;script&gt;...">` runs script on load and needs no interaction.
Neither linter flags it. There are no `<iframe>` elements in this project today.

**In review, reject:** `srcdoc` outright.

### A remote document base

`<base href="https://elsewhere.example/">` makes every relative URL in the document --
including the bundle Vite emits under `/assets/` -- resolve somewhere else, while each
`src` still *looks* local. `verify-site-artifact.ts` requires `<base href="/">` to appear
before any preload or import map, but nothing checks that a base is local.

**In review, reject:** any `<base>` whose `href` is not `/`.

### Third-party bytes

`require-sri` enforces that cross-origin `<script>` and `<link>` carry an `integrity`
attribute, and `attribute-allowed-values` enforces that its value is a digest a browser
will actually honour. That second half is not html-validate's default: `require-sri` is a
presence check, and out of the box `integrity="bogus"` satisfies it. A browser discards
metadata it cannot use and fetches the resource *unpinned*, so a malformed digest looks
pinned to a reader and behaves like no SRI at all. `html-elements.json` closes that gap by
declaring the SRI grammar as element metadata for `<script>` and `<link>`.

That grammar follows the [SRI][sri] and [CSP3][csp] productions rather than being
tightened to taste, because a false rejection here blocks a legitimate dependency bump.
It accepts both the base64 and base64url alphabets, optional padding, surrounding and
separating whitespace, multiple digests, and the `?options` suffix. It pins the
*significant length* of each digest -- 43, 64, and 86 characters for SHA-256, SHA-384, and
SHA-512 -- because length is what distinguishes a real digest from a truncated paste, and
a truncated digest fails closed in the browser at runtime.

`allowed-links` then restricts external references to the CDN allow list in
`.htmlvalidate.json`. That pattern pins the *version*, not just the host: it matches
`cdn.jsdelivr.net/npm/<package>@<major>.<minor>.<patch>/`, so `@latest`, a major-only
`@1`, a bare package name with no version, and `/gh/<user>/<repo>@<branch>/` are all
rejected.

Pinning the version there is not redundant with SRI. SRI does not apply to every element
that loads bytes -- `<img>` has no `integrity` -- so for those the URL is the only pin
available, and a floating URL serves whatever the CDN has today. Where SRI does apply, a
floating URL at least fails closed once the digest stops matching, which is a broken page
rather than unreviewed code. An exact version avoids both.

These are real: a CDN can change the bytes it serves, and this is the one boundary in this
project where an actor outside the machine is in scope.

The tools enforce the mechanics, so what is left for review is the judgement:

**In review, check:** widening `allowExternal` -- adding a host, or loosening the version
pattern -- is a deliberate decision about whose bytes we run, not a lint fix. Bumping a
pinned dependency means a new version *and* a new digest, together.

### Link relations that `require-sri` does not recognise

`require-sri` matches the whole `rel` attribute against a small set of values instead of
tokenizing it. `rel="stylesheet"` is checked; `rel="alternate stylesheet"`,
`rel="stylesheet license"`, and `rel="STYLESHEET"` are not, and load without `integrity`.
The allow list still applies, so such a link can only reach the pinned CDN -- but it
reaches it unpinned.

**In review, reject:** any `<link>` with a multi-token or unusually cased `rel` that
carries no `integrity`.

### `javascript:` URLs outside `href` and `src`

htmlhint's `inline-script-disabled` catches `javascript:` in `href` and `src`, which is
where it usually appears. It does not look anywhere else, and several other attributes
navigate or load:

```html
<form action="javascript:alert(1)"></form>
<object data="javascript:alert(1)"></object>
<meta http-equiv="refresh" content="0;url=javascript:alert(1)" />
```

Both linters accept all three as configured. html-validate's `meta-refresh` rule does not
help -- it governs the refresh *delay*, for accessibility, and is indifferent to the
scheme.

**In review, reject:** the `javascript:` scheme in any attribute, not just `href` and
`src`.

### Script inside SVG

SVG loads external code through `<script href="...">`, not HTML's `src`. It is a
different attribute in a different namespace, and none of the rules above look at it:
`require-sri` and `allowed-links` both key off HTML's `src`/`href` on HTML elements, so an
SVG `<script href>` passes every gate here. Element metadata does not help, because
html-validate does not apply it to foreign-namespace content.

There are no `.svg` files in this project today.

**In review, reject:** `<script>` inside SVG, whether it carries `href`, `xlink:href`, or
a body.

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
[sri]: https://www.w3.org/TR/sri/
[csp]: https://www.w3.org/TR/CSP3/
