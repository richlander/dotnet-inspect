import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { runInNewContext } from "node:vm";
import {
  activeSourceOperationKind,
  pdbSourceLimitationHtml,
  callGraphTargetMatchesType,
  callGraphTargetTypeId,
  memberRequestKey,
  memberSectionDefinitions,
  memberSectionIdsFor,
  packageCoordinateMatchesLocation,
  packageForView,
  packageIdentityKey,
  resolveLoadedGraphTargetCandidate,
  sourceSurfaceIsVisible,
  sourceReloadKind,
  sourceRequestNeedsLoad,
  spotlightCandidateSignature,
  workspaceCoordinatesMatch,
} from "../src/data.ts";
import {
  normalizeSourceResultSnapshot,
  sourceResultNeedsLoad,
} from "../src/source-inspection.ts";
import type { SourceWorkbenchState } from "../src/data.ts";

import {
  packageAt,
  appSource,
  parsedAppSource,
  packageAcquisitionSource,
  sourceInspectionSource,
  memberDetailInspectionSource,
  typePanelSource,
  generatedFacadeModules,
  generatedFacadeModuleUrls,
  generatedFacadeSource,
  generatedFacadeSourceText,
  engineCoordinatorSource,
} from "./composition-root-test-fixture.ts";
test("cached Platform roots re-enter Workspace membership before activation", () => {
  const retainTarget =
    appSource.match(/function retainPlatformPackageForTarget[\s\S]*?\n\}/)?.[0]
    ?? "";
  const installTarget =
    appSource.match(/function installPlatformTarget[\s\S]*?\n\}/)?.[0]
    ?? "";
  const applyViewNode = parsedAppSource.program.body.find(node =>
    node.type === "FunctionDeclaration" && node.id?.name === "applyView");
  assert.ok(applyViewNode);
  const applyView = appSource.slice(applyViewNode.start, applyViewNode.end);
  const cached = {
    ...packageAt("11.0.0", "net11.0"),
    source: { kind: "platform" },
  };
  const state = {
    packages: [] as typeof cached[],
    package: null as typeof cached | null,
    platformSelection: null,
    workspaceShareBasis: null as { tabs: unknown[] } | null,
    platformIndex: {
      target: () => ({ tfm: "net11.0", version: "11.0.0" }),
    },
  };
  const retained: typeof cached[] = [];
  const released: typeof cached[] = [];
  let invalidations = 0;
  let resolvedTabs: unknown[] = [];
  let cachedTarget: typeof cached | null = cached;
  const context = {
    state,
    cached,
    target: { tfm: "net11.0", version: "11.0.0" },
    runtimePackageForTarget: () => cachedTarget,
    retainPackageModel: (packageModel: typeof cached) => {
      retained.push(packageModel);
      state.packages = [packageModel];
    },
    releasePackageModelCaches: (packageModel: typeof cached) => {
      released.push(packageModel);
    },
    invalidateWorkspaceMembershipViews: () => {
      invalidations++;
      state.workspaceShareBasis = null;
    },
    platformCoordinateCapacityError: () => "",
    resolvedWorkspaceShareTabs: () => resolvedTabs,
    workspaceShareTabsMatchResolved: (
      requested: unknown[],
      resolved: unknown[],
    ) => requested === resolved,
    resetMemberSectionState: () => {},
    invalidateMemberDestinationWork: () => {},
    navigationHistory: { normalizeCurrent: () => {} },
    history: { state: null },
    location: { href: "https://example.test/" },
    workspaceLocation: { replace: () => true },
    withPlatformRootParentHistory: (historyState: unknown) => historyState,
    render: () => {},
    showToast: () => {},
  };

  runInNewContext(
    stripTypeScriptTypes(`${retainTarget}\n${installTarget}\ninstallPlatformTarget(target);`),
    context);
  assert.equal(state.package, cached);
  assert.deepEqual(retained, [cached]);
  assert.equal(invalidations, 1);

  resolvedTabs = [{ source: ":Platform" }];
  const preservedBasis = { tabs: resolvedTabs };
  state.packages = [];
  state.workspaceShareBasis = preservedBasis;
  runInNewContext(
    stripTypeScriptTypes(`${retainTarget}\nretainPlatformPackageForTarget(target);`),
    context);
  assert.equal(state.workspaceShareBasis, preservedBasis);

  state.packages = [];
  state.workspaceShareBasis = preservedBasis;
  runInNewContext(
    stripTypeScriptTypes(`${retainTarget}\n${installTarget}\ninstallPlatformTarget(target);`),
    context);
  assert.equal(state.workspaceShareBasis, preservedBasis);

  state.packages = [];
  state.package = null;
  state.platformSelection = null;
  state.workspaceShareBasis = null;
  retained.length = 0;
  assert.equal(
    runInNewContext(
      stripTypeScriptTypes(`${retainTarget}\n${applyView}\napplyView({
        rootKind: "platform",
        platform: {
          tfm: "net11.0",
          version: "11.0.0",
          includeAllLibraries: false,
          filter: "",
        },
        atPackageRoot: true,
        workspaceSubjectOpen: false,
      });`),
      context),
    true);
  assert.equal(state.package, cached);
  assert.deepEqual(retained, [cached]);

  const previous = cached;
  cachedTarget = null;
  state.packages = [previous];
  state.package = previous;
  state.workspaceShareBasis = preservedBasis;
  resolvedTabs = [];
  retained.length = 0;
  invalidations = 0;
  runInNewContext(
    stripTypeScriptTypes(`${retainTarget}\n${installTarget}\ninstallPlatformTarget({
      tfm: "net12.0",
      version: "12.0.0",
    });`),
    context);
  assert.equal(state.package, null);
  assert.equal(state.workspaceShareBasis, null);
  assert.deepEqual(state.packages, []);
  assert.deepEqual(released, [previous]);
  assert.equal(invalidations, 1);
});

test("Platform Library entry from demos publishes only after selection and restores failure", () => {
  const openLibrary =
    appSource.match(/async function openPlatformLibrary[\s\S]*?(?=\nfunction pickSpotlightLoadedPackage)/)?.[0]
    ?? "";
  assert.match(
    openLibrary,
    /const createsWorkspace = !scopeOnly && options\.inPlace !== true;[\s\S]*const construction = createsWorkspace\s*\? captureWorkspaceConstructionSnapshots\(navigationSeq\)\s*: null/);
  assert.match(
    openLibrary,
    /const navigationSeq = options\.navigationSeq \?\? navigationSequence\.begin\(\);/);
  assert.match(
    openLibrary,
    /await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return undefined;\s*if \(construction\) \{\s*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return undefined;\s*const publication = stageCurrentWorkspacePublication\(\s*construction\.retainedSnapshot,\s*destination\);\s*if \(!commitStagedWorkspaceWithNavigation\(\s*publication,\s*\(\) => workspaceLocation\.push\(destination\)\)\) \{\s*throw new Error\("Browser history could not be updated\."\);[\s\S]*state\.loading = false;\s*render\(construction \? \{ synchronizeUrl: false \} : undefined\)/);
  assert.match(
    openLibrary,
    /const rollbackSnapshot = construction\?\.rollbackSnapshot[\s\S]*if \(rollbackSnapshot\) \{\s*failWorkspaceCatalogAction\([\s\S]*rollbackSnapshot,[\s\S]*focusWorkbenchSearchOrHeading\);\s*return undefined;/);
  assert.match(
    openLibrary,
    /if \(deferPlatformPresentation\) installPlatformTarget\(target, false\);/);
  assert.doesNotMatch(openLibrary, /beginDemoNavigation|cancelDemoNavigation/);
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

test("member request identity distinguishes colliding type queries", () => {
  const memberSignature =
    appSource.match(/function memberRequestSignature\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    memberSignature,
    /type\?\.queryId \?\? type\?\.id,\s+type\?\.definitionId \?\? type\?\.id/);

  const request = [
    "Example.Package",
    "1.0.0",
    "net8.0",
    "Example.dll",
    "Example.Outer.Inner"
  ];
  assert.notEqual(
    memberRequestKey([...request, "Example.Outer+Inner", "M:Run"]),
    memberRequestKey([...request, "Example.Outer\\.Inner", "M:Run"]));
});

test("annotated source request identity includes the selected body", () => {
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  assert.match(
    annotatedLoader,
    /const signature = memberRequestSignature\(type, overload, true, true\)/);
  assert.match(
    annotatedLoader,
    /isCurrent: \(\) => memberRequestIsCurrent\(signature, true, true\)/);
  assert.match(
    annotatedLoader,
    /state\.selectedBodyTarget\?\.selectorKey \?\? overload\.graphSelectorKey,[\s\S]*?state\.selectedBodyTarget\?\.metadataToken \?\? overload\.metadataToken/);
  const annotatedCoordinator =
    memberDetailInspectionSource.match(/async loadFindingCensus\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.equal(
    [...annotatedCoordinator.matchAll(/request\.isCurrent\(\)/g)].length,
    3);

  const request = [
    "Example.Package",
    "1.0.0",
    "net8.0",
    "Example.dll",
    "Example.Outer.Inner",
    "Example.Outer+Inner",
    "M:Run"
  ];
  // The product stringifies the metadata token before building the key
  // (`dotnet-inspect.ts` pushes `String(state.selectedBodyTarget?.metadataToken ?? "")`),
  // so the fixture spells the token the same way rather than relying on `join` to coerce
  // a raw number.
  assert.notEqual(
    memberRequestKey([...request, String(0x06000001), "M:Run"]),
    memberRequestKey([...request, String(0x06000002), "M:<Run>b__0_0"]));
});

test("member detail adapters preserve exact engine coordinates", () => {
  const coordinator =
    appSource.match(
      /const memberDetailInspection = createMemberDetailInspectionCoordinator\(\{[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const documentationLoader =
    appSource.match(
      /async function loadSelectedMemberDocumentation\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberSource/)?.[0]
    ?? "";
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  const factsLoader =
    appSource.match(
      /async function loadSelectedMemberFacts\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberFactsSurface/)?.[0]
    ?? "";
  const factsSurfaceLoader =
    appSource.match(
      /async function loadSelectedMemberFactsSurface\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  const annotatedAction =
    appSource.match(
      /function applyAnnotatedSourceAction\(action: AnnotatedSourceAction\)[\s\S]*?\n}\n\nfunction openFindingInstanceFromFacts/)?.[0]
    ?? "";
  const factsRenderer = readFileSync(
    new URL("../src/member-facts.ts", import.meta.url), "utf8");
  assert.match(appSource, /content = renderMemberFacts\(state\)/);

  assert.match(
    coordinator,
    /request\.isRuntimePack\s*\?\s*inspectPlatformMemberDeclaration\(\s*request\.framework,\s*request\.version,\s*request\.assembly,\s*request\.platformPack,\s*request\.typeIdentity,\s*request\.member,\s*request\.selectorKey,\s*request\.metadataToken\)\s*:\s*inspectMemberDeclaration\(/);
  assert.match(
    coordinator,
    /request\.isRuntimePack\s*\?\s*inspectPlatformMemberDocumentation\(\s*request\.framework,\s*request\.version,\s*request\.assembly,\s*request\.platformPack,\s*documentationId\)\s*:\s*inspectMemberDocumentation\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*documentationId\)/);
  assert.match(
    coordinator,
    /inspectMemberFindingCensus\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.typeIdentity,\s*request\.type,\s*request\.member,\s*request\.memberSignature,\s*request\.selectorKey,\s*request\.metadataToken,\s*request\.taste\)/);
  assert.match(
    coordinator,
    /const document = result\.annotatedSource\.document;\s*validateAnnotatedSourceDocument\(document\);[\s\S]*annotatedSource: \{\s*\.\.\.result\.annotatedSource,\s*document/);
  assert.match(
    coordinator,
    /inspectMemberFacts\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.typeIdentity,\s*request\.member,\s*request\.memberSignature,\s*request\.selectorKey,\s*request\.metadataToken,\s*request\.implementationBodySelected\)/);
  assert.match(
    documentationLoader,
    /const signature = memberRequestSignature\(type, overload\)/);
  assert.match(
    documentationLoader,
    /await Promise\.all\(\[\s*memberDetailInspection\.loadDocumentation\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*platformPack: pkg\.isRuntimePack\s*\?\s*platformPackForAssembly\(type\.assembly, type\.platformPack\) \?\? ""\s*:\s*"",\s*overload,\s*isRuntimePack: Boolean\(state\.package\?\.isRuntimePack\),\s*isCurrent: \(\) => memberRequestIsCurrent\(signature\)/);
  assert.match(
    documentationLoader,
    /memberDetailInspection\.loadDeclaration\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*isRuntimePack: pkg\.isRuntimePack,\s*platformPack: pkg\.isRuntimePack\s*\?\s*platformPackForAssembly\(type\.assembly, type\.platformPack\) \?\? ""\s*:\s*"",\s*typeIdentity: type\.definitionId \?\? type\.id,\s*member: overload\.name,\s*selectorKey: overload\.graphSelectorKey,\s*metadataToken:\s*overload\.declarationMetadataToken \?\? overload\.metadataToken \?\? 0,\s*implementationMember: Boolean\(overload\.graphOnly\),\s*isCurrent: \(\) => memberRequestIsCurrent\(signature\)/);
  assert.match(
    annotatedLoader,
    /loadFindingCensus\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*typeIdentity: type\.definitionId \?\? type\.id,\s*type: type\.queryId \?\? type\.id,\s*member: state\.selectedBodyTarget\?\.memberName \?\? overload\.name,\s*memberSignature: overload\.signature,[\s\S]*taste: JSON\.stringify\(state\.taste\)/);
  assert.match(
    factsLoader,
    /const signature = memberRequestSignature\(type, overload, true\)/);
  assert.match(
    factsLoader,
    /const implementationBody = graphOnlyImplementationBody\(overload\);\s*const implementationMetadataToken = implementationBody\?\.token \?\? 0;\s*const implementationBodySelected = implementationMetadataToken !== 0;\s*return memberDetailInspection\.loadFacts\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*type: type\.queryId \?\? type\.id,\s*typeIdentity: type\.definitionId \?\? type\.id,\s*member: implementationBody\?\.memberName\s*\?\? state\.selectedBodyTarget\?\.memberName\s*\?\? overload\.name,\s*memberSignature: overload\.signature,\s*selectorKey: implementationBody\?\.selectorKey\s*\?\? state\.selectedBodyTarget\?\.selectorKey\s*\?\? overload\.graphSelectorKey,\s*metadataToken: implementationMetadataToken,\s*implementationBodySelected,\s*isCurrent: \(\) => memberRequestIsCurrent\(signature, true\)/);
  assert.match(
    factsSurfaceLoader,
    /Promise\.all\(\[\s*loadSelectedMemberFacts\(\),\s*loadSelectedMemberAnnotatedSource\(\),\s*\]\)/);
  assert.equal(
    [...appSource.matchAll(/loadSelectedMemberFactsSurface\(\)/g)].length,
    4);
  assert.match(
    annotatedAction,
    /case "source-select":[\s\S]*?const next = selectAnnotatedNode\(session, node\.id\);\s*setSession\(next\);\s*syncFindingSelectionFromAnnotatedSession\(next\)/);
  assert.match(
    packageAcquisitionSource,
    /implementationBody\?: InspectedMemberBodySelector/);
  assert.match(
    packageAcquisitionSource,
    /function retainGraphOnlyImplementationBody[\s\S]*overload\.bodySelectors\.find\([\s\S]*overload\.implementationBody = selectedBody;[\s\S]*graphMemberTargetWithSelectedBody\(target, selectedBody\)/);
  assert.match(
    appSource,
    /const selectedTarget = graphMemberTargetWithSelectedBody\(\s*target,\s*projection\.selectedBody\);[\s\S]*singleProjectedGraphMember\(projection\.type\)[\s\S]*stageGraphMemberSelection\([\s\S]*selectedTarget,[\s\S]*projectedMember\);[\s\S]*commitGraphMemberSelection\([\s\S]*selectedTarget,[\s\S]*staged\)/);
  assert.doesNotMatch(
    factsLoader,
    /state\.selectedBodyTarget\?\.metadataToken \?\? overload\.metadataToken/);
  assert.match(
    factsRenderer,
    /const heapAllocations = facts\.allocations\.filter\(a => a\.countedAsHeap\);\s*const allocOffsets = heapAllocations\.map\(a => a\.offset\)/);
});

test("type source identity includes decompiler taste", () => {
  const typeSignature =
    typePanelSource.match(/export function typeSourceSignature\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(typeSignature, /memberRequestKey\(/);
  assert.match(typeSignature, /taste/);
});

test("source operations cancel when superseded or hidden", () => {
  assert.match(
    generatedFacadeSource("inspect-web-source"),
    /\$requireManagedExports\(\)\["DotnetInspect"\]\["Web"\]\["Interop"\]\["Source"\]\["SourceExports"\]\["CancelSourceQuery\.-?\d+"\]/);
  assert.match(
    generatedFacadeSource("inspect-web-source"),
    /export function cancelSourceQuery\(\)[\s\S]*?return \$requireManagedExports\(\)/);
  assert.match(
    appSource,
    /cancelSourceQuery: cancelSourceInspection/);
  assert.match(
    appSource,
    /const operationAuthority = createOperationAuthorityPage\(\);[\s\S]*createSourceInspectionCoordinator\(\{[\s\S]*operationAuthority,/);

  assert.match(
    appSource,
    /function renderCore\(options: \{ synchronizeUrl\?: boolean \}\) \{\s*sourceInspection\.cancelHiddenRequest\(\)/);
  assert.match(
    appSource,
    /createSourceInspectionCoordinator\(\{[\s\S]*memberSourceHasConcreteOverload,[\s\S]*cancelEngineSourceRequest: \(\) => \{[\s\S]*observeAsync\(\s*cancelSourceInspection\(\),\s*"Cancelling the Source request"\)/);
  assert.match(
    sourceInspectionSource,
    /const cancelCurrentRequest = \(\) => \{[\s\S]*cancelMemberSourceRequest\(\)[\s\S]*cancelHiddenRequest\(\)[\s\S]*sourceSurfaceIsVisible\(\s*state,\s*dependencies\.memberSourceHasConcreteOverload\(\)\)[\s\S]*cancelCurrentRequest\(\)/);
  assert.match(
    sourceInspectionSource,
    /dependencies\.operationAuthority\.createSession\([\s\S]*typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.match(appSource, /sourceInspection\.loadMemberSource\(\{/);
  assert.match(appSource, /sourceInspection\.loadTypeSource\(\{/);
  assert.match(appSource, /sourceInspection\.openGraphSource\(request, title\)/);
  assert.match(appSource, /sourceInspection\.closeGraphSource\(\)/);
  const reloadBody =
    appSource.match(/function reloadVisibleSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(reloadBody, /switch \(currentSourceReloadKind\(\)\)/);
  const autoLoadBody =
    appSource.match(
      /function maybeAutoLoadVisibleSource\(\)[\s\S]*?\n}\n\nfunction maybeAutoLoadTypeMetadata/)?.[0]
    ?? "";
  assert.match(
    autoLoadBody,
    /const kind = currentSourceOperationKind\(\)/);
  assert.match(autoLoadBody, /kind === "type"/);
  assert.match(autoLoadBody, /kind === "member"/);
  assert.match(autoLoadBody, /kind === "graph"/);
  assert.match(autoLoadBody, /loadSelectedTypeSource\(\)/);
  assert.match(autoLoadBody, /loadSelectedMemberSource\(\)/);
  assert.match(autoLoadBody, /sourceResultNeedsLoad\(state\.typeSource, signature\)/);
  assert.match(autoLoadBody, /sourceResultNeedsLoad\(state\.memberSource, signature\)/);
  assert.match(
    autoLoadBody,
    /graphSourceAutoLoadRequest\(state\.graphSource\)/);
  assert.match(autoLoadBody, /openGraphSource\(/);
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  assert.match(
    annotatedLoader,
    /return memberDetailInspection\.loadFindingCensus\(\{/);
  assert.doesNotMatch(
    annotatedLoader,
    /sourceRequestNeedsLoad|memberAnnotatedLoading/);
  assert.match(
    memberDetailInspectionSource,
    /async loadFindingCensus\(request\)[\s\S]*sourceRequestNeedsLoad\([\s\S]*state\.memberAnnotatedLoading[\s\S]*state\.memberAnnotatedError/);

  // Annotating the fixture contextually types `lens` and `memberSection` against their
  // literal unions instead of widening them to `string`.
  const visible: SourceWorkbenchState = {
    settings: false,
    explorer: null,
    loading: false,
    error: "",
    home: false,
    package: {},
    atPackageRoot: false,
    graphSource: { status: "closed" },
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview"
  };
  assert.equal(sourceSurfaceIsVisible(visible), true);
  const hiddenOverrides: readonly SourceWorkbenchState[] = [
    { home: true },
    { atPackageRoot: true },
    { atLibraryRoot: true },
    { settings: true },
    { loading: true },
    { error: "failed" },
    { explorer: { open: true } },
    { package: null }
  ];
  for (const hidden of hiddenOverrides) {
    assert.equal(sourceSurfaceIsVisible({ ...visible, ...hidden }), false);
  }
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      atPackageRoot: true,
      graphSource: { status: "ready" }
    }),
    "graph");
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      atPackageRoot: true
    }),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }),
    "annotated");
  assert.equal(
    sourceReloadKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "facts"
    }),
    "annotated");
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "source"
    }, false),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }, false),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      settings: true,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }),
    null);
  assert.equal(
    sourceRequestNeedsLoad(true, false, null, ""),
    true);
  assert.equal(
    sourceRequestNeedsLoad(true, true, null, ""),
    false);
  assert.equal(
    sourceRequestNeedsLoad(true, false, { text: "source" }, ""),
    false);
  assert.equal(
    sourceRequestNeedsLoad(true, false, null, "failed"),
    false);
  assert.equal(
    sourceRequestNeedsLoad(false, true, { text: "stale" }, ""),
    true);

  const failed = {
    status: "failed",
    signature: "member",
    error: "",
  } as const;
  assert.equal(sourceResultNeedsLoad(failed, "member"), false);
  assert.equal(sourceResultNeedsLoad(failed, "other"), true);
  assert.deepEqual(
    normalizeSourceResultSnapshot({
      status: "loading",
      signature: "member",
    }),
    { status: "idle" });
});

test("browser consumer explicitly sequences same-origin host configuration", () => {
  // The coordinator owns composition: every facade initializes, in order, before host policy
  // is configured and the one entry point runs.
  for (const module of generatedFacadeModules) {
    assert.match(
      engineCoordinatorSource,
      new RegExp(`import\\("/${module}\\.js"\\)`),
      `the coordinator does not compose /${module}.js`);
  }
  assert.match(
    engineCoordinatorSource,
    /const runtime = host\.createRuntime\(\);/,
    "the coordinator must create the shared runtime through the host facade");
  // Every facade receives the same runtime promise, one after another, before readiness
  // resolves.
  assert.deepEqual(
    [...engineCoordinatorSource.matchAll(
      /await (\w+)\.initializeRuntime\(runtime\);/g)]
      .map(match => match[1]),
    [
      "host",
      "packageFacade",
      "libraryFacade",
      "metadataFacade",
      "analysisFacade",
      "sourceFacade",
      "callGraphFacade",
      "catalogFacade",
    ]);
  assert.match(
    engineCoordinatorSource,
    /readiness \?\?= initializeFacadeSet\(\);/);
  assert.match(
    engineCoordinatorSource,
    /await initializeFacades\(\);[\s\S]*host\.configureHost\(origin\);[\s\S]*await host\.runEntryPoint\(\);/);
  assert.match(
    engineCoordinatorSource,
    /startup \?\?= startEngineCore\(origin\);/);
  // Only the host facade's entry point runs, and no other module's does.
  assert.deepEqual(
    [...engineCoordinatorSource.matchAll(/(\w+)\.runEntryPoint\(\)/g)]
      .map(match => match[1]),
    ["host"]);
  assert.match(
    appSource,
    /await startEngine\(window\.location\.origin\);/);
  assert.doesNotMatch(generatedFacadeSourceText, /\bwindow\b/);
});

test("every generated browser engine module is syntactically valid", () => {
  for (const module of generatedFacadeModules) {
    const modulePath = fileURLToPath(generatedFacadeModuleUrls.get(module)!);
    const result = spawnSync(
      process.execPath,
      ["--check", modulePath],
      { encoding: "utf8" });
    assert.equal(
      result.status,
      0,
      `${modulePath} failed syntax validation:\n${result.stderr}`);
  }
});

test("generated source wrappers parse their JSON envelopes", () => {
  const wrapper = (name: string) => {
    const pattern = new RegExp(`\\nexport (?:async )?function ${name}\\(`);
    const owners = generatedFacadeModules
      .filter(module => pattern.test(generatedFacadeSource(module)));
    assert.equal(owners.length, 1,
      `${name} must be published by exactly one facade; found ${owners.join(", ")}`);
    const source = generatedFacadeSource(owners[0]!);
    const start = source.search(pattern);
    const end = source.indexOf("\nexport ", start + 1);
    return source.slice(start, end < 0 ? undefined : end);
  };

  for (const name of [
    "queryMemberAnnotatedSource",
    "queryMemberFindingCensus",
    "queryMemberFacts",
    "queryMemberSource",
    "queryTypeMemberSource",
  ]) {
    assert.match(
      wrapper(name),
      /const \$parsed = JSON\.parse\(\$result\);[\s\S]*return \$parsed;/);
  }
});

test("MethodDef-only member sections are hidden for bodiless APIs", () => {
  for (const kind of ["property", "field", "event", "constant"]) {
    // Compare stays: its Diff mode needs no body, and its Clone mode reports
    // the missing body itself. Runtime packs have no Compare at all.
    assert.deepEqual(
      memberSectionIdsFor({ kind }),
      ["overview", "compare"]);
    assert.deepEqual(
      memberSectionIdsFor({ kind }, true),
      ["overview"]);
  }
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }),
    ["overview", "call-graph", "facts", "source", "annotated", "compare"]);
});

// Arrowing between members keeps the active section (e.g. Source) sticky, the same way
// arrowing between types never disturbs the type-level lens. openMemberGroup/openOverload
// (the two entry points arrow-key nav uses) must clear cached per-member content without
// resetting memberSection, and only fall back to Overview when the newly selected member
// doesn't support the section that was showing.
test("moving between members keeps the active section sticky, falling back to Overview only when unsupported", () => {
  const openMemberGroupBody =
    appSource.match(/function openMemberGroup\(key: string\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(openMemberGroupBody, /clearMemberContentCache\(\)/);
  assert.doesNotMatch(openMemberGroupBody, /resetMemberSectionState\(\)/);
  assert.match(
    openMemberGroupBody,
    /const preserveSection =\s*state\.memberBrowseTypeId === type\?\.id && Boolean\(state\.selectedMemberKey\)/);
  assert.match(
    openMemberGroupBody,
    /state\.selectedBodyTarget = graphOnlyTarget;[\s\S]*if \(!preserveSection\) \{\s*state\.memberSection = "overview"/);
  assert.match(
    openMemberGroupBody,
    /state\.memberSection !== "overview"[\s\S]*group\.overloads\.length > 1[\s\S]*state\.selectedOverloadIndex = 0;[\s\S]*retainMemberSectionIfSupported\(group\)/);
  assert.match(
    openMemberGroupBody,
    /const retainedSection = state\.memberSection;[\s\S]*let selectedFirstOverload = false;[\s\S]*selectedFirstOverload = true;[\s\S]*if \(selectedFirstOverload && state\.memberSection !== retainedSection\) \{\s*state\.selectedOverloadIndex = null;\s*state\.selectedBodyTarget = null/);
  assert.match(openMemberGroupBody, /loadMemberSectionContent\(state\.memberSection\)/);

  const openOverloadBody =
    appSource.match(/function openOverload\(index: number\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(openOverloadBody, /clearMemberContentCache\(\)/);
  assert.doesNotMatch(openOverloadBody, /resetMemberSectionState\(\)/);
  assert.match(
    openOverloadBody,
    /state\.selectedBodyTarget = graphTarget;[\s\S]*retainMemberSectionIfSupported\(selectedMember\(selectedType\(\)\)\)/);
  assert.match(openOverloadBody, /loadMemberSectionContent\(state\.memberSection\)/);

  const retainBody =
    appSource.match(/function retainMemberSectionIfSupported\([\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(retainBody, /memberSectionsFor\(member\)/);
  assert.match(retainBody, /state\.memberSection = "overview"/);

  const resetBody =
    appSource.match(/function resetMemberSectionState\(\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(resetBody, /state\.memberSection = "overview"/);
  assert.match(resetBody, /clearMemberContentCache\(\)/);

  const selectEntryBody =
    appSource.match(/function selectMemberNavEntry\([\s\S]*?\n}\n\nfunction stepMemberNav/)?.[0]
    ?? "";
  assert.match(
    selectEntryBody,
    /entry\.group\.key === state\.selectedMemberKey[\s\S]*entry\.group\.overloads\.length === 1[\s\S]*state\.selectedOverloadIndex = null;\s*clearMemberContentCache\(\);\s*render\(\)/);
});

test("every overload-specific member loader leaves a multi-overload picker inert", () => {
  for (const name of [
    "loadSelectedMemberDocumentation",
    "loadSelectedMemberSource",
    "loadSelectedMemberAnnotatedSource",
    "loadSelectedMemberCallGraph",
    "loadSelectedMemberFacts",
  ]) {
    const body =
      appSource.match(new RegExp(`async function ${name}\\(\\)[\\s\\S]*?\\n}`))?.[0]
      ?? "";
    assert.match(body, /selectedConcreteOverload\(member\.overloads, state\.selectedOverloadIndex\)/);
    assert.match(body, /if \(!overload\) \{\s*render\(\);\s*return;\s*}/);
    assert.doesNotMatch(body, /selectedOverloadIndex \?\? 0/);
  }
});

// `memberSectionIdsFor` is the admission set for the member strip, for the URL `?section=`
// token, and for the share packet's `c` token, so a section the catalog defines but this
// function omits is defined and never reachable. It used to restate the roster, which the
// compiler could not check in that direction. This is the gate for it deriving instead:
// restoring a hand-written list makes a catalog addition stop appearing here.
test("the full member-section roster is derived from the catalog, not restated", () => {
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method", overloads: [{}, {}] }),
    memberSectionDefinitions.map(([id]) => id));
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method", overloads: [{}] }),
    memberSectionDefinitions
      .map(([id]) => id)
      .filter(id => id !== "implementation-profiles"));
});

test("source requests carry exact type and member identities", () => {
  const memberBridge =
    generatedFacadeSource("inspect-web-source")
      .match(/export async function queryMemberSource\([\s\S]*?\n}/)?.[0]
    ?? "";
  const memberLoader =
    appSource.match(/async function loadSelectedMemberSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    memberBridge,
    /typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson/);
  assert.match(
    memberLoader,
    /type\.definitionId \?\? type\.id,[\s\S]*?state\.selectedBodyTarget\?\.memberName[\s\S]*?state\.selectedBodyTarget\?\.selectorKey[\s\S]*?state\.selectedBodyTarget\?\.metadataToken/);
  assert.doesNotMatch(memberLoader, /signature:/);
});

test("call graph source identity prefers the structured type definition", () => {
  assert.equal(
    callGraphTargetTypeId({
      typeDefinitionId: "Example.Outer\\+Literal",
      typeMetadataId: ""
    }),
    "Example.Outer\\+Literal");
  assert.equal(
    callGraphTargetTypeId({ typeMetadataId: "Example.Legacy" }),
    "Example.Legacy");

  const nested = {
    id: "Example.Outer+Inner",
    definitionId: "Example.Outer+Inner",
    metadataId: "Example.Outer+Inner",
    assembly: "Example"
  };
  const literal = {
    id: "Example.Outer\\+Inner",
    definitionId: "Example.Outer\\+Inner",
    metadataId: "Example.Outer+Inner",
    assembly: "Example"
  };
  const target = {
    assembly: "Example",
    typeDefinitionId: literal.definitionId
  };
  const candidate = resolveLoadedGraphTargetCandidate(
    [{ id: "Example", types: [nested, literal] }],
    target);
  assert.equal(candidate.status, "unique");
  assert.equal(candidate.type, literal);
  assert.equal(callGraphTargetMatchesType(target, nested), false);
  assert.equal(callGraphTargetMatchesType(target, literal), true);
});

test("decompiled source discloses the PDB-source limitation", () => {
  const html = pdbSourceLimitationHtml({
    pdbSourceLimitation: "<img src=x onerror=alert(1)>"
  });
  assert.match(html, /PDB source unavailable:/);
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /&lt;img/);
  assert.match(
    typePanelSource,
    /renderSourceResult[\s\S]*pdbSourceLimitationHtml\(source\)/);
  assert.match(
    appSource,
    /source: source\.source,\s*text: memberSourceText\(source, selectedPart\),/);
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

// A real call-graph node carries more than the identity view in `data.ts` declares:
// `typeFullName` is part of the call-graph facade's own DTO in
// `facades/inspect-web-call-graph.d.ts`. Passing the wider payload is exactly what the
// product does, so the fixtures below keep the field --
// it is what makes "prefers metadata identity over the display name" a real claim -- and
// this widening keeps the excess property check, which only fires on fresh object
// literals, from rejecting it.
