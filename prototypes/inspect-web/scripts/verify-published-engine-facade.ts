import assert from "node:assert/strict";
import {
  existsSync,
  readFileSync,
  symlinkSync,
  unlinkSync,
} from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface PublishedEngineFacade {
  asyncLoweringCanary(): Promise<string>;
  buildIdentity(): unknown;
  configureHost(origin: string): void;
  initializeRuntime(): Promise<void>;
  queryPackageVersions(packageId: string): Promise<readonly string[]>;
  runEntryPoint(): Promise<number>;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isPublishedEngineFacade(
  value: unknown,
): value is PublishedEngineFacade {
  return isRecord(value)
    && typeof value.asyncLoweringCanary === "function"
    && typeof value.buildIdentity === "function"
    && typeof value.configureHost === "function"
    && typeof value.initializeRuntime === "function"
    && typeof value.queryPackageVersions === "function"
    && typeof value.runEntryPoint === "function";
}

const siteArgument = process.argv[2];
if (!siteArgument) {
  throw new Error(
    "Usage: verify-published-engine-facade.ts <published-wwwroot>");
}

const site = resolve(siteArgument);
const index = readFileSync(resolve(site, "index.html"), "utf8");
const dotnetModule = /"\.\/_framework\/dotnet\.js": "\.\/_framework\/([^"]+\.js)"/
  .exec(index)?.[1];
assert.ok(dotnetModule, "published import map has no dotnet.js mapping");

const frameworkDirectory = resolve(site, "_framework");
const dotnetAlias = resolve(frameworkDirectory, "dotnet.js");
assert.equal(
  existsSync(dotnetAlias),
  false,
  "published framework unexpectedly contains an unhashed dotnet.js");
symlinkSync(dotnetModule, dotnetAlias);

try {
  assert.equal(
    Reflect.has(globalThis, "window"),
    false,
    "published facade smoke must not depend on a window global");

  const imported: unknown = await import(
    pathToFileURL(resolve(site, "inspect-web-engine.js")).href);
  assert.ok(
    isPublishedEngineFacade(imported),
    "published facade has an unexpected public shape");

  await imported.initializeRuntime();
  imported.configureHost("https://dotnet-inspect.net");
  assert.equal(await imported.runEntryPoint(), 0);

  const identity: unknown = imported.buildIdentity();
  assert.ok(
    isRecord(identity) && typeof identity.version === "string",
    "synchronous build identity did not cross the generated facade");

  assert.equal(
    await imported.asyncLoweringCanary(),
    "inspect-web-async-lowering-ok",
    "awaited lowering canary did not cross the generated facade");

  console.log(
    `inspect-web published facade smoke passed (${identity.version}; `
      + "deterministic async canary).");
} finally {
  unlinkSync(dotnetAlias);
}
