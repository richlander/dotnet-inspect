import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  activeSourceOperationKind,
  assemblyDescriptorForType,
  beginSourceRequestState,
  cancelSourceRequestState,
  callGraphAssemblyIdentityMatches,
  callGraphDiagnosticsMessage,
  callGraphTargetMatchesType,
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
  memberSectionIdsFor,
  mermaidLabel,
  normalizeShareTabs,
  packageCoordinateMatchesLocation,
  packageForView,
  packageIdentityKey,
  parameterTitleHtml,
  removeWorkspacePackage,
  removeAppendedNotice,
  replaceCurrentNavigationEntry,
  retainWorkspacePackage,
  resolveLoadedGraphTargetCandidate,
  shareStateLengthError,
  scopedRequestState,
  selectedDependencyGroup,
  sourceSurfaceIsVisible,
  sourceReloadKind,
  sourceRequestNeedsLoad,
  spotlightCandidateKey,
  spotlightCandidateSignature,
  uniqueTypeByQueryId,
  workspaceCoordinatesMatch
} from "../src/data.ts";
import {
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid
} from "../src/graph-mermaid.ts";

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

test("a successful retry removes only its appended failure notice", () => {
  const prior = "The workspace was truncated.";
  const failed = `${prior} Couldn’t load System.Net.Http: unavailable.`;

  assert.equal(removeAppendedNotice(failed, prior, failed), prior);
  assert.equal(
    removeAppendedNotice(
      `${failed} A later warning remains.`,
      prior,
      failed),
    `${prior} A later warning remains.`);
  assert.equal(
    removeAppendedNotice("A replacement warning.", prior, failed),
    "A replacement warning.");
});

test("normalizing a history entry keeps its consumed position and later entries", () => {
  const nav = {
    index: 1,
    stack: [
      { sig: "older", view: { id: "older" } },
      { sig: "stale", view: { id: "stale" } },
      { sig: "newer", view: { id: "newer" } },
    ],
  };

  replaceCurrentNavigationEntry(nav, "normalized", { id: "normalized" });

  assert.equal(nav.index, 1);
  assert.deepEqual(nav.stack, [
    { sig: "older", view: { id: "older" } },
    { sig: "normalized", view: { id: "normalized" } },
    { sig: "newer", view: { id: "newer" } },
  ]);
});

const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const workspaceNavigationSource = readFileSync(
  new URL("../src/workspace-navigation.ts", import.meta.url),
  "utf8");
const packageAcquisitionSource = readFileSync(
  new URL("../src/package-acquisition.ts", import.meta.url),
  "utf8");
const packageInspectionSource = readFileSync(
  new URL("../src/package-inspection.ts", import.meta.url),
  "utf8");
const sourceInspectionSource = readFileSync(
  new URL("../src/source-inspection.ts", import.meta.url),
  "utf8");
const metadataInspectionSource = readFileSync(
  new URL("../src/metadata-inspection.ts", import.meta.url),
  "utf8");
const memberDetailInspectionSource = readFileSync(
  new URL("../src/member-detail-inspection.ts", import.meta.url),
  "utf8");
const callGraphInspectionSource = readFileSync(
  new URL("../src/call-graph-inspection.ts", import.meta.url),
  "utf8");
const documentInspectionSource = readFileSync(
  new URL("../src/document-inspection.ts", import.meta.url),
  "utf8");
const spotlightPackageSearchSource = readFileSync(
  new URL("../src/spotlight-package-search.ts", import.meta.url),
  "utf8");
const catalogRequestsSource = readFileSync(
  new URL("../src/catalog-requests.ts", import.meta.url),
  "utf8");
const memberFocusSource = readFileSync(
  new URL("../src/member-focus.ts", import.meta.url),
  "utf8");
const graphSource = readFileSync(
  new URL("../src/graph-mermaid.ts", import.meta.url),
  "utf8");
const graphSourceViewerSource = readFileSync(
  new URL("../src/graph-source.ts", import.meta.url),
  "utf8");
const docViewerSource = readFileSync(
  new URL("../src/doc-viewer.ts", import.meta.url),
  "utf8");
const annotatedSourceModule = readFileSync(
  new URL("../src/annotated-source.ts", import.meta.url),
  "utf8");
const typePanelSource = readFileSync(
  new URL("../src/type-panel.ts", import.meta.url),
  "utf8");
const scopeBarSource = readFileSync(
  new URL("../src/scope-bar.ts", import.meta.url),
  "utf8");
const settingsPanelSource = readFileSync(
  new URL("../src/settings-panel.ts", import.meta.url),
  "utf8");
const packageBarSource = readFileSync(
  new URL("../src/package-bar.ts", import.meta.url),
  "utf8");
const metadataViewerSource = readFileSync(
  new URL("../src/metadata-viewer.ts", import.meta.url),
  "utf8");
const packageOpportunitiesSource = readFileSync(
  new URL("../src/package-opportunities.ts", import.meta.url),
  "utf8");
const applicationSources =
  `${appSource}\n${graphSource}\n${packageBarSource}\n${metadataViewerSource}`;
const stylesSource = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
const generatedEngineModuleUrl =
  new URL("../engine/wwwroot/inspect-web-engine.js", import.meta.url);
const generatedEngineSource = readFileSync(generatedEngineModuleUrl, "utf8");
const deploySource = readFileSync(
  new URL("../../../.github/workflows/deploy-inspect-web.yml", import.meta.url),
  "utf8");
const statusBarSource = readFileSync(
  new URL("../src/status-bar.ts", import.meta.url),
  "utf8");
const spotlightSource = readFileSync(
  new URL("../src/spotlight.ts", import.meta.url),
  "utf8");
const commandBarSource = readFileSync(
  new URL("../src/command-bar.ts", import.meta.url),
  "utf8");

test("typed Spotlight owns search presentation and hosts commands", () => {
  assert.match(
    appSource,
    /createSpotlight,[\s\S]*visibleSpotlightPackageHits,[\s\S]*from "\.\/spotlight\.ts"/);
  assert.match(appSource, /openSpotlight\("", "commands"\)/);
  assert.match(appSource, /state\.spotlightOpen \? spotlight\.modalHtml\(\)/);
  assert.match(appSource, /spotlight\.inlineHtml\(enginePending\)/);
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
  assert.match(appSource, /source: pkg\.source/);
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
  assert.doesNotMatch(appSource, /source: \{ kind: "(?:nuget\.org|platform)" \}/);
  assert.match(statusBarSource, /Source: \$\{escapeHtml\(packageSourceLabel\(model\.source\)\)\}/);
});

test("typed status bar owns its rendered toggle binding", () => {
  const binding =
    appSource.match(/function bindStatusBarEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindStatusBar\(document, \{\s*onToggle: \(\) => \{[\s\S]*state\.statusBarExpanded = !state\.statusBarExpanded;[\s\S]*render\(\);[\s\S]*\},\s*\}\)/);
  assert.equal(binding.match(/\bdocument\b/g)?.length, 1);
  assert.match(
    statusBarSource,
    /export function bindStatusBar\([\s\S]*\[data-status-bar-toggle-button\][\s\S]*actions\.onToggle/);
  assert.doesNotMatch(appSource, /\[data-status-bar-toggle-button\]/);
  assert.match(
    appSource,
    /function bindEvents\(\) \{\s*bindStatusBarEvents\(\);/);
  assert.match(
    appSource,
    /function bindHomeEvents\(\) \{\s*bindStatusBarEvents\(\);/);
  assert.equal(
    appSource.match(/\bbindStatusBarEvents\(\)/g)?.length,
    3);
});

test("typed package bar owns package framework and version selection bindings", () => {
  const packageBarCreation =
    appSource.match(/const packageBar = createPackageBar\(\{[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const packageBarBinding =
    packageBarSource.match(/  function bind\(root: ParentNode\): void \{[\s\S]*?\n  }(?=\n\n  return)/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    packageBarCreation,
    /selectFramework: switchPackageFramework,[\s\S]*selectVersion: version => \{[\s\S]*state\.package\?\.isRuntimePack[\s\S]*switchPlatformVersion\(version\);[\s\S]*else switchPackageVersion\(version\)/);
  assert.match(
    packageBarSource,
    /export function bindPackageSelections\([\s\S]*\[data-framework-chip\][\s\S]*#framework[\s\S]*#package-version/);
  assert.match(
    packageBarBinding,
    /bindPackageSelections\(root, \{\s*onFrameworkSelect: selectFramework,\s*onVersionSelect: selectVersion,\s*\}\)/);
  assert.equal(
    workspaceBinding.match(/\bpackageBar\.bind\(document\)/g)?.length,
    1);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelectorAll(?:<HTMLElement>)?\("\[data-framework-chip\]"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelector(?:<HTMLSelectElement>)?\("#(?:framework|package-version)"\)/);
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
        + "packageModel\\.version,\\s*packageModel\\.activeFramework\\)"));
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
        `${engine}\\(\\s*framework,\\s*assemblyFileName,\\s*pack\\)`));
  }
  assert.match(
    dependenciesLoader,
    /function loadPackageDependencies\(\) \{\s*return packageInspection\.loadDependencies\(/);
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
    /async open\(request: PackageDocumentRequest\)[\s\S]*state\.docViewerSeq/);
});

test("typed catalog requests own release and package-version coordination", () => {
  const releaseLoader =
    appSource.match(/function ensureDotnetReleases\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  const versionLoader =
    appSource.match(/function ensurePackageVersions\(pkg: AppPackage \| null\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createCatalogRequests\(\{[\s\S]*queryDotnetReleases,[\s\S]*queryPackageVersions: packageId => inspectPackageVersions\(packageId\),[\s\S]*updatePlatformVersionSelect,[\s\S]*updatePackageVersionSelect: updateVersionSelect,/);
  assert.match(
    appSource,
    /raw\.githubusercontent\.com\/dotnet\/core\/refs\/heads\/main\/release-notes\/releases-index\.json/);
  assert.match(releaseLoader, /return catalogRequests\.ensureDotnetReleases\(\)/);
  assert.match(versionLoader, /return catalogRequests\.ensurePackageVersions\(pkg\)/);
  assert.doesNotMatch(
    `${releaseLoader}\n${versionLoader}`,
    /dotnetReleasesLoading|packageVersionsLoading|state\.packages/);
  assert.match(
    catalogRequestsSource,
    /state\.dotnetReleasesLoading = true[\s\S]*dependencies\.queryDotnetReleases\(\)[\s\S]*state\.dotnetReleasesLoading = false/);
  assert.match(
    catalogRequestsSource,
    /state\.packageVersionsLoading\[packageId\] = true[\s\S]*dependencies\.queryPackageVersions\(packageId\)[\s\S]*packageIsResident\(packageId\)/);
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
  assert.match(
    binding,
    /bindTypePanel\(document, \{/);
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
    /function bindEvents\(\) \{\s*bindStatusBarEvents\(\);\s*packageBar\.bind\(document\);\s*bindTypePanelEvents\(\);/);
  assert.match(
    typePanelSource,
    /export function bindTypePanel\([\s\S]*\[data-type\][\s\S]*\[data-namespace\][\s\S]*\[data-kind-filter\][\s\S]*\[data-nav-member\][\s\S]*\[data-nav-overload\][\s\S]*#nav-to-types[\s\S]*#clear-filter[\s\S]*#namespace-jump[\s\S]*#type-list[\s\S]*#type-filter/);
  assert.match(
    typePanelSource,
    /\[data-member-kind-filter\][\s\S]*\[data-member-access-filter\][\s\S]*\[data-member-trait-filter\][\s\S]*#clear-member-filter[\s\S]*#member-filter/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-member-(?:kind|access|trait)-filter\]"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector(?:<HTMLInputElement>)?\("#(?:member-filter|clear-member-filter)"\)/);
  assert.match(
    binding,
    /onMemberFilterClear: \(\) => \{[\s\S]*resetMemberFilters\(\);[\s\S]*renderMemberFilterAndRestoreFocus\("#clear-member-filter"\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: event => \{\s*if \(event\.key === "Escape"\) \{\s*event\.preventDefault\(\);[\s\S]*navMode\(\) === "member"[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*event\.key !== "ArrowUp" && event\.key !== "ArrowDown"[\s\S]*event\.preventDefault\(\);\s*stepMemberNav\(event\.key === "ArrowDown" \? 1 : -1, true\)/);
  const selectorCount = selector =>
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
  assert.match(
    appSource,
    /function bindScopeBarEvents\(\) \{\s*bindScopeBar\(document, \{/);
  assert.match(
    scopeBarSource,
    /export function bindScopeBar\([\s\S]*\[data-scope\][\s\S]*\[data-package-lens\][\s\S]*\[data-lens\][\s\S]*\[data-member-section\]/);
  for (const selector of [
    "[data-scope]",
    "[data-package-lens]",
    "[data-lens]",
    "[data-member-section]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("typed settings panel owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindSettingsPanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindPackageOpportunitiesEvents)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindSettingsPanel\(document, \{\s*onClose: closeSettings,\s*onOpen: openSettings,\s*onTasteClear: clearTaste,\s*onTasteOpenToggle: \(\) => \{[\s\S]*state\.tasteOpen = !state\.tasteOpen;[\s\S]*render\(\);[\s\S]*\},\s*onTasteToggle: toggleTaste,\s*onThemeSelect: setTheme,/);
  assert.doesNotMatch(
    binding,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.equal(
    appSource.match(/\bbindSettingsPanelEvents\b/g)?.length,
    4);
  assert.equal(
    appSource.match(/\bbindSettingsPanel\b/g)?.length,
    2);
  assert.match(
    appSource,
    /function bindEvents\(\) \{\s*bindStatusBarEvents\(\);\s*packageBar\.bind\(document\);\s*bindTypePanelEvents\(\);\s*bindScopeBarEvents\(\);\s*bindSettingsPanelEvents\(\);/);
  assert.match(
    appSource,
    /function bindHomeEvents\(\) \{\s*bindStatusBarEvents\(\);\s*bindSettingsPanelEvents\(\);/);
  assert.match(
    appSource,
    /function renderSettingsViewHtml\(\) \{[\s\S]*\n  bindSettingsPanelEvents\(\);\n}\n\nfunction renderGraphSource\(/);
  assert.match(
    settingsPanelSource,
    /export function bindSettingsPanel\([\s\S]*#settings-close[\s\S]*#home-settings[\s\S]*#open-settings[\s\S]*#taste-btn[\s\S]*stopPropagation\(\)[\s\S]*\.settings-seg\[data-theme\][\s\S]*\.settings-taste \[data-taste\][\s\S]*#settings-taste-clear[\s\S]*#taste-popover \[data-taste\][\s\S]*#taste-clear/);
  const entrySelectorAccess =
    /(?:querySelector(?:All)?(?:<[^>\n]+>)?|getElementById)\(\s*["']#?(?:home-settings|open-settings|taste-btn)["']\s*\)/g;
  assert.equal(appSource.match(entrySelectorAccess)?.length ?? 0, 0);
  assert.equal(settingsPanelSource.match(entrySelectorAccess)?.length, 3);
  for (const selector of ["#home-settings", "#open-settings"]) {
    assert.equal(appSource.split(`"${selector}"`).length - 1, 0, selector);
    assert.equal(
      settingsPanelSource.split(`"${selector}"`).length - 1,
      1,
      selector);
  }
  assert.equal(appSource.match(/["']#taste-btn["']/g)?.length, 1);
  assert.equal(appSource.match(/\.closest\(["']#taste-btn["']\)/g)?.length, 1);
  assert.equal(settingsPanelSource.match(/["']#taste-btn["']/g)?.length, 1);
  for (const selector of [
    "#settings-close",
    ".settings-seg[data-theme]",
    ".settings-taste [data-taste]",
    "#settings-taste-clear",
    "#taste-popover [data-taste]",
    "#taste-clear",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("metadata viewer owns its rendered explorer control bindings", () => {
  const binding =
    appSource.match(/function bindMetadataViewerEvents\(\) \{[\s\S]*?\/\/ Hydrate cards/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindMetadataExplorer\(document, ex, \{[\s\S]*onClose: closeExplorer,[\s\S]*onHistoryBack: explorerHistoryBack,[\s\S]*onHistoryForward: explorerHistoryForward,[\s\S]*onHeapFocus: heap => pushExplorerFocus\(\{ heap \}\),[\s\S]*onJump: explorerJump,[\s\S]*onOpenHeap: openExplorerHeap,[\s\S]*onOpenTable: openExplorer,[\s\S]*onPage: \(index, startRowId\) => loadExplorerWindow\(index, startRowId\),[\s\S]*onRowFocus: \(index, rowId\) => \{[\s\S]*ex\.detail = already \? null : \{ index, rowId \};[\s\S]*ex\.highlight = already \? null : \{ index, rowId \};[\s\S]*onShowOverview: explorerShowOverview,[\s\S]*onTableFocus: \(index, rowId\) => pushExplorerFocus\(\{ index, rowId \}\),/);
  assert.equal(
    appSource.match(/\bbindMetadataViewerEvents\b/g)?.length,
    3);
  assert.equal(
    appSource.match(/\bbindMetadataExplorer\b/g)?.length,
    2);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.match(
    metadataViewerSource,
    /export function bindMetadataExplorer\([\s\S]*#mde-exit[\s\S]*#mde-hist-back[\s\S]*#mde-hist-fwd[\s\S]*\[data-mde-open\][\s\S]*\[data-mde-open-heap\][\s\S]*\[data-mde-chip\][\s\S]*\[data-mde-jump\][\s\S]*\[data-mde-overview\][\s\S]*\[data-mde-page\][\s\S]*\[data-mde-heap-chip\][\s\S]*\.mde-wall \.mde-card\[data-mde-index\] \.mde-card-head[\s\S]*\.mde-wall \.mde-heap-card\[data-mde-heap\] \.mde-card-head[\s\S]*\.mde-wall \.mde-row\[data-mde-row\][\s\S]*#mde-canvas[\s\S]*\.mde-focus \.mde-row\[data-mde-row\]/);
  for (const selector of [
    "#mde-exit",
    "#mde-hist-back",
    "#mde-hist-fwd",
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
    /bindPackageOpportunities\(document, \{\s*onLookForSelect: openSpotlight,\s*onPackageSelect: packageId => openDependencyPackage\(packageId, ""\),\s*onTypeSelect: typeId => \{[\s\S]*currentPackage\(\)\.types\.find\(item => item\.id === typeId\);\s*if \(!target\) \{[\s\S]*openSpotlight\(shortTypeName\(typeId\)\);\s*return;\s*\}[\s\S]*state\.atPackageRoot = false;[\s\S]*navigateToTypeByName\(typeId\)/);
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
    /export function bindPackageOpportunities\([\s\S]*\[data-opp-type\][\s\S]*\[data-opp-package\][\s\S]*\[data-opp-lookfor\]/);
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
    /bindDocViewer\(document, \{\s*onClose: closeDocViewer,\s*onOpenDocument: openPackageDocument,\s*\}\)/);
  assert.equal(graphBinding.match(/\bdocument\b/g)?.length, 1);
  assert.equal(docBinding.match(/\bdocument\b/g)?.length, 1);
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
  ]) {
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
    appSource.match(/function bindAnnotatedSourceEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindAnnotatedSource\(document, \{[\s\S]*onClearSelection: \(\) => \{[\s\S]*memberAnnotatedFactId = null;[\s\S]*memberAnnotatedNodeIds = \[\];[\s\S]*onCopy: async \(\) => \{[\s\S]*memberAnnotated\.document\.text[\s\S]*onFactSelect: factId => \{[\s\S]*memberAnnotatedFactId === factId \? null : factId[\s\S]*onMediumToggle: medium => \{[\s\S]*MEDIA\.includes[\s\S]*MEDIA\.some\(candidate => next\[candidate\]\)[\s\S]*onOffsetSelect: offset => \{[\s\S]*nodeAtOffset\(state\.memberAnnotated\.document, offset\)[\s\S]*factsForNode/);
  assert.match(
    binding,
    /\[typedMedium\]: !state\.memberAnnotatedMedia\[typedMedium\],[\s\S]*if \(!MEDIA\.some\(candidate => next\[candidate\]\)\) return;/);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.equal(binding.match(/(?<!\.)\bdocument\b/g)?.length, 1);
  assert.match(
    annotatedSourceModule,
    /export function bindAnnotatedSource\([\s\S]*#copy-annotated[\s\S]*\[data-annotated-medium\][\s\S]*\[data-annotated-fact\][\s\S]*\[data-annotated-offset\][\s\S]*#annotated-clear/);
  for (const [identifier, count] of [
    ["bindAnnotatedSourceEvents", 2],
    ["bindAnnotatedSource", 2],
  ]) {
    assert.equal(
      appSource.match(new RegExp(`\\b${identifier}\\b`, "g"))?.length,
      count,
      identifier);
  }
  for (const selector of [
    "#copy-annotated",
    "[data-annotated-medium]",
    "[data-annotated-fact]",
    "[data-annotated-offset]",
    "#annotated-clear",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("leaving package search clears its pending loading state", () => {
  assert.match(
    spotlightPackageSearchSource,
    /state\.spotlightScope !== "all"[\s\S]*state\.spotlightPkgLoading = false;[\s\S]*return;/);
  assert.match(
    appSource,
    /event\.key === "Escape" && !event\.defaultPrevented && !typing/);
  assert.match(
    spotlightPackageSearchSource,
    /query === state\.spotlightPkgQuery[\s\S]*generation\+\+;[\s\S]*state\.spotlightPkgLoading = false;[\s\S]*return;/);
  assert.match(
    appSource,
    /visibleSpotlightPackageHits\(\s*query,\s*state\.spotlightPkgQuery,\s*state\.spotlightPkgHits,\s*\)/);
});

test("Spotlight async work is generation-gated and refreshes either mounted surface", () => {
  assert.match(
    appSource,
    /createSpotlightPackageSearch\(\{[\s\S]*queryPackages: querySpotlightPackages,[\s\S]*updateResults: \(\) => spotlight\.updateResults\(\)/);
  assert.match(
    appSource,
    /schedule: \(callback, delay\) => setTimeout\(\(\) => void callback\(\), delay\),\s*cancelScheduled: handle => clearTimeout\(handle\),/);
  assert.match(
    spotlightPackageSearchSource,
    /requestGeneration !== generation[\s\S]*state\.spotlightQuery\.trim\(\) !== query/);
  assert.doesNotMatch(appSource, /spotlightPkgGeneration|spotlightPkgTimer/);
  assert.match(
    appSource,
    /if \(!state\.spotlightOpen && !state\.home\) return;[\s\S]*spotlight\.refresh\(\)/);
});

test("global workbench shortcuts respect the topmost modal", () => {
  assert.match(
    appSource,
    /if \(state\.home\) return;[\s\S]*if \(state\.graphSourceOpen\)[\s\S]*if \(state\.docViewerOpen\)[\s\S]*if \(state\.spotlightOpen\)/);
  assert.match(
    appSource,
    /state\.spotlightOpen[\s\S]*event\.key\.toLowerCase\(\) === "k"[\s\S]*event\.preventDefault\(\);[\s\S]*openSpotlight\("", "commands"\)/);
  assert.match(
    appSource,
    /state\.spotlightOpen[\s\S]*event\.key\.toLowerCase\(\) === "p"[\s\S]*event\.preventDefault\(\);[\s\S]*openSpotlight\(\)/);
  assert.match(
    appSource,
    /state\.spotlightOpen[\s\S]*event\.key\.toLowerCase\(\) === "f"[\s\S]*event\.preventDefault\(\)/);
  assert.match(
    appSource,
    /state\.spotlightOpen[\s\S]*event\.key === "Escape"[\s\S]*closeSpotlight\(\)/);
  assert.match(
    appSource,
    /function openSpotlight\(seed = "", scope = "all"\) \{\s*if \(state\.loading \|\| state\.error\) return;\s*state\.tasteOpen = false;/);
  assert.match(
    spotlightSource,
    /function bind\(root: ParentNode, mode: "modal" \| "inline"\)[\s\S]*if \(mode === "modal"\)[\s\S]*focus\(\);/);
  assert.match(
    appSource,
    /if \(state\.explorer\?\.open\)[\s\S]*isContainedBrowserShortcut\(event\)[\s\S]*event\.preventDefault\(\)/);
  assert.match(
    appSource,
    /if \(state\.settings\)[\s\S]*isContainedBrowserShortcut\(event\)[\s\S]*event\.preventDefault\(\)/);
  assert.match(
    spotlightSource,
    /aria-activedescendant="spotlight-result-\$\{state\.spotlightIndex\}"[\s\S]*syncActiveDescendant\(items\.length\)/);
  assert.match(
    appSource,
    /if \(state\.loading \|\| state\.error\) \{\s*if \(isContainedBrowserShortcut\(event\) \|\| event\.key === "\/"\)[\s\S]*event\.preventDefault\(\);[\s\S]*return;/);
  assert.match(
    appSource,
    /function focusFilter\(\) \{[\s\S]*const input = document\.querySelector<HTMLInputElement>\([\s\S]*"#member-filter, #type-filter"\);\s*if \(!input\) return;/);
});

test("Spotlight navigation waits for selection data before restoring focus", () => {
  const selectionLoader =
    appSource.match(/function loadSelectionData\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(selectionLoader, /return loadSelectedTypeSource\(\)/);
  assert.match(selectionLoader, /return loadSelectedTypeMetadata\(\)/);
  assert.match(
    appSource,
    /async function loadPackageFromSpotlight[\s\S]*await loadPackage\([\s\S]*focusTypeList\(focusGeneration\)/);
  assert.match(
    appSource,
    /async function openPlatformLibrary[\s\S]*spotlight\.reset\(\)[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*await selectionData;[\s\S]*focusTypeList\(focusGeneration\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*packageResult:[\s\S]*typeId: string,[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*await selectionData;[\s\S]*focusTypeList\(focusGeneration\)/);
  assert.match(
    appSource,
    /let spotlightFocusGeneration = 0[\s\S]*function focusTypeList\(generation = spotlightFocusGeneration\)[\s\S]*generation !== spotlightFocusGeneration[\s\S]*isTextEntry\(\)/);
  assert.match(
    spotlightSource,
    /const generation = interactionGeneration;[\s\S]*Promise\.resolve\(execution\)\.then\(\(\) => \{\s*if \(generation === interactionGeneration\)/);
});

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
    /<\/main>[\s\S]{0,700}\$\{statusBarHtml\(\{/);
  assert.match(statusBarSource, /"statusbar data-bar"/);
  assert.match(statusBarSource, /buildIdentityHtml\(model\.buildIdentity/);
  assert.match(
    appSource,
    /variant: "home"[\s\S]{0,200}buildIdentity: state\.buildIdentity/);
  assert.match(
    statusBarSource,
    /identity\.commitUrl[\s\S]*target="_blank" rel="noopener noreferrer"/);
  assert.match(statusBarSource, /built \$\{escapeHtml\(builtAt\)\} UTC/);
  assert.match(
    deploySource,
    /-getProperty:VersionPrefix[\s\S]*-p:VersionPrefix="\$version"[\s\S]*-p:SourceRevisionId="\$GITHUB_SHA"[\s\S]*-p:BuildTimestampUtc="\$built_at"/);
});

test("bare home paints before wasm engine download", () => {
  const homePaintWait =
    appSource.match(/function waitForHomePaint\(\)[\s\S]*?\n}\n\nfunction loadStoredTaste/)?.[0] ?? "";
  const errorPackageRecovery =
    appSource.match(/function openPackageFromError[\s\S]*?\n}\n\nfunction renderLoading/)?.[0] ?? "";
  const loadingView =
    appSource.match(/function renderLoading\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberDocumentation/)?.[0] ?? "";
  assert.doesNotMatch(appSource, /from "\/engine\.js"/);
  assert.doesNotMatch(appSource, /from "\/inspect-web-engine\.js"/);
  assert.match(
    appSource,
    /async function loadEngineModule\(\)[\s\S]*await import\("\/inspect-web-engine\.js"\)/);
  assert.match(
    homePaintWait,
    /first-contentful-paint[\s\S]*observer\.observe\(\{ type: "paint", buffered: true \}\)/);
  assert.match(
    homePaintWait,
    /requestAnimationFrame\(\(\) => setTimeout\(resolve, 0\)\)/);
  assert.match(
    appSource,
    /state\.loading = !state\.home;[\s\S]*render\(\);[\s\S]*if \(state\.home\) await waitForHomePaint\(\);[\s\S]*await loadEngineModule\(\);[\s\S]*await initializeEngine\(reportEngineStatus\);[\s\S]*reportEngineStatus\("Reading package assemblies…"\)/);
  assert.match(
    appSource,
    /class="home-search \$\{enginePending[\s\S]*class="home-engine-status"/);
  assert.match(
    `${appSource}\n${statusBarSource}`,
    /state\.engineReady[\s\S]*browser wasm ready[\s\S]*browser wasm loading/);
  assert.match(
    appSource,
    /state\.retryAction = \(\) => window\.location\.reload\(\)/);
  assert.match(
    errorPackageRecovery,
    /if \(!state\.engineReady\) \{[\s\S]*window\.location\.assign\(url\);[\s\S]*return;[\s\S]*\}\s*loadPackage\(packageId, version, ""\)/);
  assert.match(
    loadingView,
    /#error-package-query[\s\S]*openPackageFromError\(packageId, version\)/);
  assert.doesNotMatch(
    loadingView,
    /#error-package-query[\s\S]*loadPackage\(packageId, version/);
});

test("Spotlight uses local type matches until the engine is ready", () => {
  const typeMatches =
    appSource.match(/function spotlightTypeMatches[\s\S]*?\n}\n\n\/\/ Flat member index/)?.[0] ?? "";
  assert.match(
    typeMatches,
    /if \(!state\.engineReady\) return spotlightFallbackMatches\(query, cache\.pool\);[\s\S]*inspectSearchTypes\(query, cache\.candidatesJson\)/);
});

test("loading brand links back to the site root", () => {
  assert.match(
    appSource,
    /<a class="loading-brand" href="\/" aria-label="dotnet inspect home"><span>◇<\/span> dotnet-inspect<\/a>/);
  assert.match(
    stylesSource,
    /\.loading-brand\s*\{[^}]*text-decoration: none;/s);
});

test("member filters retain accessible controls and focus across rerenders", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /id="clear-member-filter"[^>]*aria-label="Clear member filters"/);
  assert.match(
    memberDetailInspectionSource,
    /async loadDocumentation\(request\)[\s\S]*const preservedFocus = dependencies\.renderPreservingMemberFocus\(\);[\s\S]*state\.memberDocumentationLoading = false;[\s\S]*dependencies\.renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    stylesSource,
    /\.type-browser:not\(\.member-nav\) \.namespace-chips, \.pane-footer \{ display: none; \}/);
  assert.match(
    typePanelSource,
    /memberFilter\?\.addEventListener\(\s*"input",\s*\(\) => actions\.onMemberFilterChange\(memberFilter\.value\)\)/);
  assert.match(
    binding,
    /onMemberFilterChange: value => \{[\s\S]*state\.memberTextFilter = value;[\s\S]*renderPreservingMemberFocus\(\)/);
  assert.match(
    typePanelSource,
    /memberFilter\?\.addEventListener\(\s*"keydown",\s*actions\.onMemberFilterKeyDown\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: event => \{[\s\S]*event\.key === "Escape"[\s\S]*if \(navMode\(\) === "member"\)[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*renderMemberFilterAndRestoreFocus\("#member-filter"\)[\s\S]*stepMemberNav/);
  assert.match(
    appSource,
    /event\.key === "Escape" && !event\.defaultPrevented && !typing[\s\S]*if \(navMode\(\) === "member"\) exitMemberScope\(\)/);
  assert.match(
    appSource,
    /onShowTypes: exitMemberScope/);
  assert.match(
    appSource,
    /const renderMemberFilterAndRestoreFocus = \(selector = ""\) => \{[\s\S]*renderWithMemberFocus\(preserved\)/);
  assert.match(
    memberFocusSource,
    /active\?\.id === "type-list"[\s\S]*selector = "#type-list"/);
  const platformDrill =
    appSource.match(/async function drillPlatformNode\([\s\S]*?\n}\n\nfunction popPlatformDrill/)?.[0]
    ?? "";
  assert.match(
    platformDrill,
    /return callGraphInspection\.drill\(\{[\s\S]*type: callGraphTargetTypeId\(node\)[\s\S]*metadataToken: node\.metadataToken \?\? 0/);
  const coordinatedDrill =
    callGraphInspectionSource.match(/async drill\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.equal(
    [...coordinatedDrill.matchAll(
      /dependencies\.renderPreservingMemberFocus\(preservedFocus\)/g)].length,
    2);
  const platformNavigation =
    appSource.match(/async function navigateOrDrillPlatform\([\s\S]*?\n}\n\n\/\/ Enter the resident runtime pack/)?.[0]
    ?? "";
  assert.match(
    platformNavigation,
    /const originSignature = memberRequestSignature\(type, overload, true\);[\s\S]*state\.memberSection === "call-graph"[\s\S]*memberRequestIsCurrent\(originSignature, true\)[\s\S]*loadRuntimePack\([\s\S]*ownsNavigation\)[\s\S]*if \(!ownsNavigation\(\)\) \{[\s\S]*state\.platformDrillLoading = false;[\s\S]*state\.platformDrillError = runtimeResult\.failureMessage[\s\S]*state\.runtimePackError[\s\S]*renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    appSource,
    /function applyMemberSection\(id: MemberSection\) \{[\s\S]*state\.memberSection === "call-graph" && id !== "call-graph"[\s\S]*invalidateMemberCallGraphWork\(state\)/);
  assert.match(
    appSource,
    /function navigateToRuntimeMember\([\s\S]*const targetLibrary = libraryKey\(type\);\s*state\.libraryScope = targetLibrary \? new Set\(\[targetLibrary\]\) : null;[\s\S]*state\.typeCursor = Math\.max\(0, filteredTypes\(\)/);
});

test("shared member views retain scope and filter state", () => {
  const capture = appSource.match(
    /function captureWorkspaceUrlState\(\)[\s\S]*?\n}\n\nfunction buildStateUrl/)?.[0] ?? "";
  const encoder = workspaceNavigationSource.match(
    /function encodeWorkspaceShareState\([\s\S]*?\n}\n\nfunction decodeWorkspaceShareState/)?.[0] ?? "";
  const decoder = workspaceNavigationSource.match(
    /function decodeWorkspaceShareState\([\s\S]*?\n}\n\nfunction resolveView/)?.[0] ?? "";
  const deepLink = appSource.match(
    /function applyDeepLink\([\s\S]*?\n}\n\n\/\/ Kick off/)?.[0] ?? "";
  assert.match(encoder, /packet\.b = 1/);
  assert.match(encoder, /packet\.q = state\.memberTextFilter/);
  assert.match(encoder, /packet\.k = state\.memberKindFilter/);
  assert.match(encoder, /packet\.e = state\.memberAccessibilityFilter/);
  assert.match(encoder, /packet\.r = state\.memberTraitFilter/);
  assert.match(encoder, /packet\.d = encodeBodyTarget\(state\.selectedBodyTarget\)/);
  assert.match(decoder, /memberBrowse: raw\.b === 1/);
  assert.match(decoder, /bodyTarget: decodeBodyTarget\(raw\.d\)/);
  assert.match(capture, /selectedBodyTarget: state\.selectedBodyTarget/);
  assert.match(capture, /memberBrowse: memberScopeIsActive\(state, selectedType\(\)\?\.id\)/);
  assert.match(capture, /memberTextFilter: state\.memberTextFilter/);
  assert.match(capture, /memberKindFilter: state\.memberKindFilter/);
  assert.match(capture, /memberAccessibilityFilter: state\.memberAccessibilityFilter/);
  assert.match(capture, /memberTraitFilter: state\.memberTraitFilter/);
  assert.match(appSource, /if \(deep\.memberBrowse && groups\.length\)\s*state\.memberBrowseTypeId = type\.id/);
  assert.match(
    deepLink,
    /state\.memberSection = "overview";\s*state\.selectedBodyTarget = null;\s*if \(restoreType && deep\)[\s\S]*bodyTargetMatchesOverload\(deep\.bodyTarget, group, restoredOverload\)[\s\S]*state\.selectedBodyTarget = deep\.bodyTarget \?\? null/);
  assert.match(
    appSource,
    /function selectMemberNavEntry\(entry: MemberNavEntry, focusList: boolean\) \{\s*const preservedFocus = captureMemberFocus\(document\);[\s\S]*memberFocusRestorer\.schedule\(\s*document,\s*preservedFocus/);
  assert.match(
    appSource,
    /window\.addEventListener\("popstate"[\s\S]*const deep = loc;[\s\S]*restoreWorkspaceFromLocation\(loc, deep, navigationSeq\)/);
});

test("member entry controls move focus into the resulting member navigation", () => {
  const bindings =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  assert.match(
    bindings,
    /const enterMemberNavigation = \(action: \(\) => void\) => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*action\(\);\s*focusTypeList\(focusGeneration\);/);
  assert.match(
    bindings,
    /data-member-jump-kind[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*enterMemberScope\(\);[\s\S]*data-member-jump-access[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*data-member-jump-trait[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*data-member\]"\)\.forEach[\s\S]*enterMemberNavigation\(\(\) => openMemberGroup/);
});

test("package tab selection resets type-specific member filters", () => {
  const selection =
    appSource.match(/function selectPackageTab\([\s\S]*?\n}\n\nfunction closePackageTab/)?.[0]
    ?? "";
  assert.match(
    selection,
    /state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);[\s\S]*resetMemberFilters\(\);[\s\S]*resetMemberSectionState\(\)/);
});

test("loaded-package Spotlight selection resets type-specific member filters", () => {
  const selection =
    appSource.match(/function pickSpotlightLoadedPackage\([\s\S]*?\n}\n\nasync function pickSpotlightMember/)?.[0]
    ?? "";
  assert.match(
    selection,
    /state\.selectedTypeId = "";[\s\S]*resetMemberFilters\(\);[\s\S]*resetMemberSectionState\(\)/);
});

test("foreground package reload resets filters before selecting its first type", () => {
  const loadPackage =
    appSource.match(/async function loadPackage\([\s\S]*?\n}\n\nfunction runtimePackLoaded/)?.[0]
    ?? "";
  assert.match(
    loadPackage,
    /if \(deep && \(deep\.type \|\| deep\.member\)\) \{[\s\S]*applyDeepLink\(deep\);[\s\S]*\} else \{\s*resetMemberFilters\(\);\s*state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);/);
});

test("home demos restore the complete parsed location", () => {
  const runHomeDemo =
    appSource.match(/function runHomeDemo\([\s\S]*?\n}\n\n\/\/ Return to the intro/)?.[0]
    ?? "";
  assert.match(runHomeDemo, /restoreWorkspaceFromLocation\(loc, loc\)/);
  assert.doesNotMatch(runHomeDemo, /type: loc\.type/);
  const restoreWorkspace =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\n\/\/ Restores the full open-tab/)?.[0]
    ?? "";
  assert.match(
    restoreWorkspace,
    /applyLocationView\(loc\);[\s\S]*await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);[\s\S]*applyDeepLink\(deep\)/);
  assert.match(
    appSource,
    /function applyLocationView\(loc: ParsedLocation\) \{\s*state\.lens = loc\.lens \|\| "api";\s*state\.atPackageRoot = loc\.atPackageRoot \|\| false;\s*state\.packageLens = loc\.packageLens \|\| "overview";/);
  const callGraphDemo =
    appSource.match(/async function runCallGraphDemo\(\) \{[\s\S]*?\n}\n\n\/\/ Loads the full/)?.[0]
    ?? "";
  assert.match(
    callGraphDemo,
    /state\.selectedTypeId = type\.id;\s*state\.atPackageRoot = false;\s*state\.lens = "api";\s*state\.packageLens = "overview";\s*resetMemberFilters\(\);\s*resetMemberSectionState\(\);\s*state\.memberBrowseTypeId = type\.id;[\s\S]*state\.selectedMemberKey = member\.key;[\s\S]*state\.selectedOverloadIndex = overloadIndex;[\s\S]*state\.memberSection = "call-graph"/);
  const platformHistory =
    appSource.match(/async function restorePlatformScopeThenDeepLink\([\s\S]*?\n}\n\n\/\/ Load and scope/)?.[0]
    ?? "";
  assert.match(
    platformHistory,
    /await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);\s*applyDeepLink\(loc\)/);
  const runtimeHistory =
    appSource.match(/async function restoreRuntimePackFromHistory\([\s\S]*?\n}\n\nbootstrap\(\);/)?.[0]
    ?? "";
  assert.match(
    runtimeHistory,
    /await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);\s*applyDeepLink\(deep\)/);
});

test("opening an already-resident Platform resets type-specific member filters", () => {
  const openPlatform =
    appSource.match(/async function openRuntimePackFromHome\([\s\S]*?\n}\n\n\/\/ The inspector-bot/)?.[0]
    ?? "";
  assert.match(
    openPlatform,
    /state\.atPackageRoot = true;\s*state\.packageLens = "overview";[\s\S]*resetMemberFilters\(\);\s*state\.selectedTypeId = defaultVisibleTypeId\(pack\);/);
});

test("lens-scoped Platform library changes reset type-specific member state", () => {
  const picker =
    appSource.match(/const bindPlatformLensPicker = [\s\S]*?bindPlatformLensPicker\("data-platform-integrations-library"/)?.[0]
    ?? "";
  assert.match(
    picker,
    /const openLibrary = async \([\s\S]*originPackage: AppPackage = currentPackage\(\),[\s\S]*noticeRetryState: NoticeRetryState \| null = null[\s\S]*if \(!state\.packages\.includes\(originPackage\)[\s\S]*!packageIdentityEquals\(state\.package, originPackage\)[\s\S]*state\.queryNoticeRetryAction === noticeRetryState\.action[\s\S]*state\.queryNotice = removeAppendedNotice\([\s\S]*state\.queryNoticeRetryAction = null;[\s\S]*const runtimeResult = await loadRuntimePackAssembly\([\s\S]*\(\) => state\.packages\.includes\(originPackage\)\);[\s\S]*const loaded = runtimeResult\.packageModel;[\s\S]*previous: state\.queryNotice[\s\S]*const retryAction = \(\) =>\s*openLibrary\(name, pack, originPackage, noticeState\);[\s\S]*runtimeResult\.failureMessage[\s\S]*noticeState\.appended = state\.queryNotice;[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*state\.libraryScope = new Set\(\[key\]\);[\s\S]*normalizeLibrarySelection\(\);[\s\S]*loader\(\)/);
  assert.doesNotMatch(picker, /select\.isConnected/);
  assert.match(
    appSource,
    /function normalizeLibrarySelection\(\) \{[\s\S]*state\.selectedTypeId = first\?\.id \|\| "";[\s\S]*state\.selectedMemberKey = "";[\s\S]*state\.selectedOverloadIndex = null;[\s\S]*resetMemberFilters\(\)[\s\S]*function afterLibraryScopeChange\(\) \{\s*normalizeLibrarySelection\(\);\s*render\(\)/);
});

test("authoritative location restore clears filters and applies aggregate Platform scope", () => {
  assert.match(
    appSource,
    /function resetLocationFilters\(\) \{\s*state\.typeFilter = "";\s*state\.namespaceFilter = "";\s*state\.kindFilter = "";\s*state\.libraryScope = null;\s*state\.typeCursor = 0;\s*resetMemberFilters\(\)/);
  const workspaceRestore =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction applyLocationView/)?.[0]
    ?? "";
  assert.match(workspaceRestore, /resetLocationFilters\(\);\s*clearWorkspacePackages\(\)/);
  assert.match(
    workspaceRestore,
    /if \(isRuntimePackId\(targetModel\.id\)\) \{\s*const scoped = await applyPlatformLibraryScope\(\s*loc\.library/);
  const popstate =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(popstate, /if \(bareHome\)[\s\S]*resetLocationFilters\(\);\s*const deep = loc/);
  const runtimeHistory =
    appSource.match(/async function restoreRuntimePackFromHistory\([\s\S]*?\n}\n\nbootstrap\(\);/)?.[0]
    ?? "";
  assert.match(
    runtimeHistory,
    /activatePackage\(pack,[\s\S]*await applyPlatformLibraryScope\(\s*loc\.library,[\s\S]*applyLocationView\(loc\)/);
});

test("type projection completions render only while current and preserve navigation focus", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    typeSource,
    /const preservedFocus = dependencies\.renderPreservingMemberFocus\(\);[\s\S]*const ownsRequest = \(\) =>[\s\S]*if \(ownsRequest\(\)\) \{\s*state\.typeSourceLoading = false;\s*if \(request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(preservedFocus\);/);
  assert.doesNotMatch(typeSource, /finally \{[\s\S]*dependencies\.render\(\)/);
  const typeMetadata =
    appSource.match(/async function loadSelectedTypeMetadata\([\s\S]*?\n}\n\n\/\/ Projects/)?.[0]
    ?? "";
  assert.match(
    typeMetadata,
    /return metadataInspection\.loadTypeMetadata\(\{[\s\S]*packageId: pkg\.id,[\s\S]*assembly: type\.assembly,[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*isVisible: \(\) => \{[\s\S]*!state\.home[\s\S]*!state\.settings[\s\S]*!state\.explorer\?\.open[\s\S]*!state\.loading[\s\S]*!state\.error[\s\S]*!workbenchOverlayOwnsFocus\(\)[\s\S]*typeMetadataSignature\(currentType, pkg\) === signature/);
  assert.doesNotMatch(typeMetadata, /typeMetadataGeneration|inspectTypeProjection/);
  const typeMetadataCoordinator =
    metadataInspectionSource.match(/async loadTypeMetadata\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    typeMetadataCoordinator,
    /const generation = \+\+state\.typeMetadataGeneration;[\s\S]*const preservedFocus = dependencies\.renderPreservingMemberFocus\(\);[\s\S]*generation === state\.typeMetadataGeneration[\s\S]*if \(ownsRequest\(\)\) state\.typeMetadata = result;[\s\S]*if \(request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(preservedFocus\);/);
  assert.doesNotMatch(
    typeMetadataCoordinator,
    /renderPreservingMemberFocus\(preservedFocus\);\s*if \(state\.typeMetadata\?\.graphNodes/);
});

test("metadata explorer request coordination stays outside the composition root", () => {
  const windowLoader =
    appSource.match(/async function loadExplorerWindow\([\s\S]*?\n}\n\n\/\/ Lists/)?.[0]
    ?? "";
  const heapLoader =
    appSource.match(/async function loadExplorerHeap\([\s\S]*?\n}\n\/\/ ref->def/)?.[0]
    ?? "";
  assert.match(
    windowLoader,
    /return metadataInspection\.loadExplorerWindow\(index, startRowId, maxRows\)/);
  assert.match(
    heapLoader,
    /return metadataInspection\.loadExplorerHeap\(heapName\)/);
  assert.doesNotMatch(
    `${windowLoader}\n${heapLoader}`,
    /inspectPackageMetadataTable|inspectPlatformMetadataTable|inspectPackageHeapEntries|inspectPlatformHeapEntries/);
  assert.match(
    metadataInspectionSource,
    /dependencies\.queryPlatformTable[\s\S]*dependencies\.queryPackageTable[\s\S]*state\.explorer !== explorer[\s\S]*index === explorer\.focusIndex && !explorer\.focusHeap/);
  assert.match(
    metadataInspectionSource,
    /dependencies\.queryPlatformHeap[\s\S]*dependencies\.queryPackageHeap[\s\S]*state\.explorer !== explorer[\s\S]*explorer\.focusHeap === heapName/);
});

test("call graph request coordination stays outside the composition root", () => {
  const loader =
    appSource.match(/async function loadSelectedMemberCallGraph\([\s\S]*?\n}\n\n\/\/ Update just/)?.[0]
    ?? "";
  assert.match(
    loader,
    /return callGraphInspection\.load\(\{[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*typeIdentity: type\.definitionId \?\? type\.id,[\s\S]*platformType: type\.metadataId \?\? type\.queryId \?\? type\.id,[\s\S]*isCurrent: \(\) => memberRequestIsCurrent\(signature, true\)/);
  assert.doesNotMatch(
    loader,
    /memberCallGraphSeq|inspectMemberCallGraph|inspectExpandPlatformCallGraph/);
  assert.match(
    callGraphInspectionSource,
    /dependencies\.queryWorkspace\(request, \[\]\)[\s\S]*dependencies\.nextPaint\(\)[\s\S]*request\.workspacePackages[\s\S]*dependencies\.patchCallGraphSection\(previousMermaid\)/);
  assert.match(
    callGraphInspectionSource,
    /request\.isRuntimePack[\s\S]*loadPlatformGraph\(request\)/);
});

test("typeless member lookup and request guards stay empty", () => {
  assert.match(
    appSource,
    /function memberGroups\([\s\S]*type: BrowserTypeSurface \| null \| undefined,[\s\S]*for \(const member of \(type\?\.api \?\? \[\]\) as AppMemberSurface\[\]\)/);
  assert.match(
    appSource,
    /function memberRequestIsCurrent\([\s\S]*const type = selectedType\(\);\s*if \(!type\) return false;\s*const member = selectedMember\(type\)/);
});

test("history validates saved type and member identity before restoring Member state", () => {
  const applyView =
    appSource.match(/function applyView\(view: WorkspaceView\) \{[\s\S]*?\n}\n\nconst navigationHistory/)?.[0]
    ?? "";
  assert.match(applyView, /const type = pkg\.types\.find\(item => item\.id === view\.selectedTypeId\)/);
  assert.match(
    applyView,
    /const memberHistory = restoreMemberHistoryState\(\s*view,\s*type,\s*member/);
  assert.match(
    applyView,
    /state\.selectedTypeId = type\?\.id \?\? pkg\.types\[0\]\?\.id \?\? "";[\s\S]*state\.selectedMemberKey = memberHistory\.selectedMemberKey;[\s\S]*state\.memberBrowseTypeId = memberHistory\.memberBrowseTypeId;[\s\S]*state\.memberKindFilter = memberHistory\.memberKindFilter;[\s\S]*state\.memberAccessibilityFilter = memberHistory\.memberAccessibilityFilter;[\s\S]*state\.memberTraitFilter = memberHistory\.memberTraitFilter;[\s\S]*state\.memberTextFilter = memberHistory\.memberTextFilter/);
  assert.match(
    applyView,
    /state\.selectedOverloadIndex = memberHistory\.selectedOverloadIndex;[\s\S]*state\.memberSection = isMemberSection\(memberHistory\.memberSection\)[\s\S]*\? memberHistory\.memberSection[\s\S]*state\.selectedBodyTarget = memberHistory\.selectedBodyTarget/);
  assert.match(
    applyView,
    /navigationHistory\.normalizeCurrent\(\);[\s\S]*loadSelectedMemberSource\(\)[\s\S]*else \{\s*render\(\)/);
  assert.match(
    appSource,
    /const navigationHistory = createNavigationHistory\(\{\s*capture: captureView,\s*signature: workspaceViewSignature,\s*apply: applyView/);
  assert.match(
    workspaceNavigationSource,
    /function workspaceViewSignature\([\s\S]*b: encodeBodyTarget\(view\.bodyTarget\)/);
  assert.match(
    appSource,
    /function captureView\(\): WorkspaceView \| null \{[\s\S]*bodyTarget: state\.selectedBodyTarget/);
  assert.match(
    appSource,
    /else if \(state\.selectedTypeId !== current\.id\) \{\s*state\.selectedTypeId = current\.id;\s*state\.selectedMemberKey = "";\s*state\.memberBrowseTypeId = "";\s*state\.selectedOverloadIndex = null;\s*resetMemberFilters\(\);\s*resetMemberSectionState\(\)/);
});

test("Platform scope restoration defers selection, rendering, and data loading", () => {
  const openPlatformLibrary =
    appSource.match(/async function openPlatformLibrary\([\s\S]*?\n}\n\nfunction pickSpotlightLoadedPackage/)?.[0]
    ?? "";
  assert.match(
    openPlatformLibrary,
    /const scopeOnly = options\.scopeOnly === true;[\s\S]*state\.libraryScope = hasLib \? new Set\(\[key\]\) : null;[\s\S]*if \(scopeOnly\) return pkg;[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*render\(\);/);
  const applyScope =
    appSource.match(/async function applyPlatformLibraryScope\([\s\S]*?\n}\n\n\/\/ History/)?.[0]
    ?? "";
  assert.match(applyScope, /scopeOnly: true/);
});

test("Type Source completion settles behind workbench overlays", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /function workbenchOverlayOwnsFocus\(\) \{\s*return workbenchModalOwnsFocus\(\)\s*\|\| state\.tasteOpen;[\s\S]*function workbenchModalOwnsFocus\(\) \{\s*return state\.spotlightOpen\s*\|\| state\.graphSourceOpen\s*\|\| state\.docViewerOpen;/);
  assert.match(
    appSource,
    /sourceInspection\.loadTypeSource\(\{[\s\S]*isVisible: \(\) =>\s*activeSourceOperationKind\(state\) === "type"\s*&& !workbenchModalOwnsFocus\(\)/);
  assert.match(
    typeSource,
    /const ownsRequest = \(\) =>[\s\S]*if \(ownsRequest\(\)\) \{\s*state\.typeSourceLoading = false;\s*if \(request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    appSource,
    /function isInteractiveElement\(element: Element \| null\)[\s\S]*"button, a\[href\], input, select, textarea, summary, "[\s\S]*\[role=button\][\s\S]*!isInteractiveElement\([\s\S]*event\.target instanceof Element \? event\.target : null\)[\s\S]*event\.key === "Enter"/);
});

test("member-less Metadata omits the empty composition call to action", () => {
  assert.match(
    appSource,
    /function renderMemberComposition\(type: BrowserTypeSurface\) \{[\s\S]*if \(!kinds && !accessibilities && !traits\) return "";/);
});

test("settings keep a viewport-bounded scroll region", () => {
  const settingsPageRule =
    stylesSource.match(/\.settings-page\s*\{([^}]*)\}/s)?.[1] ?? "";
  const settingsMainRule =
    stylesSource.match(/\.settings-main\s*\{([^}]*)\}/s)?.[1] ?? "";
  assert.match(
    settingsPageRule,
    /(?:^|\n)\s*height: 100vh;/);
  assert.doesNotMatch(
    settingsPageRule,
    /(?:^|\n)\s*min-height:/);
  assert.match(
    settingsPageRule,
    /(?:^|\n)\s*grid-template-rows: auto minmax\(0, 1fr\);/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*min-height: 0;/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*overflow-y: auto;/);
});

test("home keeps a viewport-bounded scroll region and reachable footer", () => {
  const homeRule =
    stylesSource.match(/\.home\s*\{([^}]*)\}/s)?.[1] ?? "";
  const homeHeroRule =
    stylesSource.match(/\.home-hero\s*\{([^}]*)\}/s)?.[1] ?? "";
  assert.match(
    homeRule,
    /(?:^|\n)\s*height: 100vh;/);
  assert.doesNotMatch(
    homeRule,
    /(?:^|\n)\s*min-height:/);
  assert.match(
    homeRule,
    /(?:^|\n)\s*grid-template-rows: auto auto minmax\(0, 1fr\) auto;/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*min-height: 0;/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*overflow-y: auto;/);
  assert.match(
    stylesSource,
    /\.home > \.query-notice\s*\{\s*grid-row: 2;\s*\}/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*grid-row: 3;/);
  assert.match(
    stylesSource,
    /\.home-foot\s*\{[^}]*grid-row: 4;/s);
});

test("all dependency navigation paths use one product-owned coordinate matcher", () => {
  assert.equal(
    [...applicationSources.matchAll(/uniqueCompatiblePackage\(/g)].length,
    6);
  assert.match(
    generatedEngineSource,
    /matchPackageDependencyCoordinateExport = exports\.InspectionEngine\.MatchPackageDependencyCoordinate/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\([\s\S]*?JSON\.stringify\(dependencyCoordinateCandidates\(packages\)\)/);
  assert.doesNotMatch(generatedEngineSource, /PackageVersionSatisfiesDependencyRange/);
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
    graphSource,
    /const nodeInfoById = new Map\(\s+keys\.map\(key => \[idOf\.get\(key\), nodeInfo\.get\(key\)\]\)\)/);
  assert.match(
    appSource,
    /const nodeId = dataId \|\| idMatch\?\.\[1\];\s*const info = nodeId \? built\.nodeInfoById\.get\(nodeId\) : null/);
  assert.doesNotMatch(appSource, /nodeInfoByLabel/);
  assert.match(
    appSource,
    /Dependency graph truncated at \$\{built\.nodeLimit\} nodes/);
  assert.match(
    appSource,
    /const signature = dependencyGraphRenderSignature\(built\)/);
});

test("dependency navigation reserves identity and surfaces resolution failures", () => {
  assert.doesNotMatch(appSource, /state\.navigationSeq/);
  assert.match(
    appSource,
    /const navigationSeq = navigationSequence\.begin\(\);\s+state\.loading = true;[\s\S]*?await resolveDependencyVersion/);
  assert.match(
    appSource,
    /if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s+state\.loading = false;\s+appendQueryNotice/);
  assert.match(
    graphSource,
    /packageIdentityKey\(uniqueCompatiblePackage\(\s+model\.packages,\s+dependency\.id,\s+dependency\.versionRange\)\) === target\.packageKey/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\(\s+packageId,\s+declaredRange \?\? null,\s+JSON\.stringify\(dependencyCoordinateCandidates\(packages\)\)\)/);
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
    memberDetailInspectionSource.match(/async loadAnnotated\(request\)[\s\S]*?\n    },/)?.[0]
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
  assert.notEqual(
    memberRequestKey([...request, 0x06000001, "M:Run"]),
    memberRequestKey([...request, 0x06000002, "M:<Run>b__0_0"]));
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
      /async function loadSelectedMemberFacts\(\)[\s\S]*?\n}\n\ninterface LoadPackageOptions/)?.[0]
    ?? "";

  assert.match(
    coordinator,
    /inspectMemberDocumentation\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*documentationId\)/);
  assert.match(
    coordinator,
    /inspectMemberAnnotatedSource\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.typeIdentity,\s*request\.type,\s*request\.member,\s*request\.memberSignature,\s*request\.selectorKey,\s*request\.metadataToken,\s*request\.taste\)/);
  assert.match(
    coordinator,
    /const document = result\.document;\s*validateAnnotatedSourceDocument\(document\);\s*return \{ \.\.\.result, document \};/);
  assert.match(
    coordinator,
    /inspectMemberFacts\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.type,\s*request\.member,\s*request\.memberSignature\)/);
  assert.match(
    documentationLoader,
    /const signature = memberRequestSignature\(type, overload\)/);
  assert.match(
    documentationLoader,
    /return memberDetailInspection\.loadDocumentation\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*overload,\s*isRuntimePack: Boolean\(state\.package\?\.isRuntimePack\),\s*isCurrent: \(\) => memberRequestIsCurrent\(signature\)/);
  assert.match(
    annotatedLoader,
    /loadAnnotated\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*typeIdentity: type\.definitionId \?\? type\.id,\s*type: type\.queryId \?\? type\.id,\s*member: overload\.name,\s*memberSignature: overload\.signature,[\s\S]*taste: JSON\.stringify\(state\.taste\)/);
  assert.match(
    factsLoader,
    /const signature = memberRequestSignature\(type, overload\)/);
  assert.match(
    factsLoader,
    /return memberDetailInspection\.loadFacts\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*type: type\.queryId \?\? type\.id,\s*member: overload\.name,\s*memberSignature: overload\.signature,\s*isCurrent: \(\) => memberRequestIsCurrent\(signature\)/);
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
    generatedEngineSource,
    /cancelSourceQueryExport = exports\.InspectionEngine\.CancelSourceQuery/);
  assert.match(
    generatedEngineSource,
    /export function cancelSourceQuery\(\)[\s\S]*?return cancelSourceQueryExport\(\)/);
  assert.match(
    appSource,
    /cancelSourceQuery: cancelSourceInspection/);

  const renderBody =
    appSource.match(/function render\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(renderBody, /sourceInspection\.cancelHiddenRequest\(\)/);
  assert.match(
    appSource,
    /createSourceInspectionCoordinator\(\{[\s\S]*cancelEngineSourceRequest: \(\) => cancelSourceInspection\?\.\(\)/);
  assert.match(
    sourceInspectionSource,
    /cancelHiddenRequest\(\)[\s\S]*sourceSurfaceIsVisible\(state\)[\s\S]*cancelSourceRequestState\(state\)/);
  assert.match(appSource, /sourceInspection\.loadMemberSource\(\{/);
  assert.match(appSource, /sourceInspection\.loadTypeSource\(\{/);
  assert.match(appSource, /sourceInspection\.openGraphSource\(request, title\)/);
  assert.match(appSource, /sourceInspection\.closeGraphSource\(\)/);
  const reloadBody =
    appSource.match(/function reloadVisibleSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(reloadBody, /switch \(sourceReloadKind\(state\)\)/);
  const autoLoadBody =
    appSource.match(
      /function maybeAutoLoadVisibleSource\(\)[\s\S]*?\n}\n\nfunction maybeAutoLoadTypeMetadata/)?.[0]
    ?? "";
  assert.match(
    autoLoadBody,
    /const kind = activeSourceOperationKind\(state\)/);
  assert.match(autoLoadBody, /kind === "type"/);
  assert.match(autoLoadBody, /kind === "member"/);
  assert.match(autoLoadBody, /kind === "graph"/);
  assert.match(autoLoadBody, /loadSelectedTypeSource\(\)/);
  assert.match(autoLoadBody, /loadSelectedMemberSource\(\)/);
  assert.match(autoLoadBody, /openGraphSource\(/);
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  assert.match(
    annotatedLoader,
    /return memberDetailInspection\.loadAnnotated\(\{/);
  assert.doesNotMatch(
    annotatedLoader,
    /sourceRequestNeedsLoad|memberAnnotatedLoading/);
  assert.match(
    memberDetailInspectionSource,
    /async loadAnnotated\(request\)[\s\S]*sourceRequestNeedsLoad\([\s\S]*state\.memberAnnotatedLoading[\s\S]*state\.memberAnnotatedError/);

  const visible = {
    settings: false,
    explorer: null,
    loading: false,
    error: "",
    home: false,
    package: {},
    atPackageRoot: false,
    graphSourceOpen: false,
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview"
  };
  assert.equal(sourceSurfaceIsVisible(visible), true);
  for (const hidden of [
    { home: true },
    { atPackageRoot: true },
    { settings: true },
    { loading: true },
    { error: "failed" },
    { explorer: { open: true } },
    { package: null }
  ]) {
    assert.equal(sourceSurfaceIsVisible({ ...visible, ...hidden }), false);
  }
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      atPackageRoot: true,
      graphSourceOpen: true
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

  const requestState = {
    sourceRequestGeneration: 4,
    memberSourceLoading: true,
    memberSourceKey: "member",
    memberSourceError: "",
    typeSourceLoading: false,
    typeSourceKey: "",
    typeSourceError: "",
    graphSourceLoading: false,
    graphSourceError: "",
    graphSourceSeq: 0
  };
  assert.equal(beginSourceRequestState(requestState), 5);
  assert.equal(requestState.memberSourceLoading, false);
  assert.equal(requestState.memberSourceKey, "");
  requestState.typeSourceLoading = true;
  requestState.typeSourceKey = "type";
  assert.equal(cancelSourceRequestState(requestState), true);
  assert.equal(requestState.sourceRequestGeneration, 6);
  assert.equal(requestState.typeSourceLoading, false);
  assert.equal(requestState.typeSourceKey, "");
  assert.equal(requestState.typeSourceError, "");
});

test("browser engine configures the same-origin managed MSDL API", () => {
  assert.match(
    generatedEngineSource,
    /configureHostExport = exports\.InspectionEngine\.ConfigureHost[\s\S]*?configureHostExport\(window\.location\.origin\)/);
});

test("generated browser engine module is syntactically valid", () => {
  const result = spawnSync(
    process.execPath,
    ["--check", fileURLToPath(generatedEngineModuleUrl)],
    { encoding: "utf8" });
  assert.equal(
    result.status,
    0,
    `${fileURLToPath(generatedEngineModuleUrl)} failed syntax validation:\n${result.stderr}`);
});

test("generated source wrappers parse their JSON envelopes", () => {
  const wrapper = name => {
    const start = generatedEngineSource.search(
      new RegExp(`\\nexport (?:async )?function ${name}\\(`));
    assert.notEqual(start, -1, `missing generated wrapper ${name}`);
    const end = generatedEngineSource.indexOf("\nexport ", start + 1);
    return generatedEngineSource.slice(start, end < 0 ? undefined : end);
  };

  for (const name of [
    "queryMemberAnnotatedSource",
    "queryMemberSource",
    "queryTypeMemberSource",
  ]) {
    assert.match(wrapper(name), /return JSON\.parse\(result\);/);
  }
  assert.doesNotMatch(wrapper("queryMemberFacts"), /JSON\.parse\(result\)/);
});

test("MethodDef-only member sections are hidden for bodiless APIs", () => {
  for (const kind of ["property", "field", "event", "constant"]) {
    assert.deepEqual(
      memberSectionIdsFor({ kind }),
      ["overview"]);
  }
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }),
    ["overview", "call-graph", "facts", "source", "annotated"]);
});

test("source requests carry exact type and member identities", () => {
  const memberBridge =
    generatedEngineSource.match(/export async function queryMemberSource\([\s\S]*?\n}/)?.[0]
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

test("call graph diagnostics distinguish failures from expected bounds", () => {
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 2,
    incompleteEdges: 1,
    bindingIdentityConflicts: 3,
    hasUnexploredTraversalBoundary: true
  }), "Partial call graph: 2 incomplete nodes, 1 incomplete edge, and 3 binding identity conflicts.");
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true
  }), "");
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasAnalysisFailureBoundary: true
  }), "Partial call graph: one or more method bodies could not be analyzed.");
  assert.equal(callGraphDiagnosticsMessage({
    isIncomplete: true,
    incompleteNodes: 1,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true,
    hasAnalysisFailureBoundary: true
  }), "Partial call graph: 1 incomplete node and one or more method bodies could not be analyzed.");
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
    packageBarSource,
    /onFrameworkSelect\(button\.dataset\.frameworkChip \?\? ""\)/);
  assert.match(appSource, /selectFramework: switchPackageFramework/);
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
    workspaceNavigationSource,
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
    packageBarSource,
    /data-package-close=/);
  assert.match(
    packageBarSource,
    /closePackageTab\(key\)/);
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
    graphSource,
    /dependencyGraphGroupSelectionIndex\(\s*model\.packageDependencies,\s*model\.dependenciesGroupIndex,\s*fallbackGroupIndex\)/);
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
    graphSource,
    /selectedDependencyGroup\(\s*model\.packageDependencies,\s*selectedGroupIndex\)/);
});

test("Mermaid labels contain grammar-significant metadata", () => {
  const encoded = mermaidLabel(
    "A\"B\n<x>&\\\u2028\u202E\u200D\uD800X\uDC00\u{E0001}-Caf\u00E9\u{1F600}");

  assert.equal(
    encoded,
    "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028"
      + "&#92;u202E&#92;u200D&#92;uD800X&#92;uDC00"
      + "&#92;uDB40&#92;uDC01-Caf\u00E9\u{1F600}");
  for (const character of [
    '"', "\n", "<", ">", "\\", "\u2028", "\u202E", "\u200D", "\uD800", "\uDC00"
  ]) {
    assert.equal(encoded.includes(character), false);
  }
  assert.equal(encoded.endsWith("-Caf\u00E9\u{1F600}"), true);
});

test("type graph rendering contains artifact labels", () => {
  const definition = buildTypeGraphMermaid({
    graphNodes: [
      {
        id: "self",
        displayName: "Example.A\u202E\uD800-Caf\u00E9\u{1F600}",
        role: "self"
      },
      { id: "base", displayName: "Example.Base", role: "base" }
    ],
    graphEdges: [{ fromId: "self", toId: "base" }]
  });

  assert.match(
    definition,
    /t0\["A&#92;u202E&#92;uD800-Café😀"\]:::self/);
  assert.equal(definition.includes("\u202E"), false);
  assert.equal(definition.includes("\uD800"), false);
});

test("dependency graph rendering contains artifact labels", () => {
  const root = packageAt("1.0.0", "net8.0");
  const definition = buildDependencyGraphMermaid(
    {
      package: root,
      packages: [root],
      packageDependencies: {
        dependencyGroupError: "",
        dependencyGroups: [{
          index: 0,
          framework: "net8.0",
          isActive: true,
          dependencies: [{
            id: "Dependency\u200D\uDC00-Caf\u00E9\u{1F600}",
            versionRange: ""
          }]
        }]
      },
      dependenciesGroupIndex: 0,
      workspaceDependencies: {}
    },
    () => null);

  assert.match(
    definition.definition,
    /d1\["Dependency&#92;u200D&#92;uDC00-Café😀"\]:::external/);
  assert.equal(definition.definition.includes("\u200D"), false);
  assert.equal(definition.definition.includes("\uDC00"), false);
});
