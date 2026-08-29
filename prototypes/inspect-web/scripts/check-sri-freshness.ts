// Re-fetches every third-party subresource pinned in index.html and checks the bytes
// still hash to the `integrity` value committed beside them.
//
// `require-sri` in .htmlvalidate.json enforces that a cross-origin subresource *carries* a
// digest. That is a source-text property, and it is the whole of what a linter can see. It
// says nothing about whether the digest still describes what the CDN serves today, because
// that fact lives on the network and changes without any commit to this repository.
//
// The failure this closes is narrow and worth naming. A stale pin does not silently load
// the wrong script -- the browser enforces SRI and refuses it -- so the risk is not
// execution of unexpected bytes. It is that the site quietly loses the subresource, which
// on this site means syntax highlighting stops working, with nothing in CI to say why. The
// same check also surfaces a CDN that has begun serving different bytes under a pinned
// immutable URL, which is a supply-chain signal worth an issue even though SRI already
// blocked it.
//
// The document is read with html-validate's own parser rather than with a pattern. An
// earlier version matched tags and attributes with regular expressions and was defeated
// five separate ways in one review round: `data-integrity` shadowed `integrity` because
// `\b` treats `-` as a boundary, `<!--` inside a quoted value started a comment that
// swallowed a later real one, a quoted `>` ended the tag early, a backslash-form URL was
// read as same-origin, and a `<noscript>` subresource the browser never requests was
// checked anyway. Each of those is the same mistake -- a pattern is not an HTML parser --
// so the fix is to stop guessing at the grammar. Using the parser that already lints this
// file additionally means the extractor and the linter cannot disagree about what the
// document contains.

import { createHash } from "node:crypto";
import { HtmlValidate, Parser, type HtmlElement } from "html-validate";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const documentPath = path.join(projectRoot, "index.html");

// Relative URLs have to resolve against something to decide whether they are cross-origin.
// The scheme and host here stand for "wherever this site is served from"; only the
// same-origin/cross-origin distinction is read off the result, never the host itself.
const siteOrigin = "https://site.invalid/";

// Exit codes are distinguished so the scheduled workflow can say which kind of failure
// happened. A stale pin is actionable by re-pinning; an unreachable CDN is not, and
// reporting one as the other sends somebody to look for a digest mismatch that is not
// there.
const EXIT_PIN_PROBLEM = 1;
const EXIT_INFRASTRUCTURE = 2;

// Which elements Subresource Integrity actually applies to. These mirror html-validate's
// `require-sri` rule so the two agree about what needs a digest: SRI is defined for
// `script[src]` and for `link` with one of these `rel` values, where `preload` also needs
// an `as` naming a subresource kind SRI covers.
const sriLinkRel = new Set(["stylesheet", "preload", "modulepreload"]);
const sriPreloadAs = new Set(["style", "script"]);

// Algorithms SRI defines, strongest first. A browser uses the strongest algorithm present
// in the attribute and ignores the rest, so this order decides which digests are
// authoritative when an attribute lists more than one.
const algorithmStrength = ["sha512", "sha384", "sha256"];

interface Pin {
  readonly url: string;
  readonly element: string;
  readonly integrity: string;
}

// The parser preserves the source spelling of a tag name, so `<SCRIPT>` arrives as
// `"SCRIPT"`. HTML tag names are case-insensitive and the browser does not care, so
// comparing without normalizing would drop an uppercase subresource out of this check
// while still reporting success. Attribute lookup is already case-insensitive.
function tagNameOf(element: HtmlElement): string {
  return element.tagName.toLowerCase();
}

function isSriEligible(element: HtmlElement): boolean {
  if (tagNameOf(element) === "script") {
    return true;
  }
  const rel = element.getAttributeValue("rel");
  if (rel === null || !sriLinkRel.has(rel.toLowerCase())) {
    return false;
  }
  if (rel.toLowerCase() !== "preload") {
    return true;
  }
  const as = element.getAttributeValue("as");
  return as !== null && sriPreloadAs.has(as.toLowerCase());
}

// `<noscript>` content is not parsed as markup when scripting is enabled, and when
// scripting is disabled a `<script>` inside it is not executed either way. `<template>`
// content is inert until cloned. Neither is fetched on load, so checking one would report
// drift for bytes the browser never requests.
function isInertContext(element: HtmlElement): boolean {
  return element.closest("noscript") !== null || element.closest("template") !== null;
}

async function readPins(): Promise<readonly Pin[]> {
  const htmlValidate = new HtmlValidate();
  const config = await htmlValidate.getConfigFor(documentPath);
  const parser = new Parser(config);
  const data = await readFile(documentPath, "utf8");
  const dom = parser.parseHtml({ data, filename: documentPath, line: 1, column: 1, offset: 0 });

  const pins: Pin[] = [];
  for (const element of dom.querySelectorAll("script, link")) {
    if (isInertContext(element) || !isSriEligible(element)) {
      continue;
    }
    const value =
      tagNameOf(element) === "script"
        ? element.getAttributeValue("src")
        : element.getAttributeValue("href");
    if (value === null || value === "") {
      continue;
    }

    // Resolved the way a browser resolves it, rather than by inspecting the spelling.
    // `https:\\host/path` and `//host/path` are both cross-origin fetches that a pattern
    // keyed on `//` reads as same-origin or as relative.
    let resolved: URL;
    try {
      resolved = new URL(value, siteOrigin);
    } catch {
      continue;
    }
    if (resolved.origin === new URL(siteOrigin).origin) {
      continue;
    }

    pins.push({
      url: resolved.href,
      element: tagNameOf(element),
      integrity: element.getAttributeValue("integrity") ?? "",
    });
  }
  return pins;
}

interface Metadata {
  readonly algorithm: string;
  readonly digests: readonly string[];
}

// The `integrity` attribute is a whitespace-separated set of `<algorithm>-<base64>` items,
// each optionally carrying `?options`. Algorithm names are ASCII case-insensitive. A
// browser selects the strongest algorithm it supports and accepts the resource if any
// digest for that algorithm matches, so anything weaker present alongside it is ignored
// rather than being a second requirement.
function parseIntegrity(integrity: string): Metadata | { readonly error: string } {
  const tokens = integrity.split(/[\t\n\f\r ]+/u).filter((token) => token !== "");
  if (tokens.length === 0) {
    return { error: "integrity attribute is empty" };
  }

  const byAlgorithm = new Map<string, string[]>();
  for (const token of tokens) {
    const separator = token.indexOf("-");
    if (separator === -1) {
      return { error: `integrity metadata '${token}' is not '<algorithm>-<base64>'` };
    }
    const algorithm = token.slice(0, separator).toLowerCase();
    if (!algorithmStrength.includes(algorithm)) {
      // Unknown algorithms are ignored by browsers rather than rejected, so that a future
      // algorithm can be listed alongside a current one. Ignoring it here keeps this from
      // failing on a document a browser is perfectly happy with.
      continue;
    }
    // `?options` is reserved by the spec and is not part of the digest.
    const digest = token.slice(separator + 1).split("?")[0] ?? "";
    const existing = byAlgorithm.get(algorithm);
    if (existing === undefined) {
      byAlgorithm.set(algorithm, [digest]);
    } else {
      existing.push(digest);
    }
  }

  for (const algorithm of algorithmStrength) {
    const digests = byAlgorithm.get(algorithm);
    if (digests !== undefined) {
      return { algorithm, digests };
    }
  }
  return { error: `no integrity metadata names an algorithm SRI defines (${integrity})` };
}

let pinProblem = false;
let infrastructureProblem = false;

function report(status: string, url: string, ...detail: readonly string[]): void {
  console.log(`${status.padEnd(8)} ${url}`);
  for (const line of detail) {
    console.log(`         ${line}`);
  }
}

let pins: readonly Pin[];
try {
  pins = await readPins();
} catch (error) {
  console.error(`could not read ${documentPath}: ${String(error)}`);
  process.exit(EXIT_INFRASTRUCTURE);
}

if (pins.length === 0) {
  console.error(`no third-party subresources found in ${documentPath}`);
  console.error("this script exists to check them, so finding none means the markup shape changed");
  process.exit(EXIT_INFRASTRUCTURE);
}

for (const pin of pins) {
  if (pin.integrity === "") {
    report("MISSING", pin.url, `loaded from another origin by <${pin.element}> with no integrity attribute`);
    pinProblem = true;
    continue;
  }

  const metadata = parseIntegrity(pin.integrity);
  if ("error" in metadata) {
    report("INVALID", pin.url, metadata.error);
    pinProblem = true;
    continue;
  }

  // The response is hashed from its bytes rather than from decoded text. An earlier
  // version captured the body in a shell variable first, which strips trailing newlines
  // and would report drift on a resource that ends in one.
  let actual: string;
  try {
    const response = await fetch(pin.url, { redirect: "follow" });
    if (!response.ok) {
      report("FETCH", pin.url, `server responded ${String(response.status)} ${response.statusText}`);
      infrastructureProblem = true;
      continue;
    }
    actual = createHash(metadata.algorithm)
      .update(Buffer.from(await response.arrayBuffer()))
      .digest("base64");
  } catch (error) {
    report("FETCH", pin.url, "could not be retrieved", String(error));
    infrastructureProblem = true;
    continue;
  }

  if (metadata.digests.includes(actual)) {
    report("OK", pin.url);
  } else {
    report(
      "DRIFT",
      pin.url,
      ...metadata.digests.map((digest) => `pinned ${metadata.algorithm}-${digest}`),
      `served ${metadata.algorithm}-${actual}`,
    );
    pinProblem = true;
  }
}

// A pin problem outranks an outage: if both happened, the actionable one is the digest.
if (pinProblem) {
  process.exit(EXIT_PIN_PROBLEM);
}
if (infrastructureProblem) {
  process.exit(EXIT_INFRASTRUCTURE);
}
