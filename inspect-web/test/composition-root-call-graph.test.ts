import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import {
  accessibilityFilterIncludingType,
  assemblyDescriptorForType,
  callGraphAssemblyIdentityMatches,
  callGraphTargetTypeId,
  combinedGraphTargetNavigationDisposition,
  graphTargetBlockedReason,
  graphTargetNavigationDisposition,
  graphMemberDeepLinkDisposition,
  graphMemberPendingMatchesView,
  graphMemberSurfaceAssembly,
  graphMemberShareTarget,
  graphMemberSelection,
  graphMemberTargetWithSelectedBody,
  graphMemberTargetFromPacket,
  graphMemberTargetFromShare,
  graphOnlyBodyTarget,
  retainGraphOnlyBodyTarget,
  memberSectionIdsFor,
  partitionGraphMembers,
  reconcileCurrentNavigationEntry,
  replaceCurrentNavigationEntry,
  retainGraphMemberProjection,
  resolveLoadedGraphTargetCandidate,
  resolveOpportunitySourceCandidate,
  resolveOpportunitySourceType,
  resolvePlatformGraphTargetType,
  resolveRuntimeGraphTargetCandidate,
  runtimeGraphTargetAssemblyIsResident,
  runtimeGraphTargetNavigationDisposition,
  searchableMemberGroups,
  uniqueTypeByQueryId,
  uniqueWorkspaceTypeByQueryId,
} from "../src/data.ts";
import type {
  CallGraphTarget,
  GraphMemberShareIdentity,
  NavigationState,
} from "../src/data.ts";
import { platformCatalogFramework } from "../src/platform-index.ts";
import {
  platformAssemblyRequest,
  platformGraphLibraryForTarget,
} from "../src/platform-subject.ts";

import {
  packageAt,
  appSource,
  graphLegendsSource,
  appSyntax,
  functionDeclaration,
  onlyCallExpressionNamed,
  objectArgument,
  sourceText,
  callbackProperty,
  graphInteractionsSource,
  callGraphInspectionSource,
  stylesSource,
  generatedFacadeSource,
  browserGraphMemberSource,
} from "./composition-root-test-fixture.ts";
const engineCallGraphTarget = (
  fixture: CallGraphTarget & { typeFullName?: string },
): CallGraphTarget => fixture;

test("call graph navigation prefers exact metadata type identity", () => {
  assert.equal(
    callGraphTargetTypeId(engineCallGraphTarget({
      typeFullName: "Example.Outer.Inner",
      typeMetadataId: "Example.Outer`1+Inner`1"
    })),
    "Example.Outer`1+Inner`1");
  assert.equal(
    callGraphTargetTypeId(engineCallGraphTarget({ typeFullName: "Example.Legacy" })),
    "");
});

test("call graph navigation resolves accessor selectors across image-local token skew", () => {
  const group = {
    overloads: [{
      graphSelectorKey: "property-selector",
      bodySelectors: [{
        token: 123,
        memberName: "get_P",
        selectorKey: "getter-selector"
      }]
    }]
  };

  assert.deepEqual(
    graphMemberSelection([group], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
  assert.deepEqual(
    graphMemberSelection([group, group], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
  assert.equal(
    graphMemberSelection([
      group,
      {
        overloads: [{
          graphSelectorKey: "getter-selector",
          bodySelectors: [{
            token: 124,
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      }
    ], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    null);
  assert.equal(
    graphMemberSelection([
      {
        overloads: [{
          bodySelectors: [{
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      },
      {
        overloads: [{
          bodySelectors: [{
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      }
    ], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    null);

  const runtimeResolver =
    appSource.match(/function findRuntimeMemberSelection[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(runtimeResolver, /graphMemberSelection\(groups, node\)/);
  assert.doesNotMatch(runtimeResolver, /node\.metadataToken != null/);
  assert.match(runtimeResolver, /resolveRuntimeGraphTargetCandidate\(pack, node\)/);
});

test("graph-only member targets round-trip through shared URLs", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.2.3.4",
    assemblyCulture: null,
    assemblyPublicKeyToken: "0011223344556677",
    typeDefinitionId: "Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Run",
    selectorKey: "opaque-selector",
    metadataToken: 0x06000001
  };
  const encoded = graphMemberShareTarget(target);
  assert.ok(encoded, "the fixture target must encode to a share tuple");

  assert.deepEqual(graphMemberTargetFromShare(encoded), target);
  assert.equal(graphMemberShareTarget({
    ...target,
    typeDefinitionId: ""
  }), null);
  for (const metadataToken of [
    -1,
    0,
    0x02000001,
    0x06000000,
    0x07000000,
    0x106000001,
  ]) {
    assert.equal(
      graphMemberShareTarget({ ...target, metadataToken }),
      null);
    assert.equal(
      graphMemberTargetFromShare([
        ...encoded.slice(0, 8),
        metadataToken,
      ]),
      null);
  }
  assert.equal(graphMemberTargetFromShare([
    "Example",
    "1.2.3.4",
    null,
    "0011223344556677",
    "Example.Widget",
    "Example.Widget",
    "Run",
    "opaque-selector",
    "not-a-token"
  ]), null);
  assert.deepEqual(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      o: 0,
      g: encoded
    }),
    { target });
  assert.match(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      o: 0,
      g: [...encoded.slice(0, 8), "not-a-token"]
    }).error ?? "",
    /shared graph member target is invalid/);
  assert.match(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      g: encoded
    }).error ?? "",
    /shared graph member target is invalid/);
});

test("shared graph targets require explicit assembly version provenance", () => {
  const target = {
    assembly: "Example",
    typeDefinitionId: "Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Run",
    selectorKey: "opaque-selector",
    metadataToken: 0x06000001
  };
  assert.equal(graphMemberShareTarget(target), null);
  assert.equal(graphMemberTargetFromShare([
    target.assembly,
    "",
    null,
    null,
    target.typeDefinitionId,
    target.typeMetadataId,
    target.memberName,
    target.selectorKey,
    target.metadataToken
  ]), null);

  const explicitUnknown = {
    ...target,
    assemblyVersion: null
  };
  const explicitUnknownRoundTrip = graphMemberTargetFromShare(
    graphMemberShareTarget(explicitUnknown));
  assert.ok(explicitUnknownRoundTrip, "an explicit unknown version must round-trip");
  assert.equal(explicitUnknownRoundTrip.assemblyVersion, null);
});

test("graph-only members open through the typed member surface", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  const openMember =
    appSource.match(/function openMemberGroup\([\s\S]*?\n}(?=\n\nfunction enterMemberScope)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /navigateToGraphMember\([\s\S]*loaded,[\s\S]*target,[\s\S]*loadedSection,[\s\S]*failureSurface\)/);
  assert.doesNotMatch(binding, /openGraphSource\(/);
  assert.match(
    openMember,
    /const graphOnlyTarget =[\s\S]*clearMemberContentCache\(\);[\s\S]*state\.selectedBodyTarget = graphOnlyTarget;[\s\S]*retainMemberSectionIfSupported\(group\)/);
  assert.match(
    generatedFacadeSource("inspect-web-metadata"),
    /\$requireManagedExports\(\)\["DotnetInspect"\]\["Web"\]\["Interop"\]\["Metadata"\]\["MetadataExports"\]\["QueryGraphMemberSurface\.-?\d+"\]/);
  assert.match(
    generatedFacadeSource("inspect-web-metadata"),
    /export async function queryGraphMemberSurface\(packageId, version, targetFramework/);
  assert.match(graphLegendsSource, /solid border: no platform lookup/);
  assert.match(
    graphLegendsSource,
    /dashed border: platform lookup on click/);
});

test("graph-only deep links win over colliding public member groups", () => {
  const selectedType = { id: "Example.Widget" };
  const publicGroup = { key: "method:Run", overloads: [{ name: "Run" }] };
  const deepLinkGraphTarget = (
    selectorKey: string,
  ): GraphMemberShareIdentity => ({
    assembly: "Example.dll",
    typeDefinitionId: "Example.Widget",
    memberName: "Run",
    selectorKey,
    metadataToken: 0x06000001
  });
  assert.equal(
    graphMemberDeepLinkDisposition(
      {
        member: publicGroup.key,
        graphTarget: deepLinkGraphTarget("private-overload")
      },
      { status: "unique", type: selectedType },
      selectedType,
      publicGroup),
    "graph");
  assert.equal(
    graphMemberDeepLinkDisposition(
      {
        member: publicGroup.key,
        overload: "99",
        graphTarget: deepLinkGraphTarget("public-overload")
      },
      { status: "unique", type: selectedType },
      selectedType,
      publicGroup,
      { group: publicGroup, overloadIndex: 0 }),
    "local");

  const deepLink =
    appSource.match(/function applyDeepLink\([^)]*\) \{[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(
    deepLink,
    /graphMemberDeepLinkDisposition\(\s*deep,\s*graphCandidate,\s*type,\s*group,\s*localGraphSelection\)/);
  assert.match(deepLink, /else if \(disposition === "graph"/);
  assert.match(deepLink, /else if \(disposition === "public" && group && deep\.member\)/);
  assert.match(
    deepLink,
    /The shared graph member no longer matches this package and was not opened/);
});

test("pending graph-member restoration is bound to its exact view", () => {
  const pending = {
    packageKey: "Example\u00001.0.0\u0000net10.0",
    viewSignature: "{\"t\":\"Example.Widget\"}"
  };
  assert.equal(
    graphMemberPendingMatchesView(
      pending,
      pending.packageKey,
      pending.viewSignature),
    true);
  assert.equal(
    graphMemberPendingMatchesView(
      pending,
      pending.packageKey,
      "{\"t\":\"Example.Other\"}"),
    false);
  assert.match(
    appSource,
    /if \(state\.pendingGraphMemberDeepLink\s*&& !graphMemberPendingMatchesView\(/);
  assert.match(
    appSource,
    /The shared graph member's declaring type is no longer available/);
  assert.match(
    appSource,
    /else if \(disposition === "graph"[\s\S]*?state\.selectedMemberKey = deep\.member;[\s\S]*?state\.selectedBodyTarget = deep\.graphTarget;[\s\S]*?viewSignature: viewSignature\(\)/);
  assert.match(
    appSource,
    /function currentPendingGraphMember\(\) \{[\s\S]*?graphMemberPendingMatchesView\([\s\S]*?viewSignature\(\)/);
  assert.match(
    appSource,
    /function renderApiLens\([^)]*\) \{\s*const pending = currentPendingGraphMember\(\);[\s\S]*?return renderGraphMemberPendingHtml\(item, title\)/);
  assert.match(
    appSource,
    /async function restorePendingGraphMember\([\s\S]*type\.id !== pending\.type[\s\S]*declaring type is no longer available[\s\S]*loadGraphMemberSurface/);
});

test("stale graph-only navigation clears progress without surfacing its error", () => {
  const navigation =
    appSource.match(/async function navigateToGraphMemberProjection[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.match(navigation, /const navigationIsCurrent = \(\) =>/);
  assert.match(
    navigation,
    /const owner = captureViewOperation\(seq\);[\s\S]*?ownsViewOperation\(owner, state\.graphMemberNavigationSeq\)/);
  assert.equal(
    navigation.match(/if \(!navigationIsCurrent\(\)\)/g)?.length,
    2);
  assert.match(
    navigation,
    /if \(seq === state\.graphMemberNavigationSeq\) \{\s*state\.graphMemberNavigationTitle = "";\s*render\(\);/);
  assert.match(
    navigation,
    /showGraphMemberNavigationError\([\s\S]*errorMessage\(error\),[\s\S]*failureSurface\)/);
  assert.match(
    appSource,
    /const callGraphError = callGraphErrorForView\(state\);/);
  assert.match(
    appSource,
    /function popPlatformDrill\(\) \{\s*invalidateGraphMemberNavigation\(\);/);
});

test("shared package graph navigation retains portable accessor identity", () => {
  const shareState =
    appSource.match(/function captureWorkspaceUrlState\(\)[\s\S]*?\n}(?=\n\nasync function buildStateUrl)/)?.[0]
    ?? "";

  assert.match(
    shareState,
    /memberAnchor = overload\.anchorDigest \|\| null/);
  assert.match(
    shareState,
    /memberSignature = memberAnchor \? null : overload\.canonicalSignature \|\| null/);
  assert.doesNotMatch(shareState, /selectedBodyTarget:/);
  assert.match(graphLegendsSource, /solid border: no platform lookup/);
});

test("stale graph member loads cannot mutate the visible member surface", () => {
  const navigation =
    appSource.match(/async function navigateToGraphMemberProjection[\s\S]*?\n\}/)?.[0]
    ?? "";
  const restoration =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.ok(
    navigation.indexOf("if (!navigationIsCurrent())")
      < navigation.indexOf("commitGraphMemberSelection("));
  assert.ok(
    restoration.indexOf("if (!restorationIsCurrent())")
      < restoration.indexOf("commitGraphMemberSelection("));
});

test("platform graph navigation supersedes package member loading immediately", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.match(
    navigation,
    /invalidateGraphMemberNavigation\(\);\s*const seq = \+\+state\.memberCallGraphSeq;/);
  assert.match(
    navigation,
    /state\.platformDrillLoading = false;\s*state\.platformDrillError = "";/);
  assert.match(navigation, /state\.memberCallGraphExpanding = false;/);
});

test("projected members remain distinct from the public API surface", () => {
  const publicMember = {
    name: "M",
    graphOnly: false
  };
  const projectedMember = {
    name: "M",
    graphOnly: true
  };
  const { publicMembers, graphMembers } =
    partitionGraphMembers([publicMember, projectedMember]);
  const publicGroup = {
    key: "method:M",
    overloads: [publicMember]
  };
  const projectedGroup = {
    key: "graph:method:M",
    overloads: [projectedMember]
  };

  assert.deepEqual(publicMembers, [publicMember]);
  assert.deepEqual(graphMembers, [projectedMember]);
  assert.deepEqual(
    searchableMemberGroups([publicGroup, projectedGroup]),
    [publicGroup]);
  assert.match(
    appSource,
    /Graph-discovered implementation members/);
  assert.match(
    appSource,
    /partitionGraphMembers\(item\.api\)/);
  assert.match(
    appSource,
    /\$\{member\.graphOnly \? "graph:" : ""\}\$\{member\.kind\}:\$\{member\.name\}/);
  assert.match(
    appSource,
    /memberSectionIdsFor\(\s*member,\s*state\.package\?\.isRuntimePack,\s*memberHasSelectedBody\(member\)\)/);
  assert.match(
    appSource,
    /searchableMemberGroups\(memberGroups\(type\)\)/);
});

test("shared graph projection validates before committing API state", () => {
  const restoration =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?\n\}/)?.[0]
    ?? "";
  const validation = restoration.indexOf("staged.selection.group.key !== pending.member");
  const commit = restoration.indexOf("commitGraphMemberSelection(");

  assert.ok(validation >= 0);
  assert.ok(commit > validation);
  assert.doesNotMatch(restoration, /staged\.selection\.overloadIndex !== pendingOverloadIndex/);
  assert.match(
    appSource,
    /group\?\.overloads\.length === 1\s*\? graphOnlyBodyTarget\(group\.overloads\[0\]\)/);
  assert.doesNotMatch(
    appSource,
    /group\.overloads\[overloadIndex\]\.graphTarget = bodyTarget/);
});

test("selector-only accessors use body-aware implementation queries", () => {
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  // The absent rejection is claimed across the managed export assemblies that could carry
  // it, not just the host, now that call-graph and source operations have their own owners.
  for (const managedSource of [
    "../DotnetInspect.Web/InspectionEngine.cs",
    "../DotnetInspect.Web.Interop.CallGraph/CallGraphExports.cs",
    "../DotnetInspect.Web.Interop.Source/SourceExports.cs",
    "../DotnetInspect.Web.Interop.Source/AnnotatedSourceExports.cs",
  ]) {
    assert.doesNotMatch(
      readFileSync(new URL(managedSource, import.meta.url), "utf8"),
      /A call graph needs the selected overload's method-body token/);
  }
  assert.match(
    annotatedLoader,
    /member: state\.selectedBodyTarget\?\.memberName \?\? overload\.name/);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "event" }, false, true),
    ["overview", "call-graph", "facts", "annotated", "compare"]);
});

test("platform graph borders reflect actual resident lookup", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  const packageBinding = binding.slice(binding.indexOf("const packages ="));

  assert.match(binding, /resolveRuntimeGraphTargetCandidate\(pack, target\)/);
  assert.match(binding, /platform: disposition === "lookup"/);
  assert.match(binding, /if \(disposition === "member" && pack && resident\) \{[\s\S]*openRuntimeMemberFromGraph\(/);
  assert.match(binding, /else \{[\s\S]*startPlatformDrill\(target\)/);
  assert.match(
    packageBinding,
    /runtimePackForFramework\(\s*runtimePackPackage\(\),\s*platformCatalogFramework\(state\.package\?\.activeFramework \|\| ""\)\)/);
  assert.match(
    packageBinding,
    /const runtimeCandidate = \(candidate\.status === "missing"[\s\S]*?\|\| candidate\.status === "skew"\) && pack\s*\? resolveRuntimeGraphTargetCandidate\(pack, target\)/);
  assert.match(
    packageBinding,
    /const disposition = combinedGraphTargetNavigationDisposition\(\s*candidate,\s*runtimeCandidate,\s*target,\s*runtimeResident\);[\s\S]*?if \(disposition === "blocked"/);
  assert.match(
    packageBinding,
    /else if \(disposition === "resident"\) \{\s*if \(pack && resident\) \{[\s\S]*?openRuntimeMemberFromGraph\([\s\S]*?\} else \{[\s\S]*?startPlatformDrill\(target\)/);
  assert.match(
    appSource,
    /if \(candidate\.status === "resident"\s*\|\| \(candidate\.status === "missing"\s*&& assemblyResident\)\) \{[\s\S]*?await drillPlatformNode\(/);
});

test("runtime graph nodes separate member, drill, and lookup disposition", () => {
  const normalTarget = {
    kind: "normal",
    assembly: "System.Private.CoreLib",
    typeDefinitionId: "System.RuntimeType",
    memberName: "PrivateHelper",
    selectorKey: "private-helper"
  };
  const externalTarget = { ...normalTarget, kind: "external" };

  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      normalTarget,
      true),
    "member");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      normalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      normalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      externalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      externalTarget,
      false),
    "lookup");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "ambiguous" },
      externalTarget,
      false),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      externalTarget,
      false,
      true),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "resident" },
      externalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      { ...externalTarget, assemblyVersion: null },
      false),
    "none");

  const runtimeNavigation =
    appSource.match(/function navigateToRuntimeMember[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(
    runtimeNavigation,
    /state\.libraryScope = targetLibrary \? new Set\(\[targetLibrary\]\) : null/);
});

test("runtime graph identities restore through exact resident candidates", () => {
  const type = {
    id: "System.Console",
    definitionId: "System.Console",
    metadataId: "System.Console",
    assembly: "System.Console.dll",
    assemblyId: "runtime:System.Console"
  };
  const pack = {
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    types: [type],
    assemblies: [{
      id: "runtime:System.Console",
      name: "System.Console",
      version: "10.0.0.0",
      culture: null,
      publicKeyToken: "b03f5f7f11d50a3a"
    }]
  };
  const target = {
    kind: "external",
    assembly: "System.Console",
    assemblyVersion: "10.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "System.Console",
    memberName: "get_Out",
    selectorKey: "getter-selector",
    metadataToken: null
  };

  assert.deepEqual(resolveRuntimeGraphTargetCandidate(pack, target), {
    status: "unique",
    pkg: pack,
    type
  });

  assert.equal(runtimeGraphTargetAssemblyIsResident(pack, target), true);
  assert.equal(
    runtimeGraphTargetAssemblyIsResident({ ...pack, types: [] }, target),
    true);
  assert.equal(
    runtimeGraphTargetAssemblyIsResident(
      pack,
      { ...target, assemblyVersion: "10.0.0.1" }),
    false);
  assert.equal(
    graphTargetNavigationDisposition({ status: "missing" }, target, true),
    "resident");
  assert.doesNotMatch(
    appSource,
    /if \(!state\.package\?\.isRuntimePack\s*&& packet\.y/);
  assert.match(
    appSource,
    /resolveRuntimeGraphTargetCandidate\(\s*pkg,\s*deep\.graphTarget\)/);
});

test("runtime graph member activation requires the matching exact catalog", async () => {
  const navigation =
    appSource.match(/async function openRuntimeMemberFromGraph[\s\S]*?\n\}/)?.[0]
    ?? "";
  const catalog =
    appSource.match(/async function exactPlatformCatalogForType[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(
    navigation,
    /await exactPlatformCatalogForType\(pack, type\)/);
  assert.match(
    catalog,
    /await ensurePlatformCatalog\(\s*pack\.activeFramework,\s*pack\.version\)/);
  assert.match(
    catalog,
    /target\.rows\.some\(row =>\s*row\.hasImplementation\s*&& platformLibraryMatchesDescriptor\(row, library\)\)/);
  assert.match(
    navigation,
    /retainPlatformPackageForTarget\(target\) !== pack[\s\S]*matching Platform runtime model is unavailable/);
  assert.match(
    navigation,
    /const alreadyRetained = state\.packages\.includes\(pack\);[\s\S]*if \(!alreadyRetained\) renewNavigationOwnership\(\);[\s\S]*if \(!navigationIsCurrent\(\)\) return;[\s\S]*navigateToRuntimeMember\(/);
  assert.match(
    navigation,
    /catch \(error\) \{[\s\S]*showPlatformTargetError\([\s\S]*exact Platform target is unavailable[\s\S]*return;[\s\S]*navigateToRuntimeMember\(/);
  assert.match(
    navigation,
    /if \(pack\.source\.kind === "platform" && state\.packages\.includes\(pack\)\) \{\s*state\.packages = state\.packages\.filter\(candidate => candidate !== pack\);\s*invalidateWorkspaceMembershipViews\(\);/);
  assert.match(
    appSource,
    /async function spotlightPlatformTypeIsAvailable[\s\S]*exactPlatformCatalogForType\(pkg, type\)[\s\S]*showToast\([\s\S]*exact Platform target is unavailable/);
  assert.match(
    appSource,
    /async function pickSpotlightMember[\s\S]*spotlightPlatformTypeIsAvailable\([\s\S]*activatePackage\(pkg\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*spotlightPlatformTypeIsAvailable\([\s\S]*activatePackage\(pkg\)/);

  const pack = { source: { kind: "platform" } };
  const target = {};
  const events: string[] = [];
  let current = true;
  const context = {
    exactPlatformCatalogForType: async () => target,
    navigationIsCurrent: () => current,
    renewNavigationOwnership: () => {
      current = true;
      events.push("renew");
    },
    retainPlatformPackageForTarget: () => {
      current = false;
      events.push("retain");
      return pack;
    },
    state: { packages: [] as (typeof pack)[] },
    navigateToRuntimeMember: () => events.push("navigate"),
    showPlatformTargetError: async () => {},
    errorMessage: (error: unknown) => String(error),
    pack,
  };
  await runInNewContext(
    stripTypeScriptTypes(`(async () => {
      ${navigation}
      await openRuntimeMemberFromGraph(
        pack, {}, {}, 0, {}, "overview",
        navigationIsCurrent, renewNavigationOwnership, "call-graph");
    })()`),
    context);
  assert.deepEqual(events, ["retain", "renew", "navigate"]);
});

test("home navigation invalidates pending graph work", () => {
  const home =
    appSource.match(/function goHome\(\) \{[\s\S]*?\n\}/)?.[0]
    ?? "";
  const history =
    appSource.match(/window\.addEventListener\("popstate"[\s\S]*?\n\}\);/)?.[0]
    ?? "";

  assert.match(home, /invalidateGraphMemberNavigation\(\)/);
  assert.match(home, /state\.memberCallGraphExpanding = false/);
  assert.match(history, /invalidateMemberDestinationWork\(state\)/);
});

test("graph navigation restores scope and supersedes local drills", () => {
  const capture =
    appSource.match(/function captureView[\s\S]*?(?=\nfunction recordNav)/)?.[0]
    ?? "";
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const startDrill =
    appSource.match(/async function startPlatformDrill[\s\S]*?\n\}/)?.[0]
    ?? "";
  const navigation =
    appSource.match(/function navigateToMember[\s\S]*?(?=\nasync function loadSelectedMemberFacts)/)?.[0]
    ?? "";

  assert.match(capture, /libraryScope: captureLibraryScope\(state\.libraryScope\)/);
  assert.match(
    apply,
    /state\.libraryScope = restoreLibraryScope\(\s*view\.libraryScope,\s*pkg\.assemblies\.map\(assembly => assembly\.id\)\)/);
  assert.match(
    callGraphInspectionSource,
    /state\.memberCallGraphSeq\+\+;\s*state\.memberCallGraphExpanding = false;\s*state\.platformDrillLoading = false;/);
  assert.match(
    startDrill,
    /invalidateGraphMemberNavigation\(\);\s*const owner = captureViewOperation\(\+\+state\.memberCallGraphSeq\);[\s\S]*?const navigationIsCurrent = \(\) =>\s*ownsViewOperation\(owner, state\.memberCallGraphSeq\);[\s\S]*?await drillPlatformNode\(node, navigationIsCurrent\)/);
  assert.match(
    appSource,
    /else if \(disposition === "resident"\)[\s\S]*?startPlatformDrill\(target\)/);
  assert.match(
    navigation,
    /navigationPreservesAggregateLibraryScope\(pkg\)[\s\S]*state\.typeFilter = "";\s*state\.namespaceFilter = "";\s*state\.kindFilter = "";/);
  assert.match(
    navigation,
    /enterTypeSubject\(type, \{ preserveAggregate \}\)[\s\S]*enterMemberScope\(\{ preserveAggregate \}\)/);
  assert.match(
    navigation,
    /state\.accessibilityFilter = accessibilityFilterIncludingType\(\s*state\.accessibilityFilter,\s*type\)/);
});

test("restored selections reveal their accessibility bucket", () => {
  const original = new Set(["public"]);
  const revealed = accessibilityFilterIncludingType(
    original,
    { accessibilityId: "private" });
  assert.deepEqual([...original], ["public"]);
  assert.deepEqual([...revealed], ["public", "private"]);

  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const deepLink =
    appSource.match(/function applyDeepLink\([^)]*\) \{[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const reveal =
    appSource.match(/function revealTypeInFilters[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    apply,
    /const type = pkg\.types\.find[\s\S]*?if \(!state\.atPackageRoot && !state\.atLibraryRoot\) revealTypeInFilters\(type\)/);
  assert.match(
    deepLink,
    /const type = pkg\.types\.find[\s\S]*?revealTypeInFilters\(type\)[\s\S]*?state\.typeCursor = Math\.max/);
  assert.match(
    reveal,
    /typeMatchesFilterText[\s\S]*?state\.typeFilter = ""[\s\S]*?state\.namespaceFilter = ""[\s\S]*?state\.kindFilter = ""[\s\S]*?state\.libraryScope = new Set\(\[libraryKey\(type\)\]\)/);
  assert.match(
    appSource,
    /function navigateToType\([\s\S]*?enterTypeSubject\(target, options\)[\s\S]*?state\.typeCursor = filteredTypes\(\)\.findIndex/);
});

test("runtime lookup refuses ambiguous or unresolved exact targets", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    navigation,
    /candidate = resolveRuntimeGraphTargetCandidate\(pack, node\)[\s\S]*?candidate\.status === "ambiguous"[\s\S]*?showPlatformTargetError/);
  assert.match(
    navigation,
    /candidate\.status !== "unique"[\s\S]*?loaded platform assembly does not contain the exact target identity/);
  assert.match(
    navigation,
    /assemblyResident = runtimeGraphTargetAssemblyIsResident\(pack, node\)[\s\S]*?candidate\.status === "missing" && assemblyResident[\s\S]*?drillPlatformNode\(node, navigationIsCurrent\)/);
  assert.match(
    navigation,
    /let owner = captureViewOperation\(seq\);\s*const navigationIsCurrent = \(\) =>\s*ownsViewOperation\(owner, state\.memberCallGraphSeq\)/);
  assert.match(
    navigation,
    /const discardIfStale = \(\s*preservedFocus: MemberFocusSnapshot \| null = null,\s*\) => \{[\s\S]*?seq === state\.memberCallGraphSeq[\s\S]*?state\.platformDrillLoading = false;[\s\S]*?if \(preservedFocus\) renderPreservingMemberFocus\(preservedFocus\);\s*else render\(\)/);
  assert.match(
    appSource,
    /async function drillPlatformNode\(\s*node: InspectedCallGraphTarget,\s*navigationIsCurrent: \(\) => boolean = \(\) => true,\s*\)[\s\S]*?isCurrent: navigationIsCurrent/);
  assert.match(
    callGraphInspectionSource,
    /async drill\(request\)[\s\S]*?const ownsRequest = \(\) =>\s*sequence === state\.memberCallGraphSeq && request\.isCurrent\(\)[\s\S]*?const abandonStaleRequest = \(\) => \{[\s\S]*?if \(sequence !== state\.memberCallGraphSeq\) return;[\s\S]*?state\.platformDrillLoading = false;[\s\S]*?dependencies\.renderPreservingMemberFocus\(\);[\s\S]*?if \(!ownsRequest\(\)\) \{\s*abandonStaleRequest\(\);\s*return;/);
  assert.match(
    appSource,
    /if \(disposition === "blocked"\) \{[\s\S]*?blockedCallGraphNodeBinding\([\s\S]*?if \(disposition === "none"\) return null/);
});

test("history rebuilds graph-only members through exact pending identity", () => {
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const restore =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    apply,
    /graphSelection\?\.group\.key !== view\.selectedMemberKey[\s\S]*?state\.pendingGraphMemberDeepLink = \{[\s\S]*?packageKey: packageIdentityKey\(pkg\)[\s\S]*?member: view\.selectedMemberKey[\s\S]*?target: historyGraphTarget[\s\S]*?restorePendingGraphMember\(\)/);
  assert.match(
    restore,
    /state\.graphMemberNavigationTitle =[\s\S]*?render\(\);[\s\S]*?loadGraphMemberSurface/);
  assert.match(
    restore,
    /const owner = captureViewOperation\(seq\);[\s\S]*?ownsViewOperation\(owner, state\.graphMemberNavigationSeq\)/);
  assert.equal(
    restore.match(/normalizeCurrentNavEntry\(\);/g)?.length,
    2);
  assert.match(
    apply,
    /const requestedOverloadIndex = view\.selectedOverloadIndex;[\s\S]*?overload: requestedOverloadIndex/);
  assert.match(
    apply,
    /const hasSelectedBody =\s*graphSelection\?\.group\.key === view\.selectedMemberKey;[\s\S]*?memberSectionIdsFor\(member, pkg\.isRuntimePack, hasSelectedBody\)/);
  assert.match(
    apply,
    /state\.selectedBodyTarget = retainGraphOnlyImplementationBody\(\s*graphSelection\.group\.overloads\[graphSelection\.overloadIndex\],\s*view\.bodyTarget\)/);
  assert.match(
    apply,
    /memberSectionIdsFor\(\s*graphSelection\.group,\s*pkg\.isRuntimePack,\s*true\)\.includes\(view\.memberSection\)/);
  assert.match(
    appSource,
    /const hasSelectedBody = bodyTargetMatchesOverload\([\s\S]*?memberSectionIdsFor\(\s*group,\s*state\.package\?\.isRuntimePack,\s*hasSelectedBody\)/);
  assert.match(
    appSource,
    /function renderMember\(type: AppTypeSurface, member: AppMemberGroup\) \{[\s\S]*?const selectedOverloadIndex = state\.selectedOverloadIndex;[\s\S]*?const hasSelectedOverload =[\s\S]*?selectedOverloadIndex < member\.overloads\.length[\s\S]*?const overloadIndex = hasSelectedOverload \? selectedOverloadIndex \?\? 0 : 0;/);
});

test("member navigation excludes graph-only projections from ordinary filters", () => {
  const filters =
    appSource.match(/function visibleMemberGroups\([\s\S]*?\n}\n\nfunction renderMemberFilterControls\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    filters,
    /filterMemberGroups\(publicMemberGroups\(type\), memberFilterState\(\)\)/);
  assert.match(
    filters,
    /function publicMemberGroups\([\s\S]*?searchableMemberGroups\(memberGroups\(type\)\)/);
  assert.match(
    filters,
    /publicMemberGroups\(type\)\s*\.flatMap\(group => group\.overloads\)/);

  const entries =
    appSource.match(/function memberNavEntries\([\s\S]*?\n}\n\nfunction memberNavCursor/)?.[0]
    ?? "";
  assert.match(
    entries,
    /for \(const group of visibleMemberGroups\(type\)\)[\s\S]*?const graphGroup = selectedGraphMemberGroup\(type\);[\s\S]*?entries\.push\(\{ kind: "member", group: graphGroup }\)/);

  const pane =
    appSource.match(/function renderMemberNavPane\([\s\S]*?\n}\n\nfunction renderScopeBar/)?.[0]
    ?? "";
  assert.match(pane, /memberCount: publicMemberGroups\(type\)\.length/);
});

test("type API reports the filtered member count once in its header", () => {
  const renderApi =
    appSource.match(/function renderApiLens\([\s\S]*?\n}\n\nfunction renderMember/)?.[0]
    ?? "";
  assert.match(
    renderApi,
    /<h1 id="api-surface-title">Members<\/h1>/);
  assert.match(
    renderApi,
    /<p>\$\{visibleGroups\.length} of \$\{publicGroups\.length} member groups/);
  assert.doesNotMatch(renderApi, /member-filter-result/);
  assert.doesNotMatch(renderApi, /member groups visible/);
});

test("member API uses full-area overload and selected-member surfaces", () => {
  const renderApi =
    appSource.match(/function renderApiLens\([\s\S]*?\n}\n\nfunction renderMember/)?.[0]
    ?? "";
  const renderMember =
    appSource.match(/function renderMember\([\s\S]*?\n}\n\n\/\/ The annotated section/)?.[0]
    ?? "";
  const memberOverview =
    renderMember.match(/if \(state\.memberSection === "overview"\) \{[\s\S]*?\n  \} else if \(state\.memberSection === "call-graph"\)/)?.[0]
    ?? "";
  const emptyMember =
    renderApi.match(/if \(state\.memberBrowseTypeId === item\.id\) \{[\s\S]*?\n  \}/)?.[0]
    ?? "";
  assert.match(
    emptyMember,
    /class="member-surface member-empty-surface"[\s\S]*?<h1 id="member-surface-title">Members<\/h1>[\s\S]*?No member selected/);
  assert.doesNotMatch(emptyMember, /typeHeadingHtml/);
  assert.match(
    renderMember,
    /class="member-surface member-overload-surface"[\s\S]*?<h1 id="member-surface-title">\$\{escapeHtml\(member\.name\)}<\/h1>[\s\S]*?\$\{member\.overloads\.length} overloads/);
  assert.match(
    renderMember,
    /class="member-surface-scroll"[\s\S]*?class="api-list api-surface-list member-surface-list"/);
  assert.match(
    renderMember,
    /class="api-surface-footer member-surface-footer"[\s\S]*?id="member-back"[\s\S]*?Choose an overload to inspect/);
  assert.match(
    renderMember,
    /if \(!memberSectionUsesWorkingSurface\(state\.memberSection\)\) return content;[\s\S]*?class="member-surface"[\s\S]*?<p>\$\{escapeHtml\(member\.kind\)} <span>· \$\{overloadIndex \+ 1} of \$\{member\.overloads\.length}<\/span><\/p>/);
  assert.match(
    memberOverview,
    /class="learn-section member-overview-intro">\s*<section class="signature-panel"[\s\S]*?class="member-documentation"[\s\S]*?class="member-identity"/);
  assert.doesNotMatch(
    memberOverview,
    /class="learn-section member-overview-intro">\s*\$\{documentationSummary\}[\s\S]*?class="signature-panel"/);
  assert.match(
    memberOverview,
    /const documentationSummary = documentationLoading[\s\S]*?Documentation query failed:[\s\S]*?overload\.summary[\s\S]*?No summary was found in compiled XML documentation/);
  assert.match(
    memberOverview,
    /aria-labelledby="member-declaration-title"[\s\S]*?\$\{copyDeclaration\}[\s\S]*?aria-label="Copy stable selector"[\s\S]*?aria-label="Copy digest"[\s\S]*?aria-label="Copy canonical signature"/);
  assert.match(
    renderMember,
    /const copyDeclaration = selectedDeclaration\?\.text[\s\S]*?aria-label="Copy declaration"/);
  assert.match(
    memberOverview,
    /renderMemberContractSections\(\{[\s\S]*?parameters,[\s\S]*?returnType: overload\.returnType,[\s\S]*?returns: overload\.returns,[\s\S]*?exceptions: overload\.exceptions,[\s\S]*?activeFramework: pkg\.activeFramework,[\s\S]*?documentationStatus:/);
  assert.doesNotMatch(renderMember, /class="learn-title"/);
  assert.doesNotMatch(
    renderMember,
    /<dt>Namespace:<\/dt>|<dt>Assembly:<\/dt>|<dt>Package:<\/dt>/);
  assert.match(
    appSource,
    /const memberOverloadPicker =[\s\S]*?!selectedConcreteOverload\([\s\S]*?const memberWorkingSurface =[\s\S]*?currentPendingGraphMember\(\) === null[\s\S]*?memberOverloadPicker[\s\S]*?memberSectionUsesWorkingSurface\(state\.memberSection\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.api-working-surface,\s*\.detail-scroll\.metadata-working-surface,\s*\.detail-scroll\.member-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.member-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px minmax\(0, 1fr\);/s);
  assert.match(
    stylesSource,
    /\.member-surface \.learn-overview \{ max-width: none; \}/);
  assert.match(
    stylesSource,
    /\.member-surface \.learn-overview > \.learn-section:not\(\.member-overview-intro\),\s*\.member-applicability \{ max-width: 900px; \}[\s\S]*?\.member-overview-intro \.signature-panel \{ margin-top: 0; \}/);
  assert.match(
    stylesSource,
    /\.member-documentation \{ max-width: 760px;/);
  assert.match(
    stylesSource,
    /\.member-surface-scroll \{ container: member-surface \/ inline-size;[\s\S]*?@container member-surface \(max-width: 575px\) \{[\s\S]*?\.member-identity dl > div \{ grid-template-columns: minmax\(0, 1fr\); \}/);
  assert.match(
    stylesSource,
    /\.member-contract-list > div \{[^}]*grid-template-columns: minmax\(190px, 32%\) minmax\(0, 1fr\);[\s\S]*?@container member-surface \(max-width: 575px\) \{[\s\S]*?\.member-contract-list > div \{ grid-template-columns: minmax\(0, 1fr\); \}/);
  assert.match(
    stylesSource,
    /\.api-surface-head p,\s*\.metadata-surface-head p \{[^}]*overflow: hidden;[^}]*text-overflow: ellipsis;/s);
  assert.doesNotMatch(
    stylesSource,
    /\.api-surface-head p span \{[^}]*display: none;/s);
});

test("type metadata uses a full-area working surface without the inset type heading", () => {
  const renderLens =
    appSource.match(/function renderLens\([\s\S]*?\n}\n\nfunction renderApiLens/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /const metadataWorkingSurface =\s*activeScope === "type" && state\.lens === "metadata"/);
  assert.match(
    appSource,
    /metadataWorkingSurface \? " metadata-working-surface" : ""/);
  assert.match(
    renderLens,
    /case "metadata":\s*return renderTypeMetadataHtml\(item\);/);
  assert.doesNotMatch(
    renderLens,
    /case "metadata":[\s\S]*?typeHeadingHtml\(item\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.api-working-surface,\s*\.detail-scroll\.metadata-working-surface,\s*\.detail-scroll\.member-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.metadata-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px minmax\(0, 1fr\) 34px;/s);
  assert.match(
    stylesSource,
    /\.metadata-surface-scroll \{[^}]*overflow: auto;/s);
});

test("Package and Library Overview share the named identity frame", () => {
  const renderPackage =
    appSource.match(/function renderPackageView\([\s\S]*?\n}\n\nfunction libraryIdentity/)?.[0]
    ?? "";
  const renderOverview =
    appSource.match(/function renderPackageOverview\([\s\S]*?\n}\n\nfunction renderLibraryCompositionOverview/)?.[0]
    ?? "";
  assert.match(appSource,
    /const overviewWorkingSurface =[\s\S]*activeScope === "package" && state\.packageLens === "overview"[\s\S]*activeScope === "library" && state\.libraryLens === "overview"/);
  assert.match(appSource,
    /overviewWorkingSurface \? " overview-working-surface" : ""/);
  assert.match(appSource,
    /const contentNavigationIntegrated =[\s\S]*?\|\| overviewWorkingSurface[\s\S]*?;/);
  assert.match(renderPackage,
    /return packageLensBody\(\);/);
  assert.match(renderOverview,
    /renderPackageDocuments\(pkg\.documents \|\| \[\], escapeHtml\)/);
  assert.match(renderOverview,
    /renderPackageInfo\(pkg\.packageInfo, escapeHtml\)/);
  assert.doesNotMatch(renderOverview,
    /platformLibrarySelectHtml|packageLibraries\(\)|data-lib-scope|library-list/);
  assert.match(renderOverview,
    /renderOverviewSurface\(\{[\s\S]*subject: "package",[\s\S]*displayName: packageDisplayName\(pkg\),[\s\S]*iconHtml: renderInspectedSubjectIcon\(pkg\),[\s\S]*coordinateFieldsHtml: packageVersionField\(\),[\s\S]*contentHtml,/);
  const renderLibraryOverview =
    appSource.match(/function renderLibraryOverview\([\s\S]*?\n}\n\nfunction renderGraphMemberPendingHtml/)?.[0]
    ?? "";
  const renderLibraryComposition =
    appSource.match(/function renderLibraryCompositionOverview\([\s\S]*?\n}\n\nfunction renderLibraryOverview/)?.[0]
    ?? "";
  assert.match(renderLibraryComposition,
    /displayName: library\?\.name \?\? "All libraries"/);
  assert.match(renderLibraryComposition,
    /libraries\.reduce\(\(sum, candidate\) => sum \+ candidate\.types, 0\)/);
  assert.match(renderLibraryComposition,
    /libraries\.reduce\(\(sum, candidate\) => sum \+ candidate\.members, 0\)/);
  assert.match(renderLibraryOverview,
    /aggregateLibrarySubjectIsActive\(\)[\s\S]*renderLibraryCompositionOverview\(currentPackage\(\), null\)/);
  assert.match(renderLibraryOverview,
    /pkg\.isRuntimePack[\s\S]*renderLibraryCompositionOverview\(pkg, library\)/);
  assert.doesNotMatch(renderLibraryOverview, /coordinateFieldsHtml:/);
  assert.match(renderLibraryOverview, /currentLibraryApiInspection\(\)/);
  assert.match(renderLibraryOverview,
    /totalTypes: inventory\.publicTypeCount,[\s\S]*totalMembers: inventory\.publicMemberCount/);
  assert.match(renderLibraryOverview,
    /\[\.\.\.inventory\.typeKinds\][\s\S]*\[\.\.\.inventory\.namespaces\]/);
  assert.match(stylesSource,
    /\.detail-scroll\.overview-working-surface,[\s\S]*?overflow: hidden;[^}]*padding: 0;/s);
});

test("library metadata uses compact coordinates in a full-area working surface", () => {
  const renderLibrary =
    appSource.match(/function renderLibraryView\([\s\S]*?\n}\n\nfunction renderWorkspaceView/)?.[0]
    ?? "";
  const renderMetadata =
    appSource.match(/function renderPackageMetadata\([\s\S]*?\n}\n\nasync function loadPackageMetadata/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /const libraryMetadataWorkingSurface =\s*activeScope === "library" && state\.libraryLens === "metadata"/);
  assert.match(
    appSource,
    /libraryMetadataWorkingSurface \? " package-metadata-working-surface" : ""/);
  assert.match(
    appSource,
    /const contentNavigationIntegrated =[\s\S]*?\|\| libraryMetadataWorkingSurface[\s\S]*?;/);
  assert.match(
    renderLibrary,
    /if \(state\.libraryLens === "overview"\s*\|\| state\.libraryLens === "compare"\s*\|\| state\.libraryLens === "references"\s*\|\| state\.libraryLens === "integrations"\s*\|\| state\.libraryLens === "analysis"\s*\|\| state\.libraryLens === "metrics"\s*\|\| state\.libraryLens === "metadata"\) return body;/);
  assert.match(
    renderMetadata,
    /data-platform-metadata-library[\s\S]*?requireSelection: true[\s\S]*?controlsHtml: metadataLibraryControl[\s\S]*?package-metadata-controls/);
  assert.doesNotMatch(
    renderMetadata,
    /packageVersionField|packageFrameworkField|packageCoordinateFields/);
  assert.match(
    appSource,
    /function openExplorerOverview\(\s*assemblyFileName: string,\s*metadataRoot: MetadataRootSelection,\s*\)[\s\S]*?buildBaseExplorer\(assemblyFileName, metadataRoot\)[\s\S]*?ex\.overview = true;[\s\S]*?state\.explorer = ex;[\s\S]*?render\(\);/);
  assert.match(
    appSource,
    /onOpenOverview: openExplorerOverview/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.package-metadata-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.package-metadata-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px auto minmax\(0, 1fr\) 34px;/s);
  assert.match(
    stylesSource,
    /\.package-metadata-scroll \{[^}]*overflow: auto;/s);
});

test("library Metrics uses the full-area analysis working surface", () => {
  assert.match(
    appSource,
    /const libraryMetricsWorkingSurface =\s*activeScope === "library" && state\.libraryLens === "metrics"/);
  assert.match(
    appSource,
    /libraryAnalysisWorkingSurface \|\| libraryMetricsWorkingSurface \? " library-analysis-working-surface" : ""/);
  assert.match(
    appSource,
    /contentNavigationIntegrated =[\s\S]*\|\| libraryMetricsWorkingSurface[\s\S]*?;/);
});

test("package dependencies use compact coordinates in a full-area working surface", () => {
  const renderPackage =
    appSource.match(/function renderPackageView\([\s\S]*?\n}\n\nfunction renderWorkspaceView/)?.[0]
    ?? "";
  const renderDependencies =
    appSource.match(/function renderPackageDependencies\([\s\S]*?\n}\n\nfunction renderLibraryReferences/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /const packageDependenciesWorkingSurface =\s*activeScope === "package" && state\.packageLens === "dependencies"/);
  assert.match(
    appSource,
    /packageDependenciesWorkingSurface \? " package-dependencies-working-surface" : ""/);
  assert.match(
    appSource,
    /const contentNavigationIntegrated =[\s\S]*?\|\| packageDependenciesWorkingSurface[\s\S]*?;/);
  assert.match(
    renderPackage,
    /return packageLensBody\(\);/);
  assert.match(
    appSource,
    /function renderPackageDependenciesSurface\([\s\S]*?package-dependencies-surface[\s\S]*?packageVersionField\(\)[\s\S]*?package-dependencies-scroll[\s\S]*?package-dependencies-surface-footer/);
  assert.equal(
    renderDependencies.match(/renderPackageDependenciesSurface\(/g)?.length,
    5);
  assert.match(
    appSource,
    /function patchDependenciesGroup\([\s\S]*?data-package-dependencies-status[\s\S]*?status\.textContent = packageDependenciesStatus\(data, selectedGroupIndex\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.package-dependencies-working-surface,[\s\S]*?overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.package-dependencies-surface,[\s\S]*?grid-template-rows: 40px auto minmax\(0, 1fr\) 34px;/s);
  assert.match(
    stylesSource,
    /\.package-dependencies-scroll,[\s\S]*?overflow: auto;/s);
});

test("Dependencies adopts the shared graph viewer without moving the package lists", () => {
  assert.match(
    appSource,
    /id="dependency-graph-explore" data-graph-explore\$\{dependencyGraphAvailable\(\)/);
  assert.match(
    appSource,
    /<div data-dependency-graph-surface>\$\{dependencyGroupNotice\}\$\{declarationFailureNotice\}\$\{selector\}\$\{graphSection\}<\/div>\$\{pruningSection\}\$\{depList\}/);
  assert.match(
    appSource,
    /graphExplorer\.beforeRender\(graphExplorerKey\(\)\)/);
  assert.match(
    appSource,
    /graphExplorer\.afterRender\(graphExplorerTarget\(\)\)/);
  const key = appSource.match(/function graphExplorerKey\(\)[\s\S]*?\n}/)?.[0] ?? "";
  assert.match(key, /JSON\.stringify\(\["dependencies", packageDependenciesSignature\(\)\]\)/);
  assert.doesNotMatch(key, /dependenciesGroupIndex/);
  assert.match(
    appSource,
    /function switchToPackageForDependencies[\s\S]*?closeGraphExplorerForNavigation\(\);[\s\S]*?activatePackage\(target/);
  assert.match(
    appSource,
    /async function openDependencyPackage[\s\S]*?closeGraphExplorerForNavigation\(\);[\s\S]*?navigationSequence\.begin\(\)/);
  assert.match(
    appSource,
    /function restoreGraphExplorerNavigationFocus[\s\S]*?querySelector<HTMLButtonElement>\("\[data-graph-explore\]"\)/);
});

test("graph member projections stay transport- and package-bounded", () => {
  assert.match(
    browserGraphMemberSource,
    /QueryGraphMemberSurface[\s\S]*?BrowserSurfaceTextBudget\([\s\S]*?MaxRetainedTextCharacters[\s\S]*?BrowserSurfaceProjection\.Type\([\s\S]*?textBudget,[\s\S]*?selectedMembers: \[resolution\.Member\]\)[\s\S]*?textBudget\.CommitParticipant\(\)/);

  const publicMember = { name: "Public" };
  const selected = { name: "Selected", graphOnly: true };
  const removed = { name: "Removed", graphOnly: true };
  const types = [
    { api: [publicMember, removed] },
    { api: [selected] },
  ];
  retainGraphMemberProjection(types, selected);
  assert.deepEqual(types, [
    { api: [publicMember] },
    { api: [selected] },
  ]);

  const commit = sourceText(functionDeclaration("commitGraphMemberSelection"));
  assert.match(commit, /retainGraphMemberProjection\(pkg\.types, staged\.member\)/);
  assert.ok(
    commit.indexOf("retainGraphMemberProjection(pkg.types, staged.member)")
      < commit.indexOf("type.api.push(staged.member)"));
});

test("member filters retain an exact selected graph target", () => {
  const availability = sourceText(functionDeclaration("memberSelectionIsAvailable"));
  assert.match(
    availability,
    /visible\.some\(group => group\.key === state\.selectedMemberKey\)[\s\S]*selectedGraphMemberGroup\(type\) != null/);

  for (const name of ["enterMemberScope", "normalizeMemberSelection"]) {
    assert.match(
      sourceText(functionDeclaration(name)),
      /memberSelectionIsAvailable\(type, visible\)/);
  }

  const typePanelCall = onlyCallExpressionNamed(appSyntax, "bindTypePanel");
  const actions = objectArgument(typePanelCall, 1, "bindTypePanel");
  for (const name of [
    "onMemberAccessibilityFilterSelect",
    "onMemberFilterChange",
    "onMemberFilterClear",
    "onMemberFilterKeyDown",
    "onMemberKindFilterSelect",
    "onMemberTraitFilterSelect",
  ]) {
    assert.match(
      sourceText(callbackProperty(actions, name)),
      /normalizeMemberSelection\(\)/);
  }
});

test("pending graph restoration replaces its current history entry", () => {
  const navigation = {
    stack: [
      { sig: "provisional", view: { selectedOverloadIndex: 1 } },
      { sig: "forward", view: { selectedOverloadIndex: 2 } }
    ],
    index: 0
  };
  const resolved = {
    sig: "resolved",
    view: { selectedOverloadIndex: 0 }
  };

  replaceCurrentNavigationEntry(navigation, resolved);

  assert.equal(navigation.index, 0);
  assert.equal(navigation.stack.length, 2);
  assert.deepEqual(navigation.stack[0], resolved);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["resolved", "forward"]);
});

test("history normalization preserves the current index and forward entries", () => {
  const navigation: NavigationState<{ selectedOverloadIndex?: number }> = {
    stack: [
      { sig: "older", view: {} },
      { sig: "recorded", view: { selectedOverloadIndex: 0 } },
      { sig: "forward", view: {} }
    ],
    index: 1
  };
  const normalized = {
    sig: "normalized",
    view: { selectedOverloadIndex: 1 }
  };

  reconcileCurrentNavigationEntry(navigation, normalized);

  assert.equal(navigation.index, 1);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["older", "normalized", "forward"]);

  reconcileCurrentNavigationEntry(navigation, normalized);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["older", "normalized", "forward"]);
});

test("restored views reconcile normalization before rendering", () => {
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  assert.match(
    apply,
    /if \(!state\.atPackageRoot && !state\.atLibraryRoot\) revealTypeInFilters\(type\)/);
  assert.match(
    apply,
    /graphSelection\?\.group\.key !== view\.selectedMemberKey\) \{[\s\S]*?navigationHistory\.normalizeCurrent\(\);[\s\S]*?restorePendingGraphMember\(\)/);
  assert.match(
    apply,
    /state\.memberSection = memberHistory\.memberSection;[\s\S]*?navigationHistory\.normalizeCurrent\(\);/);
});

test("ambiguous call graph targets expose a visible refusal", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /return blockedCallGraphNodeBinding\([\s\S]*graphTargetBlockedReason/);
  assert.match(
    appSource,
    /function blockedCallGraphNodeBinding\([\s\S]*label: `Cannot open \$\{target\.typeFullName\}\.\$\{target\.memberName\}: \$\{reason\}`[\s\S]*blocked: true/);
  assert.match(
    appSource,
    /invalidateGraphMemberNavigation\(\);\s*state\.memberCallGraphSeq\+\+;[\s\S]*?state\.graphMemberNavigationError\s*=\s*`Could not open \$\{target\.typeFullName\}\.\$\{target\.memberName\}: \$\{reason\}\.`;[\s\S]*?render\(\)/);
  assert.match(
    graphInteractionsSource,
    /node\.setAttribute\("tabindex", "0"\);[\s\S]*node\.setAttribute\("role", "button"\)[\s\S]*node\.addEventListener\("click"[\s\S]*"call-graph-node\.activate"[\s\S]*key: \["Enter", " "\]/);
});

test("navigable call graph targets share mouse and keyboard activation", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";

  assert.equal(
    binding.match(/`Open \$\{target\.typeFullName\}\.\$\{target\.memberName\}`/g)?.length,
    3);
  assert.match(
    graphInteractionsSource,
    /node\.setAttribute\("tabindex", "0"\);[\s\S]*node\.setAttribute\("role", "button"\);[\s\S]*node\.setAttribute\("aria-label", binding\.label\)/);
  assert.match(
    stylesSource,
    /\.graph-viewport g\.node\.nav-node:hover \{ filter: drop-shadow\(0 0 2px var\(--blue\)\); \}/);
  assert.match(
    stylesSource,
    /\.graph-viewport g\.node\.nav-node:focus-visible \{[\s\S]*?outline: 2px solid var\(--blue\);[\s\S]*?filter: drop-shadow\(0 0 4px var\(--blue\)\);/);
  assert.match(
    appSource,
    /id="platform-drill-error" class="graph-drill-error" role="alert" tabindex="-1"/);
  assert.match(
    appSource,
    /function showPlatformTargetError[\s\S]*?render\(\);\s*focusPlatformGraphError\(document\);\s*await renderMermaidCallGraph\(\)/);
});

test("async graph work uses one source-view ownership contract", () => {
  const ownership =
    appSource.match(/function captureViewOperation[\s\S]*?(?=\nfunction invalidateGraphMemberNavigation)/)?.[0]
    ?? "";
  assert.match(
    ownership,
    /navigationSequence: navigationSequence\.current\(\),[\s\S]*?sourceView: viewSignature\(\)/);
  assert.match(
    ownership,
    /owner\.sequence === currentSequence[\s\S]*?owner\.navigationSequence === navigationSequence\.current\(\)[\s\S]*?owner\.sourceView === viewSignature\(\)/);
  assert.equal(
    appSource.match(/captureViewOperation\(/g)?.length,
    10);
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
    assemblyPublicKeyToken: "0011223344556677",
    typeMetadataId: "Example.Widget",
    kind: "external"
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

  const skewedPackage = {
    ...packageAt("2.0.0", "net8.0"),
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Widget"
    }],
    assemblies: [{
      id: "example",
      ...exact,
      version: "2.0.0.0"
    }]
  };
  const candidate = resolveLoadedGraphTargetCandidate(
    [skewedPackage],
    target);
  assert.deepEqual(candidate, { status: "skew" });
  assert.equal(
    graphTargetNavigationDisposition(candidate, target),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      candidate,
      target,
      false),
    "blocked");
  assert.equal(
    graphTargetBlockedReason(candidate, "package"),
    "the loaded package assembly identity does not match the exact target");

  const noProjectedTarget = {
    ...skewedPackage,
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Other"
    }]
  };
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([noProjectedTarget], target),
    { status: "skew" });

  const exactResident = {
    ...noProjectedTarget,
    assemblies: [{ id: "example", ...exact }]
  };
  const residentCandidate = resolveLoadedGraphTargetCandidate(
    [exactResident],
    target);
  assert.deepEqual(residentCandidate, { status: "resident" });
  assert.equal(
    graphTargetNavigationDisposition(residentCandidate, target),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      residentCandidate,
      target,
      false),
    "drill");
  assert.equal(
    graphTargetBlockedReason(residentCandidate, "package"),
    "the exact target type is not projected from the loaded package assembly");
});

test("surface asset currency makes repeated graph navigation reuse its type", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Internal",
    kind: "external"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    assemblies: [{
      id: "compile:ref/net8.0/Example.dll",
      name: "Example",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }],
    types: [] as Array<{
      assemblyId: string;
      assemblyName: string;
      metadataId: string;
    }>
  };

  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([pkg], target),
    { status: "resident" });

  const projectedType = {
    assemblyId: "compile:ref/net8.0/Example.dll",
    assemblyName: "Example",
    metadataId: "Example.Internal"
  };
  pkg.types.push(projectedType);

  for (let attempt = 0; attempt < 2; attempt++) {
    assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
      status: "unique",
      pkg,
      type: projectedType
    });
  }
  assert.equal(pkg.types.length, 1);
});

test("graph-member projection carries exact surface currency and a collision-safe id", () => {
  assert.match(
    appSource,
    /inspectGraphMemberSurface\([\s\S]*graphMemberSurfaceAssembly\(target, type\)/,
  );
  assert.match(
    browserGraphMemberSource,
    /BrowserSurfaceProjection\.Type\([\s\S]*qualifyId: true,[\s\S]*selectedMembers:/,
  );

  const projected = {
    id: "Surface.A:Shared.Internal",
    definitionId: "Shared.Internal",
    assemblyId: "compile:ref/net11.0/Surface.A.dll",
  };
  const types = [
    {
      id: "Shared.Internal",
      definitionId: "Shared.Internal",
      assemblyId: "compile:ref/net11.0/Surface.B.dll",
    },
    projected,
  ];

  assert.equal(
    types.find(type => type.id === projected.id)?.assemblyId,
    projected.assemblyId);
});

test("restored graph members recover dotted routing from loaded type currency", () => {
  const original = {
    assembly: "System.Text.Json",
    assemblyVersion: "10.0.0.0",
    assemblyCulture: "",
    assemblyPublicKeyToken: "cc7b13ffcd2ddd51",
    typeDefinitionId: "System.Text.Json.JsonReaderHelper",
    typeMetadataId: "System.Text.Json.JsonReaderHelper",
    memberName: "UnescapeAndCompareBothInputs",
    selectorKey: "method:UnescapeAndCompareBothInputs",
    metadataToken: 0x06000123,
    surfaceAssemblyId: "compile:ref/net10.0/System.Text.Json.dll",
  };
  const restored = graphMemberTargetFromShare(
    graphMemberShareTarget(original));

  assert.ok(restored);
  assert.equal(restored.surfaceAssemblyId, undefined);
  assert.equal(
    graphMemberSurfaceAssembly(restored, {
      assembly: "System.Text.Json.dll",
      assemblyId: "compile:ref/net10.0/System.Text.Json.dll",
    }),
    "compile:ref/net10.0/System.Text.Json.dll");
  assert.equal(
    graphMemberSurfaceAssembly(restored),
    "System.Text.Json.dll");
});

test("an exact resident runtime target wins over package identity skew", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget",
    kind: "external"
  };

  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "unique", pkg: null, type: null },
      target,
      true),
    "resident");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "resident" },
      target,
      true),
    "resident");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "missing" },
      target),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      { status: "ambiguous" },
      target,
      true),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "ambiguous" },
      target),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      { status: "unique", pkg: null, type: null },
      { ...target, assemblyVersion: null },
      true),
    "none");
});

test("graph-only overloads retain the latest graph-selected body", () => {
  const getter = {
    memberName: "get_Item",
    selectorKey: "getter-selector"
  };
  const setter = {
    memberName: "set_Item",
    selectorKey: "setter-selector"
  };
  const graphOnlyOverload = {
    graphOnly: true,
    graphTarget: getter
  };
  const publicOverload = {
    graphOnly: false
  };

  retainGraphOnlyBodyTarget(graphOnlyOverload, setter);
  retainGraphOnlyBodyTarget(publicOverload, setter);

  assert.equal(graphOnlyBodyTarget(graphOnlyOverload), setter);
  assert.equal(graphOnlyBodyTarget(publicOverload), null);
  assert.equal(
    Object.prototype.hasOwnProperty.call(publicOverload, "graphTarget"),
    false);
  assert.match(
    appSource,
    /selectedBodyTarget = retainGraphOnlyImplementationBody\(\s*overload,\s*bodyTarget\)/);
  assert.match(
    appSource,
    /const selectedTarget = retainGraphOnlyImplementationBody\(\s*staged\.member,\s*target\)/);
  assert.doesNotMatch(
    appSource,
    /group\.overloads\[overloadIndex\]\.graphTarget = bodyTarget/);
});

test("selected graph bodies preserve the full navigation identity", () => {
  const selected = graphMemberTargetWithSelectedBody({
    assembly: "Example.dll",
    assemblyVersion: "1.2.3.4",
    assemblyCulture: null,
    assemblyPublicKeyToken: "abcdef",
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "stale",
    selectorKey: "stale-selector",
    metadataToken: 0x06000002,
  }, {
    token: 0x06000001,
    memberName: "get_Value",
    selectorKey: "getter-selector",
  });

  assert.deepEqual(graphMemberShareTarget(selected), [
    "Example.dll",
    "1.2.3.4",
    null,
    "abcdef",
    "T:Example.Widget",
    "Example.Widget",
    "get_Value",
    "getter-selector",
    0x06000001,
  ]);
});

test("call graph navigation keeps identity-unknown targets inert", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: null,
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget",
    kind: "external"
  };
  const pack = {
    ...packageAt("1.0.0", "net8.0"),
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Widget"
    }],
    assemblies: [{
      id: "example",
      name: "Example",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }]
  };

  const candidate = resolveLoadedGraphTargetCandidate([pack], target);
  assert.deepEqual(candidate, { status: "missing" });
  assert.equal(
    graphTargetNavigationDisposition(candidate, target),
    "none");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      candidate,
      target,
      false),
    "none");
});

test("failed graph restoration uses the canonical empty member identity", () => {
  const restore =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    restore,
    /catch \(error\)[\s\S]*?state\.selectedMemberKey = "";/);
  assert.doesNotMatch(
    restore,
    /state\.selectedMemberKey = null/);
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

test("platform call graph navigation requires the target assembly identity", () => {
  const consoleType = {
    assembly: "System.Console.dll",
    assemblyId: "console",
    assemblyName: "System.Console",
    definitionId: "Interop+ErrorInfo"
  };
  const processType = {
    assembly: "System.Diagnostics.Process.dll",
    assemblyId: "process",
    assemblyName: "System.Diagnostics.Process",
    definitionId: "Interop+ErrorInfo"
  };
  const pack = {
    isRuntimePack: true,
    types: [consoleType, processType],
    assemblies: [
      {
        id: "console",
        name: "System.Console",
        version: "11.0.0.0",
        culture: null,
        publicKeyToken: "b03f5f7f11d50a3a"
      },
      {
        id: "process",
        name: "System.Diagnostics.Process",
        version: "11.0.0.0",
        culture: null,
        publicKeyToken: "b03f5f7f11d50a3a"
      }
    ]
  };
  const target = {
    assembly: "System.Diagnostics.Process",
    assemblyVersion: "11.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "Interop+ErrorInfo"
  };

  assert.equal(resolvePlatformGraphTargetType(pack, target), processType);
  assert.equal(
    resolvePlatformGraphTargetType({
      ...pack,
      types: [...pack.types, { ...processType }]
    }, target),
    null);
  assert.equal(
    resolvePlatformGraphTargetType(pack, {
      ...target,
      assemblyVersion: "12.0.0.0"
    }),
    null);
});

test("opportunity navigation uses exact source assembly and definition identity", () => {
  const first = {
    assembly: "A.dll",
    assemblyId: "a",
    assemblyName: "Shared",
    definitionId: "Example.Widget"
  };
  const second = {
    assembly: "B.dll",
    assemblyId: "b",
    assemblyName: "Shared",
    definitionId: "Example.Widget"
  };
  const pack = {
    types: [first, second],
    assemblies: [
      { id: "a", name: "Shared", version: "1.0.0.0" },
      { id: "b", name: "Shared", version: "2.0.0.0" }
    ]
  };

  assert.equal(resolveOpportunitySourceType(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "2.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }), second);
  assert.equal(resolveOpportunitySourceType(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "3.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }), null);
  assert.equal(resolveOpportunitySourceCandidate(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "3.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }).status, "skew");
  assert.equal(resolveOpportunitySourceCandidate({
    ...pack,
    assemblies: pack.assemblies.map(assembly => ({
      ...assembly,
      version: "2.0.0.0"
    }))
  }, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "2.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }).status, "ambiguous");
  assert.match(
    appSource,
    /opportunity\.sourceIdentity === "legacy"[\s\S]*?openSpotlight\(shortTypeName\(opportunity\.typeId\)\)[\s\S]*?opportunity\.sourceIdentity !== "exact"[\s\S]*?!opportunity\.sourceDefinitionId[\s\S]*?exact identity is unavailable[\s\S]*?if \(candidate\.status !== "unique"\) \{[\s\S]*?appendQueryNotice\(\s*`The opportunity source could not be opened: \$\{reason\}\.`\);[\s\S]*?navigateToType\(candidate\.type\)/);
});

test("graph-first platform acquisition preserves catalog family and physical filename", async () => {
  const row = {
    tfm: "net11.0",
    pack: "netcore.app" as const,
    assembly: "Mixed",
    file: "PhysicalName.dll",
    kind: "impl" as const,
    forwardsTo: null,
    version: "11.0.0.0",
    publicTypes: 1,
    inReferencePack: true,
    hasImplementation: true,
    packVersion: "11.0.0",
  };
  const target = {
    tfm: "net11.0",
    version: "11.0.0",
    rows: [row],
    supplies: [],
  };
  assert.equal(
    platformGraphLibraryForTarget(target, "Mixed.dll", "netcore.app"),
    row);

  const loader =
    appSource.match(/async function loadRuntimeGraphAssembly[\s\S]*?\n\}/)?.[0]
    ?? "";
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const drill =
    appSource.match(/async function drillPlatformNode[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const requests: unknown[][] = [];
  const catalogRequests: unknown[][] = [];
  await runInNewContext(
    stripTypeScriptTypes(`(async () => {
      ${loader}
      await loadRuntimeGraphAssembly(
        "net11.0-windows10.0.19041.0",
        "11.0.0", "Mixed", "netcore.app", () => true);
    })()`),
    {
      ensurePlatformCatalog: async (...args: unknown[]) => {
        catalogRequests.push(args);
        return target;
      },
      platformCatalogFramework,
      platformGraphLibraryForTarget,
      platformAssemblyRequest,
      loadRuntimePackAssembly: async (...args: unknown[]) => {
        requests.push(args);
        return { packageModel: {}, failureMessage: "" };
      },
      errorMessage: (error: unknown) => String(error),
    });
  assert.deepEqual(catalogRequests, [["net11.0", "11.0.0"]]);
  assert.equal(requests.length, 1);
  const request = requests[0]!;
  assert.deepEqual(
    [request[0], request[1], request[2], request[4], request[5]],
    [
      "net11.0",
      "Mixed.dll",
      "netcore.app",
      "11.0.0",
      "PhysicalName.dll",
    ]);
  assert.equal(typeof request[3], "function");
  assert.equal(
    navigation.match(/loadRuntimeGraphAssembly\(/g)?.length,
    2);
  assert.match(
    navigation,
    /const framework = platformCatalogFramework\(\s*state\.package\?\.activeFramework \|\| ""\)/);
  assert.match(
    drill,
    /const framework = platformCatalogFramework\(currentPackage\(\)\.activeFramework\)[\s\S]*resolvedPlatformTargetVersion\([\s\S]*framework\)[\s\S]*platformPackForGraphAssembly\([\s\S]*framework\) \?\? ""/);
  assert.match(
    navigation,
    /loadRuntimeGraphAssembly\(\s*framework,\s*retainedPlatform\?\.version \?\? "",\s*node\.assembly,\s*targetPack,\s*navigationIsCurrent\)/);
  assert.match(
    navigation,
    /loadRuntimeGraphAssembly\(\s*framework,\s*pack\.version,\s*node\.assembly,\s*targetPack,\s*navigationIsCurrent\)/);
  assert.doesNotMatch(navigation, /\bloadRuntimePackAssembly\(/);
});

test("cold platform graph navigation acquires the exact assembly before any default runtime", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const coldLoad =
    navigation.match(/if \(!pack\) \{[\s\S]*?(?=\n  let candidate)/)?.[0]
    ?? "";

  assert.match(
    coldLoad,
    /platformPackForGraphAssembly\(\s*node\.assembly,\s*node\.platformPack,\s*runtimePackPackage\(\),\s*framework\)[\s\S]*?loadRuntimeGraphAssembly\([\s\S]*?targetPack/);
  assert.doesNotMatch(coldLoad, /\bloadRuntimePack\(/);
});

test("relationship navigation rejects ambiguous dotted identities", () => {
  const first = { id: "A:N.T", queryId: "N.T" };
  const second = { id: "B:N.T", queryId: "N.T" };

  assert.equal(uniqueTypeByQueryId([first], "N.T"), first);
  assert.equal(uniqueTypeByQueryId([first, second], "N.T"), null);
  assert.equal(uniqueTypeByQueryId([], "N.T"), null);
});

test("relationship navigation resolves one exact type across loaded Workspace packages", () => {
  const first = {
    id: "First",
    types: [{ id: "A:N.T", queryId: "N.T" }],
  };
  const second = {
    id: "Second",
    types: [{ id: "B:N.U", queryId: "N.U" }],
  };

  assert.deepEqual(
    uniqueWorkspaceTypeByQueryId([first, second], "N.U"),
    { pkg: second, type: second.types[0] });
  assert.equal(
    uniqueWorkspaceTypeByQueryId(
      [
        first,
        {
          id: "Duplicate",
          types: [{ id: "C:N.T", queryId: "N.T" }],
        },
      ],
      "N.T"),
    null);
});

// Same widening as `engineCallGraphTarget`: the engine's diagnostics payload also carries
// `isIncomplete` and `hasUnexploredTraversalBoundary`, which the message view in `data.ts`
// deliberately ignores in favour of the counted evidence. Keeping them in the fixtures is
// what proves the message is driven by the counts rather than by the summary flag.
