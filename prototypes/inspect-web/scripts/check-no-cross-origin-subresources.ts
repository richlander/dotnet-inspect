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
//
// The set of attributes below is the set the *browser* fetches, which is deliberately not
// the set Subresource Integrity applies to. SRI covers scripts and a few link relations
// because those are the things it can hash; a containment claim is about every load the
// document causes, so `<img>`, `<iframe>`, `srcset` and the rest belong here even though
// no digest could ever be attached to them.

import { HtmlValidate, Parser, type HtmlElement } from "html-validate";
import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

// Directories that hold generated or third-party markup. `dist` is this project's own
// build output and `node_modules` is other people's packages; neither is authored here, so
// neither is evidence about what this project ships.
const skippedDirectories = new Set(["node_modules", "dist", "artifacts", ".git"]);

// Relative URLs have to resolve against something to decide whether they are cross-origin.
// The scheme and host here stand for "wherever this site is served from"; only the
// same-origin/cross-origin distinction is read off the result, never the host itself.
const siteOrigin = "https://site.invalid/";

export const EXIT_VIOLATION = 1;
export const EXIT_INFRASTRUCTURE = 2;

// Every element/attribute pair whose value the browser resolves and fetches while loading
// the document. Navigation targets (`<a href>`, `<form action>`) are deliberately absent:
// they are destinations the user chooses, not resources the page pulls in.
const fetchingAttributes: ReadonlyMap<string, readonly string[]> = new Map([
  ["script", ["src"]],
  ["link", ["href", "imagesrcset"]],
  ["img", ["src", "srcset"]],
  ["source", ["src", "srcset"]],
  ["video", ["src", "poster"]],
  ["audio", ["src"]],
  ["track", ["src"]],
  ["iframe", ["src"]],
  ["frame", ["src"]],
  ["embed", ["src"]],
  ["object", ["data"]],
  ["input", ["src"]],
  ["body", ["background"]],
  ["table", ["background"]],
  ["td", ["background"]],
  ["th", ["background"]],
  ["use", ["href", "xlink:href"]],
  ["image", ["href", "xlink:href"]],
]);

// `srcset` and `imagesrcset` hold a comma-separated candidate list, each candidate a URL
// followed by an optional descriptor. Checking the raw attribute would miss every
// candidate but the first.
const multiValueAttributes = new Set(["srcset", "imagesrcset"]);

// Schemes that carry their own payload or address the current document. They reach no
// origin, but their resolved `origin` is the opaque `"null"`, which would otherwise read
// as cross-origin.
const inertSchemes = new Set(["data:", "blob:", "about:", "javascript:", "mailto:", "tel:"]);

// CSS fetches too -- `url()`, `@import` and `@font-face` all reach the network -- and no
// amount of markup parsing can see through a stylesheet. Rather than grow a CSS parser,
// this check asserts those constructs are absent, so introducing one is a deliberate
// change here instead of a silent gap in the claim above.
const cssFetchConstruct = /url\(|@import|@font-face/iu;

export interface Subresource {
  readonly url: string;
  readonly element: string;
  readonly attribute: string;
  readonly document: string;
  readonly crossOrigin: boolean;
}

export interface ScanResult {
  readonly documents: readonly string[];
  readonly subresources: readonly Subresource[];
  readonly baseUrls: readonly Subresource[];
  readonly cssFetches: readonly string[];
}

// The parser preserves the source spelling of a tag name, so `<SCRIPT>` arrives as
// `"SCRIPT"`. Not normalizing would drop an uppercase subresource while still reporting
// success. Attribute lookup is already case-insensitive.
function tagNameOf(element: HtmlElement): string {
  return element.tagName.toLowerCase();
}

// `<noscript>` and `<template>` content is inert: it is not fetched on load, so a URL there
// is not a subresource the browser requests.
function isInertContext(element: HtmlElement): boolean {
  return element.closest("noscript") !== null || element.closest("template") !== null;
}

function isInertScheme(value: string): boolean {
  const trimmed = value.trim().toLowerCase();
  for (const scheme of inertSchemes) {
    if (trimmed.startsWith(scheme)) {
      return true;
    }
  }
  return trimmed.startsWith("#");
}

// One candidate per comma; the URL is everything before the first space, since the width
// or density descriptor that may follow is not part of it.
function candidateUrls(attribute: string, value: string): readonly string[] {
  if (!multiValueAttributes.has(attribute)) {
    return [value];
  }
  return value
    .split(",")
    .map((candidate) => candidate.trim().split(/\s+/u)[0] ?? "")
    .filter((candidate) => candidate !== "");
}

// A document's base URL is the first `<base href>` in tree order, and every relative URL in
// the document resolves against it -- including URLs written above the tag. Resolving
// against the origin instead would call a redirected fetch same-origin.
function effectiveBase(dom: ReturnType<Parser["parseHtml"]>): { readonly url: string; readonly declared: string | null } {
  for (const element of dom.querySelectorAll("base")) {
    if (isInertContext(element)) {
      continue;
    }
    const href = element.getAttributeValue("href");
    if (href === null || href.trim() === "") {
      continue;
    }
    try {
      return { url: new URL(href, siteOrigin).href, declared: href };
    } catch {
      continue;
    }
  }
  return { url: siteOrigin, declared: null };
}

function isCrossOrigin(resolved: URL): boolean {
  return resolved.origin !== new URL(siteOrigin).origin;
}

async function filesUnder(directory: string, pattern: RegExp): Promise<readonly string[]> {
  const found: string[] = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (skippedDirectories.has(entry.name)) {
        continue;
      }
      found.push(...await filesUnder(full, pattern));
    } else if (pattern.test(entry.name)) {
      found.push(full);
    }
  }
  return found;
}

async function readDocument(
  documentPath: string,
  projectRoot: string,
): Promise<{ readonly subresources: readonly Subresource[]; readonly baseUrls: readonly Subresource[]; readonly cssFetches: readonly string[] }> {
  const htmlValidate = new HtmlValidate();
  const config = await htmlValidate.getConfigFor(documentPath);
  const parser = new Parser(config);
  const data = await readFile(documentPath, "utf8");
  const dom = parser.parseHtml({ data, filename: documentPath, line: 1, column: 1, offset: 0 });
  const relative = path.relative(projectRoot, documentPath);

  const base = effectiveBase(dom);
  const baseUrls: Subresource[] = [];
  if (base.declared !== null) {
    const resolved = new URL(base.url);
    baseUrls.push({
      url: resolved.href,
      element: "base",
      attribute: "href",
      document: relative,
      crossOrigin: isCrossOrigin(resolved),
    });
  }

  const subresources: Subresource[] = [];
  const cssFetches: string[] = [];
  for (const element of dom.querySelectorAll("*")) {
    if (isInertContext(element)) {
      continue;
    }
    const tag = tagNameOf(element);

    // A stylesheet reached through markup is checked as CSS below; one written inline is
    // checked here, because it never becomes a file.
    if (tag === "style" && cssFetchConstruct.test(element.textContent)) {
      cssFetches.push(`${relative} (<style> block)`);
    }
    const inlineStyle = element.getAttributeValue("style");
    if (inlineStyle !== null && cssFetchConstruct.test(inlineStyle)) {
      cssFetches.push(`${relative} (<${tag} style="...">)`);
    }

    for (const attribute of fetchingAttributes.get(tag) ?? []) {
      const value = element.getAttributeValue(attribute);
      if (value === null || value.trim() === "") {
        continue;
      }
      for (const candidate of candidateUrls(attribute, value)) {
        if (isInertScheme(candidate)) {
          continue;
        }

        // Resolved the way a browser resolves it -- against the document's effective base,
        // not by inspecting the spelling -- so `https:\\host/path` and `//host/path` are
        // both recognized as cross-origin fetches.
        let resolved: URL;
        try {
          resolved = new URL(candidate, base.url);
        } catch {
          continue;
        }
        subresources.push({
          url: resolved.href,
          element: tag,
          attribute,
          document: relative,
          crossOrigin: isCrossOrigin(resolved),
        });
      }
    }
  }
  return { subresources, baseUrls, cssFetches };
}

export async function scan(projectRoot: string): Promise<ScanResult> {
  const documents = await filesUnder(projectRoot, /\.(?:html|htm|xhtml)$/iu);
  const subresources: Subresource[] = [];
  const baseUrls: Subresource[] = [];
  const cssFetches: string[] = [];

  for (const document of documents) {
    const read = await readDocument(document, projectRoot);
    subresources.push(...read.subresources);
    baseUrls.push(...read.baseUrls);
    cssFetches.push(...read.cssFetches);
  }

  for (const stylesheet of await filesUnder(projectRoot, /\.css$/iu)) {
    if (cssFetchConstruct.test(await readFile(stylesheet, "utf8"))) {
      cssFetches.push(path.relative(projectRoot, stylesheet));
    }
  }

  return { documents, subresources, baseUrls, cssFetches };
}

async function main(): Promise<never> {
  const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

  let result: ScanResult;
  try {
    result = await scan(projectRoot);
  } catch (error) {
    console.error(`could not scan ${projectRoot}: ${String(error)}`);
    return process.exit(EXIT_INFRASTRUCTURE);
  }

  // A check whose passing condition is "found nothing" has to prove it looked. Finding no
  // documents at all, or no subresources in any of them, means the markup shape changed
  // rather than that the project is clean.
  if (result.documents.length === 0) {
    console.error(`no HTML documents found under ${projectRoot}`);
    console.error("this check reads them, so finding none means the project layout changed");
    return process.exit(EXIT_INFRASTRUCTURE);
  }
  if (result.subresources.length === 0) {
    console.error(`no subresources found in ${String(result.documents.length)} document(s)`);
    console.error("every document here loads at least a stylesheet or a module, so finding");
    console.error("none means the extraction stopped seeing markup it used to see");
    return process.exit(EXIT_INFRASTRUCTURE);
  }

  const violations = [...result.baseUrls, ...result.subresources]
    .filter((subresource) => subresource.crossOrigin);
  for (const violation of violations) {
    console.error(`CROSS-ORIGIN ${violation.document}`);
    console.error(
      `             loaded by <${violation.element} ${violation.attribute}> `
      + `from ${violation.url}`);
  }
  for (const stylesheet of result.cssFetches) {
    console.error(`CSS FETCH    ${stylesheet}`);
    console.error("             url(), @import or @font-face can reach another origin");
  }

  if (violations.length > 0 || result.cssFetches.length > 0) {
    console.error("");
    console.error("This project resolves third-party code through the bundler so that the");
    console.error("shipped documents reach no origin but their own. Add the package as a");
    console.error("dependency and import it, or change this check deliberately.");
    return process.exit(EXIT_VIOLATION);
  }

  console.log(
    `same-origin only: ${String(result.subresources.length)} subresource(s) across `
    + `${String(result.documents.length)} document(s)`);
  return process.exit(0);
}

if (process.argv[1] !== undefined
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
