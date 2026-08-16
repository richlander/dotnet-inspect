import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  assemblyDescriptorForType,
  callGraphAssemblyIdentityMatches,
  callGraphDiagnosticsMessage,
  callGraphTargetTypeId,
  createDependencyGraphPendingState,
  createDependencyGraphRenderSequence,
  dependencyCoordinateCandidates,
  dependencyGroupSelectionMessage,
  dependencyGraphGroupSelectionIndex,
  dependencyGraphExternalKey,
  dependencyGraphPackageKey,
  dependencyGraphRenderSignature,
  ensureBoundedGraphNode,
  graphTargetNavigationDisposition,
  graphMemberSelection,
  MARKDOWN_SANITIZE_OPTIONS,
  MAX_SHARE_STATE_CHARACTERS,
  MAX_WORKSPACE_PACKAGES,
  memberRequestKey,
  mermaidLabel,
  normalizeShareTabs,
  packageCoordinateMatchesLocation,
  packageForView,
  packageIdentityKey,
  parameterTitleHtml,
  removeWorkspacePackage,
  retainWorkspacePackage,
  resolveLoadedGraphTargetCandidate,
  shareStateLengthError,
  scopedRequestState,
  selectedDependencyGroup,
  spotlightCandidateKey,
  spotlightCandidateSignature,
  uniqueTypeByQueryId,
  workspaceCoordinatesMatch
} from "../src/data.js";

const packageAt = (version, framework, types = 1) => ({
  id: "Example.Package",
  version,
  activeFramework: framework,
  types: Array.from({ length: types }, (_, index) => ({ id: `Type${index}` }))
});

test("dependency candidates carry typed package provenance to the product engine", () => {
  const packageCandidate = packageAt("2.0.0", "net8.0");
  const runtimeCandidate = {
    ...packageAt("10.0.10", "net10.0"),
    id: "Microsoft.NETCore.App",
    isRuntimePack: true
  };

  assert.deepEqual(
    dependencyCoordinateCandidates([packageCandidate, runtimeCandidate]),
    [
      {
        key: packageIdentityKey(packageCandidate),
        provenance: "NuGetPackage",
        packageId: "Example.Package",
        version: "2.0.0",
        targetFramework: "net8.0"
      },
      {
        key: packageIdentityKey(runtimeCandidate),
        provenance: "PlatformRuntime",
        packageId: "Microsoft.NETCore.App",
        version: "10.0.10",
        targetFramework: "net10.0"
      }
    ]);
});

test("dependency graph keys preserve complete coordinates and declared ranges", () => {
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("1.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")));
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net9.0")));
  assert.notEqual(
    dependencyGraphExternalKey("Example.Package", "[1.0.0]"),
    dependencyGraphExternalKey("Example.Package", "2.*"));
});

test("dependency graph node insertion is bounded", () => {
  const nodes = new Map();
  let truncated = false;
  for (let index = 0; index < 8000; index++) {
    const result = ensureBoundedGraphNode(
      nodes,
      `node-${index}`,
      () => ({ index }),
      80);
    truncated ||= result.truncated;
  }

  assert.equal(nodes.size, 80);
  assert.equal(truncated, true);
  assert.equal(
    ensureBoundedGraphNode(nodes, "node-1", () => null, 80).node,
    nodes.get("node-1"));
});

const appSource = readFileSync(new URL("../src/app.js", import.meta.url), "utf8");
const stylesSource = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
const engineSource = readFileSync(
  new URL("../engine/wwwroot/engine.js", import.meta.url),
  "utf8");
const deploySource = readFileSync(
  new URL("../../../.github/workflows/deploy-inspect-web.yml", import.meta.url),
  "utf8");

test("dependency graph render identity includes truncation and navigation", () => {
  const graph = {
    definition: "flowchart TD\n  d0[Example]",
    nodeInfoById: new Map([[
      "d0",
      {
        kind: "open",
        packageKey: "Example.Package|1.0.0|net8.0",
        id: "Example.Package",
        versionRange: ""
      }
    ]]),
    truncated: false,
    nodeLimit: 80
  };
  const signature = dependencyGraphRenderSignature(graph);

  assert.notEqual(
    signature,
    dependencyGraphRenderSignature({ ...graph, truncated: true }));
  assert.notEqual(
    signature,
    dependencyGraphRenderSignature({
      ...graph,
      nodeInfoById: new Map([[
        "d0",
        {
          ...graph.nodeInfoById.get("d0"),
          packageKey: "Example.Package|2.0.0|net8.0"
        }
      ]])
    }));
});

test("ready status shows versioned linked build provenance", () => {
  assert.match(appSource, /state\.buildIdentity = inspectBuildIdentity\(\)/);
  assert.match(
    appSource,
    /class="statusbar"[\s\S]{0,200}\$\{buildIdentityHtml\(\)\}/);
  assert.match(
    appSource,
    /class="home-foot"[\s\S]{0,500}\$\{buildIdentityHtml\(\)\}/);
  assert.match(
    appSource,
    /identity\.commitUrl[\s\S]*target="_blank" rel="noopener noreferrer"/);
  assert.match(appSource, /built \$\{escapeHtml\(builtAt\)\} UTC/);
  assert.match(
    deploySource,
    /-getProperty:VersionPrefix[\s\S]*-p:VersionPrefix="\$version"[\s\S]*-p:SourceRevisionId="\$GITHUB_SHA"[\s\S]*-p:BuildTimestampUtc="\$built_at"/);
});

test("bare home renders before wasm engine download", () => {
  assert.doesNotMatch(appSource, /from "\/engine\.js"/);
  assert.match(
    appSource,
    /async function loadEngineModule\(\)[\s\S]*await import\("\/engine\.js"\)/);
  assert.match(
    appSource,
    /state\.loading = !state\.home;[\s\S]*render\(\);[\s\S]*await loadEngineModule\(\)/);
  assert.match(
    appSource,
    /class="home-search \$\{enginePending[\s\S]*class="home-engine-status"/);
  assert.match(
    appSource,
    /state\.engineReady[\s\S]*browser wasm ready[\s\S]*browser wasm loading/);
});

test("settings keep a viewport-bounded scroll region", () => {
  assert.match(
    stylesSource,
    /\.settings-page\s*\{[^}]*height: 100vh;[^}]*grid-template-rows: auto minmax\(0, 1fr\);/s);
  assert.match(
    stylesSource,
    /\.settings-main\s*\{[^}]*min-height: 0;[^}]*overflow-y: auto;/s);
});

test("all dependency navigation paths use one product-owned coordinate matcher", () => {
  assert.equal(
    [...appSource.matchAll(/uniqueCompatiblePackage\(/g)].length,
    5);
  assert.match(
    engineSource,
    /MatchPackageDependencyCoordinate[\s\S]*JSON\.stringify\(candidates\)/);
  assert.doesNotMatch(engineSource, /PackageVersionSatisfiesDependencyRange/);
  assert.doesNotMatch(appSource, /dependencyVersionSatisfies/);
});

test("empty dependency graph invalidates an in-flight render", () => {
  const sequence = createDependencyGraphRenderSequence();
  const stale = sequence.begin();

  sequence.invalidate();

  assert.equal(sequence.isCurrent(stale), false);
  assert.match(
    appSource,
    /if \(!built\) \{\s*depGraphRenderSequence\.invalidate\(\);/);
});

test("stale dependency graph cleanup preserves a replacement with the same signature", () => {
  const sequence = createDependencyGraphRenderSequence();
  const dataset = {};
  const pending = createDependencyGraphPendingState(dataset);
  const signature = "same graph";

  const stale = sequence.begin();
  pending.begin(signature, stale);
  sequence.invalidate();
  pending.invalidate();
  const replacement = sequence.begin();
  pending.begin(signature, replacement);

  assert.equal(pending.complete(signature, stale), false);
  assert.equal(pending.isPending(signature), true);
  assert.equal(pending.complete(signature, replacement), true);
  assert.equal(pending.isPending(signature), false);
});

test("dependency graph binds navigation to generated node identities", () => {
  assert.match(
    appSource,
    /const nodeInfoById = new Map\(\s+keys\.map\(key => \[idOf\.get\(key\), nodeInfo\.get\(key\)\]\)\)/);
  assert.match(
    appSource,
    /built\.nodeInfoById\.get\(dataId \|\| idMatch\?\.\[1\]\)/);
  assert.doesNotMatch(appSource, /nodeInfoByLabel/);
  assert.match(
    appSource,
    /Dependency graph truncated at \$\{built\.nodeLimit\} nodes/);
  assert.match(
    appSource,
    /const signature = dependencyGraphRenderSignature\(built\)/);
});

test("dependency navigation reserves identity and surfaces resolution failures", () => {
  assert.match(
    appSource,
    /const navigationSeq = \+\+state\.navigationSeq;\s+state\.loading = true;[\s\S]*?await resolveDependencyVersion/);
  assert.match(
    appSource,
    /if \(navigationSeq !== state\.navigationSeq\) return;\s+state\.loading = false;\s+appendQueryNotice/);
  assert.match(
    appSource,
    /packageIdentityKey\(uniqueCompatiblePackage\(\s+state\.packages,\s+dependency\.id,\s+dependency\.versionRange\)\) === target\.packageKey/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\(\s+packageId,\s+declaredRange,\s+dependencyCoordinateCandidates\(packages\)\)/);
  assert.doesNotMatch(appSource, /dependencyVersionSatisfies/);
});

test("spotlight candidate identity includes version and framework", () => {
  const net8 = packageAt("1.0.0", "net8.0");
  const net9 = packageAt("1.0.0", "net9.0");
  const v2 = packageAt("2.0.0", "net8.0");

  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(net9, "Example.Type"));
  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(v2, "Example.Type"));
});

test("spotlight cache signature changes when a coordinate is replaced", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);
  const newFramework = packageAt("1.0.0", "net9.0", 4);

  const oldSignature = spotlightCandidateSignature(oldPackage, [oldPackage]);
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newVersion, [newVersion]));
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newFramework, [newFramework]));
});

test("member cache signatures use the same complete coordinates", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);

  assert.notEqual(
    spotlightCandidateSignature(oldPackage, [oldPackage]),
    spotlightCandidateSignature(newVersion, [newVersion]));
});

test("member source request identity includes decompiler taste", () => {
  const request = ["Example.Package", "1.0.0", "net8.0", "Example.dll", "Example.Widget", "M:Run"];

  assert.notEqual(
    memberRequestKey(request),
    memberRequestKey(request, ["prefer-var"]));
  assert.notEqual(
    memberRequestKey(request, ["prefer-var"]),
    memberRequestKey(request, ["prefer-explicit-types"]));
});

test("history never applies a selection to another coordinate", () => {
  const oldPackage = packageAt("1.0.0", "net8.0");
  const newVersion = packageAt("2.0.0", "net8.0");
  const view = {
    package: oldPackage.id,
    packageKey: packageIdentityKey(oldPackage)
  };

  assert.equal(packageForView([newVersion], view), null);
  assert.equal(packageForView([oldPackage, newVersion], view), oldPackage);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version,
    framework: oldPackage.activeFramework
  }), true);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version,
    framework: "net9.0"
  }), false);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version
  }), false);
});

test("history restores the complete saved workspace coordinate set", () => {
  const first = packageAt("1.0.0", "net8.0");
  const second = packageAt("2.0.0", "net9.0");
  const tabs = [
    { id: first.id, version: first.version, framework: first.activeFramework },
    { id: second.id, version: second.version, framework: second.activeFramework }
  ];

  assert.equal(workspaceCoordinatesMatch([first, second], tabs), true);
  assert.equal(workspaceCoordinatesMatch([first], tabs), false);
  assert.equal(workspaceCoordinatesMatch([second, first], tabs), false);
});

test("call graph navigation prefers exact metadata type identity", () => {
  assert.equal(
    callGraphTargetTypeId({
      typeFullName: "Example.Outer.Inner",
      typeMetadataId: "Example.Outer`1+Inner`1"
    }),
    "Example.Outer`1+Inner`1");
  assert.equal(
    callGraphTargetTypeId({ typeFullName: "Example.Legacy" }),
    "");
});

test("call graph navigation resolves accessor body selectors without a token", () => {
  const groups = [{
    overloads: [{
      graphSelectorKey: "property-selector",
      bodySelectors: [{
        token: 123,
        memberName: "get_P",
        selectorKey: "getter-selector"
      }]
    }]
  }];

  assert.deepEqual(
    graphMemberSelection(groups, {
      metadataToken: null,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
});

test("call graph navigation rejects ambiguous loaded package coordinates", () => {
  const target = {
    assembly: "Example",
    typeMetadataId: "Example.Widget",
    kind: "external"
  };
  const first = {
    ...packageAt("1.0.0", "net8.0"),
    types: [{ assembly: "Example", metadataId: "Example.Widget" }]
  };
  const second = {
    ...packageAt("2.0.0", "net9.0"),
    types: [{ assembly: "Example", metadataId: "Example.Widget" }]
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([first], target), {
    status: "unique",
    pkg: first,
    type: first.types[0]
  });
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([first, second], target),
    { status: "ambiguous" });
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([], target),
    { status: "missing" });
  assert.equal(
    graphTargetNavigationDisposition({ status: "ambiguous" }, target),
    "blocked");
  assert.equal(
    graphTargetNavigationDisposition({ status: "missing" }, target),
    "platform");
  assert.equal(
    graphTargetNavigationDisposition(
      { status: "missing" },
      { ...target, assemblyVersion: null }),
    "none");
});

test("call graph navigation rejects assembly identity skew", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: "neutral",
    assemblyPublicKeyToken: "0011223344556677"
  };
  const exact = {
    name: "Example",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: "0011223344556677"
  };

  assert.equal(callGraphAssemblyIdentityMatches(target, exact), true);
  assert.equal(
    callGraphAssemblyIdentityMatches(target, { ...exact, version: "2.0.0.0" }),
    false);
  assert.equal(
    callGraphAssemblyIdentityMatches(
      { ...target, assemblyVersion: null },
      exact),
    false);
  assert.equal(
    callGraphAssemblyIdentityMatches(
      { assembly: "Example", typeMetadataId: "Example.Widget" },
      exact),
    true);
});

test("call graph navigation joins asset names through metadata identity", () => {
  const type = {
    assembly: "Physical.dll",
    assemblyId: "asset:physical",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    types: [type],
    assemblies: [{
      id: "asset:physical",
      name: "Logical",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }]
  };
  const target = {
    assembly: "Logical",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget"
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
    status: "unique",
    pkg,
    type
  });
});

test("package overview joins member counts by exact asset identity", () => {
  const descriptors = [
    { id: "asset:a", name: "Logical", publicMembers: 3 },
    { id: "asset:b", name: "Logical", publicMembers: 7 }
  ];

  assert.equal(
    assemblyDescriptorForType(descriptors, {
      assembly: "Physical.dll",
      assemblyId: "asset:b"
    }),
    descriptors[1]);
  assert.equal(
    assemblyDescriptorForType(descriptors, {
      assembly: "Physical.dll",
      assemblyId: "asset:missing"
    }),
    null);
});

test("call graph navigation joins duplicate metadata names by asset identity", () => {
  const firstType = {
    assembly: "A.dll",
    assemblyId: "asset:a",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const secondType = {
    assembly: "B.dll",
    assemblyId: "asset:b",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    types: [firstType, secondType],
    assemblies: [
      { id: "asset:a", name: "Logical", version: "1.0.0.0" },
      { id: "asset:b", name: "Logical", version: "2.0.0.0" }
    ]
  };
  const target = {
    assembly: "Logical",
    assemblyVersion: "2.0.0.0",
    typeMetadataId: "Example.Widget"
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
    status: "unique",
    pkg,
    type: secondType
  });
});

test("relationship navigation rejects ambiguous dotted identities", () => {
  const first = { id: "A:N.T", queryId: "N.T" };
  const second = { id: "B:N.T", queryId: "N.T" };

  assert.equal(uniqueTypeByQueryId([first], "N.T"), first);
  assert.equal(uniqueTypeByQueryId([first, second], "N.T"), null);
});

test("incomplete call graphs produce a visible diagnostic", () => {
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 2,
    incompleteEdges: 1,
    bindingIdentityConflicts: 3
  }), "Partial call graph: 2 incomplete nodes, 1 incomplete edge, and 3 binding identity conflicts.");
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true,
    hasAnalysisFailureBoundary: true
  }), "Partial call graph: 0 incomplete nodes, 0 incomplete edges, and 0 binding identity conflicts. Boundaries: unexplored traversal and analysis failure.");
  assert.equal(callGraphDiagnosticsMessage({ isIncomplete: false }), "");
});

test("parameter titles preserve generic identities and contain metadata text", () => {
  assert.equal(
    parameterTitleHtml([
      { type: "System.Collections.Generic.Dictionary<System.String, Example.Widget>" },
      { type: '<img src=x onerror="alert(1)">' }
    ]),
    "(System.Collections.Generic.Dictionary&lt;System.String, Example.Widget&gt;, &lt;img src=x onerror=&quot;alert(1)&quot;&gt;)");
});

test("package Markdown has no styling or resource-loading authority", () => {
  for (const tag of ["style", "img", "iframe", "video", "audio", "source", "link", "svg"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_TAGS.includes(tag), false);
  }
  for (const attribute of ["style", "src", "srcset", "href", "poster", "class", "id"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_ATTR.includes(attribute), false);
  }
});

test("shared workspaces are bounded before package loading", () => {
  const tuples = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => [`Package.${index}`, "1.0.0", "net10.0"]);
  assert.equal(normalizeShareTabs(tuples).error, "");
  assert.match(
    normalizeShareTabs([...tuples, ["Package.Overflow", "1.0.0", "net10.0"]]).error,
    /12-package limit/);
  for (const malformed of [
    [null],
    [[]],
    [[""]],
    [[{}, "1.0.0", "net10.0"]],
    [["Package", "1.0.0", "net10.0", "unexpected"]]
  ]) {
    assert.match(normalizeShareTabs(malformed).error, /invalid/);
  }
  assert.equal(shareStateLengthError("x".repeat(MAX_SHARE_STATE_CHARACTERS)), "");
  assert.match(
    shareStateLengthError("x".repeat(MAX_SHARE_STATE_CHARACTERS + 1)),
    /65536-character limit/);
});

test("workspace package models retain the active and newest coordinates within the limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  const incoming = packageAt("13.0.0", "net10.0");

  const retained = retainWorkspacePackage(packages, active, incoming);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), true);
  assert.equal(retained.packages.includes(incoming), true);
  assert.deepEqual(retained.evicted, [packages[1]]);
});

test("workspace package replacement reuses its slot at the package limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  const replacement = packageAt("99.0.0", "net10.0");

  const retained = retainWorkspacePackage(
    packages,
    active,
    replacement,
    active);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), false);
  assert.equal(retained.packages.includes(replacement), true);
  assert.deepEqual(retained.evicted, [active]);
});

test("closing a package removes its coordinate and selects the adjacent tab", () => {
  const first = packageAt("1.0.0", "net8.0");
  const active = packageAt("2.0.0", "net9.0");
  const last = packageAt("3.0.0", "net10.0");

  const removed = removeWorkspacePackage(
    [first, active, last],
    active,
    packageIdentityKey(active));

  assert.deepEqual(removed.packages, [first, last]);
  assert.equal(removed.active, last);
  assert.equal(removed.closed, active);

  const only = removeWorkspacePackage(
    [active],
    active,
    packageIdentityKey(active));
  assert.deepEqual(only.packages, []);
  assert.equal(only.active, null);
});

test("workspace UI routes replacements and restore notices through bounded paths", () => {
  assert.match(
    appSource,
    /switchPackageFramework\(button\.dataset\.frameworkChip\)/);
  assert.match(appSource, /switchPackageFramework\(argument\)/);
  assert.doesNotMatch(
    appSource,
    /loadPackage\(state\.package\.id, state\.package\.version, (?:button\.dataset\.frameworkChip|argument)\)/);
  assert.match(
    appSource,
    /deepLink: deep,\s+navigationSeq,\s+queryNotice: state\.queryNotice/);
  assert.match(
    appSource,
    /clearWorkspacePackages\(\);\s+render\(\);/);
  assert.match(
    appSource,
    /if \(loc\.tabs\?\.length && !workspaceCoordinatesMatch\(state\.packages, loc\.tabs\)\) \{\s+restoreWorkspaceFromLocation/);
  assert.match(
    appSource,
    /for \(const packageModel of discarded\)\s+releasePackageModelCaches\(packageModel\);/);
  assert.match(
    appSource,
    /type: type\.queryId \?\? type\.id,\s+typeIdentity: type\.definitionId \?\? type\.id/);
  assert.match(
    appSource,
    /if \(!pkg && tabs\.length\) \{\s+const target = tabs\[/);
  assert.match(
    appSource,
    /state\.error = "";\s+state\.errorTitle = "";\s+state\.errorDetail = "";\s+state\.retryAction = null;\s+state\.home = true;/);
  assert.match(
    appSource,
    /\(\) => \(state\.retryAction \?\? bootstrap\)\(\)/);
  assert.match(
    appSource,
    /state\.retryAction = openRuntimePackFromHome/);
  assert.match(
    appSource,
    /state\.retryAction = options\.retryAction/);
  assert.match(
    appSource,
    /state\.retryAction = runCallGraphDemo/);
  assert.match(
    appSource,
    /appendQueryNotice\(\s+friendly\.message,\s+options\.retryAction/);
  assert.match(
    appSource,
    /data-package-close=/);
  assert.match(
    appSource,
    /closePackageTab\(button\.dataset\.packageClose\)/);
  assert.match(
    appSource,
    /const key = assemblyId \|\| `legacy:\$\{asm\}`/);
  assert.match(
    appSource,
    /assemblyDescriptorForType\(pkg\.assemblies, stat\)/);
  assert.match(
    appSource,
    /activatePackage\(targetPackage\)/);
});

test("member documentation state is scoped to the exact request", () => {
  assert.deepEqual(
    scopedRequestState("package-a\u0000member-a", "package-b\u0000member-b", false, "bad XML"),
    { loading: false, error: "" });
  assert.deepEqual(
    scopedRequestState("same", "same", true, ""),
    { loading: true, error: "" });
});

test("dependency selection exposes a missing exact framework", () => {
  assert.equal(
    dependencyGroupSelectionMessage({
      dependencyGroupError: "No exact dependency group."
    }),
    "No exact dependency group.");
  assert.equal(dependencyGroupSelectionMessage({}), "");
});

test("dependency group selection resets when package identity changes", () => {
  assert.match(
    appSource,
    /const changed = !packageIdentityEquals\(state\.package, pkg\);\s+state\.package = pkg;\s+if \(changed\)\s+state\.dependenciesGroupIndex = null;/);
});

test("missing exact dependency groups never create graph edges", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [{
      framework: "net9.0",
      isActive: false,
      dependencies: [{ id: "Wrong.Dependency", versionRange: "1.0.0" }]
    }]
  };

  assert.equal(selectedDependencyGroup(data), null);
});

test("dependency graph honors explicit selection after an exact group miss", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: false, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
});

test("dependency graph does not turn display fallback into explicit selection", () => {
  const missingExact = {
    dependencyGroupError: "No exact dependency group."
  };

  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, null, 0),
    null);
  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, 1, 0),
    1);
  assert.equal(
    dependencyGraphGroupSelectionIndex({}, null, 1),
    1);
  assert.match(
    appSource,
    /dependencyGraphGroupSelectionIndex\(\s*state\.packageDependencies,\s*state\.dependenciesGroupIndex,\s*resolveDependenciesGroupIndex\(groups\)\)/);
});

test("dependency graph uses each cached package's product-selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "any", isActive: false, dependencies: [] },
      { index: 1, framework: "any", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data),
    data.dependencyGroups[1]);
});

test("dependency graph uses the active package's explicitly selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
  assert.match(
    appSource,
    /selectedDependencyGroup\(\s*state\.packageDependencies,\s*selectedGroupIndex\)/);
});

test("Mermaid labels contain grammar-significant metadata", () => {
  const encoded = mermaidLabel("A\"B\n<x>&\\\u2028");

  assert.equal(
    encoded,
    "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028");
  for (const character of ['"', "\n", "<", ">", "\\", "\u2028"]) {
    assert.equal(encoded.includes(character), false);
  }
});
