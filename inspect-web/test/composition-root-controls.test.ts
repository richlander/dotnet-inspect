import assert from "node:assert/strict";
import test from "node:test";
import type {
  ImportDeclaration,
} from "oxc-parser";
import {
  combinedGraphTargetNavigationDisposition,
  memberSectionIdsFor,
  mergeInspectionErrorEntries,
  mergeInspectionErrors,
  renderInspectionErrors,
  platformPackForGraphAssembly,
  platformPackFromAcquiredProvenance,
  platformPackFromProvenance,
  platformPackToken,
  resolveRuntimeGraphTargetCandidate,
  runtimePackForFramework,
  typeLensesFor,
} from "../src/data.ts";

import {
  appSource,
  parsedAppSource,
  appSyntax,
  syntaxNodes,
  onlySyntaxNode,
  type DeclaredFunction,
  functionDeclaration,
  onlyCallExpressionNamed,
  objectArgument,
  statementAt,
  assertIdentifierArgument,
  callExpressionsNamed,
  sourceText,
  namedProperty,
  callbackProperty,
  directCallExpression,
  directCallName,
  statementSignatures,
  productionTypeScriptSources,
  workspaceNavigationSource,
  packageAcquisitionSource,
  packageInspectionSource,
  packageViewSource,
  libraryControlsSource,
  shellControlsSource,
  graphInteractionsSource,
  callGraphInspectionSource,
  documentInspectionSource,
  spotlightPackageSearchSource,
  catalogRequestsSource,
  graphSourceViewerSource,
  docViewerSource,
  annotatedSourceModule,
  typePanelSource,
  keybindingRegistrySource,
  workbenchKeybindingsSource,
  scopeBarSource,
  settingsPanelSource,
  packageControlsSource,
  metadataViewerSource,
  packageOpportunitiesSource,
  stylesSource,
  dataBarSource,
  spotlightSource,
  commandBarSource,
} from "./composition-root-test-fixture.ts";
test("shared HTML escaping covers text and attribute delimiters", () => {
  const helper = sourceText(functionDeclaration("escapeHtml"));
  assert.deepEqual(
    helper.match(/\.replaceAll\([^)]*\)/g),
    [
      `.replaceAll("&", "&amp;")`,
      `.replaceAll("<", "&lt;")`,
      `.replaceAll(">", "&gt;")`,
      `.replaceAll('"', "&quot;")`,
      `.replaceAll("'", "&#39;")`,
    ]);
});

test("platform type and member navigation hides package-only operations", () => {
  assert.deepEqual(
    typeLensesFor({ isRuntimePack: true }).map(([id]) => id),
    ["api"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }, true),
    ["overview", "call-graph"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }, false),
    ["overview", "call-graph", "facts", "source", "annotated", "compare"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "property" }, false, true),
    ["overview", "call-graph", "facts", "annotated", "compare"]);
});

test("platform call graphs carry the target pack into lazy acquisition", () => {
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      "aspnetcore.app",
      [],
      [],
      []),
    "aspnetcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      [],
      [],
      []),
    "netcore.app");
  assert.equal(
    platformPackFromAcquiredProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      []),
    null);
  const acquiredRuntime = {
    activeFramework: "net10.0",
    assemblies: [{
      name: "Microsoft.AspNetCore.Http",
      platformPack: "aspnetcore.app",
    }],
  };
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      acquiredRuntime,
      "net10.0"),
    "aspnetcore.app");
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      acquiredRuntime,
      "net9.0"),
    null);
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      null,
      "net10.0"),
    null);
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      [{
        name: "Microsoft.AspNetCore.Http",
        platformPack: "aspnetcore.app"
      }],
      [],
      []),
    "aspnetcore.app");
  assert.match(
    appSource,
    /resolvedPlatformTargetVersion\(\s*captured\.resolvedTabs,\s*runtimePack,\s*framework\)[\s\S]*callGraphInspection\.drill\(\{[\s\S]*platformVersion,/);
  assert.doesNotMatch(
    appSource,
    /platformVersion:\s*currentPackage\(\)\.version/);
  assert.match(
    appSource,
    /platformType:\s*type\.definitionId\s*\?\?\s*type\.metadataId[\s\S]*platformPack:\s*platformPackForAssembly\(type\.assembly,\s*type\.platformPack\)/);
  assert.match(
    callGraphInspectionSource,
    /pack:\s*request\.platformPack/);
  assert.match(
    appSource,
    /queryPlatformCallGraph\(inspectExpandPlatformCallGraph, request\)/);
  assert.match(
    callGraphInspectionSource,
    /return query\(\s*request\.framework,\s*request\.platformVersion,\s*request\.assembly,\s*request\.pack/);
});

test("platform pack inference rejects cross-family ambiguity", () => {
  assert.throws(
    () => platformPackFromProvenance(
      "Shared",
      null,
      [
        { name: "Shared", platformPack: "netcore.app" },
        { name: "Shared", platformPack: "aspnetcore.app" }
      ],
      [],
      []),
    /available from multiple platform packs/);
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      null,
      [{ name: "Shared", platformPack: "netcore.app" }],
      [{ assembly: "Shared", pack: "aspnetcore.app" }],
      [{ assembly: "Shared", pack: "aspnetcore.app" }]),
    "netcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      null,
      [],
      [{ assembly: "Shared", pack: "aspnetcore.app" }],
      [{ assembly: "Shared", pack: "netcore.app" }]),
    "netcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      "aspnetcore.app",
      [{ name: "Shared", platformPack: "netcore.app" }],
      [],
      [{ assembly: "Shared", pack: "aspnetcore.app" }]),
    "aspnetcore.app");
  assert.match(
    appSource,
    /const resident = runtimeAssemblyIsResident\(\s*originPackage,\s*row\.assembly,\s*row\.pack\)/);
});

test("runtime graph acquisition ignores a resident pack from another TFM", () => {
  const stale = {
    id: "Microsoft.NETCore.App",
    activeFramework: "net8.0",
    isRuntimePack: true,
    assemblies: [{
      id: "console",
      name: "System.Console",
      version: "8.0.0.0",
      culture: null,
      publicKeyToken: "b03f5f7f11d50a3a"
    }],
    types: [{
      id: "System.Console:System.Console",
      definitionId: "System.Console",
      assemblyId: "console",
      assemblyName: "System.Console"
    }]
  };
  const net9Target = {
    assembly: "System.Console",
    assemblyVersion: "9.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "System.Console",
    kind: "external"
  };

  const usable = runtimePackForFramework(stale, "net9.0");
  assert.equal(usable, null);
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      usable
        ? resolveRuntimeGraphTargetCandidate(usable, net9Target)
        : null,
      net9Target),
    "platform");

  const matching = runtimePackForFramework(stale, "NET8.0");
  assert.equal(matching, stale);
  assert.equal(
    resolveRuntimeGraphTargetCandidate(
      matching,
      { ...net9Target, assemblyVersion: "8.0.0.0" }).status,
    "unique");
  assert.equal(
    appSource.match(
      /const pack = runtimePackForFramework\(\s*runtimePackPackage\(\),\s*platformCatalogFramework\(state\.package\?\.activeFramework \|\| ""\)\)/g)?.length,
    2);
  assert.match(
    appSource,
    /let pack = runtimePackForFramework\(\s*runtimePackPackage\(\),\s*framework\)/);
});

test("platform library selection remains distinct from canonical Platform identity", () => {
  assert.equal(platformPackToken("aspnetcore.app"), "aspnetcore.app");
  assert.equal(platformPackToken("netcore.app"), "netcore.app");
  assert.equal(platformPackToken("unknown.app"), null);
  assert.match(
    workspaceNavigationSource,
    /tab\.kind === "group" && tab\.source === ":Platform"/);
  assert.match(
    workspaceNavigationSource,
    /id: "Microsoft\.NETCore\.App"/);
  assert.match(
    appSource,
    /platformLibraryKey\(row\) === assembly[\s\S]*target\.tfm, platformAssemblyRequest\(row\), row\.pack/);
});

test("platform inspection notices survive cumulative surface loads", () => {
  assert.equal(
    mergeInspectionErrors("", "System.Synthetic: omitted 1 metadata row."),
    "System.Synthetic: omitted 1 metadata row.");
  assert.equal(
    mergeInspectionErrors(
      "First: omitted 1 metadata row.",
      "Second: omitted 2 metadata rows."),
    "First: omitted 1 metadata row.; Second: omitted 2 metadata rows.");
  assert.equal(
    mergeInspectionErrors(
      "First: omitted 1 metadata row.",
      "First: omitted 1 metadata row."),
    "First: omitted 1 metadata row.");
  assert.equal(
    mergeInspectionErrors(
      "First: truncated; 0 assemblies were not projected.",
      "Second: omitted 2 metadata rows."),
    "First: truncated; 0 assemblies were not projected.; "
      + "Second: omitted 2 metadata rows.");
  const entries = mergeInspectionErrorEntries(
    [
      "First: truncated; 0 assemblies were not projected.",
      "Second: truncated; 0 assemblies were not projected.",
    ],
    ["Second: truncated; 0 assemblies were not projected."]);
  assert.deepEqual(entries, [
    "First: truncated; 0 assemblies were not projected.",
    "Second: truncated; 0 assemblies were not projected.",
  ]);
  assert.equal(
    renderInspectionErrors(entries),
    "First: truncated; 0 assemblies were not projected.; "
      + "Second: truncated; 0 assemblies were not projected.");
  assert.match(
    packageAcquisitionSource,
    /existing\.inspectionErrors\s*=\s*mergeInspectionErrorEntries\(/);
  assert.match(
    packageAcquisitionSource,
    /inspectionError:\s*renderInspectionErrors\(inspectionErrors\)/);
});

test("typed Spotlight owns search presentation and hosts commands", () => {
  assert.match(
    appSource,
    /createSpotlight,[\s\S]*from "\.\/spotlight\.ts"/);
  assert.match(
    appSource,
    /createSpotlightPackageSearch,[\s\S]*visibleSpotlightPackageHits,[\s\S]*from "\.\/spotlight-package-search\.ts"/);
  assert.match(appSource, /openSpotlight\("", "commands"\)/);
  assert.match(appSource, /state\.spotlightOpen \? spotlight\.modalHtml\(\)/);
  assert.match(
    appSource,
    /spotlight\.inlineHtml\(enginePending, showReadyGlint\)/);
  assert.doesNotMatch(appSource, /function renderSpotlight\(/);
  assert.doesNotMatch(appSource, /commandBar\.html\(\)/);
  assert.match(spotlightSource, /const COMMAND_SCOPE = \{ id: "commands"/);
  assert.match(
    spotlightSource,
    /type HighlightRange = readonly \[start: number, end: number\]/);
  assert.match(spotlightSource, /function handleModalKeys\(event: KeyboardEvent\)/);
  assert.match(commandBarSource, /export function commandPaletteResults\(/);
});

test("workspace data bar receives package acquisition provenance", () => {
  assert.match(
    appSource,
    /createPackageAcquisition\(\{[\s\S]*queryPackage:[\s\S]*loadRuntimePack:[\s\S]*loadRuntimePackAssembly:/);
  assert.match(appSource, /packageAcquisition\.loadPackage\(\{/);
  assert.match(appSource, /packageAcquisition\.loadRuntimePack\(/);
  assert.match(appSource, /packageAcquisition\.loadRuntimePackAssembly\(/);
  assert.doesNotMatch(appSource, /runtimePackLoadPromise|waitForRuntimePackLoad/);
  assert.match(
    appSource,
    /interface RuntimeLoadResult \{[\s\S]*failureMessage: string;[\s\S]*const result = await packageAcquisition\.loadRuntimePack\([\s\S]*failureMessage: result\.error === null \? "" : errorMessage\(result\.error\)/);
  assert.match(packageAcquisitionSource, /source: \{ kind: "nuget\.org" \}/);
  assert.match(packageAcquisitionSource, /source: \{ kind: "platform" \}/);
  assert.match(packageAcquisitionSource, /producerLabel: "NuGet\.org"/);
  assert.match(packageAcquisitionSource, /producerLabel: "Platform"/);
  assert.doesNotMatch(appSource, /source: \{ kind: "(?:nuget\.org|platform)" \}/);
  assert.match(
    appSource,
    /producer: \{\s*kind: pkg\.source\.kind === "platform"\s*\|\| state\.rootKind === "library"\s*\? "acquisition"\s*: "package",\s*label: pkg\.producerLabel,\s*\}/);
  assert.match(
    dataBarSource,
    /const producerLabel = model\.producer\?\.label\.trim\(\) \?\? ""/);
  assert.doesNotMatch(dataBarSource, /new URL|URLSearchParams|\.split\(/);
});

test("uploaded Libraries remain transient closed-world subjects", () => {
  const open =
    appSource.match(
      /async function openUploadedLibraryFile\([\s\S]*?\n}\n\nbindLibraryOpenDocument/)?.[0]
    ?? "";
  const memberDocumentation =
    appSource.match(
      /async function loadSelectedMemberDocumentation\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberSource/)?.[0]
    ?? "";
  const libraryOverview =
    appSource.match(
      /function renderLibraryOverview\(\)[\s\S]*?\n}\n\nfunction renderGraphMemberPendingHtml/)?.[0]
    ?? "";

  assert.match(
    open,
    /const operationSequence = \+\+libraryOpenSequence;\s*const navigationSeq = navigationSequence\.begin\(\);\s*const isCurrent = \(\) =>\s*operationSequence === libraryOpenSequence\s*&& navigationSequence\.isCurrent\(navigationSeq\);[\s\S]*state\.libraryOpen = true;[\s\S]*await waitForLibraryEngineReady\(\);[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*file\.arrayBuffer\(\)[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*inspectOpenUploadedLibrary\(file\.name, content\);[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*const retainedSnapshot = retainedWorkspaces\.activeWorkspaceId === null\s*\? null\s*: captureRetainedWorkspaceSnapshot\(\);[\s\S]*createUploadedLibraryModel\(inspection\.content\)[\s\S]*activatePackage\(packageModel, \{ resetAccessibility: true \}\)[\s\S]*state\.uploadedLibrary = packageModel[\s\S]*state\.rootKind = "library"[\s\S]*detachActiveRetainedWorkspace\([\s\S]*retainedSnapshot\);[\s\S]*activeWorkspaceUrl = null;\s*activatedLibrary = true;\s*workspaceLocation\.replace\("\/"\)/);
  assert.doesNotMatch(
    open,
    /retainPackageModel|state\.packages|syncUrl|workspaceShareBasis/);
  assert.match(
    appSource,
    /else if \(state\.rootKind !== "library"\s*&& options\.synchronizeUrl !== false\) \{\s*syncUrl\(\);/);
  assert.match(
    appSource,
    /if \(state\.rootKind !== "library"\) \{\s*maybeAutoLoadVisibleSource\(\);[\s\S]*maybeAutoLoadPackageMetadata\(\);\s*\}/);
  assert.match(
    memberDocumentation,
    /if \(state\.rootKind === "library"\) \{\s*render\(\{ synchronizeUrl: false \}\);\s*return;\s*\}/);
  assert.match(
    libraryOverview,
    /if \(state\.rootKind === "library" \|\| pkg\.isRuntimePack\) \{\s*return renderLibraryCompositionOverview\(pkg, library\);\s*\}/);
});

test("data bar has no expansion state or interaction binding", () => {
  assert.doesNotMatch(appSource, /statusBarExpanded|bindStatusBarEvents/);
  assert.doesNotMatch(
    `${appSource}\n${dataBarSource}`,
    /data-status-bar-toggle|status-bar-toggle|aria-expanded/);
  assert.doesNotMatch(dataBarSource, /addEventListener|querySelector/);
});

test("typed package controls own framework and version selection bindings", () => {
  const packageControlsCreation =
    appSource.match(/const packageControls = createPackageControls\(\{[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const packageControlsBinding =
    packageControlsSource.match(/  function bind\(root: ParentNode\): void \{[\s\S]*?\n  }(?=\n\n  return)/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    packageControlsCreation,
    /selectFramework: \(framework, source\) => \{[\s\S]*contentFrameUsesPush\(\)[\s\S]*contentFramePane = "detail"[\s\S]*observeAsync\(\s*switchPackageFramework\(\s*framework,\s*source === "legacy" \? "framework" : "package-framework"\),\s*"Switching the package framework"\);[\s\S]*selectVersion: version => \{[\s\S]*state\.package\?\.isRuntimePack[\s\S]*observeAsync\(\s*switchPlatformVersion\(version\),\s*"Switching the platform version"\);[\s\S]*else\s*observeAsync\(\s*switchPackageVersion\(version\),\s*"Switching the package version"\)/);
  assert.match(
    packageControlsSource,
    /export function bindPackageSelections\([\s\S]*data-package-framework[\s\S]*#framework[\s\S]*#package-version/);
  assert.match(
    appSource,
    /function packageVersionField\(\)[\s\S]*id="package-version"/);
  assert.doesNotMatch(
    appSource,
    /function packageFrameworkField|function packageCoordinateFields/);
  assert.match(
    packageControlsBinding,
    /bindPackageSelections\(root, \{\s*onFrameworkSelect: selectFramework,\s*onVersionSelect: selectVersion,\s*\}\)/);
  assert.equal(
    workspaceBinding.match(/\bpackageControls\.bind\(document\)/g)?.length,
    1);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelectorAll(?:<HTMLElement>)?\("\[data-framework-chip\]"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelector(?:<HTMLSelectElement>)?\("#(?:framework|package-version)"\)/);
});

test("explicit coordinate changes discard a floating canonical basis", () => {
  const packageVersion = appSource.match(
    /async function switchPackageVersion\([\s\S]*?\n}/)?.[0] ?? "";
  const packageFramework = appSource.match(
    /async function switchPackageFramework\([\s\S]*?\n}/)?.[0] ?? "";
  const platformVersion = appSource.match(
    /async function switchPlatformVersion\([\s\S]*?\n}/)?.[0] ?? "";
  const packageLoader = appSource.match(
    /async function loadPackage\([\s\S]*?\n}(?=\n\nfunction )/)?.[0] ?? "";

  assert.match(packageVersion, /invalidateWorkspaceShareBasis: true/);
  assert.match(packageFramework, /invalidateWorkspaceShareBasis: true/);
  assert.match(
    packageLoader,
    /if \(options\.invalidateWorkspaceShareBasis\)\s*state\.workspaceShareBasis = null;\s*activatePackage/);
  assert.match(
    platformVersion,
    /if \(!loaded\)[\s\S]*return;[\s\S]*state\.workspaceShareBasis = null;[\s\S]*activatePackage/);
  assert.doesNotMatch(
    platformVersion.slice(0, platformVersion.indexOf("await loadRuntimePack(")),
    /state\.packages =|state\.libraryScope = null|state\.platformStack = \[\]/);
  assert.match(
    platformVersion,
    /if \(!loaded\)[\s\S]*appendQueryNotice\([\s\S]*render\(\);\s*return;/);
  assert.match(
    platformVersion,
    /state\.workspaceShareBasis = null;\s*state\.platformStack = \[\];\s*activatePackage[\s\S]*const firstLibrary = packageLibraries\(\)\[0\];\s*state\.libraryScope = firstLibrary \? new Set\(\[firstLibrary\.id\]\) : null/);
});

test("typed package inspection owns package-root request coordination", () => {
  const dependenciesLoader =
    appSource.match(/async function loadPackageDependencies\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createPackageInspectionCoordinator\(\{[\s\S]*queryDependencies:[\s\S]*queryPackageIntegrations:[\s\S]*queryPlatformMetadata:/);
  assert.match(
    appSource,
    /createPackageInspectionCoordinator\(\{[\s\S]*render: renderPreservingMemberFocus,[\s\S]*renderDependencyGraph,/);
  assert.match(
    appSource,
    /queryDependencies: packageModel => inspectPackageDependencies\(\s*packageModel\.id,\s*packageModel\.version,\s*packageModel\.activeFramework,\s*packageModel\.assemblyId\)/);
  for (const engine of [
    "inspectPackageIntegrations",
    "inspectPackageOpportunities",
    "inspectPackagePerformance",
    "inspectPackageMetadata",
  ]) {
    assert.match(
      appSource,
      new RegExp(
        `${engine}\\(\\s*packageModel\\.id,\\s*`
        + "packageModel\\.version,\\s*packageModel\\.activeFramework,\\s*library\\)"));
  }
  for (const engine of [
    "inspectPlatformIntegrations",
    "inspectPlatformOpportunities",
    "inspectPlatformPerformance",
    "inspectPlatformMetadata",
  ]) {
    assert.match(
      appSource,
      new RegExp(
        `${engine}\\(\\s*framework,\\s*platformVersion,\\s*`
        + "assemblyFileName,\\s*pack\\)"));
  }
  assert.match(
    dependenciesLoader,
    /async function loadPackageDependencies\(\) \{[\s\S]*return packageInspection\.loadDependencies\(/);
  assert.match(appSource, /packageInspection\.ensureWorkspaceDependencies\(\)/);
  assert.match(appSource, /packageInspection\.loadIntegrations\(/);
  assert.match(appSource, /packageInspection\.loadOpportunities\(/);
  assert.match(appSource, /packageInspection\.loadPerformance\(/);
  assert.match(appSource, /packageInspection\.loadMetadata\(/);
  assert.match(
    packageInspectionSource,
    /async loadDependencies\(packageModel, signature\)[\s\S]*state\.packageDependenciesKey/);
  assert.doesNotMatch(dependenciesLoader, /state\.packageDependenciesKey/);
});

test("typed package view owns package navigation bindings", () => {
  const binding =
    appSource.match(/const packageViewActions: PackageViewBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const packageViewBinding =
    appSource.match(/function bindPackageViewEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindPackageDependencyListEvents)/)?.[0]
    ?? "";
  const dependencyListBinding =
    appSource.match(/function bindPackageDependencyListEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const dependencyPatch =
    appSource.match(/function patchDependenciesGroup\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  const actionSource = (name: string) =>
    binding.match(
      new RegExp(`  ${name}: [\\s\\S]*?(?=\\n  on[A-Z])`))?.[0]
      ?? "";
  assert.match(
    packageViewSource,
    /export function bindPackageView\([\s\S]*\[data-dep-group\][\s\S]*\[data-kind-jump\][\s\S]*\[data-namespace-jump\][\s\S]*\[data-lib-scope\][\s\S]*\[data-graph-type\][\s\S]*\[data-perf-selector\]/);
  assert.match(
    packageViewSource,
    /export function bindPackageDependencyList\([\s\S]*\[data-dep-open\][\s\S]*\[data-dep-load\]/);
  assert.equal(
    workspaceBinding.match(/\bbindPackageViewEvents\(\)/g)?.length,
    1);
  assert.match(
    packageViewBinding,
    /bindPackageView\(document, packageViewActions\)/);
  assert.doesNotMatch(
    packageViewBinding,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    dependencyListBinding,
    /bindPackageDependencyList\(document, packageViewActions\)/);
  assert.doesNotMatch(
    dependencyListBinding,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    dependencyPatch,
    /listSection\.outerHTML = dependencyListSectionHtml[\s\S]*bindPackageDependencyListEvents\(\);[\s\S]*renderDependencyGraph\(\)/);
  assert.match(
    binding,
    /onDependencyGroupSelect: index => \{[\s\S]*state\.dependenciesGroupIndex === index[\s\S]*state\.dependenciesGroupIndex = index;[\s\S]*patchDependenciesGroup\(\)/);
  assert.match(
    binding,
    /onDependencyLoad: \(id, version\) =>\s*observeAsync\(\s*openDependencyPackage\(id, version\),\s*"Opening a dependency package"\),\s*onDependencyOpen: switchToPackageForDependencies,\s*onGraphTypeSelect: navigateToTypeByName/);
  const kindJump = actionSource("onKindJump");
  const libraryJump = actionSource("onLibraryScopeSelect");
  const namespaceJump = actionSource("onNamespaceJump");
  assert.match(
    kindJump,
    /state\.atPackageRoot = false;[\s\S]*state\.kindFilter = kind;[\s\S]*state\.namespaceFilter = ""/);
  assert.match(
    libraryJump,
    /if \(!library\) return;[\s\S]*if \(!selectLibrarySubject\(library\)\) return;[\s\S]*if \(kind\) \{\s*state\.atLibraryRoot = false;\s*state\.kindFilter = kind;/);
  assert.match(
    appSource,
    /function selectLibrarySubject\([\s\S]*preserveLens\?: boolean[\s\S]*state\.atPackageRoot = false;[\s\S]*state\.atLibraryRoot = true;[\s\S]*state\.libraryScope = new Set\(\[library\.id\]\);[\s\S]*if \(!options\.preserveLens\) state\.libraryLens = "overview";[\s\S]*normalizeLibrarySelection\(\);[\s\S]*state\.package\?\.isRuntimePack[\s\S]*recordPlatformRecent\(/);
  assert.match(
    appSource,
    /function bindLibrarySubjectNavEvents\(\) \{[\s\S]*selectAggregateLibrarySubject\(\{ preserveLens: true \}\)[\s\S]*selectLibrarySubject\(id, \{ preserveLens: true \}\)/);
  assert.match(
    appSource,
    /function enterMemberScope\([\s\S]*preserveAggregate\?: boolean[\s\S]*options\.preserveAggregate \?\? aggregateLibrarySubjectIsActive\(\);[\s\S]*if \(!preserveAggregate\)\s*state\.libraryScope = new Set\(\[libraryKey\(type\)\]\);/);
  assert.match(
    appSource,
    /function enterRetainedLibrarySubject\([\s\S]*state\.libraryScope === null[\s\S]*selectAggregateLibrarySubject\(options\)[\s\S]*selectLibrarySubject\(selectedLibrary\(\)\?\.id \?\? "", options\)/);
  assert.match(
    appSource,
    /function drillIn\(\)[\s\S]*if \(state\.atPackageRoot\) \{\s*if \(!enterRetainedLibrarySubject\(\)\) return;/);
  assert.match(
    appSource,
    /async function pickSpotlightMember[\s\S]*navigationPreservesAggregateLibraryScope\(pkg\)[\s\S]*enterTypeSubject\(type, \{ preserveAggregate \}\)[\s\S]*enterMemberScope\(\{ preserveAggregate \}\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*navigationPreservesAggregateLibraryScope\(pkg\)[\s\S]*enterTypeSubject\(type, \{ preserveAggregate \}\)/);
  for (const spotlightEntry of [
    appSource.match(/async function pickSpotlightMember[\s\S]*?\n}/)?.[0] ?? "",
    appSource.match(/async function pickSpotlight\([\s\S]*?\n}/)?.[0] ?? "",
  ]) {
    assert.doesNotMatch(
      spotlightEntry,
      /state\.libraryScope = new Set\(\[libraryKey\(type\)\]\)/);
  }
  assert.match(
    namespaceJump,
    /state\.atPackageRoot = false;[\s\S]*state\.namespaceFilter = namespace;[\s\S]*state\.kindFilter = ""/);
  for (const source of [kindJump, namespaceJump]) {
    assert.match(
      source,
      /state\.typeFilter = "";[\s\S]*state\.selectedMemberKey = "";[\s\S]*state\.memberBrowseTypeId = "";[\s\S]*resetMemberFilters\(\);[\s\S]*state\.typeCursor = 0;[\s\S]*const first = filteredTypes\(\)\[0\];[\s\S]*if \(first\) state\.selectedTypeId = first\.id;[\s\S]*render\(\)/);
    assert.equal(source.match(/\brender\(\)/g)?.length, 1);
  }
  assert.equal(libraryJump.match(/\brender\(\)/g)?.length, 1);
  assert.match(
    binding,
    /onPerformanceMemberSelect: target => \{[\s\S]*drillToPerfMember\(\s*target\.stableSelector,\s*target\.assembly,\s*target\.typeId\)/);
  assert.match(
    appSource,
    /function drillToPerfMember\([\s\S]*resetMemberSectionState\(\);[\s\S]*loadSelectedMemberDocumentation\(\)/);
  assert.doesNotMatch(
    appSource.match(
      /function drillToPerfMember\([\s\S]*?\n}/)?.[0] ?? "",
    /memberSection = "facts"|loadSelectedMemberFacts\(\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-(?:dep-group|dep-open|dep-load|kind-jump|namespace-jump|lib-scope|graph-type|perf-selector)\]"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /\[data-(?:dep-group|dep-open|dep-load|kind-jump|namespace-jump|lib-scope|graph-type|perf-selector)\]/);
  assert.doesNotMatch(appSource, /function bindDependencyListHandlers\(/);
});

test("typed library controls own library and Platform picker bindings", () => {
  const binding =
    appSource.match(/const libraryControlActions: LibraryControlBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const wrapper =
    appSource.match(/function bindLibraryControlsEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindTypePanelEvents)/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  assert.match(
    libraryControlsSource,
    /export function bindLibraryControls\([\s\S]*\[data-library-chip\][\s\S]*\[data-access-chip\][\s\S]*#library-jump[\s\S]*\[data-platform-library-select\]/);
  for (const lens of [
    "integrations",
    "analysis",
    "metrics",
    "metadata",
  ]) {
    assert.match(
      libraryControlsSource,
      new RegExp(`\\[data-platform-${lens}-library\\]`));
  }
  assert.equal(
    workspaceBinding.match(/\bbindLibraryControlsEvents\(\)/g)?.length,
    1);
  assert.match(
    wrapper,
    /bindLibraryControls\(document, libraryControlActions\)/);
  assert.doesNotMatch(
    wrapper,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    binding,
    /onAccessibilityChipSelect: accessibility => \{[\s\S]*toggleAccessibilityChip\(accessibility\);[\s\S]*afterLibraryScopeChange\(\)/);
  assert.match(
    binding,
    /onLibraryChipSelect: library => \{\s*if \(library && selectLibrarySubject\(library\)\) render\(\);/);
  assert.match(
    binding,
    /onLibraryJump: library => \{\s*if \(library && selectLibrarySubject\(library\)\) render\(\);/);
  assert.match(
    binding,
    /onPlatformLibrarySelect: \(name, pack\) =>\s*observeAsync\(\s*openPlatformLibrary\(name, pack, \{ inPlace: true \}\),\s*"Opening a platform library"\)/);
  assert.match(
    appSource,
    /const requiresSelection = options\.requireSelection === true && !scoped;[\s\S]*?<option value="" selected disabled>Choose a library<\/option>/);
  assert.equal(
    appSource.match(/requireSelection: true/g)?.length,
    1);
  assert.match(
    binding,
    /onPlatformLensLibrarySelect: \(lens, name, pack\) =>\s*observeAsync\(\s*openPlatformLensLibrary\(lens, name, pack\),\s*"Opening a platform library"\)/);
  assert.match(
    appSource,
    /else if \(lens === "metrics"\) await loadPackageLibraryMetrics\(\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /\[data-(?:library-chip|access-chip|platform-(?:library-select|integrations-library|opportunities-library|analysis-library|metrics-library|metadata-library))\]|#library-jump/);
  assert.doesNotMatch(
    appSource,
    /\[data-(?:library-chip|access-chip|platform-(?:library-select|integrations-library|opportunities-library|analysis-library|metrics-library|metadata-library))\]|#library-jump/);
  assert.doesNotMatch(appSource, /bindPlatformLensPicker/);
});

test("type accessibility controls offer an all-access selection", () => {
  const toggle =
    appSource.match(/function toggleAccessibilityChip\([\s\S]*?\n}(?=\n\n\/\/ The accessibility selector)/)?.[0]
    ?? "";
  const control =
    appSource.match(/function accessibilityControl\(\) \{[\s\S]*?\n}(?=\n\n\/\/ Options for the namespace picker)/)?.[0]
    ?? "";

  assert.match(
    toggle,
    /if \(!bucket\) \{[\s\S]*new Set\(accessibilityBuckets\(\)\.map\(descriptor => descriptor\.id\)\);[\s\S]*return;/);
  assert.match(
    control,
    /const allOn = buckets\.every\([\s\S]*data-access-chip="">all access<\/button>/);
});

test("typed shell controls own workbench, home, and load-error bindings", () => {
  const workbenchActions =
    appSource.match(/const workbenchShellActions: WorkbenchShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const homeActions =
    appSource.match(/const homeShellActions: HomeShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const loadErrorActions =
    appSource.match(/const loadErrorShellActions: LoadErrorShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const homeBinding =
    appSource.match(/function bindHomeEvents\([^)]*\) \{[\s\S]*?\n}(?=\n\nfunction openProductDemos)/)?.[0]
    ?? "";
  const loadingBinding =
    appSource.match(/function renderLoading\(\) \{[\s\S]*?\n}(?=\n\nasync function loadSelectedMemberDocumentation)/)?.[0]
    ?? "";
  assert.match(
    shellControlsSource,
    /export function bindWorkbenchShell\([\s\S]*\[data-subject-copy\][\s\S]*#application-menu-button[\s\S]*#application-menu[\s\S]*\[data-application-action\][\s\S]*#dismiss-notice[\s\S]*#retry-notice[\s\S]*#dismiss-package-notice[\s\S]*#nav-back[\s\S]*#nav-forward[\s\S]*#open-search[\s\S]*export function focusWorkbenchSearch\([\s\S]*#open-search/);
  assert.match(
    shellControlsSource,
    /export function bindHomeShell\([\s\S]*#home-theme[\s\S]*#dismiss-notice[\s\S]*#home-demos/);
  assert.match(
    shellControlsSource,
    /export function bindLoadErrorShell\([\s\S]*#retry-load[\s\S]*#error-package-query[\s\S]*#error-package-input[\s\S]*#toggle-error-detail[\s\S]*\.load-error-detail/);
  assert.match(
    shellControlsSource,
    /import \{\s*parsePackageQuery,\s*type ParsedPackageQuery,\s*\} from "\.\/package-controls\.ts"/);
  assert.equal(
    workspaceBinding.match(/\bbindWorkbenchShell\(\b/g)?.length,
    1);
  assert.match(
    workspaceBinding,
    /bindWorkbenchShell\(document, workbenchShellActions\)/);
  assert.match(
    homeBinding,
    /bindHomeShell\(document, homeShellActions\)[\s\S]*spotlight\.bind\(document, "inline"\)[\s\S]*#spotlight-input/);
  assert.match(
    loadingBinding,
    /app\.innerHTML = `[\s\S]*bindLoadErrorShell\(document, loadErrorShellActions\)/);
  assert.match(
    workbenchActions,
    /onApplicationAction: dispatchApplicationAction,\s*onCopySubjectSegment: index => \{[\s\S]*currentInspectedSubjectPath\(\)\[index\][\s\S]*copyText\(segment\.label, `\$\{segment\.kind\} name copied`\)[\s\S]*onDismissNotice: dismissQueryNotice,\n  onDismissPackageNotice:/);
  assert.match(
    workbenchActions,
    /onDismissPackageNotice: \(\) => \{[\s\S]*pkg\.inspectionErrors = \[\];[\s\S]*pkg\.inspectionError = "";[\s\S]*render\(\);\s*\},\n  onNavigateBack:/);
  assert.match(
    workbenchActions,
    /onNavigateBack: navBack,[\s\S]*onNavigateForward: navForward,[\s\S]*onRetryNotice: \(\) => \{[\s\S]*state\.queryNoticeRetryAction;[\s\S]*if \(retryAction\) observeAction\(retryAction, "Retrying the inspection"\);[\s\S]*onSearch: \(\) => openSpotlight\(\)/);
  assert.match(
    homeActions,
    /onDismissNotice: dismissQueryNotice,\s*onOpenDemos: openProductDemos,\s*onOpenLibrary: \(\) => openLibraryDialog\("home"\),\s*onToggleTheme: toggleTheme/);
  assert.match(
    loadErrorActions,
    /onOpenPackage: openPackageQuery,\s*onRetry: \(\) => \{\s*if \(state\.retryAction === retryUnavailable\) return;\s*observeAction\(\s*state\.retryAction \?\? bootstrap,\s*"Retrying the inspection"\);\s*\}/);
  assert.doesNotMatch(
    appSource,
    /\bquerySelector(?:All)?(?:<[^>]+>)?\("(?:#(?:share|dismiss-notice|retry-notice|dismiss-package-notice|nav-back|nav-forward|open-search|help|home-theme|home-demos|retry-load|error-package-query|error-package-input|toggle-error-detail)|\[data-subject-copy\]|\.load-error-detail)"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /#(?:share|dismiss-notice|retry-notice|dismiss-package-notice|nav-back|nav-forward|open-search|help)/);
  assert.doesNotMatch(homeBinding, /#(?:home-theme|dismiss-notice|home-demos)/);
  assert.doesNotMatch(
    loadingBinding,
    /#(?:retry-load|error-package-query|error-package-input|toggle-error-detail)|"\.load-error-detail"/);
});

test("keyboard help projects available global and current graph bindings", () => {
  const openKeyboardHelp =
    appSource.match(/function openKeyboardHelp\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    openKeyboardHelp,
    /querySelector<HTMLElement>\("\.graph-viewport"\)[\s\S]*keybindings\.availableBindingsFor\(\)[\s\S]*keybindings\.availableBindingsFor\(graphViewport\)[\s\S]*state\.keyboardHelp = true/);
  assert.match(
    shellControlsSource,
    /\["graph\.zoom", "Zoom the current graph"\][\s\S]*\["graph\.pan-horizontal", "Pan the current graph horizontally"\][\s\S]*\["graph\.pan-vertical", "Pan the current graph vertically"\]/);
  assert.match(
    appSource,
    /const inspectionNavigationIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && scope\(\) !== "workspace"/);
  assert.match(
    appSource,
    /const workspaceDrillInIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && state\.package !== null/);
  const renderWorkspaceFocus =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?\n}\n\nfunction renderWorkspaceCatalogView/,
    )?.[0]
    ?? "";
  assert.equal(
    renderWorkspaceFocus.match(
      /restoreWorkspaceFocus\(document, workspaceFocus\)/g,
    )?.length,
    3);
  assert.match(
    renderWorkspaceFocus,
    /const workspaceFocus = captureWorkspaceFocus\(focusedElement\);[\s\S]*renderWorkspaceCatalogView\(\);[\s\S]*else if \(workspaceFocus\) \{\s*if \(!restoreWorkspaceFocus\(document, workspaceFocus\)\) \{\s*focusLevelOneHeading\(\);[\s\S]*recordNav\(\);[\s\S]*return;/);
  const catalogRenderer =
    appSource.match(/function renderWorkspaceCatalogView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    catalogRenderer,
    /workbenchShellHtml\(\{[\s\S]*<main id="subject-panel" class="workspace">/);
  assert.match(
    appSource,
    /function drillIn\(\) \{\s*if \(scope\(\) === "workspace"\) \{\s*if \(!state\.package\) return;/);
  const drillInBinding =
    appSource.match(/keybindings\.register\(\{\s*id: "workspace\.drill-in"[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    drillInBinding,
    /available: workspaceDrillInIsAvailable/);
  assert.match(drillInBinding, /run: \(\) => \{\s*drillIn\(\);/);
  assert.match(
    appSource,
    /const workspaceHistoryBackIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && navigationHistory\.canBack\(\)/);
  assert.match(
    appSource,
    /const workspaceHistoryForwardIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && navigationHistory\.canForward\(\)/);
  assert.match(
    appSource,
    /\["ArrowLeft", navBack, workspaceHistoryBackIsAvailable\][\s\S]*\["ArrowRight", navForward, workspaceHistoryForwardIsAvailable\][\s\S]*available,/);
});

test("delayed Share completion preserves newer focus ownership", () => {
  const share =
    appSource.match(/async function share\(\) \{[\s\S]*?\n}/)?.[0] ?? "";
  assert.match(
    share,
    /const focusOwner = captureApplicationMenuFocusOwner\(document\)/);
  assert.match(
    share,
    /requestAnimationFrame\(\(\) =>\s*restoreApplicationMenuFocusIfOwned\(document, focusOwner\)\)/);
});

test("deferred Spotlight focus preserves newer document focus", () => {
  const focusGuard =
    appSource.match(/function canRestoreWorkbenchFocus\([\s\S]*?\n}/)?.[0]
    ?? "";
  const focusTypeList =
    appSource.match(/function focusTypeList\([\s\S]*?\n}/)?.[0] ?? "";
  assert.match(focusGuard, /applicationMenuOwnsFocus\(document\)/);
  assert.equal(
    focusTypeList.match(
      /canRestoreWorkbenchFocus\(generation, focusGeneration\)/g)?.length,
    3);
});

test("the shell separates typed target and Subject navigation rows", () => {
  const renderNode = functionDeclaration("renderCore");
  const subjectPathNode = functionDeclaration("inspectedSubjectPath");
  const subjectPathRenderer = functionDeclaration("renderInspectedSubjectPath");
  const subjectIconRenderer =
    functionDeclaration("renderInspectedSubjectIcon");
  const render = appSource.slice(renderNode.start, renderNode.end);
  const subjectPath = appSource.slice(subjectPathNode.start, subjectPathNode.end);
  const renderer =
    appSource.slice(subjectPathRenderer.start, subjectPathRenderer.end);
  const iconRenderer =
    appSource.slice(subjectIconRenderer.start, subjectIconRenderer.end);

  assert.match(
    render,
    /workbenchShellHtml\(\{[\s\S]*contextualActionsHtml:[\s\S]*class="working-surface-actions"[\s\S]*inspectedTargetHtml:[\s\S]*class="inspected-target"[\s\S]*renderInspectedSubjectIcon\(pkg\)[\s\S]*class="subject-path"[\s\S]*subjectInspectorHtml: renderScopeBar\(\)[\s\S]*titleNavigationHtml: renderTitleNavigation\([\s\S]*<main id="subject-panel" class="workspace\$\{contentFrameEnabled[\s\S]*renderApplicationMenu\(state\.rootKind !== "library"\)/);
  assert.doesNotMatch(render, /id="copy-name"|id="taste-btn"/);
  assert.doesNotMatch(
    render,
    /<section class="detail-pane">\s*<header class="detail-head">/);
  assert.match(
    subjectPath,
    /rootIsPresented\(\)[\s\S]*kind: state\.rootKind[\s\S]*label: state\.rootKind === "platform"[\s\S]*platformTargetLabel\(\)[\s\S]*packageDisplayName\(pkg\)[\s\S]*kind: "type"[\s\S]*current\.namespace[\s\S]*kind: "member"[\s\S]*label: member\.name/);
  assert.match(
    renderer,
    /segment\.label[\s\S]*segment\.copyable[\s\S]*data-subject-copy="\$\{index\}"[\s\S]*segment\.kind/);
  assert.match(
    iconRenderer,
    /pkg\.icon[\s\S]*data:\$\{pkg\.icon\.mediaType\};base64,\$\{pkg\.icon\.base64\}[\s\S]*NUGET_DEFAULT_PACKAGE_ICON/);
  assert.doesNotMatch(iconRenderer, /⬡|iconUrl/);
  assert.match(
    appSource,
    /NUGET_DEFAULT_PACKAGE_ICON[\s\S]*default-package-icon-256x256\.png[\s\S]*data-package-icon[\s\S]*packageIcon\.onerror =[\s\S]*packageIcon\.src = NUGET_DEFAULT_PACKAGE_ICON/);
});

test("typed graph interactions own graph controls and Mermaid node bindings", () => {
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const typeGraph =
    appSource.match(/async function renderTypeGraph\(\) \{[\s\S]*?\n}(?=\n\nfunction navigateToTypeByName)/)?.[0]
    ?? "";
  const annotatedRelationshipGraph =
    appSource.match(/async function renderAnnotatedRelationshipDiagram\(\) \{[\s\S]*?\n}(?=\n\n\/\/ Projects the neutral type-relationship)/)?.[0]
    ?? "";
  const dependencyGraph =
    appSource.match(/async function renderDependencyGraph\(\) \{[\s\S]*?\n}(?=\n\nfunction switchToPackageForDependencies)/)?.[0]
    ?? "";
  const callGraph =
    appSource.match(/function renderMermaidCallGraph\(\): Promise<CallGraphRenderResult> \{[\s\S]*?\n}(?=\n\nfunction callGraphNodeBinding)/)?.[0]
    ?? "";
  const callGraphBinding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction currentCallGraph)/)?.[0]
    ?? "";
  assert.match(
    graphInteractionsSource,
    /export function bindGraphBack\([\s\S]*\[data-graph-back\]/);
  assert.match(
    graphInteractionsSource,
    /function mermaidNodeId\([\s\S]*data-id[\s\S]*flowchart-/);
  assert.match(
    graphInteractionsSource,
    /export function bindGraphPanZoom\([\s\S]*"wheel"[\s\S]*"pointerdown"[\s\S]*"pointermove"[\s\S]*"pointerup"[\s\S]*"pointercancel"[\s\S]*\.graph-controls button[\s\S]*id: "graph\.zoom"[\s\S]*id: "graph\.pan-horizontal"[\s\S]*id: "graph\.pan-vertical"[\s\S]*resolveCallGraphNode/);
  assert.match(
    graphInteractionsSource,
    /resolveTypeGraphNode[\s\S]*"t"[\s\S]*"unavailableLabel" in binding[\s\S]*non-nav[\s\S]*createElementNS/);
  assert.match(
    graphInteractionsSource,
    /const resolveNode = options\.resolveCallGraphNode\s*\?\? options\.resolveDependencyGraphNode\s*\?\? options\.resolveTypeGraphNode;[\s\S]*mermaidNodeId\(node, prefix\)[\s\S]*if \(!moved\) binding\.onSelect\(\)/);
  assert.match(
    appSource,
    /const graphBackActions: GraphBackBindingActions = \{\s*onBack: popPlatformDrill,\s*};/);
  assert.match(
    workspaceBinding,
    /bindGraphBack\(document, graphBackActions\)/);
  assert.match(
    workspaceBinding,
    /bindCallGraphTraversalFramework\(\)/);
  assert.match(
    appSource,
    /function bindCallGraphTraversalFramework\(\)[\s\S]*\[data-call-graph-traversal-framework\][\s\S]*addEventListener\("change"[\s\S]*invalidateMemberCallGraphWork\(state\)[\s\S]*loadSelectedMemberCallGraph\(\)/);
  assert.match(
    typeGraph,
    /bindGraphPanZoom\(container, viewport, \{[\s\S]*resolveTypeGraphNode: nodeId => \{[\s\S]*graphNodeOf\.get\(nodeId\)[\s\S]*closeGraphExplorerForNavigation\(\);[\s\S]*navigateToWorkspaceType\(candidate\.pkg, candidate\.type\)/);
  assert.match(
    typeGraph,
    /unavailableLabel:[\s\S]*not uniquely available in the loaded Workspace surfaces/);
  assert.match(
    appSource,
    /function navigateToWorkspaceType\([\s\S]*navigationPreservesAggregateLibraryScope\(pkg\)[\s\S]*selectWorkspacePackage\(pkg, \{ renderSelection: false \}\);[\s\S]*navigateToType\(target, \{ preserveAggregate \}\)/);
  assert.match(
    appSource,
    /function navigateToType\([\s\S]*preserveAggregate\?: boolean[\s\S]*enterTypeSubject\(target, options\)/);
  assert.match(
    typeGraph,
    /const candidate = graphNode\.role === "self"[\s\S]*\{ pkg: currentPackage\(\), type: currentType \}[\s\S]*uniqueWorkspaceTypeByQueryId<AppTypeSurface, AppPackage>\([\s\S]*state\.packages,[\s\S]*fullName\)/);
  assert.match(
    annotatedRelationshipGraph,
    /buildAnnotatedRelationshipGraphMermaid\(\s*model\.callRelationships\)[\s\S]*bindGraphPanZoom\(targetContainer, viewport, \{ keybindings \}\)/);
  assert.match(
    dependencyGraph,
    /bindGraphPanZoom\(container, viewport, \{[\s\S]*resolveDependencyGraphNode: nodeId => \{[\s\S]*built\.nodeInfoById\.get\(nodeId\)[\s\S]*switchToPackageForDependencies\(info\.packageKey\)[\s\S]*openDependencyPackage\(info\.id, info\.versionRange\)/);
  assert.match(
    dependencyGraph,
    /const info = nodeId \? built\.nodeInfoById\.get\(nodeId\) : null;\s*if \(!info \|\| info\.kind === "self"\) return null/);
  assert.match(
    callGraph,
    /const mounted = currentCallGraph\(\);[\s\S]*mounted\?\.mermaid !== definition[\s\S]*bindGraphPanZoom\(targetContainer, viewport, \{[\s\S]*resolveCallGraphNode: nodeId =>[\s\S]*callGraphNodeBinding\(mounted, nodeId\)/);
  assert.match(
    callGraph,
    /active\.noBody\) return Promise\.resolve\(\{ status: "rendered" \}\);[\s\S]*pending\.definition === active\.mermaid[\s\S]*pending\.theme === theme[\s\S]*return pending\.promise/);
  assert.match(
    callGraph,
    /catch \(error\) \{[\s\S]*const message = errorMessage\(error\);[\s\S]*graph-render-error[\s\S]*return \{ status: "failed", message \}/);
  assert.match(
    callGraphBinding,
    /callGraph\.targets\?\.find\(candidate => candidate\.id === nodeId\)[\s\S]*const drilled =\s*state\.platformStack\.length > 0 \|\| Boolean\(state\.package\?\.isRuntimePack\);[\s\S]*resolveRuntimeGraphTargetCandidate\(pack, target\)[\s\S]*runtimeGraphTargetNavigationDisposition\([\s\S]*blockedCallGraphNodeBinding/);
  assert.match(
    callGraphBinding,
    /if \(disposition === "member" && pack && resident\) \{[\s\S]*openRuntimeMemberFromGraph\([\s\S]*\} else if \(disposition === "lookup"\) \{[\s\S]*navigateOrDrillPlatform\([\s\S]*target,[\s\S]*runtimeSection,[\s\S]*failureSurface\)[\s\S]*\} else if \(destination === "member"\)[\s\S]*startPlatformDrill\(target\)/);
  assert.equal(
    [...callGraphBinding.matchAll(
      /let owner = captureViewOperation\(state\.memberCallGraphSeq\);[\s\S]{0,500}?\(\) => ownsViewOperation\(owner, state\.memberCallGraphSeq\)[\s\S]{0,200}?owner = captureViewOperation\(state\.memberCallGraphSeq\)/g)]
      .length,
    2);
  assert.match(
    callGraphBinding,
    /const loaded = disposition === "loaded" && candidate\.status === "unique"\s*\? resolveLoadedGraphTarget\(target, candidate\)\s*: null/);
  assert.match(
    callGraphBinding,
    /combinedGraphTargetNavigationDisposition\(\s*candidate,\s*runtimeCandidate,\s*target,\s*runtimeResident\)/);
  assert.match(
    callGraphBinding,
    /if \(loaded\) \{[\s\S]*navigateToGraphMember\([\s\S]*loaded,[\s\S]*target,[\s\S]*loadedSection,[\s\S]*failureSurface\)[\s\S]*\} else if \(disposition === "resident"\) \{[\s\S]*startPlatformDrill\(target\)[\s\S]*\} else if \(platform\) \{[\s\S]*navigateOrDrillPlatform\([\s\S]*target,[\s\S]*runtimeSection,[\s\S]*failureSurface\)/);
  assert.match(
    graphInteractionsSource,
    /resolveCallGraphNode[\s\S]*setAttribute\("tabindex", "0"\)[\s\S]*setAttribute\("role", "button"\)[\s\S]*setAttribute\("aria-label", binding\.label\)[\s\S]*addEventListener\("click"[\s\S]*"call-graph-node\.activate"[\s\S]*"dependency-graph-node\.activate"[\s\S]*key: \["Enter", " "\]/);
  assert.equal(appSource.match(/\bbindGraphBack\(/g)?.length, 1);
  assert.equal(appSource.match(/\bbindGraphPanZoom\(/g)?.length, 4);
  assert.equal(appSource.match(/\bcallGraphNodeBinding\(/g)?.length, 2);
  assert.doesNotMatch(
    `${annotatedRelationshipGraph}\n${typeGraph}\n${dependencyGraph}\n${callGraph}`,
    /\.addEventListener\(|querySelectorAll<SVGGElement>\("g\.node"\)/);
  assert.doesNotMatch(
    appSource,
    /function (?:attachGraphPanZoom|graphTargetForSvgNode)\(/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector\("\[data-graph-back\]"\)/);
  assert.match(
    appSource,
    /document\.addEventListener\("focusin", trackContentFrameFocus\)/);
  assert.match(
    appSource,
    /document\.addEventListener\("pointerdown", trackContentFramePointer\)/);
  assert.equal(appSource.match(/\.addEventListener\(/g)?.length, 6);
});

test("Call graph presentation keeps renderer source internal", () => {
  assert.doesNotMatch(appSource, /Mermaid source|class="graph-mermaid"/);
  assert.doesNotMatch(stylesSource, /\.graph-mermaid/);
});

test("typed document inspection owns package document request coordination", () => {
  const documentLoader =
    appSource.match(/function openPackageDocument\(path: string\)[\s\S]*?\n}/)?.[0]
    ?? "";
  const documentCloser =
    appSource.match(/function closeDocViewer\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createDocumentInspectionCoordinator\(\{[\s\S]*queryDocument:[\s\S]*renderMarkdown,[\s\S]*renderMarkdownInline,/);
  assert.match(
    appSource,
    /queryDocument: request => inspectPackageDocument\(\s*request\.packageId,\s*request\.version,\s*request\.document\.path\)/);
  assert.match(
    documentLoader,
    /return documentInspection\.open\(\{\s*packageId: pkg\.id,\s*version: pkg\.version,\s*document: doc,\s*\}\)/);
  assert.match(documentCloser, /documentInspection\.close\(\)/);
  assert.doesNotMatch(documentLoader, /state\.docViewer(?:Seq|Open|Loading)/);
  assert.doesNotMatch(documentCloser, /state\.docViewer(?:Seq|Open|Loading)/);
  assert.match(
    documentInspectionSource,
    /async open\(request: PackageDocumentRequest\)[\s\S]*const pending = \{[\s\S]*status: "loading",[\s\S]*state\.docViewer = pending[\s\S]*state\.docViewer !== pending/);
});

test("typed catalog requests own package-version coordination", () => {
  const versionLoader =
    appSource.match(/function ensurePackageVersions\(pkg: AppPackage \| null\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createCatalogRequests\(\{[\s\S]*queryPackageVersions: pkg => inspectPackageVersions\(pkg\.id, pkg\.version\),[\s\S]*updatePackageVersionSelect: updateVersionSelect,/);
  assert.match(versionLoader, /return catalogRequests\.ensurePackageVersions\(pkg\)/);
  assert.doesNotMatch(
    versionLoader,
    /packageVersionsLoading|state\.packages/);
  assert.match(
    catalogRequestsSource,
    /inventories\.set\(pkg, pending\)[\s\S]*dependencies\.queryPackageVersions\(pkg\)[\s\S]*if \(isCurrent\(\)\)/);
  assert.doesNotMatch(
    catalogRequestsSource,
    /\bfetch\(|\bdocument\b|inspectPackageVersions/);
});

test("typed type panel owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const rootEventBinder =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const clearFilters =
    binding.match(/onClearFilters: \(\) => \{[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindTypePanel\(document, \{/);
  assert.doesNotMatch(clearFilters, /libraryScope/);
  assert.doesNotMatch(clearFilters, /focusFilter/);
  assert.match(
    clearFilters,
    /state\.accessibilityFilter = defaultAccessibilityFilter\(state\.package\)/);
  assert.match(
    binding,
    /onTypeFilterChange: value => \{[\s\S]*?render\(\);\s*focusFilter\(\{ immediate: true \}\);\s*},/);
  assert.match(
    binding,
    /onTypeFilterDisclosureToggle: expanded => \{\s*state\.typeFiltersExpanded = expanded;\s*},/);
  assert.match(
    binding,
    /onTypeFilterEscape: \(\) => \{\s*state\.typeFilter = "";\s*render\(\);\s*focusFilter\(\{ immediate: true \}\);\s*},/);
  assert.equal(
    appSource.match(/\bbindTypePanelEvents\b/g)?.length,
    2);
  assert.equal(
    rootEventBinder.match(/\bbindTypePanelEvents\(\)/g)?.length,
    1);
  assert.equal(
    rootEventBinder.match(/^\s*bindTypePanelEvents\(\);$/gm)?.length,
    1);
  assert.match(
    appSource,
    /function bindEvents\(\) \{\s*packageControls\.bind\(document\);\s*bindWorkspaceSubjectEvents\(\);\s*bindTypePanelEvents\(\);/);
  assert.match(
    typePanelSource,
    /export function bindTypePanel\([\s\S]*\[data-type\][\s\S]*\[data-namespace\][\s\S]*\[data-kind-filter\][\s\S]*\[data-nav-member\][\s\S]*\[data-nav-overload\][\s\S]*#nav-to-types[\s\S]*#clear-filter[\s\S]*#namespace-jump[\s\S]*#type-list[\s\S]*#type-filter/);
  assert.match(
    typePanelSource,
    /\[data-member-kind-filter\][\s\S]*\[data-member-access-filter\][\s\S]*\[data-member-trait-filter\][\s\S]*#clear-member-filter[\s\S]*#member-filter/);
  assert.match(
    typePanelSource,
    /\[data-member-jump-kind\][\s\S]*\[data-member-jump-access\][\s\S]*\[data-member-jump-trait\][\s\S]*\[data-member\][\s\S]*\[data-overload\][\s\S]*#member-back[\s\S]*#copy-signature[\s\S]*\[data-copy-anchor\][\s\S]*#copy-source[\s\S]*#copy-type-source[\s\S]*#explore-source/);
  assert.doesNotMatch(typePanelSource, /#copy-name|onCopyName/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-member-(?:kind|access|trait)-filter\]"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector(?:<HTMLInputElement>)?\("#(?:member-filter|clear-member-filter)"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-(?:member-jump-(?:kind|access|trait)|member|overload|copy-anchor)\]"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector\("#(?:member-back|copy-signature|copy-source|copy-type-source|explore-source)"\)/);
  assert.match(
    binding,
    /const enterMemberNavigation = \(action: \(\) => void\) => \{[\s\S]*beginSpotlightNavigation\(\);[\s\S]*contentFramePane = "navigation";[\s\S]*action\(\);[\s\S]*restoreContentNavigationFocus\(focusGeneration\)/);
  const callbackSource = (name: string) =>
    binding.match(
      new RegExp(`    ${name}: [\\s\\S]*?(?=\\n    on[A-Z])`))?.[0]
      ?? "";
  for (const [name, stateField] of [
    ["onMemberCompositionAccessibilitySelect", "memberAccessibilityFilter"],
    ["onMemberCompositionKindSelect", "memberKindFilter"],
    ["onMemberCompositionTraitSelect", "memberTraitFilter"],
  ] as const) {
    const source = callbackSource(name);
    assert.match(
      source,
      new RegExp(
        `enterMemberNavigation\\(\\(\\) => \\{[\\s\\S]*resetMemberFilters\\(\\);`
        + `[\\s\\S]*state\\.${stateField} = value;`
        + "[\\s\\S]*enterMemberScope\\(\\);[\\s\\S]*render\\(\\)"));
    assert.equal(source.match(/\brender\(\)/g)?.length, 1);
  }
  assert.match(
    binding,
    /onMemberGroupOpen: memberKey => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*showContentDetailAfterRender\(\);\s*openMemberGroup\(memberKey\);\s*if \(!contentFrameMedia\.matches\)\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    binding,
    /onMemberBack: drillOut[\s\S]*onMemberOverloadOpen: openOverload/);
  assert.doesNotMatch(
    binding,
    /onCopyName|currentInspectedSubjectName/);
  assert.match(
    binding,
    /onCopySignature: \(\) => \{[\s\S]*state\.memberDeclarationKey === signature[\s\S]*state\.memberDeclaration\?\.text[\s\S]*void copyText\(state\.memberDeclaration\.text, "declaration copied"\)/);
  assert.match(
    binding,
    /onCopyAnchor: anchor => \{[\s\S]*selector: overload\?\.stableSelector,[\s\S]*digest: overload\?\.anchorDigest,[\s\S]*canonical: overload\?\.canonicalSignature[\s\S]*void copyText\(value, `\$\{anchor\} copied`\)/);
  assert.match(
    binding,
    /onCopyMemberSource: \(\) => \{[\s\S]*sourceResultForSignature\([\s\S]*memberSourceText\([\s\S]*memberSourcePartSelector\.current\(signature, source\)[\s\S]*"source copied"[\s\S]*onMemberSourcePartSelect: part => \{[\s\S]*memberSourcePartSelector\.select\(signature, source, part\)[\s\S]*render\(\)[\s\S]*onCopyTypeSource: \(\) => \{[\s\S]*state\.typeSource\.status !== "ready"[\s\S]*typeCodeViewText\(state\.typeSource\.source\)[\s\S]*text !== null[\s\S]*void copyText\(text, "source copied"\)/);
  assert.match(
    binding,
    /onMemberFilterClear: \(\) => \{[\s\S]*resetMemberFilters\(\);[\s\S]*renderMemberFilterAndRestoreFocus\("#clear-member-filter"\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: \(event, value\) => \{\s*if \(event\.key === "Escape"\) \{\s*if \(navMode\(\) !== "member" && value === ""\) return false;[\s\S]*navMode\(\) === "member"[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*return true;[\s\S]*event\.key !== "ArrowUp" && event\.key !== "ArrowDown"\) return false;\s*stepMemberNav\(event\.key === "ArrowDown" \? 1 : -1, true\);\s*return true/);
  assert.match(
    binding,
    /bindTypePanel\(document, \{[\s\S]*}, keybindings\);/);
  const selectorCount = (selector: string) =>
    appSource.split(selector).length - 1;
  assert.deepEqual(
    Object.fromEntries([
      "[data-type]",
      "[data-namespace]",
      "[data-kind-filter]",
      "[data-nav-member]",
      "[data-nav-overload]",
      "#nav-to-types",
      "#clear-filter",
      "#namespace-jump",
    ].map(selector => [selector, selectorCount(selector)])),
    {
      "[data-type]": 0,
      "[data-namespace]": 0,
      "[data-kind-filter]": 0,
      "[data-nav-member]": 0,
      "[data-nav-overload]": 0,
      "#nav-to-types": 0,
      "#clear-filter": 0,
      "#namespace-jump": 0,
    });
  assert.equal(selectorCount("#type-filter"), 1);
  assert.equal(selectorCount("#type-list"), 5);
});

test("typed scope bar owns its rendered control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const rootEventBinder = functionDeclaration("bindEvents");
  const scopeEventBinder = functionDeclaration("bindScopeBarEvents");
  const rootScopeCalls = callExpressionsNamed(appSyntax, "bindScopeBarEvents");
  assert.equal(rootScopeCalls.length, 3);
  for (const rootScopeCall of rootScopeCalls)
    assert.equal(rootScopeCall.arguments.length, 0);
  const innerScopeCall = onlyCallExpressionNamed(appSyntax, "bindScopeBar");
  assert.equal(innerScopeCall.arguments.length, 3);
  assertIdentifierArgument(innerScopeCall, 0, "document", "bindScopeBar");
  assertIdentifierArgument(innerScopeCall, 2, "scopeBarState", "bindScopeBar");
  assert.equal(
    callExpressionsNamed(scopeEventBinder, "bindScopeBar").length,
    1);
  assert.equal(
    scopeEventBinder.body.body.filter(statement =>
      statement.type === "ExpressionStatement"
      && statement.expression.type === "AssignmentExpression"
      && statement.expression.right === innerScopeCall).length,
    1);
  assert.equal(
    callExpressionsNamed(rootEventBinder, "bindScopeBarEvents").length,
    1);
  const directRootCalls = rootEventBinder.body.body.map(directCallName);
  const typePanelIndex = directRootCalls.indexOf("bindTypePanelEvents");
  assert.notEqual(typePanelIndex, -1);
  assert.equal(directRootCalls[typePanelIndex + 1], "bindScopeBarEvents");

  const actions = objectArgument(innerScopeCall, 1, "bindScopeBar");
  const memberSection = callbackProperty(actions, "onMemberSectionSelect");
  assert.deepEqual(
    statementSignatures(memberSection.body.body),
    [
      'assign:contentFramePane = "detail"',
      "call:applyMemberSection(section)",
    ]);

  const packageLens = callbackProperty(actions, "onPackageLensSelect");
  assert.deepEqual(
    statementSignatures(packageLens.body.body),
    [
      'assign:contentFramePane = "detail"',
      "assign:state.packageLens = lens",
      "call:render()",
    ]);

  const libraryLens = callbackProperty(actions, "onLibraryLensSelect");
  assert.deepEqual(
    statementSignatures(libraryLens.body.body),
    [
      'assign:contentFramePane = "detail"',
      "call:selectLibraryLens(lens)",
    ]);

  const scope = callbackProperty(actions, "onScopeSelect");
  assert.match(appSource.slice(scope.start, scope.end), /target === "platform"[\s\S]*showPlatformRoot\(\)/);
  assert.deepEqual(
    statementSignatures(scope.body.body.slice(1)),
    [
      'assign:contentFramePane = "detail"',
      {
        if: 'target === "workspace"',
        whenTrue: [
          "expression:navigationSequence.begin()",
          "assign:state.workspaceSubjectOpen = true",
          "assign:state.atPackageRoot = true",
          "assign:state.atLibraryRoot = false",
          'assign:state.selectedMemberKey = ""',
          'assign:state.memberBrowseTypeId = ""',
          "assign:state.selectedOverloadIndex = null",
        ],
        whenFalse: [
          {
            if: 'target === "package"',
            whenTrue: [
              "assign:state.workspaceSubjectOpen = false",
              "assign:state.atPackageRoot = true",
              "assign:state.atLibraryRoot = false",
            ],
            whenFalse: [
              {
                if: 'target === "library"',
                whenTrue: [
                  {
                    if: "!enterRetainedLibrarySubject({ preserveView: true })",
                    whenTrue: ["statement:ReturnStatement:return;"],
                    whenFalse: [],
                  },
                ],
                whenFalse: [
                  {
                    if: 'target === "type"',
                    whenTrue: [
                      "assign:state.workspaceSubjectOpen = false",
                      {
                        if: "!enterTypeSubject(selectedType())",
                        whenTrue: ["statement:ReturnStatement:return;"],
                        whenFalse: [],
                      },
                      'assign:state.selectedMemberKey = ""',
                      'assign:state.memberBrowseTypeId = ""',
                      "assign:state.selectedOverloadIndex = null",
                    ],
                    whenFalse: [
                      {
                        if: 'target === "member"',
                        whenTrue: [
                          "assign:state.workspaceSubjectOpen = false",
                          "assign:state.atPackageRoot = false",
                          "assign:state.atLibraryRoot = false",
                          "call:enterMemberScope()",
                        ],
                        // A scope this dispatch does not handle is now a compile error rather
                        // than a silently ignored click.
                        whenFalse: ['call:assertNever(target, "workspace scope")'],
                      },
                    ],
                  },
                ],
              }
            ],
          },
        ],
      },
      "call:render()",
    ]);

  const typeLens = callbackProperty(actions, "onTypeLensSelect");
  assert.deepEqual(
    statementSignatures(typeLens.body.body),
    [
      'assign:contentFramePane = "detail"',
      "assign:state.lens = lens",
      'assign:state.selectedMemberKey = ""',
      'assign:state.memberBrowseTypeId = ""',
      "call:render()",
    ]);
  assert.match(
    scopeBarSource,
    /function bindItemActions\([\s\S]*\[data-scope\][\s\S]*\[data-package-lens\][\s\S]*\[data-library-lens\][\s\S]*\[data-lens\][\s\S]*\[data-member-section\][\s\S]*export function bindScopeBar\([\s\S]*bindItemActions\(root, actions\)/);
  for (const selector of [
    "[data-scope]",
    "[data-package-lens]",
    "[data-library-lens]",
    "[data-lens]",
    "[data-member-section]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("typed settings panel owns its rendered control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const bindEvents = functionDeclaration("bindEvents");
  const bindHomeEvents = functionDeclaration("bindHomeEvents");
  const renderSettings = functionDeclaration("renderSettingsViewHtml");
  const settingsEventBinder = functionDeclaration("bindSettingsPanelEvents");
  const settingsPanelImport = onlySyntaxNode(
    appSyntax.body.filter(
      (node): node is ImportDeclaration =>
        node.type === "ImportDeclaration"
        && node.source.value === "./settings-panel.ts"),
    "settings panel import");
  assert.equal(
    (settingsPanelImport.specifiers ?? []).filter(
      specifier => specifier.type === "ImportSpecifier"
        && specifier.imported.type === "Identifier"
        && specifier.imported.name === "bindSettingsPanel"
        && specifier.local.name === "bindSettingsPanel").length,
    1);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindSettingsPanel").length,
    3);
  const eventBinderCalls = callExpressionsNamed(appSyntax, "bindSettingsPanelEvents");
  assert.equal(eventBinderCalls.length, 5);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindSettingsPanelEvents").length,
    6);
  const settingsBinders: readonly (readonly [
    DeclaredFunction,
    string,
    string | null,
  ])[] = [
    [bindEvents, "workbench settings binder", "bindScopeBarEvents"],
    [bindHomeEvents, "home settings binder", null],
  ];
  for (const [owner, description, predecessor] of settingsBinders) {
    assert.equal(
      callExpressionsNamed(owner, "bindSettingsPanelEvents").length,
      1,
      description);
    assert.equal(
      owner.body.body
        .map(statement => directCallExpression(statement, "bindSettingsPanelEvents"))
        .filter(Boolean)
        .length,
      1,
      `${description} direct call`);
    if (predecessor) {
      const directCallNames = owner.body.body.map(directCallName);
      assert.equal(
        directCallNames.indexOf("bindSettingsPanelEvents"),
        directCallNames.indexOf(predecessor) + 1,
        `${description} order`);
    }
  }
  const directWorkbenchCalls = bindEvents.body.body.map(directCallName);
  assert.equal(
    directWorkbenchCalls.indexOf("bindSettingsPanelEvents"),
    directWorkbenchCalls.indexOf("bindScopeBarEvents") + 1);
  assert.equal(renderSettings.body.body.length, 1);
  const [renderStatement] = renderSettings.body.body;
  assert.ok(renderStatement !== undefined);
  assert.ok(
    renderStatement.type === "ReturnStatement",
    `settings view must return markup, found ${renderStatement.type}`);
  const renderCall = renderStatement.argument;
  assert.ok(
    renderCall?.type === "CallExpression",
    `settings view must return a call, found ${renderCall?.type ?? "nothing"}`);
  assert.ok(
    renderCall.callee.type === "Identifier",
    `settings view call must name a function, found ${renderCall.callee.type}`);
  assert.equal(renderCall.callee.name, "renderSettingsView");

  const innerSettingsCall = onlyCallExpressionNamed(appSyntax, "bindSettingsPanel");
  assert.equal(
    directCallExpression(
      onlySyntaxNode(settingsEventBinder.body.body, "bindSettingsPanelEvents body"),
      "bindSettingsPanel"),
    innerSettingsCall);
  assert.equal(innerSettingsCall.arguments.length, 2);
  assertIdentifierArgument(innerSettingsCall, 0, "document", "bindSettingsPanel");
  const actions = objectArgument(innerSettingsCall, 1, "bindSettingsPanel");
  assert.equal(actions.properties.length, 6);
  const settingsActions: readonly (readonly [string, string])[] = [
    ["onClose", "closeSettings"],
    ["onOpenDiagnostics", "openDiagnosticsRoute"],
    ["onOpen", "openSettings"],
    ["onTasteClear", "clearTaste"],
    ["onTasteToggle", "toggleTaste"],
    ["onThemeSelect", "setTheme"],
  ];
  for (const [name, value] of settingsActions) {
    const target = namedProperty(actions, name).value;
    assert.ok(
      target.type === "Identifier",
      `${name} settings action must be an identifier, found ${target.type}`);
    assert.equal(target.name, value);
  }
  assert.match(
    appSource,
    /import \{(?=[^}]*\bbindSettingsPanel,)[^}]*} from "\.\/settings-panel\.ts";/);
  assert.doesNotMatch(
    sourceText(settingsEventBinder),
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    settingsPanelSource,
    /export function bindSettingsPanel\([\s\S]*#settings-close[\s\S]*#home-settings[\s\S]*#settings-backdrop[\s\S]*#settings-dialog[\s\S]*\.settings-seg\[data-theme\][\s\S]*\.settings-taste \[data-taste\][\s\S]*#settings-taste-clear/);
  assert.doesNotMatch(
    settingsPanelSource,
    /#taste-btn|#taste-popover|#taste-clear|renderTastePopover|onTasteOpenToggle/);
  assert.doesNotMatch(
    productionTypeScriptSources.map(({ source }) => source).join("\n"),
    /#open-settings/);
  assert.doesNotMatch(
    productionTypeScriptSources.map(({ source }) => source).join("\n"),
    /#taste-btn|#taste-popover|#taste-clear|tasteOpen/);
  for (const selector of [
    "#settings-close",
    ".settings-seg[data-theme]",
    ".settings-taste [data-taste]",
    "#settings-taste-clear",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("metadata viewer owns its rendered explorer control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const bindEvents = functionDeclaration("bindEvents");
  const renderMetadata = functionDeclaration("renderMetadataExplorer");
  const metadataEventBinder = functionDeclaration("bindMetadataViewerEvents");
  const outerCalls = callExpressionsNamed(appSyntax, "bindMetadataViewerEvents");
  assert.equal(outerCalls.length, 2);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindMetadataViewerEvents").length,
    3);
  const metadataBinders: readonly (readonly [DeclaredFunction, string])[] = [
    [bindEvents, "workbench metadata binder"],
    [renderMetadata, "metadata explorer binder"],
  ];
  for (const [owner, description] of metadataBinders) {
    assert.equal(
      callExpressionsNamed(owner, "bindMetadataViewerEvents").length,
      1,
      description);
    assert.equal(
      owner.body.body
        .map(statement => directCallExpression(statement, "bindMetadataViewerEvents"))
        .filter(Boolean)
        .length,
      1,
      `${description} direct call`);
  }
  const directWorkbenchCalls = bindEvents.body.body.map(directCallName);
  assert.equal(
    directWorkbenchCalls.indexOf("bindMetadataViewerEvents"),
    directWorkbenchCalls.indexOf("bindSettingsPanelEvents") + 1);
  const metadataRenderStatements = renderMetadata.body.body;
  const replacementIndex = metadataRenderStatements.findIndex(
    statement => statement.type === "ExpressionStatement"
      && statement.expression.type === "AssignmentExpression"
      && statement.expression.operator === "="
      && sourceText(statement.expression.left) === "app.innerHTML");
  const binderIndex = metadataRenderStatements.findIndex(
    statement => directCallExpression(statement, "bindMetadataViewerEvents"));
  assert.notEqual(replacementIndex, -1);
  assert.equal(binderIndex, replacementIndex + 1);

  const innerCall = onlyCallExpressionNamed(appSyntax, "bindMetadataExplorer");
  assert.equal(
    metadataEventBinder.body.body
      .map(statement => directCallExpression(statement, "bindMetadataExplorer"))
      .filter(Boolean)
      .length,
    1);
  assert.deepEqual(
    statementSignatures(metadataEventBinder.body.body.slice(0, 1)),
    ["declare:const ex = state.explorer"]);
  assert.equal(
    directCallExpression(
      statementAt(metadataEventBinder.body.body, 1, "bindMetadataExplorerEvents"),
      "bindMetadataExplorer"),
    innerCall);
  assert.deepEqual(
    statementSignatures(metadataEventBinder.body.body.slice(2, 3)),
    [
      {
        if: "!ex",
        whenTrue: ["statement:ReturnStatement:return;"],
        whenFalse: [],
      },
    ]);
  assert.equal(innerCall.arguments.length, 3);
  assertIdentifierArgument(innerCall, 0, "document", "bindMetadataExplorer");
  assertIdentifierArgument(innerCall, 1, "ex", "bindMetadataExplorer");
  const actions = objectArgument(innerCall, 2, "bindMetadataExplorer");
  assert.equal(actions.properties.length, 14);
  const rowFocus = callbackProperty(actions, "onRowFocus");
  assert.deepEqual(
    statementSignatures(rowFocus.body.body),
    [
      {
        if: "!ex",
        whenTrue: ["statement:ReturnStatement:return;"],
        whenFalse: [],
      },
      "declare:const already = ex.detail && ex.detail.index === index && ex.detail.rowId === rowId",
      "assign:ex.detail = already ? null : { index, rowId }",
      "assign:ex.highlight = already ? null : { index, rowId }",
      "declare:const current = ex.history[ex.historyPos]",
      {
        if: "current && current.index === index",
        whenTrue: ["assign:current.rowId = already ? 0 : rowId"],
        whenFalse: [],
      },
      "call:render()",
    ]);

  const binding = sourceText(innerCall);
  assert.match(
    binding,
    /bindMetadataExplorer\s*\(document, ex, \{[\s\S]*onClose: closeExplorer,[\s\S]*onHistoryBack: explorerHistoryBack,[\s\S]*onHistoryForward: explorerHistoryForward,[\s\S]*onHeapFocus: heap => pushExplorerFocus\(\{ heap \}\),[\s\S]*onJump: explorerJump,[\s\S]*onOpenHeap: openExplorerHeap,[\s\S]*onOpenOverview: openExplorerOverview,[\s\S]*onOpenTable: openExplorer,[\s\S]*onPage: \(index, startRowId\) =>\s*observeAsync\(\s*loadExplorerWindow\(index, startRowId\),\s*"Loading metadata table rows"\),[\s\S]*onRetryPackageMetadata: \(\) =>\s*observeAsync\(loadPackageMetadata\(\), "Retrying package metadata"\),[\s\S]*onRowFocus: \(index, rowId\) => \{[\s\S]*ex\.detail = already \? null : \{ index, rowId \};[\s\S]*ex\.highlight = already \? null : \{ index, rowId \};[\s\S]*onShowOverview: explorerShowOverview,[\s\S]*onTableFocus: \(index, rowId\) => pushExplorerFocus\(\{ index, rowId \}\),/);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.match(
    metadataViewerSource,
    /export function bindMetadataExplorer\([\s\S]*\[data-package-metadata-retry\][\s\S]*\[data-metadata-root\][\s\S]*#mde-exit[\s\S]*#mde-hist-back[\s\S]*#mde-hist-fwd[\s\S]*\[data-mde-explore\][\s\S]*\[data-mde-open\][\s\S]*\[data-mde-open-heap\][\s\S]*\[data-mde-chip\][\s\S]*\[data-mde-jump\][\s\S]*\[data-mde-overview\][\s\S]*\[data-mde-page\][\s\S]*\[data-mde-heap-chip\][\s\S]*\.mde-wall \.mde-card\[data-mde-index\] \.mde-card-head[\s\S]*\.mde-wall \.mde-heap-card\[data-mde-heap\] \.mde-card-head[\s\S]*\.mde-wall \.mde-row\[data-mde-row\][\s\S]*#mde-canvas[\s\S]*\.mde-focus \.mde-row\[data-mde-row\]/);
  for (const selector of [
    "#mde-exit",
    "#mde-hist-back",
    "#mde-hist-fwd",
    "[data-metadata-root]",
    "[data-mde-explore]",
    "[data-mde-open]",
    "[data-mde-open-heap]",
    "[data-mde-chip]",
    "[data-mde-jump]",
    "[data-mde-overview]",
    "[data-mde-page]",
    "[data-mde-heap-chip]",
    ".mde-wall .mde-card[data-mde-index] .mde-card-head",
    ".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head",
    ".mde-wall .mde-row[data-mde-row]",
    ".mde-focus .mde-row[data-mde-row]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
  assert.equal(appSource.split("#mde-canvas").length - 1, 1);
});

test("package opportunities owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindPackageOpportunitiesEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const bindEvents =
    appSource.match(
      /function bindEvents\(\) \{[\s\S]*?\n}\n\n(?=(?:async )?function )/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindPackageOpportunities\(document, \{\s*onLookForSelect: openSpotlight,[\s\S]*onTypeSelect: opportunity => \{[\s\S]*opportunity\.sourceIdentity === "legacy"[\s\S]*exact identity is unavailable[\s\S]*resolveOpportunitySourceCandidate\(\s*currentPackage\(\),\s*opportunity\)[\s\S]*candidate\.status !== "unique"[\s\S]*navigateToType\(candidate\.type\)/);
  assert.match(
    bindEvents,
    /^\s*bindPackageOpportunitiesEvents\(\);\s*$/m);
  const compositionPrefix =
    bindEvents.slice(0, bindEvents.indexOf("bindPackageOpportunitiesEvents();"));
  assert.equal(compositionPrefix.match(/\{/g)?.length, 1);
  assert.equal(compositionPrefix.match(/\}/g)?.length ?? 0, 0);
  assert.equal(
    appSource.match(/\bbindPackageOpportunitiesEvents\b/g)?.length,
    2);
  assert.equal(
    appSource.match(/\bbindPackageOpportunities\b/g)?.length,
    2);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.equal(binding.match(/\bdocument\b/g)?.length, 1);
  assert.match(
    packageOpportunitiesSource,
    /export function bindPackageOpportunities\([\s\S]*\[data-opp-type\][\s\S]*sourceDefinitionId: button\.dataset\.oppSourceDefinition[\s\S]*sourceAssembly: button\.dataset\.oppSourceAssembly[\s\S]*sourceAssemblyVersion: button\.dataset\.oppSourceVersion[\s\S]*sourceAssemblyCulture: button\.dataset\.oppSourceCulture[\s\S]*sourceAssemblyPublicKeyToken: button\.dataset\.oppSourceToken[\s\S]*\[data-opp-package\][\s\S]*\[data-opp-lookfor\]/);
  for (const selector of [
    "[data-opp-type]",
    "[data-opp-package]",
    "[data-opp-lookfor]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("modal viewers own their rendered close bindings", () => {
  const graphBinding =
    appSource.match(/function bindGraphSourceEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const docBinding =
    appSource.match(/function bindDocViewerEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    graphBinding,
    /bindGraphSource\(document, \{\s*onClose: closeGraphSource,\s*\}\)/);
  assert.match(
    docBinding,
    /bindDocViewer\(document, \{\s*onClose: closeDocViewer,\s*onOpenDocument: path =>\s*observeAsync\(openPackageDocument\(path\), "Opening a package document"\),\s*\}\)/);
  assert.equal(
    graphBinding.match(/\bbindGraphSource\(document\b/g)?.length,
    1);
  assert.equal(
    docBinding.match(/\bbindDocViewer\(document\b/g)?.length,
    1);
  assert.match(
    graphSourceViewerSource,
    /export function bindGraphSource\([\s\S]*#graph-source-backdrop[\s\S]*event\.target === backdrop[\s\S]*#graph-source-close/);
  assert.match(
    docViewerSource,
    /export function bindDocViewer\([\s\S]*#doc-viewer-backdrop[\s\S]*event\.target === backdrop[\s\S]*#doc-viewer-close[\s\S]*\[data-doc-path\]/);
  for (const [identifier, count] of [
    ["bindGraphSourceEvents", 2],
    ["bindDocViewerEvents", 2],
    ["bindGraphSource", 2],
    ["bindDocViewer", 2],
  ] as const) {
    assert.equal(
      appSource.match(new RegExp(`\\b${identifier}\\b`, "g"))?.length,
      count,
      identifier);
  }
  for (const selector of [
    "#graph-source-backdrop",
    "#graph-source-close",
    "#doc-viewer-backdrop",
    "#doc-viewer-close",
    "[data-doc-path]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("annotated source owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindAnnotatedSourceEvents\(\) \{[\s\S]*?\n}(?=\n\nconst workbenchShellActions)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindAnnotatedSource\(document, \{\s*onAction: applyAnnotatedSourceAction,\s*}\);/);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.equal(binding.match(/(?<!\.)\bdocument\b/g)?.length, 1);
  assert.match(
    annotatedSourceModule,
    /export function bindAnnotatedSource\([\s\S]*\[data-annotated-action\][\s\S]*\[data-annotated-source-start\][\s\S]*#annotated-source-backdrop[\s\S]*#annotated-source-modal/);
  for (const [identifier, count] of [
    ["bindAnnotatedSourceEvents", 2],
    ["bindAnnotatedSource", 2],
  ] as const) {
    assert.equal(
      appSource.match(new RegExp(`\\b${identifier}\\b`, "g"))?.length,
      count,
      identifier);
  }
  for (const selector of [
    "[data-annotated-action]",
    "[data-annotated-source-start]",
    "#annotated-source-backdrop",
    "#annotated-source-modal",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("annotated source validation failures stay visible at the shell boundary", () => {
  assert.match(
    appSource,
    /function renderAnnotatedSource\(result: AnnotatedSourceResult\) \{\s*try \{[\s\S]*renderAnnotatedSourcePure\([\s\S]*catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;\s*return renderAnnotatedSourceRejection\(error\)/,
  );
  assert.match(
    appSource,
    /function renderAnnotatedSourceModal\(\) \{[\s\S]*try \{[\s\S]*renderAnnotatedSourceModalPure\([\s\S]*catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;[\s\S]*Annotated source document rejected[\s\S]*data-annotated-action="close-modal"/,
  );
  assert.match(
    appSource,
    /function renderAnnotatedSourceRejection\(error: TypeError\) \{[\s\S]*Annotated source document rejected[\s\S]*escapeHtml\(errorMessage\(error\)\)/,
  );
  assert.match(
    appSource,
    /function dismissAnnotatedSourceModal\(restoreExploreFocus: boolean\) \{[\s\S]*try \{\s*model = createAnnotatedSourceViewerModel\(state\.memberAnnotated\);\s*\} catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;\s*state\.memberAnnotatedEmbedded = null;\s*state\.memberAnnotatedModal = null;[\s\S]*renderAndFocusAnnotated\("#annotated-source-rejection-title", "embedded"\);[\s\S]*return true;\s*\}[\s\S]*dismissModalSession\(model, state\.memberAnnotatedModal\)/,
  );
  assert.match(
    appSource,
    /if \(action\.kind === "close-modal"\) \{\s*dismissAnnotatedSourceModal\(true\);\s*return;\s*\}\s*const model = createAnnotatedSourceViewerModel\(result\)/,
  );
  assert.match(
    appSource,
    /catch \(error\) \{\s*if \(!\(error instanceof TypeError\) \|\| session\.surface !== "modal"\) throw error;\s*return dismissAnnotatedSourceModal\(true\);/,
  );
});

test("annotated source Escape and history ownership track the mounted surface", () => {
  assert.match(
    appSource,
    /const embeddedAnnotatedSourceDetailContextIsActive = \(\) =>\s*workspaceKeyboardContextIsActive\(\)\s*&& !workbenchOverlayOwnsFocus\(\)\s*&& state\.memberSection === "annotated"\s*&& Boolean\(state\.memberAnnotatedEmbedded\?\.detail\);\s*const annotatedSourceEscapeContextIsActive = \(\) =>\s*annotatedSourceContextIsActive\(\)\s*\|\| embeddedAnnotatedSourceDetailContextIsActive\(\)/);

  const dismiss =
    appSource.match(
      /function dismissModalsForRoutedNavigation\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    dismiss,
    /const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal\(false\)/);
  assert.match(dismiss, /return dismissedAnnotatedSourceModal/);

  const popstate =
    appSource.match(
      /window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation\(\);\s*invalidateMemberDestinationWork\(state\);[\s\S]*if \(dismissedAnnotatedSourceModal\) render\(\{ synchronizeUrl: false \}\);\s*if \(isDiagnosticsPath/);
  assert.match(
    appSource,
    /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\)[\s\S]*if \(productDemosRouteVisible\) \{\s*document\.title = "Demos — dotnet-inspect";\s*\} else if \(state\.rootKind !== "library"\s*&& options\.synchronizeUrl !== false\) \{\s*syncUrl\(\);\s*\}/);
});

test("package search state owner settles pending work and projects visible cache", () => {
  assert.match(
    spotlightPackageSearchSource,
    /if \(!packageScopeIsActive\(\)\) \{\s*state\.spotlightPackageSearch = settledPackageSearch\(current\);\s*return;/);
  assert.match(
    appSource,
    /id: "workspace\.drill-out-escape"[\s\S]*key: "Escape"[\s\S]*!isTextEntry\(\)/);
  assert.match(
    spotlightPackageSearchSource,
    /if \(query === cached\?\.query\) \{\s*state\.spotlightPackageSearch = cached;\s*return;/);
  assert.match(
    appSource,
    /visibleSpotlightPackageHits\(\s*state\.spotlightPackageSearch,\s*query,\s*\)/);
});

test("Spotlight async work is receipt-gated and refreshes either mounted surface", () => {
  assert.match(
    appSource,
    /createSpotlightPackageSearch\(\{[\s\S]*queryPackages: querySpotlightPackages,[\s\S]*updateResults: \(\) => spotlight\.updateResults\(\)/);
  assert.match(
    appSource,
    /schedule: \(callback, delay\) => setTimeout\(\(\) => void callback\(\), delay\),\s*cancelScheduled: handle => clearTimeout\(handle\),/);
  assert.match(
    spotlightPackageSearchSource,
    /state\.spotlightPackageSearch !== pending[\s\S]*state\.spotlightQuery\.trim\(\) !== query[\s\S]*!packageScopeIsActive\(\)/);
  assert.doesNotMatch(
    spotlightPackageSearchSource,
    /generation|spotlightPkgGeneration|spotlightPkgTimer/);
  assert.match(
    appSource,
    /window\.__platformIndex\.then\(index => \{[\s\S]*if \(state\.spotlightOpen\) spotlight\.refresh\(\)/);
  assert.doesNotMatch(appSource, /rtpack-suggest|data-sl-load-runtime/);
  assert.doesNotMatch(appSource, /function activateRuntimePack\(/);
});

test("global workbench shortcuts respect the topmost modal", () => {
  // The composition root wires the single link-navigation owner once; it must not regain
  // a raw document click listener of its own (see the `.addEventListener` count assertion
  // in "typed graph interactions own graph controls and Mermaid node bindings").
  assert.match(
    appSource,
    /bindWorkspaceLinkNavigation\(document, \{[\s\S]*currentOrigin: \(\) => location\.origin,[\s\S]*resolve: href => new URL\(href, location\.href\),[\s\S]*navigate: url => observeAsync\(\s*navigateInAppUrl\(url\),\s*"Opening the selected link"\),/);
  // Modal ownership is explicit priority policy, while the reusable registry remains
  // independent of inspect-web state and attaches the only raw keydown listener.
  assert.match(
    workbenchKeybindingsSource,
    /workspace: 100,[\s\S]*element: 200,[\s\S]*spotlight: 300,[\s\S]*documentViewer: 310,[\s\S]*graphSource: 320,[\s\S]*unavailableWorkspace: 330,[\s\S]*settings: 340,[\s\S]*metadataExplorer: 350/);
  assert.match(
    keybindingRegistrySource,
    /dispatch\(event: KeyboardEvent\): KeybindingDispatchResult[\s\S]*candidates\.sort\([\s\S]*candidate\.binding\.run\(event\)[\s\S]*event\.preventDefault\(\)[\s\S]*target\.addEventListener\("keydown", listener\)/);
  assert.match(
    appSource,
    /id: "graph-source\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.graphSource[\s\S]*when: graphSourceContextIsActive[\s\S]*closeGraphSource\(\)/);
  assert.match(
    appSource,
    /id: "document-viewer\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.documentViewer[\s\S]*when: documentViewerContextIsActive[\s\S]*closeDocViewer\(\)/);
  assert.match(
    appSource,
    /id: "spotlight\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.spotlight[\s\S]*when: spotlightContextIsActive[\s\S]*closeSpotlight\(\)/);
  assert.match(
    appSource,
    /id: "spotlight\.open-commands"[\s\S]*commandOrControl: true[\s\S]*openSpotlight\("", "commands"\)[\s\S]*id: "spotlight\.open-all"[\s\S]*openSpotlight\(\)[\s\S]*id: "spotlight\.contain-browser-find"/);
  assert.doesNotMatch(
    appSource,
    /id: "taste\.dismiss"|state\.tasteOpen/);
  assert.match(
    appSource,
    /function openSpotlight\(seed = "", spotlightScope: SpotlightScope = "all"\) \{\s*if \(state\.loading \|\| state\.error\) return;\s*beginSpotlightNavigation\(\)/);
  assert.match(
    spotlightSource,
    /function bind\(root: ParentNode, mode: "modal" \| "inline"\)[\s\S]*if \(mode === "modal"\)[\s\S]*focus\(selection\);/);
  assert.match(
    appSource,
    /id: "metadata-explorer\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.metadataExplorer[\s\S]*Boolean\(state\.explorer\?\.open\)[\s\S]*metadata-explorer\.contain-browser-shortcut/);
  assert.match(
    appSource,
    /id: "settings\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.settings[\s\S]*when: \(\) => state\.settings[\s\S]*settings\.contain-browser-shortcut/);
  assert.match(
    spotlightSource,
    /aria-activedescendant="spotlight-result-\$\{state\.spotlightIndex\}"[\s\S]*syncActiveDescendant\(items\.length\)/);
  assert.match(
    appSource,
    /const unavailableWorkspaceContext = \(\) =>[\s\S]*!state\.home && \(state\.loading \|\| Boolean\(state\.error\)\)[\s\S]*unavailable-workspace\.contain-browser-shortcut[\s\S]*unavailable-workspace\.contain-filter-shortcut/);
  assert.match(
    appSource,
    /function workspaceKeyboardContextIsActive\(\)[\s\S]*!state\.explorer\?\.open[\s\S]*!state\.settings[\s\S]*!state\.home[\s\S]*!state\.packageQueryOpen[\s\S]*!state\.loading[\s\S]*!state\.error[\s\S]*!graphSourceIsOpen\(state\.graphSource\)[\s\S]*!documentViewerIsOpen\(state\.docViewer\)[\s\S]*!state\.spotlightOpen/);
  assert.equal(
    keybindingRegistrySource.match(/addEventListener\("keydown"/g)?.length,
    1);
  assert.equal(
    [
      appSource,
      graphInteractionsSource,
      packageControlsSource,
      spotlightSource,
      typePanelSource,
    ].join("\n").match(/addEventListener\(\s*"keydown"/g)?.length ?? 0,
    0);
  assert.match(appSource, /keybindings\.attach\(document\)/);
  assert.match(
    appSource,
    /function focusFilter\([\s\S]*\{ immediate = false \}: \{ immediate\?: boolean \} = \{\},[\s\S]*const focus = \(\) => \{[\s\S]*"#member-filter, #type-filter"[\s\S]*if \(immediate\) \{\s*focus\(\);\s*return;\s*}\s*requestAnimationFrame\(focus\);/);
  assert.match(
    appSource,
    /function focusFilter\([\s\S]*input\.closest<HTMLDetailsElement>\(\s*"\[data-member-filter-disclosure\]"\)[\s\S]*input\.closest<HTMLDetailsElement>\(\s*"\[data-type-filter-disclosure\]"\)[\s\S]*state\.memberFiltersExpanded = true;[\s\S]*state\.typeFiltersExpanded = true;[\s\S]*disclosure\.open = true;[\s\S]*input\.focus\(\)/);
});

test("Spotlight navigation waits for selection data before restoring focus", () => {
  const typeLensLoader =
    appSource.match(/function loadSelectedTypeLensData\([\s\S]*?\n}/)?.[0];
  const selectionLoader =
    appSource.match(/function loadSelectionData\(\)[\s\S]*?\n}/)?.[0];
  assert.ok(typeLensLoader);
  assert.ok(selectionLoader);
  assert.match(typeLensLoader, /return loadSelectedTypeSource\(\)/);
  assert.match(typeLensLoader, /return loadSelectedTypeMetadata\(\)/);
  assert.match(
    selectionLoader,
    /const typeLensLoad = loadSelectedTypeLensData\(\);\s*if \(typeLensLoad !== "member"\) return typeLensLoad;/);
  assert.match(
    appSource,
    /async function loadPackageFromSpotlight[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*await loadPackage\([\s\S]*if \(loaded\) \{[\s\S]*destination = \(await buildStateUrl\(\)\)\.toString\(\);[\s\S]*failWorkspaceCatalogAction\([\s\S]*rollbackSnapshot,[\s\S]*return;[\s\S]*publishCurrentWorkspace\(retainedSnapshot\);\s*workspaceLocation\.push\(destination\);\s*render\(\{ synchronizeUrl: false \}\);\s*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function openPlatformLibrary[\s\S]*const navigationGeneration = scopeOnly \? null : beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*spotlight\.reset\(\)[\s\S]*await loadSelectionData\(\);[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function pickSpotlightMember[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*await loadSelectedMemberDocumentation\(\);[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*packageResult:[\s\S]*typeId: string,[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*await selectionData;[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /function selectWorkspacePackage\([\s\S]*if \(!packageModel\) return;\s*if \(navigationSeq === undefined\) navigationSequence\.begin\(\);\s*else if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;/);
  assert.match(
    appSource,
    /async function pickSpotlightMember\([\s\S]*if \(!pkg \|\| !type\)[\s\S]*const navigationSeq = navigationSequence\.begin\(\);[\s\S]*spotlightPlatformTypeIsAvailable\([\s\S]*const navigationGeneration = beginSpotlightNavigation\(\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*if \(!pkg \|\| !type\)[\s\S]*const navigationSeq = navigationSequence\.begin\(\);[\s\S]*spotlightPlatformTypeIsAvailable\([\s\S]*const navigationGeneration = beginSpotlightNavigation\(\)/);
  assert.match(
    appSource,
    /let spotlightFocusGeneration = 0;\s*let documentFocusGeneration = 0[\s\S]*function canRestoreWorkbenchFocus\([\s\S]*generation === spotlightFocusGeneration[\s\S]*focusGeneration === documentFocusGeneration[\s\S]*isTextEntry\(\)[\s\S]*function focusTypeList\([\s\S]*focusGeneration = documentFocusGeneration,[\s\S]*canRestoreWorkbenchFocus\(generation, focusGeneration\)/);
  assert.match(
    appSource,
    /captureFocusAfterDismiss: \(\) => \{\s*const navigationGeneration = spotlightFocusGeneration;\s*const focusGeneration = documentFocusGeneration;\s*return \(\) => restoreContentFrameFocusAfterDismiss\(\s*navigationGeneration,\s*focusGeneration\)/);
  assert.match(
    spotlightSource,
    /const generation = interactionGeneration;[\s\S]*const focusAfterExecution = \(\) => \{[\s\S]*generation === interactionGeneration[\s\S]*Promise\.resolve\(execution\)\.then\(\s*focusAfterExecution,\s*\(error: unknown\) => \{\s*options\.reportCommandError\(error\);\s*focusAfterExecution\(\)/);
});
