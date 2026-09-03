// Checks that no document in this project loads a subresource from another origin.
//
// This replaces a weekly check that re-fetched pinned CDN bytes and compared them against
// their committed `integrity` digests. That check existed because the site loaded Prism
// from jsDelivr, and whether a pinned digest still described what the CDN served was a
// fact about the network rather than about the source tree. Prism is now an ordinary
// dependency resolved by the bundler, so there is no third-party subresource left to drift
// and no digest left to re-pin.
//
// What replaces it is stronger rather than weaker. Freshness was a maintenance property --
// a stale pin meant the browser refused the bytes and syntax highlighting quietly stopped
// working. The property enforced here is containment: the shipped documents reach no
// origin but their own, so there is no third-party fetch to be tampered with, blocked, or
// deanonymized in the first place. It is also decidable offline, which the freshness check
// was not, so it runs on every pull request instead of once a week.
//
// Adding a cross-origin subresource is a deliberate decision, not an accident to be
// patched up with a digest. If one is ever warranted, this check is the place that has to
// change, and changing it puts the decision in a diff.
//
// Documents are read with html-validate's own parser rather than with a pattern, so this
// check and the linter cannot disagree about what the markup contains.

import { HtmlValidate, Parser, type HtmlElement } from "html-validate";
import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

// Directories that hold generated or third-party markup. `dist` is this project's own
// build output and `node_modules` is other people's packages; neither is authored here, so
// neither is evidence about what this project ships.
const skippedDirectories = new Set(["node_modules", "dist", "artifacts", ".git"]);

// Relative URLs have to resolve against something to decide whether they are cross-origin.
// The scheme and host here stand for "wherever this site is served from"; only the
// same-origin/cross-origin distinction is read off the result, never the host itself.
const siteOrigin = "https://site.invalid/";

const EXIT_VIOLATION = 1;
const EXIT_INFRASTRUCTURE = 2;

// Which elements Subresource Integrity applies to, and therefore which ones represent a
// subresource fetch the browser performs on load. These mirror html-validate's
// `require-sri` rule so the two agree about what counts as a subresource.
const sriLinkRel = new Set(["stylesheet", "preload", "modulepreload"]);
const sriPreloadAs = new Set(["style", "script"]);

interface Subresource {
  readonly url: string;
  readonly element: string;
  readonly document: string;
  readonly crossOrigin: boolean;
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

// `<noscript>` and `<template>` content is inert: it is not fetched on load, so a URL there
// is not a subresource the browser requests.
function isInertContext(element: HtmlElement): boolean {
  return element.closest("noscript") !== null || element.closest("template") !== null;
}

async function htmlDocuments(directory: string): Promise<readonly string[]> {
  const found: string[] = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (skippedDirectories.has(entry.name)) {
        continue;
      }
      found.push(...await htmlDocuments(full));
    } else if (/\.(?:html|htm|xhtml)$/iu.test(entry.name)) {
      found.push(full);
    }
  }
  return found;
}

async function readSubresources(documentPath: string): Promise<readonly Subresource[]> {
  const htmlValidate = new HtmlValidate();
  const config = await htmlValidate.getConfigFor(documentPath);
  const parser = new Parser(config);
  const data = await readFile(documentPath, "utf8");
  const dom = parser.parseHtml({ data, filename: documentPath, line: 1, column: 1, offset: 0 });

  const subresources: Subresource[] = [];
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

    subresources.push({
      url: resolved.href,
      element: tagNameOf(element),
      document: path.relative(projectRoot, documentPath),
      crossOrigin: resolved.origin !== new URL(siteOrigin).origin,
    });
  }
  return subresources;
}

let documents: readonly string[];
try {
  documents = await htmlDocuments(projectRoot);
} catch (error) {
  console.error(`could not enumerate documents under ${projectRoot}: ${String(error)}`);
  process.exit(EXIT_INFRASTRUCTURE);
}

// A check whose passing condition is "found nothing" has to prove it looked. Finding no
// documents at all, or no subresources in any of them, means the markup shape changed
// rather than that the project is clean.
if (documents.length === 0) {
  console.error(`no HTML documents found under ${projectRoot}`);
  console.error("this check reads them, so finding none means the project layout changed");
  process.exit(EXIT_INFRASTRUCTURE);
}

const subresources: Subresource[] = [];
for (const document of documents) {
  try {
    subresources.push(...await readSubresources(document));
  } catch (error) {
    console.error(`could not read ${document}: ${String(error)}`);
    process.exit(EXIT_INFRASTRUCTURE);
  }
}

if (subresources.length === 0) {
  console.error(`no subresources found in ${String(documents.length)} document(s)`);
  console.error("every document here loads at least a stylesheet or a module, so finding");
  console.error("none means the extraction stopped seeing markup it used to see");
  process.exit(EXIT_INFRASTRUCTURE);
}

const violations = subresources.filter((subresource) => subresource.crossOrigin);
for (const violation of violations) {
  console.error(`CROSS-ORIGIN ${violation.document}`);
  console.error(`             loaded by <${violation.element}> from ${violation.url}`);
}

if (violations.length > 0) {
  console.error("");
  console.error("This project resolves third-party code through the bundler so that the");
  console.error("shipped documents reach no origin but their own. Add the package as a");
  console.error("dependency and import it, or change this check deliberately.");
  process.exit(EXIT_VIOLATION);
}

console.log(
  `same-origin only: ${String(subresources.length)} subresource(s) across `
  + `${String(documents.length)} document(s)`);
