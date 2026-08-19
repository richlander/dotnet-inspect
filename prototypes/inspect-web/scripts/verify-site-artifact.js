import { existsSync, readFileSync } from "node:fs";
import { resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

export function verifySiteArtifact(siteArgument) {
  const site = resolve(siteArgument);
  const manifestPath = resolve(site, "manifest.json");
  const indexPath = resolve(site, "index.html");
  if (!existsSync(manifestPath) || !existsSync(indexPath)) {
    throw new Error(`${siteArgument} is missing index.html or manifest.json.`);
  }

  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const indexEntry = manifest["index.html"];
  if (!indexEntry || typeof indexEntry.file !== "string") {
    throw new Error("The Vite manifest has no index.html entry.");
  }

  const assets = new Set();
  const addAsset = (asset) => {
    if (
      typeof asset !== "string"
      || !/^assets\/(?:[A-Za-z0-9_-][A-Za-z0-9._-]*\/)*[A-Za-z0-9_-][A-Za-z0-9._-]*$/.test(asset)
      || asset.split("/").some((segment) => segment === "." || segment === "..")
    ) {
      throw new Error(`The Vite manifest contains invalid asset '${asset}'.`);
    }
    assets.add(asset);
  };

  for (const [key, entry] of Object.entries(manifest)) {
    if (!entry || typeof entry !== "object") {
      throw new Error(`The Vite manifest entry '${key}' is invalid.`);
    }
    addAsset(entry.file);
    for (const asset of entry.css ?? []) {
      addAsset(asset);
    }
    for (const asset of entry.assets ?? []) {
      addAsset(asset);
    }
    for (const imported of [...(entry.imports ?? []), ...(entry.dynamicImports ?? [])]) {
      if (typeof imported !== "string" || !Object.hasOwn(manifest, imported)) {
        throw new Error(`The Vite manifest entry '${key}' imports missing entry '${imported}'.`);
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

  const index = readFileSync(indexPath, "utf8");
  if (!index.includes(`src="/${indexEntry.file}"`)) {
    throw new Error(`index.html does not load Vite entry '${indexEntry.file}'.`);
  }

  for (const stylesheet of indexEntry.css ?? []) {
    if (!index.includes(`href="/${stylesheet}"`)) {
      throw new Error(`index.html does not load Vite stylesheet '${stylesheet}'.`);
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const siteArgument = process.argv[2];
  if (!siteArgument) {
    throw new Error("Usage: node scripts/verify-site-artifact.js <site-directory>");
  }
  verifySiteArtifact(siteArgument);
}
