import { existsSync, readFileSync } from "node:fs";
import { resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

// This script gates `npm run build`, and the manifest it reads is Rollup output rather
// than a hand-written file, so the shape it relies on can change under it. `JSON.parse`
// is typed `any`, which would hand every read below back unchecked; the reads are
// narrowed from `unknown` through type predicates instead, so a manifest that stops
// matching produces this file's own diagnostic rather than a `TypeError` raised several
// members later.
function isObjectLike(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

function isList(value: unknown): value is readonly unknown[] {
  return Array.isArray(value);
}

const assetPattern
  = /^assets\/(?:[A-Za-z0-9_-][A-Za-z0-9._-]*\/)*[A-Za-z0-9_-][A-Za-z0-9._-]*$/;

function validateAsset(asset: unknown): string {
  if (
    typeof asset !== "string"
    || !assetPattern.test(asset)
    || asset.split("/").some(segment => segment === "." || segment === "..")
  ) {
    throw new Error(`The Vite manifest contains invalid asset '${String(asset)}'.`);
  }
  return asset;
}

// An absent list is the normal case for an entry with no stylesheets or imports. A
// present non-list was previously read straight into `for...of`, where an object failed
// as "not iterable" and a string silently iterated its characters; naming it here keeps
// the failure attributable to the entry that carries it.
function assetList(value: unknown, key: string, field: string): readonly unknown[] {
  if (value === undefined || value === null) {
    return [];
  }
  if (!isList(value)) {
    throw new Error(`The Vite manifest entry '${key}' has a non-array '${field}'.`);
  }
  return value;
}

export function verifySiteArtifact(siteArgument: string): void {
  const site = resolve(siteArgument);
  const manifestPath = resolve(site, "manifest.json");
  const indexPath = resolve(site, "index.html");
  if (!existsSync(manifestPath) || !existsSync(indexPath)) {
    throw new Error(`${siteArgument} is missing index.html or manifest.json.`);
  }

  const parsed: unknown = JSON.parse(readFileSync(manifestPath, "utf8"));
  // A non-object manifest has no `index.html` entry either, so it reports the same
  // missing-entry failure it did when this read was unchecked.
  const manifest: Readonly<Record<string, unknown>> = isObjectLike(parsed) ? parsed : {};
  const indexValue = manifest["index.html"];
  const indexEntry = isObjectLike(indexValue) ? indexValue : undefined;
  const indexFile = indexEntry?.file;
  if (typeof indexFile !== "string") {
    throw new Error("The Vite manifest has no index.html entry.");
  }
  const index = readFileSync(indexPath, "utf8");
  const baseIndex = index.indexOf('<base href="/"');
  const preloadIndex = index.indexOf('rel="preload"');
  const importMapIndex = index.indexOf('<script type="importmap"');
  if (baseIndex < 0) {
    throw new Error('index.html is missing <base href="/">.');
  }
  if (preloadIndex >= 0 && baseIndex > preloadIndex) {
    throw new Error('index.html places <base href="/"> after the runtime preload.');
  }
  if (importMapIndex >= 0 && baseIndex > importMapIndex) {
    throw new Error('index.html places <base href="/"> after the import map.');
  }

  const assets = new Set<string>();

  for (const [key, value] of Object.entries(manifest)) {
    if (!isObjectLike(value)) {
      throw new Error(`The Vite manifest entry '${key}' is invalid.`);
    }
    assets.add(validateAsset(value.file));
    for (const asset of assetList(value.css, key, "css")) {
      assets.add(validateAsset(asset));
    }
    for (const asset of assetList(value.assets, key, "assets")) {
      assets.add(validateAsset(asset));
    }
    for (const imported of [
      ...assetList(value.imports, key, "imports"),
      ...assetList(value.dynamicImports, key, "dynamicImports"),
    ]) {
      if (typeof imported !== "string" || !Object.hasOwn(manifest, imported)) {
        throw new Error(
          `The Vite manifest entry '${key}' imports missing entry '${String(imported)}'.`,
        );
      }
    }
  }

  if (assets.size === 0) {
    throw new Error("The Vite manifest contains no output assets.");
  }

  for (const asset of assets) {
    const assetPath = resolve(site, asset);
    if (!assetPath.startsWith(`${site}${sep}`) || !existsSync(assetPath)) {
      throw new Error(`The Vite manifest references missing asset '${asset}'.`);
    }
  }

  if (!index.includes(`src="/${indexFile}"`)) {
    throw new Error(`index.html does not load Vite entry '${indexFile}'.`);
  }

  // The loop above already validated every asset the manifest declares, so this mapping
  // recovers the element type without introducing a failure the loop would not have
  // raised first.
  for (const stylesheet of assetList(indexEntry?.css, "index.html", "css")
    .map(validateAsset)) {
    if (!index.includes(`href="/${stylesheet}"`)) {
      throw new Error(`index.html does not load Vite stylesheet '${stylesheet}'.`);
    }
  }
}

const invokedPath = process.argv[1];
if (invokedPath !== undefined
  && import.meta.url === pathToFileURL(resolve(invokedPath)).href) {
  const siteArgument = process.argv[2];
  if (!siteArgument) {
    throw new Error("Usage: node scripts/verify-site-artifact.ts <site-directory>");
  }
  verifySiteArtifact(siteArgument);
}
