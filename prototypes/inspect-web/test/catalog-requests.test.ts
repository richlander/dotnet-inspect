import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { parseSync } from "oxc-parser";
import {
  createCatalogRequests,
  type CatalogPackage,
  type CatalogRequestDependencies,
  type CatalogRequestState,
} from "../src/catalog-requests.ts";
import type { BrowserPackageVersions } from "../src/facades/inspect-web-package.d.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

const pkg = (id = "Example.Package"): CatalogPackage => ({ id, version: "2.0.0" });
const inventory = (): BrowserPackageVersions => ({
  versions: ["2.0.0", "1.0.0"],
  currentVersionInsertionIndex: 0,
  previousVersion: "1.0.0",
  previousVersionUnavailableReason: null,
});

const source = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const selector = parseSync("dotnet-inspect.ts", source).program.body.find(
  node => node.type === "FunctionDeclaration" && node.id?.name === "versionOptionsHtml");
assert.ok(selector);
const selectorSource = stripTypeScriptTypes(source.slice(selector.start, selector.end));

function harness(overrides: Partial<CatalogRequestDependencies> = {}) {
  const current = pkg();
  const state: CatalogRequestState = {
    package: current,
    packages: [current],
    dotnetReleases: null,
    dotnetReleasesLoading: false,
  };
  const updated: CatalogPackage[] = [];
  let platformUpdates = 0;
  const requests = createCatalogRequests({
    state,
    queryDotnetReleases: async () => [{ major: 10, tfm: "net10.0", version: "10.0.0" }],
    queryPackageVersions: async () => inventory(),
    updatePlatformVersionSelect: () => { platformUpdates++; },
    updatePackageVersionSelect: item => updated.push(item),
    ...overrides,
  });
  return { requests, state, current, updated, platformUpdates: () => platformUpdates };
}

test("release requests cache rows and refresh only a selected Platform", async () => {
  const h = harness();
  h.current.isRuntimePack = true;
  await h.requests.ensureDotnetReleases();
  await h.requests.ensureDotnetReleases();
  assert.deepEqual(h.state.dotnetReleases, [{ major: 10, tfm: "net10.0", version: "10.0.0" }]);
  assert.equal(h.platformUpdates(), 1);
  assert.equal(h.state.dotnetReleasesLoading, false);
  const other = harness();
  await other.requests.ensureDotnetReleases();
  assert.equal(other.platformUpdates(), 0);
});

test("release requests deduplicate pending work and preserve retry on failure", async () => {
  const pending = deferred<never>();
  let calls = 0;
  const h = harness({ queryDotnetReleases: () => { calls++; return pending.promise; } });
  const first = h.requests.ensureDotnetReleases();
  await h.requests.ensureDotnetReleases();
  assert.equal(calls, 1);
  pending.reject(new Error("offline"));
  await first;
  assert.equal(h.state.dotnetReleasesLoading, false);
  assert.equal(h.state.dotnetReleases, null);
  await h.requests.ensureDotnetReleases();
  assert.equal(calls, 2);
});

test("version requests retain native order and default for the exact resident model", async () => {
  const h = harness();
  await h.requests.ensurePackageVersions(h.current);
  assert.deepEqual(h.requests.packageVersions(h.current), {
    status: "available", inventory: inventory(),
  });
  assert.deepEqual(h.updated, [h.current]);
  const sameCoordinate = pkg();
  assert.deepEqual(h.requests.packageVersions(sameCoordinate), { status: "idle" });
});

const orderedVersions = ["2.0.0", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0"] as const;
for (const [current, insertionIndex, returned, reason, expected] of [
  ["3.0.0", 0, orderedVersions, null,
    ["3.0.0", "2.0.0", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0"]],
  ["1.11.0", 2, orderedVersions, null,
    ["2.0.0", "2.0.0-rc.1", "1.11.0", "1.10.0", "1.9.0", "1.0.0"]],
  ["0.5.0", 5, orderedVersions, null,
    ["2.0.0", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0", "0.5.0"]],
  ["2.0.0-rc.2", 1, orderedVersions, null,
    ["2.0.0", "2.0.0-rc.2", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0"]],
  ["2.0.0+other", 0, orderedVersions, null,
    ["2.0.0+other", "2.0.0", "2.0.0-rc.1", "1.10.0", "1.9.0", "1.0.0"]],
  ["2.0.0", 0, orderedVersions, null, orderedVersions],
  ["1.0.0", 0, [], null, ["1.0.0"]],
  ["1.5.0", 1, ["2.0.0", "1.0.0"], "Listing unknown",
    ["2.0.0", "1.5.0", "1.0.0"]],
] as const) {
  test(`production selector preserves native order for ${current} in [${returned.join(", ")}]`, async () => {
    const value: BrowserPackageVersions = {
      versions: [...returned],
      currentVersionInsertionIndex: insertionIndex,
      previousVersion: null,
      previousVersionUnavailableReason: reason,
    };
    const h = harness({ queryPackageVersions: async () => value });
    h.current.version = current;
    await h.requests.ensurePackageVersions(h.current);
    const html: unknown = runInNewContext(`${selectorSource}\nversionOptionsHtml(pkg)`, {
      pkg: h.current,
      catalogRequests: h.requests,
      escapeHtml: (text: string) => text,
    });
    assert.ok(typeof html === "string");
    const options = [...html.matchAll(/<option value="([^"]+)"([^>]*)>/g)];
    assert.deepEqual(options.map(match => match[1]), expected);
    assert.deepEqual(options.filter(match => match[2]?.includes("selected"))
      .map(match => match[1]), [current]);
    assert.deepEqual(value.versions, returned);
  });
}

test("version requests ignore missing, unretained, and Platform models", async () => {
  const h = harness({ queryPackageVersions: async () => { throw new Error("must not query"); } });
  await h.requests.ensurePackageVersions(null);
  await h.requests.ensurePackageVersions(pkg());
  h.current.isRuntimePack = true;
  await h.requests.ensurePackageVersions(h.current);
  assert.deepEqual(h.updated, []);
});

test("version requests deduplicate pending work and reuse a completed inventory", async () => {
  const pending = deferred<BrowserPackageVersions>();
  let calls = 0;
  const h = harness({ queryPackageVersions: () => { calls++; return pending.promise; } });
  const first = h.requests.ensurePackageVersions(h.current);
  await h.requests.ensurePackageVersions(h.current);
  assert.equal(calls, 1);
  assert.deepEqual(h.requests.packageVersions(h.current), { status: "loading" });
  pending.resolve(inventory());
  await first;
  await h.requests.ensurePackageVersions(h.current);
  assert.equal(calls, 1);
});

test("failure is visible and retries only when explicitly invalidated", async () => {
  let calls = 0;
  const h = harness({ queryPackageVersions: async () => { calls++; throw new Error("offline"); } });
  await h.requests.ensurePackageVersions(h.current);
  assert.deepEqual(h.requests.packageVersions(h.current), { status: "failed", message: "offline" });
  await h.requests.ensurePackageVersions(h.current);
  assert.equal(calls, 1);
  h.requests.forgetPackage(h.current);
  await h.requests.ensurePackageVersions(h.current);
  assert.equal(calls, 2);
  assert.deepEqual(h.updated, [h.current, h.current]);
});

for (const reject of [false, true]) {
  test(`late ${reject ? "failure" : "success"} cannot publish after same-coordinate replacement`, async () => {
    const pending = deferred<BrowserPackageVersions>();
    const h = harness({ queryPackageVersions: () => pending.promise });
    const first = h.requests.ensurePackageVersions(h.current);
    const replacement = pkg();
    h.state.packages = [replacement];
    h.requests.forgetPackage(h.current);
    if (reject) pending.reject(new Error("late"));
    else pending.resolve(inventory());
    await first;
    assert.deepEqual(h.requests.packageVersions(replacement), { status: "idle" });
    assert.deepEqual(h.updated, []);
  });
}

test("a replaced pending request cannot overwrite an explicit retry on the same model", async () => {
  const pending = deferred<BrowserPackageVersions>();
  let calls = 0;
  const h = harness({
    queryPackageVersions: () => ++calls === 1 ? pending.promise : Promise.resolve(inventory()),
  });
  const first = h.requests.ensurePackageVersions(h.current);
  h.requests.forgetPackage(h.current);
  await h.requests.ensurePackageVersions(h.current);
  pending.reject(new Error("superseded"));
  await first;
  assert.deepEqual(h.requests.packageVersions(h.current), { status: "available", inventory: inventory() });
  assert.deepEqual(h.updated, [h.current]);
});

test("rollback copies completed inventories but never copies a pending request", async () => {
  const h = harness();
  await h.requests.ensurePackageVersions(h.current);
  const copy = pkg();
  h.requests.copyPackage(h.current, copy);
  assert.deepEqual(h.requests.packageVersions(copy), h.requests.packageVersions(h.current));
  const pending = deferred<BrowserPackageVersions>();
  const other = harness({ queryPackageVersions: () => pending.promise });
  const first = other.requests.ensurePackageVersions(other.current);
  other.requests.copyPackage(other.current, copy);
  assert.deepEqual(other.requests.packageVersions(copy), { status: "idle" });
  pending.resolve(inventory());
  await first;
});

test("publication callback errors are not swallowed as acquisition failures", async () => {
  const h = harness({ updatePackageVersionSelect: () => { throw new Error("render failed"); } });
  await assert.rejects(h.requests.ensurePackageVersions(h.current), /render failed/);
});
