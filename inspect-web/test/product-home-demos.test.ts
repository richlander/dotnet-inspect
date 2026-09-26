import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import { parseSync } from "oxc-parser";
import { memberRequestKey } from "../src/data.ts";
import type {
  MemberCallGraphRequest,
  PlatformDrillRequest,
} from "../src/call-graph-inspection.ts";
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
  productHomeDemosViewHtml,
  setProductHomeDemoCatalog,
} from "../src/product-home-demos.ts";

test("Demos renders the ordered catalog separately from Workspace contents", () => {
  setProductHomeDemoCatalog([{
    id: "stj-serializer",
    title: "System.Text.Json",
    summary: "Browse a real package API",
  }, {
    id: "stj-graph",
    title: "Serializer graph",
    summary: "Explore serialization calls",
  }]);
  const html = productHomeDemosViewHtml(value => value, "");
  assert.match(html, /<h1>Demos<\/h1>/);
  assert.match(html, /2 available/);
  assert.match(html, /data-workspace-demo="stj-serializer"[\s\S]*data-workspace-demo="stj-graph"/);
  assert.match(html, /aria-label="Open demo System.Text.Json"/);
  assert.doesNotMatch(html, /data-workspace-save|loaded coordinates|<h2>Packages/);
});

test("Demos distinguishes an empty catalog from a visible catalog failure", () => {
  setProductHomeDemoCatalog([]);
  assert.match(productHomeDemosViewHtml(value => value, ""),
    /No product demos are available/);
  const failed = productHomeDemosViewHtml(
    value => value.replaceAll("<", "&lt;"),
    "Product demos are unavailable: <offline>");
  assert.match(failed, /role="alert">Product demos are unavailable: &lt;offline>/);
  assert.doesNotMatch(failed, /0 available|No product demos/);
});

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
      platformContextId: focusKind === "platform" ? "demo-context" : null,
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
  assert.equal(prepared.contextId, "demo-context");
  assert.equal(prepared.package.assembly, "System.Text.Json");
  assert.deepEqual(
    prepared.package.assemblies.map(assembly => assembly.name),
    ["System.Text.Json", "System.Runtime"]);
});

test("Platform activation rejects a missing retained context instead of ordinary browsing", () => {
  const demo = result(
    "platform", "runtime", "10.0.12", "net10.0", "System.Text.Json",
    [surface("Microsoft.NETCore.App", "10.0.12", "net10.0", "System.Text.Json", "netcore.app")]);
  assert.throws(
    () => prepareProductHomeDemoSource({
      ...demo,
      activation: { ...demo.activation!, platformContextId: null },
    }),
    /omitted its retained context/);
});

test("actual app activation, member reload, drill and workspace reset preserve context association", async () => {
  const source = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
  const parsed = parseSync("dotnet-inspect.ts", source);
  assert.deepEqual(parsed.errors, []);
  const names = new Set([
    "installPlatformHomeDemoSource", "clearWorkspacePackages",
    "memberRequestSignature", "loadSelectedMemberCallGraph", "drillPlatformNode",
  ]);
  const declarations = parsed.program.body.filter(node =>
    node.type === "FunctionDeclaration" && names.has(node.id?.name ?? ""));
  assert.equal(declarations.length, names.size);
  const demo = result(
    "platform", "runtime", "10.0.12", "net10.0", "System.Text.Json",
    [surface("Microsoft.NETCore.App", "10.0.12", "net10.0", "System.Text.Json", "netcore.app")]);
  const prepared = prepareProductHomeDemoSource(demo);
  assert.equal(prepared.kind, "platform");
  if (prepared.kind !== "platform") return;
  const pkg = prepared.package;
  const type = pkg.types[0]!;
  const overload = { name: "Serialize", signature: "Serialize()", graphSelectorKey: "selector", metadataToken: 1 };
  const state = {
    package: pkg,
    packages: [pkg],
    platformDemoContextId: null as string | null,
    callGraphTraversalFramework: "net12.0",
    selectedOverloadIndex: 0,
    selectedBodyTarget: null,
  };
  const loads: MemberCallGraphRequest[] = [];
  const drills: PlatformDrillRequest[] = [];
  const context = {
    state,
    prepared,
    activation: demo.activation,
    drillTarget: {
      assembly: "System.Text.Json", memberName: "Serialize",
      typeFullName: type.definitionId, selectorKey: "selector",
    },
    memberRequestKey,
    ensurePlatformCatalog: async () => ({ tfm: "net10.0", version: "10.0.12" }),
    navigationSequence: { isCurrent: () => true },
    platformGraphLibraryForTarget: () => ({}),
    platformLibraryMatchesDescriptor: () => true,
    retainPackageModel: () => { state.packages = [pkg]; },
    releasePackageModelCaches: () => {},
    openPlatformLibrary: async () => { state.package = pkg; return pkg; },
    selectedType: () => type,
    selectedMember: () => ({ overloads: [overload] }),
    selectedConcreteOverload: () => overload,
    currentPackage: () => pkg,
    assemblyDescriptorForType: () => pkg.assemblies[0],
    platformPackForAssembly: () => "netcore.app",
    callGraphInspection: {
      load: async (request: MemberCallGraphRequest) => { loads.push(request); },
      drill: async (request: PlatformDrillRequest) => { drills.push(request); },
    },
    directUseClusterInspection: { reset: () => {} },
    platformCatalogFramework: () => "net10.0",
    runtimePackPackage: () => pkg,
    runtimePackForFramework: () => pkg,
    capturedShareTabs: () => ({ resolvedTabs: [] }),
    resolvedPlatformTargetVersion: () => "10.0.12",
    platformPackForGraphAssembly: () => "netcore.app",
    callGraphTargetTypeId: () => type.definitionId,
    stripArity: (name: string) => name,
  };
  runInNewContext(
    stripTypeScriptTypes(declarations.map(node => source.slice(node.start, node.end)).join("\n")),
    context);
  const load = () => Promise.resolve<unknown>(
    runInNewContext("loadSelectedMemberCallGraph()", context));
  await Promise.resolve<unknown>(runInNewContext(
    'installPlatformHomeDemoSource(prepared, activation, "demo", 1)', context));
  assert.equal(state.platformDemoContextId, prepared.contextId);
  await load();
  overload.name = "Deserialize";
  overload.signature = "Deserialize()";
  await load();
  overload.name = "Serialize";
  overload.signature = "Serialize()";
  await load();
  await Promise.resolve<unknown>(runInNewContext("drillPlatformNode(drillTarget)", context));
  assert.deepEqual(loads.map(request => request.platformContextId),
    ["demo-context", "demo-context", "demo-context"]);
  assert.deepEqual(loads.map(request => request.traversalFramework),
    ["net12.0", "net12.0", "net12.0"]);
  assert.equal(drills[0]!.contextId, "demo-context");
  assert.equal(loads[0]!.signature, loads[2]!.signature);
  const retained = { ...state };
  runInNewContext("clearWorkspacePackages()", context);
  assert.equal(state.platformDemoContextId, null);
  state.package = pkg;
  await load();
  assert.equal(loads[3]!.platformContextId, null);
  assert.notEqual(loads[0]!.signature, loads[3]!.signature);
  Object.assign(state, retained);
  await load();
  assert.equal(loads[4]!.platformContextId, "demo-context");
  assert.equal(loads[4]!.signature, loads[0]!.signature);
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
  assert.match(ready, /Browse demos →/);
  assert.match(ready, /2 available/);
  assert.doesNotMatch(ready, /disabled/);

  assert.match(
    homeDemosEntryHtml(true, "", value => value),
    /disabled/);
  assert.match(
    homeDemosEntryHtml(false, "Unavailable", value => value),
    /Catalog unavailable/);
});
