import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  DEFAULT_PLATFORM_FRAMEWORK,
  parsePlatformCatalogTarget,
  parsePlatformIndex,
} from "../src/platform-index.ts";

function row(assembly: string, overrides: Record<string, unknown> = {}) {
  return {
    tfm: "net11.0", pack: "netcore.app", assembly, file: `${assembly}.dll`,
    kind: "impl", forwardsTo: null, version: "11.0.0.0", publicTypes: 3,
    inReferencePack: true, hasImplementation: true,
    packVersion: "11.0.0-preview.7.26381.103", ...overrides,
  };
}

function catalog() {
  return {
    schemaVersion: 1,
    defaultFramework: DEFAULT_PLATFORM_FRAMEWORK,
    targets: [{
      tfm: "net11.0", version: "11.0.0-preview.7.26381.103",
      rows: [
        row("System.Text.Json"),
        row("System.Runtime", { kind: "facade", forwardsTo: "System.Private.CoreLib" }),
        row("System.Private.CoreLib", { inReferencePack: false }),
        row("Reference.Only", { kind: "ref", hasImplementation: false }),
      ],
    }],
  };
}

test("catalog preserves reference membership independently of runtime role", () => {
  const index = parsePlatformIndex(catalog());
  assert.deepEqual(index.assembliesFor("net11.0").filter(library => library.inReferencePack)
    .map(library => library.assembly), ["System.Text.Json", "System.Runtime", "Reference.Only"]);
  assert.equal(index.lookup("net11.0", "System.Private.CoreLib")?.inReferencePack, false);
  assert.equal(index.lookup("net11.0", "system.text.json.DLL")?.kind, "impl");
  assert.equal(index.isFacade("net11.0", "System.Runtime"), true);
  assert.equal(index.forwardsTo("net11.0", "System.Runtime"), "System.Private.CoreLib");
  assert.equal(index.lookup("net11.0", "Reference.Only")?.hasImplementation, false);
  assert.equal(index.target("net10.0"), null);
});

test("new exact catalogs coexist without changing the selected shipped target", () => {
  const index = parsePlatformIndex(catalog());
  const version = "11.0.0-rc.1.1";
  index.addTarget(parsePlatformCatalogTarget({
    tfm: "net11.0", version,
    rows: [row("New.Library", { packVersion: version })],
  }));
  assert.equal(index.target("net11.0")?.version, "11.0.0-preview.7.26381.103");
  assert.equal(index.target("net11.0", version)?.rows[0]?.assembly, "New.Library");
  assert.equal(index.lookup("net11.0", "New.Library"), null);
  assert.equal(index.lookup("net11.0", "New.Library", "netcore.app", version)?.packVersion, version);
  assert.equal(index.targets().length, 2);
  assert.throws(() => index.addTarget(parsePlatformCatalogTarget({
    tfm: "net11.0", version,
    rows: [row("Different.Library", { packVersion: version })],
  })), /changed for pinned target/);
});

test("catalog rejects mismatched versions and invalid rather than empty inventories", () => {
  const target = catalog().targets[0];
  assert.ok(target);
  assert.throws(() => parsePlatformCatalogTarget({ ...target, version: "11.0.0" }), /coordinate mismatch/);
  assert.throws(() => parsePlatformCatalogTarget({ ...target, rows: [] }), /no library inventory/);
  assert.throws(() => parsePlatformCatalogTarget({ ...target, rows: [row("Bad", { publicTypes: -1 })] }), /type count/);
  assert.throws(() => parsePlatformCatalogTarget({
    ...target, rows: [row("Bad", { inReferencePack: false, hasImplementation: false })],
  }), /membership/);
  assert.throws(() => parsePlatformCatalogTarget({
    ...target, rows: [row("Duplicate"), row("duplicate")],
  }), /Duplicate/);
  assert.throws(() => parsePlatformIndex({ ...catalog(), defaultFramework: "net12.0" }), /default target/);
});

test("library identity includes its pack instead of choosing a colliding name", () => {
  const value = catalog();
  const target = value.targets[0];
  assert.ok(target);
  target.rows.push(row("System.Text.Json", { pack: "aspnetcore.app" }));
  const index = parsePlatformIndex(value);
  assert.equal(index.lookup("net11.0", "System.Text.Json"), null);
  assert.equal(index.lookup("net11.0", "System.Text.Json", "netcore.app")?.pack, "netcore.app");
});

test("shipped catalog supplies the exact default target and representative library roles", async () => {
  const value: unknown = JSON.parse(await readFile(
    new URL("../assets/platform-index.json", import.meta.url), "utf8"));
  const index = parsePlatformIndex(value);
  const target = index.target(DEFAULT_PLATFORM_FRAMEWORK);
  assert.ok(target);
  assert.match(target.version, /^11\.0\./);
  const json = index.lookup(target.tfm, "System.Text.Json", "netcore.app", target.version);
  const facade = index.lookup(target.tfm, "System.Runtime", "netcore.app", target.version);
  const core = index.lookup(target.tfm, "System.Private.CoreLib", "netcore.app", target.version);
  assert.equal(json?.kind, "impl");
  assert.equal(json?.inReferencePack, true);
  assert.equal(facade?.kind, "facade");
  assert.equal(facade?.inReferencePack, true);
  assert.equal(core?.kind, "impl");
  assert.equal(core?.inReferencePack, false);
  assert.equal(core?.hasImplementation, true);
  assert.ok(target.rows.every(library => library.packVersion === target.version));
});
