// Re-fetches every third-party subresource pinned in index.html and checks that the bytes
// still hash to the `integrity` value committed beside them.
//
// `require-sri` in .htmlvalidate.json enforces that a cross-origin subresource *carries* a
// digest. That is a source-text property and it is the whole of what a linter can see. It
// says nothing about whether the digest still describes what the CDN serves today, because
// that fact lives on the network and changes without any commit to this repository.
//
// What this closes is narrow, and it is not a security control. SRI is the security
// control and the browser enforces it: a stale pin means the browser *refuses* the bytes
// rather than running unexpected ones. The failure is that the site quietly loses the
// subresource -- on this site, syntax highlighting stops working -- with nothing to say
// why. The same check would also surface a CDN serving different bytes under a pinned
// immutable URL, which is worth an issue even though SRI already blocked it.
//
// The document is read with html-validate's own parser rather than with a pattern, so the
// checker and the linter cannot disagree about what the markup contains.

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
// `"SCRIPT"`. Not normalizing would drop an uppercase subresource while still reporting
// success. Attribute lookup is already case-insensitive.
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

// `<noscript>` and `<template>` content is inert: it is not fetched on load, so checking
// it would report drift for bytes the browser never requests.
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

    // Resolved the way a browser resolves it, rather than by inspecting the spelling:
    // `https:\\host/path` and `//host/path` are both cross-origin fetches.
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

  // Hashed from the response bytes rather than from decoded text, so a resource ending
  // in a newline is not misread.
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
