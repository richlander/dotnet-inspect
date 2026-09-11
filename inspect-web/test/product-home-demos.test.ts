import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserHomeDemoRunResult,
  BrowserPackageSurface,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  homeDemosEntryHtml,
  isProductHomeDemoId,
  isProductHomeDemosPath,
  prepareProductHomeDemoSource,
  productHomeDemoCatalog,
  setProductHomeDemoCatalog,
} from "../src/product-home-demos.ts";

function surface(
  packageId: string,
  version: string,
  framework: string,
  assembly: string,
  platformPack: string | null = null,
): BrowserPackageSurface {
  return {
    package: packageId,
    version,
    frameworks: [framework],
    activeFramework: framework,
    icon: null,
    defaultAssemblyId: assembly,
    compileLibrary: {
      status: "Selected",
      targetFramework: framework,
      message: null,
    },
    assemblies: [{
      id: assembly,
      name: assembly,
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null,
      asset: `${assembly}.dll`,
      publicTypes: 1,
      publicMembers: 1,
      platformPack,
    }],
    types: [{
      id: `${assembly}.Type`,
      definitionId: `${assembly}.Type`,
      queryId: `${assembly}.Type`,
      metadataId: `${assembly}.Type`,
      name: "Type",
      displayName: "Type",
      namespace: assembly,
      kind: "class",
      accessibility: "public",
      accessibilityId: "public",
      assembly,
      assemblyId: assembly,
      assemblyName: assembly,
      members: 0,
      signature: `public class ${assembly}.Type`,
      api: [],
      platformPack,
    }],
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 1,
    }],
    totalMembers: 0,
    documents: [],
    inspectionErrors: [],
    inspectionError: null,
  };
}

function result(
  focusKind: string,
  focusId: string,
  focusVersion: string,
  focusFramework: string,
  focusAssembly: string | null,
  packages: readonly BrowserPackageSurface[],
): BrowserHomeDemoRunResult {
  return {
    found: true,
    packages,
    activation: {
      focusKind,
      focusId,
      focusVersion,
      focusFramework,
      focusAssembly,
      typeId: `${focusAssembly ?? focusId}.Type`,
      section: "Methods",
      memberName: null,
      memberKind: null,
      memberAnchorDigest: null,
      memberSection: null,
    },
    callGraph: null,
  };
}

test("isProductHomeDemoId uses the installed engine catalog", () => {
  const catalog = [
    {
      id: "stj-serializer",
      title: "System.Text.Json",
      summary: "Browse a real package API",
    },
    {
      id: "aspire-redis-callgraph",
      title: "Aspire AddRedis",
      summary: "Redis resource registration graph",
    },
  ];
  setProductHomeDemoCatalog(catalog);
  assert.deepEqual(productHomeDemoCatalog(), catalog);
  assert.equal(isProductHomeDemoId("stj-serializer"), true);
  assert.equal(isProductHomeDemoId("aspire-redis-callgraph"), true);
  assert.equal(isProductHomeDemoId("platform-list"), false);
  assert.equal(isProductHomeDemoId(""), false);
  assert.equal(isProductHomeDemoId(undefined), false);
});

test("product demo discovery owns one canonical application route", () => {
  assert.equal(isProductHomeDemosPath("/demos"), true);
  assert.equal(isProductHomeDemosPath("/demos/"), true);
  assert.equal(isProductHomeDemosPath("/DEMOS"), false);
  assert.equal(isProductHomeDemosPath("/demos//"), false);
  assert.equal(isProductHomeDemosPath("/demo"), false);
  assert.equal(isProductHomeDemosPath("/"), false);
});

test("package activation retains a non-first exact focus", () => {
  const peer = surface("Peer.Package", "1.0.0", "net10.0", "Peer");
  const focus = surface("Focus.Package", "2.0.0", "net10.0", "Focus");
  const prepared = prepareProductHomeDemoSource(
    result(
      "package",
      "Focus.Package",
      "2.0.0",
      "net10.0",
      null,
      [peer, focus]));
  assert.equal(prepared.kind, "package");
  if (prepared.kind !== "package") return;
  assert.deepEqual(
    prepared.packages.map(packageModel => packageModel.id),
    ["Peer.Package", "Focus.Package"]);
  assert.equal(prepared.focusPackage.id, "Focus.Package");
});

test("Platform activation merges one exact target around its non-first focus", () => {
  const peer = surface(
    "Microsoft.NETCore.App",
    "10.0.12",
    "net10.0",
    "System.Runtime",
    "netcore.app");
  const focus = surface(
    "Microsoft.NETCore.App",
    "10.0.12",
    "net10.0",
    "System.Text.Json",
    "netcore.app");
  const prepared = prepareProductHomeDemoSource(
    result(
      "platform",
      "runtime",
      "10.0.12",
      "net10.0",
      "System.Text.Json",
      [peer, focus]));
  assert.equal(prepared.kind, "platform");
  if (prepared.kind !== "platform") return;
  assert.equal(prepared.focusAssembly, "System.Text.Json");
  assert.equal(prepared.focusPack, "netcore.app");
  assert.equal(prepared.package.assembly, "System.Text.Json");
  assert.deepEqual(
    prepared.package.assemblies.map(assembly => assembly.name),
    ["System.Text.Json", "System.Runtime"]);
});

test("Platform activation rejects mixed exact targets before model installation", () => {
  const focus = surface(
    "Microsoft.NETCore.App",
    "10.0.12",
    "net10.0",
    "System.Text.Json",
    "netcore.app");
  const peer = surface(
    "Microsoft.NETCore.App",
    "11.0.0",
    "net11.0",
    "System.Runtime",
    "netcore.app");
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "platform",
        "runtime",
        "10.0.12",
        "net10.0",
        "System.Text.Json",
        [focus, peer])),
    /different exact targets/);
});

test("Platform activation requires one exact focus descriptor and family", () => {
  const focus = surface(
    "Microsoft.NETCore.App",
    "10.0.12",
    "net10.0",
    "System.Text.Json");
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "platform",
        "runtime",
        "10.0.12",
        "net10.0",
        "System.Text.Json",
        [focus])),
    /retain its Platform family/);
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "platform",
        "runtime",
        "10.0.12",
        "net10.0",
        "System.Text.Json",
        [
          { ...focus, assemblies: [{ ...focus.assemblies[0]!, platformPack: "netcore.app" }] },
          { ...focus, assemblies: [{ ...focus.assemblies[0]!, platformPack: "netcore.app" }] },
        ])),
    /matched 2 returned surfaces/);
});

test("Platform activation requires its source family to match the focus pack", () => {
  const focus = surface(
    "Microsoft.NETCore.App",
    "10.0.12",
    "net10.0",
    "System.Text.Json",
    "netcore.app");
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "platform",
        "aspnetcore",
        "10.0.12",
        "net10.0",
        "System.Text.Json",
        [focus])),
    /focus family did not match/);
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "platform",
        "netstandard",
        "10.0.12",
        "net10.0",
        "System.Text.Json",
        [focus])),
    /focus family did not match/);
});

test("unknown activation kinds fail rather than becoming package-shaped", () => {
  assert.throws(
    () => prepareProductHomeDemoSource(
      result(
        "embedded",
        "Example",
        "1.0.0",
        "net10.0",
        null,
        [surface("Example", "1.0.0", "net10.0", "Example")])),
    /unsupported product home demo focus/);
});

test("home demo entry reflects catalog readiness", () => {
  setProductHomeDemoCatalog([
    { id: "one", title: "One", summary: "First" },
    { id: "two", title: "Two", summary: "Second" },
  ]);
  const ready = homeDemosEntryHtml(false, "", value => value);
  assert.match(ready, /id="home-demos"/);
  assert.match(ready, /2 available/);
  assert.doesNotMatch(ready, /disabled/);

  assert.match(
    homeDemosEntryHtml(true, "", value => value),
    /disabled/);
  assert.match(
    homeDemosEntryHtml(false, "Unavailable", value => value),
    /Catalog unavailable/);
});
