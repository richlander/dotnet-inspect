import assert from "node:assert/strict";
import test from "node:test";
import {
  createDependencyGraphPendingState,
  createDependencyGraphRenderSequence,
  dependencyGraphRenderSignature,
  spotlightCandidateKey,
  spotlightCandidateSignature,
} from "../src/data.ts";

import {
  packageAt,
  appSource,
  functionDeclaration,
  workspaceNavigationSource,
  shellControlsSource,
  graphInteractionsSource,
  sourceInspectionSource,
  metadataInspectionSource,
  memberDetailInspectionSource,
  callGraphInspectionSource,
  memberFocusSource,
  graphSource,
  typePanelSource,
  applicationSources,
  stylesSource,
  indexSource,
  generatedFacadeModules,
  generatedFacadeSource,
  generatedFacadeSourceText,
  deploySource,
  dataBarSource,
  diagnosticsViewSource,
  diagnosticsRouteSource,
  commandBarSource,
} from "./composition-root-test-fixture.ts";
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
  const rootNodeInfo = graph.nodeInfoById.get("d0");
  assert.ok(rootNodeInfo, "the graph fixture must describe node d0");
  assert.notEqual(
    signature,
    dependencyGraphRenderSignature({
      ...graph,
      nodeInfoById: new Map([[
        "d0",
        {
          ...rootNodeInfo,
          packageKey: "Example.Package|2.0.0|net8.0"
        }
      ]])
    }));
});

test("data bar shows versioned linked build provenance", () => {
  assert.match(
    appSource,
    /async function loadBuildIdentity\(\) \{[\s\S]*state\.buildIdentity = await engineClient\.host\.buildIdentity\(\);[\s\S]*state\.buildIdentityStatus = "ready";[\s\S]*state\.buildIdentityStatus = "failed"/);
  assert.equal(appSource.match(/\bdataBarHtml\(\{/g)?.length, 5);
  assert.match(
    appSource,
    /<\/main>[\s\S]{0,700}\$\{dataBarHtml\(\{/);
  assert.match(dataBarSource, /<footer class="data-bar" aria-label="Product information">/);
  assert.match(dataBarSource, /buildIdentityItems\(model\.buildIdentity/);
  assert.match(
    appSource,
    /\$\{dataBarHtml\(\{\s*buildIdentity: state\.buildIdentity,\s*}, escapeHtml\)\}/);
  assert.match(
    appSource,
    /producer: \{ kind: "acquisition", label: "Platform" \}/);
  assert.match(
    dataBarSource,
    /href="\$\{CLI_TOOL_URL\}"[\s\S]*href="\$\{AGENT_SKILL_URL\}"[\s\S]*href="\$\{ROUTED_ENTRY_PATHS\.diagnostics\}"[\s\S]*href="\$\{ROUTED_ENTRY_PATHS\.credits\}"/);
  assert.match(
    dataBarSource,
    /identity\.commitUrl[\s\S]*target="_blank" rel="noopener noreferrer"/);
  assert.match(dataBarSource, /compactUtcDate\(identity\.builtAtUtc\)/);
  assert.doesNotMatch(dataBarSource, /\bbuilt\b/i);
  assert.match(
    deploySource,
    /-getProperty:VersionPrefix[\s\S]*-p:VersionPrefix="\$version"[\s\S]*-p:SourceRevisionId="\$GITHUB_SHA"[\s\S]*-p:BuildTimestampUtc="\$built_at"/);
});

test("Diagnostics is a routed typed surface outside the Application menu", () => {
  assert.match(
    appSource,
    /if \(isDiagnosticsPath\(location\.pathname\)\) \{\s*loadingBotSrc = null;\s*renderDiagnosticsPage\(\);\s*bindLibraryOpenEvents\(\);\s*return;/);
  assert.match(
    appSource,
    /function renderDiagnosticsPage\(\)[\s\S]*diagnosticsViewHtml\(\{[\s\S]*bindDiagnosticsView\(document/);
  assert.doesNotMatch(appSource, /class="diagnostics-/);
  assert.match(
    diagnosticsViewSource,
    /<h1 id="diagnostics-heading" tabindex="-1">Diagnostics<\/h1>/);
  assert.match(
    diagnosticsViewSource,
    /id="diagnostics-back"[\s\S]*aria-label="Back to previous page"/);
  assert.match(
    diagnosticsViewSource,
    /runtimeCardHtml\(model\.runtime[\s\S]*buildCardHtml\(model\.build[\s\S]*cacheCardHtml\(model\.packageCache/);
  assert.match(
    diagnosticsRouteSource,
    /DIAGNOSTICS_PATH = ROUTED_ENTRY_PATHS\.diagnostics[\s\S]*isRoutedEntryPath\(pathname, DIAGNOSTICS_PATH\)/);
  const applicationMenu =
    shellControlsSource.match(
      /export function renderApplicationMenu\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.doesNotMatch(applicationMenu, /Diagnostics|diagnostics/);
  assert.doesNotMatch(commandBarSource, /"diagnostics"/);
});

test("bootstrap reconciles persisted style choices with the product catalog", () => {
  const bootstrap =
    appSource.match(
      /async function bootstrap\(\) \{[\s\S]*?\n}\n\nfunction computeDiagnostics/,
    )?.[0] ?? "";
  assert.match(
    bootstrap,
    /state\.styleOptions = \([\s\S]*reconcileStyleTaste\(\s*state\.taste,\s*state\.styleOptions\);[\s\S]*state\.taste = reconciledTaste;[\s\S]*localStorage\.setItem\("inspect-taste", JSON\.stringify\(state\.taste\)\)/);
});

test("bare home paints before wasm engine download", () => {
  const renderDispatch =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?const pkg = state\.package;/,
    )?.[0] ?? "";
  const bootstrap =
    appSource.match(/async function bootstrap\(\) \{[\s\S]*?\n}\n\nfunction computeDiagnostics/)?.[0] ?? "";
  const homePaintWait =
    appSource.match(/function waitForHomePaint\(\)[\s\S]*?\n}\n\nfunction loadStoredTaste/)?.[0] ?? "";
  const errorPackageRecovery =
    appSource.match(/function openPackageQuery[\s\S]*?\n}\n\nconst loadErrorShellActions/)?.[0] ?? "";
  const loadingView =
    appSource.match(/function renderLoading\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberDocumentation/)?.[0] ?? "";
  assert.doesNotMatch(appSource, /from "\/engine\.js"/);
  assert.doesNotMatch(appSource, /inspect-web-engine/);
  assert.match(
    appSource,
    /async function loadEngineModule\(\)[\s\S]*import\("\.\/engine-worker-client\.ts"\)[\s\S]*createProductionEngineWorkerClient\(origin,/);
  for (const module of generatedFacadeModules) {
    assert.doesNotMatch(
      appSource,
      new RegExp(
        `import\\("/${module}\\.js"\\)`),
      `the page runtime still imports /${module}.js`);
  }
  assert.match(
    homePaintWait,
    /first-contentful-paint[\s\S]*observer\.observe\(\{ type: "paint", buffered: true \}\)/);
  assert.match(
    homePaintWait,
    /requestAnimationFrame\(\(\) => setTimeout\(resolve, 0\)\)/);
  assert.match(
    appSource,
    /state\.loading = !state\.home;[\s\S]*render\(\);[\s\S]*if \(state\.home\) await waitForHomePaint\(\);[\s\S]*await loadEngineModule\(\);[\s\S]*reportEngineStatus\("Loading \.NET WebAssembly…"\);[\s\S]*await startEngine\(window\.location\.origin\);[\s\S]*reportEngineStatus\("Reading package assemblies…"\)/);
  assert.match(
    renderDispatch,
    /if \(state\.credits\) \{[\s\S]*renderCreditsView\(\);[\s\S]*if \(\(state\.loading[\s\S]*!loadingPackageContent[\s\S]*!retainedWorkspacePostingVisible\)[\s\S]*\|\| state\.error\)/);
  assert.match(
    bootstrap,
    /state\.engineStartupFailed = false;[\s\S]*const reportEngineStatus = \(message: string\) => \{[\s\S]*if \(!state\.credits\) render\(\);[\s\S]*if \(state\.home\) \{[\s\S]*if \(!state\.credits\) render\(\);[\s\S]*catch \(error\) \{[\s\S]*showEngineFailure\(error\)/);
  assert.match(
    appSource,
    /const showReadyGlint = state\.engineReady && homeReadyGlintPending;[\s\S]*homeReadyGlintPending = false;[\s\S]*homeBotAnimationStartedAt[\s\S]*--home-bot-animation-delay:/);
  assert.match(
    stylesSource,
    /animation-delay: var\(--home-bot-animation-delay, 0ms\)/);
  assert.match(
    appSource,
    /class="home-search \$\{enginePending[\s\S]*class="home-engine-status"/);
  assert.match(
    appSource,
    /state\.engineReady[\s\S]*class="home-engine-status"/);
  assert.doesNotMatch(dataBarSource, /engineReady|browser wasm/i);
  assert.match(
    appSource,
    /state\.retryAction = \(\) => window\.location\.reload\(\)/);
  assert.match(
    errorPackageRecovery,
    /findOpenPackageForQuery\(state, query\)[\s\S]*selectWorkspacePackage\(openPackage\);[\s\S]*return;[\s\S]*if \(!state\.engineReady\) \{[\s\S]*window\.location\.assign\(url\);[\s\S]*return;[\s\S]*\}\s*observeAsync\(\s*loadPackageFromSpotlight\(query\.packageId, query\.version, ""\)/);
  assert.match(
    loadingView,
    /id="error-package-query"[\s\S]*bindLoadErrorShell\(document, loadErrorShellActions\)/);
  assert.doesNotMatch(
    loadingView,
    /id="error-package-query"[\s\S]*(?:openPackageQuery|loadPackage)\(/);
});

test("Spotlight uses local type matches until the engine is ready", () => {
  const typeMatches =
    appSource.match(/function spotlightTypeMatches[\s\S]*?\n}\n\n\/\/ Flat member index/)?.[0] ?? "";
  assert.match(
    typeMatches,
    /if \(!state\.engineReady\) return spotlightFallbackMatches\(query, cache\.pool\);[\s\S]*inspectSearchTypes\(query, cache\.candidates\)/);
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
    /\.type-library-context \.namespace-chips, \.pane-footer \{ display: none; \}/);
  assert.match(
    typePanelSource,
    /memberFilter\?\.addEventListener\(\s*"input",\s*\(\) => actions\.onMemberFilterChange\(memberFilter\.value\)\)/);
  assert.match(
    binding,
    /onMemberFilterChange: value => \{[\s\S]*state\.memberTextFilter = value;[\s\S]*renderPreservingMemberFocus\(\)/);
  assert.match(
    typePanelSource,
    /id: "member-filter\.navigate"[\s\S]*key: \["Escape", "ArrowUp", "ArrowDown"\][\s\S]*run: event => actions\.onMemberFilterKeyDown\(event, memberFilter\.value\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: \(event, value\) => \{[\s\S]*event\.key === "Escape"[\s\S]*if \(navMode\(\) !== "member" && value === ""\) return false;[\s\S]*if \(navMode\(\) === "member"\)[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*renderMemberFilterAndRestoreFocus\("#member-filter"\)[\s\S]*return true[\s\S]*stepMemberNav/);
  assert.match(
    appSource,
    /id: "workspace\.drill-out-escape"[\s\S]*key: "Escape"[\s\S]*!isTextEntry\(\)[\s\S]*if \(navMode\(\) === "member"\) exitMemberScope\(\)/);
  assert.match(
    appSource,
    /function exitMemberScope\(\) \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*contentFramePane = "navigation";[\s\S]*render\(\);\s*restoreContentNavigationFocus\(focusGeneration\);\s*return true;/);
  assert.match(appSource, /onShowTypes: exitMemberScope,/);
  assert.match(appSource, /<summary id="member-filter-summary">/);
  assert.match(typePanelSource, /<summary id="type-filter-summary">/);
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
    /let owner = captureViewOperation\(seq\);[\s\S]*ownsViewOperation\(owner, state\.memberCallGraphSeq\)[\s\S]*const discardIfStale = \([\s\S]*loadRuntimeGraphAssembly\([\s\S]*navigationIsCurrent[\s\S]*runtimeResult\.failureMessage[\s\S]*state\.runtimePackError[\s\S]*renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    appSource,
    /function applyMemberSection\(id: MemberSection\) \{[\s\S]*state\.memberSection === "call-graph" && id !== "call-graph"[\s\S]*invalidateMemberCallGraphWork\(state\)/);
  assert.match(
    appSource,
    /function navigateToRuntimeMember\([\s\S]*const targetLibrary = libraryKey\(type\);\s*state\.libraryScope = targetLibrary \? new Set\(\[targetLibrary\]\) : null;[\s\S]*state\.typeCursor = Math\.max\(0, filteredTypes\(\)/);
});

test("Type inventory filters preserve their focused control across rerenders", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  for (const name of [
    "onClearFilters",
    "onKindSelect",
    "onNamespaceSelect",
  ]) {
    const callback =
      binding.match(new RegExp(`    ${name}: [\\s\\S]*?(?=\\n    on[A-Z])`))
        ?.[0] ?? "";
    assert.match(callback, /renderPreservingMemberFocus\(\)/);
    assert.doesNotMatch(callback, /\brender\(\)/);
  }
  assert.match(
    appSource,
    /function afterLibraryScopeChange\(\) \{\s*normalizeLibrarySelection\(\);\s*renderPreservingMemberFocus\(\)/);
});

test("shared member views use portable product identity and omit UI-local filters", () => {
  const capture = appSource.match(
    /function captureWorkspaceUrlState\(\)[\s\S]*?\n}\n\nasync function buildStateUrl/)?.[0] ?? "";
  const encoder = workspaceNavigationSource.match(
    /function workspaceShareState\([\s\S]*?\n}\n\nfunction encodedWorkspaceSharePacket/)?.[0] ?? "";
  const deepLink = appSource.match(
    /function applyDeepLink\([\s\S]*?\n}\n\n\/\/ Kick off/)?.[0] ?? "";
  assert.match(
    encoder,
    /tabs: state\.tabs,[\s\S]*contexts: state\.contexts,[\s\S]*view: state\.view/);
  assert.match(
    capture,
    /memberAnchor = overload\.anchorDigest \|\| null;[\s\S]*memberSignature = memberAnchor \? null : overload\.canonicalSignature \|\| null/);
  assert.match(
    capture,
    /const library = selectedLibraryShareKey\(\);\s*const libraries =\s*workspaceSubjectOpen \|\| platformRoot \|\| packageSubjectOpen \|\| !library\s*\? \[\]\s*: \[library\]/);
  assert.match(
    capture,
    /!packageSubjectOpen\s*&& state\.libraryScope\s*&& state\.libraryScope\.size > 1[\s\S]*Select one library/);
  assert.match(
    capture,
    /overload\.bodySelectors\.length > 1[\s\S]*accessor-specific section/);
  assert.match(
    capture,
    /overload\.graphOnly[\s\S]*Graph-discovered members cannot be shared/);
  assert.match(capture, /package: state\.rootKind === "platform" \? "" : state\.package\?\.id/);
  assert.doesNotMatch(capture, /memberTextFilter:/);
  assert.doesNotMatch(capture, /memberKindFilter:/);
  assert.doesNotMatch(capture, /memberAccessibilityFilter:/);
  assert.doesNotMatch(capture, /memberTraitFilter:/);
  assert.match(
    deepLink,
    /deep\.memberAnchor \|\| deep\.memberSignature[\s\S]*portableMatches\.length === 1[\s\S]*solePortableBodyTarget\(selection\.overload\)[\s\S]*state\.selectedBodyTarget = portableBodyTarget/);
  assert.match(
    appSource,
    /function selectMemberNavEntry\(entry: MemberNavEntry, focusList: boolean\) \{\s*const preservedFocus = captureMemberFocus\(document\);[\s\S]*scheduleMemberFocusAfterRender\(preservedFocus, replacementAuthority\)/);
  assert.match(
    appSource,
    /window\.addEventListener\("popstate"[\s\S]*const deep = loc;[\s\S]*restoreFreshWorkspaceFromHistory\(loc, navigationSeq\)/);
});

test("the frontend delegates compact packet syntax to the product codec", () => {
  assert.doesNotMatch(workspaceNavigationSource, /\batob\b|\bbtoa\b/);
  assert.doesNotMatch(
    workspaceNavigationSource,
    /\b(?:packet|raw)\.(?:f|t|g|a|x|v|y|m|s|c|l)\b/);
  assert.doesNotMatch(
    workspaceNavigationSource,
    /\bWorkspaceSharePacket\b|encodeBase64Url|decodeBase64Url/);
  assert.match(
    workspaceNavigationSource,
    /decodeWorkspaceShareResult\((?:await )?decode\(value\)\)/);
  assert.match(
    workspaceNavigationSource,
    /encodedWorkspaceSharePacket\(\s*(?:await )?encode\(workspaceShareState\(state\)\)\)/);
});

test("the selected canonical context bounds call graph workspace membership", () => {
  const selection = appSource.match(
    /function selectedCallGraphWorkspacePackages\(\)[\s\S]*?\n}/)?.[0] ?? "";
  const loader = appSource.match(
    /async function loadSelectedMemberCallGraph\([\s\S]*?\n}/)?.[0] ?? "";

  assert.match(
    selection,
    /selectedBrowserCallGraphPackageTabIds\(basis\)/);
  assert.match(
    selection,
    /packageTabIds\.includes\(activeTab\.id\)/);
  assert.match(
    loader,
    /workspacePackages = selectedCallGraphWorkspacePackages\(\)/);
  assert.match(
    appSource,
    /callGraphCaptureTopology\(\s*captured\.tabs,\s*activeIndex,\s*participantTabIds\)/);
});

test("canonical restoration is atomic and history adopts the active packet basis", () => {
  const restore = appSource.match(
    /async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failCanonicalWorkspaceRestore/)?.[0] ?? "";
  const history = appSource.match(
    /window\.addEventListener\("popstate"[\s\S]*?\n}\);/)?.[0] ?? "";
  const sync = appSource.match(
    /function syncUrl\(\)[\s\S]*?\n}/)?.[0] ?? "";
  const stateUrl = appSource.match(
    /async function buildStateUrl\([\s\S]*?\n}/)?.[0] ?? "";
  const scopePlatform = appSource.match(
    /async function openPlatformLibrary\([\s\S]*?\n}/)?.[0] ?? "";
  const validateView = appSource.match(
    /function canonicalViewRestorationFailure\([\s\S]*?\n}/)?.[0] ?? "";
  const initialRestore = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}/)?.[0] ?? "";

  assert.match(
    restore,
    /canonicalTabCountPreserved[\s\S]*canonicalTabsPreserved[\s\S]*failedTabCount > 0 \|\| !canonicalTabsPreserved[\s\S]*failCanonicalWorkspaceRestore/);
  assert.match(
    restore,
    /canonicalViewRestorationFailure\(\s*targetModel,\s*deep,\s*loc\.lens,\s*loc\.libraryLens,\s*loc\.atPackageRoot && !loc\.workspaceSubjectOpen\s*\? loc\.packageLens\s*: null\)[\s\S]*failCanonicalWorkspaceRestore/);
  assert.match(
    validateView,
    /const aggregateLibrarySubject =\s*state\.rootKind === "package"\s*&& state\.libraryScope === null\s*&& aggregateLibrarySubjectIsAvailable\(\)/);
  assert.match(
    restore,
    /canonicalSnapshot = loc\.hasWorkspaceState[\s\S]*captureCanonicalWorkspaceRestoreSnapshot/);
  assert.match(
    history,
    /retainedWorkspaceIdFromHistory\(history\.state\)[\s\S]*activateRetainedWorkspaceProjection\(historyWorkspaceId, false\)[\s\S]*canonicalSnapshot = loc\.hasWorkspaceState[\s\S]*commitWorkspaceShareBasis\(loc\.shareState\)/);
  assert.match(
    restore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*failCanonicalWorkspaceRestore\(/);
  assert.match(
    history,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*failCanonicalWorkspaceRestore\(/);
  assert.match(
    initialRestore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*restoreWorkspaceFromLocation\([\s\S]*return;[\s\S]*const packageId = loc\.package/);
  assert.match(
    appSource,
    /function failCanonicalWorkspaceRestore\([\s\S]*const failedUrl = location\.href;[\s\S]*bindWorkspaceRetryToUrl\(\s*failedUrl,\s*\(\) => location\.href,\s*url => workspaceLocation\.replace\(url, history\.state\),\s*retryAction\)[\s\S]*snapshot\?\.hasWorkspace[\s\S]*restoreCanonicalWorkspaceRestoreSnapshot\(snapshot\)[\s\S]*appendQueryNotice\([\s\S]*ownedRetryAction\);[\s\S]*failedWorkspaceUrlState = \{\s*kind: "canonical",\s*url: failedUrl,[\s\S]*projection: workspaceUrlProjection\(\)[\s\S]*render\(\);\s*restartRestoredWorkspaceSelectionData\(\);\s*return/);
  assert.match(
    restore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*canonicalSnapshot,\s*null\)/);
  assert.match(
    restore,
    /const loaded = await loadPackage\([\s\S]*location: loc,\s*navigationSeq,\s*queryNotice: state\.queryNotice,\s*deferWorkspacePublication: failureHandler !== null/);
  assert.match(
    history,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*invalidSnapshot,\s*null\)/);
  assert.match(
    history,
    /if \(isCreditsPath\(location\.pathname\)\) \{[\s\S]*render\(\{[\s\S]*synchronizeUrl: !unavailableWorkspaceAdmissionRejected,[\s\S]*\}\);\s*return;\s*\}\s*if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.workspaceSubjectOpen = true;[\s\S]*render\(\);[\s\S]*return;\s*\}/);
  assert.match(
    history,
    /isProductHomeDemosPath\(location\.pathname\)[\s\S]*const focusWorkspaceOnEntry =\s*!state\.packageQueryReturnFocusPending\s*&& !state\.packageActivityReturnFocusPending;[\s\S]*render\(\);\s*if \(state\.engineReady && focusWorkspaceOnEntry\)/);
  assert.doesNotMatch(
    appSource,
    /preserveUrlThroughNextRender/);
  assert.match(
    sync,
    /if \(retainFailedWorkspaceUrl\(\)\) return;/);
  assert.match(
    appSource,
    /function retainFailedWorkspaceUrl\(\) \{\s*const failedState = failedWorkspaceUrlState;\s*const retainedState = retainWorkspaceUrlPreservation\(\s*failedState,\s*location\.href,\s*workspaceUrlProjection\(\)\);\s*if \(retainedState\) return true;\s*if \(failedState\?\.kind === "route"\s*&& !recoverWorkspaceRouteFailure\(\s*failedState,\s*location,\s*url => workspaceLocation\.replace\(url, history\.state\)\)\) \{\s*return true;\s*\}\s*failedWorkspaceUrlState = null;\s*return false;\s*\}/);
  assert.match(
    appSource,
    /if \(\(state\.loading[\s\S]*!loadingPackageContent[\s\S]*!retainedWorkspacePostingVisible\)[\s\S]*\|\| state\.error\) \{[\s\S]*return;\s*\}\s*retainFailedWorkspaceUrl\(\);\s*if \(state\.workspaceSubjectOpen && isProductHomeDemosPath\(location\.pathname\)\)/);
  assert.match(
    appSource,
    /navigation: navigationHistory\.snapshot\(\),\s*failedWorkspaceUrlState: failedWorkspaceUrlState[\s\S]*structuredClone\(failedWorkspaceUrlState\)[\s\S]*navigationHistory\.restore\(snapshot\.navigation\);[\s\S]*failedWorkspaceUrlState = snapshot\.failedWorkspaceUrlState[\s\S]*structuredClone\(snapshot\.failedWorkspaceUrlState\)/);
  assert.match(
    appSource,
    /captureCanonicalWorkspaceRestoreSnapshot\(\)[\s\S]*sourceInspection\.cancelCurrentRequest\(\);\s*libraryApiDiff\.cancelCurrentRequest\(\);\s*cancelFindingCensusRequest\(state\)[\s\S]*structuredClone\(state\.packages\)/);
  assert.match(
    appSource,
    /function commitWorkspaceShareBasis\([\s\S]*state\.workspaceShareBasis = basis;[\s\S]*sourceInspection\.clearGraphSource\(\)/);
  assert.match(
    history,
    /invalidateMemberDestinationWork\(state\)[\s\S]*captureCanonicalWorkspaceRestoreSnapshot/);
  assert.match(
    appSource,
    /const \{ tabs, resolvedTabs, preservesBasis \} = capturedShareTabs\(\);[\s\S]*activeShareTabIndex\(tabs, resolvedTabs\)[\s\S]*browserCreatedCallGraphTabIds\(tabs, activeIndex\)/);
  assert.match(
    appSource,
    /captured\.preservesBasis,[\s\S]*state\.memberSection === "call-graph"/);
  assert.match(sync, /snapshot = captureWorkspaceUrlState\(\)/);
  assert.match(
    sync,
    /if \(state\.atPackageRoot && state\.package\) \{[\s\S]*void buildStateUrl\(\)\.then\([\s\S]*workspaceLocation\.replace\(destination, history\.state\)/);
  assert.match(
    sync,
    /const pushFromProductDemos =\s*isProductHomeDemosPath\(location\.pathname\);[\s\S]*const publish = \(url: URL\) => \{\s*if \(revision !== syncUrlRevision\s*\|\| !navigationSequence\.isCurrent\(navigationSeq\)\) return;[\s\S]*if \(!pushFromProductDemos\) \{\s*void workspaceLocation\.build\(snapshot\)\.then\(\s*publish,[\s\S]*void workspaceLocation\.build\(snapshot\)\.then\(\s*publish,/);
  assert.match(
    sync,
    /const revision = \+\+syncUrlRevision;[\s\S]*if \(revision !== syncUrlRevision\s*\|\| !navigationSequence\.isCurrent\(navigationSeq\)\) return(?: undefined)?;[\s\S]*activeWorkspaceUrl = destination/);
  assert.match(
    appSource,
    /const productDemosRouteVisible =\s*scope\(\) === "workspace"\s*&& isProductHomeDemosPath\(location\.pathname\);[\s\S]*document\.title = "Demos — dotnet-inspect";[\s\S]*else if \(state\.rootKind !== "library"\s*&& options\.synchronizeUrl !== false\) \{\s*syncUrl\(\)/);
  assert.match(
    stateUrl,
    /const snapshot = captureWorkspaceUrlState\(\);[\s\S]*await workspaceLocation\.build\(snapshot, base\)/);
  assert.match(
    scopePlatform,
    /platformLibraryMatchesDescriptor\(row, item\)[\s\S]*state\.libraryScope = new Set\(\[library\.id\]\)[\s\S]*if \(scopeOnly\) return pkg/);
  assert.match(
    validateView,
    /typeLensesFor\(pkg\)[\s\S]*deep\.section && !hasPortableMember/);
});

test("initial workspace packet resolution waits for the engine phase", () => {
  assert.match(
    appSource,
    /const initialWorkspace = workspaceLocation\.preflightCurrent\(\);\s*const initialLocation = initialWorkspace\.visible/);
  assert.match(
    appSource,
    /state\.packageQueryOpen = isPackageQueryPath\(location\.pathname\);[\s\S]*state\.packageActivityOpen = isPackageActivityPath\(location\.pathname\);[\s\S]*const diagnosticsOpen = isDiagnosticsPath\(location\.pathname\);[\s\S]*const productHomeDemosOpen = isProductHomeDemosPath\(location\.pathname\);[\s\S]*state\.home = state\.credits\s*\|\| \(!diagnosticsOpen\s*&& !state\.packageQueryOpen\s*&& !state\.packageActivityOpen\s*&& !productHomeDemosOpen\s*&& !initialLocation\.package\s*&& !initialWorkspace\.hasWorkspaceState\s*&& !initialLocation\.routeFailure\)/);
  const restore = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}\n\nfunction isStyleTier/)?.[0]
    ?? "";
  assert.match(
    restore,
    /const navigationSeq = navigationSequence\.current\(\);\s*const loc = await workspaceLocation\.preflightCurrent\(\)\.resolve\(\);[\s\S]*framework: loc\.framework \|\| DEFAULT_REQUESTED_FRAMEWORK[\s\S]*state\.requestedPackage = resolvedLocation\.package;[\s\S]*state\.requestedVersion = resolvedLocation\.version;[\s\S]*state\.requestedFramework = resolvedLocation\.framework;[\s\S]*await restoreWorkspaceFromLocation\(\s*resolvedLocation,\s*deepLinkFromLocation\(resolvedLocation\),\s*navigationSeq\)/);
  const bootstrap = appSource.match(
    /async function bootstrap\(\)[\s\S]*?\n}\n\nobserveAsync\(bootstrap\(\)/)?.[0]
    ?? "";
  const initializeAt =
    bootstrap.indexOf("await startEngine(window.location.origin);");
  const restoreAt = bootstrap.indexOf("await restoreInitialWorkspace();");
  assert.notEqual(initializeAt, -1);
  assert.notEqual(restoreAt, -1);
  assert.ok(initializeAt < restoreAt);
  assert.match(
    bootstrap,
    /if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.workspaceSubjectOpen = true;[\s\S]*render\(\);[\s\S]*return;/);
});

test("malformed package routes use the contained restore failure path", () => {
  const restore = appSource.match(
    /async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failWorkspaceRoute/)?.[0]
    ?? "";
  assert.match(
    restore,
    /if \(loc\.routeFailure\) \{[\s\S]*if \(failureHandler\) \{\s*failureHandler\(loc\.routeFailure\.message\);\s*\} else \{\s*failWorkspaceRoute\(loc\.routeFailure\.message\);[\s\S]*return;\s*\}\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*if \(failureHandler\) \{\s*failureHandler\("The existing package route could not be cleared\."\);[\s\S]*render\(\);\s*return;\s*\}/);

  const initial = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}\n\nfunction isStyleTier/)?.[0]
    ?? "";
  assert.match(
    initial,
    /if \(loc\.routeFailure\) \{\s*await restoreWorkspaceFromLocation\(\s*loc,\s*deepLinkFromLocation\(loc\),\s*navigationSeq\);\s*return;/);

  const popstate =
    appSource.match(/window\.addEventListener\("popstate"[\s\S]*?\n\}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /if \(loc\.routeFailure\) \{\s*failWorkspaceRoute\(loc\.routeFailure\.message\);\s*return;\s*\}\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}\s*state\.queryNotice = loc\.workspaceNotice \|\| "";[\s\S]*if \(loc\.hasWorkspaceState && !loc\.shareState\) \{[\s\S]*const invalidSnapshot = captureCanonicalWorkspaceRestoreSnapshot\(\);[\s\S]*const bareHome/);

  const failure = appSource.match(
    /function failWorkspaceRoute\([\s\S]*?\n}\n\nfunction failCanonicalWorkspaceRestore/)?.[0]
    ?? "";
  assert.match(
    failure,
    /function failWorkspaceRoute\(message: string\) \{\s*if \(state\.package\)[\s\S]*failedWorkspaceUrlState = \{\s*kind: "route",\s*notice: `Package route failed: \$\{message\}`,[\s\S]*pathname: location\.pathname,\s*search: location\.search,\s*recoveryUrl: buildPackageRootStateUrl\(location\.href,[\s\S]*state\.errorTitle = "Package route failed";[\s\S]*state\.error = message;[\s\S]*state\.retryAction = retryUnavailable/);
  assert.match(
    appSource,
    /function goHome\(\) \{\s*navigationSequence\.begin\(\);\s*state\.loading = false;[\s\S]*invalidateGraphMemberNavigation\(\);\s*clearNavigationError\(\);\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}[\s\S]*workspaceLocation\.push\("\/"\);[\s\S]*render\(\)/);
  assert.match(
    appSource,
    /function visibleQueryNotice\(\) \{\s*const routeNotice = failedWorkspaceUrlState\?\.kind === "route"\s*\? failedWorkspaceUrlState\.notice\s*: null;\s*return \[state\.queryNotice, routeNotice\]\s*\.filter\(Boolean\)\s*\.join\(" "\);\s*\}/);
  assert.match(
    appSource,
    /function clearWorkspaceRouteFailure\(recoveryUrl\?: string\) \{\s*if \(failedWorkspaceUrlState\?\.kind !== "route"\) return true;\s*if \(!recoverWorkspaceRouteFailure\(\s*failedWorkspaceUrlState,\s*location,\s*url => workspaceLocation\.replace\(url, history\.state\),\s*recoveryUrl\)\) \{\s*return false;\s*\}\s*failedWorkspaceUrlState = null;\s*return true;\s*\}[\s\S]*function dismissQueryNotice\(\) \{\s*const routeFailureOnHome =\s*failedWorkspaceUrlState\?\.kind === "route" && state\.home;\s*state\.queryNotice = "";\s*state\.queryNoticeRetryAction = null;\s*if \(!clearWorkspaceRouteFailure\(routeFailureOnHome \? "\/" : undefined\)\) \{\s*render\(\);\s*return;\s*\}\s*failedWorkspaceUrlState = null;\s*render\(\);\s*\}/);
  assert.match(
    appSource,
    /const workbenchShellActions: WorkbenchShellBindingActions = \{\s*onApplicationAction: dispatchApplicationAction,\s*onCopySubjectSegment: index => \{[\s\S]*onDismissNotice: dismissQueryNotice,/);
  assert.match(
    appSource,
    /const homeShellActions: HomeShellBindingActions = \{\s*onDismissNotice: dismissQueryNotice,\s*onOpenDemos: openProductDemos,/);
  assert.equal(
    appSource.match(
      /<span class="query-notice-text">\${escapeHtml\(visibleQueryNotice\(\)\)}<\/span>/g,
    )?.length,
    1);
  assert.equal(
    appSource.match(/\${renderQueryNotice\(\)}/g)?.length,
    4);
  assert.match(
    appSource,
    /state\.queryNotice && state\.queryNoticeRetryAction\s*\? '<button id="retry-notice"/);
  assert.match(
    appSource,
    /function openCredits\(\) \{\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}/);
  assert.match(
    appSource,
    /state\.retryAction === retryUnavailable\s*\? ""\s*: `<button id="retry-load" type="button">retry<\/button>`/);
});

test("member entry controls choose the resulting content-frame pane", () => {
  const bindings =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    bindings,
    /const enterMemberNavigation = \(action: \(\) => void\) => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*contentFramePane = "navigation";\s*action\(\);\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    bindings,
    /onMemberCompositionAccessibilitySelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*enterMemberScope\(\);[\s\S]*onMemberCompositionKindSelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*onMemberCompositionTraitSelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*onMemberGroupOpen: memberKey => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*showContentDetailAfterRender\(\);\s*openMemberGroup\(memberKey\);[\s\S]*restoreContentNavigationFocus\(focusGeneration\);/);
});

test("render invalidates focus ownership before replacing content-frame DOM", () => {
  const render = functionDeclaration("render");
  const source = appSource.slice(render.start, render.end);

  assert.match(
    source,
    /const focusedElement = document\.activeElement instanceof HTMLElement[\s\S]*contentFrameFocusOwner = null;\s*contentFrameReplacementAuthority = null;[\s\S]*app\.innerHTML = `/);
});

test("content-frame focus ownership clears after focus settles outside both panes", () => {
  assert.match(
    appSource,
    /function trackContentFrameFocus\(event: FocusEvent\) \{\s*documentFocusGeneration\+\+;\s*contentFrameReplacementAuthority = null;[\s\S]*contentFrameFocusOwner = contentFrameFocusOwnerFor\(focused\)/);
  assert.match(
    appSource,
    /function trackContentFramePointer\(event: PointerEvent\) \{\s*documentFocusGeneration\+\+;\s*contentFrameReplacementAuthority = null;[\s\S]*contentFrameFocusOwner = contentFrameFocusOwnerFor\(pointed\)/);
  assert.match(
    appSource,
    /function releaseContentFrameFocusOwner\(\) \{\s*requestAnimationFrame\(\(\) => \{\s*requestAnimationFrame\(\(\) => \{[\s\S]*contentFrameFocusOwnerFor\(focused\) === null[\s\S]*contentFrameFocusOwner = null/);
  assert.match(
    appSource,
    /document\.addEventListener\("pointerdown", trackContentFramePointer\);\s*document\.addEventListener\("focusin", trackContentFrameFocus\);\s*document\.addEventListener\("focusout", releaseContentFrameFocusOwner\)/);
});

test("focus-preserving renders retain pane authority until restoration", () => {
  const capture = functionDeclaration(
    "captureContentFrameReplacementAuthority");
  const captureSource = appSource.slice(capture.start, capture.end);
  const schedule = functionDeclaration("scheduleMemberFocusAfterRender");
  const scheduleSource = appSource.slice(schedule.start, schedule.end);

  assert.match(
    appSource,
    /const resizeFocusOwner = contentFrameResizeFocusOwner\(\s*focused,\s*contentFrameFocusOwner,\s*replacementFocusOwner\);[\s\S]*contentFrameReplacementAuthority = null;[\s\S]*decideContentFrameResize\([\s\S]*resizeFocusOwner\)/);
  assert.match(
    captureSource,
    /owner: contentFrameFocusOwnerFor\(focused\),\s*focusGeneration: documentFocusGeneration/);
  assert.match(
    scheduleSource,
    /replacementAuthority\.owner !== null[\s\S]*replacementAuthority\.focusGeneration === documentFocusGeneration[\s\S]*contentFrameReplacementAuthority = replacementAuthority/);
  assert.match(
    scheduleSource,
    /memberFocusRestorer\.schedule\([\s\S]*requestAnimationFrame,\s*\(\) => replacementAuthority\.focusGeneration === documentFocusGeneration\);[\s\S]*requestAnimationFrame\(\(\) => \{[\s\S]*contentFrameReplacementAuthority === replacementAuthority[\s\S]*contentFrameReplacementAuthority = null/);
  assert.match(
    appSource,
    /function selectMemberNavEntry\([\s\S]*captureContentFrameReplacementAuthority\(\)[\s\S]*scheduleMemberFocusAfterRender\(preservedFocus, replacementAuthority\)/);
});

test("package-root Open and selected-Type activation preserve local frame state", () => {
  const drillIn = functionDeclaration("drillIn");
  const drillInSource = appSource.slice(drillIn.start, drillIn.end);
  const bindings =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";

  assert.match(
    drillInSource,
    /if \(state\.atPackageRoot\) \{\s*if \(!enterRetainedLibrarySubject\(\)\) return;\s*showContentDetailAfterRender\(\);\s*render\(\);\s*return;/);
  assert.match(
    drillInSource,
    /if \(state\.atLibraryRoot\) \{\s*if \(!enterTypeSubject\(selectedType\(\)\)\) return;\s*showContentDetailAfterRender\(\);\s*render\(\);/);
  assert.match(
    drillInSource,
    /if \(navMode\(\) === "type"\) \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*if \(enterMemberScope\(\)\) \{\s*contentFramePane = "navigation";\s*render\(\);\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    bindings,
    /onTypeSelect: typeId => \{\s*if \(scope\(\) === "type" && typeId === state\.selectedTypeId\) \{\s*if \(contentFrameMedia\.matches\) showContentDetail\(\);\s*return;\s*}\s*showContentDetailAfterRender\(\);[\s\S]*resetMemberFilters\(\)/);
});

test("workspace package selection resets type-specific member filters", () => {
  const selection =
    appSource.match(/function selectWorkspacePackage\([\s\S]*?\n}\n\nfunction activatePackage/)?.[0]
    ?? "";
  assert.match(
    selection,
    /state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);[\s\S]*resetMemberFilters\(\);[\s\S]*resetMemberSectionState\(\)/);
});

test("loaded-package Spotlight selection reuses the complete package transition", () => {
  const selection =
    appSource.match(/function pickSpotlightLoadedPackage\([\s\S]*?\n}\n\nasync function pickSpotlightMember/)?.[0]
    ?? "";
  const packageTransition =
    appSource.match(/function selectWorkspacePackage\([\s\S]*?\n}\n\nfunction workspaceOccurrenceRequest/)?.[0]
    ?? "";
  assert.match(
    selection,
    /spotlight\.reset\(\);\s*selectWorkspacePackage\(target, \{ publishInitial: true \}\);\s*if \(retainedWorkspaces\.activeWorkspaceId === null\) return;\s*focusTypeList\(focusGeneration\)/);
  assert.match(
    packageTransition,
    /publishInitial = false[\s\S]*const rollbackSnapshot = publishInitial\s*&& retainedWorkspaces\.activeWorkspaceId === null[\s\S]*publishInitialLoadedWorkspace\(rollbackSnapshot\)[\s\S]*render\(\{ synchronizeUrl: rollbackSnapshot === null \}\)/);
});

test("loaded Platform selections publish the first retained Workspace", () => {
  const memberSelection =
    appSource.match(/async function pickSpotlightMember\([\s\S]*?\n}\n\nasync function pickSpotlight\(/)?.[0]
    ?? "";
  const typeSelection =
    appSource.match(/async function pickSpotlight\([\s\S]*?\n}\n\nfunction executeCommand/)?.[0]
    ?? "";
  const publication =
    appSource.match(/function publishInitialLoadedWorkspace\([\s\S]*?\n}\n\nfunction publishCurrentWorkspace/)?.[0]
    ?? "";

  assert.match(
    publication,
    /void buildStateUrl\(\)\.then\(\s*destination => \{[\s\S]*publishCurrentWorkspace\(null\);\s*workspaceLocation\.push\(destination\.toString\(\)\)/);
  for (const selection of [memberSelection, typeSelection]) {
    assert.match(
      selection,
      /const rollbackSnapshot = retainedWorkspaces\.activeWorkspaceId === null[\s\S]*publishInitialLoadedWorkspace\(rollbackSnapshot\)[\s\S]*render\(\{ synchronizeUrl: rollbackSnapshot === null \}\)/);
  }
});

test("foreground package reload resets filters before selecting its first type", () => {
  const loadPackage =
    appSource.match(/async function loadPackage\([\s\S]*?\n}\n\nfunction platformSurfaceLoaded/)?.[0]
    ?? "";
  assert.match(
    loadPackage,
    /if \(deep\) \{[\s\S]*applyDeepLink\(deep\);[\s\S]*\} else \{\s*resetMemberFilters\(\);\s*state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);/);
});

test("home demos execute typed results and commit canonical navigation", () => {
  const runHomeDemo =
    appSource.match(/function runHomeDemo\([\s\S]*?\n}\n\n\/\/ Return to the intro/)?.[0]
    ?? "";
  const resolveDemo =
    appSource.match(/async function resolveAndRunHomeDemo\([\s\S]*?\n}\n\nfunction openWorkspacePackagePicker/)?.[0]
    ?? "";
  assert.match(
    resolveDemo,
    /const snapshot = captureCanonicalWorkspaceRestoreSnapshot\(\);[\s\S]*const construction =\s*captureWorkspaceConstructionSnapshots\(navigationSeq\);[\s\S]*prepareUnpublishedWorkspace\(\);\s*await runEngineHomeDemo\(\s*kind,\s*construction\.rollbackSnapshot \?\? snapshot,\s*construction\.retainedSnapshot,\s*navigationSeq,/);
  assert.match(
    runHomeDemo,
    /finally \{\s*cancelDemoNavigation\(navigationSeq\);[\s\S]*function failDemoWorkspaceOpen\([\s\S]*failWorkspaceCatalogAction\(\s*`Demo failed: \$\{message\}`,\s*snapshot,\s*retryable \? \(\) => runHomeDemo\(demoId\) : null,\s*\(\) => restoreWorkspaceFocus\(document, \{ kind: "demo", id: demoId \}\)/);
  assert.doesNotMatch(runHomeDemo, /workspaceLocation\.replace\("\/demos"/);
  assert.doesNotMatch(
    resolveDemo,
    /\bresolveHomeDemo\(|productHomeDemoLocationHref|parseWorkspaceHref/);
  const restoreWorkspace =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failWorkspaceRoute/)?.[0]
    ?? "";
  assert.match(
    restoreWorkspace,
    /applyLocationView\(loc\);[\s\S]*await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);[\s\S]*applyDeepLink\(deep\)/);
  assert.match(
    appSource,
    /function applyLocationView\(loc: ParsedLocation\) \{\s*state\.rootKind = loc\.rootKind;\s*state\.lens = loc\.lens \|\| "api";\s*state\.atPackageRoot = loc\.atPackageRoot \|\| false;\s*if \(loc\.hasWorkspaceState\s*&& state\.atPackageRoot\s*&& loc\.packageLens === "dependencies"\) \{\s*state\.dependenciesGroupIndex = null;\s*\}\s*state\.atLibraryRoot = !state\.atPackageRoot\s*&& \(loc\.atLibraryRoot \|\| false\);\s*state\.workspaceSubjectOpen =\s*loc\.workspaceSubjectOpen && state\.atPackageRoot;[\s\S]*?state\.packageLens = loc\.packageLens \|\| "overview";\s*state\.libraryLens = loc\.libraryLens \|\| "overview";/);
  const engineDemo =
    appSource.match(/async function runEngineHomeDemo\([\s\S]*?\n}\n\n\/\/ Loads the full/)?.[0]
    ?? "";
  assert.match(engineDemo, /result = await inspectRunHomeDemo\(demoId\)/);
  assert.match(engineDemo, /prepareProductHomeDemoSource\(result\)/);
  assert.doesNotMatch(
    engineDemo,
    /resolveHomeDemo|productHomeDemoLocationHref|loadPackage\(/);
  assert.match(
    appSource,
    /async function installPlatformHomeDemoSource\([\s\S]*platformGraphLibraryForTarget\([\s\S]*platformLibraryMatchesDescriptor\([\s\S]*openPlatformLibrary\([\s\S]*scopeOnly: true,[\s\S]*navigationSeq,[\s\S]*tfm: target\.tfm,[\s\S]*version: target\.version/);
  assert.match(
    engineDemo,
    /if \(source\.kind === "package"\) \{[\s\S]*installPackageHomeDemoSource\(source\);[\s\S]*\} else \{[\s\S]*installPlatformHomeDemoSource\([\s\S]*\}/);
  assert.match(
    engineDemo,
    /state\.loading = false;\s*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*stageDemoNavigation\(navigationSeq, destination\);\s*render\(\);\s*if \(isCallGraph && result\.callGraph\) \{\s*let renderResult = await renderMermaidCallGraph\(\);[\s\S]*const publication = stageCurrentWorkspacePublication\(\s*previousSnapshot,\s*destination\);\s*if \(!commitStagedWorkspaceNavigation\(navigationSeq, publication\)\)/);
  assert.match(
    engineDemo,
    /if \(!commitStagedWorkspaceNavigation\(navigationSeq, publication\)\)[\s\S]*syncUrl\(\);\s*render\(\{ synchronizeUrl: false \}\);\s*focusInspectionResult\(navigationSeq\)/);
  assert.doesNotMatch(engineDemo, /publishCurrentWorkspace\(/);
  assert.match(
    appSource,
    /function applyProductHomeDemoSelection\([\s\S]*state\.libraryScope = new Set\(\[libraryKey\(type\)\]\);[\s\S]*revealTypeInFilters\(type\);[\s\S]*state\.selectedTypeId = type\.id;[\s\S]*state\.atPackageRoot = false;[\s\S]*state\.atLibraryRoot = false;[\s\S]*resetMemberSectionState\(\);[\s\S]*state\.memberBrowseTypeId = member \? type\.id : "";[\s\S]*state\.selectedMemberKey = member\?\.key \?\? "";[\s\S]*state\.selectedOverloadIndex = overloadIndex;[\s\S]*state\.memberCallGraph = result\.callGraph/);
  assert.match(
    restoreWorkspace,
    /await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*if \(!commitRestoredWorkspaceNavigation\(navigationSeq, failureHandler, commitHistory\)\) return;\s*if \(focusResult\) \{\s*focusInspectionResult\(navigationSeq\);/);
  assert.match(
    restoreWorkspace,
    /if \(loadedPlatformTarget\)[\s\S]*commitWorkspaceShareBasis\(loc\.shareState\);\s*if \(!failureHandler\) \{\s*ensureCurrentWorkspacePublished\(\);\s*\}\s*state\.loading = false;\s*render\(\);\s*startPlatformTargetWork\(loadedPlatformTarget\);\s*await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*if \(!commitRestoredWorkspaceNavigation\(navigationSeq, failureHandler, commitHistory\)\) return;/);
  assert.match(
    restoreWorkspace,
    /commitWorkspaceShareBasis\(loc\.shareState\);\s*if \(!failureHandler\) \{\s*ensureCurrentWorkspacePublished\(\);[\s\S]*state\.loading = false;\s*render\(\);\s*await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*if \(!commitRestoredWorkspaceNavigation\(navigationSeq, failureHandler, commitHistory\)\) return;[\s\S]*if \(!failureHandler\) \{\s*workspaceLocation\.replace\(location\.href, history\.state\);/);
  assert.match(
    appSource,
    /function commitRestoredWorkspaceNavigation\([\s\S]*if \(!failureHandler \|\| !commitHistory\) return true;\s*if \(!commitDemoNavigation\(navigationSeq\)\) return false;\s*syncUrl\(\);/);
  assert.match(
    restoreWorkspace,
    /if \(!clearWorkspaceRouteFailure\(\)\) \{\s*if \(failureHandler\) \{\s*failureHandler\("The existing package route could not be cleared\."\);[\s\S]*if \(!loc\.package\) \{\s*failureHandler\?\.\(\s*"The resolved product demo did not identify a package\."\)/);
  assert.match(
    restoreWorkspace,
    /const retryRestore = \(\) => restoreWorkspaceFromLocation\(\s*loc,\s*deep,\s*undefined,\s*undefined,\s*focusResult,\s*failureHandler,\s*commitHistory\)/);
  assert.match(
    restoreWorkspace,
    /failCanonicalWorkspaceRestore\([\s\S]*canonicalSnapshot,\s*retryRestore\)/);
  assert.match(
    restoreWorkspace,
    /applyPlatformLibraryScope\([\s\S]*\(\) => restoreWorkspaceFromLocation\(\s*loc,\s*deep,\s*undefined,\s*canonicalSnapshot,\s*focusResult,\s*failureHandler,\s*commitHistory\)/);
  assert.match(
    restoreWorkspace,
    /const loaded = await loadPackage\([\s\S]*if \(loaded && focusResult && navigationSequence\.isCurrent\(navigationSeq\)\) \{[\s\S]*focusInspectionResult\(navigationSeq\);[\s\S]*state\.retryAction = retryRestore/);
  const platformHistory =
    appSource.match(/async function restorePlatformScopeThenDeepLink\([\s\S]*?\n}\n\n\/\/ Load and scope/)?.[0]
    ?? "";
  assert.match(
    platformHistory,
    /await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);\s*applyDeepLink\(loc\)/);
});

test("Spotlight package opening retains the active Workspace and publishes a fresh one", () => {
  const spotlightPackageLoad =
    appSource.match(/async function loadPackageFromSpotlight\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    spotlightPackageLoad,
    /if \(!canPublishRetainedWorkspace\(\)\) \{[\s\S]*retainedWorkspaceCapacityMessage\(\)[\s\S]*return;/);
  assert.match(
    spotlightPackageLoad,
    /const navigationSeq = navigationSequence\.begin\(\);[\s\S]*const \{ rollbackSnapshot, retainedSnapshot \} =\s*captureWorkspaceConstructionSnapshots\(navigationSeq\);\s*prepareUnpublishedWorkspace\(\);/);
  assert.match(
    spotlightPackageLoad,
    /deferWorkspacePublication: true,[\s\S]*failureHandler: \(message: string\) => \{[\s\S]*failWorkspaceCatalogAction\(\s*message,\s*rollbackSnapshot,[\s\S]*if \(loaded\) \{[\s\S]*destination = \(await buildStateUrl\(\)\)\.toString\(\);[\s\S]*failWorkspaceCatalogAction\([\s\S]*rollbackSnapshot,[\s\S]*return;[\s\S]*publishCurrentWorkspace\(retainedSnapshot\);\s*workspaceLocation\.push\(destination\);\s*render\(\{ synchronizeUrl: false \}\);\s*focusTypeList\(navigationGeneration, focusGeneration\)/);

  const prepareWorkspace =
    appSource.match(/function prepareUnpublishedWorkspace\(\): void \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    prepareWorkspace,
    /clearWorkspacePackages\(\);\s*state\.queryNotice = "";\s*state\.queryNoticeRetryAction = null;/);

  const catalogFailure =
    appSource.match(/function failWorkspaceCatalogAction\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    catalogFailure,
    /restoreCanonicalWorkspaceRestoreSnapshot\(snapshot\);[\s\S]*state\.loading = false;[\s\S]*state\.error = "";[\s\S]*state\.retryAction = null;[\s\S]*appendQueryNotice\(message, retry\);[\s\S]*render\(\);[\s\S]*if \(!restoreFocus\(\)\) \{\s*focusWorkspaceOrHeading\(\)/);
  assert.doesNotMatch(
    catalogFailure,
    /state\.(?:workspaceSubjectOpen|atPackageRoot|atLibraryRoot)\s*=/);
  assert.doesNotMatch(catalogFailure, /workspaceLocation\.(?:push|replace)/);

  assert.match(
    appSource,
    /const innerNavigationSequence = createNavigationSequence\(\);[\s\S]*begin\(\): number \{\s*if \(packageContentLoadingSequence !== null\s*&& innerNavigationSequence\.isCurrent\(packageContentLoadingSequence\)\) \{\s*state\.loading = false;\s*\}\s*packageContentLoadingSequence = null;\s*packageContentLoadingFocusControl = null;\s*cancelPendingWorkspaceConstruction\(\);\s*settleInterruptedPlatformStatus\(state\);\s*return innerNavigationSequence\.begin\(\);[\s\S]*invalidate\(\): void \{\s*cancelPendingWorkspaceConstruction\(\);\s*settleInterruptedPlatformStatus\(state\);/);
  assert.match(
    appSource,
    /function cancelPendingWorkspaceConstruction\(\): void \{[\s\S]*pendingWorkspaceConstruction = null;\s*memberDetailInspection\.invalidate\(\);[\s\S]*releaseRetainedWorkspaceSnapshot\(pending\.retainedSnapshot\);[\s\S]*restoreCanonicalWorkspaceRestoreSnapshot\(pending\.supersessionSnapshot\);/);
  assert.match(
    appSource,
    /function discardPendingWorkspaceConstruction\(\): void \{[\s\S]*pendingWorkspaceConstruction = null;\s*if \(pending\) memberDetailInspection\.invalidate\(\);/);

  const loadPackage =
    appSource.match(/async function loadPackage\([\s\S]*?\n}\n\nfunction platformSurfaceLoaded/)?.[0]
    ?? "";
  assert.match(
    loadPackage,
    /if \(options\.failureHandler\) \{\s*options\.failureHandler\(friendly\.message\);\s*return null;\s*\}\s*if \(prevPackage\)/);

  const platformLibraryLoad =
    appSource.match(/async function openPlatformLibrary\([\s\S]*?\n}\n\nfunction pickSpotlightLoadedPackage/)?.[0]
    ?? "";
  assert.match(
    platformLibraryLoad,
    /ensurePlatformCatalog\(tfm, version\)[\s\S]*installPlatformTarget\(target\)[\s\S]*state\.platformOpeningStatus = \{ loading: true/);
  assert.match(
    platformLibraryLoad,
    /if \(!pkg\) throw new Error\(runtimeResult\.failureMessage[\s\S]*state\.platformOpeningStatus = \{ loading: false, error: `Could not open Platform Library:[\s\S]*platformLibraryRetry = options\.retryAction/);
  assert.match(
    platformLibraryLoad,
    /const createsWorkspace = !scopeOnly && options\.inPlace !== true;[\s\S]*if \(createsWorkspace && !canPublishRetainedWorkspace\(\)\)[\s\S]*const construction = createsWorkspace\s*\? captureWorkspaceConstructionSnapshots\(navigationSeq\)\s*: null;[\s\S]*if \(construction\) prepareUnpublishedWorkspace\(\);/);
  assert.match(
    platformLibraryLoad,
    /catch \(error\) \{[\s\S]*const rollbackSnapshot = construction\?\.rollbackSnapshot[\s\S]*if \(rollbackSnapshot\) \{\s*failWorkspaceCatalogAction\([\s\S]*rollbackSnapshot,\s*\(\) => openPlatformLibrary\(assembly, pack, \{ \.\.\.options, tfm, version \}\),\s*focusWorkbenchSearchOrHeading\);[\s\S]*return undefined;/);
  assert.match(
    platformLibraryLoad,
    /if \(construction\) \{\s*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return undefined;\s*publishCurrentWorkspace\(construction\.retainedSnapshot\);\s*workspaceLocation\.push\(destination\)/);
  assert.match(
    appSource,
    /function focusWorkbenchSearchOrHeading\(\): boolean \{\s*return focusWorkbenchSearch\(document\) \|\| focusLevelOneHeading\(\);\s*}/);
});

test("catalog rollback reacquires Workspace occurrences with current authority", () => {
  const restoreSnapshot =
    appSource.match(/function restoreCanonicalWorkspaceRestoreSnapshot\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    restoreSnapshot,
    /clearWorkspaceOccurrenceView\(\);[\s\S]*Object\.assign\(state, snapshot\.state\);[\s\S]*state\.workspaceOccurrenceSignature = "";[\s\S]*state\.workspaceOccurrenceLoading = false;[\s\S]*state\.workspaceOccurrences = null;[\s\S]*state\.workspaceOccurrenceError = "";/);
  assert.match(
    appSource,
    /platformLibraryRetry,\s*platformCatalogRetry,[\s\S]*platformLibraryRetry = snapshot\.platformLibraryRetry;\s*platformCatalogRetry = snapshot\.platformCatalogRetry;/);
  assert.match(
    appSource,
    /platformLibraryRetry: snapshot\.platformLibraryRetry,\s*platformCatalogRetry: snapshot\.platformCatalogRetry,/);
  assert.match(
    appSource,
    /function settleInterruptedPlatformStatus\(targetState: AppState\)[\s\S]*if \(targetState\.platformCatalogStatus\.loading\)[\s\S]*error: "Platform catalog loading was interrupted\."[\s\S]*if \(targetState\.platformOpeningStatus\.loading\)[\s\S]*error: "Platform Library opening was interrupted\."/);
  assert.match(
    appSource,
    /const navigationSequence = \{[\s\S]*begin\(\): number \{[\s\S]*settleInterruptedPlatformStatus\(state\);[\s\S]*invalidate\(\): void \{[\s\S]*settleInterruptedPlatformStatus\(state\);/);
  assert.match(
    appSource,
    /retryAction: \(\) =>\s*restorePlatformHistoryView\(view, row, navigationSequence\.current\(\)\)/);

  const ensureOccurrence =
    appSource.match(/function ensureWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    ensureOccurrence,
    /const signature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);\s*if \(state\.workspaceOccurrenceLoading\) return;\s*if \(signature === state\.workspaceOccurrenceSignature\) return;/);
  const occurrenceQuery =
    appSource.match(/async function queryWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    occurrenceQuery,
    /!superseded[\s\S]*revision === workspaceOccurrenceRevision[\s\S]*signature === state\.workspaceOccurrenceSignature[\s\S]*signature === JSON\.stringify\(workspaceOccurrenceRequest\(\)\)/);
  assert.match(
    occurrenceQuery,
    /const ownsCurrentRequest =\s*revision === workspaceOccurrenceRevision\s*&& signature === state\.workspaceOccurrenceSignature;\s*if \(ownsCurrentRequest\) state\.workspaceOccurrenceLoading = false;\s*const desiredSignature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);[\s\S]*&& !state\.workspaceOccurrenceLoading[\s\S]*state\.workspaceOccurrenceSignature !== desiredSignature[\s\S]*state\.workspaceOccurrenceSignature = "";\s*ensureWorkspaceOccurrenceView\(\);/);
  const clearOccurrence =
    appSource.match(/function clearWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    clearOccurrence,
    /workspaceOccurrenceRevision\+\+;[\s\S]*state\.workspaceOccurrenceSignature = "";[\s\S]*state\.workspaceOccurrenceLoading = false;[\s\S]*state\.workspaceOccurrences = null;/);
});

test("Workspace occurrence rerenders preserve catalog failure focus", () => {
  const render =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?\n}\n\nfunction renderWorkspaceCatalogView/,
    )?.[0]
    ?? "";
  assert.match(
    render,
    /const workbenchSearchHadFocus = focusedElement\?\.id === "open-search";\s*const levelOneHeadingHadFocus =\s*focusedElement\?\.matches\("main h1"\) === true;/);
  assert.equal(
    render.match(
      /else if \(workbenchSearchHadFocus\) \{\s*focusWorkbenchSearch\(document\);\s*} else if \(levelOneHeadingHadFocus\) \{\s*focusLevelOneHeading\(\);/g,
    )?.length,
    2);
  const occurrenceQuery =
    appSource.match(/async function queryWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(occurrenceQuery, /finally \{[\s\S]*render\(\);\s*}/);
});

test("lens-scoped Platform library changes reset type-specific member state", () => {
  const picker =
    appSource.match(/async function openPlatformLensLibrary\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    picker,
    /originPackage: AppPackage = currentPackage\(\),[\s\S]*noticeRetryState: NoticeRetryState \| null = null[\s\S]*if \(!state\.packages\.includes\(originPackage\)[\s\S]*!packageIdentityEquals\(state\.package, originPackage\)[\s\S]*state\.queryNoticeRetryAction === noticeRetryState\.action[\s\S]*state\.queryNotice = removeAppendedNotice\([\s\S]*state\.queryNoticeRetryAction = null;[\s\S]*const pack = selectedPack \|\| platformPackForAssembly\(key\);[\s\S]*state\.platformIndex\?\.target\(\s*originPackage\.activeFramework,\s*originPackage\.version\)[\s\S]*row\.hasImplementation[\s\S]*row\.pack === pack[\s\S]*row\.assembly\.toLowerCase\(\) === key\.toLowerCase\(\)[\s\S]*runtimeAssemblyIsResident\(\s*originPackage,\s*row\.assembly,\s*row\.pack\)[\s\S]*const runtimeResult = await loadRuntimePackAssembly\(\s*originPackage\.activeFramework,\s*platformAssemblyRequest\(row\),\s*row\.pack,\s*isCurrent,\s*originPackage\.version,\s*row\.file\);[\s\S]*const loaded = runtimeResult\.packageModel;[\s\S]*previous: state\.queryNotice[\s\S]*const retryAction = \(\) =>\s*openPlatformLensLibrary\([\s\S]*noticeState\);[\s\S]*runtimeResult\.failureMessage[\s\S]*noticeState\.appended = state\.queryNotice;[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*state\.libraryScope = new Set\(\[library\.id\]\);[\s\S]*normalizeLibrarySelection\(\);[\s\S]*lens === "integrations"[\s\S]*state\.integrationMode === "opportunities"[\s\S]*loadPackageOpportunities\(\)[\s\S]*loadPackageIntegrations\(\)[\s\S]*lens === "analysis"[\s\S]*loadPackagePerformance\(\)[\s\S]*loadPackageMetadata\(\)/);
  assert.doesNotMatch(
    picker,
    /\(\) => state\.packages\.includes\(originPackage\)/);
  assert.doesNotMatch(picker, /select\.isConnected/);
  assert.match(
    appSource,
    /function normalizeLibrarySelection\(\) \{[\s\S]*state\.selectedTypeId = first\?\.id \|\| "";[\s\S]*state\.selectedMemberKey = "";[\s\S]*state\.selectedOverloadIndex = null;[\s\S]*resetMemberFilters\(\)[\s\S]*function afterLibraryScopeChange\(\) \{\s*normalizeLibrarySelection\(\);\s*renderPreservingMemberFocus\(\)/);
});

test("package Metadata retries remain explicit rather than render-driven", () => {
  const autoLoad =
    appSource.match(/function maybeAutoLoadPackageMetadata\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    autoLoad,
    /state\.packageMetadataKey === packageScopeSignature\(\)\) return;[\s\S]*observeAsync\(loadPackageMetadata\(\)/);
  assert.doesNotMatch(autoLoad, /packageMetadataError/);
});

test("Spotlight searches framework Libraries without offering a Platform root", () => {
  const results =
    appSource.match(/function frameworkLibrarySpotlightResults\(query: string\): SpotlightResult\[\] \{[\s\S]*?\n}\n/)?.[0]
    ?? "";
  assert.ok(results, "frameworkLibrarySpotlightResults was not found");
  assert.match(
    results,
    /if \(platformSurfaceLoaded\(\)\) \{[\s\S]*spotlightTypeMatches\(query\)/);
  assert.match(results, /kind: "framework-lib"/);
  assert.doesNotMatch(results, /kind: "platform"|rtpack-suggest/);
});

test("Workspace Platform presentation follows provenance rather than the active package", () => {
  const sourceFor = (name: string) => {
    const declaration = functionDeclaration(name);
    return appSource.slice(declaration.start, declaration.end);
  };
  const platformPresentation = sourceFor("platformIsPresentedAsRoot");
  const rootPresentation = sourceFor("rootIsPresented");
  const workspace = sourceFor("renderWorkspaceView");

  assert.match(
    platformPresentation,
    /state\.platformSelection !== null[\s\S]*state\.platformPresentedAsRoot[\s\S]*hasPlatformRootHistoryView\(\)/);
  assert.doesNotMatch(
    platformPresentation,
    /state\.rootKind !== "platform"/);
  assert.match(
    rootPresentation,
    /state\.rootKind !== "platform"\s*\|\|\s*platformIsPresentedAsRoot\(\)/);
  assert.match(
    workspace,
    /const presentPlatform = platformIsPresentedAsRoot\(\);[\s\S]*runtimePackageForTarget\(state\.platformSelection\)[\s\S]*state\.package === frameworkPackage[\s\S]*state\.frameworkLibraryPresentation[\s\S]*state\.frameworkLibraryPresentation\.libraryId[\s\S]*frameworkPackage\.assemblyId[\s\S]*platform: presentPlatform \? state\.platformSelection : null[\s\S]*frameworkLibraries: frameworkLibrary[\s\S]*assembly: frameworkLibrary\.name[\s\S]*pack: frameworkLibrary\.platformPack/);
  assert.match(
    appSource,
    /onFrameworkLibrary: \(assembly, pack, tfm, version\) =>[\s\S]*openPlatformLibrary\(assembly, pack, \{[\s\S]*deferPlatformPresentation: true,[\s\S]*inPlace: true,[\s\S]*tfm,[\s\S]*version/);
  assert.match(
    appSource,
    /function activatePackage\([\s\S]*state\.package\?\.isRuntimePack[\s\S]*state\.frameworkLibraryPresentation = \{[\s\S]*libraryId: library\.id/);
});

test("Spotlight keeps loaded framework Library navigation in place", () => {
  const roster =
    appSource.match(/function platformLibraryRoster\(query: string\) \{[\s\S]*?\n}\n/)?.[0]
    ?? "";
  const picker =
    appSource.match(/function pickSpotlightResult\(result: SpotlightResult\) \{[\s\S]*?\n}\n/)?.[0]
    ?? "";
  const openLibrary =
    appSource.match(/async function openPlatformLibrary\([\s\S]*?\n}\n\nfunction pickSpotlightLoadedPackage/)?.[0]
    ?? "";
  const memberSelection =
    appSource.match(/async function pickSpotlightMember\([\s\S]*?\n}\n\nasync function pickSpotlight\(/)?.[0]
    ?? "";
  const typeSelection =
    appSource.match(/async function pickSpotlight\([\s\S]*?\n}\n\nfunction executeCommand/)?.[0]
    ?? "";

  assert.match(
    roster,
    /loaded: runtimeAssemblyIsResident\(rt, row\.assembly, row\.pack\)/);
  assert.match(
    picker,
    /openPlatformLibrary\(\s*result\.assembly,\s*result\.pack,\s*\{[\s\S]*deferPlatformPresentation: true,[\s\S]*inPlace: result\.loaded === true,[\s\S]*tfm: result\.tfm,[\s\S]*version: result\.version/);
  assert.match(picker, /case "framework-lib":/);
  assert.doesNotMatch(picker, /case "platform":|case "rtpack-suggest":/);
  assert.doesNotMatch(appSource, /function activateRuntimePack\(/);
  assert.match(
    openLibrary,
    /if \(createsWorkspace\) spotlight\.reset\(\);\s*const construction = createsWorkspace\s*\? captureWorkspaceConstructionSnapshots\(navigationSeq\)\s*: null/);
  for (const selection of [memberSelection, typeSelection]) {
    assert.match(
      selection,
      /beginSpotlightNavigation\(\);[\s\S]*spotlight\.reset\(\);\s*const rollbackSnapshot = retainedWorkspaces\.activeWorkspaceId === null\s*\? captureCanonicalWorkspaceRestoreSnapshot\(\)/);
  }
});

test("Package query and Activity are routed Spotlight actions", () => {
  const results =
    appSource.match(/function spotlightResults\(\): SpotlightResult\[\] \{[\s\S]*?\n}\n\ninterface NugetSearchResult/)?.[0]
    ?? "";
  const route =
    appSource.match(/function openPackageQueryRoute\([\s\S]*?\n}\n\nfunction openPackageActivityRoute/)?.[0]
    ?? "";
  const activityRoute =
    appSource.match(/function openPackageActivityRoute\([\s\S]*?\n}\n\nasync function selectWorkspaceApplicationScope/)?.[0]
    ?? "";
  const closeActivityRoute =
    appSource.match(/function closePackageActivityRoute\(\)[\s\S]*?\n}\n\nfunction preparePackageQueryRequest/)?.[0]
    ?? "";
  const handoff =
    appSource.match(/async function openPackageQueryRow\([\s\S]*?\n}\n\nconst packageQueryActions/)?.[0]
    ?? "";
  const syncUrl =
    appSource.match(/function syncUrl\(\)[\s\S]*?\n}/)?.[0]
    ?? "";

  assert.match(
    results,
    /kind: "package-query",[\s\S]*prefix: validPackageQuerySearchText\(query\),/);
  assert.match(results, /kind: "package-activity"/);
  assert.match(
    appSource,
    /case "package-query":\s*openPackageQueryRoute\(result\.prefix\);\s*break;/);
  assert.match(
    appSource,
    /case "package-activity":\s*openPackageActivityRoute\(\);\s*break;/);
  assert.match(
    route,
    /const predecessorEntryId = ensureCurrentHistoryEntryId\(\);[\s\S]*state\.packageQueryOpen = true;[\s\S]*workspaceLocation\.push\([\s\S]*"\/query",[\s\S]*packageQueryHistoryState\([\s\S]*predecessorEntryId,[\s\S]*returnFocus[\s\S]*focusPackageQueryInput\(\)/);
  assert.doesNotMatch(route, /packageQueryController\.run/);
  assert.match(
    activityRoute,
    /const predecessorEntryId = ensureCurrentHistoryEntryId\(\);[\s\S]*state\.packageQueryOpen = false;[\s\S]*state\.packageActivityOpen = true;[\s\S]*workspaceLocation\.push\(\s*PACKAGE_ACTIVITY_PATH,[\s\S]*packageActivityHistoryState\([\s\S]*predecessorEntryId,[\s\S]*returnFocus[\s\S]*focusPackageActivityInput\(\)/);
  assert.match(
    appSource,
    /function focusPackageActivityInput\(\) \{[\s\S]*const packageSet = document\.querySelector<HTMLSelectElement>\([\s\S]*if \(packageSet && !packageSet\.disabled\) \{[\s\S]*packageSet\.focus\(\);[\s\S]*document\.activeElement === packageSet[\s\S]*focusLevelOneHeading\(\)/);
  assert.match(
    closeActivityRoute,
    /packageChangesController\.cancel\("disposed"\);[\s\S]*state\.packageActivityOpenedFromApp[\s\S]*history\.back\(\)[\s\S]*state\.packageActivityOpen = false;[\s\S]*workspaceLocation\.replace\("\/"\)/);
  assert.match(
    appSource,
    /createPackageChangesController\([\s\S]*if \(!state\.packageActivityOpen\) return;\s*schedulePackageActivityStreamRender\(\)/);
  assert.match(
    appSource,
    /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{\s*sourceInspection\.cancelHiddenRequest\(\);[\s\S]*?document\.body\.classList\.remove\(\s*"package-query-route",\s*"package-activity-route"\);[\s\S]*if \(state\.packageQueryOpen\s*&& state\.engineReady\s*&& !state\.loading\s*&& !state\.error\) \{\s*document\.body\.classList\.add\("package-query-route"\)/);
  assert.match(
    stylesSource,
    /@media \(max-width: 860px\) \{\s*body\.package-query-route,\s*body\.package-activity-route \{ min-width: 0; \}/);
  assert.match(
    handoff,
    /if \(!canPublishRetainedWorkspace\(\)\)[\s\S]*packageQueryController\.cancel\(\);\s*packageChangesController\.cancel\("disposed"\);\s*discardPackageQueryTermEditors\(\);\s*state\.packageQueryOpen = false;\s*const navigationSeq = navigationSequence\.begin\(\);\s*const \{ rollbackSnapshot, retainedSnapshot \} =\s*captureWorkspaceConstructionSnapshots\(navigationSeq\);\s*prepareUnpublishedWorkspace\(\);[\s\S]*await loadPackage\([\s\S]*deferWorkspacePublication: true,[\s\S]*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) \{[\s\S]*return;\s*\}[\s\S]*packageQueryHandoffNavigationSeq = null;[\s\S]*destination = \(await buildStateUrl\(\)\)\.toString\(\);[\s\S]*discardPendingWorkspaceConstruction\(\);[\s\S]*restoreCanonicalWorkspaceRestoreSnapshot\(rollbackSnapshot\);[\s\S]*state\.packageQueryOpen = true;[\s\S]*return;[\s\S]*publishCurrentWorkspace\(retainedSnapshot\);\s*workspaceLocation\.push\(destination\)/);
  assert.match(
    syncUrl,
    /function syncUrl\(\) \{\s*if \(activeRetainedWorkspacePosting !== null\) return;\s*if \(currentPackageQueryHandoff\(\)\) return;\s*if \(pendingDemoNavigation[\s\S]*navigationSequence\.isCurrent\(pendingDemoNavigation\.navigationSeq\)\) return;\s*if \(pendingWorkspaceConstruction[\s\S]*pendingWorkspaceConstruction\.navigationSeq\)\) return;\s*if \(retainFailedWorkspaceUrl\(\)\) return;/);
  assert.match(
    handoff,
    /state\.packageQueryNavigationError = failure;[\s\S]*data-query-row-open=/);
  assert.doesNotMatch(handoff, /state\.packageQueryOpenedFromApp = false/);
  assert.doesNotMatch(handoff, /state\.packageQueryReturnFocus = null/);
  assert.doesNotMatch(
    handoff,
    /state\.packageQueryReturnFocusPending = true/);
  assert.match(
    appSource,
    /function renderPackageQueryPage\(\) \{\s*packageQueryRender\.renderFull\(\);\s*}\s*function replacePackageQueryPage\(\) \{\s*const focus = capturePackageQueryFocus\(document\);\s*const viewport =\s*capturePackageQueryViewport\(document\) \?\? packageQueryViewport;[\s\S]*app\.innerHTML = renderPackageQueryView\(\{[\s\S]*viewport,[\s\S]*bindPackageQueryView\(document, packageQueryActions\);\s*restorePackageQueryViewport\(document, viewport\);\s*packageQueryViewport =\s*capturePackageQueryViewport\(document\) \?\? viewport;\s*restorePackageQueryFocus\(document, focus\)/);
  const streamPatch =
    appSource.match(/function patchPackageQueryPage\(\) \{[\s\S]*?\n}\n/)?.[0]
    ?? "";
  assert.match(
    streamPatch,
    /const viewport =\s*capturePackageQueryViewport\(document\) \?\? packageQueryViewport;[\s\S]*patchPackageQueryStream\(\s*document,[\s\S]*viewport,[\s\S]*packageQueryActions\)[\s\S]*restorePackageQueryViewport\(document, viewport\);\s*packageQueryViewport =\s*capturePackageQueryViewport\(document\) \?\? viewport/);
  assert.doesNotMatch(streamPatch, /app\.innerHTML/);
  assert.match(
    appSource,
    /const packageQueryRender =\s*createPackageQueryRenderScheduler\(\{[\s\S]*requestFrame: callback => requestAnimationFrame\(callback\),[\s\S]*compositionActive: \(\) =>\s*packageQueryEditorCompositionActive\(document\),[\s\S]*renderStream: patchPackageQueryPage,[\s\S]*renderFull: replacePackageQueryPage,[\s\S]*function schedulePackageQueryStreamRender\(\) \{\s*packageQueryRender\.scheduleStream\(\);/);
  const popstate =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /const leftPackageQueryHandoff = currentPackageQueryHandoff\(\);\s*const navigationSeq = navigationSequence\.begin\(\)/);
  assert.match(
    popstate,
    /const navigationSeq = navigationSequence\.begin\(\);\s*let leftPackageQueryForWorkspaceSuccessor = false;\s*let unavailableWorkspaceAdmissionRejected = false;\s*const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation\(\);\s*invalidateMemberDestinationWork\(state\);[\s\S]*retainedWorkspaceIdFromHistory\(history\.state\)[\s\S]*activateRetainedWorkspaceProjection\(historyWorkspaceId, false\)/);
  assert.match(
    appSource,
    /function dismissModalsForRoutedNavigation\(\) \{\s*closeGraphExplorerForNavigation\(\);\s*const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal\(false\);\s*state\.settings = false;\s*state\.keyboardHelp = false;\s*libraryOpenSequence\+\+;\s*state\.libraryOpen = false;\s*state\.libraryOpenBusy = false;\s*state\.libraryOpenError = "";\s*state\.explorer = null;\s*spotlight\.reset\(\);\s*sourceInspection\.clearGraphSource\(\);\s*documentInspection\.clear\(\);\s*return dismissedAnnotatedSourceModal/);
  assert.match(
    route,
    /dismissModalsForRoutedNavigation\(\);\s*supersedeRetainedLocationIntentForRoutedNavigation\(\);\s*navigationSequence\.begin\(\)/);
  assert.match(
    popstate,
    /if \(isPackageQueryPath\(location\.pathname\)\) \{[\s\S]*applyPackageQueryHistory\(history\.state\)/);
  assert.match(
    popstate,
    /state\.loading = !state\.engineReady;\s*render\(\);\s*if \(state\.engineReady\) focusPackageQueryInput\(\)/);
  assert.match(
    popstate,
    /if \(state\.packageQueryOpen \|\| leftPackageQueryHandoff\) \{[\s\S]*packageQueryHandoffNavigationSeq = null;[\s\S]*state\.packageQueryReturnFocusPending =\s*state\.packageQueryReturnFocus !== null[\s\S]*isPackageQueryPredecessor\(\s*history\.state,\s*state\.packageQueryPredecessorEntryId\)/);
  assert.match(
    popstate,
    /if \(!state\.engineReady\) \{\s*const pendingWorkspace = workspaceLocation\.preflightCurrent\(\);\s*const pendingLocation = pendingWorkspace\.visible;[\s\S]*state\.loading = !state\.home;[\s\S]*render\(\);\s*return;\s*\}\s*const loc = await parseLocation\(\)/);
  assert.match(
    popstate,
    /if \(leftPackageQueryForWorkspaceSuccessor\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq;\s*\}\s*if \(!state\.engineReady\)/);
  assert.match(
    appSource,
    /function closePackageQueryRoute\(\) \{\s*navigationSequence\.begin\(\);[\s\S]*history\.back\(\)/);
  assert.match(
    appSource,
    /function closePackageQueryRoute\(\) \{[\s\S]*state\.packageQueryReturnFocusPending =\s*state\.packageQueryReturnFocus !== null;[\s\S]*history\.back\(\)/);
  assert.match(
    appSource,
    /function openCredits\(\) \{[\s\S]*?navigationSequence\.begin\(\);\s*state\.loading = false;\s*discardPackageQueryTermEditors\(\);\s*state\.packageQueryOpen = false/);
  assert.match(
    appSource,
    /function restorePackageQueryReturnFocus\(\) \{\s*if \(!state\.packageQueryReturnFocusPending\) return;[\s\S]*state\.packageQueryReturnFocus === "application-query"[\s\S]*if \(state\.packageQueryReturnFocus !== "package-search"\) return;[\s\S]*focusWorkbenchSearch\(document\)[\s\S]*focusLevelOneHeading\(\)/);
  assert.match(
    appSource,
    /if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.diag = computeDiagnostics\([\s\S]*render\(\);\s*if \(!state\.packageQueryReturnFocusPending\) \{\s*afterCurrentNavigationFrame\(\(\) =>\s*focusWorkspaceOrHeading\(\)\)/);
  assert.match(
    appSource,
    /state\.packageQueryReturnFocus === "application-query"[\s\S]*data-application-scope="query"[\s\S]*focusRenderedElement\(queryScope\)[\s\S]*else if \(focusLevelOneHeading\(\)\)/);
  assert.match(
    appSource,
    /function afterCurrentNavigationFrame\(action: \(\) => void\) \{\s*const navigationSeq = navigationSequence\.current\(\);[\s\S]*if \(navigationSequence\.isCurrent\(navigationSeq\)\) action\(\)/);
  assert.match(
    appSource,
    /function focusTypeList\([\s\S]*afterCurrentNavigationFrame\(\(\) => \{[\s\S]*"#type-list"/);
  assert.match(
    appSource,
    /if \(!pushFromProductDemos\) \{\s*void workspaceLocation\.build\(snapshot\)\.then\(\s*publish,/);
  assert.equal(
    appSource.match(
      /\? withScopeQuery\(state\.packageQueryState\.request, validText\)/g)
      ?.length,
    1);
  assert.match(
    appSource,
    /function submitPackageQueryRequest\(request: QueryRequest\) \{\s*packageQueryLiveAnnouncer\.reset\(\);\s*if \(!shouldExecuteQuery\(request\)\) \{\s*packageQueryController\.configure\(request\);\s*return;\s*\}\s*void packageQueryController\.run\(request\)/);
  assert.match(
    appSource,
    /function resetPackageQueryState\(\) \{[\s\S]*state\.packageQueryState\.termDraft = fresh\.termDraft \?\? null;\s*state\.packageQueryState\.termEdits = fresh\.termEdits \?\? \[\]/);
  assert.match(
    appSource,
    /function goHome\(\) \{[\s\S]*discardPackageQueryTermEditors\(\);\s*state\.packageQueryOpen = false/);
  assert.match(
    appSource,
    /function runPackageQuery\(text: string\) \{\s*const request = preparePackageQueryRequest\(text\);\s*submitPackageQueryRequest\(request\)/);
  assert.doesNotMatch(appSource, /function discoverPackages\(/);
  assert.match(
    appSource,
    /function preparePackageQueryControlRequest\(\s*text: string,\s*\): QueryRequest \{\s*return preparePackageQueryRequest\(text\)/);
  assert.match(
    appSource,
    /state\.packageQueryCatalogError =\s*`Package-query vocabulary is unavailable/);
  assert.match(
    appSource,
    /try \{\s*const catalog =\s*packageQueryCatalog\(await engineClient\.package\.listPackageQueryCatalog\(\)\);\s*state\.packageQueryPresets = catalog\.presets;\s*state\.packageQueryTerms = catalog\.terms;\s*\} catch \(error\) \{[\s\S]*state\.packageQueryPresets = \[\];\s*state\.packageQueryTerms = \[\];\s*state\.packageQueryCatalogError =[\s\S]*\}/);
  assert.match(
    appSource,
    /function applyPackageQueryTerm\([\s\S]*\? withTerm\(current, descriptor, operator, value\)\s*: replaceTerm\(current, index, operator, value\);[\s\S]*state\.packageQueryState\.termDraft = null;[\s\S]*state\.packageQueryState\.termEdits = edits;[\s\S]*submitPackageQueryRequest\(request\)/);
  assert.match(
    appSource,
    /function editPackageQueryTerm\([\s\S]*state\.packageQueryState\.termDraft = \{[\s\S]*operator,[\s\S]*value,[\s\S]*state\.packageQueryState\.termEdits = edits/);
  assert.match(
    appSource,
    /function removePackageQueryTerm\([\s\S]*submitPackageQueryRequest\(withoutTerm\(current, index\)\)/);
  assert.doesNotMatch(
    appSource,
    /state\.packageQuerySourceCatalog/);
  assert.match(
    appSource,
    /navigationError: \[\s*state\.packageQueryCatalogError,\s*state\.packageQueryNavigationError/);
  assert.match(
    appSource,
    /const announcement = takePackageQueryAnnouncement\(\);[\s\S]*packageQueryLiveAnnouncer\.enqueue\(announcement\)/);
  assert.match(
    appSource,
    /createPackageQueryLiveAnnouncer\(\s*\(\) => document\.querySelector<HTMLElement>\("#package-query-announcement"\)\)/);
  assert.match(
    indexSource,
    /id="package-query-announcement"[\s\S]*class="query-announcement"[\s\S]*role="alert"[\s\S]*aria-live="assertive"[\s\S]*aria-atomic="true"/);
  assert.match(
    appSource,
    /renderApplicationScopeBar\(\s*activeScope === "workspace" \? "workspace" : null,\s*true,\s*escapeHtml\)/);
  assert.match(
    appSource,
    /openPackageQueryRoute\("", \{\s*preserveState: true,\s*returnFocus: "application-query"/);
  assert.match(
    appSource,
    /async function selectWorkspaceApplicationScope\(\) \{\s*const pkg = state\.package;[\s\S]*navigationSequence\.begin\(\);[\s\S]*const projected = await buildStateUrl\(\);[\s\S]*resolvePackageQueryWorkspaceSuccessor\(\s*\(\) => projected,[\s\S]*fallback\.hash = "workspace";[\s\S]*appendQueryNotice\([\s\S]*complete state could not be saved in the address bar[\s\S]*workspaceLocation\.push\(successor\.url\.toString\(\)\);\s*render\(\)/);
  assert.match(
    appSource,
    /onApplicationScopeSelect: applicationScope => \{[\s\S]*applicationScope === "query"[\s\S]*applicationScope === "activity"[\s\S]*openPackageActivityRoute\("application-activity"\)[\s\S]*else if \(scope\(\) !== "workspace"\) \{\s*observeAsync\(\s*selectWorkspaceApplicationScope\(\),\s*"Opening the Workspace scope"\)/);
  assert.match(
    appSource,
    /const focusWorkspaceAfterRoutedPage =\s*state\.packageQueryOpen \|\| state\.packageActivityOpen;\s*if \(focusWorkspaceAfterRoutedPage\) \{\s*discardPackageQueryTermEditors\(\);\s*state\.packageQueryOpen = false;\s*state\.packageActivityOpen = false;\s*packageQueryController\.cancel\(\);\s*packageChangesController\.cancel\("disposed"\);\s*state\.packageQueryNavigationError = "";\s*\}\s*const navigationSeq = navigationSequence\.begin\(\);\s*if \(focusWorkspaceAfterRoutedPage\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq;\s*\}\s*let loc: ParsedLocation;\s*try \{\s*loc = await parseWorkspaceHref\(url\.toString\(\)\);[\s\S]*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;[\s\S]*if \(!sameWorkspace\) \{[\s\S]*openFreshWorkspaceLink\(loc, navigationSeq\)[\s\S]*\} else \{\s*workspaceLocation\.push/);
  assert.match(
    appSource,
    /function restorePackageQueryWorkspaceFocus\(\) \{\s*const navigationSeq = packageQueryWorkspaceFocusNavigationSeq;[\s\S]*navigationSequence\.isCurrent\(navigationSeq\)[\s\S]*afterCurrentNavigationFrame\(\(\) => \{\s*if \(!focusLevelOneHeading\(\)\) \{\s*document\.querySelector<HTMLElement>\("#type-list"\)\?\.focus\(\)/);
  assert.match(
    appSource,
    /closest\("\[data-scope-bar\], \[data-application-scope-strip\]"\)[\s\S]*bindEvents\(\);[\s\S]*if \(scopeBarOwnsFocus\) \{\s*let restored = false;\s*if \(scopeBarFocus\) \{\s*scopeBarBinding\?\.revealFocusTarget\(scopeBarFocus\);\s*restored = restoreScopeBarFocus\(document, scopeBarFocus\);\s*\}\s*if \(!restored\) \{\s*document\.querySelector<HTMLElement>\("\.brand"\)\s*\?\.focus\(\{ preventScroll: true \}\);\s*\}\s*app\.removeAttribute\("tabindex"\);\s*\}\s*restorePackageRouteReturnFocus\(\)/);
  assert.match(
    appSource,
    /if \(state\.workspaceSubjectOpen && isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*renderProductDemosPage\(\);[\s\S]*restorePackageRouteReturnFocus\(\);\s*return;/);
  assert.match(
    appSource,
    /if \(scope\(\) === "platform"\) \{[\s\S]*renderPlatformView\(\);[\s\S]*restorePackageRouteReturnFocus\(\);[\s\S]*return;/);
  assert.match(
    appSource,
    /function restorePackageRouteReturnFocus\(\) \{\s*if \(pendingWorkspaceConstruction !== null\) return;\s*restorePackageQueryReturnFocus\(\);\s*restorePackageActivityReturnFocus\(\);\s*restorePackageQueryWorkspaceFocus\(\)/);
  assert.match(
    popstate,
    /leftPackageQueryForWorkspaceSuccessor =\s*!state\.packageQueryReturnFocusPending;[\s\S]*if \(leftPackageQueryForWorkspaceSuccessor\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq/);
  assert.match(
    appSource,
    /url => workspaceLocation\.replace\(url, history\.state\)/);
});

test("browser history reuses available identities and publishes only unavailable ones", () => {
  const history =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const restore =
    appSource.match(/async function restoreFreshWorkspaceFromHistory\([\s\S]*?\n}/)?.[0]
    ?? "";
  const restoreRetained =
    appSource.match(/async function restoreRetainedWorkspaceFromHistory\([\s\S]*?\n}/)?.[0]
    ?? "";

  assert.match(
    history,
    /historyWorkspaceAvailable = historyWorkspaceId !== null[\s\S]*activateRetainedWorkspaceProjection\(historyWorkspaceId, false\)/);
  assert.match(
    history,
    /managedHistoryWorkspaceAvailable[\s\S]*selectBrowserEntry\(\{[\s\S]*retainedDefinitionId: historyWorkspaceId[\s\S]*activate\(\s*historyWorkspaceId,[\s\S]*installRetainedWorkspacePosting\([\s\S]*posting\.canonicalLocation === location\.href \? "exact" : "changed"/);
  assert.match(
    history,
    /let restoredActiveManagedWorkspace = false;[\s\S]*activeDefinitionId\s*=== historyWorkspaceId[\s\S]*restoredActiveManagedWorkspace = true;[\s\S]*if \(!restoredActiveManagedWorkspace\) return;[\s\S]*if \(isPackageQueryPath\(location\.pathname\)\)[\s\S]*if \(state\.packageQueryOpen \|\| leftPackageQueryHandoff\)[\s\S]*if \(restoredActiveManagedWorkspace\s*&& activeRetainedWorkspacePosting\?\.canonicalLocation === location\.href\) \{\s*state\.credits = false;\s*state\.home = false;\s*render\(\{ synchronizeUrl: false \}\);\s*return;\s*\}[\s\S]*const loc = await parseLocation\(\)/);
  assert.match(
    history,
    /const unavailableGlobalWorkspace =\s*historyWorkspaceReferenced\s*&& !managedHistoryWorkspaceAvailable\s*&& !historyWorkspaceAvailable/);
  assert.match(
    history,
    /if \(bareHome\) \{\s*if \(historyWorkspaceReferenced\s*&& !managedHistoryWorkspaceAvailable\s*&& !historyWorkspaceAvailable\)/);
  assert.match(
    appSource,
    /function supersedeRetainedLocationIntentForRoutedNavigation\(\): void \{\s*if \(retainedLocationIntents\.currentIntentId === null\) return;[\s\S]*admitNonBrowser\(\s*"none",[\s\S]*if \(!retainedLocationIntents\.publish\(effect, history\)\)/);
  assert.match(
    appSource,
    /function goHome\(\)[\s\S]*supersedeRetainedLocationIntentForRoutedNavigation\(\)[\s\S]*workspaceLocation\.push\("\/"\)/);
  assert.match(
    appSource,
    /function openCredits\(\)[\s\S]*supersedeRetainedLocationIntentForRoutedNavigation\(\)[\s\S]*workspaceLocation\.push\("\/credits"\)/);
  assert.match(
    history,
    /historyWorkspaceAvailable[\s\S]*activeDefinitionId !== null[\s\S]*activateCompatibilityRetainedWorkspace\(historyWorkspaceId, \{[\s\S]*declaration: locationIntent,[\s\S]*restoration:/);
  assert.match(
    history,
    /issuedManagedRetainedDefinitionIds\.has\(historyWorkspaceId\)[\s\S]*realignRetainedLocationIntent\(locationIntent, "unavailable"\)/);
  assert.match(
    history,
    /const restoreHistoryWorkspace = \(\) => historyWorkspaceAvailable\s*\? restoreRetainedWorkspaceFromHistory\(loc, navigationSeq\)\s*: historyWorkspaceReferenced\s*\|\| retainedWorkspaces\.activeWorkspaceId === null\s*\? restoreFreshWorkspaceFromHistory\(loc, navigationSeq\)\s*: restoreRetainedWorkspaceFromHistory\(loc, navigationSeq\)[\s\S]*!workspaceCoordinatesMatch\(state\.packages, loc\.tabs\)[\s\S]*restoreHistoryWorkspace\(\)/);
  assert.match(
    history,
    /const target = loc\.package\s*\? state\.packages\.find\(candidate =>\s*packageCoordinateMatchesLocation\(candidate, loc\)\)\s*: null;\s*if \(loc\.tabs\?\.length && !target\) \{[\s\S]*restoreHistoryWorkspace\(\)[\s\S]*\}\s*if \(target\) \{\s*activatePackage\(target, \{ resetAccessibility: true \}\)/);
  assert.match(
    restore,
    /captureWorkspaceConstructionSnapshots\(navigationSeq\)[\s\S]*prepareUnpublishedWorkspace\(\)[\s\S]*restoreWorkspaceFromLocation\([\s\S]*false,\s*fail,\s*false\)[\s\S]*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*publishCurrentWorkspace\(construction\.retainedSnapshot\)[\s\S]*workspaceLocation\.replace\(destination, history\.state\)/);
  assert.match(
    restoreRetained,
    /captureWorkspaceMutationSnapshot\(navigationSeq\)[\s\S]*discardPendingWorkspaceConstruction\(\);\s*failCanonicalWorkspaceRestore\([\s\S]*rollbackSnapshot,[\s\S]*restoreWorkspaceFromLocation\([\s\S]*false,\s*fail,\s*false\)[\s\S]*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*discardPendingWorkspaceConstruction\(\);\s*activeWorkspaceUrl = destination;\s*workspaceLocation\.replace\(destination, history\.state\)/);
  assert.match(
    appSource,
    /hasWorkspace:\s*state\.package !== null\s*\|\| state\.platformSelection !== null\s*\|\| retainedWorkspaces\.activeWorkspaceId !== null/);
  assert.match(
    appSource,
    /const retainedState: AppState = \{\s*\.\.\.cloned,\s*platformIndex,\s*retryAction: null,\s*queryNoticeRetryAction: null,\s*\}/);
  assert.match(
    appSource,
    /retainedWorkspaceHistorySessionId = crypto\.randomUUID\(\)[\s\S]*historyState\[retainedWorkspaceHistorySessionKey\]\s*!== retainedWorkspaceHistorySessionId\) return null;[\s\S]*function historyReferencesRetainedWorkspace\(historyState: unknown\): boolean \{\s*return retainedWorkspaceIdFromHistory\(historyState\) !== null;/);
  assert.match(
    appSource,
    /function rebindActiveWorkspaceHistory\(\): void \{\s*workspaceLocation\.replace\(\s*activeWorkspaceUrl \?\? \(state\.package \|\| state\.platformSelection \? location\.href : "\/demos"\),\s*history\.state\)/);
  assert.match(
    appSource,
    /function syncUrl\(\) \{\s*if \(activeRetainedWorkspacePosting !== null\) return;/);
  assert.match(
    appSource,
    /function finishPackageRemoval\([\s\S]*if \(!state\.package && !state\.platformSelection\) \{\s*activeWorkspaceUrl = "\/demos";\s*if \(!state\.home\) \{\s*state\.workspaceSubjectOpen = true;\s*workspaceLocation\.replace\("\/demos", history\.state\)/);
  assert.match(
    appSource,
    /if \(snapshot\.state\.packages\.length === 0\s*&& !snapshot\.state\.platformSelection\s*&& !snapshot\.state\.uploadedLibrary\) \{\s*snapshot\.state\.workspaceSubjectOpen = true;\s*snapshot\.state\.atPackageRoot = true;\s*snapshot\.state\.atLibraryRoot = false;/);
  assert.match(
    appSource,
    /function restoreCanonicalWorkspaceRestoreSnapshot\([\s\S]*const memberCallGraphSeq = state\.memberCallGraphSeq;[\s\S]*const platformIndex = state\.platformIndex \?\? snapshot\.state\.platformIndex;\s*clearWorkspaceOccurrenceView\(\);[\s\S]*Object\.assign\(state, snapshot\.state\);[\s\S]*state\.memberCallGraphSeq =\s*Math\.max\(memberCallGraphSeq, snapshot\.state\.memberCallGraphSeq\) \+ 1;[\s\S]*state\.platformIndex = platformIndex;/);
  assert.match(
    appSource,
    /function cloneCanonicalWorkspaceSnapshotForRetention\([\s\S]*const platformIndex = snapshot\.state\.platformIndex;\s*const cloned = structuredClone\(\{\s*\.\.\.snapshot\.state,\s*platformIndex: null,[\s\S]*const retainedState: AppState = \{\s*\.\.\.cloned,\s*platformIndex,/);
  assert.match(
    appSource,
    /function retainedWorkspaceItems\(\) \{\s*const publishedActiveSnapshot = pendingWorkspaceConstruction\s*\? pendingWorkspaceConstruction\.retainedSnapshot\s*\?\? pendingWorkspaceConstruction\.supersessionSnapshot[\s\S]*const workspaceState = workspace\.id === retainedWorkspaces\.activeWorkspaceId\s*\? publishedActiveSnapshot\?\.state \?\? state\s*: workspace\.snapshot\?\.state;[\s\S]*packageCount: workspaceState[\s\S]*workspaceState\.packages\.filter\(pkg => pkg\.source\.kind !== "platform"\)\.length\s*\+ \(workspaceState\.platformSelection \? 1 : 0\)/);
  assert.match(
    appSource,
    /function workspaceKeyboardContextIsActive\(\): boolean \{\s*return pendingWorkspaceConstruction === null/);
  assert.match(
    appSource,
    /function invalidateWorkspaceAsyncOwners\(\): void \{\s*memberDetailInspection\.invalidate\(\);/);
  assert.match(
    appSource,
    /const workspaceModalContextIsAvailable = \(\) =>\s*pendingWorkspaceConstruction === null/);
  assert.match(
    appSource,
    /function restoreRetainedWorkspaceSnapshot\([\s\S]*activeWorkspaceUrl = snapshot\.url;\s*if \(restoreUrl\) \{\s*workspaceLocation\.replace\(\s*snapshot\.url,\s*withPlatformRootParentHistory\(\s*history\.state,\s*navigationSnapshotHasPlatformRootParent\(snapshot\.navigation\)\)\)[\s\S]*function selectRetainedWorkspace\(workspaceId: string\)[\s\S]*activateRetainedWorkspaceProjection\(workspaceId, false\)[\s\S]*workspaceLocation\.push\(\s*activeWorkspaceUrl \?\? "\/demos",\s*withPlatformRootParentHistory\(\s*history\.state,\s*navigationSnapshotHasPlatformRootParent\(\s*navigationHistory\.snapshot\(\)\)\)\)[\s\S]*function deleteRetainedWorkspace\(workspaceId: string\)[\s\S]*restoreRetainedWorkspaceSnapshot\(transition\.activatedSnapshot\)/);
});

test("managed Saved Open keeps compact rows, packet fidelity, and focus ownership", () => {
  const post = appSource.match(
    /function postRetainedWorkspace\([\s\S]*?\n}\n\nfunction clearRetainedWorkspacePosting/,
  )?.[0] ?? "";
  const install = appSource.match(
    /async function installRetainedWorkspacePosting\([\s\S]*?\n}\n\nasync function activateRetainedPackageAction/,
  )?.[0] ?? "";
  const packageAction = appSource.match(
    /async function activateRetainedPackageAction\([\s\S]*?\n}\n\nasync function activateRetainedPlatformAction/,
  )?.[0] ?? "";
  const platformAction = appSource.match(
    /async function activateRetainedPlatformAction\([\s\S]*?\n}\n\nfunction retainedWorkspaceItems/,
  )?.[0] ?? "";
  const capture = appSource.match(
    /async function captureSavedWorkspacePacket\(\)[\s\S]*?\n}\n\nasync function buildStateUrl/,
  )?.[0] ?? "";
  const renderDispatch = appSource.match(
    /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?const pkg = state\.package;/,
  )?.[0] ?? "";
  const workspaceView = appSource.match(
    /function renderWorkspaceView\(\)[\s\S]*?\n}\n\nfunction packageLensBody/,
  )?.[0] ?? "";

  assert.match(
    post,
    /presentationCurrent = true[\s\S]*if \(presentationCurrent\) \{[\s\S]*captureWorkspaceFocus\(focusedElement\)[\s\S]*focusApplicationMenuButton\(document\)[\s\S]*activeRetainedWorkspacePosting = posting;\s*retainedWorkspaceInitialDetailAuthority = \{[\s\S]*navigationSeq: navigationSequence\.current\(\),\s*presentationCurrent,[\s\S]*createNavigationDescriptorPresentation\(posting\);\s*if \(!presentationCurrent\) return;[\s\S]*render\(\{ synchronizeUrl: false \}\)/);
  assert.match(
    install,
    /const detailAuthority = retainedWorkspaceInitialDetailAuthority;[\s\S]*detailAuthority\?\.realizationId !== posting\.realizationId[\s\S]*const detailNavigationSeq = detailAuthority\.navigationSeq;\s*const admitInitialDetail = detailAuthority\.presentationCurrent\s*&& navigationSequence\.isCurrent\(detailNavigationSeq\);[\s\S]*if \(admitInitialDetail\) \{[\s\S]*admitRetainedPackage[\s\S]*admitRetainedPlatform[\s\S]*if \(admitInitialDetail\s*&& navigationSequence\.isCurrent\(detailNavigationSeq\)\)/);
  assert.doesNotMatch(install, /Promise\.all/);
  assert.match(
    install,
    /posting\.packages\.find\([\s\S]*navigationId === activeTabId[\s\S]*posting\.platforms\.find\([\s\S]*navigationId === activeTabId/);
  assert.match(
    install,
    /try \{[\s\S]*admitRetainedPackage[\s\S]*admitRetainedPlatform[\s\S]*catch \(error\) \{[\s\S]*detailFailure = errorMessage\(error\)[\s\S]*state\.packages = \[[\s\S]*packageModel[\s\S]*platformModel/);
  assert.match(
    install,
    /withNavigationPackageDetailFailure\([\s\S]*withNavigationPlatformDetailFailure\(/);
  assert.match(
    install,
    /browserRestoration[\s\S]*render\(\{ synchronizeUrl: false \}\);/);
  assert.match(
    install,
    /effect\.kind === "none" && effect\.reason === "stale"[\s\S]*installedRetainedLocation = association;\s*return;/);
  assert.match(
    appSource,
    /controller\.activate\(\s*definition\.id,\s*\(\) => retainedLocationIntents\.currentIntentId === locationIntent\.id,\s*posting => installRetainedWorkspacePosting\(posting, locationIntent\),\s*undefined,\s*undefined,\s*posting => retainedLocationPresentationCurrent\(\s*locationIntent,\s*posting\.canonicalLocation,\s*\),\s*\)/);
  assert.match(
    appSource,
    /function retainedLocationPresentationCurrent\([\s\S]*retainedLocationIntents\.currentIntentId === intent\.id[\s\S]*retainedLocationIntents\.currentIntentId === null[\s\S]*installedRetainedLocation\?\.canonicalLocation === canonicalLocation[\s\S]*location\.href === canonicalLocation/);
  assert.match(
    appSource,
    /function completeRetainedActivationPresentation\([\s\S]*navigationSeq: number[\s\S]*result\.posting === null[\s\S]*!navigationSequence\.isCurrent\(navigationSeq\)[\s\S]*retainedLocationPresentationCurrent\(\s*locationIntent,\s*result\.posting\.canonicalLocation,[\s\S]*render\(\{ synchronizeUrl: false \}\);\s*afterCurrentNavigationFrame\(focusWorkspaceOrHeading\)/);
  assert.match(
    appSource,
    /async function openSavedWorkspaceEntry\([\s\S]*const activationNavigationSeq = navigationSequence\.begin\(\)[\s\S]*result\.status === "activated" \|\| result\.status === "noEffect"[\s\S]*completeRetainedActivationPresentation\(\s*result,\s*locationIntent,\s*activationNavigationSeq,\s*\)/);
  assert.match(
    appSource,
    /async function activateManagedRetainedWorkspace\([\s\S]*const activationNavigationSeq = navigationSequence\.current\(\)[\s\S]*result\.status === "activated" \|\| result\.status === "noEffect"[\s\S]*completeRetainedActivationPresentation\(\s*result,\s*locationIntent,\s*activationNavigationSeq,\s*\)/);
  assert.match(
    packageAction,
    /catch \(error\) \{[\s\S]*withNavigationPackageDetailFailure\([\s\S]*render\(\{ synchronizeUrl: false \}\);[\s\S]*return;/);
  assert.match(
    platformAction,
    /catch \(error\) \{[\s\S]*withNavigationPlatformDetailFailure\([\s\S]*render\(\{ synchronizeUrl: false \}\);[\s\S]*return;/);
  assert.match(
    renderDispatch,
    /retainedWorkspacePostingVisible[\s\S]*!retainedWorkspacePostingVisible[\s\S]*workspaceCatalogVisible/);
  assert.match(
    workspaceView,
    /retainedWorkspacePresentation\s*\? \{[\s\S]*navigationPackages:[\s\S]*navigationPlatforms:[\s\S]*\}\s*: \{\s*canAddPackage:/);
  assert.match(
    capture,
    /if \(activeRetainedWorkspacePosting !== null\) \{\s*return activeRetainedWorkspacePosting\.canonicalPacket;\s*\}/);
});

test("same-origin links retain different-coordinate Workspaces", () => {
  const navigate =
    appSource.match(/async function navigateInAppUrl\(url: URL\) \{[\s\S]*?\n}\n\nbindWorkspaceLinkNavigation/)?.[0]
    ?? "";
  const navigateCurrent =
    appSource.match(/async function navigateWithinCurrentWorkspace\([\s\S]*?\n}\n\nasync function openFreshWorkspaceLink/)?.[0]
    ?? "";
  const openFresh =
    appSource.match(/async function openFreshWorkspaceLink\([\s\S]*?\n}\n\nasync function navigateInAppUrl/)?.[0]
    ?? "";

  assert.match(
    navigate,
    /if \(loc\.hasWorkspaceState && !loc\.shareState\) \{[\s\S]*Workspace restore failed:[\s\S]*render\(\{ synchronizeUrl: false \}\);\s*return;\s*\}[\s\S]*const tablessTarget = !loc\.tabs\.length && loc\.package\s*\? state\.packages\.find\(candidate =>\s*packageCoordinateMatchesLocation\(candidate, loc\)\)\s*: undefined;\s*const sameWorkspace = loc\.tabs\.length\s*\? workspaceCoordinatesMatch\(state\.packages, loc\.tabs\)\s*: tablessTarget !== undefined/);
  assert.match(
    navigate,
    /if \(!sameWorkspace\) \{\s*observeAsync\(\s*openFreshWorkspaceLink\(loc, navigationSeq\),\s*"Opening workspace link"\);\s*\} else \{\s*workspaceLocation\.push\(url\.toString\(\)\);\s*observeAsync\(\s*navigateWithinCurrentWorkspace\(loc, navigationSeq\),\s*"Navigating"\)/);
  assert.doesNotMatch(navigateCurrent, /clearWorkspacePackages|prepareUnpublishedWorkspace/);
  assert.match(
    navigateCurrent,
    /state\.packages\.find\(candidate =>\s*packageCoordinateMatchesLocation\(candidate, loc\)\)[\s\S]*activatePackage\(target, \{ resetAccessibility: true \}\)[\s\S]*applyDeepLink\(loc\)/);
  assert.match(
    openFresh,
    /captureWorkspaceConstructionSnapshots\(navigationSeq\)[\s\S]*prepareUnpublishedWorkspace\(\)[\s\S]*restoreWorkspaceFromLocation\([\s\S]*false,\s*fail,\s*false\)[\s\S]*const destination = \(await buildStateUrl\(\)\)\.toString\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*publishCurrentWorkspace\(construction\.retainedSnapshot\);\s*workspaceLocation\.push\(destination\)/);
  assert.doesNotMatch(
    navigate.match(/const loc = parseWorkspaceHref[\s\S]*?if \(!sameWorkspace\)/)?.[0] ?? "",
    /workspaceLocation\.push/);
  assert.match(
    appSource,
    /function publishFreshEmptyWorkspaceFromHistory\([\s\S]*if \(!canPublishRetainedWorkspace\(\)\) \{[\s\S]*history\.replaceState\(history\.state, "", destination\);[\s\S]*return false/);
  assert.match(
    appSource,
    /let unavailableWorkspaceAdmissionRejected = false;[\s\S]*unavailableWorkspaceAdmissionRejected =\s*!publishFreshEmptyWorkspaceFromHistory\(location\.href\)[\s\S]*render\(\{\s*synchronizeUrl: !unavailableWorkspaceAdmissionRejected,\s*\}\)/);
});

test("authoritative location restore clears filters and applies aggregate Platform scope", () => {
  assert.match(
    appSource,
    /function resetLocationFilters\(\) \{\s*state\.typeFilter = "";\s*state\.namespaceFilter = "";\s*state\.kindFilter = "";\s*state\.libraryScope = null;\s*state\.typeCursor = 0;\s*resetMemberFilters\(\)/);
  const workspaceRestore =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failWorkspaceRoute/)?.[0]
    ?? "";
  assert.match(workspaceRestore, /resetLocationFilters\(\);\s*clearWorkspacePackages\(\)/);
  assert.match(
    workspaceRestore,
    /if \(targetModel\.source\.kind === "platform"\) \{\s*const scoped = await applyPlatformLibraryScope\(\s*loc\.library/);
  const popstate =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(popstate, /if \(bareHome\)[\s\S]*resetLocationFilters\(\);\s*const deep = loc/);
  assert.match(
    popstate,
    /isRuntimePackId\(loc\.package\)[\s\S]*restoreHistoryWorkspace\(\)/);
});

test("type projection completions render only while current and preserve navigation focus", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  const typeSourceAuthority =
    sourceInspectionSource.match(
      /const publishTypeSourceEvent = \(event: TypeSourceFeatureEvent\)[\s\S]*?const typeSourceSession:/,
    )?.[0]
    ?? "";
  assert.match(
    typeSourceAuthority,
    /case "started":[\s\S]*case "replaced":[\s\S]*state\.typeSource = \{[\s\S]*status: "loading"[\s\S]*context\.preservedFocus =\s*dependencies\.renderPreservingMemberFocus\(\);[\s\S]*case "terminal":[\s\S]*state\.typeSource = event\.outcome\.kind === "succeeded"[\s\S]*status: "ready"[\s\S]*status: "failed"[\s\S]*if \(context\.request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(\s*context\.preservedFocus,/);
  assert.match(
    typeSource,
    /typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.doesNotMatch(typeSource, /sourceRequestGeneration|ownsRequest/);
  assert.doesNotMatch(typeSource, /finally \{[\s\S]*dependencies\.render\(\)/);
  const typeMetadata =
    appSource.match(/async function loadSelectedTypeMetadata\([\s\S]*?\n}\n\n\/\/ Projects/)?.[0]
    ?? "";
  assert.match(
    typeMetadata,
    /return metadataInspection\.loadTypeMetadata\(\{[\s\S]*packageId: pkg\.id,[\s\S]*assembly: type\.assembly,[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*typeIdentity: type\.definitionId \?\? type\.id,[\s\S]*isVisible: \(\) => \{[\s\S]*!state\.home[\s\S]*!state\.settings[\s\S]*!state\.explorer\?\.open[\s\S]*!state\.loading[\s\S]*!state\.error[\s\S]*!workbenchOverlayOwnsFocus\(\)[\s\S]*selectedTypeMetadataLibraryIdentity\(\)\) === signature/);
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
  const focus =
    appSource.match(/function applyExplorerFocus\(\)[\s\S]*?\n}\n\n\/\/ Center/)?.[0]
    ?? "";
  const pageSizer =
    appSource.match(/function syncExplorerPageSize\(\)[\s\S]*?\n}\n\n\/\/ Renders/)?.[0]
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
    /const ownsRequest = \(\) =>[\s\S]*state\.explorer === explorer[\s\S]*requests\.get\(index\) === requestSequence[\s\S]*dependencies\.queryPlatformTable[\s\S]*dependencies\.queryPackageTable[\s\S]*if \(!ownsRequest\(\)\) return;[\s\S]*index === explorer\.focusIndex && !explorer\.focusHeap/);
  assert.match(
    metadataInspectionSource,
    /dependencies\.queryPlatformHeap[\s\S]*dependencies\.queryPackageHeap[\s\S]*state\.explorer !== explorer[\s\S]*explorer\.focusHeap === heapName/);
  assert.match(
    focus,
    /const heapWindow = ex\.heapWindows\[entry\.heap\];\s*if \(!heapWindow \|\| \(!heapWindow\.loading && !heapWindow\.data\)\)\s*observeAsync\(loadExplorerHeap\(entry\.heap\), "Loading metadata heap rows"\);\s*else render\(\)/);
  assert.match(
    focus,
    /const onScreen = win && !win\.loading && win\.data &&/);
  assert.match(
    pageSizer,
    /if \(win\?\.data && !win\.loading[\s\S]*loadExplorerWindow\(ex\.focusIndex, win\.data\.startRowId, fit\)/);
  assert.match(
    appSource,
    /state\.explorer\.focusHeap && !state\.explorer\.heapWindows\[state\.explorer\.focusHeap\]/);
});

test("call graph request coordination stays outside the composition root", () => {
  const loader =
    appSource.match(/async function loadSelectedMemberCallGraph\([\s\S]*?\n}\n\n\/\/ Update just/)?.[0]
    ?? "";
  assert.match(
    loader,
    /return callGraphInspection\.load\(\{[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*typeIdentity: type\.definitionId \?\? type\.id,[\s\S]*platformType:\s*type\.definitionId \?\? type\.metadataId \?\? type\.queryId \?\? type\.id,[\s\S]*platformPack:\s*platformPackForAssembly\(type\.assembly, type\.platformPack\) \?\? "",[\s\S]*isCurrent: \(\) => memberRequestIsCurrent\(signature, true\)/);
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
    /function memberGroups\([\s\S]*type: AppTypeSurface \| null \| undefined,[\s\S]*for \(const member of type\?\.api \?\? \[\]\)/);
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
    /const capacityError = view\.platform\s*\? platformCoordinateCapacityError\(\)\s*:\s*"";\s*if \(capacityError\) \{\s*showToast\(capacityError\);\s*return false/);
  assert.match(
    applyView,
    /if \(view\.rootKind !== "platform" && view\.platform\) \{[\s\S]*state\.platformIndex\?\.target\([\s\S]*if \(!target\) return false;\s*retainPlatformPackageForTarget\(target\);/);
  assert.match(
    applyView,
    /const memberHistory = restoreMemberHistoryState\(\s*view,\s*type,\s*member/);
  assert.match(
    applyView,
    /state\.selectedTypeId = type\?\.id \?\? defaultVisibleTypeId\(pkg\);[\s\S]*state\.selectedMemberKey = memberHistory\.selectedMemberKey;[\s\S]*state\.memberBrowseTypeId = memberHistory\.memberBrowseTypeId;[\s\S]*state\.memberKindFilter = memberHistory\.memberKindFilter;[\s\S]*state\.memberAccessibilityFilter = memberHistory\.memberAccessibilityFilter;[\s\S]*state\.memberTraitFilter = memberHistory\.memberTraitFilter;[\s\S]*state\.memberTextFilter = memberHistory\.memberTextFilter/);
  assert.match(
    applyView,
    /state\.selectedOverloadIndex = memberHistory\.selectedOverloadIndex;[\s\S]*state\.memberSection = memberHistory\.memberSection;[\s\S]*state\.selectedBodyTarget = memberHistory\.selectedBodyTarget/);
  assert.match(
    applyView,
    /navigationHistory\.normalizeCurrent\(\);[\s\S]*loadSelectedMemberSource\(\)[\s\S]*else \{\s*render\(\)/);
  assert.match(
    appSource,
    /const navigationHistory = createNavigationHistory\(\{\s*capture: captureView,\s*signature: workspaceViewSignature,\s*apply: applyView/);
  assert.match(
    workspaceNavigationSource,
    /function workspaceViewSignature\([\s\S]*b: graphTarget \? null : encodeBodyTarget\(view\.bodyTarget\),[\s\S]*g: graphTarget/);
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
    /const scopeOnly = options\.scopeOnly === true;[\s\S]*platformLibraryMatchesDescriptor\(row, item\)[\s\S]*state\.libraryScope = new Set\(\[library\.id\]\);[\s\S]*if \(scopeOnly\) return pkg;[\s\S]*render\(\);[\s\S]*await loadSelectionData\(\)/);
  const applyScope =
    appSource.match(/async function applyPlatformLibraryScope\([\s\S]*?\n}\n\nobserveAsync\(bootstrap\(\)/)?.[0]
    ?? "";
  assert.match(applyScope, /scopeOnly: true/);
});

test("Type Source completion settles behind workbench overlays", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  const typeSourceAuthority =
    sourceInspectionSource.match(
      /const publishTypeSourceEvent = \(event: TypeSourceFeatureEvent\)[\s\S]*?const typeSourceSession:/,
    )?.[0]
    ?? "";
  assert.match(
    appSource,
    /function workbenchOverlayOwnsFocus\(\) \{\s*return workbenchModalOwnsFocus\(\);[\s\S]*function workbenchModalOwnsFocus\(\) \{\s*return state\.libraryOpen\s*\|\| state\.spotlightOpen\s*\|\| graphSourceIsOpen\(state\.graphSource\)\s*\|\| documentViewerIsOpen\(state\.docViewer\)\s*\|\| state\.memberAnnotatedModal !== null\s*\|\| graphExplorer\.isOpen;/);
  assert.match(
    appSource,
    /sourceInspection\.loadTypeSource\(\{[\s\S]*isVisible: \(\) =>\s*currentSourceOperationKind\(\) === "type"\s*&& !workbenchModalOwnsFocus\(\)/);
  assert.match(
    typeSourceAuthority,
    /case "terminal":[\s\S]*state\.typeSource = event\.outcome\.kind === "succeeded"[\s\S]*if \(context\.request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(\s*context\.preservedFocus,/);
  assert.match(
    typeSource,
    /typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.match(
    appSource,
    /function isInteractiveElement\(element: Element \| null\)[\s\S]*"button, a\[href\], input, select, textarea, summary, "[\s\S]*\[role=button\][\s\S]*id: "workspace\.drill-in"[\s\S]*key: "Enter"[\s\S]*!isInteractiveElement\([\s\S]*event\.target instanceof Element \? event\.target : null\)/);
});

test("member-less Metadata omits the empty composition call to action", () => {
  const composition =
    appSource.match(
      /function renderMemberComposition\(type: AppTypeSurface\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    composition,
    /if \(!kinds && !accessibilities && !traits\) return "";/);
});

test("Metadata composition excludes graph-projected implementation members", () => {
  const composition =
    appSource.match(
      /function renderMemberComposition\(type: AppTypeSurface\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    composition,
    /const \{ publicMembers \} = partitionGraphMembers\(type\.api\);/);
  assert.match(
    composition,
    /memberKinds\(publicSurface\)/);
  assert.match(
    composition,
    /memberAccessibilities\(publicSurface\)/);
  assert.match(
    composition,
    /availableMemberTraits\(publicSurface\)/);
});

test("settings keep a viewport-bounded scroll region", () => {
  const settingsDialogRule =
    stylesSource.match(/\.application-dialog\s*\{([^}]*)\}/s)?.[1] ?? "";
  const settingsMainRule =
    stylesSource.match(/\.settings-main\s*\{([^}]*)\}/s)?.[1] ?? "";
  assert.match(
    settingsDialogRule,
    /(?:^|\n)\s*max-height: min\(760px, calc\(100vh - 24px\)\);/);
  assert.match(
    settingsDialogRule,
    /(?:^|\n)\s*grid-template-rows: auto minmax\(0, 1fr\);/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*min-height: 0;/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*overflow-y: auto;/);
});

test("home and workbench keep viewport-bounded content above the data bar", () => {
  const workbenchRule =
    stylesSource.match(/\.workbench\s*\{([^}]*)\}/s)?.[1] ?? "";
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
    workbenchRule,
    /grid-template-rows: 42px 46px auto minmax\(0, 1fr\) 30px;/);
  assert.match(
    homeRule,
    /(?:^|\n)\s*grid-template-rows: auto auto minmax\(0, 1fr\) 30px;/);
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
    /\.workbench > \.notice-stack\s*\{\s*grid-row: 3;\s*\}[\s\S]*\.workbench > \.workspace\s*\{\s*grid-row: 4;\s*\}[\s\S]*\.workbench > \.data-bar\s*\{\s*grid-row: 5;\s*\}/);
  assert.match(
    stylesSource,
    /\.home > \.data-bar\s*\{\s*grid-row: 4;\s*\}/);
});

test("all dependency navigation paths use one product-owned coordinate matcher", () => {
  assert.equal(
    [...applicationSources.matchAll(/uniqueCompatiblePackage\(/g)].length,
    6);
  assert.match(
    generatedFacadeSource("inspect-web-package"),
    /\$requireManagedExports\(\)\["DotnetInspect"\]\["Web"\]\["Interop"\]\["Package"\]\["PackageExports"\]\["MatchPackageDependencyCoordinate\.-?\d+"\]/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\([\s\S]*?JSON\.stringify\(dependencyCoordinateCandidates\(packages\)\)/);
  assert.doesNotMatch(
    generatedFacadeSourceText,
    /PackageVersionSatisfiesDependencyRange/);
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
    /const nodeInfoById = new Map<string, DependencyGraphNodeInfo>\(\);[\s\S]*for \(const key of keys\)[\s\S]*const navigationInfo: DependencyGraphNodeInfo = \{ kind: info\.kind \};[\s\S]*nodeInfoById\.set\(id, navigationInfo\)/);
  assert.match(
    graphInteractionsSource,
    /const dataId = node\.getAttribute\("data-id"\);[\s\S]*return dataId \|\| idMatch\?\.\[1\] \|\| ""/);
  assert.match(
    appSource,
    /resolveDependencyGraphNode: nodeId => \{[\s\S]*built\.nodeInfoById\.get\(nodeId\)/);
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
    /packageIdentityKey\(await uniqueCompatiblePackage\(\s+model\.packages,\s+dependency\.id,\s+dependency\.versionRange\)\) === target\.packageKey/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\(\s+packageId,\s+declaredRange \?\? null,\s+dependencyCoordinateCandidates\(packages\)\)/);
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

test("spotlight cache signature changes when an equal-count surface is replaced", () => {
  const packageModel = {
    ...packageAt("1.0.0", "net8.0", 1),
    surfaceRevision: 0,
  };
  const original = spotlightCandidateSignature(packageModel, [packageModel]);
  packageModel.surfaceRevision++;

  assert.notEqual(
    original,
    spotlightCandidateSignature(packageModel, [packageModel]));
});
