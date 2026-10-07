import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  parsePlatformCatalogTarget,
  parsePlatformIndex,
  platformCatalogFramework,
  platformRuntimePruningInventory,
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
    schemaVersion: 2,
    defaultFramework: "net11.0",
    targets: [{
      tfm: "net11.0", version: "11.0.0-preview.7.26381.103",
      supplies: [{
        pack: "netcore.app",
        family: "Microsoft.NETCore.App",
        package: "System.Text.Json",
        version: "11.0.0-preview.7.26381.103",
      }],
      rows: [
        row("System.Text.Json"),
        row("System.Runtime", { kind: "facade", forwardsTo: "System.Private.CoreLib" }),
        row("System.Private.CoreLib", { inReferencePack: false }),
        row("Reference.Only", { kind: "ref", hasImplementation: false }),
      ],
    }],
  };
}

test("platform-qualified targets use their base catalog release line", () => {
  assert.equal(platformCatalogFramework("net10.0"), "net10.0");
  assert.equal(platformCatalogFramework("net10.0-browser"), "net10.0");
  assert.equal(
    platformCatalogFramework("net10.0-windows10.0.19041.0"),
    "net10.0");
  assert.equal(platformCatalogFramework("net10.0-"), "net10.0-");
  assert.equal(platformCatalogFramework("netstandard2.0"), "netstandard2.0");
});

test("catalog preserves reference membership independently of runtime role", () => {
  const index = parsePlatformIndex(catalog());
  assert.deepEqual(index.assembliesFor("net11.0").filter(library => library.inReferencePack)
    .map(library => library.assembly), ["System.Text.Json", "System.Runtime", "Reference.Only"]);
  assert.equal(index.lookup("net11.0", "System.Private.CoreLib")?.inReferencePack, false);
  assert.equal(index.lookup("net11.0", "system.text.json.DLL")?.kind, "impl");
  assert.equal(index.isFacade("net11.0", "System.Runtime"), true);
  assert.equal(index.forwardsTo("net11.0", "System.Runtime"), "System.Private.CoreLib");
  assert.equal(index.lookup("net11.0", "Reference.Only")?.hasImplementation, false);
  assert.deepEqual(index.target("net11.0")?.supplies, [{
    pack: "netcore.app",
    family: "Microsoft.NETCore.App",
    package: "System.Text.Json",
    version: "11.0.0-preview.7.26381.103",
  }]);
  assert.equal(index.target("net10.0"), null);
});

test("call graph pruning receives only the exact Runtime supply inventory", () => {
  const value = catalog().targets[0];
  assert.ok(value);
  const target = parsePlatformCatalogTarget({
    ...value,
    supplies: [
      ...value.supplies,
      {
        pack: "aspnetcore.app",
        family: "Microsoft.AspNetCore.App",
        package: "Microsoft.Extensions.Http",
        version: value.version,
      },
    ],
  });
  assert.deepEqual(
    platformRuntimePruningInventory(target, target.tfm),
    {
      tfm: target.tfm,
      version: target.version,
      supplies: [{
        package: "System.Text.Json",
        version: target.version,
      }],
    });
  assert.deepEqual(
    platformRuntimePruningInventory({
      ...target,
      supplies: null,
    }, target.tfm),
    {
      tfm: target.tfm,
      version: target.version,
      supplies: null,
    });
  assert.deepEqual(
    platformRuntimePruningInventory(target, `${target.tfm}-ios`),
    {
      tfm: target.tfm,
      version: target.version,
      supplies: null,
    });
});

test("new exact catalogs coexist without changing the selected shipped target", () => {
  const index = parsePlatformIndex(catalog());
  const version = "11.0.0-rc.1.1";
  index.addTarget(parsePlatformCatalogTarget({
    tfm: "net11.0", version,
    supplies: [],
    rows: [row("New.Library", { packVersion: version })],
  }));
  assert.equal(index.target("net11.0")?.version, "11.0.0-preview.7.26381.103");
  assert.equal(index.target("net11.0", version)?.rows[0]?.assembly, "New.Library");
  assert.equal(index.lookup("net11.0", "New.Library"), null);
  assert.equal(index.lookup("net11.0", "New.Library", "netcore.app", version)?.packVersion, version);
  assert.equal(index.targets().length, 2);
  assert.throws(() => index.addTarget(parsePlatformCatalogTarget({
    tfm: "net11.0", version,
    supplies: [],
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
  assert.throws(() => parsePlatformCatalogTarget({
    ...target,
    supplies: [
      ...target.supplies,
      { ...target.supplies[0]!, family: "Microsoft.AspNetCore.App" },
    ],
  }), /supply family/);
  assert.throws(() => parsePlatformCatalogTarget({
    ...target,
    supplies: [...target.supplies, target.supplies[0]!],
  }), /Duplicate platform package supply/);
  const targetWithoutSupplies = { ...target, supplies: undefined };
  assert.equal(
    parsePlatformCatalogTarget(targetWithoutSupplies).supplies,
    null);
  assert.throws(
    () => parsePlatformIndex({
      ...catalog(),
      targets: [targetWithoutSupplies],
    }),
    /no exact package supply inventory/);
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

test("shipped catalog has the exact default target and representative library roles", async () => {
  const value: unknown = JSON.parse(await readFile(
    new URL("../assets/platform-index.json", import.meta.url), "utf8"));
  const index = parsePlatformIndex(value);
  const target = index.target(index.defaultFramework);
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
