import {
  accessibilityFilterIncludingType,
  activeSourceOperationKind,
  assemblyDescriptorForType,
  assertNever,
  callGraphAssemblyIdentityMatches,
  callGraphDiagnosticsMessage,
  callGraphTargetMatchesType,
  callGraphTargetTypeId,
  combinedGraphTargetNavigationDisposition,
  createDependencyGraphPendingState,
  createDependencyGraphRenderSequence,
  dependencyCoordinateCandidates,
  dependencyGroupSelectionMessage,
  dependencyGraphRenderSignature,
  graphTargetBlockedReason,
  graphMemberDeepLinkDisposition,
  graphMemberPendingMatchesView,
  graphMemberSurfaceAssembly,
  graphMemberShareTarget,
  graphMemberSelection,
  graphMemberTargetWithSelectedBody,
  graphMemberTargetFromShare,
  graphOnlyBodyTarget,
  MARKDOWN_SANITIZE_OPTIONS,
  MAX_WORKSPACE_PACKAGES,
  memberRequestKey,
  isMemberSection,
  memberSectionDefinitions,
  memberSectionIdsFor,
  packageCoordinateMatchesLocation,
  packageForView,
  packageIdentityKey,
  packageLenses,
  partitionGraphMembers,
  platformPackForGraphAssembly,
  platformPackFromProvenance,
  removeAppendedNotice,
  retainGraphMemberProjection,
  retainWorkspacePackage,
  resolveLoadedGraphTargetCandidate,
  resolveOpportunitySourceCandidate,
  resolveRuntimeGraphTargetCandidate,
  runtimeGraphTargetAssemblyIsResident,
  runtimeGraphTargetNavigationDisposition,
  runtimePackForFramework,
  scopedRequestState,
  searchableMemberGroups,
  sourceReloadKind,
  sourceRequestNeedsLoad,
  spotlightCandidateKey,
  spotlightCandidateSignature,
  typeLensesFor,
  type DependencyGroupData,
  type GraphMemberTarget,
  type GraphMemberShareIdentity,
  type PackageIdentity,
  type PlatformPack,
  type WorkspaceCoordinate,
  uniqueTypeByQueryId,
  workspaceCoordinatesMatch
} from "./data.ts";
import type {
  MemberSection,
  PackageLens,
  TypeLens,
  WorkspaceScope,
} from "./data.ts";
import type { CommandPaletteResult } from "./command-bar.ts";
import {
  bodyTargetMatchesOverload,
  captureLibraryScope,
  filterMemberGroups,
  invalidateGraphMemberNavigationWork,
  invalidateMemberCallGraphWork,
  invalidateMemberDestinationWork,
  invalidateSourceDestinationWork,
  MEMBER_TRAITS,
  memberNavTargetIndex,
  memberScopeIsActive,
  restoreLibraryScope,
  restoreMemberHistoryState,
  selectedConcreteOverload,
  type BodyTarget,
} from "./member-filtering.ts";
import {
  bindWorkspaceLinkNavigation,
  bindWorkspaceRetryToUrl,
  browserCreatedCallGraphTabIds,
  buildPackageRootStateUrl,
  callGraphCaptureTopology,
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  recoverWorkspaceRouteFailure,
  retainedMissingPlatformTarget,
  retainedPlatformTargetVersion,
  selectedBrowserCallGraphPackageTabIds,
  retainWorkspaceUrlPreservation,
  workspaceShareTabsMatchResolved,
  workspaceShareCaptureTopology,
  workspaceViewSignature,
  type NavigationHistorySnapshot,
  type ParsedWorkspaceLocation,
  type WorkspaceDeepLink,
  type WorkspaceUrlPreservation,
  type WorkspaceUrlState,
  type WorkspaceView,
} from "./workspace-navigation.ts";
import {
  createWorkbenchKeybindings,
  WORKBENCH_KEYBINDING_PRIORITY,
} from "./workbench-keybindings.ts";
import {
  createNuGetPackageModel,
  createAppMemberSurface,
  createAppTypeSurface,
  createPackageAcquisition,
  graphOnlyImplementationBody,
  retainGraphOnlyImplementationBody,
  runtimeAssemblyIsResident,
  runtimePackIsResident,
  type AppMemberSurface,
  type AppPackage,
  type AppTypeSurface,
  type InspectedMemberSurface,
  type InspectedPackageDocument,
  type InspectedTypeSurface,
} from "./package-acquisition.ts";
import {
  createPackageInspectionCoordinator,
  resolvePackagePerformanceMember,
  workspaceDependencyKey,
  type PackagePerformance,
} from "./package-inspection.ts";
import {
  bindPackageDependencyList,
  bindPackageView,
  type PackageViewBindingActions,
} from "./package-view.ts";
import {
  bindLibraryControls,
  type LibraryControlBindingActions,
  type PlatformLibraryLens,
} from "./library-controls.ts";
import {
  applicationMenuOwnsFocus,
  bindHomeShell,
  bindLoadErrorShell,
  bindWorkbenchShell,
  captureApplicationMenuFocusOwner,
  focusApplicationMenuButton,
  focusWorkbenchSearch,
  renderApplicationMenu,
  renderKeyboardHelpDialog,
  renderTitleNavigation,
  restoreApplicationMenuFocusIfOwned,
  type ApplicationAction,
  type HomeShellBindingActions,
  type LoadErrorShellBindingActions,
  type WorkbenchShellBinding,
  type WorkbenchShellBindingActions,
  workbenchShellHtml,
} from "./shell-controls.ts";
import {
  homeDemosEntryHtml,
  isProductHomeDemosPath,
  productHomeDemoCatalog,
  productHomeDemoLocationHref,
  setProductHomeDemoCatalog,
  type ProductHomeDemoId,
} from "./product-home-demos.ts";
import {
  createSourceInspectionCoordinator,
  type GraphSourceRequest,
} from "./source-inspection.ts";
import { renderMemberContractSections } from "./member-overview.ts";
import { renderMemberFacts } from "./member-facts.ts";
import { createOperationAuthorityPage } from "./operation-authority.ts";
import {
  createMetadataInspectionCoordinator,
  type AppExplorerState,
} from "./metadata-inspection.ts";
import {
  cancelAnnotatedSourceRequest,
  createMemberDetailInspectionCoordinator,
  type MemberFacts,
} from "./member-detail-inspection.ts";
import {
  callGraphErrorForView,
  createCallGraphInspectionCoordinator,
  type InspectedCallGraph,
  type InspectedCallGraphTarget,
  type PlatformStackEntry,
} from "./call-graph-inspection.ts";
import { createDocumentInspectionCoordinator } from "./document-inspection.ts";
import {
  captureMemberFocus,
  createMemberFocusRestorer,
  focusPlatformGraphError,
  type MemberFocusSnapshot,
} from "./member-focus.ts";
import {
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid
} from "./graph-mermaid.ts";
import {
  bindDependencyGraphNodes,
  bindGraphBack,
  bindGraphPanZoom,
  bindTypeGraphNodes,
  type CallGraphNodeBinding,
  type GraphBackBindingActions,
} from "./graph-interactions.ts";
import { bindGraphExplore, createGraphExplorer } from "./graph-explorer.ts";
import {
  validateAnnotatedSourceDocument,
} from "./annotated-source-view.ts";
import {
  createCSharpRangeHighlighter,
} from "./csharp-highlighting.ts";
import type {
  CSharpHighlightExclusion,
} from "./csharp-highlighting.ts";
import {
  clearAnnotations,
  closeFindingDetail,
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  dismissModalSession,
  escapeAnnotatedSource,
  hitTestAnnotatedNode,
  openModalSession,
  selectAllAnnotations,
  selectDefaultAnnotations,
  selectFinding,
  selectNode as selectAnnotatedNode,
  toggleCoordinates,
  toggleFindingAnnotation,
  toggleMedium,
  type AnnotatedFocusTarget,
  type AnnotatedSourceSession,
  type AnnotatedSourceViewerModel,
} from "./annotated-source-session.ts";
import {
  bindScopeBar,
  captureScopeBarFocus,
  createScopeBarState,
  focusRenderedElement,
  renderApplicationScopeBar,
  renderScopeBar as renderScopeBarPure,
  restoreScopeBarFocus,
  scopeBarShortLabel,
  type ScopeBarBinding,
} from "./scope-bar.ts";
import {
  bindWorkspaceSubject,
  captureWorkspaceFocus,
  focusWorkspace,
  renderWorkspaceSubject,
  renderWorkspaceView as renderWorkspaceViewPure,
  restoreWorkspaceFocus,
  workspaceOccurrenceActionsAreVisible,
} from "./workspace-subject.ts";
import {
  bindDocViewer,
  renderDocViewer as renderDocViewerPure,
  renderPackageDocuments,
  type DocViewerMeta,
} from "./doc-viewer.ts";
import {
  bindGraphSource,
  renderGraphSource as renderGraphSourcePure,
} from "./graph-source.ts";
import {
  annotatedFocusSelector,
  captureAnnotatedSourceScroll,
  renderAnnotatedSourcePageActions,
  bindAnnotatedSource,
  renderAnnotatedSource as renderAnnotatedSourcePure,
  renderAnnotatedSourceModal as renderAnnotatedSourceModalPure,
  restoreAnnotatedSourceScroll,
  type AnnotatedSourceAction,
  type AnnotatedSourceResult,
} from "./annotated-source.ts";
import {
  bindPackageOpportunities,
  renderPackageOpportunities as renderPackageOpportunitiesPure,
} from "./package-opportunities.ts";
import {
  bindContentFrame,
  bindContentFrameMedia,
  CONTENT_FRAME_NARROW_QUERY,
  contentFrameFocusOwnerFor,
  contentFrameResizeFocusOwner,
  decideContentFrameResize,
  focusContentNavigation,
  focusContentNavigationToggle,
  renderContentNavigationBar,
  type ContentFrameFocusOwner,
  type ContentFrameFocusTarget,
  type ContentFramePane,
} from "./content-frame.ts";
import {
  bindTypePanel,
  renderGraphMemberPending,
  renderMemberNav,
  renderSourcePageActions,
  renderSourceResult,
  renderTypeMetadata,
  renderTypeNav,
  renderTypeSource,
  type MemberNavEntry,
  typeMetadataSignature,
  typeSourceSignature,
} from "./type-panel.ts";
import {
  createPackageControls,
  findOpenPackageForQuery,
  type PackageControlPackage,
  type ParsedPackageQuery,
} from "./package-controls.ts";
import {
  bindMetadataExplorer,
  cssEscape,
  estimateExplorerPageSize,
  EXPLORER_PAGE,
  EXPLORER_ROW_H,
  heapStreamName,
  renderMetadataExplorer as renderMetadataExplorerHtml,
  renderPackageMetadata as renderPackageMetadataHtml,
  sameFocus,
  type ExplorerFocus,
  type PackageMetadata,
} from "./metadata-viewer.ts";
import {
  bindSettingsPanel,
  reconcileStyleTaste,
  renderSettingsView,
  type StyleOption,
  type StyleTier,
} from "./settings-panel.ts";
import { renderBrand } from "./brand.ts";
import { loadPlatformIndex, type PlatformIndex } from "./platform-index.ts";
import {
  createSpotlight,
  type SpotlightPackageHit,
  type SpotlightResult,
  type SpotlightScope,
  visibleSpotlightPackageHits,
} from "./spotlight.ts";
import { createSpotlightPackageSearch } from "./spotlight-package-search.ts";
import {
  compareVersionsDesc,
  createCatalogRequests,
  type DotnetRelease,
} from "./catalog-requests.ts";
import { bindStatusBar, fmtBytes, statusBarHtml } from "./status-bar.ts";
import {
  bindCreditsPanel,
  isCreditsPath,
  renderCreditsPage,
} from "./credits-panel.ts";
import {
  createPackageQueryController,
  createQueryRequest,
  initialQueryState,
  toggleFacet,
  withScopeQuery,
  type PackageQueryState,
  type QueryFacetTerm,
} from "./package-query.ts";
import {
  createPackageQueryLiveAnnouncer,
  createPackageQueryAnnouncementTracker,
} from "./package-query-announcements.ts";
import {
  createBrowserPackageQueryDataSource,
  packageQueryFacets,
} from "./package-query-source.ts";
import {
  bindPackageQueryView,
  capturePackageQueryFocus,
  capturePackageQueryScroll,
  renderPackageQueryView,
  restorePackageQueryFocus,
  restorePackageQueryScroll,
  type PackageQueryBindingActions,
} from "./package-query-view.ts";
import {
  historyEntryId,
  isPackageQueryPath,
  isPackageQueryPredecessor,
  packageQueryHistoryState,
  readPackageQueryHistory,
  resolvePackageQueryWorkspaceSuccessor,
  validPackageQueryPrefix,
  withHistoryEntryId,
  type PackageQueryReturnFocus,
} from "./package-query-route.ts";
import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import type {
  BrowserPackageCacheStats,
  BrowserPackageDependencies,
  BrowserPackageDependencyGroup,
  BrowserPackageSurface,
  BrowserWorkspacePackageOccurrenceActivation,
  BrowserWorkspacePackageOccurrenceView,
} from "./facades/inspect-web-package.d.ts";
import type { BrowserTypeMetadata } from "./facades/inspect-web-metadata.d.ts";
import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "./facades/inspect-web-analysis.d.ts";
import type { BrowserSource } from "./facades/inspect-web-source.d.ts";
import type {
  BrowserHomeDemoResolveResult,
  BrowserHomeDemoRunResult,
  BrowserWorkspaceShareState,
} from "./facades/inspect-web-catalog.d.ts";

// Each generated module publishes its own operations and its own wire declarations, so the
// application binds every operation through the module that owns it. There is no aggregate
// facade: a binding below names the facade it came from.
type HostFacade = typeof import("./facades/inspect-web-host.d.ts");
type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
type MetadataFacade = typeof import("./facades/inspect-web-metadata.d.ts");
type AnalysisFacade = typeof import("./facades/inspect-web-analysis.d.ts");
type SourceFacade = typeof import("./facades/inspect-web-source.d.ts");
type CallGraphFacade = typeof import("./facades/inspect-web-call-graph.d.ts");
type CatalogFacade = typeof import("./facades/inspect-web-catalog.d.ts");
type EngineCoordinator = typeof import("./engine-facades.ts");

let startEngine: EngineCoordinator["startEngine"];
let inspectBuildIdentity: HostFacade["buildIdentity"];
let cancelPackageQuery: PackageFacade["cancelPackageQuery"];
let inspectPackageDocument: PackageFacade["getPackageDocument"];
let inspectListPackageQueryFacets: PackageFacade["listPackageQueryFacets"];
let inspectLoadRuntimePack: PackageFacade["loadRuntimePack"];
let inspectLoadRuntimePackAssembly: PackageFacade["loadRuntimePackAssembly"];
let matchPackageDependencyCoordinate:
  PackageFacade["matchPackageDependencyCoordinate"];
let inspectPackageCacheStats: PackageFacade["packageCacheStats"];
let inspectMemberDocumentation: PackageFacade["queryMemberDocumentation"];
let inspectPackage: PackageFacade["queryPackage"];
let inspectPackageDependencies: PackageFacade["queryPackageDependencies"];
let inspectPackageVersions: PackageFacade["queryPackageVersions"];
let resolveDependencyVersion: PackageFacade["resolvePackageDependencyVersion"];
let inspectRunPackageQuery: PackageFacade["runPackageQuery"];
let inspectSearchTypes: PackageFacade["searchTypes"];
let inspectQueryWorkspacePackageOccurrences:
  PackageFacade["queryWorkspacePackageOccurrences"];
let inspectActivateWorkspacePackageOccurrence:
  PackageFacade["activateWorkspacePackageOccurrence"];
let inspectClearWorkspacePackageOccurrences:
  PackageFacade["clearWorkspacePackageOccurrences"];
let inspectGraphMemberSurface: MetadataFacade["queryGraphMemberSurface"];
let inspectPackageHeapEntries: MetadataFacade["queryPackageHeapEntries"];
let inspectPackageMetadata: MetadataFacade["queryPackageMetadata"];
let inspectPackageMetadataTable: MetadataFacade["queryPackageMetadataTable"];
let inspectPlatformHeapEntries: MetadataFacade["queryPlatformHeapEntries"];
let inspectPlatformMetadata: MetadataFacade["queryPlatformMetadata"];
let inspectPlatformMetadataTable: MetadataFacade["queryPlatformMetadataTable"];
let inspectTypeProjection: MetadataFacade["queryTypeProjection"];
let inspectMemberFacts: AnalysisFacade["queryMemberFacts"];
let inspectPackageIntegrations: AnalysisFacade["queryPackageIntegrations"];
let inspectPackageOpportunities: AnalysisFacade["queryPackageOpportunities"];
let inspectPackagePerformance: AnalysisFacade["queryPackagePerformance"];
let inspectPlatformIntegrations: AnalysisFacade["queryPlatformIntegrations"];
let inspectPlatformOpportunities: AnalysisFacade["queryPlatformOpportunities"];
let inspectPlatformPerformance: AnalysisFacade["queryPlatformPerformance"];
let cancelSourceInspection: SourceFacade["cancelSourceQuery"];
let inspectMemberAnnotatedSource: SourceFacade["queryMemberAnnotatedSource"];
let inspectMemberSource: SourceFacade["queryMemberSource"];
let inspectTypeMemberSource: SourceFacade["queryTypeMemberSource"];
let inspectTypeSource: SourceFacade["queryTypeSource"];
let inspectExpandPlatformCallGraph: CallGraphFacade["expandPlatformCallGraph"];
let inspectMemberCallGraph: CallGraphFacade["queryMemberCallGraph"];
let inspectDecodeWorkspaceShareState: CatalogFacade["decodeWorkspaceShareState"];
let inspectEncodeWorkspaceShareState: CatalogFacade["encodeWorkspaceShareState"];
let inspectListHomeDemos: CatalogFacade["listHomeDemos"];
let inspectVocabulary: CatalogFacade["listVocabulary"];
let inspectResolveHomeDemo: CatalogFacade["resolveHomeDemo"];
let inspectRunHomeDemo: CatalogFacade["runHomeDemo"];
let productHomeDemoCatalogError = "";

// The generated modules stay off the first-paint path, so they are imported once the home
// view has painted. `engine-facades.ts` owns composition of the whole set; this function
// owns nothing but binding, and each operation is bound from the module that publishes it.
async function loadEngineModule() {
  const [
    hostFacade,
    packageFacade,
    metadataFacade,
    analysisFacade,
    sourceFacade,
    callGraphFacade,
    catalogFacade,
    coordinator,
  ] = await Promise.all([
    import("/inspect-web-host.js"),
    import("/inspect-web-package.js"),
    import("/inspect-web-metadata.js"),
    import("/inspect-web-analysis.js"),
    import("/inspect-web-source.js"),
    import("/inspect-web-call-graph.js"),
    import("/inspect-web-catalog.js"),
    import("./engine-facades.ts"),
  ]);
  ({ startEngine } = coordinator);
  ({ buildIdentity: inspectBuildIdentity } = hostFacade);
  ({
    cancelPackageQuery,
    getPackageDocument: inspectPackageDocument,
    listPackageQueryFacets: inspectListPackageQueryFacets,
    loadRuntimePack: inspectLoadRuntimePack,
    loadRuntimePackAssembly: inspectLoadRuntimePackAssembly,
    matchPackageDependencyCoordinate,
    packageCacheStats: inspectPackageCacheStats,
    queryMemberDocumentation: inspectMemberDocumentation,
    queryPackage: inspectPackage,
    queryPackageDependencies: inspectPackageDependencies,
    queryPackageVersions: inspectPackageVersions,
    resolvePackageDependencyVersion: resolveDependencyVersion,
    runPackageQuery: inspectRunPackageQuery,
    searchTypes: inspectSearchTypes,
    queryWorkspacePackageOccurrences:
      inspectQueryWorkspacePackageOccurrences,
    activateWorkspacePackageOccurrence:
      inspectActivateWorkspacePackageOccurrence,
    clearWorkspacePackageOccurrences:
      inspectClearWorkspacePackageOccurrences,
  } = packageFacade);
  ({
    queryGraphMemberSurface: inspectGraphMemberSurface,
    queryPackageHeapEntries: inspectPackageHeapEntries,
    queryPackageMetadata: inspectPackageMetadata,
    queryPackageMetadataTable: inspectPackageMetadataTable,
    queryPlatformHeapEntries: inspectPlatformHeapEntries,
    queryPlatformMetadata: inspectPlatformMetadata,
    queryPlatformMetadataTable: inspectPlatformMetadataTable,
    queryTypeProjection: inspectTypeProjection,
  } = metadataFacade);
  ({
    queryMemberFacts: inspectMemberFacts,
    queryPackageIntegrations: inspectPackageIntegrations,
    queryPackageOpportunities: inspectPackageOpportunities,
    queryPackagePerformance: inspectPackagePerformance,
    queryPlatformIntegrations: inspectPlatformIntegrations,
    queryPlatformOpportunities: inspectPlatformOpportunities,
    queryPlatformPerformance: inspectPlatformPerformance,
  } = analysisFacade);
  ({
    cancelSourceQuery: cancelSourceInspection,
    queryMemberAnnotatedSource: inspectMemberAnnotatedSource,
    queryMemberSource: inspectMemberSource,
    queryTypeMemberSource: inspectTypeMemberSource,
    queryTypeSource: inspectTypeSource,
  } = sourceFacade);
  ({
    expandPlatformCallGraph: inspectExpandPlatformCallGraph,
    queryMemberCallGraph: inspectMemberCallGraph,
  } = callGraphFacade);
  ({
    decodeWorkspaceShareState: inspectDecodeWorkspaceShareState,
    encodeWorkspaceShareState: inspectEncodeWorkspaceShareState,
    listHomeDemos: inspectListHomeDemos,
    listVocabulary: inspectVocabulary,
    resolveHomeDemo: inspectResolveHomeDemo,
    runHomeDemo: inspectRunHomeDemo,
  } = catalogFacade);
}

declare global {
  interface Window {
    __platformIndex?: Promise<PlatformIndex | null>;
  }
}

function waitForHomePaint() {
  if (document.visibilityState === "hidden") return Promise.resolve();
  if (globalThis.PerformanceObserver?.supportedEntryTypes?.includes("paint")) {
    if (performance.getEntriesByName("first-contentful-paint", "paint").length) {
      return Promise.resolve();
    }
    return new Promise<void>(resolve => {
      const observer = new PerformanceObserver(list => {
        if (!list.getEntries().some(entry => entry.name === "first-contentful-paint")) return;
        observer.disconnect();
        resolve();
      });
      observer.observe({ type: "paint", buffered: true });
    });
  }
  return new Promise<void>(resolve =>
    requestAnimationFrame(() => setTimeout(resolve, 0)));
}

interface AppMemberGroup {
  key: string;
  name: string;
  kind: string;
  overloads: AppMemberSurface[];
}

function loadStoredTaste() {
  try {
    const value: unknown = JSON.parse(localStorage.getItem("inspect-taste") || "[]");
    if (!Array.isArray(value)) return [];
    const entries: unknown[] = value;
    return entries.filter((item): item is string => typeof item === "string");
  } catch {
    return [];
  }
}

const PLATFORM_RECENT_MAX = 8;
const RECENT_PACKAGES_MAX = 12;

// Recently-opened NuGet packages, most-recent first, persisted across sessions so the
// Home listing survives a refresh (the in-memory workspace does not). Written only from
// actual opens (a successful loadPackage), never from search hits or prefetches. Each
// entry is { id, version, framework }; re-opening refetches the nupkg (fast from the
// browser HTTP cache when still present).
function loadRecentPackages() {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem("inspect-recent-packages") || "[]");
    if (!Array.isArray(value)) return [];
    const entries: unknown[] = value;
    return entries
      .filter((entry): entry is Record<string, unknown> & { id: string } =>
        isRecord(entry) && typeof entry.id === "string" && entry.id.length > 0)
      .map(entry => ({
        id: entry.id,
        version: typeof entry.version === "string" && entry.version ? entry.version : "latest",
        framework: typeof entry.framework === "string" ? entry.framework : "",
      }))
      .slice(0, RECENT_PACKAGES_MAX);
  } catch {
    return [];
  }
}

// Recently-opened platform libraries, most-recent first, persisted across sessions.
// Backs the selector's "Recent" group and the "start on the library you were last
// looking at instead of the aggregate overview" behaviour. Each entry is
// { assembly, pack }; the pack (netcore.app | aspnetcore.app) rides along so a
// remembered ASP.NET Core library re-materialises from the right shared framework.
function loadPlatformRecent() {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem("inspect-platform-recent") || "[]");
    if (!Array.isArray(value)) return [];
    const entries: unknown[] = value;
    return entries
      .filter((entry): entry is Record<string, unknown> & { assembly: string } =>
        isRecord(entry) && typeof entry.assembly === "string")
      .map(entry => ({
        assembly: entry.assembly.replace(/\.dll$/i, ""),
        pack: entry.pack === "aspnetcore.app" ? "aspnetcore.app" : "netcore.app",
      }))
      .slice(0, PLATFORM_RECENT_MAX);
  } catch {
    return [];
  }
}

const retryUnavailable = "unavailable" as const;
type RetryAction = (() => void | Promise<unknown>) | null;
type WorkspaceRestoreFailureHandler = (
  message: string,
) => void;
type ErrorRetryAction = RetryAction | typeof retryUnavailable;

interface SpotlightCache {
  signature: string;
  pool: Array<{ pkg: AppPackage; type: AppTypeSurface }>;
  keyMap: Map<string, { pkg: AppPackage; type: AppTypeSurface }>;
  candidatesJson: string;
}

type HighlightRange = readonly [start: number, end: number];

interface SpotlightMemberCandidate {
  pkg: AppPackage;
  type: AppTypeSurface;
  memberKey: string;
  name: string;
  kind: string;
}

interface SpotlightMemberCache {
  signature: string;
  pool: SpotlightMemberCandidate[];
}

interface PlatformRecent {
  assembly: string;
  pack: string;
}

interface PendingGraphMemberDeepLink {
  packageKey: string;
  viewSignature: string;
  type: string;
  member: string;
  overload: number | string | null;
  section: string | null;
  target: GraphMemberShareIdentity;
}

interface RecentPackage {
  id: string;
  version: string;
  framework: string;
}

interface Diagnostics {
  downloadMs: number;
  startupMs: number;
  precomputeMs: number;
  totalMs: number;
  transfer: number;
  decoded: number;
  assets: number;
}

let spotlightCache: SpotlightCache | null = null;
const HOME_BOT_ANIMATION_DURATION_MS = 5500;
const DEFAULT_REQUESTED_FRAMEWORK = "net10.0";
let homeBotAnimationStartedAt: number | null = null;
let homeReadyGlintPending = true;
const initialState = {
  theme: localStorage.getItem("inspect-theme") === "light" ? "light" : "dark",
  statusBarExpanded: false,
  memberFiltersExpanded: false,
  typeFiltersExpanded: false,
  packages: [],
  package: null,
  home: false,
  credits: false,
  packageQueryOpen: false,
  packageQueryPrefix: "",
  packageQueryNavigationError: "",
  packageQueryCatalogError: "",
  packageQueryOpenedFromApp: false,
  packageQueryPredecessorEntryId: null,
  packageQueryReturnFocus: null,
  packageQueryReturnFocusPending: false,
  packageQueryState: initialQueryState(),
  packageQueryFacets: [],
  platformIndex: null,
  queryNotice: "",
  queryNoticeRetryAction: null,
  requestedPackage: "System.Text.Json",
  requestedVersion: "10.0.0",
  requestedFramework: DEFAULT_REQUESTED_FRAMEWORK,
  workspaceShareBasis: null,
  selectedTypeId: "",
  selectedMemberKey: "",
  memberBrowseTypeId: "",
  selectedOverloadIndex: null,
  memberSection: "overview" as const,
  memberKindFilter: "all",
  memberAccessibilityFilter: "all",
  memberTraitFilter: "",
  memberTextFilter: "",
  memberSource: null,
  memberSourceLoading: false,
  memberSourceError: "",
  memberSourceKey: "",
  sourceRequestGeneration: 0,
  memberAnnotated: null,
  memberAnnotatedLoading: false,
  memberAnnotatedError: "",
  annotatedDestinationError: "",
  memberAnnotatedKey: "",
  memberAnnotatedEmbedded: null,
  memberAnnotatedModal: null,
  typeSource: null,
  typeSourceLoading: false,
  typeSourceError: "",
  typeSourceKey: "",
  typeMetadata: null,
  typeMetadataLoading: false,
  typeMetadataError: "",
  typeMetadataKey: "",
  typeMetadataGeneration: 0,
  packageDependencies: null,
  packageDependenciesLoading: false,
  packageDependenciesError: "",
  packageDependenciesKey: "",
  dependenciesGroupIndex: null,
  workspaceDependencies: {},
  workspaceDependencyErrors: {},
  workspaceDependencyLoads: new Set<string>(),
  packageIntegrations: null,
  packageIntegrationsLoading: false,
  packageIntegrationsError: "",
  packageIntegrationsKey: "",
  packageOpportunities: null,
  packageOpportunitiesLoading: false,
  packageOpportunitiesError: "",
  packageOpportunitiesKey: "",
  packagePerformance: null,
  packagePerformanceLoading: false,
  packagePerformanceError: "",
  packagePerformanceKey: "",
  packageMetadata: null,
  packageMetadataLoading: false,
  packageMetadataError: "",
  packageMetadataKey: "",
  explorer: null,
  memberCallGraph: null,
  memberCallGraphLoading: false,
  memberCallGraphError: "",
  graphMemberNavigationError: "",
  memberCallGraphKey: "",
  memberCallGraphExpanding: false,
  memberCallGraphSeq: 0,
  graphMemberNavigationSeq: 0,
  graphMemberNavigationTitle: "",
  pendingGraphMemberDeepLink: null,
  platformStack: [],
  platformDrillLoading: false,
  platformDrillError: "",
  dotnetReleases: null,
  dotnetReleasesLoading: false,
  memberFacts: null,
  memberFactsLoading: false,
  memberFactsError: "",
  memberFactsKey: "",
  memberDocumentationLoading: false,
  memberDocumentationError: "",
  memberDocumentationKey: "",
  lens: "api" as const,
  packageLens: "overview" as const,
  workspaceOccurrences: null,
  workspaceOccurrenceSignature: "",
  workspaceOccurrenceLoading: false,
  workspaceOccurrenceError: "",
  workspaceSubjectOpen: false,
  atPackageRoot: false,
  typeFilter: "",
  namespaceFilter: "",
  kindFilter: "",
  libraryScope: null,
  platformRecent: loadPlatformRecent(),
  recentPackages: loadRecentPackages(),
  accessibilityFilter: new Set<string>(),
  spotlightOpen: false,
  spotlightQuery: "",
  spotlightIndex: 0,
  spotlightScope: "all" as const,
  spotlightFocus: "input" as const,
  spotlightChipIndex: 0,
  spotlightPkgHits: [],
  spotlightPkgLoading: false,
  spotlightPkgQuery: "",
  packageVersions: {},
  packageVersionsLoading: {},
  runtimePackLoading: false,
  runtimePackError: "",
  selectedBodyTarget: null,
  graphSourceOpen: false,
  graphSource: null,
  graphSourceLoading: false,
  graphSourceError: "",
  docViewerOpen: false,
  docViewer: null,
  docViewerLoading: false,
  docViewerError: "",
  docViewerHtml: "",
  docViewerMeta: null,
  docViewerSeq: 0,
  graphSourceTitle: "",
  graphSourceRequest: null,
  graphSourceSeq: 0,
  styleTiers: null,
  styleOptions: null,
  styleCatalogError: "",
  taste: loadStoredTaste(),
  settings: false,
  settingsReturn: "home",
  keyboardHelp: false,
  typeCursor: 0,
  history: [],
  loading: true,
  loadingMessage: "Starting browser inspection engine…",
  loadingSubtitle: "",
  engineReady: false,
  engineStartupFailed: false,
  engineStatus: "Loading browser WebAssembly…",
  error: "",
  errorTitle: "",
  errorDetail: "",
  retryAction: null,
  diag: null,
  buildIdentity: null,
  packageCacheStats: null,
};

interface StateOverrides {
  packages: AppPackage[];
  package: AppPackage | null;
  workspaceOccurrences: BrowserWorkspacePackageOccurrenceView | null;
  workspaceShareBasis: BrowserWorkspaceShareState | null;
  platformIndex: PlatformIndex | null;
  queryNoticeRetryAction: RetryAction;
  selectedOverloadIndex: number | null;
  memberSource: BrowserSource | null;
  memberAnnotated: AnnotatedSourceResult | null;
  memberAnnotatedEmbedded: AnnotatedSourceSession | null;
  memberAnnotatedModal: AnnotatedSourceSession | null;
  typeSource: BrowserSource | null;
  typeMetadata: BrowserTypeMetadata | null;
  packageDependencies: BrowserPackageDependencies | null;
  dependenciesGroupIndex: number | null;
  workspaceDependencies: Record<string, DependencyGroupData>;
  workspaceDependencyErrors: Record<string, string>;
  workspaceDependencyLoads: Set<string>;
  packageIntegrations: BrowserPackageIntegrations | null;
  packageOpportunities: BrowserPackageOpportunities | null;
  packagePerformance: PackagePerformance | null;
  packageMetadata: PackageMetadata | null;
  explorer: AppExplorerState | null;
  memberCallGraph: InspectedCallGraph | null;
  pendingGraphMemberDeepLink: PendingGraphMemberDeepLink | null;
  platformStack: PlatformStackEntry[];
  dotnetReleases: DotnetRelease[] | null;
  memberFacts: MemberFacts | null;
  libraryScope: Set<string> | null;
  accessibilityFilter: Set<string>;
  spotlightPkgHits: SpotlightPackageHit[];
  spotlightFocus: "input" | "chips";
  spotlightScope: SpotlightScope;
  memberSection: MemberSection;
  lens: TypeLens;
  packageLens: PackageLens;
  packageVersions: Record<string, string[]>;
  packageVersionsLoading: Record<string, boolean>;
  platformRecent: PlatformRecent[];
  recentPackages: RecentPackage[];
  selectedBodyTarget: BodyTarget | null;
  graphSource: BrowserSource | null;
  docViewer: InspectedPackageDocument | null;
  docViewerMeta: DocViewerMeta | null;
  graphSourceRequest: { request: GraphSourceRequest; title: string } | null;
  styleTiers: StyleTier[] | null;
  styleOptions: StyleOption[] | null;
  history: string[];
  retryAction: ErrorRetryAction;
  diag: Diagnostics | null;
  buildIdentity: BrowserBuildIdentity | null;
  packageCacheStats: BrowserPackageCacheStats | null;
  packageQueryState: PackageQueryState;
  packageQueryFacets: QueryFacetTerm[];
  packageQueryPredecessorEntryId: string | null;
  packageQueryReturnFocus: PackageQueryReturnFocus | null;
}

type AppState = Omit<typeof initialState, keyof StateOverrides> & StateOverrides;

const state: AppState = initialState;
const scopeBarState = createScopeBarState();
let scopeBarBinding: ScopeBarBinding | null = null;
let workbenchShellBinding: WorkbenchShellBinding | null = null;
type FailedWorkspaceUrlState = WorkspaceUrlPreservation & (
  | { kind: "canonical" }
  | {
    kind: "route";
    notice: string;
    pathname: string;
    search: string;
    recoveryUrl: string;
  }
);
let failedWorkspaceUrlState: FailedWorkspaceUrlState | null = null;
let packageQueryWorkspaceFocusNavigationSeq: number | null = null;
let packageQueryHandoffNavigationSeq: number | null = null;

interface CanonicalWorkspaceRestoreSnapshot {
  state: AppState;
  hasWorkspace: boolean;
  navigation: NavigationHistorySnapshot<WorkspaceView>;
  failedWorkspaceUrlState: FailedWorkspaceUrlState | null;
}

function captureCanonicalWorkspaceRestoreSnapshot():
CanonicalWorkspaceRestoreSnapshot {
  sourceInspection.cancelCurrentRequest();
  cancelAnnotatedSourceRequest(state);
  const packages = structuredClone(state.packages);
  const activeKey = state.package
    ? packageIdentityKey(state.package)
    : null;
  return {
    state: {
      ...state,
      packages,
      package: activeKey
        ? packages.find(pkg => packageIdentityKey(pkg) === activeKey) ?? null
        : null,
      workspaceDependencies: structuredClone(state.workspaceDependencies),
      workspaceDependencyErrors:
        structuredClone(state.workspaceDependencyErrors),
      workspaceDependencyLoads: new Set(state.workspaceDependencyLoads),
      packageVersions: structuredClone(state.packageVersions),
      packageVersionsLoading:
        structuredClone(state.packageVersionsLoading),
      libraryScope: state.libraryScope
        ? new Set(state.libraryScope)
        : null,
      accessibilityFilter: new Set(state.accessibilityFilter),
      memberAnnotatedEmbedded: state.memberAnnotatedEmbedded
        ? structuredClone(state.memberAnnotatedEmbedded)
        : null,
      memberAnnotatedModal: state.memberAnnotatedModal
        ? structuredClone(state.memberAnnotatedModal)
        : null,
      platformStack: structuredClone(state.platformStack),
      platformRecent: structuredClone(state.platformRecent),
      recentPackages: structuredClone(state.recentPackages),
      spotlightPkgHits: structuredClone(state.spotlightPkgHits),
      history: [...state.history],
    },
    hasWorkspace: state.package !== null,
    navigation: navigationHistory.snapshot(),
    failedWorkspaceUrlState: failedWorkspaceUrlState
      ? structuredClone(failedWorkspaceUrlState)
      : null,
  };
}

function restoreCanonicalWorkspaceRestoreSnapshot(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
) {
  clearWorkspaceOccurrenceView();
  clearWorkspacePackages();
  Object.assign(state, snapshot.state);
  state.workspaceOccurrenceSignature = "";
  state.workspaceOccurrenceLoading = false;
  state.workspaceOccurrences = null;
  state.workspaceOccurrenceError = "";
  navigationHistory.restore(snapshot.navigation);
  failedWorkspaceUrlState = snapshot.failedWorkspaceUrlState
    ? structuredClone(snapshot.failedWorkspaceUrlState)
    : null;
  spotlightCache = null;
  persistRecentPackages();
  persistPlatformRecent();
  refreshPackageStats();
}

const keybindings = createWorkbenchKeybindings();
let keyboardHelpBindings = keybindings.bindingsFor();
const operationAuthority = createOperationAuthorityPage();
const sourceInspection = createSourceInspectionCoordinator({
  state,
  operationAuthority,
  queryMemberSource: request => inspectMemberSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.selectorKey,
    request.metadataToken,
    request.taste),
  queryTypeSource: request => inspectTypeSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.taste),
  queryGraphSource: (request, taste) => inspectTypeMemberSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.selectorKey,
    request.metadataToken,
    taste),
  memberSourceHasConcreteOverload,
  cancelEngineSourceRequest: () => cancelSourceInspection?.(),
  reportOperationDiagnostic: diagnostic => {
    console.error("Source operation authority failure.", diagnostic);
    return undefined;
  },
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
});
const packageQueryController = createPackageQueryController(
  state.packageQueryState,
  createBrowserPackageQueryDataSource({
    cancel: () => cancelPackageQuery(),
    run: (
      prefix,
      facetIdsJson,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      eventSink,
    ) => inspectRunPackageQuery(
      prefix,
      facetIdsJson,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      eventSink),
  }),
  () => {
    if (state.packageQueryOpen) render();
  },
);
const packageQueryAnnouncements = createPackageQueryAnnouncementTracker();
const packageQueryLiveAnnouncer = createPackageQueryLiveAnnouncer(
  () => document.querySelector<HTMLElement>("#package-query-announcement"));

const metadataInspection = createMetadataInspectionCoordinator({
  state,
  queryTypeMetadata: request => inspectTypeProjection(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type),
  queryPackageTable: (explorer, index, startRowId, maxRows) =>
    inspectPackageMetadataTable(
      explorer.packageId,
      explorer.version,
      explorer.framework,
      explorer.assemblyFileName,
      index,
      startRowId,
      maxRows),
  queryPlatformTable: (explorer, index, startRowId, maxRows) =>
    inspectPlatformMetadataTable(
      explorer.framework,
      explorer.version,
      explorer.assemblyFileName,
      explorer.pack || "",
      index,
      startRowId,
      maxRows),
  queryPackageHeap: (explorer, heapName) =>
    inspectPackageHeapEntries(
      explorer.packageId,
      explorer.version,
      explorer.framework,
      explorer.assemblyFileName,
      heapName),
  queryPlatformHeap: (explorer, heapName) =>
    inspectPlatformHeapEntries(
      explorer.framework,
      explorer.version,
      explorer.assemblyFileName,
      explorer.pack || "",
      heapName),
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
  scrollExplorerToFocus: explorerScrollToFocus,
});
const memberDetailInspection = createMemberDetailInspectionCoordinator({
  state,
  queryDocumentation: (request, documentationId) =>
    inspectMemberDocumentation(
      request.packageId,
      request.version,
      request.framework,
      request.assembly,
      documentationId),
  queryAnnotated: async request => {
    const result = await inspectMemberAnnotatedSource(
      request.packageId,
      request.version,
      request.framework,
      request.assembly,
      request.typeIdentity,
      request.type,
      request.member,
      request.memberSignature,
      request.selectorKey,
      request.metadataToken,
      request.taste);
    const document = result.document;
    validateAnnotatedSourceDocument(document);
    return { ...result, document };
  },
  queryFacts: request =>
    inspectMemberFacts(
      request.packageId,
      request.version,
      request.framework,
      request.assembly,
      request.typeIdentity,
      request.member,
      request.memberSignature,
      request.selectorKey,
      request.metadataToken,
      request.implementationBodySelected),
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
});
const callGraphInspection = createCallGraphInspectionCoordinator({
  state,
  queryWorkspace: (request, workspace) => inspectMemberCallGraph(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.typeIdentity,
    request.type,
    request.member,
    request.memberSignature,
    request.selectorKey,
    request.metadataToken,
    JSON.stringify(workspace)),
  queryPlatform: request =>
    inspectExpandPlatformCallGraph(
      request.framework,
      request.platformVersion,
      request.assembly,
      request.pack,
      request.assemblyVersion ?? "",
      request.assemblyCulture,
      request.assemblyPublicKeyToken,
      request.type,
      request.member,
      request.selectorKey,
      request.metadataToken),
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
  renderCallGraph: async () => {
    await renderMermaidCallGraph();
  },
  nextPaint,
  refreshPackageStats,
  patchCallGraphSection,
});
const documentInspection = createDocumentInspectionCoordinator({
  state,
  queryDocument: request => inspectPackageDocument(
    request.packageId,
    request.version,
    request.document.path),
  renderMarkdown,
  renderMarkdownInline,
  describeError: errorMessage,
  render,
});

function captureView(): WorkspaceView | null {
  if (!state.package) return null;
  return {
    package: state.package.id,
    packageKey: packageIdentityKey(state.package),
    workspaceSubjectOpen: state.workspaceSubjectOpen,
    lens: state.lens,
    selectedTypeId: state.selectedTypeId,
    selectedMemberKey: state.selectedMemberKey,
    memberBrowseTypeId: state.memberBrowseTypeId,
    memberKindFilter: state.memberKindFilter,
    memberAccessibilityFilter: state.memberAccessibilityFilter,
    memberTraitFilter: state.memberTraitFilter,
    memberTextFilter: state.memberTextFilter,
    selectedOverloadIndex: state.selectedOverloadIndex,
    bodyTarget: state.selectedBodyTarget,
    memberSection: state.memberSection,
    atPackageRoot: state.atPackageRoot,
    packageLens: state.packageLens,
    libraryScope: captureLibraryScope(state.libraryScope),
  };
}

function viewSignature() {
  const view = captureView();
  return view ? workspaceViewSignature(view) : "";
}

interface ViewOperationOwner {
  sequence: number;
  navigationSequence: number;
  sourceView: string;
}

function captureViewOperation(sequence: number): ViewOperationOwner {
  return {
    sequence,
    navigationSequence: navigationSequence.current(),
    sourceView: viewSignature(),
  };
}

function ownsViewOperation(
  owner: ViewOperationOwner,
  currentSequence: number,
) {
  return owner.sequence === currentSequence
    && owner.navigationSequence === navigationSequence.current()
    && owner.sourceView === viewSignature();
}

function invalidateGraphMemberNavigation() {
  invalidateGraphMemberNavigationWork(state);
}

function normalizeCurrentNavEntry() {
  navigationHistory.normalizeCurrent();
}

function applyView(view: WorkspaceView) {
  const pkg = packageForView(state.packages, view);
  if (!pkg) return false;
  invalidateMemberDestinationWork(state);
  activatePackage(pkg);
  state.libraryScope = restoreLibraryScope(
    view.libraryScope,
    pkg.types.map(type => libraryKey(type)));
  const type = pkg.types.find(item => item.id === view.selectedTypeId);
  const member = type
    ? memberGroups(type).find(group => group.key === view.selectedMemberKey)
    : null;
  const graphSelection = type && view.bodyTarget
    ? findGraphMemberSelection(type, view.bodyTarget)
    : null;
  const hasSelectedBody =
    graphSelection?.group.key === view.selectedMemberKey;
  const memberHistory = restoreMemberHistoryState(
    view,
    type,
    member,
    member
      ? memberSectionIdsFor(member, pkg.isRuntimePack, hasSelectedBody)
      : []);
  state.lens = view.lens;
  state.selectedTypeId = type?.id ?? pkg.types[0]?.id ?? "";
  state.selectedMemberKey = memberHistory.selectedMemberKey;
  state.memberBrowseTypeId = memberHistory.memberBrowseTypeId;
  state.memberKindFilter = memberHistory.memberKindFilter;
  state.memberAccessibilityFilter = memberHistory.memberAccessibilityFilter;
  state.memberTraitFilter = memberHistory.memberTraitFilter;
  state.memberTextFilter = memberHistory.memberTextFilter;
  state.selectedOverloadIndex = memberHistory.selectedOverloadIndex;
  state.memberSection = memberHistory.memberSection;
  state.atPackageRoot = view.atPackageRoot ?? false;
  state.workspaceSubjectOpen =
    view.workspaceSubjectOpen && state.atPackageRoot;
  state.packageLens = view.packageLens ?? "overview";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = memberHistory.selectedBodyTarget;
  if (!state.atPackageRoot) revealTypeInFilters(type);
  const requestedOverloadIndex = view.selectedOverloadIndex;
  const historyGraphTarget =
    graphMemberTargetFromShare(graphMemberShareTarget(view.bodyTarget));
  if (!state.atPackageRoot
    && type
    && graphSelection?.group.key === view.selectedMemberKey) {
    state.selectedMemberKey = graphSelection.group.key;
    state.memberBrowseTypeId = type.id;
    state.selectedOverloadIndex = graphSelection.overloadIndex;
    state.selectedBodyTarget = retainGraphOnlyImplementationBody(
      graphSelection.group.overloads[graphSelection.overloadIndex],
      view.bodyTarget);
    state.memberSection = isMemberSection(view.memberSection)
      && memberSectionIdsFor(
        graphSelection.group,
        pkg.isRuntimePack,
        true).includes(view.memberSection)
      ? view.memberSection
      : "overview";
  }
  if (!state.atPackageRoot
    && state.lens === "api"
    && view.selectedMemberKey
    && type
    && historyGraphTarget
    && graphSelection?.group.key !== view.selectedMemberKey) {
    state.selectedMemberKey = view.selectedMemberKey;
    state.selectedOverloadIndex = view.selectedOverloadIndex;
    state.memberSection = isMemberSection(view.memberSection)
      ? view.memberSection
      : "overview";
    state.selectedBodyTarget = view.bodyTarget;
    navigationHistory.normalizeCurrent();
    state.pendingGraphMemberDeepLink = {
      packageKey: packageIdentityKey(pkg),
      viewSignature: viewSignature(),
      type: type.id,
      member: view.selectedMemberKey,
      overload: requestedOverloadIndex,
      section: view.memberSection,
      target: historyGraphTarget,
    };
    observeAsync(
      restorePendingGraphMember(),
      "Restoring a graph member from navigation history");
    return true;
  }
  navigationHistory.normalizeCurrent();
  if (!state.atPackageRoot && state.lens === "api" && state.selectedMemberKey && member) {
    const section = state.memberSection;
    if (section === "source")
      observeAsync(loadSelectedMemberSource(), "Loading member source");
    else if (section === "annotated")
      observeAsync(loadSelectedMemberAnnotatedSource(), "Loading annotated member source");
    else if (section === "call-graph")
      observeAsync(loadSelectedMemberCallGraph(), "Loading the member call graph");
    else if (section === "facts")
      observeAsync(loadSelectedMemberFacts(), "Loading member facts");
    else if (section === "overview")
      observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
    else
      assertNever(section, "member section");
  } else {
    render();
  }
  return true;
}

const navigationHistory = createNavigationHistory({
  capture: captureView,
  signature: workspaceViewSignature,
  apply: applyView,
  onExhausted: render,
});
const navigationSequence = createNavigationSequence();

function currentPackageQueryHandoff() {
  return packageQueryHandoffNavigationSeq !== null
    && navigationSequence.isCurrent(packageQueryHandoffNavigationSeq);
}

function recordNav() {
  navigationHistory.record();
}

function navBack() {
  closeGraphExplorerForNavigation();
  dismissAnnotatedSourceModal(false);
  navigationHistory.back();
}

function navForward() {
  closeGraphExplorerForNavigation();
  dismissAnnotatedSourceModal(false);
  navigationHistory.forward();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function memberSectionsFor(member: AppMemberGroup) {
  const allowed = new Set(
    memberSectionIdsFor(
      member,
      state.package?.isRuntimePack,
      memberHasSelectedBody(member)));
  return memberSectionDefinitions.filter(([id]) => allowed.has(id));
}

function memberHasSelectedBody(member: AppMemberGroup) {
  const type = selectedType();
  if (!type || !state.selectedBodyTarget) return false;
  const selection = findGraphMemberSelection(type, state.selectedBodyTarget);
  return selection?.group.key === member.key
    && (state.selectedOverloadIndex == null
      || selection.overloadIndex === state.selectedOverloadIndex);
}

const workspaceLocation = createWorkspaceLocationPersistence({
  current: () => ({
    href: location.href,
    pathname: location.pathname,
    search: location.search,
    hash: location.hash,
  }),
  replace: (url, historyState) =>
    history.replaceState(historyState, "", url),
  push: (url, historyState) =>
    history.pushState(historyState, "", url),
  decode: value => inspectDecodeWorkspaceShareState(value),
  encode: stateJson => inspectEncodeWorkspaceShareState(stateJson),
});
let pendingDemoNavigation: {
  navigationSeq: number;
  destination: string;
} | null = null;

function parseLocation() {
  return workspaceLocation.parseCurrent();
}

function parseWorkspaceHref(href: string): ParsedLocation {
  const url = new URL(href, location.href);
  return parseWorkspaceLocation({
    href: url.href,
    pathname: url.pathname,
    search: url.search,
    hash: url.hash,
  }, value => inspectDecodeWorkspaceShareState(value));
}

function beginDemoNavigation(destination: string): number {
  const navigationSeq = navigationSequence.begin();
  stageDemoNavigation(navigationSeq, destination);
  return navigationSeq;
}

function stageDemoNavigation(
  navigationSeq: number,
  destination: string,
): void {
  pendingDemoNavigation = { navigationSeq, destination };
}

function commitDemoNavigation(navigationSeq: number): boolean {
  if (!navigationSequence.isCurrent(navigationSeq)
    || pendingDemoNavigation?.navigationSeq !== navigationSeq) return false;
  workspaceLocation.push(pendingDemoNavigation.destination);
  pendingDemoNavigation = null;
  return true;
}

function cancelDemoNavigation(navigationSeq?: number): void {
  if (navigationSeq === undefined
    || pendingDemoNavigation?.navigationSeq === navigationSeq) {
    pendingDemoNavigation = null;
  }
}

type ParsedLocation = ParsedWorkspaceLocation;

const initialWorkspace = workspaceLocation.preflightCurrent();
const initialLocation = initialWorkspace.visible;
// A bare visit (no package, no shared workspace packet) lands on the intro/home page
// instead of auto-loading a package. Any deep link or shared link skips home and restores
// its workspace directly.
state.credits = isCreditsPath(location.pathname);
state.packageQueryOpen = isPackageQueryPath(location.pathname);
const productHomeDemosOpen = isProductHomeDemosPath(location.pathname);
if (state.packageQueryOpen) {
  applyPackageQueryHistory(history.state);
}
state.home = state.credits
  || (!state.packageQueryOpen
    && !productHomeDemosOpen
    && !initialLocation.package
    && !initialWorkspace.hasWorkspaceState
    && !initialLocation.routeFailure);
if (productHomeDemosOpen) {
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
}
state.queryNotice = "";
if (initialLocation.package) {
  state.requestedPackage = initialLocation.package;
  state.requestedVersion = initialLocation.version || "latest";
}
if (initialLocation.framework) state.requestedFramework = initialLocation.framework;
if (initialLocation.lens) state.lens = initialLocation.lens;
if (initialLocation.atPackageRoot) {
  state.atPackageRoot = true;
  state.packageLens = initialLocation.packageLens || "overview";
}

function deepLinkFromLocation(loc: ParsedLocation): DeepLink {
  return {
    type: loc.type,
    member: loc.member,
    memberAnchor: loc.memberAnchor,
    memberSignature: loc.memberSignature,
    overload: loc.overload,
    section: loc.section,
    bodyTarget: loc.bodyTarget,
    memberBrowse: loc.memberBrowse,
    memberTextFilter: loc.memberTextFilter,
    memberKindFilter: loc.memberKindFilter,
    memberAccessibilityFilter: loc.memberAccessibilityFilter,
    memberTraitFilter: loc.memberTraitFilter,
    graphTarget: loc.graphTarget
  };
}

function requireElement(selector: string): HTMLElement {
  const element = document.querySelector<HTMLElement>(selector);
  if (!element) throw new Error(`Required element '${selector}' is missing.`);
  return element;
}

const app = requireElement("#app");
const graphExplorer = createGraphExplorer(document);
let graphExplorerNavigationFocusPending = false;
let graphExplorerOriginKey: string | null = null;
type MermaidModule = typeof import("mermaid");
type MarkedModule = typeof import("marked");
type DomPurifyModule = typeof import("dompurify");
let mermaidModule: Promise<MermaidModule> | undefined;
let markdownModule: Promise<[MarkedModule, DomPurifyModule]> | undefined;
const depGraphRenderSequence = createDependencyGraphRenderSequence();
let callGraphRenderSeq = 0;
type CallGraphRenderResult =
  | { status: "rendered" }
  | { status: "superseded" }
  | { status: "failed"; message: string };
let callGraphRenderOperation: {
  definition: string;
  theme: "light" | "dark";
  promise: Promise<CallGraphRenderResult>;
} | null = null;
let spotlightFocusGeneration = 0;
let documentFocusGeneration = 0;
let contentFramePane: ContentFramePane = "detail";
let contentFrameFocusOwner: ContentFrameFocusOwner = null;
interface ContentFrameReplacementAuthority {
  owner: ContentFrameFocusOwner;
  focusGeneration: number;
}
let contentFrameReplacementAuthority: ContentFrameReplacementAuthority | null =
  null;
const contentFrameMedia = window.matchMedia(CONTENT_FRAME_NARROW_QUERY);
document.documentElement.dataset.theme = state.theme;

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const NUGET_DEFAULT_PACKAGE_ICON =
  "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png";

function renderInspectedSubjectIcon(pkg: AppPackage): string {
  if (scope() === "workspace")
    return '<span class="subject-icon" aria-hidden="true">W</span>';
  if (pkg.isRuntimePack)
    return '<span class="subject-icon" aria-hidden="true">◎</span>';

  const source = pkg.icon
    ? `data:${pkg.icon.mediaType};base64,${pkg.icon.base64}`
    : NUGET_DEFAULT_PACKAGE_ICON;
  return `<span class="subject-icon" aria-hidden="true">
    <img src="${escapeHtml(source)}" alt="" data-package-icon>
  </span>`;
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (isRecord(error) && typeof error.message === "string") return error.message;
  if (typeof error === "string") return error;
  if (typeof error === "number" || typeof error === "boolean" || typeof error === "bigint")
    return String(error);
  return "";
}

function reportAsyncFailure(description: string, error: unknown): void {
  console.error(`${description} failed.`, error);
  appendQueryNotice(
    `${description} failed: ${errorMessage(error) || "Unknown error."}`);
  render();
}

function observeAsync(
  operation: Promise<unknown> | undefined,
  description: string,
): void {
  if (!operation) return;
  operation.catch((error: unknown) => {
    reportAsyncFailure(description, error);
  });
}

function observeAction(
  action: () => void | Promise<unknown>,
  description: string,
): void {
  try {
    observeAsync(Promise.resolve(action()), description);
  } catch (error) {
    reportAsyncFailure(description, error);
  }
}

// The Wasm engine is an in-process producer whose DTO surface is generated by ts-jsexport.
// Runtime validators are not generated yet, so this is the one trusted JSON/type boundary.
// oxlint-disable-next-line typescript/no-unnecessary-type-parameters
function parseEngineJson<T>(json: string): T {
  const parsed: unknown = JSON.parse(json);
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return parsed as T;
}

function currentPackage(): AppPackage {
  if (!state.package) throw new Error("No package is active.");
  return state.package;
}

const spotlightPackageSearch = createSpotlightPackageSearch({
  state,
  queryPackages: querySpotlightPackages,
  schedule: (callback, delay) => setTimeout(() => void callback(), delay),
  cancelScheduled: handle => clearTimeout(handle),
  updateResults: () => spotlight.updateResults(),
});
const catalogRequests = createCatalogRequests({
  state,
  queryDotnetReleases,
  queryPackageVersions: packageId => inspectPackageVersions(packageId),
  updatePlatformVersionSelect,
  updatePackageVersionSelect: updateVersionSelect,
});
const spotlight = createSpotlight({
  keybindings,
  state,
  lenses: () => typeLensesFor(state.package),
  escapeHtml,
  highlightRanges,
  kindIcon,
  searchResults: spotlightResults,
  pickResult: pickSpotlightResult,
  executeCommand,
  reportCommandError: error =>
    reportAsyncFailure("Running a Spotlight command", error),
  commandContext: () => !state.home && state.package
    ? { command: state.spotlightQuery, package: state.package }
    : null,
  schedulePackageFetch: () => spotlightPackageSearch.schedule(),
  resetPackageSearch: () => spotlightPackageSearch.reset(),
  packageSearchLoading: () => state.spotlightPkgLoading,
  packageCount: () => state.packages.length,
  activeFramework: () => state.package?.activeFramework || "",
  render,
  focusAfterDismiss: () =>
    restoreContentFrameFocusAfterDismiss(
      spotlightFocusGeneration,
      documentFocusGeneration),
  captureFocusAfterDismiss: () => {
    const navigationGeneration = spotlightFocusGeneration;
    const focusGeneration = documentFocusGeneration;
    return () => restoreContentFrameFocusAfterDismiss(
      navigationGeneration,
      focusGeneration);
  },
});

function beginSpotlightNavigation() {
  return ++spotlightFocusGeneration;
}

function isTextEntry(element: Element | null = document.activeElement) {
  return ["INPUT", "SELECT", "TEXTAREA"].includes(element?.tagName ?? "")
    || (element instanceof HTMLElement && element.isContentEditable);
}

function isInteractiveElement(element: Element | null) {
  return Boolean(element?.matches(
    "button, a[href], input, select, textarea, summary, "
    + "[role=button], [role=link], [role=checkbox]"));
}

function canRestoreWorkbenchFocus(
  generation: number,
  focusGeneration = documentFocusGeneration,
) {
  return generation === spotlightFocusGeneration
    && focusGeneration === documentFocusGeneration
    && !state.spotlightOpen && !state.graphSourceOpen && !state.docViewerOpen
    && !state.settings && !state.keyboardHelp
    && !applicationMenuOwnsFocus(document) && !isTextEntry();
}

function focusTypeList(
  generation = spotlightFocusGeneration,
  focusGeneration = documentFocusGeneration,
) {
  if (!canRestoreWorkbenchFocus(generation, focusGeneration)) return;
  afterCurrentNavigationFrame(() => {
    if (!canRestoreWorkbenchFocus(generation, focusGeneration)) return;
    if (contentFrameUsesPush() && contentFrameMedia.matches) {
      contentFramePane = "detail";
      render({ synchronizeUrl: false });
      afterCurrentNavigationFrame(() => {
        if (canRestoreWorkbenchFocus(generation, focusGeneration))
          focusContentNavigationToggle(document);
      });
      return;
    }
    focusContentNavigation(document);
  });
}

function restoreContentNavigationFocus(
  generation: number,
  focusGeneration = documentFocusGeneration,
) {
  if (!canRestoreWorkbenchFocus(generation, focusGeneration)) return;
  afterCurrentNavigationFrame(() => {
    if (canRestoreWorkbenchFocus(generation, focusGeneration))
      focusContentNavigation(document);
  });
}

function restoreContentFrameFocusAfterDismiss(
  generation = spotlightFocusGeneration,
  focusGeneration = documentFocusGeneration,
) {
  if (!canRestoreWorkbenchFocus(generation, focusGeneration)) return;
  afterCurrentNavigationFrame(() => {
    if (!canRestoreWorkbenchFocus(generation, focusGeneration)) return;
    if (contentFrameUsesPush() && contentFrameMedia.matches) {
      if (contentFramePane === "navigation")
        focusContentNavigation(document);
      else
        focusContentNavigationToggle(document);
      return;
    }
    focusContentNavigation(document);
  });
}

function contentFrameUsesPush() {
  return scope() !== "workspace";
}

function showContentNavigation() {
  if (!contentFrameUsesPush()) return;
  contentFramePane = "navigation";
  render({ synchronizeUrl: false });
  afterCurrentNavigationFrame(() => focusContentNavigation(document));
}

function showContentDetail() {
  if (!contentFrameUsesPush()) return;
  contentFramePane = "detail";
  render({ synchronizeUrl: false });
  afterCurrentNavigationFrame(() =>
    focusContentNavigationToggle(document));
}

function showContentDetailAfterRender() {
  contentFramePane = "detail";
  if (!contentFrameMedia.matches) return;
  afterCurrentNavigationFrame(() =>
    focusContentNavigationToggle(document));
}

function focusContentFrameTarget(target: ContentFrameFocusTarget) {
  if (target === "navigation")
    focusContentNavigation(document);
  else if (target === "navigation-toggle")
    focusContentNavigationToggle(document);
}

function trackContentFrameFocus(event: FocusEvent) {
  documentFocusGeneration++;
  contentFrameReplacementAuthority = null;
  const focused = event.target instanceof HTMLElement ? event.target : null;
  contentFrameFocusOwner = contentFrameFocusOwnerFor(focused);
}

function trackContentFramePointer(event: PointerEvent) {
  documentFocusGeneration++;
  contentFrameReplacementAuthority = null;
  const pointed = event.target instanceof Element ? event.target : null;
  contentFrameFocusOwner = contentFrameFocusOwnerFor(pointed);
}

function releaseContentFrameFocusOwner() {
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      const focused = document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
      if (contentFrameFocusOwnerFor(focused) === null)
        contentFrameFocusOwner = null;
    });
  });
}

function handleContentFrameResize(event: MediaQueryListEvent) {
  if (!contentFrameUsesPush()) return;
  const focused = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null;
  const replacementFocusOwner =
    contentFrameReplacementAuthority?.owner ?? null;
  const resizeFocusOwner = contentFrameResizeFocusOwner(
    focused,
    contentFrameFocusOwner,
    replacementFocusOwner);
  contentFrameReplacementAuthority = null;
  contentFrameFocusOwner = replacementFocusOwner === "navigation"
    || replacementFocusOwner === "detail"
    ? contentFrameFocusOwnerFor(focused)
    : resizeFocusOwner;
  const decision = decideContentFrameResize(
    contentFramePane,
    event.matches,
    resizeFocusOwner);
  contentFramePane = decision.pane;
  if (decision.render) {
    render({ synchronizeUrl: false });
    afterCurrentNavigationFrame(() =>
      focusContentFrameTarget(decision.focus));
    return;
  }
  if (decision.focus)
    requestAnimationFrame(() => focusContentFrameTarget(decision.focus));
}

function openSpotlight(seed = "", spotlightScope: SpotlightScope = "all") {
  if (state.loading || state.error) return;
  beginSpotlightNavigation();
  spotlight.open(seed, spotlightScope);
}

function closeSpotlight() {
  spotlight.close();
}

const packageControls = createPackageControls({
  selectFramework: framework =>
    observeAsync(
      switchPackageFramework(framework),
      "Switching the package framework"),
  selectVersion: version => {
    if (state.package?.isRuntimePack)
      observeAsync(
        switchPlatformVersion(version),
        "Switching the platform version");
    else
      observeAsync(
        switchPackageVersion(version),
        "Switching the package version");
  },
});

function selectedType() {
  if (!state.package) return null;
  return state.package.types.find(item => item.id === state.selectedTypeId) || filteredTypes()[0] || state.package.types[0];
}

function filteredTypes() {
  if (!state.package) return [];
  const needle = state.typeFilter.toLowerCase();
  return state.package.types.filter(item => {
    return typeMatchesFilterText(item, needle)
      && (!state.namespaceFilter || item.namespace === state.namespaceFilter)
      && (!state.kindFilter || typeKind(item.kind) === state.kindFilter)
      && (!state.libraryScope || state.libraryScope.has(libraryKey(item)))
      && state.accessibilityFilter.has(item.accessibilityId);
  });
}

// The type the type list would land on by default: the first type the CURRENT
// accessibility filter (and, if set, library scope) admits, not merely the first type the
// backend happens to return. Package/library roots otherwise land on whatever type sorts
// first server-side — often an internal compiler-generated type (e.g. an FxResources.*.SR
// resource shim) — while the type list itself (filteredTypes) hides it, splitting the
// landing type from the visible list. Honoring libraryScope here (rather than only
// accessibility) matters for restores that legitimately set it before falling back to a
// default type -- e.g. a deep link to a platform library's root with no explicit type --
// so the default lands inside the restored library instead of picking a package-wide type
// that a later reconciliation step then treats as evidence the scope should be cleared.
// Callers must reset any stale type/namespace/kind/library filters (and the accessibility
// filter, via activatePackage) before calling this so it reflects the incoming package.
function defaultVisibleTypeId(pkg: AppPackage | null | undefined) {
  if (!pkg) return "";
  const visible = pkg.types.find(item =>
    state.accessibilityFilter.has(item.accessibilityId)
    && (!state.libraryScope || state.libraryScope.has(libraryKey(item))));
  if (visible) return visible.id;
  // No type within the active library scope passes the current accessibility filter -- e.g.
  // an internal-only platform library (zero public types) reached via a link with no explicit
  // type. Prefer a type still within the requested scope over an unrelated package-wide type,
  // so the caller's accessibility-widening reconciliation (see reconcileAccessibilityFilter)
  // can admit it without losing the library scope that was the actual target of the restore.
  const libraryScope = state.libraryScope;
  if (libraryScope) {
    const scoped = pkg.types.find(item => libraryScope.has(libraryKey(item)));
    if (scoped) return scoped.id;
  }
  return pkg.types[0]?.id || "";
}

// Widen state.accessibilityFilter, if necessary, so it admits the given type. Every
// defaultVisibleTypeId caller must invoke this immediately after assigning
// state.selectedTypeId so a package/library where every type falls outside the current
// filter (e.g. one with zero public types) doesn't leave the type list empty while the pane
// renders a type filteredTypes() would hide.
function reconcileAccessibilityFilter(
  type: InspectedTypeSurface | null | undefined,
) {
  if (!type) return;
  if (!state.accessibilityFilter.has(type.accessibilityId)) {
    const next = new Set(state.accessibilityFilter);
    next.add(type.accessibilityId);
    state.accessibilityFilter = next;
  }
}


// The "Filter types" box matches, within the active scope, on the type's own identity
// (name/namespace/kind), the owning library (assembly) name, and — so a member you
// remember surfaces its declaring type — any member name on the type. The member scan
// runs only when the cheaper identity/library match misses, so keystroke filtering stays
// responsive on large packs like the runtime pseudo-package.
function typeMatchesFilterText(item: AppTypeSurface, needle: string) {
  if (!needle) return true;
  if (`${item.name} ${item.namespace} ${item.kind} ${libraryKey(item)}`.toLowerCase().includes(needle)) return true;
  const members = item.api?.filter(member => !member.graphOnly);
  if (!members || !members.length) return false;
  for (const member of members) {
    if ((member.name || "").toLowerCase().includes(needle)) return true;
  }
  return false;
}

// Owning-library key for a type: the assembly file name without a .dll suffix,
// falling back to the package's primary assembly. Used to scope the type list to
// one or more libraries within a multi-assembly package.
function libraryKey(item: InspectedTypeSurface | null | undefined) {
  const asm = (item && item.assembly) || (state.package && state.package.assembly) || "";
  return asm.replace(/\.dll$/i, "");
}

// Libraries present among the loaded types, each with its type count, sorted by
// size then name. The unit the Library selector and per-library overview use.
function packageLibraries() {
  if (!state.package) return [];
  const counts = new Map<string, number>();
  for (const item of state.package.types) {
    if (!state.accessibilityFilter.has(item.accessibilityId)) continue;
    const key = libraryKey(item);
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  return [...counts.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
}

const LIBRARY_CHIP_MAX = 6;

// How the Library selector presents itself: hidden for a single-library package,
// multi-select chips (all on by default) for a handful, single-select dropdown
// once there are too many to fit as chips.
function libraryMode() {
  const count = packageLibraries().length;
  if (count <= 1) return "none";
  return count <= LIBRARY_CHIP_MAX ? "chips" : "dropdown";
}

// Effective set of in-scope library keys (a null scope means every library).
function activeLibrarySet() {
  if (state.libraryScope) return state.libraryScope;
  return new Set(packageLibraries().map(lib => lib.name));
}

// Multi-select chip toggle. "" resets to all libraries (null scope). Toggling a
// single chip flips it in the active set; a set that ends up full or empty
// collapses back to the "all libraries" default.
function toggleLibraryChip(name: string) {
  if (!name) { state.libraryScope = null; return; }
  const next = new Set(activeLibrarySet());
  if (next.has(name)) next.delete(name); else next.add(name);
  const all = packageLibraries();
  if (next.size === 0 || next.size === all.length) state.libraryScope = null;
  else state.libraryScope = next;
}

// Reset the type cursor/selection to the first in-scope type after the library
// scope changes, keeping the current namespace/kind filters.
function normalizeLibrarySelection() {
  state.typeCursor = 0;
  const first = filteredTypes()[0];
  state.selectedTypeId = first?.id || "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberFilters();
}

function afterLibraryScopeChange() {
  normalizeLibrarySelection();
  renderPreservingMemberFocus();
}

// The Library selector for the type nav pane. Mirrors the framework controls:
// chips (multi-select, all on by default — the inverse of the single-select
// framework chips) for a handful of libraries, a single-select dropdown once a
// package (e.g. the runtime pack) carries too many.
function libraryControl() {
  if (state.package?.isRuntimePack) {
    const select = platformLibrarySelectHtml();
    return select ? `<div class="library-picker platform-library-picker">${select}</div>` : "";
  }
  const mode = libraryMode();
  if (mode === "none") return "";
  const libs = packageLibraries();
  if (mode === "dropdown") {
    const only = state.libraryScope && state.libraryScope.size === 1
      ? [...state.libraryScope][0] : "";
    const total = libs.reduce((sum, lib) => sum + lib.count, 0);
    return `<div class="library-picker">
      <select id="library-jump" class="scope-select" aria-label="Scope to a library">
        <option value="" ${!only ? "selected" : ""}>All libraries · ${total}</option>
        ${libs.map(lib => `<option value="${escapeHtml(lib.name)}" ${only === lib.name ? "selected" : ""}>${escapeHtml(lib.name)} · ${lib.count}</option>`).join("")}
      </select>
    </div>`;
  }
  const active = activeLibrarySet();
  const allOn = !state.libraryScope;
  const chips = libs
    .map(lib => `<button class="${active.has(lib.name) ? "active" : ""}" data-library-chip="${escapeHtml(lib.name)}" title="${escapeHtml(lib.name)}"><span class="ns-count">${lib.count}</span>${escapeHtml(lib.name)}</button>`)
    .join("");
  return `<div class="namespace-chips library-chips" aria-label="Library filters">
    <button class="${allOn ? "active" : ""}" data-library-chip="">all libraries</button>
    ${chips}
  </div>`;
}

function namespaces() {
  if (!state.package) return [];
  return [...new Set(state.package.types
    .filter(item => state.accessibilityFilter.has(item.accessibilityId))
    .map(item => item.namespace))];
}

function accessibilityBuckets() {
  return state.package?.accessibility ?? [];
}

function defaultAccessibilityFilter(pkg: AppPackage | null | undefined): Set<string> {
  return new Set((pkg?.accessibility ?? [])
    .filter(descriptor => descriptor.isDefault)
    .map(descriptor => descriptor.id));
}

function revealTypeInFilters(type: AppTypeSurface | null | undefined) {
  if (!type) return;
  state.accessibilityFilter = accessibilityFilterIncludingType(
    state.accessibilityFilter,
    type);
  if (!typeMatchesFilterText(type, state.typeFilter.toLowerCase()))
    state.typeFilter = "";
  if (state.namespaceFilter && type.namespace !== state.namespaceFilter)
    state.namespaceFilter = "";
  if (state.kindFilter && typeKind(type.kind) !== state.kindFilter)
    state.kindFilter = "";
  if (state.libraryScope && !state.libraryScope.has(libraryKey(type)))
    state.libraryScope = null;
}

function packageIdentityEquals(
  left: PackageIdentity | null | undefined,
  right: PackageIdentity | null | undefined,
) {
  return Boolean(left && right && packageIdentityKey(left) === packageIdentityKey(right));
}

function retainPackageModel(
  packageModel: AppPackage,
  replacedPackage: AppPackage | null = null,
) {
  const activeWasReplaced = packageIdentityEquals(state.package, packageModel);
  const retained = retainWorkspacePackage(
    state.packages,
    state.package,
    packageModel,
    replacedPackage);
  state.packages = retained.packages;
  if (activeWasReplaced)
    state.package = packageModel;

  for (const evicted of retained.evicted)
    releasePackageModelCaches(evicted);
}

function releasePackageModelCaches(packageModel: AppPackage) {
  const dependencyKey = workspaceDependencyKey(packageModel);
  delete state.workspaceDependencies[dependencyKey];
  delete state.workspaceDependencyErrors[dependencyKey];
  state.workspaceDependencyLoads.delete(dependencyKey);

  const id = packageModel.id.toLowerCase();
  if (!state.packages.some(item => item.id.toLowerCase() === id)) {
    delete state.packageVersions[id];
    delete state.packageVersionsLoading[id];
  }
}

function clearWorkspacePackages() {
  const discarded = state.packages;
  state.packages = [];
  state.package = null;
  state.workspaceShareBasis = null;
  for (const packageModel of discarded)
    releasePackageModelCaches(packageModel);
}

function resetLocationFilters() {
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.libraryScope = null;
  state.typeCursor = 0;
  resetMemberFilters();
}

function selectWorkspacePackage(
  pkg: PackageControlPackage | null,
  { stayInWorkspace = false }: { stayInWorkspace?: boolean } = {},
) {
  const packageModel = pkg
    ? state.packages.find(item => packageIdentityKey(item) === packageIdentityKey(pkg))
    : null;
  if (!packageModel) return;
  activatePackage(packageModel, { resetAccessibility: true });
  state.home = false;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.libraryScope = null;
  state.selectedTypeId = defaultVisibleTypeId(packageModel);
  reconcileAccessibilityFilter(
    packageModel.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberFilters();
  resetMemberSectionState();
  state.workspaceSubjectOpen = stayInWorkspace;
  render();
}

function workspaceOccurrenceRequest() {
  return state.packages
    .filter(item => !item.isRuntimePack)
    .map(item => ({
      package: item.id,
      version: item.version,
      framework: item.activeFramework,
    }));
}

function ensureWorkspaceOccurrenceView() {
  if (!state.engineReady) return;
  const signature = JSON.stringify(workspaceOccurrenceRequest());
  if (state.workspaceOccurrenceLoading) return;
  if (signature === state.workspaceOccurrenceSignature) return;

  state.workspaceOccurrenceSignature = signature;
  void queryWorkspaceOccurrenceView();
}

let workspaceOccurrenceRevision = 0;

async function queryWorkspaceOccurrenceView() {
  const signature = state.workspaceOccurrenceSignature;
  const revision = workspaceOccurrenceRevision;
  let superseded = false;
  state.workspaceOccurrenceLoading = true;
  state.workspaceOccurrenceError = "";
  try {
    const view = await inspectQueryWorkspacePackageOccurrences(signature);
    superseded = view.superseded;
    if (!superseded
      && revision === workspaceOccurrenceRevision
      && signature === state.workspaceOccurrenceSignature
      && signature === JSON.stringify(workspaceOccurrenceRequest())) {
      state.workspaceOccurrences = view;
    }
  } catch (error: unknown) {
    if (revision === workspaceOccurrenceRevision
      && signature === state.workspaceOccurrenceSignature
      && signature === JSON.stringify(workspaceOccurrenceRequest())) {
      state.workspaceOccurrences = null;
      state.workspaceOccurrenceError =
        error instanceof Error ? error.message : String(error);
    }
  } finally {
    const ownsCurrentRequest =
      revision === workspaceOccurrenceRevision
      && signature === state.workspaceOccurrenceSignature;
    if (ownsCurrentRequest) state.workspaceOccurrenceLoading = false;
    const desiredSignature = JSON.stringify(workspaceOccurrenceRequest());
    if (workspaceOccurrenceViewIsVisible()
      && !state.workspaceOccurrenceLoading
      && (superseded
        || state.workspaceOccurrenceSignature !== desiredSignature)) {
      state.workspaceOccurrenceSignature = "";
      ensureWorkspaceOccurrenceView();
    }
    render();
  }
}

function retryWorkspaceOccurrenceView() {
  state.workspaceOccurrenceSignature = "";
  ensureWorkspaceOccurrenceView();
  render();
}

function clearWorkspaceOccurrenceView() {
  inspectClearWorkspacePackageOccurrences();
  workspaceOccurrenceRevision++;
  state.workspaceOccurrenceSignature = "";
  state.workspaceOccurrenceLoading = false;
  state.workspaceOccurrences = null;
  state.workspaceOccurrenceError = "";
}

function workspaceOccurrenceViewIsVisible() {
  return workspaceOccurrenceActionsAreVisible({
    engineReady: state.engineReady,
    scope: scope(),
    explorerOpen: state.explorer?.open === true,
    creditsOpen: state.credits,
    packageQueryOpen: state.packageQueryOpen,
    loading: state.loading,
    error: state.error,
    home: state.home,
    hasPackage: state.package !== null,
  });
}

function activateWorkspacePackageOccurrence(action: string) {
  const result: BrowserWorkspacePackageOccurrenceActivation =
    inspectActivateWorkspacePackageOccurrence(action);
  if (!result.activated || !result.package) {
    state.workspaceOccurrenceSignature = "";
    ensureWorkspaceOccurrenceView();
    showToast(
      result.superseded
        ? "That Workspace view was replaced. Package actions have been refreshed."
        : "The package occurrence could not be activated.",
    );
    return;
  }

  const packageModel = createNuGetPackageModel(result.package);
  retainPackageModel(packageModel);
  selectWorkspacePackage(packageModel);
}

function activatePackage(
  pkg: AppPackage,
  { resetAccessibility = false }: { resetAccessibility?: boolean } = {},
) {
  const changed = !packageIdentityEquals(state.package, pkg);
  state.workspaceSubjectOpen = false;
  state.package = pkg;
  if (changed)
    state.dependenciesGroupIndex = null;
  if (changed) {
    state.memberBrowseTypeId = "";
    resetMemberFilters();
  }
  if (pkg && (changed || resetAccessibility || state.accessibilityFilter.size === 0))
    state.accessibilityFilter = defaultAccessibilityFilter(pkg);
  return changed;
}

function isDefaultAccessibility(type: InspectedTypeSurface) {
  return Boolean(state.package?.accessibility?.some(
    descriptor => descriptor.isDefault && descriptor.id === type.accessibilityId));
}

// Multi-select chip toggle for the accessibility filter. An empty bucket
// selects every bucket; otherwise, an empty result falls back to the "public"
// default so the type list is never blanked out.
function toggleAccessibilityChip(bucket: string) {
  if (!bucket) {
    state.accessibilityFilter =
      new Set(accessibilityBuckets().map(descriptor => descriptor.id));
    return;
  }
  const next = new Set(state.accessibilityFilter);
  if (next.has(bucket)) next.delete(bucket); else next.add(bucket);
  if (next.size === 0) {
    for (const id of defaultAccessibilityFilter(state.package)) next.add(id);
  }
  state.accessibilityFilter = next;
}

// The accessibility selector for the type nav pane: a multi-select chip row
// (public on by default) that surfaces the package's non-public types on demand.
// Rendered only when the package carries more than the public bucket.
function accessibilityControl() {
  const buckets = accessibilityBuckets();
  if (buckets.length <= 1) return "";
  const allOn = buckets.every(
    bucket => state.accessibilityFilter.has(bucket.id));
  const chips = buckets
    .map(bucket => `<button class="${state.accessibilityFilter.has(bucket.id) ? "active" : ""}" data-access-chip="${escapeHtml(bucket.id)}">${escapeHtml(bucket.label)}</button>`)
    .join("");
  return `<div class="namespace-chips access-chips" aria-label="Accessibility filters">
    <button class="${allOn ? "active" : ""}" data-access-chip="">all access</button>
    ${chips}
  </div>`;
}

function typeFilterSummary() {
  const buckets = accessibilityBuckets();
  const activeAccessibility =
    buckets.filter(bucket => state.accessibilityFilter.has(bucket.id));
  const accessibilitySummary = activeAccessibility.length === buckets.length
    ? ""
    : activeAccessibility
      .map(bucket => bucket.label.toLowerCase())
      .join(", ");
  return [
    state.typeFilter,
    state.namespaceFilter,
    state.kindFilter,
    accessibilitySummary,
  ].filter(Boolean).join(" · ") || "All types";
}

// Options for the namespace picker dropdown: every namespace in the active
// package (honoring the library + accessibility filters), sorted, with its type
// count.
function namespaceOptions() {
  if (!state.package) return "";
  const counts = new Map<string, number>();
  for (const item of state.package.types) {
    if (state.libraryScope && !state.libraryScope.has(libraryKey(item))) continue;
    if (!state.accessibilityFilter.has(item.accessibilityId)) continue;
    counts.set(item.namespace, (counts.get(item.namespace) || 0) + 1);
  }
  return [...counts.keys()]
    .sort((a, b) => a.localeCompare(b))
    .map(ns => `<option value="${escapeHtml(ns)}" ${state.namespaceFilter === ns ? "selected" : ""}>${escapeHtml(ns || "(global namespace)")} · ${counts.get(ns)}</option>`)
    .join("");
}

// Collapse a raw kind string ("sealed class", "readonly struct", "enum", …) to a
// primary bucket used by the kind filter chips.
type TypeKind = "class" | "struct" | "interface" | "enum" | "delegate";

function typeKind(kind: string): TypeKind {
  const value = (kind || "").toLowerCase();
  if (value.includes("interface")) return "interface";
  if (value.includes("delegate")) return "delegate";
  if (value.includes("enum")) return "enum";
  if (value.includes("struct")) return "struct";
  return "class";
}

const KIND_ORDER: readonly TypeKind[] =
  ["class", "struct", "interface", "enum", "delegate"];

// Kind buckets present in the current package, honoring the active namespace filter
// (but not the kind filter itself, so chips stay stable while one is selected).
function typeKinds() {
  if (!state.package) return [];
  const present = new Set(state.package.types
    .filter(item => !state.namespaceFilter || item.namespace === state.namespaceFilter)
    .filter(item => !state.libraryScope || state.libraryScope.has(libraryKey(item)))
    .filter(item => state.accessibilityFilter.has(item.accessibilityId))
    .map(item => typeKind(item.kind)));
  return KIND_ORDER.filter(kind => present.has(kind));
}


function typeGroups() {
  const groups = new Map<string, InspectedTypeSurface[]>();
  for (const item of filteredTypes()) {
    let group = groups.get(item.namespace);
    if (!group) {
      group = [];
      groups.set(item.namespace, group);
    }
    group.push(item);
  }
  return groups;
}

function memberGroups(
  type: AppTypeSurface | null | undefined,
): AppMemberGroup[] {
  const groups = new Map<string, AppMemberGroup>();
  for (const member of type?.api ?? []) {
    const key =
      `${member.graphOnly ? "graph:" : ""}${member.kind}:${member.name}`;
    let group = groups.get(key);
    if (!group) {
      group = { key, name: member.name, kind: member.kind, overloads: [] };
      groups.set(key, group);
    }
    group.overloads.push(member);
  }
  return [...groups.values()];
}

function memberFilterState() {
  return {
    query: state.memberTextFilter,
    kind: state.memberKindFilter,
    accessibility: state.memberAccessibilityFilter,
    trait: state.memberTraitFilter
  };
}

function resetMemberFilters() {
  state.memberKindFilter = "all";
  state.memberAccessibilityFilter = "all";
  state.memberTraitFilter = "";
  state.memberTextFilter = "";
}

function visibleMemberGroups(type: AppTypeSurface) {
  return filterMemberGroups(publicMemberGroups(type), memberFilterState());
}

function publicMemberGroups(type: AppTypeSurface) {
  return searchableMemberGroups(memberGroups(type));
}

function selectedGraphMemberGroup(type: AppTypeSurface) {
  return memberGroups(type).find(group =>
    group.key === state.selectedMemberKey
    && group.overloads.some(overload => overload.graphOnly));
}

function memberSelectionIsAvailable(
  type: AppTypeSurface,
  visible: readonly { key: string }[],
) {
  return visible.some(group => group.key === state.selectedMemberKey)
    || selectedGraphMemberGroup(type) != null;
}

function memberKinds(type: AppTypeSurface) {
  return [...new Set(publicMemberGroups(type).map(group => group.kind))];
}

function memberAccessibilities(type: AppTypeSurface) {
  const values = new Set(
    publicMemberGroups(type)
      .flatMap(group => group.overloads)
      .map(member => member.accessibility));
  return ["public", "protected", "internal", "private", "protected internal", "private protected"]
    .filter(value => values.has(value))
    .concat([...values].filter(value => value && ![
      "public", "protected", "internal", "private", "protected internal", "private protected"
    ].includes(value)).sort());
}

function availableMemberTraits(type: AppTypeSurface) {
  const publicMembers =
    publicMemberGroups(type).flatMap(group => group.overloads);
  return MEMBER_TRAITS.filter(([property]) =>
    publicMembers.some(member => member[property]));
}

function renderMemberFilterControls(type: AppTypeSurface) {
  const kinds = memberKinds(type);
  const accessibilities = memberAccessibilities(type);
  const traits = availableMemberTraits(type);
  const activeTrait = traits.find(
    ([property]) => property === state.memberTraitFilter)?.[1];
  const filterSummary = [
    state.memberTextFilter ? `text: ${state.memberTextFilter}` : "",
    state.memberKindFilter === "all"
      ? ""
      : state.memberKindFilter.replaceAll("-", " "),
    state.memberAccessibilityFilter === "all"
      ? ""
      : state.memberAccessibilityFilter,
    activeTrait ?? "",
  ].filter(Boolean).join(" · ") || "All members";
  return `
    <details class="filter-disclosure member-filter-disclosure" data-member-filter-disclosure${state.memberFiltersExpanded ? " open" : ""}>
      <summary id="member-filter-summary"><span aria-hidden="true">›</span><strong>Filters</strong><small>${escapeHtml(filterSummary)}</small></summary>
      <div class="type-search member-search">
        <span aria-hidden="true">/</span>
        <input id="member-filter" aria-label="Filter members and signatures" value="${escapeHtml(state.memberTextFilter)}" placeholder="Filter members and signatures" autocomplete="off" spellcheck="false" />
        <button class="tiny-button" id="clear-member-filter" title="Clear member filters" aria-label="Clear member filters">×</button>
      </div>
      <div class="member-filter-stack">
        <div class="namespace-chips kind-chips" aria-label="Member kind filters">
          <button class="${state.memberKindFilter === "all" ? "active" : ""}" data-member-kind-filter="all" aria-pressed="${state.memberKindFilter === "all"}">all kinds</button>
          ${kinds.map(kind => `<button class="${state.memberKindFilter === kind ? "active" : ""}" data-member-kind-filter="${escapeHtml(kind)}" aria-pressed="${state.memberKindFilter === kind}">${escapeHtml(kind.replaceAll("-", " "))}</button>`).join("")}
        </div>
        ${accessibilities.length ? `<div class="namespace-chips access-chips" aria-label="Member accessibility filters">
          <button class="${state.memberAccessibilityFilter === "all" ? "active" : ""}" data-member-access-filter="all" aria-pressed="${state.memberAccessibilityFilter === "all"}">all access</button>
          ${accessibilities.map(accessibility => `<button class="${state.memberAccessibilityFilter === accessibility ? "active" : ""}" data-member-access-filter="${escapeHtml(accessibility)}" aria-pressed="${state.memberAccessibilityFilter === accessibility}">${escapeHtml(accessibility)}</button>`).join("")}
        </div>` : ""}
        ${traits.length ? `<div class="namespace-chips member-trait-chips" aria-label="Member trait filters">
          <button class="${!state.memberTraitFilter ? "active" : ""}" data-member-trait-filter="" aria-pressed="${!state.memberTraitFilter}">all traits</button>
          ${traits.map(([property, label]) => `<button class="${state.memberTraitFilter === property ? "active" : ""}" data-member-trait-filter="${property}" aria-pressed="${state.memberTraitFilter === property}">${label}</button>`).join("")}
        </div>` : ""}
      </div>
    </details>`;
}

function compositionFilterButton(
  count: number,
  label: string,
  attribute: string,
  value: string,
  className = "",
) {
  return `<button class="composition-filter ${className}" ${attribute}="${escapeHtml(value)}"><strong>${count}</strong><span>${escapeHtml(label)}</span></button>`;
}

function renderMemberComposition(type: AppTypeSurface) {
  const { publicMembers } = partitionGraphMembers(type.api);
  const publicSurface = { ...type, api: publicMembers };
  const kinds = memberKinds(publicSurface)
    .map(kind => compositionFilterButton(
      publicMembers.filter(member => member.kind === kind).length,
      kind.replaceAll("-", " "),
      "data-member-jump-kind",
      kind))
    .join("");
  const accessibilities = memberAccessibilities(publicSurface)
    .map(accessibility => compositionFilterButton(
      publicMembers.filter(member => member.accessibility === accessibility).length,
      accessibility,
      "data-member-jump-access",
      accessibility))
    .join("");
  const traits = availableMemberTraits(publicSurface)
    .map(([property, label]) => compositionFilterButton(
      publicMembers.filter(member => member[property]).length,
      label,
      "data-member-jump-trait",
      property,
      `flag-${label}`))
    .join("");
  if (!kinds && !accessibilities && !traits) return "";
  return `
    <div class="composition-filters" aria-label="Browse members by kind">${kinds}</div>
    ${accessibilities ? `<div class="composition-filters" aria-label="Browse members by accessibility">${accessibilities}</div>` : ""}
    ${traits ? `<div class="composition-filters" aria-label="Browse members by trait">${traits}</div>` : ""}`;
}

function selectedMember(type: AppTypeSurface | null | undefined) {
  return memberGroups(type).find(group => group.key === state.selectedMemberKey);
}

// Selection sits on a scope ladder: workspace, package, type, or member. Library joins
// this ladder when its product-issued navigation descriptors are available.
function scope(): WorkspaceScope {
  if (state.workspaceSubjectOpen && state.atPackageRoot) return "workspace";
  if (state.atPackageRoot) return "package";
  return memberScopeIsActive(state, selectedType()?.id) ? "member" : "type";
}

function selectScopeLensByIndex(index: number, workspaceScope: WorkspaceScope): void {
  if (workspaceScope === "workspace") {
    return;
  } else if (workspaceScope === "package") {
    const selected = packageLensesFor(state.package)[index];
    if (selected) {
      state.packageLens = selected[0];
      render();
    }
  } else if (workspaceScope === "type") {
    const selected = typeLensesFor(state.package)[index];
    if (selected) {
      state.lens = selected[0];
      render();
    }
  } else if (workspaceScope === "member") {
    const member = selectedMember(selectedType());
    const selected = member ? memberSectionsFor(member)[index] : undefined;
    if (selected) applyMemberSection(selected[0]);
  } else {
    assertNever(workspaceScope, "workspace scope");
  }
}

// The resident runtime pseudo-package (Microsoft.NETCore.App) has no NuGet nupkg, so the
// package lenses that fetch one would 404. Integrations and Opportunities scan a
// selected library through the content-backed platform workspace; dependencies remain
// package-only, while Analysis reports its explicit product-query gap.
function packageLensesFor(pkg: AppPackage | null) {
  if (!pkg?.isRuntimePack) return packageLenses;
  return packageLenses.filter(([id]) =>
    id === "overview" || id === "integrations" || id === "opportunities" || id === "analysis" || id === "metadata");
}

// The single platform library the Integrations/Opportunities/Analysis lenses scan: whatever
// is currently scoped (one library), else none — the lens then prompts the user to pick one.
// Identity is the bare assembly key (no .dll), matching libraryScope and the platform roster.
function scopedPlatformLibrary() {
  if (!state.package?.isRuntimePack) return null;
  if (state.libraryScope && state.libraryScope.size === 1)
    return state.libraryScope.values().next().value ?? null;
  return null;
}

// The nav pane reacts to context: types at the top level, or the current type's
// members (with the active member's overloads nested) once a member is open under
// the API lens. Both modes render into #type-list so keyboard/scroll logic is shared.
function navMode() {
  return memberScopeIsActive(state, selectedType()?.id) ? "member" : "type";
}

function memberSourceHasConcreteOverload() {
  const member = selectedMember(selectedType());
  return Boolean(
    member
    && selectedConcreteOverload(
      member.overloads,
      state.selectedOverloadIndex));
}

function memberSectionUsesWorkingSurface(section: MemberSection) {
  return section === "overview"
    || section === "call-graph"
    || section === "facts";
}

function currentSourceOperationKind() {
  return activeSourceOperationKind(
    state,
    memberSourceHasConcreteOverload());
}

function currentSourceReloadKind() {
  return sourceReloadKind(
    state,
    memberSourceHasConcreteOverload());
}

function clearMemberContentCache() {
  invalidateMemberDestinationWork(state);
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = null;
}

function resetMemberSectionState() {
  state.memberSection = "overview";
  clearMemberContentCache();
}

function retainMemberSectionIfSupported(member: AppMemberGroup | undefined) {
  if (!member
    || !memberSectionsFor(member).some(([id]) => id === state.memberSection)) {
    state.memberSection = "overview";
  }
}

function loadMemberSectionContent(id: MemberSection) {
  if (id === "source")
    observeAsync(loadSelectedMemberSource(), "Loading member source");
  else if (id === "annotated")
    observeAsync(loadSelectedMemberAnnotatedSource(), "Loading annotated member source");
  else if (id === "call-graph")
    observeAsync(loadSelectedMemberCallGraph(), "Loading the member call graph");
  else if (id === "facts")
    observeAsync(loadSelectedMemberFacts(), "Loading member facts");
  else if (id === "overview")
    observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
  else
    assertNever(id, "member section");
}

function openMemberGroup(key: string) {
  const type = selectedType();
  const preserveSection =
    state.memberBrowseTypeId === type?.id && Boolean(state.selectedMemberKey);
  const group = memberGroups(type).find(candidate => candidate.key === key);
  const graphOnlyTarget =
    group?.overloads.length === 1
      ? graphOnlyBodyTarget(group.overloads[0])
      : null;
  state.memberBrowseTypeId = type?.id ?? "";
  state.selectedMemberKey = key;
  state.selectedOverloadIndex = graphOnlyTarget ? 0 : null;
  clearMemberContentCache();
  state.selectedBodyTarget = graphOnlyTarget;
  if (!preserveSection) {
    state.memberSection = "overview";
  } else {
    const retainedSection = state.memberSection;
    let selectedFirstOverload = false;
    if (state.memberSection !== "overview"
      && group
      && group.overloads.length > 1
      && state.selectedOverloadIndex == null) {
      state.selectedOverloadIndex = 0;
      state.selectedBodyTarget = graphOnlyBodyTarget(group.overloads[0]);
      selectedFirstOverload = true;
    }
    retainMemberSectionIfSupported(group);
    if (selectedFirstOverload && state.memberSection !== retainedSection) {
      state.selectedOverloadIndex = null;
      state.selectedBodyTarget = null;
    }
  }
  loadMemberSectionContent(state.memberSection);
}

function enterMemberScope() {
  const type = selectedType();
  if (!type) return false;
  const groups = memberGroups(type);
  if (!groups.length) {
    state.memberBrowseTypeId = "";
    return false;
  }
  state.atPackageRoot = false;
  state.lens = "api";
  state.memberBrowseTypeId = type.id;
  const visible = visibleMemberGroups(type);
  if (!memberSelectionIsAvailable(type, visible)) {
    const first = visible[0];
    if (first) openMemberGroup(first.key);
    else {
      state.selectedMemberKey = "";
      state.selectedOverloadIndex = null;
      resetMemberSectionState();
    }
  }
  return true;
}

function normalizeMemberSelection() {
  const type = selectedType();
  if (!type || !state.selectedMemberKey) return;
  const visible = visibleMemberGroups(type);
  if (!memberSelectionIsAvailable(type, visible)) {
    state.memberBrowseTypeId = type.id;
    state.selectedMemberKey = "";
    state.selectedOverloadIndex = null;
    resetMemberSectionState();
  }
}

function openOverload(index: number) {
  const graphTarget = graphOnlyBodyTarget(
    selectedMember(selectedType())?.overloads[index]);
  state.selectedOverloadIndex = index;
  clearMemberContentCache();
  state.selectedBodyTarget = graphTarget;
  retainMemberSectionIfSupported(selectedMember(selectedType()));
  loadMemberSectionContent(state.memberSection);
}

// Switch the open member's section (Overview / Call graph / Facts / Source / Annotated) and
// kick off its lazy load. Shared by the scope-bar strip click and the 1—5 shortcut. If a
// multi-overload member is still on its picker, resolve the first overload so the section
// has content to show.
function applyMemberSection(id: MemberSection) {
  const member = selectedMember(selectedType());
  if (member && member.overloads.length > 1 && state.selectedOverloadIndex == null) {
    state.selectedOverloadIndex = 0;
    state.selectedBodyTarget = graphOnlyBodyTarget(member.overloads[0]);
  }
  if (state.memberSection === "call-graph" && id !== "call-graph") {
    invalidateMemberCallGraphWork(state);
  }
  state.memberSection = id;
  loadMemberSectionContent(id);
}

// Flattened, ordered nav rows for member mode: filtered public groups plus the selected
// graph-only target, with active overloads nested beneath their group. This is the exact
// list ↑/↓ walks.
function memberNavEntries(type: AppTypeSurface): MemberNavEntry[] {
  const entries: MemberNavEntry[] = [];
  for (const group of visibleMemberGroups(type)) {
    entries.push({ kind: "member", group });
    if (group.key === state.selectedMemberKey && group.overloads.length > 1) {
      group.overloads.forEach((_, index) => entries.push({ kind: "overload", group, index }));
    }
  }
  const graphGroup = selectedGraphMemberGroup(type);
  if (graphGroup) {
    entries.push({ kind: "member", group: graphGroup });
    if (graphGroup.overloads.length > 1) {
      graphGroup.overloads.forEach(
        (_, index) => entries.push({ kind: "overload", group: graphGroup, index }));
    }
  }
  return entries;
}

function memberNavCursor(entries: readonly MemberNavEntry[]) {
  return entries.findIndex(entry => {
    if (entry.kind === "overload") {
      return entry.group.key === state.selectedMemberKey && state.selectedOverloadIndex === entry.index;
    }
    const isMulti = entry.group.overloads.length > 1;
    return entry.group.key === state.selectedMemberKey && (isMulti ? state.selectedOverloadIndex == null : true);
  });
}

function selectMemberNavEntry(entry: MemberNavEntry, focusList: boolean) {
  const preservedFocus = captureMemberFocus(document);
  const replacementAuthority = captureContentFrameReplacementAuthority();
  if (entry.kind === "member") {
    if (entry.group.key === state.selectedMemberKey) {
      if (entry.group.overloads.length === 1) {
        render();
      } else {
        state.selectedOverloadIndex = null;
        clearMemberContentCache();
        render();
      }
    } else {
      openMemberGroup(entry.group.key);
    }
  } else {
    if (entry.group.key !== state.selectedMemberKey) state.selectedMemberKey = entry.group.key;
    openOverload(entry.index);
  }
  scheduleMemberFocusAfterRender(preservedFocus, replacementAuthority);
  requestAnimationFrame(() => {
    if (focusList) document.querySelector<HTMLElement>("#type-list")?.focus();
    document.querySelector("#type-list .selected")?.scrollIntoView({ block: "nearest" });
  });
}

function stepMemberNav(delta: number, focusList: boolean) {
  const type = selectedType();
  if (!type) return;
  const entries = memberNavEntries(type);
  if (!entries.length) return;
  const cursor = memberNavTargetIndex(memberNavCursor(entries), entries.length, delta);
  const entry = entries[cursor];
  if (entry) selectMemberNavEntry(entry, focusList);
}

// ↑/↓ always act on the visible nav list, whatever depth you are at.
function stepNav(delta: number) {
  if (navMode() === "member") stepMemberNav(delta, false);
  else stepTypeSelection(delta);
}

// ←/→ act on the horizontal tab strip at your depth: sections when a concrete
// overload is open, otherwise the lens strip.
function stepHorizontal(delta: number) {
  if (scope() === "workspace") return;
  if (state.atPackageRoot) {
    const strip = packageLensesFor(state.package);
    const index = strip.findIndex(([id]) => id === state.packageLens);
    const next = strip[(index + delta + strip.length) % strip.length];
    if (!next) return;
    state.packageLens = next[0];
    render();
    return;
  }
  const type = selectedType();
  const member = state.lens === "api" ? selectedMember(type) : null;
  if (scope() === "member" && !member) return;
  const overloadOpen = member && !(member.overloads.length > 1 && state.selectedOverloadIndex == null);
  if (overloadOpen) {
    const order = memberSectionsFor(member).map(([id]) => id);
    let index = order.indexOf(state.memberSection);
    if (index < 0) index = 0;
    const next = order[(index + delta + order.length) % order.length];
    if (next) applyMemberSection(next);
  } else {
    // `typeLensesFor` from main; the checked-index guard from this slice. `available`
    // can be empty, which is exactly the case the bare index read could not express.
    const available = typeLensesFor(state.package);
    const index = available.findIndex(([id]) => id === state.lens);
    const next = available[(index + delta + available.length) % available.length];
    if (!next) return;
    state.lens = next[0];
    render();
  }
}

// Enter drills one level deeper; Escape/Backspace pops back out.
function drillIn() {
  if (scope() === "workspace") {
    if (!state.package) return;
    state.workspaceSubjectOpen = false;
    state.atPackageRoot = true;
    render();
    return;
  }
  if (state.atPackageRoot) {
    state.atPackageRoot = false;
    showContentDetailAfterRender();
    render();
    return;
  }
  const type = selectedType();
  if (!type) return;
  if (navMode() === "type") {
    const focusGeneration = beginSpotlightNavigation();
    if (enterMemberScope()) {
      contentFramePane = "navigation";
      render();
      restoreContentNavigationFocus(focusGeneration);
    }
  } else {
    const member = selectedMember(type);
    if (member && member.overloads.length > 1 && state.selectedOverloadIndex == null) {
      showContentDetailAfterRender();
      openOverload(0);
    } else if (contentFrameUsesPush() && contentFrameMedia.matches) {
      showContentDetail();
    } else {
      document.querySelector<HTMLElement>(".detail-scroll")?.focus();
    }
  }
}

function drillOut() {
  if (navMode() === "member") {
    const member = selectedMember(selectedType());
    if (member && member.overloads.length > 1 && state.selectedOverloadIndex != null) {
      state.selectedOverloadIndex = null;
      resetMemberSectionState();
    } else {
      return exitMemberScope();
    }
    render();
    return true;
  }
  if (!state.atPackageRoot) {
    state.atPackageRoot = true;
    render();
    return true;
  }
  if (!state.workspaceSubjectOpen) {
    state.workspaceSubjectOpen = true;
    render();
    return true;
  }
  return false;
}

function exitMemberScope() {
  const focusGeneration = beginSpotlightNavigation();
  contentFramePane = "navigation";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberSectionState();
  render();
  restoreContentNavigationFocus(focusGeneration);
  return true;
}


// The C#-spelled type name for display (List<T>, Dictionary<TKey, TValue>). Identity —
// item.id / item.name — stays the metadata form for selection, search, and deep-links.
function typeDisplayName(
  item: { displayName?: string; name?: string } | null | undefined,
) {
  return item?.displayName || item?.name || "";
}

function render(options: { synchronizeUrl?: boolean } = {}) {
  sourceInspection.cancelHiddenRequest();
  const graphExplorerWasOpen = graphExplorer.isOpen;
  graphExplorer.beforeRender(callGraphExplorerKey());
  if (graphExplorerWasOpen && !graphExplorer.isOpen) {
    graphExplorerNavigationFocusPending = !state.settings && !state.keyboardHelp
      && !state.explorer?.open && !workbenchModalOwnsFocus();
  }
  if (graphExplorerNavigationFocusPending) {
    queueMicrotask(restoreGraphExplorerNavigationFocus);
  }
  if (!workspaceOccurrenceViewIsVisible()
    && (state.workspaceOccurrenceSignature
      || state.workspaceOccurrences)) {
    clearWorkspaceOccurrenceView();
  }
  document.body.classList.remove("package-query-route");
  const applicationMenuHadFocus = applicationMenuOwnsFocus(document);
  const focusedElement = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null;
  contentFrameFocusOwner = null;
  contentFrameReplacementAuthority = null;
  const scopeBarOwnsFocus = focusedElement
    ?.closest("[data-scope-bar], [data-application-scope-strip]") != null;
  const scopeBarFocus = focusedElement
    ? captureScopeBarFocus(focusedElement)
    : null;
  const workspaceFocus = captureWorkspaceFocus(focusedElement);
  const workbenchSearchHadFocus = focusedElement?.id === "open-search";
  const levelOneHeadingHadFocus =
    focusedElement?.matches("main h1") === true;
  scopeBarBinding?.disconnect();
  workbenchShellBinding?.disconnect();
  workbenchShellBinding = null;

  // The Metadata Explorer is a full-bleed "browse the database" view layered over the
  // package workbench. Like Settings it owns no URL and renders first, returning to the
  // Metadata lens on close.
  if (state.explorer?.open) {
    loadingBotSrc = null;
    renderMetadataExplorer();
    return;
  }
  if (state.credits) {
    loadingBotSrc = null;
    renderCreditsView();
    return;
  }
  if (state.packageQueryOpen
    && state.engineReady
    && !state.loading
    && !state.error) {
    document.body.classList.add("package-query-route");
    loadingBotSrc = null;
    renderPackageQueryPage();
    return;
  }
  packageQueryLiveAnnouncer.reset();
  // A loading/interstitial view holds one random bot for its whole appearance; any non-loading
  // view resets it so the next interstitial picks a fresh random bot (see interstitialBotSrc).
  const workspaceCatalogVisible =
    state.workspaceSubjectOpen
    && isProductHomeDemosPath(location.pathname)
    && state.engineReady;
  const showingInterstitial =
    state.loading
    || state.error
    || (!state.home && !state.package && !workspaceCatalogVisible);
  if (!showingInterstitial) loadingBotSrc = null;
  if (state.loading || state.error) {
    renderLoading();
    return;
  }
  retainFailedWorkspaceUrl();
  if (state.home) {
    renderHomeView();
    return;
  }
  if (!state.package) {
    if (workspaceCatalogVisible) {
      renderWorkspaceCatalogView();
      if (state.settings) {
        document.querySelector<HTMLElement>("#settings-title")
          ?.focus({ preventScroll: true });
      } else if (state.keyboardHelp) {
        document.querySelector<HTMLElement>("#keyboard-help-title")
          ?.focus({ preventScroll: true });
      } else if (applicationMenuHadFocus) {
        focusApplicationMenuButton(document);
      } else if (workspaceFocus) {
        restoreWorkspaceFocus(document, workspaceFocus);
      } else if (workbenchSearchHadFocus) {
        focusWorkbenchSearch(document);
      } else if (levelOneHeadingHadFocus) {
        focusLevelOneHeading();
      }
      if (scopeBarOwnsFocus) {
        let restored = false;
        if (scopeBarFocus) {
          scopeBarBinding?.revealFocusTarget(scopeBarFocus);
          restored = restoreScopeBarFocus(document, scopeBarFocus);
        }
        if (!restored) {
          document.querySelector<HTMLElement>(".brand")
            ?.focus({ preventScroll: true });
        }
        app.removeAttribute("tabindex");
      }
      restorePackageQueryReturnFocus();
      restorePackageQueryWorkspaceFocus();
      recordNav();
      return;
    }
    renderLoading();
    return;
  }
  const pkg = state.package;
  const current = selectedType();
  if (!current) {
    state.atPackageRoot = true;
    state.selectedTypeId = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
  } else if (state.selectedTypeId !== current.id) {
    state.selectedTypeId = current.id;
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
    resetMemberFilters();
    resetMemberSectionState();
  }
  const visible = filteredTypes();
  // Keep the package lens on something the active package actually supports, so a restored
  // URL or stale selection can neither render nor auto-load a lens that fetches a missing nupkg.
  if (state.atPackageRoot && !packageLensesFor(pkg).some(([id]) => id === state.packageLens)) {
    state.packageLens = "overview";
  }
  if (!state.atPackageRoot
    && scope() === "type"
    && !typeLensesFor(state.package).some(([id]) => id === state.lens)) {
    state.lens = "api";
  }
  state.typeCursor = Math.min(state.typeCursor, Math.max(visible.length - 1, 0));
  const activeScope = scope();
  const sourcePageKind =
    activeScope === "type" && state.lens === "source"
      ? "type"
      : activeScope === "member"
        && state.memberSection === "source"
        && memberSourceHasConcreteOverload()
        ? "member"
        : null;
  const currentTypeSourceSignature = current
    ? typeSourceSignature(
        current,
        currentPackage(),
        state.taste,
        memberRequestKey)
    : "";
  const sourcePageSource =
    sourcePageKind === "member"
      ? state.memberSource
      : sourcePageKind === "type"
        && state.typeSourceKey === currentTypeSourceSignature
        ? state.typeSource
        : null;
  const sourceWorkingSurface =
    sourcePageKind !== null && sourcePageSource !== null;
  const apiWorkingSurface =
    activeScope === "type" && state.lens === "api";
  const metadataWorkingSurface =
    activeScope === "type" && state.lens === "metadata";
  const packageDependenciesWorkingSurface =
    activeScope === "package" && state.packageLens === "dependencies";
  const packageMetadataWorkingSurface =
    activeScope === "package" && state.packageLens === "metadata";
  const currentMember = current ? selectedMember(current) : undefined;
  const memberOverloadPicker =
    currentMember !== undefined
    && currentMember.overloads.length > 1
    && !selectedConcreteOverload(
      currentMember.overloads,
      state.selectedOverloadIndex);
  const memberWorkingSurface =
    activeScope === "member"
    && current !== null
    && currentPendingGraphMember() === null
    && (memberOverloadPicker
      || memberSectionUsesWorkingSurface(state.memberSection));
  const annotatedPageContext =
    activeScope === "member"
    && state.memberSection === "annotated"
    && memberSourceHasConcreteOverload();
  const annotatedWorkingSurface =
    annotatedPageContext && state.memberAnnotatedEmbedded !== null;
  const callGraphPageContext =
    activeScope === "member" && state.memberSection === "call-graph";
  const subjectPath = currentInspectedSubjectPath();
  const subjectPathLabel = subjectPath.map(segment => segment.label).join(" > ");
  const inspectorPanelSemantics = hasEffectiveInspector()
    ? ' role="tabpanel" aria-labelledby="active-inspector-tab"'
    : "";
  const contentFrameEnabled = activeScope !== "workspace";
  const contentNavigationLabel =
    navMode() === "member" && current ? "Members" : "Types";
  const contentNavigationIntegrated =
    apiWorkingSurface
    || metadataWorkingSurface
    || packageDependenciesWorkingSurface
    || packageMetadataWorkingSurface
    || memberWorkingSurface;

  if (scopeBarOwnsFocus) {
    app.tabIndex = -1;
    app.focus({ preventScroll: true });
  }
  const applicationModalOpen = state.settings || state.keyboardHelp;
  app.innerHTML = `
    <div class="workbench"${state.memberAnnotatedModal || applicationModalOpen ? " inert" : ""}>
      ${workbenchShellHtml({
        applicationScopeHtml: renderApplicationScopeBar(
          activeScope === "workspace" ? "workspace" : null,
          true,
          escapeHtml),
        contextualActionsHtml: annotatedPageContext || sourcePageKind || callGraphPageContext
          ? `<div class="working-surface-actions" role="group" aria-label="${callGraphPageContext ? "Call graph actions" : annotatedPageContext ? "Annotated Source actions" : "Source actions"}">
              ${callGraphPageContext
                ? `<button type="button" id="call-graph-explore" data-graph-explore${currentCallGraph() && !currentCallGraph()?.noBody ? "" : " disabled"}>Explore</button>`
                : ""}
              ${annotatedPageContext
                ? renderAnnotatedSourcePageActions(annotatedWorkingSurface)
                : ""}
              ${sourcePageKind
                ? renderSourcePageActions({
                    source: sourcePageSource,
                    copyButtonId: sourcePageKind === "member"
                      ? "copy-source"
                      : "copy-type-source",
                    escapeHtml,
                  })
                : ""}
            </div>`
          : "",
        inspectedTargetHtml: `
          <div class="inspected-target" aria-label="Inspected target">
            ${renderInspectedSubjectIcon(pkg)}
            <div class="subject-path" aria-label="${escapeHtml(subjectPathLabel)}" title="${escapeHtml(subjectPathLabel)}">
              ${renderInspectedSubjectPath(subjectPath)}
            </div>
          </div>`,
        subjectInspectorHtml: renderScopeBar(),
        titleNavigationHtml: renderTitleNavigation(
          navigationHistory.canBack(),
          navigationHistory.canForward()),
      })}

      <div class="notice-stack">
        ${renderQueryNotice()}
        ${pkg.inspectionError
          ? `<div class="query-notice" role="alert">
              <span class="query-notice-glyph">⚠</span>
              <span class="query-notice-text">${escapeHtml(`${pkg.id}@${pkg.version}: ${pkg.inspectionError}`)}</span>
              <button id="dismiss-package-notice" type="button" aria-label="Dismiss">×</button>
            </div>`
          : ""}
      </div>

      <main id="subject-panel" class="workspace${contentFrameEnabled ? " content-frame" : ""}"
        ${contentFrameEnabled ? `data-content-pane="${contentFramePane}"` : ""}
        role="tabpanel" aria-labelledby="${activeScope === "workspace" ? "application-scope-workspace" : "active-subject-tab"}">
        ${renderNavPane(current, visible)}

        <section class="detail-pane${contentFrameEnabled
          ? contentNavigationIntegrated
            ? " content-navigation-integrated"
            : " content-navigation-separated"
          : ""}">
          ${contentFrameEnabled
            ? renderContentNavigationBar(contentNavigationLabel)
            : ""}
          <article id="inspector-panel" class="detail-scroll${annotatedWorkingSurface ? " annotated-working-surface" : ""}${sourceWorkingSurface ? " source-working-surface" : ""}${apiWorkingSurface ? " api-working-surface" : ""}${metadataWorkingSurface ? " metadata-working-surface" : ""}${packageDependenciesWorkingSurface ? " package-dependencies-working-surface" : ""}${packageMetadataWorkingSurface ? " package-metadata-working-surface" : ""}${memberWorkingSurface ? " member-working-surface" : ""}"${inspectorPanelSemantics}>
            ${renderLens(current)}
          </article>
        </section>
      </main>

      ${statusBarHtml({
        buildIdentity: state.buildIdentity,
        diagnostics: state.diag,
        packageCache: state.packageCacheStats,
        source: pkg.source,
        assembly: current?.assembly ?? pkg.assembly,
        framework: pkg.activeFramework,
        expanded: state.statusBarExpanded,
      }, escapeHtml)}
      ${state.spotlightOpen ? spotlight.modalHtml() : ""}
      ${state.graphSourceOpen ? renderGraphSource() : ""}
      ${state.docViewerOpen ? renderDocViewer() : ""}
    </div>
    ${renderApplicationMenu(true)}
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp
      ? renderKeyboardHelpDialog(keyboardHelpBindings)
      : ""}
    ${renderAnnotatedSourceModal()}`;

  const packageIcon =
    document.querySelector<HTMLImageElement>("[data-package-icon]");
  if (packageIcon) {
    packageIcon.onerror = () => {
      if (packageIcon.getAttribute("src") === NUGET_DEFAULT_PACKAGE_ICON) return;
      packageIcon.src = NUGET_DEFAULT_PACKAGE_ICON;
    };
  }
  bindEvents();
  if (state.settings) {
    document.querySelector<HTMLElement>("#settings-title")
      ?.focus({ preventScroll: true });
  } else if (state.keyboardHelp) {
    document.querySelector<HTMLElement>("#keyboard-help-title")
      ?.focus({ preventScroll: true });
  } else if (applicationMenuHadFocus) {
    focusApplicationMenuButton(document);
  } else if (workspaceFocus) {
    restoreWorkspaceFocus(document, workspaceFocus);
  } else if (workbenchSearchHadFocus) {
    focusWorkbenchSearch(document);
  } else if (levelOneHeadingHadFocus) {
    focusLevelOneHeading();
  }
  if (scopeBarOwnsFocus) {
    let restored = false;
    if (scopeBarFocus) {
      scopeBarBinding?.revealFocusTarget(scopeBarFocus);
      restored = restoreScopeBarFocus(document, scopeBarFocus);
    }
    if (!restored) {
      document.querySelector<HTMLElement>(".brand")
        ?.focus({ preventScroll: true });
    }
    app.removeAttribute("tabindex");
  }
  restorePackageQueryReturnFocus();
  restorePackageQueryWorkspaceFocus();
  graphExplorer.afterRender(callGraphExplorerTarget());
  recordNav();
  const productDemosRouteVisible =
    scope() === "workspace"
    && isProductHomeDemosPath(location.pathname);
  if (productDemosRouteVisible) {
    document.title = "Demos — dotnet-inspect";
  } else if (options.synchronizeUrl !== false) {
    syncUrl();
  }
  maybeAutoLoadVisibleSource();
  maybeAutoLoadTypeMetadata();
  maybeAutoLoadPackageDependencies();
  maybeAutoLoadPackageIntegrations();
  maybeAutoLoadPackageOpportunities();
  maybeAutoLoadPackagePerformance();
  maybeAutoLoadPackageMetadata();
  if (scope() === "member"
    && state.memberSection === "call-graph"
    && currentCallGraph()?.mermaid) {
    observeAsync(renderMermaidCallGraph(), "Rendering the member call graph");
  }
}

function renderWorkspaceCatalogView() {
  document.title = "Demos — dotnet-inspect";
  const subjectPath: readonly SubjectPathSegment[] = [{
    kind: "workspace",
    label: "Workspace",
    copyable: false,
  }];
  app.innerHTML = `
    <div class="workbench"${state.settings || state.keyboardHelp ? " inert" : ""}>
      ${workbenchShellHtml({
        applicationScopeHtml: renderApplicationScopeBar(
          "workspace",
          true,
          escapeHtml),
        inspectedTargetHtml: `
          <div class="inspected-target" aria-label="Inspected target">
            <span class="subject-icon" aria-hidden="true">W</span>
            <div class="subject-path" aria-label="Workspace" title="Workspace">
              ${renderInspectedSubjectPath(subjectPath)}
            </div>
          </div>`,
        subjectInspectorHtml: renderScopeBar(["workspace"]),
        titleNavigationHtml: renderTitleNavigation(
          navigationHistory.canBack(),
          navigationHistory.canForward()),
      })}
      <div class="notice-stack">
        ${renderQueryNotice()}
      </div>
      <main id="subject-panel" class="workspace" role="tabpanel" aria-labelledby="application-scope-workspace">
        ${renderWorkspaceNavPane()}
        <section class="detail-pane">
          <article id="inspector-panel" class="detail-scroll">
            ${renderWorkspaceView()}
          </article>
        </section>
      </main>
      ${statusBarHtml({
        buildIdentity: state.buildIdentity,
        diagnostics: state.diag,
        packageCache: state.packageCacheStats,
        expanded: state.statusBarExpanded,
      }, escapeHtml)}
      ${state.spotlightOpen ? spotlight.modalHtml() : ""}
    </div>
    ${renderApplicationMenu(false)}
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp
      ? renderKeyboardHelpDialog(keyboardHelpBindings)
      : ""}`;
  bindStatusBarEvents();
  bindScopeBarEvents();
  bindWorkspaceSubjectEvents();
  bindSettingsPanelEvents();
  workbenchShellBinding =
    bindWorkbenchShell(document, workbenchShellActions);
  if (state.spotlightOpen) spotlight.bind(document, "modal");
}

function maybeAutoLoadVisibleSource() {
  const kind = currentSourceOperationKind();
  if (kind === "graph") {
    if (state.graphSourceRequest
      && sourceRequestNeedsLoad(
        true,
        state.graphSourceLoading,
        state.graphSource,
        state.graphSourceError)) {
      observeAsync(
        openGraphSource(
          state.graphSourceRequest.request,
          state.graphSourceRequest.title),
        "Loading graph source");
    }
    return;
  }
  const type = selectedType();
  if (!type) return;
  const pkg = currentPackage();
  if (kind === "type") {
    const signature = typeSourceSignature(type, pkg, state.taste, memberRequestKey);
    if (sourceRequestNeedsLoad(
        state.typeSourceKey === signature,
        state.typeSourceLoading,
        state.typeSource,
        state.typeSourceError)) {
      observeAsync(loadSelectedTypeSource(), "Loading type source");
    }
    return;
  }
  if (kind === "member") {
    const member = selectedMember(type);
    const overload = member
      ? selectedConcreteOverload(member.overloads, state.selectedOverloadIndex)
      : undefined;
    if (!member || !overload) return;
    const signature = memberRequestSignature(type, overload, false, true);
    if (sourceRequestNeedsLoad(
        state.memberSourceKey === signature,
        state.memberSourceLoading,
        state.memberSource,
        state.memberSourceError)) {
      observeAsync(loadSelectedMemberSource(), "Loading member source");
    }
  }
}

function maybeAutoLoadTypeMetadata() {
  if (state.lens !== "metadata") return;
  const type = selectedType();
  if (!type) return;
  const signature = typeMetadataSignature(type, currentPackage());
  if (state.typeMetadataKey === signature) {
    if (state.typeMetadata && state.typeMetadata.graphNodes.length > 1)
      observeAsync(renderTypeGraph(), "Rendering the type graph");
    return;
  }
  observeAsync(loadSelectedTypeMetadata(), "Loading type metadata");
}

function renderNavPane(
  current: AppTypeSurface | null | undefined,
  visible: readonly AppTypeSurface[],
) {
  if (scope() === "workspace") return renderWorkspaceNavPane();
  return navMode() === "member" && current
    ? renderMemberNavPane(current)
    : renderTypeNavPane(current, visible);
}

type SubjectPathKind = "workspace" | "package" | "type" | "member";

interface SubjectPathSegment {
  kind: SubjectPathKind;
  label: string;
  copyable: boolean;
}

function inspectedSubjectPath(
  pkg: AppPackage,
  current: AppTypeSurface | null | undefined,
): readonly SubjectPathSegment[] {
  if (scope() === "workspace") {
    return [{
      kind: "workspace",
      label: "Workspace",
      copyable: false,
    }];
  }
  const path: SubjectPathSegment[] = [{
    kind: "package",
    label: packageDisplayName(pkg),
    copyable: true,
  }];
  if (state.atPackageRoot || !current) return path;
  path.push({
    kind: "type",
    label: current.namespace
      ? `${current.namespace}.${typeDisplayName(current)}`
      : typeDisplayName(current),
    copyable: true,
  });
  const member = scope() === "member" ? selectedMember(current) : null;
  if (member) {
    path.push({
      kind: "member",
      label: member.name,
      copyable: true,
    });
  }
  return path;
}

function currentInspectedSubjectPath(): readonly SubjectPathSegment[] {
  if (scope() === "workspace") {
    return [{
      kind: "workspace",
      label: "Workspace",
      copyable: false,
    }];
  }
  return state.package
    ? inspectedSubjectPath(state.package, selectedType())
    : [];
}

function renderInspectedSubjectPath(
  path: readonly SubjectPathSegment[],
): string {
  return path.map((segment, index) => {
    const root = index === 0 ? " root" : "";
    const current = index === path.length - 1 ? " current" : "";
    const separator = index === 0
      ? ""
      : '<span class="subject-path-separator" aria-hidden="true">&gt;</span>';
    const label = escapeHtml(segment.label);
    const content = segment.copyable
      ? `<button type="button" class="subject-path-segment${root}${current}" data-subject-copy="${index}" title="Copy ${label}" aria-label="Copy ${escapeHtml(segment.kind)} name ${label}">${label}</button>`
      : `<span class="subject-path-segment${root}${current}">${label}</span>`;
    return `${separator}${content}`;
  }).join("");
}

function renderWorkspaceNavPane() {
  return renderWorkspaceSubject({
    packageCount: state.packages.length,
    selected: state.workspaceSubjectOpen,
    escapeHtml,
  });
}

function renderTypeNavPane(
  current: AppTypeSurface | null | undefined,
  visible: readonly AppTypeSurface[],
) {
  return renderTypeNav({
    current: current ?? null,
    visible,
    typeGroups: typeGroups(),
    typeFilter: state.typeFilter,
    namespaceFilter: state.namespaceFilter,
    kindFilter: state.kindFilter,
    namespaceCount: namespaces().length,
    namespaceOptionsHtml: namespaceOptions(),
    kindFilters: typeKinds(),
    accessibilityControlHtml: accessibilityControl(),
    libraryControlHtml: libraryControl(),
    filtersExpanded: state.typeFiltersExpanded,
    filterSummary: typeFilterSummary(),
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });
}

function renderMemberNavPane(type: AppTypeSurface) {
  const visibleGroups = visibleMemberGroups(type);
  return renderMemberNav({
    type,
    entries: memberNavEntries(type),
    memberCount: publicMemberGroups(type).length,
    visibleMemberCount: visibleGroups.length,
    filterControlsHtml: renderMemberFilterControls(type),
    selectedMemberKey: state.selectedMemberKey,
    selectedOverloadIndex: state.selectedOverloadIndex,
    escapeHtml,
    typeDisplayName,
    shortKind,
    highlight,
  });
}

// The scope switcher + lens strip. The leading segmented control is the scope ladder —
// Package (whole package), Types (one public type), and Member (a member of that type).
// Member is available as soon as the selected type has members. Each segment is selectable
// and swaps the strip beside it:
//   package → package lenses   type → type lenses   member → member sections
// Keeping all three families of buttons on one strip means the member modes (Overview,
// Call graph, …) live here too instead of inside the detail pane.
function hasEffectiveInspector(): boolean {
  const sc = scope();
  if (sc === "workspace") return false;
  if (sc === "package") {
    return packageLensesFor(state.package)
      .some(([id]) => id === state.packageLens);
  }
  if (sc === "member") {
    const selected = selectedType();
    const member = selected && selectedMember(selected);
    return Boolean(member && memberSectionsFor(member)
      .some(([id]) => id === state.memberSection));
  }
  return typeLensesFor(state.package).some(([id]) => id === state.lens);
}

function packageLensPresentation(
  id: PackageLens,
): string {
  switch (id) {
    case "overview": return "◫";
    case "dependencies": return "⇄";
    case "integrations": return "⌁";
    case "opportunities": return "◇";
    case "analysis": return "∿";
    case "metadata": return "≡";
    default: return assertNever(id, "package lens presentation");
  }
}

function typeLensPresentation(
  id: TypeLens,
): string {
  switch (id) {
    case "api": return "⌘";
    case "metadata": return "≡";
    case "source": return "⌑";
    default: return assertNever(id, "type lens presentation");
  }
}

function memberSectionPresentation(
  id: MemberSection,
): string {
  switch (id) {
    case "overview": return "◫";
    case "call-graph": return "⑂";
    case "facts": return "·";
    case "source": return "⌑";
    case "annotated": return "✎";
    default: return assertNever(id, "member section presentation");
  }
}

function scopeBarInspectorDefinitions<TId extends string>(
  definitions: readonly (readonly [TId, string])[],
  presentation: (id: TId) => string,
): readonly (readonly [TId, string, string, string])[] {
  return definitions.map(([id, label]) => {
    return [id, label, scopeBarShortLabel(label), presentation(id)];
  });
}

function renderScopeBar(
  availableScopes?: readonly WorkspaceScope[],
) {
  const sc = scope();
  const selected = selectedType();
  const showMemberScope =
    !state.atPackageRoot && Boolean(selected && memberGroups(selected).length);
  if (sc === "workspace") {
    return renderScopeBarPure({
      scope: sc,
      strip: [],
      activeStripId: null,
      stripAttribute: "data-workspace-lens",
      panelId: "inspector-panel",
      ...(availableScopes ? { availableScopes } : {}),
      showMemberScope,
      escapeHtml,
    });
  }
  if (sc === "package") {
    return renderScopeBarPure({
      scope: sc,
      strip: scopeBarInspectorDefinitions(
        packageLensesFor(state.package),
        packageLensPresentation),
      activeStripId: state.packageLens,
      stripAttribute: "data-package-lens",
      panelId: "inspector-panel",
      showMemberScope,
      escapeHtml,
    });
  }
  if (sc === "member") {
    const member = selectedMember(selected);
    return renderScopeBarPure({
      scope: sc,
      strip: scopeBarInspectorDefinitions(
        member ? memberSectionsFor(member) : [],
        memberSectionPresentation),
      activeStripId: state.memberSection,
      stripAttribute: "data-member-section",
      panelId: "inspector-panel",
      showMemberScope,
      emptyStripLabel: "Filtered member list",
      escapeHtml,
    });
  }
  if (sc === "type") {
    return renderScopeBarPure({
      scope: sc,
      // `typeLensesFor` rather than the raw catalog: a runtime pack offers only the API
      // lens, and reading the catalog directly here would skip that restriction.
      strip: scopeBarInspectorDefinitions(
        typeLensesFor(state.package),
        typeLensPresentation),
      activeStripId: state.lens,
      stripAttribute: "data-lens",
      panelId: "inspector-panel",
      showMemberScope,
      escapeHtml,
    });
  }
  // A new scope used to render silently as the type strip.
  return assertNever(sc, "workspace scope");
}

function packageHeading() {
  const pkg = currentPackage();
  return `<header class="type-heading">
    <div class="type-badge">${pkg.isRuntimePack ? "◎" : "⬡"}</div>
    <div>
      <div class="type-namespace">${pkg.isRuntimePack ? "Shared framework" : "NuGet package"}</div>
      <h1>${escapeHtml(packageDisplayName(pkg))}</h1>
      <code class="type-signature">${pkg.isRuntimePack ? `${escapeHtml(packageDisplayName(pkg))} · ${escapeHtml(pkg.version)}` : `${escapeHtml(pkg.id)}@${escapeHtml(pkg.version)}`}</code>
    </div>
    <div class="type-metrics"><span><strong>${pkg.totalTypes}</strong> types</span><span><strong>${pkg.totalMembers.toLocaleString()}</strong> members</span></div>
  </header>`;
}

function packageCoordinateFields() {
  const pkg = currentPackage();
  return `<label class="version-select">
    <span>Version</span>
    <select id="package-version">
      ${versionOptionsHtml(pkg)}
    </select>
  </label>
  <label class="framework-select">
    <span>Framework</span>
    <select id="framework"${pkg.frameworks.length <= 1 ? " disabled" : ""}>
      ${pkg.frameworks.map(item => `<option ${item === pkg.activeFramework ? "selected" : ""}>${escapeHtml(item)}</option>`).join("")}
    </select>
  </label>`;
}

function packageCoordinateControls() {
  const pkg = currentPackage();
  return `<section class="document-section package-coordinate-editor" aria-labelledby="package-coordinate-heading">
    <div class="section-title">
      <h2 id="package-coordinate-heading">Package coordinate</h2>
      <span>${pkg.frameworks.length} target framework${pkg.frameworks.length === 1 ? "" : "s"}</span>
    </div>
    <div class="package-coordinate-fields">${packageCoordinateFields()}</div>
  </section>`;
}

function renderPackageView() {
  const body = packageLensBody();
  if (state.packageLens === "dependencies"
    || state.packageLens === "metadata") return body;
  return `${packageHeading()}${packageCoordinateControls()}${body}`;
}

function renderWorkspaceView() {
  if (state.packages.length > 0) ensureWorkspaceOccurrenceView();
  return renderWorkspaceViewPure({
    occurrences: state.workspaceOccurrences?.occurrences ?? [],
    packages: state.packages,
    demos: productHomeDemoCatalog(),
    demoError: productHomeDemoCatalogError,
    loading: state.workspaceOccurrenceLoading,
    error: state.workspaceOccurrenceError,
    escapeHtml,
  });
}

function packageLensBody() {
  switch (state.packageLens) {
    case "overview": return renderPackageOverview();
    case "dependencies": return renderPackageDependencies();
    case "integrations": return renderPackageIntegrations();
    case "opportunities": return renderPackageOpportunities();
    case "analysis": return renderPackagePerformance();
    case "metadata": return renderPackageMetadata();
  }
  // `packageLenses` drives the rendered strip directly, so a new catalog entry is offered
  // to users the moment it is added. It used to fall through to a "not available"
  // placeholder here, which is indistinguishable from a lens that is wired but empty.
  return assertNever(state.packageLens, "package lens");
}

function packageDependenciesSignature() {
  const pkg = currentPackage();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}#${pkg.assemblyId}`;
}

function renderPackageDependenciesSurface(content: string, status: string) {
  const pkg = currentPackage();
  const coordinate = `${pkg.id}@${pkg.version}`;
  return `<section class="package-dependencies-surface" aria-labelledby="package-dependencies-surface-title">
    <header class="api-surface-head package-dependencies-surface-head">
      <h1 id="package-dependencies-surface-title">Dependencies</h1>
      <p data-package-dependencies-status>${escapeHtml(status)}</p>
    </header>
    <section class="package-dependencies-controls" aria-label="Dependency coordinate">
      <div class="package-coordinate-fields">${packageCoordinateFields()}</div>
    </section>
    <div class="package-dependencies-scroll">
      ${content}
    </div>
    <footer class="api-surface-footer package-dependencies-surface-footer">
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
      <span title="${escapeHtml(pkg.activeFramework)}">${escapeHtml(pkg.activeFramework)}</span>
    </footer>
  </section>`;
}

function packageDependenciesStatus(
  data: BrowserPackageDependencies,
  selectedGroupIndex: number | null,
) {
  const groups = data.dependencyGroups || [];
  const selectedGroup =
    groups.find(group => group.index === selectedGroupIndex) ?? groups[0];
  const dependencyCount = selectedGroup?.dependencies?.length ?? 0;
  const referenceCount = data.assemblyReferences?.length ?? 0;
  const referenceStatus = data.assemblyReferenceError
    ? "reference read failed"
    : `${referenceCount} reference${referenceCount === 1 ? "" : "s"}`;
  return `${dependencyCount} package${dependencyCount === 1 ? "" : "s"} · ${referenceStatus}`;
}

function renderPackageDependencies() {
  const current = packageDependenciesSignature();
  const fresh = state.packageDependenciesKey === current;
  if (state.packageDependenciesLoading && fresh) {
    return renderPackageDependenciesSurface(
      `<section class="document-section package-dependencies-state source-progress"><span class="loader"></span><h2>Reading dependencies…</h2><p>Parsing the package manifest and assembly references.</p></section>`,
      "reading");
  }
  if (fresh && state.packageDependenciesError) {
    return renderPackageDependenciesSurface(
      `<section class="document-section package-dependencies-state empty-document"><span class="large-glyph">⌘</span><h2>Dependency query failed</h2><p>${escapeHtml(state.packageDependenciesError)}</p></section>`,
      "query failed");
  }
  const data = fresh ? state.packageDependencies : null;
  if (!data) {
    return renderPackageDependenciesSurface(
      `<section class="document-section package-dependencies-state empty-document"><span class="loader"></span><h2>Loading…</h2></section>`,
      "loading");
  }

  const groups = data.dependencyGroups || [];
  const assemblyReferences = assemblyReferencesSectionHtml(data);
  const dependencyGroupError = dependencyGroupSelectionMessage(data);
  const dependencyGroupNotice = dependencyGroupError
    ? `<section class="document-section empty-document"><span class="large-glyph">△</span><h2>No exact dependency group</h2><p>${escapeHtml(dependencyGroupError)}</p></section>`
    : "";
  if (!groups.length) {
    return renderPackageDependenciesSurface(
      `${dependencyGroupNotice}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No package dependencies</h2><p>The manifest declares no NuGet dependencies — a self-contained package.</p></section>${assemblyReferences}`,
      packageDependenciesStatus(data, null));
  }

  const selectedGroupIndex = resolveDependenciesGroupIndex(groups);
  const orderedGroups = groups;
  const selectorChips = orderedGroups
    .map(group => `<button class="type-chip ${group.index === selectedGroupIndex ? "active" : ""}" data-dep-group="${group.index}">${escapeHtml(group.framework)}</button>`)
    .join("");
  const selector = `
    <section class="document-section">
      <div class="section-title"><h2>Target frameworks</h2><span>one framework at a time</span></div>
      <div class="type-chip-list" id="dep-tfm-chips">${selectorChips}</div>
    </section>`;

  const depList = dependencyListSectionHtml(groups, selectedGroupIndex);

  const graphSection = `
    <section class="document-section">
      <div class="section-title"><h2>Dependency graph</h2><span>callers above · dependencies below · click a package to open</span></div>
      ${workspaceDependencyErrorHtml()}
      <div id="dependency-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
    </section>`;

  return renderPackageDependenciesSurface(
    `${dependencyGroupNotice}${selector}${graphSection}${depList}${assemblyReferences}`,
    packageDependenciesStatus(data, selectedGroupIndex));
}

function assemblyReferencesSectionHtml(data: BrowserPackageDependencies) {
  const references = data.assemblyReferences || [];
  const assembly = data.assembly || "selected assembly";
  if (data.assemblyReferenceError) {
    return `
      <section class="document-section">
        <div class="section-title"><h2>Assembly references</h2><span>${escapeHtml(assembly)}</span></div>
        <div class="empty-list">Inspection failed: ${escapeHtml(data.assemblyReferenceError)}</div>
      </section>`;
  }

  return `
    <section class="document-section">
      <div class="section-title"><h2>Assembly references</h2><span>${escapeHtml(assembly)} · ${references.length} direct reference${references.length === 1 ? "" : "s"}</span></div>
      ${references.length
        ? `<ul class="dep-list">${references.map(reference =>
            `<li><span class="dep-name">${escapeHtml(reference.name)}</span><code class="dep-version">${escapeHtml(`${reference.version} · ${reference.culture || "neutral"} · ${reference.publicKeyToken ? `pkt ${reference.publicKeyToken}` : "unsigned"}`)}</code></li>`).join("")}</ul>`
        : `<div class="empty-list">This assembly declares no direct AssemblyRef rows.</div>`}
    </section>`;
}

function uniqueCompatiblePackage(
  packages: readonly AppPackage[],
  packageId: string,
  declaredRange: string | null | undefined,
) {
  const match = matchPackageDependencyCoordinate(
    packageId,
    declaredRange ?? null,
    JSON.stringify(dependencyCoordinateCandidates(packages)));
  if (match.outcome !== "Unique") return null;
  return packages.find(candidate =>
    packageIdentityKey(candidate) === match.candidateKey) || null;
}

// The NuGet dependency list for the selected TFM. Extracted so a framework switch can
// replace just this section in place instead of re-rendering the whole page (which would
// reset the dependency graph container to its loader and flash the diagram).
function dependencyListSectionHtml(
  groups: readonly BrowserPackageDependencyGroup[],
  selectedGroupIndex: number | null,
) {
  const group = groups.find(candidate => candidate.index === selectedGroupIndex) || groups[0];
  if (!group) throw new Error("Cannot render a dependency list without a dependency group.");
  const deps = group.dependencies || [];
  return `
    <section class="document-section" id="dep-list-section">
      <div class="section-title"><h2>NuGet dependencies</h2><span>${escapeHtml(group.framework)} · ${deps.length} package${deps.length === 1 ? "" : "s"}</span></div>
      ${deps.length
        ? `<ul class="dep-list">${deps.map(dependency => {
            const open = uniqueCompatiblePackage(
              state.packages,
              dependency.id,
              dependency.versionRange);
            const attrs = open
              ? `data-dep-open="${escapeHtml(packageIdentityKey(open))}" title="Switch to ${escapeHtml(dependency.id)}"`
              : `data-dep-load="${escapeHtml(dependency.id)}" data-dep-version="${escapeHtml(dependency.versionRange || "")}" title="Open ${escapeHtml(dependency.id)} in a new tab"`;
            return `<li><button class="dep-name as-link${open ? " is-open" : ""}" ${attrs}>${escapeHtml(dependency.id)}</button><code class="dep-version">${escapeHtml(dependency.versionRange || "*")}</code></li>`;
          }).join("")}</ul>`
        : `<div class="empty-list">No package dependencies declared for ${escapeHtml(group.framework)}.</div>`}
    </section>`;
}

// Switch the dependency lens to a different target framework without a full page render:
// toggle the active chip, swap the dependency list in place, and let renderDependencyGraph
// swap the diagram (it keeps the old SVG until the new one is ready, so no loader flash).
function patchDependenciesGroup() {
  const data = state.packageDependencies;
  const groups = data?.dependencyGroups || [];
  const listSection = document.querySelector<HTMLElement>("#dep-list-section");
  const status =
    document.querySelector<HTMLElement>("[data-package-dependencies-status]");
  if (!data || !groups.length || !listSection || !status) { render(); return; }
  const selectedGroupIndex = resolveDependenciesGroupIndex(groups);
  document.querySelectorAll<HTMLElement>("#dep-tfm-chips [data-dep-group]").forEach(button =>
    button.classList.toggle(
      "active",
      Number(button.dataset.depGroup) === selectedGroupIndex));
  status.textContent = packageDependenciesStatus(data, selectedGroupIndex);
  listSection.outerHTML = dependencyListSectionHtml(groups, selectedGroupIndex);
  bindPackageDependencyListEvents();
  observeAsync(renderDependencyGraph(), "Rendering the dependency graph");
}

function resolveDependenciesGroupIndex(
  groups: readonly BrowserPackageDependencyGroup[],
) {
  if (groups.some(group => group.index === state.dependenciesGroupIndex)) {
    return state.dependenciesGroupIndex;
  }
  const active = groups.find(group => group.isActive);
  return active?.index ?? groups[0]?.index ?? null;
}

const packageInspection = createPackageInspectionCoordinator({
  state,
  queryDependencies: packageModel => inspectPackageDependencies(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework,
    packageModel.assemblyId),
  queryPackageIntegrations: packageModel => inspectPackageIntegrations(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework),
  queryPlatformIntegrations: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) =>
    inspectPlatformIntegrations(
      framework,
      platformVersion,
      assemblyFileName,
      pack),
  queryPackageOpportunities: packageModel => inspectPackageOpportunities(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework),
  queryPlatformOpportunities: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) =>
    inspectPlatformOpportunities(
      framework,
      platformVersion,
      assemblyFileName,
      pack),
  queryPackagePerformance: packageModel => inspectPackagePerformance(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework),
  queryPlatformPerformance: async (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) =>
    parseEngineJson<PackagePerformance>(
      await inspectPlatformPerformance(
        framework,
        platformVersion,
        assemblyFileName,
        pack)),
  queryPackageMetadata: packageModel =>
    inspectPackageMetadata(
      packageModel.id,
      packageModel.version,
      packageModel.activeFramework),
  queryPlatformMetadata: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) =>
    inspectPlatformMetadata(
      framework,
      platformVersion,
      assemblyFileName,
      pack),
  platformPackForAssembly: assemblyName =>
    platformPackForAssembly(assemblyName) ?? "",
  describeError: errorMessage,
  refreshPackageStats,
  render: renderPreservingMemberFocus,
  renderDependencyGraph,
});

async function loadPackageDependencies() {
  return packageInspection.loadDependencies(
    currentPackage(),
    packageDependenciesSignature());
}

function maybeAutoLoadPackageDependencies() {
  if (!state.atPackageRoot || state.packageLens !== "dependencies") return;
  if (state.packageDependenciesKey === packageDependenciesSignature()) {
    if (state.packageDependencies) {
      observeAsync(renderDependencyGraph(), "Rendering the dependency graph");
      observeAsync(ensureWorkspaceDependencies(), "Loading workspace dependencies");
    }
    return;
  }
  observeAsync(loadPackageDependencies(), "Loading package dependencies");
}

// Fetches dependency manifests for every other open package so the dependency graph can
// draw incoming "caller" edges (open packages that declare a dependency on the current one).
async function ensureWorkspaceDependencies() {
  return packageInspection.ensureWorkspaceDependencies();
}

function workspaceDependencyErrorHtml() {
  const failures = state.packages
    .filter(item => !item.isRuntimePack)
    .map(item => {
      const key = workspaceDependencyKey(item);
      return state.workspaceDependencyErrors[key]
        ? `${item.id}@${item.version}: ${state.workspaceDependencyErrors[key]}`
        : null;
    })
    .filter(Boolean);
  return failures.length
    ? `<div class="graph-drill-error">Dependency workspace is incomplete: ${escapeHtml(failures.join("; "))}</div>`
    : "";
}

function packageIntegrationsSignature() {
  const pkg = currentPackage();
  const lib = scopedPlatformLibrary();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

function renderPackageIntegrations() {
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const scopedLib = scopedPlatformLibrary();
  // On the Platform the scan targets one library at a time (the whole shared framework is
  // ~160 assemblies). Offer a picker to switch libraries; when nothing is scoped yet, prompt
  // for a choice instead of scanning.
  const platformPicker = isPlatform
    ? `<section class="document-section"><div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml({ dataAttr: "data-platform-integrations-library", selected: scopedLib || "" })}</div></section>`
    : "";
  if (isPlatform && !scopedLib) {
    return `${platformPicker}<section class="document-section empty-document"><span class="large-glyph">◈</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to scan its public surface for DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting integration signals.</p></section>`;
  }
  const scanScope = isPlatform
    ? `${scopedLib} · ${escapeHtml(pkg.activeFramework)}`
    : escapeHtml(pkg.activeFramework);
  const current = packageIntegrationsSignature();
  const fresh = state.packageIntegrationsKey === current;
  if (state.packageIntegrationsLoading && fresh) {
    return `${platformPicker}<section class="document-section source-progress"><span class="loader"></span><h2>Scanning integrations…</h2><p>Reading the public surface of ${isPlatform ? escapeHtml(scopedLib) : "each assembly"} for ecosystem signals.</p></section>`;
  }
  if (fresh && state.packageIntegrationsError) {
    return `${platformPicker}<section class="document-section empty-document"><span class="large-glyph">◈</span><h2>Integration scan failed</h2><p>${escapeHtml(state.packageIntegrationsError)}</p></section>`;
  }
  const data = fresh ? state.packageIntegrations : null;
  if (!data) {
    return `${platformPicker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const categories = data.categories || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be scanned</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!categories.length) {
    return `${platformPicker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No ecosystem integrations detected</h2><p>The public surface of ${isPlatform ? escapeHtml(scopedLib) : escapeHtml(pkg.activeFramework)} shows no known DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting signals.</p></section>`;
  }

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Ecosystem integrations</h2><span>${categories.length} categor${categories.length === 1 ? "y" : "ies"} · ${data.totalSignals} signal${data.totalSignals === 1 ? "" : "s"} · ${scanScope}</span></div>
      <div class="type-chip-list">${categories.map(category => `<span class="type-chip">${escapeHtml(category.integration)} <span class="ns-count">${category.signals.length}</span></span>`).join("")}</div>
    </section>`;

  const blocks = categories.map(category => {
    const signals = [...category.signals].sort((a, b) => {
      const rank = (shape: string) => /type/i.test(shape) ? 0 : 1;
      return rank(a.shape) - rank(b.shape) || a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name);
    });
    const typeCount = signals.filter(signal => /type/i.test(signal.shape)).length;
    const apiCount = signals.length - typeCount;
    const rows = signals.map(signal => {
      const isType = /type/i.test(signal.shape);
      const { short, qualifier } = splitSignalName(signal.name);
      return `
        <div class="signal-row" title="${escapeHtml(signal.name)} · ${escapeHtml(signal.shape)} · ${escapeHtml(signal.kind)}">
          <span class="signal-badge signal-${isType ? "type" : "api"}">${isType ? "T" : "ƒ"}</span>
          <span class="signal-body"><span class="signal-name">${escapeHtml(short)}</span>${qualifier ? `<span class="signal-ns">${escapeHtml(qualifier)}</span>` : ""}</span>
          <span class="signal-kind">${escapeHtml(signal.kind)}</span>
        </div>`;
    }).join("");
    return `
    <section class="document-section">
      <div class="section-title"><h2>${escapeHtml(category.integration)}</h2><span>${typeCount} type${typeCount === 1 ? "" : "s"} · ${apiCount} API${apiCount === 1 ? "" : "s"}</span></div>
      <div class="signal-list">${rows}</div>
    </section>`;
  }).join("");

  return `${platformPicker}${warning}${summary}${blocks}`;
}

async function loadPackageIntegrations() {
  const pkg = currentPackage();
  const scopedLib = scopedPlatformLibrary();
  return packageInspection.loadIntegrations(
    pkg,
    packageIntegrationsSignature(),
    scopedLib);
}

function maybeAutoLoadPackageIntegrations() {
  if (!state.atPackageRoot || state.packageLens !== "integrations") return;
  if (state.packageIntegrationsKey === packageIntegrationsSignature()) return;
  observeAsync(loadPackageIntegrations(), "Loading package integrations");
}

function packageScopeSignature() {
  const pkg = currentPackage();
  const lib = scopedPlatformLibrary();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

// The Opportunities and Analysis lenses run over one platform library at a time, so on the
// Platform they render the same inline library picker as Integrations and prompt for a choice
// when nothing is scoped. This mirrors renderPackageIntegrations' platform handling.
function platformLensPicker(dataAttr: string) {
  const scopedLib = scopedPlatformLibrary();
  return `<section class="document-section"><div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml({ dataAttr, selected: scopedLib || "" })}</div></section>`;
}

function renderPackageOpportunities() {
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const scopedLib = scopedPlatformLibrary();
  const picker = isPlatform ? platformLensPicker("data-platform-opportunities-library") : "";
  const current = packageScopeSignature();
  return renderPackageOpportunitiesPure({
    isPlatform,
    scopedLibrary: scopedLib,
    activeFramework: pkg.activeFramework,
    picker,
    fresh: state.packageOpportunitiesKey === current,
    loading: state.packageOpportunitiesLoading,
    error: state.packageOpportunitiesError,
    data: state.packageOpportunities,
    escapeHtml,
  });
}

async function loadPackageOpportunities() {
  const pkg = currentPackage();
  const scopedLib = scopedPlatformLibrary();
  return packageInspection.loadOpportunities(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackageOpportunities() {
  if (!state.atPackageRoot || state.packageLens !== "opportunities") return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageOpportunitiesKey === packageScopeSignature()) return;
  observeAsync(loadPackageOpportunities(), "Loading package opportunities");
}

function renderPackagePerformance() {
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const scopedLib = scopedPlatformLibrary();
  const picker = isPlatform ? platformLensPicker("data-platform-analysis-library") : "";
  if (isPlatform && !scopedLib) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to classify allocation and performance opportunities across its method bodies.</p></section>`;
  }
  const scanScope = isPlatform
    ? `${escapeHtml(scopedLib)} · ${escapeHtml(pkg.activeFramework)}`
    : escapeHtml(pkg.activeFramework);
  const current = packageScopeSignature();
  const fresh = state.packagePerformanceKey === current;
  if (state.packagePerformanceLoading && fresh) {
    return `${picker}<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing allocations…</h2><p>Classifying allocation and performance opportunities across every method body.</p></section>`;
  }
  if (fresh && state.packagePerformanceError) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Analysis failed</h2><p>${escapeHtml(state.packagePerformanceError)}</p></section>`;
  }
  const data = fresh ? state.packagePerformance : null;
  if (!data) {
    return `${picker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const members = data.members || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be analyzed</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";
  const nonPublicNote = data.nonPublicOpportunities > 0
    ? ` · ${data.nonPublicOpportunities} in non-public members`
    : "";

  if (!members.length) {
    return `${picker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No public allocation hot spots</h2><p>${data.totalOpportunities} allocation/performance opportunit${data.totalOpportunities === 1 ? "y was" : "ies were"} classified, but none surface on a public member of ${scanScope}${nonPublicNote}.</p></section>`;
  }

  const rows = members.map(member => {
    const display = escapeHtml(`${shortTypeName(member.typeId)}.${member.memberName}`);
    const shapes = member.shapes.map(shape => `<span class="perf-shape">${escapeHtml(shape)}</span>`).join("");
    const loopBadge = member.inLoopCount > 0 ? `<span class="perf-loop" title="${member.inLoopCount} in a loop">↻ ${member.inLoopCount}</span>` : "";
    return `
      <button class="perf-row" data-perf-selector="${escapeHtml(member.stableSelector)}" data-perf-assembly="${escapeHtml(member.assembly)}" data-perf-type="${escapeHtml(member.typeId)}" title="${escapeHtml(member.typeId)}.${escapeHtml(member.memberName)} — open member">
        <span class="perf-count">${member.opportunityCount}</span>
        <span class="perf-member"><span class="perf-name">${display}</span><span class="perf-shapes">${shapes}</span></span>
        <span class="perf-meta">${loopBadge}<span class="perf-confidence perf-${escapeHtml((member.confidence || "").toLowerCase())}">${escapeHtml(member.confidence || "—")}</span></span>
      </button>`;
  }).join("");

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Allocation &amp; performance triage</h2><span>${members.length} public member${members.length === 1 ? "" : "s"} · ${data.totalOpportunities} opportunit${data.totalOpportunities === 1 ? "y" : "ies"}${nonPublicNote} · ${scanScope}</span></div>
      <p class="lens-note">Ranked by product triage policy. Static IL classification — confirm impact with a benchmark or profiler. Select a member to open its API details.</p>
    </section>`;

  return `${picker}${warning}${summary}<section class="document-section"><div class="perf-list">${rows}</div></section>`;
}

async function loadPackagePerformance() {
  const pkg = currentPackage();
  const scopedLib = scopedPlatformLibrary();
  return packageInspection.loadPerformance(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackagePerformance() {
  if (!state.atPackageRoot || state.packageLens !== "analysis") return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packagePerformanceKey === packageScopeSignature()) return;
  observeAsync(loadPackagePerformance(), "Loading package analysis");
}

// The Metadata lens: the image-level "container" view of each assembly — metadata format
// version, heap sizes, ECMA-335 table row counts, and PE/CLI header facts. This is the shape
// of the metadata itself, distinct from the API surface (the types within). For the platform
// it scopes to one runtime-pack assembly (the shared framework is ~160 assemblies); for a
// NuGet package it describes every active-framework lib/ assembly.
function renderPackageMetadata() {
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const fresh = state.packageMetadataKey === packageScopeSignature();
  const scopedLibrary = scopedPlatformLibrary() || "";
  const platformMetadataSelect = isPlatform
    ? platformLibrarySelectHtml({
        dataAttr: "data-platform-metadata-library",
        selected: scopedLibrary,
        requireSelection: true,
      })
    : "";
  const metadataLibraryControl = platformMetadataSelect
    ? `<label class="metadata-library-select">
        <span>Library</span>
        ${platformMetadataSelect}
      </label>`
    : "";
  return renderPackageMetadataHtml({
    isPlatform,
    scopedLibrary,
    packageId: pkg.id,
    packageVersion: pkg.version,
    activeFramework: pkg.activeFramework,
    controlsHtml: `<section class="package-metadata-controls" aria-label="Metadata coordinate">
      <div class="package-coordinate-fields">
        ${packageCoordinateFields()}
        ${metadataLibraryControl}
      </div>
    </section>`,
    fresh,
    loading: state.packageMetadataLoading,
    error: state.packageMetadataError || "",
    metadata: state.packageMetadata || null,
    escapeHtml,
    fmtBytes,
  });
}

async function loadPackageMetadata() {
  const pkg = currentPackage();
  const scopedLib = scopedPlatformLibrary();
  return packageInspection.loadMetadata(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackageMetadata() {
  if (!state.atPackageRoot || state.packageLens !== "metadata") return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageMetadataKey === packageScopeSignature()) return;
  observeAsync(loadPackageMetadata(), "Loading package metadata");
}

// ─── Metadata Explorer ─────────────────────────────────────────────────────────
// A spatial "browse the metadata like a database" view. The overview lens hands off an
// assembly + a starting table; the explorer lays every populated table out as a card,
// lazy-loads each table's row window on demand, renders cells with their typed values, and
// turns handle/range cells into ref->def jumps that transport you to the target table+row.

// Current adaptive page size for the open explorer, falling back to the constant owned by
// metadata-viewer.ts.
function explorerPageSize() {
  return state.explorer?.pageSize || EXPLORER_PAGE;
}

// Opens the explorer over one assembly, focused on a table (and optionally a row). The table
// directory comes from the already-loaded overview so the canvas can render immediately; each
// card fetches its own row window.
function openExplorer(assemblyFileName: string, tableIndex: number, rowId = 0) {
  const ex = buildBaseExplorer(assemblyFileName);
  if (!ex) return;
  ex.history = [{ index: tableIndex, rowId: rowId || 0 }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

function openExplorerOverview(assemblyFileName: string) {
  const ex = buildBaseExplorer(assemblyFileName);
  if (!ex) return;
  ex.overview = true;
  state.explorer = ex;
  render();
}

// Opens the explorer focused on a heap card (#Strings / #Blob / #GUID / #US) rather than a table.
function openExplorerHeap(assemblyFileName: string, heapName: string) {
  const ex = buildBaseExplorer(assemblyFileName);
  if (!ex) return;
  ex.history = [{ heap: heapName }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

// The common explorer state: the table + heap directories drawn from the loaded overview, plus
// empty window caches. Focus is set by the caller (openExplorer / openExplorerHeap).
function buildBaseExplorer(assemblyFileName: string): AppExplorerState | null {
  const data = state.packageMetadata;
  const asm = (data?.assemblies || []).find(a => a.assembly === assemblyFileName)
    || (data?.assemblies || [])[0];
  if (!asm) return null;
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const directory = (asm.tables || [])
    .slice()
    .sort((a, b) => a.index - b.index)
    .map(t => ({ index: t.index, name: t.name, rowCount: t.rowCount, isProjected: t.isProjected }));
  const heaps = (asm.heaps || [])
    .filter(h => h.sizeInBytes > 0)
    .map(h => ({ name: h.name, streamName: heapStreamName(h.name), sizeInBytes: h.sizeInBytes, addressing: h.addressing }));
  return {
    open: true,
    isPlatform,
    assemblyFileName: asm.assembly,
    pack: isPlatform ? platformPackForAssembly(asm.assembly.replace(/\.dll$/i, "")) : null,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    directory,
    heaps,
    windows: {},
    heapWindows: {},
    focusIndex: directory[0]?.index ?? 0,
    focusHeap: null,
    highlight: null,
    detail: null,
    history: [],
    historyPos: -1,
    overview: false,
    pageSize: estimateExplorerPageSize(window.innerHeight || 0),
    pendingScroll: false,
  };
}

function closeExplorer() {
  state.explorer = null;
  render();
}

async function loadExplorerWindow(
  index: number,
  startRowId = 1,
  maxRows = explorerPageSize(),
) {
  return metadataInspection.loadExplorerWindow(index, startRowId, maxRows);
}

// Lists one heap's entries via the engine (referenced-only for #Strings/#Blob, complete for
// #GUID, nothing for #US). Cached per heap name; coverage/truncation travel with the result.
async function loadExplorerHeap(heapName: string) {
  return metadataInspection.loadExplorerHeap(heapName);
}
// ref->def: transport to the target table+row. Every jump pushes a focus entry onto the
// history stack so Back/Forward can walk the journey — essential once the focus panel hides
// the table you came from (including intra-table hops like TypeDef.Extends -> another TypeDef,
// which otherwise look like "you didn't move").
function explorerJump(index: number, rowId: number) {
  pushExplorerFocus({ index, rowId: rowId || 0 });
}

// Move focus to a new entry, truncating any forward history (a fresh branch). Re-selecting the
// current table just updates its row in place rather than stacking a duplicate.
function pushExplorerFocus(entry: ExplorerFocus) {
  const ex = state.explorer;
  if (!ex) return;
  const cur = ex.history[ex.historyPos];
  if (sameFocus(cur, entry)) {
    ex.history[ex.historyPos] = entry;
  } else {
    ex.history = ex.history.slice(0, ex.historyPos + 1);
    ex.history.push(entry);
    ex.historyPos = ex.history.length - 1;
  }
  applyExplorerFocus();
}

function explorerHistoryBack() {
  const ex = state.explorer;
  if (!ex || ex.historyPos <= 0) return;
  ex.historyPos--;
  applyExplorerFocus();
}

function explorerHistoryForward() {
  const ex = state.explorer;
  if (!ex || ex.historyPos >= ex.history.length - 1) return;
  ex.historyPos++;
  applyExplorerFocus();
}

// Zoom out from the focus lightbox to the all-tables wall (undimmed + interactive). The current
// position is remembered (focusIndex/history untouched), so clicking back into it — or Back /
// Forward — resumes exactly where you were. Escape from here exits to the Metadata page.
function explorerShowOverview() {
  const ex = state.explorer;
  if (!ex || ex.overview) return;
  ex.overview = true;
  render();
  requestAnimationFrame(() => {
    const card = ex.focusHeap
      ? document.querySelector(`.mde-wall .mde-heap-card[data-mde-heap="${cssEscape(ex.focusHeap)}"]`)
      : document.querySelector(`.mde-wall .mde-card[data-mde-index="${ex.focusIndex}"]`);
    if (card) card.scrollIntoView({ behavior: "smooth", block: "center" });
  });
}

// Realize the current history entry: set focus + highlight + detail, load the window/heap that
// backs it, render, and scroll it into place. The single source of truth for "where am I".
function applyExplorerFocus() {
  const ex = state.explorer;
  const entry = ex?.history[ex.historyPos];
  if (!entry) return;
  ex.overview = false;
  if (entry.heap != null) {
    ex.focusHeap = entry.heap;
    ex.highlight = null;
    ex.detail = null;
    const heapWindow = ex.heapWindows[entry.heap];
    if (!heapWindow || (!heapWindow.loading && !heapWindow.data))
      observeAsync(loadExplorerHeap(entry.heap), "Loading metadata heap rows");
    else render();
  } else {
    if (entry.index == null) return;
    ex.focusHeap = null;
    ex.focusIndex = entry.index;
    ex.highlight = entry.rowId ? { index: entry.index, rowId: entry.rowId } : null;
    ex.detail = entry.rowId ? { index: entry.index, rowId: entry.rowId } : null;
    const start = entry.rowId ? Math.max(1, Math.floor((entry.rowId - 1) / explorerPageSize()) * explorerPageSize() + 1) : 1;
    const win = ex.windows[entry.index];
    const onScreen = win && !win.loading && win.data && (!entry.rowId
      || (entry.rowId >= win.data.startRowId && entry.rowId < win.data.startRowId + (win.data.rows?.length || 0)));
    if (onScreen) render();
    else observeAsync(loadExplorerWindow(entry.index, start), "Loading metadata table rows");
  }
  ex.pendingScroll = true;
  explorerScrollToFocus();
}

// Center the active card in the dim wall behind the lightbox, and scroll the highlighted row into
// view in the focus panel's grid — but ONLY for a real navigation (pendingScroll), so ordinary
// re-renders (row selection, a background card hydrating) never nudge the wall. If the target
// window is still loading, the flag stays set and the loader's finally completes the scroll.
function explorerScrollToFocus() {
  requestAnimationFrame(() => {
    const ex = state.explorer;
    if (!ex || !ex.pendingScroll || ex.overview) return;
    const wallCard = ex.focusHeap
      ? document.querySelector(`.mde-wall .mde-heap-card[data-mde-heap="${cssEscape(ex.focusHeap)}"]`)
      : document.querySelector(`.mde-wall .mde-card[data-mde-index="${ex.focusIndex}"]`);
    if (wallCard) wallCard.scrollIntoView({ behavior: "smooth", block: "center" });
    if (!ex.highlight) {
      const focusGrid = document.querySelector(".mde-focus .mde-grid-scroll");
      if (focusGrid) focusGrid.scrollTop = 0;
      ex.pendingScroll = false;
      return;
    }
    const row = document.querySelector(`.mde-focus .mde-row[data-mde-row="${ex.highlight.index}:${ex.highlight.rowId}"]`);
    if (row) {
      row.scrollIntoView({ behavior: "smooth", block: "center" });
      ex.pendingScroll = false;
    }
  });
}

// Size the row window to the focus panel's actual visible height so a tall panel fills instead of
// showing 50 rows over a half-empty grid. Measures the rendered row height + scroll viewport,
// then grows the focused window (once) if it can show more rows. No-ops when the size is already
// right, so it converges without thrashing.
function syncExplorerPageSize() {
  const ex = state.explorer;
  if (!ex || ex.overview || ex.focusHeap) return;
  const scroll = document.querySelector(".mde-focus .mde-grid-scroll");
  const row = document.querySelector(".mde-focus .mde-row");
  if (!scroll || !row) return;
  const rowH = row.getBoundingClientRect().height || EXPLORER_ROW_H;
  const viewH = scroll.clientHeight || 0;
  if (rowH < 6 || viewH < 40) return;
  const fit = Math.max(20, Math.min(500, Math.floor(viewH / rowH) + 2));
  if (fit === ex.pageSize) return;
  ex.pageSize = fit;
  const win = ex.windows[ex.focusIndex];
  const rows = win?.data?.rows ?? [];
  if (win?.data && !win.loading
    && rows.length < fit && rows.length < win.data.rowCount) {
    observeAsync(
      loadExplorerWindow(ex.focusIndex, win.data.startRowId, fit),
      "Loading metadata table rows");
  }
}

// Renders the explorer surface owned by metadata-viewer.ts, then binds its events.
function renderMetadataExplorer() {
  const explorer = state.explorer;
  if (!explorer) return;
  app.innerHTML = renderMetadataExplorerHtml({
    explorer,
    escapeHtml,
    fmtBytes,
  });
  bindMetadataViewerEvents();
}

let explorerObserver: IntersectionObserver | null = null;
function bindMetadataViewerEvents() {
  const ex = state.explorer;
  bindMetadataExplorer(document, ex, {
    onClose: closeExplorer,
    onHistoryBack: explorerHistoryBack,
    onHistoryForward: explorerHistoryForward,
    onHeapFocus: heap => pushExplorerFocus({ heap }),
    onJump: explorerJump,
    onOpenHeap: openExplorerHeap,
    onOpenOverview: openExplorerOverview,
    onOpenTable: openExplorer,
    onPage: (index, startRowId) =>
      observeAsync(
        loadExplorerWindow(index, startRowId),
        "Loading metadata table rows"),
    onRetryPackageMetadata: () =>
      observeAsync(loadPackageMetadata(), "Retrying package metadata"),
    onRowFocus: (index, rowId) => {
      if (!ex) return;
      const already =
        ex.detail && ex.detail.index === index && ex.detail.rowId === rowId;
      ex.detail = already ? null : { index, rowId };
      ex.highlight = already ? null : { index, rowId };
      const current = ex.history[ex.historyPos];
      if (current && current.index === index) {
        current.rowId = already ? 0 : rowId;
      }
      render();
    },
    onShowOverview: explorerShowOverview,
    onTableFocus: (index, rowId) => pushExplorerFocus({ index, rowId }),
  });
  if (!ex) return;
  // Hydrate cards as they scroll into view (the "wall of tables filling in as you pan" feel).
  explorerObserver?.disconnect();
  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        if (!(entry.target instanceof HTMLElement)) continue;
        if (entry.target.dataset.mdeHeapNeedsLoad != null) {
          observeAsync(
            loadExplorerHeap(entry.target.dataset.mdeHeapNeedsLoad),
            "Loading metadata heap rows");
        } else {
          observeAsync(
            loadExplorerWindow(Number(entry.target.dataset.mdeNeedsLoad)),
            "Loading metadata table rows");
        }
      }
    }
  }, { root: document.querySelector("#mde-canvas"), rootMargin: "200px" });
  explorerObserver = observer;
  document.querySelectorAll<HTMLElement>("[data-mde-needs-load], [data-mde-heap-needs-load]")
    .forEach(el => observer.observe(el));

  // Always ensure the focused table or heap is loaded (its window backs the focus panel).
  if (state.explorer) {
    if (state.explorer.focusHeap && !state.explorer.heapWindows[state.explorer.focusHeap]) {
      observeAsync(
        loadExplorerHeap(state.explorer.focusHeap),
        "Loading metadata heap rows");
    } else if (!state.explorer.focusHeap && !state.explorer.windows[state.explorer.focusIndex]) {
      observeAsync(
        loadExplorerWindow(state.explorer.focusIndex),
        "Loading metadata table rows");
    }
    // Once the focus panel is laid out, size the row window to its actual height.
    if (!state.explorer.overview && !state.explorer.focusHeap) {
      requestAnimationFrame(syncExplorerPageSize);
    }
    ensureExplorerResizeListener();
  }
}

// Re-fit the focus window when the viewport changes (registered once, lives for the app).
let explorerResizeBound = false;
function ensureExplorerResizeListener() {
  if (explorerResizeBound) return;
  explorerResizeBound = true;
  let resizeTimer: ReturnType<typeof setTimeout> | null = null;
  window.addEventListener("resize", () => {
    if (resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      const ex = state.explorer;
      if (ex && ex.open && !ex.overview && !ex.focusHeap) syncExplorerPageSize();
    }, 150);
  });
}


// Stable product identities bridge implementation-body evidence to the
// reference-preferred surface the navigation pane renders.
function drillToPerfMember(
  stableSelector: string,
  assembly: string,
  typeId: string,
) {
  const pkg = currentPackage();
  const target = resolvePackagePerformanceMember(pkg, {
    assembly,
    typeId,
    stableSelector,
  });
  if (!target) return;
  const { type: targetType, member } = target;

  state.atPackageRoot = false;
  state.selectedTypeId = targetType.id;
  state.memberBrowseTypeId = targetType.id;
  state.namespaceFilter = "";
  resetMemberFilters();
  state.lens = "api";
  const key = `${member.kind}:${member.name}`;
  state.selectedMemberKey = key;
  const group = memberGroups(targetType).find(candidate => candidate.key === key);
  const overloadIndex = group && group.overloads.length > 1
    ? group.overloads.findIndex(
      overload => overload.stableSelector === stableSelector)
    : -1;
  state.selectedOverloadIndex = overloadIndex >= 0 ? overloadIndex : null;
  resetMemberSectionState();
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === targetType.id);
  observeAsync(
    loadSelectedMemberDocumentation(),
    "Loading member documentation");
}

function renderPackageOverview() {
  const pkg = currentPackage();

  const kindPlural: Record<TypeKind, string> = {
    class: "classes",
    struct: "structs",
    interface: "interfaces",
    enum: "enums",
    delegate: "delegates",
  };

  interface LibraryStat {
    assemblyId: string;
    assembly: string;
    types: number;
    kinds: Map<TypeKind, number>;
  }

  // Per-library breakdown: group the loaded types by their owning assembly, each
  // with its own types-by-kind. The library is the meaningful unit for
  // measurement — a merged "classes per package" number is noise. The overview
  // reports the public surface (matching the package's headline type count);
  // non-public types are reached via the type nav pane's accessibility filter.
  const libStats = new Map<string, LibraryStat>();
  for (const type of pkg.types) {
    if (!isDefaultAccessibility(type)) continue;
    const asm = type.assembly || pkg.assembly || "(unknown)";
    const assemblyId = type.assemblyId || "";
    const key = assemblyId || `legacy:${asm}`;
    let stat = libStats.get(key);
    if (!stat) {
      stat = { assemblyId, assembly: asm, types: 0, kinds: new Map() };
      libStats.set(key, stat);
    }
    stat.types++;
    const kind = typeKind(type.kind);
    stat.kinds.set(kind, (stat.kinds.get(kind) || 0) + 1);
  }
  const memberFor = (stat: LibraryStat) => {
    const hit = assemblyDescriptorForType(pkg.assemblies, stat);
    return hit ? hit.publicMembers : null;
  };
  const libraryRows = [...libStats.entries()]
    .sort((a, b) => b[1].types - a[1].types)
    .map(([, stat]) => {
      const asm = stat.assembly;
      const name = asm.endsWith(".dll") ? asm.slice(0, -4) : asm;
      const members = memberFor(stat);
      const multi = libStats.size > 1;
      const kinds = KIND_ORDER
        .filter(kind => stat.kinds.has(kind))
        .map(kind => multi
          ? `<button class="lib-kind as-button" data-lib-scope="${escapeHtml(name)}" data-lib-kind="${kind}" title="Show ${kindPlural[kind]} in ${escapeHtml(name)}"><strong>${stat.kinds.get(kind)}</strong> ${kindPlural[kind]}</button>`
          : `<span class="lib-kind"><strong>${stat.kinds.get(kind)}</strong> ${kindPlural[kind]}</span>`)
        .join("");
      const nameCell = multi
        ? `<button class="library-name as-button" data-lib-scope="${escapeHtml(name)}" title="Show all ${escapeHtml(name)} types">${escapeHtml(name)}</button>`
        : `<span class="library-name" title="${escapeHtml(asm)}">${escapeHtml(name)}</span>`;
      return `<div class="library-row">
        <div class="library-row-head">
          ${nameCell}
          <span class="library-metric">${stat.types} type${stat.types === 1 ? "" : "s"}${members != null ? ` · ${members.toLocaleString()} members` : ""}</span>
        </div>
        <div class="library-kinds">${kinds}</div>
      </div>`;
    })
    .join("");

  // For the runtime pack, the loaded set is one library; the static index knows
  // the full roster, so surface how many more libraries this framework carries.
  // Scope the count to the resident pack — the index now spans both the CoreCLR
  // and ASP.NET Core shared frameworks, and conflating them would overcount.
  let librariesSubtitle = `${libStats.size} loaded`;
  if (pkg.isRuntimePack && state.platformIndex) {
    const indexPack = /aspnetcore/i.test(pkg.id) ? "aspnetcore.app" : "netcore.app";
    const total = state.platformIndex.assembliesFor(pkg.activeFramework, indexPack).filter(a => a.kind === "impl").length;
    if (total > 0) librariesSubtitle = `${libStats.size} loaded · ${total} in ${escapeHtml(pkg.activeFramework)}`;
  }

  const nsCounts = new Map<string, number>();
  for (const type of pkg.types) {
    if (!isDefaultAccessibility(type)) continue;
    const ns = type.namespace || "global";
    nsCounts.set(ns, (nsCounts.get(ns) || 0) + 1);
  }
  const namespaceChips = [...nsCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 12)
    .map(([ns, count]) => `<button class="type-chip" data-namespace-jump="${escapeHtml(ns)}"><span class="ns-count">${count}</span>${escapeHtml(ns)}</button>`)
    .join("");
  const nsOverflow = nsCounts.size > 12 ? `<span class="ns-overflow">+${nsCounts.size - 12} more</span>` : "";

  const documentsSection =
    renderPackageDocuments(pkg.documents || [], escapeHtml);

  return `
    <section class="document-section">
      <div class="section-title"><h2>Libraries</h2><span>${librariesSubtitle}</span></div>
      ${pkg.isRuntimePack ? `<div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml()}</div>` : ""}
      <div class="library-list">${libraryRows}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Namespaces</h2><span>${nsCounts.size} — click to filter</span></div>
      <div class="type-chip-list">${namespaceChips}${nsOverflow}</div>
    </section>${documentsSection}`;
}

function renderGraphMemberPendingHtml(
  item: AppTypeSurface,
  title: string,
) {
  return renderGraphMemberPending({
    item,
    title,
    packageContext: currentPackage(),
    escapeHtml,
    typeDisplayName,
    kindIcon,
    highlight,
  });
}

function renderTypeMetadataHtml(item: AppTypeSurface) {
  return renderTypeMetadata({
    item,
    packageContext: currentPackage(),
    metadataState: state,
    memberCompositionHtml: renderMemberComposition(item),
    escapeHtml,
    relatedTypeChip,
    factRows,
  });
}

function renderTypeSourceHtml(item: AppTypeSurface) {
  const currentSignature = typeSourceSignature(
    item,
    currentPackage(),
    state.taste,
    memberRequestKey);
  return renderTypeSource({
    item,
    currentSignature,
    sourceState: state,
    escapeHtml,
    highlightCSharp,
  });
}

function currentPendingGraphMember() {
  const pending = state.pendingGraphMemberDeepLink;
  return pending
    && graphMemberPendingMatchesView(
      pending,
      packageIdentityKey(state.package),
      viewSignature())
    ? pending
    : null;
}

function renderLens(item: AppTypeSurface | null | undefined) {
  if (scope() === "workspace") return renderWorkspaceView();
  if (state.atPackageRoot) return renderPackageView();
  if (!item) return "";
  switch (state.lens) {
    case "source":
      return renderTypeSourceHtml(item);
    case "metadata":
      return renderTypeMetadataHtml(item);
    case "api":
      return renderApiLens(item);
    default:
      return assertNever(state.lens, "type lens");
  }
}

function renderApiLens(item: AppTypeSurface) {
  const pending = currentPendingGraphMember();
  if (pending) {
    const title =
      state.graphMemberNavigationTitle
      || `${typeDisplayName(item)}.${pending.target.memberName}`;
    return renderGraphMemberPendingHtml(item, title);
  }
  const member = selectedMember(item);
  if (member) return renderMember(item, member);
  const { publicMembers, graphMembers } =
    partitionGraphMembers(item.api);
  const publicSurface = {
    ...item,
    api: publicMembers
  };
  const publicGroups = memberGroups(publicSurface);
  const visibleGroups = visibleMemberGroups(publicSurface);
  if (state.memberBrowseTypeId === item.id) {
    return `
      <section class="member-surface member-empty-surface" aria-labelledby="member-surface-title">
        <header class="api-surface-head member-surface-head">
          <h1 id="member-surface-title">Members</h1>
          <p>${visibleGroups.length} of ${publicGroups.length} member groups <span>· no member selected</span></p>
        </header>
        <div class="member-surface-scroll">
          <section class="empty-member-section">
            <span class="large-glyph">⌕</span>
            <h2>No member selected</h2>
            <p>Adjust the member filters or choose a member from the list.</p>
          </section>
        </div>
      </section>`;
  }
  const graphGroups = memberGroups({
    ...item,
    api: graphMembers
  });
  return `
    <section class="api-surface" aria-labelledby="api-surface-title">
      <header class="api-surface-head">
        <h1 id="api-surface-title">Members</h1>
        <p>${visibleGroups.length} of ${publicGroups.length} member groups <span>· ${item.members} overloads</span></p>
      </header>
      <div class="member-browser-controls api-surface-controls">${renderMemberFilterControls(publicSurface)}</div>
      <div class="api-surface-scroll">
        <div class="api-list api-surface-list">${visibleGroups.map(group => {
        const overload = group.overloads[0];
        if (!overload)
          throw new Error(`Member group '${group.key}' did not contain an overload.`);
        return `
        <button class="api-row" data-member="${escapeHtml(group.key)}">
          <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
          <code>${highlight(overload.signature)}</code>
          <small>${group.overloads.length === 1 ? escapeHtml(group.kind) : `${group.overloads.length} overloads`}</small>
        </button>`;
        }).join("") || '<div class="empty-list">No declared public members match these filters.</div>'}</div>
        ${graphGroups.length
          ? `<section class="api-surface-secondary">
              <div class="section-title"><h2>Graph-discovered implementation members</h2><span>${graphGroups.reduce((count, group) => count + group.overloads.length, 0)} projected</span></div>
              <div class="api-list">${graphGroups.map(group => {
                const overload = group.overloads[0];
                if (!overload)
                  throw new Error(`Member group '${group.key}' did not contain an overload.`);
                return `
                <button class="api-row" data-member="${escapeHtml(group.key)}">
                  <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
                  <code>${highlight(overload.signature)}</code>
                  <small>${group.overloads.length === 1 ? "implementation" : `${group.overloads.length} implementations`}</small>
                </button>`;
              }).join("")}</div>
            </section>`
          : ""}
      </div>
      <footer class="api-surface-footer">
        <span>Select a row to inspect its API</span>
      </footer>
    </section>`;
}

function renderMember(type: AppTypeSurface, member: AppMemberGroup) {
  const selectedOverloadIndex = state.selectedOverloadIndex;
  const hasSelectedOverload =
    selectedOverloadIndex != null
    && Number.isInteger(selectedOverloadIndex)
    && selectedOverloadIndex >= 0
    && selectedOverloadIndex < member.overloads.length;
  if (member.overloads.length > 1 && !hasSelectedOverload) {
    return `
      <section class="member-surface member-overload-surface" aria-labelledby="member-surface-title">
        <header class="api-surface-head member-surface-head">
          <h1 id="member-surface-title">${escapeHtml(member.name)}</h1>
          <p>${member.overloads.length} overloads <span>· ${escapeHtml(member.kind)}</span></p>
        </header>
        <div class="member-surface-scroll">
          <div class="api-list api-surface-list member-surface-list">
            ${member.overloads.map((overload, index) => `
              <button class="api-row overload-row" data-overload="${index}">
                <span class="member-icon">${index + 1}</span>
                <code>${highlight(overload.signature)}</code>
                <small>open →</small>
              </button>`).join("")}
          </div>
        </div>
        <footer class="api-surface-footer member-surface-footer">
          <button class="member-back" id="member-back">← ${escapeHtml(typeDisplayName(type))}</button>
          <span>Choose an overload to inspect</span>
        </footer>
      </section>`;
  }
  const overloadIndex = hasSelectedOverload ? selectedOverloadIndex ?? 0 : 0;
  const overload = member.overloads[overloadIndex];
  if (!overload) return "";
  const pkg = currentPackage();
  const documentationKey = memberRequestSignature(type, overload);
  const documentationState = scopedRequestState(
    state.memberDocumentationKey,
    documentationKey,
    state.memberDocumentationLoading,
    state.memberDocumentationError);
  const documentationLoading = documentationState.loading;
  const documentationError = documentationState.error;
  let content;
  if (state.memberSection === "overview") {
    const parameters = overload.parameters ?? [];
    const documentationSummary = documentationLoading
      ? '<p class="docs-loading">Loading package documentation…</p>'
      : documentationError
        ? `<p class="docs-unavailable">Documentation query failed: ${escapeHtml(documentationError)}</p>`
        : overload.summary
          ? `<p class="api-summary">${escapeHtml(overload.summary)}</p>`
          : '<p class="docs-unavailable">No summary was found in the package XML documentation.</p>';
    content = `
      <article class="learn-overview">
        <section class="learn-section member-overview-intro">
          <section class="signature-panel" aria-labelledby="member-declaration-title">
            <div class="signature-language">
              <h2 id="member-declaration-title"><span>C#</span><small>declaration</small></h2>
              <button id="copy-signature" type="button" aria-label="Copy declaration">copy</button>
            </div>
            <pre class="language-csharp signature-code"><code class="language-csharp">${highlightCSharp(overload.signature)}</code></pre>
          </section>
          <section class="member-documentation" aria-labelledby="member-documentation-title">
            <div class="member-documentation-heading">
              <h2 id="member-documentation-title">Summary</h2>
            </div>
            ${documentationSummary}
          </section>
          <section class="member-identity" aria-labelledby="member-identity-title">
            <div class="identity-heading"><h2 id="member-identity-title">Identity</h2><span>stable across builds</span></div>
            <dl>
              <div><dt>Stable selector</dt><dd><code>${escapeHtml(overload.stableSelector)}</code><button type="button" data-copy-anchor="selector" aria-label="Copy stable selector">copy</button></dd></div>
              <div><dt>Digest</dt><dd><code>${escapeHtml(overload.anchorDigest)}</code><button type="button" data-copy-anchor="digest" aria-label="Copy digest">copy</button></dd></div>
              <div class="canonical-identity"><dt>Canonical signature</dt><dd><code>${escapeHtml(overload.canonicalSignature)}</code><button type="button" data-copy-anchor="canonical" aria-label="Copy canonical signature">copy</button></dd></div>
            </dl>
            <p>Derived from the canonical signature; suitable for selecting this overload across builds.</p>
          </section>
        </section>
        ${renderMemberContractSections({
          parameters,
          returnType: overload.returnType,
          returns: overload.returns,
          exceptions: overload.exceptions,
          activeFramework: pkg.activeFramework,
          documentationStatus: documentationLoading
            ? "loading"
            : documentationError
              ? "error"
              : "loaded",
        })}
      </article>
    `;
  } else if (state.memberSection === "call-graph") {
    const active = currentCallGraph();
    const callGraphError = callGraphErrorForView(state);
    const drilled = state.platformStack.length > 0;
    // A resident runtime-pack member uses the cumulative platform workspace rather than the
    // open-package workspace. Keep its scope label distinct while preserving callers returned
    // from every platform assembly loaded into that binding-consistent group.
    const platformView = drilled || Boolean(state.package?.isRuntimePack);
    const callers = active?.callers?.children ?? [];
    const callees = active?.callees?.children ?? [];
    const graphScope = active?.scope;
    const otherWorkspaceLibraries = Math.max(
      0,
      state.packages.filter(packageItem => !packageItem.isRuntimePack).length - 1);
    const breadcrumb = drilled
      ? `<div class="graph-breadcrumb">
          <button type="button" data-graph-back title="Back one level">‹ Back</button>
          <span class="graph-crumbs">${escapeHtml(platformCrumbTrail())}</span>
        </div>`
      : "";
    const scopeLine = !graphScope
      ? ""
      : platformView
      ? `<div class="graph-scope"><strong>Platform${drilled ? " descent" : " workspace"}</strong><span>${graphScope.callerAssemblies} resident assemblies · ${graphScope.assemblies} participants</span><strong>Callees</strong><span>${escapeHtml(graphScope.calleeScope)} · depth 2</span></div>`
      : `<div class="graph-scope"><strong>Workspace callers</strong><span>${graphScope.packages} loaded packages · ${graphScope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(graphScope.calleeScope)} · depth 2</span></div>`;
    const diagnostics = active?.diagnostics;
    const diagnosticsMessage = callGraphDiagnosticsMessage(diagnostics);
    const incompleteGraph = diagnosticsMessage
      ? `<div class="graph-drill-error graph-diagnostics">${escapeHtml(diagnosticsMessage)}</div>`
      : "";
    content = state.memberCallGraphLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Building workspace call graph…</h2><p>Scanning implementation IL across ${state.packages.length} loaded package${state.packages.length === 1 ? "" : "s"}.</p></section>`
      : active && active.noBody
        ? `<section class="document-section empty-member-section"><h2>No call graph</h2><p>${escapeHtml(active.callees?.memberName || "This member")} is an abstract or interface method — it declares no IL body, so it has no in-assembly callers or callees to graph.</p></section>`
        : active
        ? `<section class="document-section call-graph-section">
            <div class="section-title"><h2>Call graph</h2><span>${callers.length} caller${callers.length === 1 ? "" : "s"} · ${callees.length} callee${callees.length === 1 ? "" : "s"}</span></div>
            ${breadcrumb}
            ${state.platformDrillLoading
              ? `<div class="graph-expanding"><span class="loader"></span> Range-fetching the implementation assembly from the runtime pack…</div>`
              : ""}
            ${state.platformDrillError
              ? `<div id="platform-drill-error" class="graph-drill-error" role="alert" tabindex="-1">${escapeHtml(state.platformDrillError)}</div>`
              : ""}
            ${state.memberCallGraphExpanding
              ? `<div class="graph-expanding"><span class="loader"></span> Scanning ${otherWorkspaceLibraries} other librar${otherWorkspaceLibraries === 1 ? "y" : "ies"} for callers…</div>`
              : ""}
            ${state.graphMemberNavigationTitle
              ? `<div class="graph-expanding"><span class="loader"></span> Opening ${escapeHtml(state.graphMemberNavigationTitle)}…</div>`
              : ""}
            ${callGraphError
              ? `<div class="graph-drill-error">${escapeHtml(callGraphError)}</div>`
              : ""}
            ${incompleteGraph}
            ${scopeLine}
            <div id="call-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
            <div class="graph-legend" aria-label="Graph legend">
              <span><i class="legend-swatch target"></i>target member</span>
              <span><i class="legend-swatch same-type"></i>same declaring type</span>
              <span><i class="legend-swatch different-type"></i>different type, same assembly</span>
              <span><i class="legend-swatch different-assembly"></i>different assembly</span>
              <span><i class="legend-swatch loaded-node"></i>solid border: no platform lookup</span>
              <span><i class="legend-swatch platform-node"></i>dashed border: external assembly (platform lookup on click)</span>
            </div>
            <details class="graph-mermaid"><summary>Mermaid source</summary><pre><code>${escapeHtml(active.mermaid)}</code></pre></details>
          </section>`
        : `<section class="document-section empty-member-section"><h2>Call graph query failed</h2><p>${escapeHtml(callGraphError || "No call graph result was returned.")}</p></section>`;
    content = `<div data-call-graph-surface>${content}</div>`;
  } else if (state.memberSection === "facts") {
    content = renderMemberFacts(state);
  } else if (state.memberSection === "annotated") {
    const destinationError = state.annotatedDestinationError
      ? `<div id="annotated-destination-error" class="graph-drill-error" role="alert">${escapeHtml(state.annotatedDestinationError)}</div>`
      : "";
    content = destinationError + (state.memberAnnotatedLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Annotating member…</h2><p>Raising the selected overload to C#, interleaving its IL, and collecting the facts observed about it.</p></section>`
      : state.memberAnnotated
        ? renderAnnotatedSource(state.memberAnnotated)
        : `<section class="document-section empty-member-section"><h2>Annotated source query failed</h2><p>${escapeHtml(state.memberAnnotatedError || "No annotated source result was returned.")}</p></section>`);
  } else if (state.memberSection === "source") {
    content = state.memberSourceLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p></section>`
      : state.memberSource
        ? renderSourceResult({
            source: state.memberSource,
            escapeHtml,
            highlightCSharp,
          })
        : `<section class="document-section empty-member-section"><h2>Source query failed</h2><p>${escapeHtml(state.memberSourceError || "No source result was returned.")}</p></section>`;
  } else {
    assertNever(state.memberSection, "member section");
  }
  if (!memberSectionUsesWorkingSurface(state.memberSection)) return content;
  // The member-mode strip (Overview / Call graph / Facts / Source / Annotated) now lives in
  // the top scope+lens bar, so the detail view renders only the section content itself.
  return `
    <section class="member-surface" aria-labelledby="member-surface-title">
      <header class="api-surface-head member-surface-head">
        <h1 id="member-surface-title">${escapeHtml(member.name)}</h1>
        <p>${escapeHtml(member.kind)} <span>· ${overloadIndex + 1} of ${member.overloads.length}</span></p>
      </header>
      <div class="member-surface-scroll">${content}</div>
    </section>`;
}

// The annotated section renders the product's portable AnnotatedSourceDocument directly: canonical
// lines from its text buffer, structural segments from its nodes, and the fact -> target -> node ->
// span walk it defines. Coordinates, validation, and segmentation belong to document-model.ts.
function renderAnnotatedSource(result: AnnotatedSourceResult) {
  try {
    const session = state.memberAnnotatedEmbedded
      ?? createEmbeddedSession(createAnnotatedSourceViewerModel(result));
    return renderAnnotatedSourcePure({
      result,
      session,
      escapeHtml,
      highlightCSharp: annotatedSourceHighlighter,
    });
  } catch (error) {
    if (!(error instanceof TypeError)) throw error;
    return renderAnnotatedSourceRejection(error);
  }
}

function renderAnnotatedSourceModal() {
  if (!state.memberAnnotated || !state.memberAnnotatedModal) return "";
  try {
    return renderAnnotatedSourceModalPure({
      result: state.memberAnnotated,
      session: state.memberAnnotatedModal,
      escapeHtml,
      highlightCSharp: annotatedSourceHighlighter,
    });
  } catch (error) {
    if (!(error instanceof TypeError)) throw error;
    return `<div id="annotated-source-backdrop" class="annotated-modal-backdrop">
      <section id="annotated-source-modal" class="annotated-modal"
        role="dialog" aria-modal="true" aria-labelledby="annotated-modal-title">
        <header class="annotated-modal-head">
          <div>
            <p class="section-eyebrow">Explore Annotated Source</p>
            <h2 id="annotated-modal-title" tabindex="-1">Annotated source document rejected</h2>
          </div>
          <div class="annotated-modal-head-actions">
            <button id="annotated-modal-close" type="button"
              data-annotated-action="close-modal">Close</button>
          </div>
        </header>
        <section class="annotated-modal-failure" role="alert">
          <p>${escapeHtml(errorMessage(error))}</p>
        </section>
      </section>
    </div>`;
  }
}

function renderAnnotatedSourceRejection(error: TypeError) {
  return `<section class="document-section empty-member-section">
    <h2 id="annotated-source-rejection-title" tabindex="-1">Annotated source document rejected</h2>
    <p>${escapeHtml(errorMessage(error))}</p>
  </section>`;
}

type FactSummaryRow =
  readonly [key: string, value: string];

function factRows(rows: readonly FactSummaryRow[]) {
  return `<dl class="fact-rows">${rows.map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd><code>${escapeHtml(value)}</code></dd></div>`).join("")}</dl>`;
}

function shortTypeName(fullName: string) {
  const generic = fullName.indexOf("<");
  const head = generic < 0 ? fullName : fullName.slice(0, generic);
  const tail = generic < 0 ? "" : fullName.slice(generic);
  const dot = head.lastIndexOf(".");
  return (dot < 0 ? head : head.slice(dot + 1)) + tail;
}

// Split an integration signal's fully-qualified name into its short member/type name and a
// declaring qualifier. Cuts off a method parameter list or generic argument list before the
// last-dot split so a dot inside "(...)" or "<...>" never gets mistaken for the name boundary.
function splitSignalName(fullName: string) {
  const paren = fullName.indexOf("(");
  const angle = fullName.indexOf("<");
  const bounds = [paren, angle].filter(i => i >= 0);
  const cut = bounds.length ? Math.min(...bounds) : -1;
  const head = cut < 0 ? fullName : fullName.slice(0, cut);
  const suffix = cut < 0 ? "" : fullName.slice(cut);
  const dot = head.lastIndexOf(".");
  return {
    short: (dot < 0 ? head : head.slice(dot + 1)) + suffix,
    qualifier: dot < 0 ? "" : head.slice(0, dot),
  };
}

function kindIcon(kind: string) {
  if (kind.includes("struct")) return "S";
  if (kind === "enum") return "E";
  if (kind.includes("interface")) return "I";
  return "C";
}

function shortKind(kind: string) {
  return kind.replace("sealed ", "").replace("abstract ", "").replace("static ", "").replace("readonly ", "");
}

function highlight(value: string) {
  return escapeHtml(value)
    .replace(/\b(public|static|class|abstract|sealed|readonly|struct|return|if|is|new|default)\b/g, '<span class="kw">$1</span>')
    .replace(/\b(string|object|void|Type|Stream|Task|ValueTask|CancellationToken|TValue)\b/g, '<span class="primitive">$1</span>');
}

function highlightCSharp(value: string) {
  const source = value;
  if (window.Prism?.languages?.csharp) {
    return window.Prism.highlight(
      source,
      window.Prism.languages.csharp,
      "csharp");
  }
  return escapeHtml(source);
}

function annotatedSourceHighlighter(
  source: string,
  tokenizationSource: string,
  excludedRanges: readonly CSharpHighlightExclusion[],
) {
  return createCSharpRangeHighlighter(
    source,
    window.Prism,
    escapeHtml,
    tokenizationSource,
    excludedRanges,
  );
}

const packageViewActions: PackageViewBindingActions = {
  onDependencyGroupSelect: index => {
    if (state.dependenciesGroupIndex === index) return;
    state.dependenciesGroupIndex = index;
    patchDependenciesGroup();
  },
  onDependencyLoad: (id, version) =>
    observeAsync(
      openDependencyPackage(id, version),
      "Opening a dependency package"),
  onDependencyOpen: switchToPackageForDependencies,
  onGraphTypeSelect: navigateToTypeByName,
  onKindJump: kind => {
    state.atPackageRoot = false;
    state.kindFilter = kind;
    state.namespaceFilter = "";
    state.typeFilter = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    resetMemberFilters();
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    render();
  },
  onLibraryScopeSelect: (library, kind) => {
    state.atPackageRoot = false;
    if (!library) return;
    state.libraryScope = new Set([library]);
    if (state.package?.isRuntimePack)
      recordPlatformRecent(library, platformPackForAssembly(library));
    state.kindFilter = kind;
    state.namespaceFilter = "";
    state.typeFilter = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    resetMemberFilters();
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    render();
  },
  onNamespaceJump: namespace => {
    state.atPackageRoot = false;
    state.namespaceFilter = namespace;
    state.kindFilter = "";
    state.typeFilter = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    resetMemberFilters();
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    render();
  },
  onPerformanceMemberSelect: target => {
    drillToPerfMember(
      target.stableSelector,
      target.assembly,
      target.typeId);
  },
};

interface NoticeRetryState {
  action: RetryAction;
  previous: string;
  appended: string;
}

const libraryControlActions: LibraryControlBindingActions = {
  onAccessibilityChipSelect: accessibility => {
    toggleAccessibilityChip(accessibility);
    afterLibraryScopeChange();
  },
  onLibraryChipSelect: library => {
    toggleLibraryChip(library);
    afterLibraryScopeChange();
  },
  onLibraryJump: library => {
    state.libraryScope = library ? new Set([library]) : null;
    afterLibraryScopeChange();
  },
  onPlatformLibrarySelect: (name, pack) =>
    observeAsync(
      openPlatformLibrary(name, pack),
      "Opening a platform library"),
  onPlatformLensLibrarySelect: (lens, name, pack) =>
    observeAsync(
      openPlatformLensLibrary(lens, name, pack),
      "Opening a platform library"),
};

async function openPlatformLensLibrary(
  lens: PlatformLibraryLens,
  name: string,
  selectedPack: string | null | undefined,
  originPackage: AppPackage = currentPackage(),
  noticeRetryState: NoticeRetryState | null = null,
) {
  if (!state.packages.includes(originPackage)
    || !packageIdentityEquals(state.package, originPackage)
    || state.home
    || !state.atPackageRoot
    || state.packageLens !== lens) {
    return;
  }
  if (noticeRetryState
    && state.queryNoticeRetryAction === noticeRetryState.action) {
    state.queryNotice = removeAppendedNotice(
      state.queryNotice,
      noticeRetryState.previous,
      noticeRetryState.appended);
    state.queryNoticeRetryAction = null;
  }
  const navigationSeq = navigationSequence.begin();
  const isCurrent = () =>
    navigationSequence.isCurrent(navigationSeq)
    && !state.home
    && state.atPackageRoot
    && state.packageLens === lens
    && packageIdentityEquals(state.package, originPackage);
  const key = name.replace(/\.dll$/i, "");
  const pack = selectedPack || platformPackForAssembly(key);
  const resident = runtimeAssemblyIsResident(
    runtimePackPackage(),
    key,
    pack ?? "");
  if (!resident) {
    const runtimeResult = await loadRuntimePackAssembly(
      platformScopeTfm(),
      `${key}.dll`,
      pack ?? "",
      () => state.packages.includes(originPackage),
      originPackage.version);
    const loaded = runtimeResult.packageModel;
    if (!loaded) {
      if (isCurrent()) {
        const noticeState: NoticeRetryState = {
          action: null,
          previous: state.queryNotice,
          appended: "",
        };
        const retryAction = () =>
          openPlatformLensLibrary(
            lens,
            name,
            pack,
            originPackage,
            noticeState);
        noticeState.action = retryAction;
        appendQueryNotice(
          `Couldn’t load ${key}: ${runtimeResult.failureMessage
            || state.runtimePackError
            || "runtime pack acquisition failed."}`,
          retryAction);
        noticeState.appended = state.queryNotice;
        render();
      }
      return;
    }
  }
  if (!isCurrent()) return;
  state.libraryScope = new Set([key]);
  recordPlatformRecent(key, pack);
  state.atPackageRoot = true;
  state.packageLens = lens;
  state.namespaceFilter = "";
  state.typeFilter = "";
  state.kindFilter = "";
  normalizeLibrarySelection();
  if (lens === "integrations") await loadPackageIntegrations();
  else if (lens === "opportunities") await loadPackageOpportunities();
  else if (lens === "analysis") await loadPackagePerformance();
  else await loadPackageMetadata();
}

function bindPackageViewEvents() {
  bindPackageView(document, packageViewActions);
}

function bindPackageDependencyListEvents() {
  bindPackageDependencyList(document, packageViewActions);
}

function bindStatusBarEvents() {
  bindStatusBar(document, {
    onToggle: () => {
      state.statusBarExpanded = !state.statusBarExpanded;
      render();
    },
  });
}

function bindLibraryControlsEvents() {
  bindLibraryControls(document, libraryControlActions);
}

function bindTypePanelEvents() {
  const renderMemberFilterAndRestoreFocus = (selector = "") => {
    const preserved = captureMemberFocus(document);
    if (selector) {
      preserved.selector = selector;
      preserved.dataTarget = null;
    }
    renderWithMemberFocus(preserved);
  };
  const enterMemberNavigation = (action: () => void) => {
    const focusGeneration = beginSpotlightNavigation();
    contentFramePane = "navigation";
    action();
    restoreContentNavigationFocus(focusGeneration);
  };
  bindTypePanel(document, {
    onClearFilters: () => {
      state.typeFilter = "";
      state.namespaceFilter = "";
      state.kindFilter = "";
      state.accessibilityFilter = defaultAccessibilityFilter(state.package);
      renderPreservingMemberFocus();
    },
    onCopyAnchor: anchor => {
      const type = selectedType();
      const member = selectedMember(type);
      const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
      const values = {
        selector: overload?.stableSelector,
        digest: overload?.anchorDigest,
        canonical: overload?.canonicalSignature
      };
      const value = anchor ? values[anchor] : undefined;
      if (value) void copyText(value, `${anchor} copied`);
    },
    onCopyMemberSource: () => {
      if (state.memberSource)
        void copyText(state.memberSource.text, "source copied");
    },
    onCopySignature: () => {
      const type = selectedType();
      const member = selectedMember(type);
      const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
      if (overload)
        void copyText(overload.signature, "signature copied");
    },
    onCopyTypeSource: () => {
      if (state.typeSource)
        void copyText(state.typeSource.text, "source copied");
    },
    onKindSelect: kind => {
      state.kindFilter = kind;
      state.typeCursor = 0;
      const first = filteredTypes()[0];
      if (first) state.selectedTypeId = first.id;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      resetMemberFilters();
      renderPreservingMemberFocus();
    },
    onListKeyDown: handleTypeKeys,
    onMemberAccessibilityFilterSelect: value => {
      state.memberAccessibilityFilter = value ?? "all";
      normalizeMemberSelection();
      renderMemberFilterAndRestoreFocus();
    },
    onMemberBack: drillOut,
    onMemberCompositionAccessibilitySelect: value => {
      enterMemberNavigation(() => {
        resetMemberFilters();
        state.memberAccessibilityFilter = value;
        enterMemberScope();
        render();
      });
    },
    onMemberCompositionKindSelect: value => {
      enterMemberNavigation(() => {
        resetMemberFilters();
        state.memberKindFilter = value;
        enterMemberScope();
        render();
      });
    },
    onMemberCompositionTraitSelect: value => {
      enterMemberNavigation(() => {
        resetMemberFilters();
        state.memberTraitFilter = value;
        enterMemberScope();
        render();
      });
    },
    onMemberFilterChange: value => {
      state.memberTextFilter = value;
      normalizeMemberSelection();
      renderPreservingMemberFocus();
    },
    onMemberFilterDisclosureToggle: expanded => {
      state.memberFiltersExpanded = expanded;
    },
    onMemberFilterClear: () => {
      resetMemberFilters();
      normalizeMemberSelection();
      renderMemberFilterAndRestoreFocus("#clear-member-filter");
    },
    onMemberFilterKeyDown: (event, value) => {
      if (event.key === "Escape") {
        if (navMode() !== "member" && value === "") return false;
        if (navMode() === "member") {
          exitMemberScope();
        } else {
          state.memberTextFilter = "";
          normalizeMemberSelection();
          renderMemberFilterAndRestoreFocus("#member-filter");
        }
        return true;
      }
      if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return false;
      stepMemberNav(event.key === "ArrowDown" ? 1 : -1, true);
      return true;
    },
    onMemberGroupOpen: memberKey => {
      const focusGeneration = beginSpotlightNavigation();
      showContentDetailAfterRender();
      openMemberGroup(memberKey);
      if (!contentFrameMedia.matches)
        restoreContentNavigationFocus(focusGeneration);
    },
    onMemberKindFilterSelect: value => {
      state.memberKindFilter = value ?? "all";
      normalizeMemberSelection();
      renderMemberFilterAndRestoreFocus();
    },
    onMemberOverloadOpen: openOverload,
    onMemberSelect: memberKey => {
      const group = memberGroups(selectedType())
        .find(item => item.key === memberKey);
      if (group) {
        showContentDetailAfterRender();
        selectMemberNavEntry({ kind: "member", group }, false);
      }
    },
    onMemberTraitFilterSelect: value => {
      state.memberTraitFilter = value ?? "";
      normalizeMemberSelection();
      renderMemberFilterAndRestoreFocus();
    },
    onNamespaceSelect: namespace => {
      state.namespaceFilter = namespace;
      state.typeCursor = 0;
      const first = filteredTypes()[0];
      if (first) state.selectedTypeId = first.id;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      resetMemberFilters();
      renderPreservingMemberFocus();
    },
    onOverloadSelect: index => {
      const group = selectedMember(selectedType());
      if (group) {
        showContentDetailAfterRender();
        selectMemberNavEntry({ kind: "overload", group, index }, false);
      }
    },
    onShowTypes: exitMemberScope,
    onTypeFilterChange: value => {
      state.typeFilter = value;
      state.typeCursor = 0;
      const first = filteredTypes()[0];
      if (first) state.selectedTypeId = first.id;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      resetMemberFilters();
      render();
      focusFilter({ immediate: true });
    },
    onTypeFilterDisclosureToggle: expanded => {
      state.typeFiltersExpanded = expanded;
    },
    onTypeFilterEscape: () => {
      state.typeFilter = "";
      render();
      focusFilter({ immediate: true });
    },
    onTypeSelect: typeId => {
      if (scope() === "type" && typeId === state.selectedTypeId) {
        if (contentFrameMedia.matches) showContentDetail();
        return;
      }
      showContentDetailAfterRender();
      state.atPackageRoot = false;
      state.selectedTypeId = typeId;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      resetMemberFilters();
      state.typeCursor = filteredTypes()
        .findIndex(item => item.id === state.selectedTypeId);
      render();
    },
  }, keybindings);
}

function bindScopeBarEvents() {
  scopeBarBinding = bindScopeBar(document, {
    onApplicationScopeSelect: applicationScope => {
      if (applicationScope === "query") {
        openPackageQueryRoute("", {
          preserveState: true,
          returnFocus: "application-query",
        });
      } else if (scope() !== "workspace") {
        selectWorkspaceApplicationScope();
      }
    },
    onMemberSectionSelect: section => {
      contentFramePane = "detail";
      applyMemberSection(section);
    },
    onPackageLensSelect: lens => {
      contentFramePane = "detail";
      state.packageLens = lens;
      render();
    },
    onScopeSelect: target => {
      contentFramePane = "detail";
      if (target === "workspace") {
        state.workspaceSubjectOpen = true;
        state.atPackageRoot = true;
        state.selectedMemberKey = "";
        state.memberBrowseTypeId = "";
        state.selectedOverloadIndex = null;
      } else if (target === "package") {
        state.workspaceSubjectOpen = false;
        state.atPackageRoot = true;
      } else if (target === "type") {
        state.workspaceSubjectOpen = false;
        // Pop out to the type level: leave the package root and drop any open member so the
        // type lenses (API / Metadata / Source) take the strip. Ensure a type is selected.
        state.atPackageRoot = false;
        if (!state.selectedTypeId) {
          const first = filteredTypes()[0];
          if (first) state.selectedTypeId = first.id;
        }
        state.selectedMemberKey = "";
        state.memberBrowseTypeId = "";
        state.selectedOverloadIndex = null;
      } else if (target === "member") {
        state.workspaceSubjectOpen = false;
        enterMemberScope();
      } else {
        // A new scope used to be accepted here and then do nothing at all.
        assertNever(target, "workspace scope");
      }
      render();
    },
    onTypeLensSelect: lens => {
      contentFramePane = "detail";
      state.lens = lens;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      render();
    },
  }, scopeBarState);
}

function bindSettingsPanelEvents() {
  bindSettingsPanel(document, {
    onClose: closeSettings,
    onOpen: openSettings,
    onTasteClear: clearTaste,
    onTasteToggle: toggleTaste,
    onThemeSelect: setTheme,
  });
}

function bindPackageOpportunitiesEvents() {
  bindPackageOpportunities(document, {
    onLookForSelect: openSpotlight,
    onPackageSelect: packageId =>
      observeAsync(
        openDependencyPackage(packageId, ""),
        "Opening an opportunity package"),
    onTypeSelect: opportunity => {
      if (opportunity.sourceIdentity === "legacy") {
        openSpotlight(shortTypeName(opportunity.typeId));
        return;
      }
      if (opportunity.sourceIdentity !== "exact"
        || !opportunity.sourceDefinitionId) {
        appendQueryNotice(
          "The opportunity source could not be opened because its exact identity is unavailable.");
        render();
        return;
      }
      const candidate = resolveOpportunitySourceCandidate(
        currentPackage(),
        opportunity);
      if (candidate.status !== "unique") {
        const reason = candidate.status === "ambiguous"
          ? "the exact identity matched multiple loaded types"
          : candidate.status === "skew"
            ? "the loaded assembly identity does not match the exact source"
            : candidate.status === "resident"
              ? "the exact source type is not projected from the loaded assembly"
              : "the loaded package does not contain the exact source identity";
        appendQueryNotice(
          `The opportunity source could not be opened: ${reason}.`);
        render();
        return;
      }
      state.atPackageRoot = false;
      navigateToType(candidate.type);
    },
  });
}

function bindContentFrameEvents() {
  bindContentFrame(document, {
    onShowDetail: showContentDetail,
    onShowNavigation: showContentNavigation,
  });
}

function bindGraphSourceEvents() {
  bindGraphSource(document, {
    onClose: closeGraphSource,
  });
}

function bindDocViewerEvents() {
  bindDocViewer(document, {
    onClose: closeDocViewer,
    onOpenDocument: path =>
      observeAsync(openPackageDocument(path), "Opening a package document"),
  });
}

function scheduleAnnotatedFocus(
  target: AnnotatedFocusTarget | string,
  surface: "embedded" | "modal" = "modal",
  preventScroll = false,
) {
  const selector = typeof target === "string"
    ? target
    : annotatedFocusSelector(target, surface);
  requestAnimationFrame(() => {
    const element = document.querySelector<HTMLElement>(selector);
    if (preventScroll) element?.focus({ preventScroll: true });
    else element?.focus();
  });
}

function renderAndFocusAnnotated(
  target: AnnotatedFocusTarget | string,
  surface: "embedded" | "modal" = "modal",
  preventScroll = false,
) {
  const scroll = captureAnnotatedSourceScroll(document);
  render();
  restoreAnnotatedSourceScroll(document, scroll);
  scheduleAnnotatedFocus(target, surface, preventScroll);
}

function openAnnotatedSourceModal() {
  graphExplorer.close(false);
  if (!state.memberAnnotated) return;
  invalidateMemberDestinationWork(state);
  state.annotatedDestinationError = "";
  const model = createAnnotatedSourceViewerModel(state.memberAnnotated);
  const embedded = state.memberAnnotatedEmbedded
    ?? createEmbeddedSession(model);
  const opened = openModalSession(model, embedded);
  state.memberAnnotatedEmbedded = opened.embedded;
  state.memberAnnotatedModal = opened.modal;
  spotlight.reset();
  sourceInspection.clearGraphSource();
  documentInspection.clear();
  renderAndFocusAnnotated(opened.focus);
}

function dismissAnnotatedSourceModal(restoreExploreFocus: boolean) {
  if (!state.memberAnnotated || !state.memberAnnotatedModal) return false;
  let model: AnnotatedSourceViewerModel;
  try {
    model = createAnnotatedSourceViewerModel(state.memberAnnotated);
  } catch (error) {
    if (!(error instanceof TypeError)) throw error;
    state.memberAnnotatedEmbedded = null;
    state.memberAnnotatedModal = null;
    if (restoreExploreFocus) {
      renderAndFocusAnnotated("#annotated-source-rejection-title", "embedded");
    }
    return true;
  }
  state.memberAnnotatedEmbedded =
    dismissModalSession(model, state.memberAnnotatedModal);
  state.memberAnnotatedModal = null;
  if (restoreExploreFocus) renderAndFocusAnnotated({ kind: "explore" }, "embedded");
  return true;
}

function applyAnnotatedSourceAction(action: AnnotatedSourceAction) {
  const result = state.memberAnnotated;
  if (!result) return;
  if (action.kind === "close-modal") {
    dismissAnnotatedSourceModal(true);
    return;
  }
  const model = createAnnotatedSourceViewerModel(result);
  const surface = state.memberAnnotatedModal ? "modal" : "embedded";
  const session = state.memberAnnotatedModal
    ?? state.memberAnnotatedEmbedded
    ?? createEmbeddedSession(model);
  const setSession = (next: AnnotatedSourceSession) => {
    if (surface === "modal") state.memberAnnotatedModal = next;
    else state.memberAnnotatedEmbedded = next;
  };

  switch (action.kind) {
    case "copy":
      void copyText(result.document.text, "annotated source copied");
      return;
    case "explore":
      openAnnotatedSourceModal();
      return;
    case "close-detail": {
      const closed = closeFindingDetail(model, session);
      setSession(closed.state);
      renderAndFocusAnnotated(closed.focus, surface);
      return;
    }
    case "annotation-open":
      setSession(selectFinding(session, action.opener));
      renderAndFocusAnnotated("#annotated-detail-title", surface, true);
      return;
    case "inspector-open":
      setSession(selectFinding(session, {
        kind: "inspector",
        factId: action.factId,
      }));
      renderAndFocusAnnotated("#annotated-detail-title", "modal", true);
      return;
    case "annotation-set": {
      const transition = action.value === "Default"
        ? selectDefaultAnnotations(model, session)
        : action.value === "All"
          ? selectAllAnnotations(model, session)
          : clearAnnotations(session);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus);
      return;
    }
    case "finding-toggle": {
      const transition =
        toggleFindingAnnotation(model, session, action.factId);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus);
      return;
    }
    case "medium-toggle": {
      const transition = toggleMedium(model, session, action.medium);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus);
      return;
    }
    case "coordinate-toggle": {
      const transition = toggleCoordinates(session);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus);
      return;
    }
    case "destination-open": {
      const destination =
        model.invocationDestinations[action.destinationIndex];
      if (!destination) return;
      invalidateMemberDestinationWork(state);
      state.annotatedDestinationError = "";
      const binding =
        callGraphTargetBinding(
          destination.target,
          action.destination,
          "annotated")
        ?? blockedCallGraphNodeBinding(
          destination.target,
          "the exact target is unavailable in the current workspace",
          "annotated");
      dismissAnnotatedSourceModal(false);
      binding.onSelect();
      return;
    }
    case "node-select":
      setSession(selectAnnotatedNode(session, action.nodeId));
      renderAndFocusAnnotated({ kind: "node", nodeId: action.nodeId });
      return;
    case "source-select": {
      const node =
        hitTestAnnotatedNode(model, action.offset, action.medium);
      if (!node) return;
      setSession(selectAnnotatedNode(session, node.id));
      renderAndFocusAnnotated({ kind: "node", nodeId: node.id });
      return;
    }
  }
}

function bindAnnotatedSourceEvents() {
  bindAnnotatedSource(document, {
    onAction: applyAnnotatedSourceAction,
  });
}

const workbenchShellActions: WorkbenchShellBindingActions = {
  onApplicationAction: dispatchApplicationAction,
  onCopySubjectSegment: index => {
    const segment = currentInspectedSubjectPath()[index];
    if (segment?.copyable)
      void copyText(segment.label, `${segment.kind} name copied`);
  },
  onDismissNotice: dismissQueryNotice,
  onDismissPackageNotice: () => {
    const pkg = currentPackage();
    pkg.inspectionErrors = [];
    pkg.inspectionError = "";
    render();
  },
  onNavigateBack: navBack,
  onNavigateForward: navForward,
  onRetryNotice: () => {
    const retryAction = state.queryNoticeRetryAction;
    if (retryAction) observeAction(retryAction, "Retrying the inspection");
  },
  onSearch: () => openSpotlight(),
};

const graphBackActions: GraphBackBindingActions = {
  onBack: popPlatformDrill,
};

function bindWorkspaceSubjectEvents() {
  bindWorkspaceSubject(document, {
    onSelect: openDefaultWorkspace,
    onActivate: action =>
      observeAction(
        () => activateWorkspacePackageOccurrence(action),
        "Opening the Workspace package"),
    onDemo: runHomeDemo,
    onRetry: retryWorkspaceOccurrenceView,
  });
}

function bindEvents() {
  bindStatusBarEvents();
  packageControls.bind(document);
  bindWorkspaceSubjectEvents();
  bindTypePanelEvents();
  bindScopeBarEvents();
  bindSettingsPanelEvents();
  bindMetadataViewerEvents();
  bindPackageOpportunitiesEvents();
  bindGraphSourceEvents();
  bindDocViewerEvents();
  bindAnnotatedSourceEvents();
  bindPackageViewEvents();
  bindLibraryControlsEvents();
  workbenchShellBinding =
    bindWorkbenchShell(document, workbenchShellActions);
  bindGraphBack(document, graphBackActions);
  bindGraphExplore(document, openCallGraphExplorer);
  bindContentFrameEvents();
  observeAsync(ensurePackageVersions(state.package), "Loading package versions");
  if (state.package?.isRuntimePack)
    observeAsync(ensureDotnetReleases(), "Loading .NET release information");
  if (state.spotlightOpen) spotlight.bind(document, "modal");
}

function toggleTheme() {
  setTheme(state.theme === "dark" ? "light" : "dark");
}

function toggleCreditsTheme(): "light" | "dark" {
  setTheme(state.theme === "dark" ? "light" : "dark", false);
  return state.theme === "light" ? "light" : "dark";
}

// Apply and persist a specific theme, refreshing any live graphs whose colors are theme-bound.
function setTheme(theme: "light" | "dark", renderView = true) {
  state.theme = theme === "light" ? "light" : "dark";
  localStorage.setItem("inspect-theme", state.theme);
  document.documentElement.dataset.theme = state.theme;
  if (!renderView) return;
  render();
  if (state.memberCallGraph)
    observeAsync(renderMermaidCallGraph(), "Rendering the member call graph");
  const depGraph = document.querySelector<HTMLElement>("#dependency-graph-diagram");
  if (depGraph) {
    depGraph.dataset.graphDef = "";
    observeAsync(renderDependencyGraph(), "Rendering the dependency graph");
  }
}

function handleTypeKeys(event: KeyboardEvent): boolean {
  if (navMode() === "member") {
    if (event.key === "ArrowDown" || event.key === "j") {
      stepMemberNav(1, true);
      return true;
    } else if (event.key === "ArrowUp" || event.key === "k") {
      stepMemberNav(-1, true);
      return true;
    } else if (event.key === "ArrowLeft" && !event.altKey && !event.shiftKey) {
      // Alt/Shift+ArrowLeft is the global back gesture (see the document keydown
      // handler); leave it unclaimed here so it isn't swallowed as in-page stepping.
      stepHorizontal(-1);
      return true;
    } else if (event.key === "ArrowRight" && !event.altKey && !event.shiftKey) {
      stepHorizontal(1);
      return true;
    }
    return false;
  }
  const items = filteredTypes();
  if (!items.length) return false;
  let cursor = items.findIndex(item => item.id === state.selectedTypeId);
  if (cursor < 0) cursor = Math.min(state.typeCursor, items.length - 1);
  if (event.key === "ArrowDown" || event.key === "j") {
    cursor = Math.min(items.length - 1, cursor + 1);
  } else if (event.key === "ArrowUp" || event.key === "k") {
    cursor = Math.max(0, cursor - 1);
  } else if (event.key === "Home") {
    cursor = 0;
  } else if (event.key === "End") {
    cursor = items.length - 1;
  } else if (event.key === "/") {
    focusFilter();
    return true;
  } else {
    return false;
  }
  selectTypeByCursor(cursor, items, true);
  return true;
}

function selectTypeByCursor(
  cursor: number,
  items: readonly InspectedTypeSurface[],
  focusList: boolean,
) {
  const selected = items[cursor];
  if (!selected) return;
  state.typeCursor = cursor;
  state.selectedTypeId = selected.id;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  render();
  requestAnimationFrame(() => {
    if (focusList) document.querySelector<HTMLElement>("#type-list")?.focus();
    document.querySelector(`[data-type="${CSS.escape(state.selectedTypeId)}"]`)?.scrollIntoView({ block: "nearest" });
  });
}

function stepTypeSelection(delta: number) {
  const items = filteredTypes();
  if (!items.length) return;
  let cursor = items.findIndex(item => item.id === state.selectedTypeId);
  if (cursor < 0) cursor = Math.min(state.typeCursor, items.length - 1);
  cursor = Math.max(0, Math.min(items.length - 1, cursor + delta));
  selectTypeByCursor(cursor, items, false);
}

function spotlightPool() {
  const pool: SpotlightCache["pool"] = [];
  const seen = new Set<string>();
  const pkgs = [state.package, ...state.packages.filter(item => item !== state.package)];
  for (const pkg of pkgs) {
    if (!pkg?.types) continue;
    for (const type of pkg.types) {
      const key = spotlightCandidateKey(pkg, type.id);
      if (seen.has(key)) continue;
      seen.add(key);
      pool.push({ pkg, type });
    }
  }
  return pool;
}

function spotlightCandidates() {
  const active = state.package ?? state.packages[0];
  const signature = active
    ? spotlightCandidateSignature(active, state.packages)
    : "";
  if (spotlightCache && spotlightCache.signature === signature) return spotlightCache;

  const pool = spotlightPool();
  const keyMap: SpotlightCache["keyMap"] = new Map();
  const candidates = pool.map(item => {
    const key = spotlightCandidateKey(item.pkg, item.type.id);
    keyMap.set(key, item);
    const full = `${item.type.namespace ? `${item.type.namespace}.` : ""}${item.type.name}`;
    return { key, name: item.type.name, full };
  });
  spotlightCache = {
    signature,
    pool,
    keyMap,
    candidatesJson: JSON.stringify(candidates),
  };
  return spotlightCache;
}

// Highlight is presentation only; ranking is owned by the engine's SearchTypes.
// Recompute visible spans against the simple type name (exact → prefix → substring → subsequence).
function computeHighlightRanges(
  name: string,
  lowerQuery: string,
): HighlightRange[] {
  if (!lowerQuery) return [];
  const lower = name.toLowerCase();
  if (lower === lowerQuery) return [[0, name.length]];
  if (lower.startsWith(lowerQuery)) return [[0, lowerQuery.length]];
  const index = lower.indexOf(lowerQuery);
  if (index >= 0) return [[index, index + lowerQuery.length]];
  const sub = subsequenceRanges(lower, lowerQuery);
  return sub ? sub.ranges : [];
}

function subsequenceRanges(
  text: string,
  query: string,
): { ranges: HighlightRange[]; contig: number } | null {
  let ti = 0;
  let qi = 0;
  let contig = 0;
  let last = -2;
  const ranges: HighlightRange[] = [];
  while (ti < text.length && qi < query.length) {
    if (text[ti] === query[qi]) {
      if (ti === last + 1) contig++;
      const tail = ranges[ranges.length - 1];
      if (tail && tail[1] === ti)
        ranges[ranges.length - 1] = [tail[0], ti + 1];
      else ranges.push([ti, ti + 1]);
      last = ti;
      qi++;
    }
    ti++;
  }
  return qi === query.length ? { ranges, contig } : null;
}

function highlightRanges(name: string, ranges: readonly HighlightRange[]) {
  if (!ranges || !ranges.length) return escapeHtml(name);
  let out = "";
  let pos = 0;
  for (const [start, end] of ranges) {
    out += escapeHtml(name.slice(pos, start));
    out += `<mark>${escapeHtml(name.slice(start, end))}</mark>`;
    pos = end;
  }
  return out + escapeHtml(name.slice(pos));
}

function spotlightFallbackMatches(
  query: string,
  pool: readonly SpotlightCache["pool"][number][],
) {
  const lowerQuery = query.toLowerCase();
  const scored: Array<{
    item: SpotlightCache["pool"][number];
    rank: number;
  }> = [];
  for (const item of pool) {
    const lower = item.type.name.toLowerCase();
    let rank;
    if (lower === lowerQuery) rank = 0;
    else if (lower.startsWith(lowerQuery)) rank = 1;
    else if (lower.includes(lowerQuery)) rank = 2;
    else continue;
    scored.push({ item, rank });
  }
  scored.sort((a, b) =>
    a.rank - b.rank
    || a.item.type.name.length - b.item.type.name.length
    || a.item.type.name.localeCompare(b.item.type.name));
  return scored
    .slice(0, 30)
    .map(entry => ({ ...entry.item, ranges: computeHighlightRanges(entry.item.type.name, lowerQuery) }));
}

// Ranked type matches across all loaded packages (engine-owned SearchTypes, with a
// client-side fallback). This is one target among several the scoped Spotlight blends.
function spotlightTypeMatches(query: string) {
  const cache = spotlightCandidates();
  if (!query) {
    return cache.pool
      .filter(item => item.pkg === state.package)
      .sort((a, b) => a.type.name.localeCompare(b.type.name))
      .map(item => ({ ...item, ranges: [] }));
  }
  if (!state.engineReady) return spotlightFallbackMatches(query, cache.pool);
  const hits = inspectSearchTypes(query, cache.candidatesJson);
  if (!hits) return spotlightFallbackMatches(query, cache.pool);
  const lowerQuery = query.toLowerCase();
  const matches: Array<SpotlightCache["pool"][number] & {
    ranges: HighlightRange[];
  }> = [];
  for (const hit of hits) {
    const item = cache.keyMap.get(hit.key);
    if (!item) continue;
    matches.push({ ...item, ranges: computeHighlightRanges(item.type.name, lowerQuery) });
  }
  return matches;
}

// Flat member index across every loaded type, deduped by (package, type, member group).
// Cached against the same workspace signature as the type pool so it rebuilds only when
// packages or their type counts change.
let spotlightMemberCache: SpotlightMemberCache | null = null;
function spotlightMemberCandidates() {
  const active = state.package ?? state.packages[0];
  const signature = active
    ? spotlightCandidateSignature(active, state.packages)
    : "";
  if (spotlightMemberCache && spotlightMemberCache.signature === signature) return spotlightMemberCache.pool;
  const pool: SpotlightMemberCandidate[] = [];
  for (const pkg of [state.package, ...state.packages.filter(item => item !== state.package)]) {
    if (!pkg?.types) continue;
    for (const type of pkg.types) {
      for (const group of searchableMemberGroups(memberGroups(type))) {
        pool.push({ pkg, type, memberKey: group.key, name: group.name, kind: group.kind });
      }
    }
  }
  spotlightMemberCache = { signature, pool };
  return pool;
}

function spotlightMemberMatches(query: string) {
  const pool = spotlightMemberCandidates();
  if (!query) return [];
  const lowerQuery = query.toLowerCase();
  const scored: Array<{ item: SpotlightMemberCandidate; rank: number }> = [];
  for (const item of pool) {
    const lower = item.name.toLowerCase();
    let rank;
    if (lower === lowerQuery) rank = 0;
    else if (lower.startsWith(lowerQuery)) rank = 1;
    else if (lower.includes(lowerQuery)) rank = 2;
    else {
      const sub = subsequenceRanges(lower, lowerQuery);
      if (!sub) continue;
      rank = 3;
    }
    scored.push({ item, rank });
  }
  scored.sort((a, b) =>
    a.rank - b.rank
    || a.item.name.length - b.item.name.length
    || a.item.name.localeCompare(b.item.name));
  return scored.map(entry => ({ ...entry.item, ranges: computeHighlightRanges(entry.item.name, lowerQuery) }));
}

// Already-open packages whose id matches the query (or all of them when the query is empty).
function spotlightLoadedPackageMatches(query: string) {
  const lowerQuery = query.toLowerCase();
  return state.packages
    .filter(pkg => !lowerQuery || pkg.id.toLowerCase().includes(lowerQuery))
    .map(pkg => ({ pkg, ranges: computeHighlightRanges(pkg.id, lowerQuery) }));
}

// The target framework the Platform scope resolves libraries against. A resident Platform
// pack's own framework is authoritative — even a preview TFM (e.g. net11.0) the static index
// does not carry yet, whose roster is then honestly empty rather than silently another
// major's libraries. With no resident pack (home Platform scope), prefer the focused
// package's framework, then net10.0 — always clamped to a TFM the static index carries.
function platformScopeTfm(): string {
  const idx = state.platformIndex;
  const known = idx ? idx.tfms() : [];
  const resident = runtimePackPackage()?.activeFramework;
  if (resident) return resident;
  const inIndex = (tfm: string | undefined) =>
    Boolean(tfm && (!idx || known.includes(tfm)));
  for (const candidate of [state.package?.activeFramework, "net10.0"]) {
    if (candidate && inIndex(candidate)) return candidate;
  }
  return known.includes("net10.0") ? "net10.0" : (known[known.length - 1] || "net10.0");
}

// Index-first library roster for the Platform scope: every implementation
// assembly the static platform index knows for the active TFM, across the
// CoreCLR (netcore.app) and ASP.NET Core (aspnetcore.app) shared frameworks —
// with NO pack download. Each library drills in by fetching just that one
// assembly from its shared-framework pack. Matched on assembly name; sorted
// CoreCLR first, then by public-type count so the biggest libraries surface
// first.
function platformLibraryRoster(query: string) {
  const idx = state.platformIndex;
  if (!idx) return [];
  const tfm = platformScopeTfm();
  const lower = query.trim().toLowerCase();
  const rt = runtimePackPackage();
  const loadedKeys = new Set((rt?.assemblies || []).map(a => (a.name || "").replace(/\.dll$/i, "")));
  const rows: Array<{
    assembly: string;
    pack: string;
    publicTypes: number;
    loaded: boolean;
    ranges: HighlightRange[];
  }> = [];
  for (const pack of ["netcore.app", "aspnetcore.app"] as const) {
    for (const row of idx.assembliesFor(tfm, pack)) {
      if (row.kind !== "impl") continue;
      if (lower && !row.assembly.toLowerCase().includes(lower)) continue;
      rows.push({
        assembly: row.assembly,
        pack,
        publicTypes: row.publicTypes,
        loaded: loadedKeys.has(row.assembly),
        ranges: computeHighlightRanges(row.assembly, lower),
      });
    }
  }
  rows.sort((a, b) =>
    (a.pack === b.pack ? 0 : a.pack === "netcore.app" ? -1 : 1)
    || b.publicTypes - a.publicTypes
    || a.assembly.localeCompare(b.assembly));
  return rows;
}

// Which shared framework an assembly ships in. Product-supplied provenance from
// a surface wins, followed by the resident model, current index, and recent
// history.
function platformPackForAssembly(
  key: string,
  exactPack: unknown = null,
): PlatformPack | null {
  const resident = runtimePackPackage();
  return platformPackFromProvenance(
    key,
    exactPack,
    resident?.assemblies,
    state.platformRecent,
    platformLibraryRoster(""));
}

// Remember an opened platform library at the front of the recent list (most-recent
// first, deduped, capped) and persist it. Recent duplicates the .NET / ASP.NET Core
// catalog groups by design — no cross-group de-dupe.
function recordPlatformRecent(assembly: string, pack: string | null) {
  const key = (assembly || "").replace(/\.dll$/i, "");
  if (!key) return;
  const normPack = pack === "aspnetcore.app" ? "aspnetcore.app"
    : pack === "netcore.app" ? "netcore.app"
    : platformPackForAssembly(key);
  if (!normPack) return;
  const rest = (state.platformRecent || []).filter(entry => entry.assembly !== key);
  state.platformRecent = [{ assembly: key, pack: normPack }, ...rest].slice(0, PLATFORM_RECENT_MAX);
  persistPlatformRecent();
}

function persistPlatformRecent() {
  try {
    localStorage.setItem("inspect-platform-recent", JSON.stringify(state.platformRecent));
  } catch {
    // Persistence is best-effort; an in-memory recent list still works this session.
  }
}

// Remember an opened NuGet package at the front of the recent list (most-recent first,
// deduped by id, capped) and persist it, so the Home listing survives a refresh. Called
// only from a successful open, never from search hits or prefetches. The resident runtime
// pseudo-package has no nupkg and is excluded.
function recordRecentPackage(id: string, version: string, framework: string) {
  if (!id || isRuntimePackId(id)) return;
  const rest = (state.recentPackages || []).filter(entry => entry.id.toLowerCase() !== id.toLowerCase());
  state.recentPackages = [
    { id, version: version || "latest", framework: framework || "" },
    ...rest,
  ].slice(0, RECENT_PACKAGES_MAX);
  persistRecentPackages();
}

function persistRecentPackages() {
  try {
    localStorage.setItem("inspect-recent-packages", JSON.stringify(state.recentPackages));
  } catch {
    // Persistence is best-effort; the in-memory list still works this session.
  }
}

// Blends the four targets into one ordered result list, honouring the active scope chip.
// In "all" each group is capped so every target stays visible; a focused scope shows a
// deeper single-target list. Loaded packages rank ahead of NuGet discovery hits, which
// exclude anything already open.
function runtimeSpotlightResults(query: string): SpotlightResult[] {
  const results: SpotlightResult[] = [];
  if (state.runtimePackLoading) {
    results.push({ kind: "rtpack-status", loading: true });
    return results;
  }
  if (state.runtimePackError && !runtimePackLoaded()) {
    results.push({ kind: "rtpack-status", error: state.runtimePackError });
  }
  // Index-first: the platform library roster needs no pack download. Selecting
  // "Platform" instantly lists the CoreCLR + ASP.NET Core libraries the static
  // index knows for the active framework, filterable by name.
  const roster = platformLibraryRoster(query);
  for (const lib of roster.slice(0, 200)) {
    results.push({ ...lib, kind: "platform-lib" });
  }
  // Once a pack is resident, blend its type/member matches so drilled-in
  // platform content stays searchable alongside the library roster.
  if (platformSurfaceLoaded()) {
    const typeSource = query ? spotlightTypeMatches(query) : [];
    for (const match of typeSource.filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
      results.push({ ...match, kind: "type" });
    }
    if (query) {
      for (const match of spotlightMemberMatches(query).filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
        results.push({ ...match, kind: "member" });
      }
    }
  }
  // Only when the static index is unavailable do we fall back to the old
  // download-first prompt, so the scope is never empty and inert.
  if (!roster.length && !runtimePackLoaded()) {
    results.push({ kind: "rtpack-suggest" });
  }
  return results;
}

function spotlightResults(): SpotlightResult[] {
  const query = state.spotlightQuery.trim();
  const spotlightScope = state.spotlightScope;
  // Exhaustive scope dispatch. "all" blends the package, type and member scopes, so those four
  // arms share the composed body below instead of each owning a renderer. Adding an entry to the
  // spotlight scope catalog offers it to users immediately, so it must fail compilation here
  // until it declares which of these shapes it is.
  switch (spotlightScope) {
    case "runtime":
      return runtimeSpotlightResults(query);
    case "commands":
      // `spotlight.ts` answers the command scope from the command palette and never delegates
      // here. Reaching this arm means that interception was removed, which is a wiring failure
      // and not an empty result set.
      throw new Error("Spotlight delegated the command scope to the workspace search results.");
    case "all":
    case "packages":
    case "types":
    case "members":
      break;
    default:
      return assertNever(spotlightScope, "spotlight scope");
  }

  const all = spotlightScope === "all";
  const results: SpotlightResult[] = [];

  if (all || spotlightScope === "packages") {
    const loaded = spotlightLoadedPackageMatches(query).slice(0, all ? 3 : 20);
    for (const match of loaded) results.push({ kind: "pkg-loaded", pkg: match.pkg, ranges: match.ranges });
    const openIds = new Set(state.packages.map(pkg => pkg.id.toLowerCase()));
    // Persisted recently-opened packages that are not currently open. These carry the
    // Home listing across a refresh (the in-memory workspace is gone); re-opening one
    // refetches its nupkg (fast from the browser HTTP cache).
    const lowerQuery = query.toLowerCase();
    const recentShown = new Set();
    for (const entry of state.recentPackages || []) {
      const key = entry.id.toLowerCase();
      if (openIds.has(key) || recentShown.has(key)) continue;
      if (lowerQuery && !key.includes(lowerQuery)) continue;
      recentShown.add(key);
      results.push({ kind: "pkg-recent", entry, ranges: computeHighlightRanges(entry.id, lowerQuery) });
      if (all && recentShown.size >= 6) break;
    }
    let added = 0;
    const packageHits = visibleSpotlightPackageHits(
      query,
      state.spotlightPkgQuery,
      state.spotlightPkgHits,
    );
    for (const hit of packageHits) {
      if (openIds.has(hit.id.toLowerCase()) || recentShown.has(hit.id.toLowerCase())) continue;
      results.push({ kind: "pkg-nuget", hit, ranges: computeHighlightRanges(hit.id, query.toLowerCase()) });
      if (all && ++added >= 4) break;
    }
    results.push({
      kind: "package-query",
      prefix: validPackageQueryPrefix(query),
    });
  }
  if ((all || spotlightScope === "types") && query) {
    for (const match of spotlightTypeMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "type" });
  } else if (spotlightScope === "types" && !query) {
    for (const match of spotlightTypeMatches("").slice(0, 40)) results.push({ ...match, kind: "type" });
  }
  if ((all || spotlightScope === "members") && query) {
    for (const match of spotlightMemberMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "member" });
  }
  // Offer the runtime pack when the user is clearly hunting a platform type but it isn't
  // loaded yet — one gesture makes BCL types (TextWriter, String…) searchable session-wide.
  if ((all || spotlightScope === "types") && query.length >= 2 && !runtimePackLoaded() && !state.runtimePackLoading) {
    results.push({ kind: "rtpack-suggest" });
  }
  return results;
}

interface NugetSearchResult {
  id: string;
  version: string;
  description?: string;
}

interface NugetSearchResponse {
  data?: NugetSearchResult[];
}

interface DotnetReleaseIndexEntry {
  "channel-version": string;
  "latest-release": string;
}

function isNugetSearchResult(value: unknown): value is NugetSearchResult {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.version === "string"
    && (value.description === undefined || typeof value.description === "string");
}

function isDotnetReleaseIndexEntry(
  value: unknown,
): value is DotnetReleaseIndexEntry {
  return isRecord(value)
    && typeof value["channel-version"] === "string"
    && typeof value["latest-release"] === "string";
}

async function querySpotlightPackages(query: string): Promise<SpotlightPackageHit[]> {
  const url = `https://azuresearch-usnc.nuget.org/query?q=${encodeURIComponent(query)}&take=8&prerelease=true&semVerLevel=2.0.0`;
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const payload: unknown = await response.json();
  if (!isRecord(payload)
      || (payload.data !== undefined
        && (!Array.isArray(payload.data) || !payload.data.every(isNugetSearchResult)))) {
    throw new TypeError("NuGet search returned an invalid response.");
  }
  const typedPayload: NugetSearchResponse = payload;
  return (typedPayload.data || []).map(item => ({
    id: item.id,
    version: item.version,
    description: item.description || "",
  }));
}

// Build the <option> list for the version selector. Always includes the currently loaded
// version (even before the flatcontainer index has been fetched) so the control is never empty.
function versionOptionsHtml(pkg: AppPackage) {
  if (pkg.isRuntimePack) return platformVersionOptionsHtml(pkg);
  const idLower = pkg.id.toLowerCase();
  const fetched = state.packageVersions[idLower] ?? [];
  const versions = fetched.length ? fetched.slice() : [pkg.version];
  if (!versions.some(v => v.toLowerCase() === pkg.version.toLowerCase())) {
    versions.unshift(pkg.version);
    versions.sort(compareVersionsDesc);
  }
  return versions
    .map(v => `<option value="${escapeHtml(v)}" ${v.toLowerCase() === pkg.version.toLowerCase() ? "selected" : ""}>${escapeHtml(v)}</option>`)
    .join("");
}

// The Platform version selector's options: one entry per in-support .NET major (8+) from the
// dotnet/core releases index, each labelled with that channel's latest release — the latest
// stable patch for stable majors, the latest preview for a preview major (e.g. .NET 11). The
// option value is the TFM (net8.0 …) so a change reloads the whole Platform at that major. A
// preview major whose TFM the bundled library index doesn't carry loads (CoreLib browsing)
// but offers no library roster yet — honest, not hidden. The active TFM is always present so
// the control is never empty before the index loads.
function platformVersionOptionsHtml(pkg: AppPackage) {
  const releases = state.dotnetReleases || [];
  const list = releases.map(r => ({ tfm: r.tfm, version: r.version }));
  if (!list.some(r => r.tfm === pkg.activeFramework)) {
    list.unshift({ tfm: pkg.activeFramework, version: pkg.version });
  }
  return list
    .map(r => `<option value="${escapeHtml(r.tfm)}" ${r.tfm === pkg.activeFramework ? "selected" : ""}>${escapeHtml(r.version)}</option>`)
    .join("");
}

async function queryDotnetReleases(): Promise<DotnetRelease[]> {
  const url = "https://raw.githubusercontent.com/dotnet/core/refs/heads/main/release-notes/releases-index.json";
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const payload: unknown = await response.json();
  if (!isRecord(payload)) throw new TypeError("The .NET release index was invalid.");
  const releases = payload["releases-index"];
  if (releases !== undefined
      && (!Array.isArray(releases)
        || !releases.every(isDotnetReleaseIndexEntry))) {
    throw new TypeError("The .NET release index contained an invalid release.");
  }
  const typedReleases: DotnetReleaseIndexEntry[] = releases || [];
  return typedReleases
    .map(entry => {
      const major = parseInt(entry["channel-version"], 10);
      return {
        major,
        tfm: `net${entry["channel-version"]}`,
        version: entry["latest-release"],
      };
    })
    .filter(row => Number.isFinite(row.major) && row.major >= 8 && row.version)
    .sort((a, b) => b.major - a.major);
}

function ensureDotnetReleases() {
  return catalogRequests.ensureDotnetReleases();
}

function updatePlatformVersionSelect() {
  if (!state.package?.isRuntimePack) return;
  const select = document.querySelector("#package-version");
  if (select) select.innerHTML = versionOptionsHtml(state.package);
}

// Switch the resident Platform to a different .NET major (by TFM). Drops the current
// pseudo-package and its accumulated drilled libraries, then loads a fresh Platform for the
// chosen TFM and lands on its overview — mirroring the in-place version switch for ordinary
// packages. The engine resolves the exact latest patch for that major.
async function switchPlatformVersion(
  tfm: string,
  retryPackage: AppPackage | null = null,
  noticeRetryState: NoticeRetryState | null = null,
) {
  const pkg = runtimePackPackage() ?? retryPackage;
  if (!pkg || !tfm || tfm === pkg.activeFramework) return;
  if (noticeRetryState
    && state.queryNoticeRetryAction === noticeRetryState.action) {
    state.queryNotice = removeAppendedNotice(
      state.queryNotice,
      noticeRetryState.previous,
      noticeRetryState.appended);
    state.queryNoticeRetryAction = null;
  }
  const navigationSeq = navigationSequence.begin();
  state.home = false;
  state.loading = true;
  state.error = "";
  state.retryAction = null;
  state.loadingMessage = "Loading the .NET Platform…";
  state.loadingSubtitle = `.NET Platform · ${tfm}`;
  render();
  const runtimeResult = await loadRuntimePack(
    tfm,
    () => navigationSequence.isCurrent(navigationSeq));
  const loaded = runtimeResult.packageModel;
  if (!navigationSequence.isCurrent(navigationSeq)
    || (state.package && state.package !== pkg)) return;
  if (!loaded) {
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    const failure = runtimeResult.failureMessage
      || state.runtimePackError
      || "Couldn’t load the .NET Platform.";
    const retryState: NoticeRetryState = {
      action: null,
      previous: state.queryNotice,
      appended: "",
    };
    const retryAction = () =>
      switchPlatformVersion(tfm, pkg, retryState);
    retryState.action = retryAction;
    appendQueryNotice(
      failure,
      retryAction);
    retryState.appended = state.queryNotice;
    state.retryAction = null;
    render();
    return;
  }
  state.workspaceShareBasis = null;
  state.platformStack = [];
  activatePackage(loaded, { resetAccessibility: true });
  state.loading = false;
  state.atPackageRoot = true;
  state.packageLens = "overview";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  // A library scope from the version being switched away from doesn't necessarily carry over
  // to the new version's assembly layout; clear it like the other stale filters above so
  // defaultVisibleTypeId (which now also honors libraryScope) picks from the whole incoming
  // package rather than a possibly-stale or now-nonexistent library.
  state.libraryScope = null;
  state.selectedTypeId = defaultVisibleTypeId(loaded);
  reconcileAccessibilityFilter(loaded.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  render();
  observeAsync(loadSelectionData(), "Loading selection data");
}

function ensurePackageVersions(pkg: AppPackage | null) {
  return catalogRequests.ensurePackageVersions(pkg);
}

// Repaint just the version <select> options without a full re-render, so an async index
// fetch never disturbs focus, scroll, or the rest of the workbench.
function updateVersionSelect(idLower: string) {
  if (!state.package || state.package.id.toLowerCase() !== idLower) return;
  const select = document.querySelector("#package-version");
  if (select) select.innerHTML = versionOptionsHtml(state.package);
}

// Switch the current package to a different published version. Replaces the current tab in
// place (drops the previous version's entry) so the selector mutates this package rather than
// spawning a second tab, mirroring a browser's version picker.
async function switchPackageVersion(newVersion: string) {
  const pkg = state.package;
  if (!pkg || pkg.isRuntimePack) return;
  const id = pkg.id;
  const oldVersion = pkg.version;
  if (!newVersion || newVersion.toLowerCase() === oldVersion.toLowerCase()) return;
  const framework = pkg.activeFramework;
  await loadPackage(id, newVersion, framework, {
    replacePackage: pkg,
    invalidateWorkspaceShareBasis: true,
  });
}

async function switchPackageFramework(newFramework: string) {
  const pkg = state.package;
  if (!pkg || pkg.isRuntimePack) return;
  if (!newFramework
    || newFramework.toLowerCase() === pkg.activeFramework.toLowerCase()) return;
  await loadPackage(
    pkg.id,
    pkg.version,
    newFramework,
    {
      replacePackage: pkg,
      invalidateWorkspaceShareBasis: true,
    });
}


// Routes a blended result to the right navigation path per its kind.
function pickSpotlightResult(result: SpotlightResult) {
  if (!result) { closeSpotlight(); return; }
  switch (result.kind) {
    case "package-query":
      openPackageQueryRoute(result.prefix);
      break;
    case "pkg-loaded": pickSpotlightLoadedPackage(result.pkg); break;
    case "pkg-nuget":
      observeAsync(
        loadPackageFromSpotlight(result.hit.id, result.hit.version),
        "Loading a Spotlight package");
      break;
    case "pkg-recent":
      observeAsync(
        loadPackageFromSpotlight(
          result.entry.id,
          result.entry.version,
          result.entry.framework),
        "Loading a recent package");
      break;
    case "member":
      observeAsync(pickSpotlightMember(result), "Opening a Spotlight member");
      break;
    case "rtpack-suggest": state.spotlightScope = "runtime"; state.spotlightIndex = 0; activateRuntimePack(); break;
    case "platform-lib":
      observeAsync(
        openPlatformLibrary(result.assembly, result.pack),
        "Opening a platform library");
      break;
    case "rtpack-status": break;
    case "type":
      observeAsync(pickSpotlight(result.pkg, result.type.id), "Opening a Spotlight type");
      break;
    default:
      observeAsync(executeCommand(result.value, result), "Running a Spotlight command");
      break;
  }
}

async function loadPackageFromSpotlight(
  id: string,
  version = "latest",
  framework = "",
) {
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  const openedFromProductDemos =
    isProductHomeDemosPath(location.pathname);
  spotlight.reset();
  const catalogSnapshot = openedFromProductDemos
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  const loaded = await loadPackage(
    id,
    version,
    framework,
    catalogSnapshot
      ? {
          failureHandler: message =>
            failWorkspaceCatalogAction(
              message,
              catalogSnapshot,
              () => loadPackageFromSpotlight(id, version, framework),
              focusWorkbenchSearchOrHeading,
            ),
        }
      : {});
  if (loaded || !catalogSnapshot)
    focusTypeList(navigationGeneration, focusGeneration);
}

// Kicks off the runtime-pack load (if not already loaded/loading) and repaints the
// spotlight in place so the loading row and, once resolved, the platform types appear
// without tearing down the dialog.
function activateRuntimePack() {
  if (runtimePackLoaded() || state.runtimePackLoading) {
    spotlight.refresh();
    return;
  }
  const framework = state.package?.activeFramework || "";
  const navigationSeq = navigationSequence.current();
  const pending = loadRuntimePack(
    framework,
    () => navigationSequence.isCurrent(navigationSeq)); // sets runtimePackLoading synchronously
  spotlight.refresh();
  observeAsync(
    pending.then(() => {
      if (!state.spotlightOpen && !state.home) return undefined;
      spotlight.refresh();
      return undefined;
    }),
    "Loading the runtime pack");
}

// Drill into one platform library from the index-first Platform scope: lazily fetch just
// that assembly from its shared-framework pack (CoreCLR or ASP.NET Core), creating or
// extending the resident runtime pseudo-package, then scope the workbench to it and land in
// its type list. The download happens only here, on demand — selecting the Platform scope
// itself never downloads.
interface OpenPlatformLibraryOptions {
  scopeOnly?: boolean;
  navigationSeq?: number;
  retryAction?: RetryAction;
}

async function openPlatformLibrary(
  assembly: string,
  pack: string,
  options: OpenPlatformLibraryOptions = {},
) {
  const scopeOnly = options.scopeOnly === true;
  const navigationGeneration = scopeOnly ? null : beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  const navigationSeq = options.navigationSeq ?? navigationSequence.begin();
  if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
  const openedFromProductDemos =
    !scopeOnly && isProductHomeDemosPath(location.pathname);
  spotlight.reset();
  const catalogSnapshot = openedFromProductDemos
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  const key = (assembly || "").replace(/\.dll$/i, "");
  const fileName = key ? `${key}.dll` : "";
  const tfm = platformScopeTfm();
  const alreadyLoaded = runtimeAssemblyIsResident(
    runtimePackPackage(),
    key,
    pack);
  if (!alreadyLoaded) {
    state.home = false;
    state.loading = true;
    state.error = "";
    state.retryAction = null;
    state.loadingMessage = "Loading the platform library…";
    state.loadingSubtitle = `${key} · ${tfm}`;
    render();
    const runtimeResult = await loadRuntimePackAssembly(
      tfm,
      fileName,
      pack,
      () => navigationSequence.isCurrent(navigationSeq),
      runtimePackPackage()?.version ?? "");
    const loaded = runtimeResult.packageModel;
    if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
    if (!loaded) {
      state.loading = false;
      const failureMessage =
        runtimeResult.failureMessage || state.runtimePackError;
      const message = failureMessage
        ? `Couldn’t load ${key}: ${failureMessage}`
        : `Couldn’t load ${key} from the .NET runtime pack.`;
      if (catalogSnapshot) {
        failWorkspaceCatalogAction(
          message,
          catalogSnapshot,
          () => openPlatformLibrary(assembly, pack),
          focusWorkbenchSearchOrHeading,
        );
        return undefined;
      }
      state.error = message;
      state.errorTitle = "Platform library failed";
      state.retryAction = options.retryAction
        ?? (() => openPlatformLibrary(assembly, pack));
      render();
      return undefined;
    }
  }
  const pkg = runtimePackPackage();
  if (!pkg) { render(); return undefined; }
  activatePackage(pkg, { resetAccessibility: true });
  state.home = false;
  const actualKey = pkg.types
    .map(libraryKey)
    .find(candidate => candidate.toLowerCase() === key.toLowerCase());
  const hasLib = actualKey !== undefined;
  state.libraryScope = actualKey ? new Set([actualKey]) : null;
  if (actualKey) recordPlatformRecent(actualKey, pack);
  if (scopeOnly) return hasLib ? pkg : undefined;
  state.loading = false;
  state.atPackageRoot = !hasLib; // scoped → jump straight to the type list; otherwise the overview
  state.packageLens = "overview";
  state.namespaceFilter = "";
  state.typeFilter = "";
  state.kindFilter = "";
  const scoped = filteredTypes();
  state.selectedTypeId = scoped[0]?.id || pkg.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.selectedOverloadIndex = null;
  const selectionData = loadSelectionData();
  render();
  await selectionData;
  if (navigationGeneration != null)
    focusTypeList(navigationGeneration, focusGeneration);
  return undefined;
}

function pickSpotlightLoadedPackage(pkg: {
  id: string;
  version: string;
  activeFramework?: string;
}) {
  const target = state.packages.find(item =>
    item.id === pkg.id
    && item.version === pkg.version
    && (!pkg.activeFramework
      || item.activeFramework === pkg.activeFramework));
  if (!target) { closeSpotlight(); return; }
  const focusGeneration = beginSpotlightNavigation();
  state.home = false;
  activatePackage(target, { resetAccessibility: true });
  state.atPackageRoot = true;
  state.selectedTypeId = "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberFilters();
  resetMemberSectionState();
  spotlight.reset();
  render();
  focusTypeList(focusGeneration);
}

async function pickSpotlightMember(
  result: Extract<SpotlightResult, { kind: "member" }>,
) {
  const pkg = state.packages.find(item =>
    item.id === result.pkg.id
    && item.version === result.pkg.version
    && (!result.pkg.activeFramework
      || item.activeFramework === result.pkg.activeFramework));
  const type = pkg?.types?.find(item => item.id === result.type.id);
  if (!pkg || !type) { closeSpotlight(); return; }
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  state.home = false;
  activatePackage(pkg);
  state.atPackageRoot = false;
  state.selectedTypeId = type.id;
  state.lens = "api";
  resetMemberFilters();
  state.memberBrowseTypeId = type.id;
  state.selectedMemberKey = result.memberKey;
  state.selectedOverloadIndex = null;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  spotlight.reset();
  resetMemberSectionState();
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  render();
  await loadSelectedMemberDocumentation();
  focusTypeList(navigationGeneration, focusGeneration);
}

async function pickSpotlight(
  packageResult: { id: string; version: string; activeFramework?: string },
  typeId: string,
) {
  const pkg = state.packages.find(item =>
    item.id === packageResult.id
    && item.version === packageResult.version
    && (!packageResult.activeFramework
      || item.activeFramework === packageResult.activeFramework));
  const type = pkg?.types?.find(item => item.id === typeId);
  if (!pkg || !type) {
    closeSpotlight();
    return;
  }
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  state.home = false;
  activatePackage(pkg);
  state.atPackageRoot = false;
  state.selectedTypeId = type.id;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  state.selectedBodyTarget = null;
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphKey = "";
  state.memberCallGraphError = "";
  state.graphMemberNavigationError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  spotlight.reset();
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  const selectionData = loadSelectionData();
  render();
  await selectionData;
  if (navigationGeneration !== spotlightFocusGeneration) return;
  requestAnimationFrame(() => {
    if (navigationGeneration !== spotlightFocusGeneration) return;
    document.querySelector(`[data-type="${CSS.escape(state.selectedTypeId)}"]`)?.scrollIntoView({ block: "nearest" });
  });
  focusTypeList(navigationGeneration, focusGeneration);
}

function executeCommand(
  value: string,
  result: CommandPaletteResult | null = null,
) {
  const pkg = currentPackage();
  beginSpotlightNavigation();
  const [verb, ...rest] = value.split(/\s+/);
  const argument = rest.join(" ");
  let operation;
  if (verb === "type") {
    const match = result?.targetTypeId
      ? pkg.types.find(item => item.id === result.targetTypeId)
      : pkg.types.find(item => item.name.toLowerCase() === argument.toLowerCase())
        || pkg.types.find(item => item.name.toLowerCase().includes(argument.toLowerCase()));
    if (match) {
      state.selectedTypeId = match.id;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      resetMemberFilters();
      operation = loadSelectionData();
    }
  } else if (verb === "show") {
    const match = typeLensesFor(state.package)
      .find(([id, label]) =>
        id === argument.toLowerCase()
        || label.toLowerCase() === argument.toLowerCase());
    if (match) {
      state.lens = match[0];
      operation = loadSelectionData();
    }
  } else if (verb === "framework" && pkg.frameworks.includes(argument)) {
    operation = switchPackageFramework(argument);
  } else if (verb === "package") {
    const [id, version = "latest"] = argument.split("@");
    if (id) operation = loadPackage(id, version, "");
  } else if (verb === "clear") {
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
  } else if (verb === "find" || verb === "types") {
    state.typeFilter = argument.replace(/^public\s*/, "");
  } else if (verb === "share") {
    dispatchApplicationAction("share");
  } else if (verb === "settings") {
    dispatchApplicationAction("settings");
  } else if (value === "keyboard help") {
    dispatchApplicationAction("keyboard-help");
  }
  state.history = [value, ...state.history.filter(item => item !== value)].slice(0, 5);
  return operation;
}

function focusFilter(
  { immediate = false }: { immediate?: boolean } = {},
) {
  if (contentFrameUsesPush()
    && contentFrameMedia.matches
    && contentFramePane !== "navigation") {
    contentFramePane = "navigation";
    render({ synchronizeUrl: false });
  }
  const focus = () => {
    const input = document.querySelector<HTMLInputElement>(
      "#member-filter, #type-filter");
    if (!input) return;
    const memberDisclosure = input.closest<HTMLDetailsElement>(
      "[data-member-filter-disclosure]");
    const typeDisclosure = input.closest<HTMLDetailsElement>(
      "[data-type-filter-disclosure]");
    const disclosure = memberDisclosure ?? typeDisclosure;
    if (disclosure && !disclosure.open) {
      if (memberDisclosure)
        state.memberFiltersExpanded = true;
      else
        state.typeFiltersExpanded = true;
      disclosure.open = true;
    }
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  };
  if (immediate) {
    focus();
    return;
  }
  requestAnimationFrame(focus);
}

const memberFocusRestorer = createMemberFocusRestorer();

function captureContentFrameReplacementAuthority():
ContentFrameReplacementAuthority {
  const focused = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null;
  return {
    owner: contentFrameFocusOwnerFor(focused),
    focusGeneration: documentFocusGeneration,
  };
}

function scheduleMemberFocusAfterRender(
  preserved: MemberFocusSnapshot,
  replacementAuthority: ContentFrameReplacementAuthority,
) {
  if (replacementAuthority.owner !== null
    && replacementAuthority.focusGeneration === documentFocusGeneration)
    contentFrameReplacementAuthority = replacementAuthority;
  memberFocusRestorer.schedule(
    document,
    preserved,
    requestAnimationFrame,
    () => replacementAuthority.focusGeneration === documentFocusGeneration);
  requestAnimationFrame(() => {
    if (contentFrameReplacementAuthority === replacementAuthority)
      contentFrameReplacementAuthority = null;
  });
}

function renderWithMemberFocus(preserved: MemberFocusSnapshot) {
  const replacementAuthority = captureContentFrameReplacementAuthority();
  render();
  scheduleMemberFocusAfterRender(preserved, replacementAuthority);
  return preserved;
}

function renderPreservingMemberFocus(
  fallback: MemberFocusSnapshot | null = null,
) {
  const applicationMenuHadFocus = applicationMenuOwnsFocus(document);
  const current = captureMemberFocus(document);
  const preserved = memberFocusRestorer.resolve(current, fallback);
  if (applicationMenuHadFocus) {
    render();
    return preserved;
  }
  return renderWithMemberFocus(preserved);
}

function workbenchOverlayOwnsFocus() {
  return workbenchModalOwnsFocus();
}

function workbenchModalOwnsFocus() {
  return state.spotlightOpen
    || state.graphSourceOpen
    || state.docViewerOpen
    || state.memberAnnotatedModal !== null
    || graphExplorer.isOpen;
}

function resolvedWorkspaceShareTabs():
BrowserWorkspaceShareState["tabs"] {
  return state.packages.map((pkg, index) => ({
    id: `t${index}`,
    kind: pkg.isRuntimePack ? "group" : "package",
    source: pkg.isRuntimePack ? ":Platform" : pkg.id,
    version: pkg.version || null,
    framework: pkg.activeFramework || null,
    runtimeIdentifier: null,
  }));
}

function capturedShareTabs() {
  const basis = state.workspaceShareBasis;
  const resolvedTabs = resolvedWorkspaceShareTabs();
  const preservesBasis = Boolean(basis
    && workspaceShareTabsMatchResolved(basis.tabs, resolvedTabs));
  const tabs: BrowserWorkspaceShareState["tabs"] =
    preservesBasis && basis
      ? basis.tabs.map(tab => ({ ...tab }))
      : resolvedTabs;
  return { tabs, preservesBasis };
}

function commitWorkspaceShareBasis(
  basis: BrowserWorkspaceShareState | null,
) {
  state.workspaceShareBasis = basis;
  sourceInspection.clearGraphSource();
}

function selectedCallGraphWorkspacePackages(): AppPackage[] {
  if (state.package?.isRuntimePack) return [];
  const basis = state.workspaceShareBasis;
  const { tabs, preservesBasis } = capturedShareTabs();
  const activeIndex = state.package
    ? state.packages.indexOf(state.package)
    : -1;
  const activeTab = tabs[activeIndex];
  if (!activeTab) {
    throw new Error(
      "The active package is no longer part of the Browser workspace.");
  }
  const packageForTabId = (id: string) => {
    const index = tabs.findIndex(tab => tab.id === id);
    return index >= 0 ? state.packages[index] ?? null : null;
  };
  if (!preservesBasis || !basis) {
    return browserCreatedCallGraphTabIds(tabs, activeIndex)
      .map(packageForTabId)
      .filter((pkg): pkg is AppPackage =>
        pkg !== null && !pkg.isRuntimePack);
  }

  const packageTabIds = selectedBrowserCallGraphPackageTabIds(basis);
  if (!packageTabIds.includes(activeTab.id)) {
    throw new Error(
      "The active package is not part of the selected Call Graph context.");
  }
  return packageTabIds.map(id => {
    const packageModel = packageForTabId(id);
    if (!packageModel || packageModel.isRuntimePack) {
      throw new Error(
        "The selected Call Graph context could not be realized by this browser.");
    }
    return packageModel;
  });
}

function captureWorkspaceUrlState(): WorkspaceUrlState | null {
  if (!state.package) return null;
  const workspaceSubjectOpen = scope() === "workspace";
  if (state.atPackageRoot && !workspaceSubjectOpen) {
    throw new Error(
      "Package views do not yet have product-owned share facet identities.");
  }
  if (!workspaceSubjectOpen && state.pendingGraphMemberDeepLink) {
    throw new Error(
      "The pending graph member must resolve before this workspace can be shared.");
  }

  const captured = capturedShareTabs();
  const tabs = captured.tabs;
  const activeIndex = state.packages.indexOf(state.package);
  const activeTab = tabs[activeIndex];
  if (!activeTab) return null;

  const basis = state.workspaceShareBasis;
  const { contexts, selectedContextId } = workspaceShareCaptureTopology(
    tabs,
    activeIndex,
    basis,
    captured.preservesBasis,
    !workspaceSubjectOpen && state.memberSection === "call-graph");

  const type = workspaceSubjectOpen ? null : selectedType();
  const member = workspaceSubjectOpen ? null : selectedMember(type);
  let memberAnchor: string | null = null;
  let memberSignature: string | null = null;
  if (member) {
    const overloadIndex = state.selectedOverloadIndex
      ?? (member.overloads.length === 1 ? 0 : null);
    if (overloadIndex === null) {
      throw new Error(
        "Select a concrete overload before sharing this member view.");
    }
    const overload = member.overloads[overloadIndex];
    if (!overload) {
      throw new Error(
        "The selected overload is no longer available and cannot be shared.");
    }
    if (overload.graphOnly) {
      throw new Error(
        "Graph-discovered members cannot be shared until workspace packets carry their portable target identity.");
    }
    memberAnchor = overload.anchorDigest || null;
    memberSignature = memberAnchor ? null : overload.canonicalSignature || null;
    if (!memberAnchor && !memberSignature) {
      throw new Error(
        "The selected overload has no portable product identity and cannot be shared.");
    }
    if (state.memberSection !== "overview"
      && overload.bodySelectors.length > 1) {
      throw new Error(
        "This accessor-specific section cannot be shared until workspace packets carry portable body identity.");
    }
  }

  if (state.libraryScope && state.libraryScope.size > 1) {
    throw new Error(
      "Select one library before sharing this Browser workspace.");
  }
  const libraries = state.libraryScope
    ? [...state.libraryScope].sort((left, right) =>
        left < right ? -1 : left > right ? 1 : 0)
    : [];
  return {
    package: state.package.id,
    subject: workspaceSubjectOpen ? "workspace" : null,
    tabs,
    contexts,
    activeTabId: activeTab.id,
    selectedContextId,
    view: {
      lens: workspaceSubjectOpen ? null : state.lens,
      type: workspaceSubjectOpen ? null : state.selectedTypeId || null,
      memberAnchor,
      memberSignature,
      section: member && state.memberSection !== "overview"
        ? state.memberSection
        : null,
      libraries,
    },
  };
}

function buildStateUrl(base = location.href) {
  if (scope() === "workspace") {
    const snapshot = captureWorkspaceUrlState();
    return snapshot
      ? workspaceLocation.build(snapshot, base)
      : new URL(base);
  }
  if (state.atPackageRoot && state.package) {
    return buildPackageRootStateUrl(base, {
      package: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      lens: state.packageLens,
    });
  }
  const snapshot = captureWorkspaceUrlState();
  return snapshot
    ? workspaceLocation.build(snapshot, base)
    : new URL(base);
}

// Rewrite the address bar to reflect the current selection so a refresh restores it and
// the URL is always shareable. replaceState (not pushState) keeps the app's own
// back/forward buttons authoritative and avoids flooding browser history on every render.
function workspaceUrlProjection() {
  return JSON.stringify({
    packages: state.packages.map(packageIdentityKey),
    basis: state.workspaceShareBasis,
    view: captureView(),
  });
}

function syncUrl() {
  if (currentPackageQueryHandoff()) return;
  if (pendingDemoNavigation
    && navigationSequence.isCurrent(pendingDemoNavigation.navigationSeq)) return;
  if (retainFailedWorkspaceUrl()) return;
  try {
    const pushFromProductDemos =
      isProductHomeDemosPath(location.pathname);
    if (state.atPackageRoot && state.package && !state.loading) {
      document.title = `dotnet-inspect -- ${packageDisplayName(state.package)}`;
      const destination = buildStateUrl().toString();
      if (pushFromProductDemos) {
        workspaceLocation.push(destination);
      } else {
        workspaceLocation.replace(destination, history.state);
      }
      return;
    }
    const snapshot = captureWorkspaceUrlState();
    if (!snapshot || state.loading) return;
    document.title = `dotnet-inspect -- ${packageDisplayName(state.package)}`;
    if (pushFromProductDemos) {
      workspaceLocation.push(
        workspaceLocation.build(snapshot).toString());
    } else {
      workspaceLocation.sync(snapshot, history.state);
    }
  } catch {
    // Keep the current URL while the active Browser state is not projectable.
  }
}

// Apply a parsed URL selection onto the currently loaded package, validating that the
// type/member/overload/section still exist.
type DeepLink = WorkspaceDeepLink;

function solePortableBodyTarget(
  overload: AppMemberSurface,
): BodyTarget | null {
  const selector = overload.bodySelectors.length === 1
    ? overload.bodySelectors[0]
    : null;
  return selector
    ? {
        memberName: selector.memberName,
        selectorKey: selector.selectorKey,
        metadataToken: selector.token,
      }
    : null;
}

function canonicalViewRestorationFailure(
  pkg: AppPackage,
  deep: DeepLink,
  requestedLens: TypeLens | null,
): string | null {
  const lens = requestedLens ?? "api";
  if (!typeLensesFor(pkg).some(([id]) => id === lens)) {
    return `The shared '${lens}' lens is not available for ${pkg.id}.`;
  }
  if (lens !== "api" && !deep.type) {
    return `The shared '${lens}' lens requires a selected type.`;
  }
  const requestedType = deep.type
    ? pkg.types.find(type => type.id === deep.type)
    : null;
  if (deep.type && !requestedType) {
    return `The shared type '${deep.type}' is no longer available.`;
  }
  if (requestedType
    && state.libraryScope
    && !state.libraryScope.has(libraryKey(requestedType))) {
    return `The shared type '${deep.type}' is not part of the selected library.`;
  }
  const hasPortableMember = Boolean(
    deep.memberAnchor || deep.memberSignature);
  if (deep.section && !hasPortableMember) {
    return `The shared member section '${deep.section}' requires a selected member.`;
  }
  if (!hasPortableMember) return null;
  if (lens !== "api") {
    return `The shared member selection is not available in the '${lens}' lens.`;
  }
  if (!deep.type) {
    return "The shared member has no declaring type and cannot be restored.";
  }

  if (!requestedType) {
    return `The shared member's declaring type '${deep.type}' is no longer available.`;
  }
  const matches = memberGroups(requestedType).flatMap(group =>
    group.overloads.map(overload => ({
      group,
      overload,
    }))).filter(candidate =>
      deep.memberAnchor
        ? candidate.overload.anchorDigest === deep.memberAnchor
        : candidate.overload.canonicalSignature === deep.memberSignature);
  if (matches.length === 0) {
    return "The shared member is no longer available.";
  }
  if (matches.length > 1) {
    return "The shared member identity is ambiguous.";
  }

  const selection = matches[0]!;
  const hasSelectedBody =
    solePortableBodyTarget(selection.overload) !== null;
  if (deep.section
    && !memberSectionIdsFor(
      selection.group,
      pkg.isRuntimePack,
      hasSelectedBody).includes(deep.section)) {
    return `The shared member section '${deep.section}' is not available for this member.`;
  }
  return null;
}

function applyDeepLink(deep: DeepLink | null | undefined) {
  const pkg = state.package;
  if (!pkg) return;
  // Every caller reaches this from a URL/history-driven restore (initial load, workspace
  // restore, back/forward, or an explicit deep link passed to loadPackage), never from an
  // in-app link click that means to preserve the current type-list filter. Clear the
  // type/namespace/kind filters so a value left over from Browse elsewhere doesn't hide the
  // restored type from the list (library scope is deliberately left alone: for a platform
  // link it is already restored by applyPlatformLibraryScope, and for a package link it is
  // restored from the selected canonical library before this runs).
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberSourceKey = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.memberAnnotatedKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberFactsKey = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberCallGraphExpanding = false;
  state.memberCallGraphSeq++;
  invalidateGraphMemberNavigation();
  state.selectedBodyTarget = null;
  state.platformStack = [];
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  const restoreType = deep?.type && pkg.types.some(item => item.id === deep.type);
  resetMemberFilters();
  state.selectedTypeId = restoreType
    ? deep?.type ?? ""
    : defaultVisibleTypeId(pkg);
  // The restored/defaulted type may sit outside the current accessibility bucket or the
  // platform's library scope (e.g. an internal type reached via a shared link, or a history
  // entry for a type in a library the session had since scoped away from). Reconcile both
  // filters against the actual selected type so the type list and the displayed type stay
  // aligned, instead of showing an unrelated first type -- or an empty list -- while the pane
  // renders the restored one.
  const selected = pkg.types.find(item => item.id === state.selectedTypeId);
  if (selected) {
    reconcileAccessibilityFilter(selected);
    // For the platform pseudo-package, only clear the restored scope if the selected type
    // doesn't actually belong to it --
    // defaultVisibleTypeId now prefers a type within libraryScope even when none of that
    // scope's types pass the accessibility filter (see its own comment), so this should only
    // trigger when the scope's library genuinely has no types at all.
    if (isRuntimePackId(pkg.id)) {
      if (state.libraryScope && !state.libraryScope.has(libraryKey(selected))) {
        state.libraryScope = null;
      }
    }
  }

  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  if (deep?.graphTarget && !restoreType) {
    appendQueryNotice(
      "The shared graph member's declaring type is no longer available and was not opened.");
  } else if (restoreType && deep) {
    const type = pkg.types.find(item => item.id === deep.type);
    if (!type) return;
    revealTypeInFilters(type);
    const groups = memberGroups(type);
    state.memberTextFilter = deep.memberTextFilter || "";
    state.memberKindFilter = deep.memberKindFilter
      && memberKinds(type).includes(deep.memberKindFilter)
      ? deep.memberKindFilter
      : "all";
    state.memberAccessibilityFilter = deep.memberAccessibilityFilter
      && memberAccessibilities(type).includes(deep.memberAccessibilityFilter)
      ? deep.memberAccessibilityFilter
      : "all";
    state.memberTraitFilter = deep.memberTraitFilter
      && MEMBER_TRAITS.some(([property]) =>
        property === deep.memberTraitFilter)
      ? deep.memberTraitFilter
      : "";
    if (deep.memberBrowse && groups.length)
      state.memberBrowseTypeId = type.id;
    const portableMatches = deep.memberAnchor || deep.memberSignature
      ? groups.flatMap(group =>
          group.overloads.map((overload, overloadIndex) => ({
            group,
            overload,
            overloadIndex,
          }))).filter(candidate =>
            deep.memberAnchor
              ? candidate.overload.anchorDigest === deep.memberAnchor
              : candidate.overload.canonicalSignature === deep.memberSignature)
      : [];
    let restoredPortableMember = false;
    if (deep.memberAnchor || deep.memberSignature) {
      if (portableMatches.length === 1) {
        const selection = portableMatches[0]!;
        state.memberBrowseTypeId = type.id;
        state.selectedMemberKey = selection.group.key;
        state.selectedOverloadIndex = selection.overloadIndex;
        const portableBodyTarget =
          solePortableBodyTarget(selection.overload);
        const hasSelectedBody = portableBodyTarget !== null;
        if (deep.section
          && isMemberSection(deep.section)
          && memberSectionIdsFor(
            selection.group,
            state.package?.isRuntimePack,
            hasSelectedBody).includes(deep.section)) {
          state.memberSection = deep.section;
          state.selectedBodyTarget = portableBodyTarget;
        }
        restoredPortableMember = true;
      } else {
        appendQueryNotice(
          portableMatches.length === 0
            ? "The shared member is no longer available and was not opened."
            : "The shared member identity is ambiguous and was not opened.");
      }
    }
    if (!restoredPortableMember) {
    const group = deep.member ? groups.find(item => item.key === deep.member) : null;
    const graphCandidate = deep.member && deep.graphTarget
      ? pkg.isRuntimePack
        ? resolveRuntimeGraphTargetCandidate(pkg, deep.graphTarget)
        : resolveLoadedGraphTargetCandidate([pkg], deep.graphTarget)
      : null;
    const localGraphSelection =
      deep.graphTarget
        && graphCandidate?.status === "unique"
        && graphCandidate.type === type
        ? findGraphMemberSelection(type, deep.graphTarget)
        : null;
    const disposition =
      pkg.isRuntimePack && deep.member && deep.graphTarget && !localGraphSelection
        ? "mismatch"
        : graphMemberDeepLinkDisposition(
            deep,
            graphCandidate,
            type,
            group,
            localGraphSelection);
    if (disposition === "local"
      && localGraphSelection
      && deep.graphTarget) {
      state.selectedMemberKey = localGraphSelection.group.key;
      state.selectedOverloadIndex = localGraphSelection.overloadIndex;
      state.selectedBodyTarget = retainGraphOnlyImplementationBody(
        localGraphSelection.group.overloads[localGraphSelection.overloadIndex],
        deep.graphTarget);
      state.memberSection = deep.section
        && isMemberSection(deep.section)
        && memberSectionIdsFor(
          localGraphSelection.group,
          state.package?.isRuntimePack,
          true).includes(deep.section)
        ? deep.section
        : "overview";
    } else if (disposition === "graph"
      && deep.member
      && deep.graphTarget) {
      const overloadIndex = Number(deep.overload);
      state.selectedMemberKey = deep.member;
      state.selectedOverloadIndex =
        Number.isInteger(overloadIndex) && overloadIndex >= 0
          ? overloadIndex
          : null;
      state.memberSection = deep.section && isMemberSection(deep.section)
        ? deep.section
        : "overview";
      state.selectedBodyTarget = deep.graphTarget;
      state.pendingGraphMemberDeepLink = {
        packageKey: packageIdentityKey(pkg),
        viewSignature: viewSignature(),
        type: deep.type ?? type.id,
        member: deep.member,
        overload: deep.overload ?? null,
        section: deep.section ?? null,
        target: deep.graphTarget
      };
    } else if (disposition === "mismatch") {
      appendQueryNotice(
        "The shared graph member no longer matches this package and was not opened.");
    } else if (disposition === "public" && group && deep.member) {
      state.memberBrowseTypeId = type.id;
      state.selectedMemberKey = deep.member ?? "";
      const overloadIndex = Number(deep.overload);
      if (deep.overload != null && deep.overload !== ""
        && Number.isInteger(overloadIndex) && overloadIndex >= 0
        && overloadIndex < group.overloads.length) {
        state.selectedOverloadIndex = overloadIndex;
      }
      const restoredOverload = group.overloads[
        state.selectedOverloadIndex ?? (group.overloads.length === 1 ? 0 : -1)];
      const hasSelectedBody = bodyTargetMatchesOverload(
        deep.bodyTarget,
        group,
        restoredOverload);
      if (deep.section
        && isMemberSection(deep.section)
        && memberSectionIdsFor(
          group,
          state.package?.isRuntimePack,
          hasSelectedBody).includes(deep.section)) {
        state.memberSection = deep.section;
      }
      if (hasSelectedBody) {
        state.selectedBodyTarget = deep.bodyTarget ?? null;
      }
    }
    }
  }
  state.typeCursor = Math.max(0, filteredTypes().findIndex(item => item.id === state.selectedTypeId));
}

// Kick off the async data load implied by the current lens/section so a restored or
// history-navigated view fills in its content.
// Returns the loader for a type-level lens, or the sentinel `"member"` when the lens
// defers to the member-section loaders below. A new type lens used to fall through the
// `state.lens !== "api"` test and silently fetch nothing.
function loadSelectedTypeLensData(): Promise<void> | undefined | "member" {
  switch (state.lens) {
    case "source": return loadSelectedTypeSource();
    case "metadata": return loadSelectedTypeMetadata();
    case "api": return "member";
  }
  return assertNever(state.lens, "type lens");
}

function loadSelectionData() {
  if (state.pendingGraphMemberDeepLink
    && !graphMemberPendingMatchesView(
      state.pendingGraphMemberDeepLink,
      packageIdentityKey(state.package),
      viewSignature())) {
    invalidateGraphMemberNavigation();
  }
  if (state.pendingGraphMemberDeepLink) {
    return restorePendingGraphMember();
  }
  if (state.atPackageRoot) return undefined;
  const typeLensLoad = loadSelectedTypeLensData();
  if (typeLensLoad !== "member") return typeLensLoad;
  if (!state.selectedMemberKey) return undefined;
  const member = selectedMember(selectedType());
  if (!member) return undefined;
  if (member.overloads.length > 1 && state.selectedOverloadIndex == null) return undefined;
  switch (state.memberSection) {
    case "source": return loadSelectedMemberSource();
    case "annotated": return loadSelectedMemberAnnotatedSource();
    case "call-graph": return loadSelectedMemberCallGraph();
    case "facts": return loadSelectedMemberFacts();
    case "overview": return loadSelectedMemberDocumentation();
    default: return assertNever(state.memberSection, "member section");
  }
}

async function share() {
  const focusOwner = captureApplicationMenuFocusOwner(document);
  try {
    await navigator.clipboard?.writeText(buildStateUrl().toString());
    showToast("selection link copied");
  } catch (error) {
    state.queryNotice = errorMessage(error);
    state.queryNoticeRetryAction = null;
    render();
  } finally {
    requestAnimationFrame(() =>
      restoreApplicationMenuFocusIfOwned(document, focusOwner));
  }
}

function showToast(message: string, duration = 2200) {
  document.querySelector(".toast")?.remove();
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.setAttribute("role", "status");
  toast.setAttribute("aria-live", "polite");
  toast.textContent = message;
  document.body.append(toast);
  setTimeout(() => toast.remove(), duration);
}

// Turns a raw inspection failure into a friendly, actionable message. A mistyped package
// name surfaces as a NuGet 404; call that out plainly instead of showing a stack trace.
function friendlyLoadError(
  error: unknown,
  packageId: string,
  version: string | null | undefined,
) {
  const raw = errorMessage(error);
  if (/\b404\b|not\s*found/i.test(raw)) {
    const suffix = version && version !== "latest" ? `@${version}` : "";
    return {
      notFound: true,
      title: "Package not found",
      message: `Package “${packageId}${suffix}” wasn’t found on NuGet. Check the spelling — names are case-insensitive — and try again.`
    };
  }
  return {
    notFound: false,
    title: "Inspection query failed",
    message: `Couldn’t load “${packageId}”: ${raw || "unknown error"}`
  };
}

function appendQueryNotice(message: string, retryAction: RetryAction = null) {
  if (!message) return;
  state.queryNotice = state.queryNotice
    ? `${state.queryNotice} ${message}`
    : message;
  state.queryNoticeRetryAction = retryAction;
}

function visibleQueryNotice() {
  const routeNotice = failedWorkspaceUrlState?.kind === "route"
    ? failedWorkspaceUrlState.notice
    : null;
  return [state.queryNotice, routeNotice]
    .filter(Boolean)
    .join(" ");
}

function renderQueryNotice() {
  const notice = visibleQueryNotice();
  return notice
    ? `<div class="query-notice" role="alert">
        <span class="query-notice-glyph">⚠</span>
        <span class="query-notice-text">${escapeHtml(notice)}</span>
        ${state.queryNotice && state.queryNoticeRetryAction
          ? '<button id="retry-notice" type="button">retry</button>'
          : ""}
        <button id="dismiss-notice" type="button" aria-label="Dismiss">×</button>
      </div>`
    : "";
}

function retainFailedWorkspaceUrl() {
  const failedState = failedWorkspaceUrlState;
  const retainedState = retainWorkspaceUrlPreservation(
    failedState,
    location.href,
    workspaceUrlProjection());
  if (retainedState) return true;
  if (failedState?.kind === "route"
    && !recoverWorkspaceRouteFailure(
      failedState,
      location,
      url => workspaceLocation.replace(url, history.state))) {
    return true;
  }
  failedWorkspaceUrlState = null;
  return false;
}

function clearWorkspaceRouteFailure(recoveryUrl?: string) {
  if (failedWorkspaceUrlState?.kind !== "route") return true;
  if (!recoverWorkspaceRouteFailure(
    failedWorkspaceUrlState,
    location,
    url => workspaceLocation.replace(url, history.state),
    recoveryUrl)) {
    return false;
  }
  failedWorkspaceUrlState = null;
  return true;
}

function dismissQueryNotice() {
  const routeFailureOnHome =
    failedWorkspaceUrlState?.kind === "route" && state.home;
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  if (!clearWorkspaceRouteFailure(routeFailureOnHome ? "/" : undefined)) {
    render();
    return;
  }
  failedWorkspaceUrlState = null;
  render();
}

async function copyText(value: string, confirmation: string) {
  try {
    await navigator.clipboard.writeText(value);
    showToast(confirmation);
  } catch {
    showToast("clipboard access was denied");
  }
}

// The intro/home page shown on a bare visit: what the tool is, a persistent Spotlight-style
// search, and a few demo entry points. The search reuses the Spotlight machinery in place
// (shared #spotlight-input / #spotlight-chips / #spotlight-results ids), so results, scope
// chips, NuGet discovery, and result picking all behave exactly like the modal Spotlight.
function renderHomeView() {
  document.title = "dotnet-inspect -- Inspect any NuGet package: types, methods, metadata, decompilation.";
  const enginePending = !state.engineReady;
  const showReadyGlint = state.engineReady && homeReadyGlintPending;
  if (showReadyGlint) homeReadyGlintPending = false;
  if (state.engineReady && homeBotAnimationStartedAt === null) {
    homeBotAnimationStartedAt = performance.now();
  }
  const botAnimationDelay = homeBotAnimationStartedAt === null
    ? 0
    : -((performance.now() - homeBotAnimationStartedAt)
      % HOME_BOT_ANIMATION_DURATION_MS);
  app.innerHTML = `
    <div class="home"${state.settings ? " inert" : ""}>
      <header class="home-bar">
        ${renderBrand()}
        <div class="home-bar-actions">
          <a class="home-link" href="https://github.com/richlander/dotnet-inspect" target="_blank" rel="noreferrer">GitHub</a>
          <button id="home-settings" aria-label="Open settings" title="Settings">⚙</button>
          <button id="home-theme" aria-label="Switch theme">${state.theme === "dark" ? "light" : "dark"}</button>
        </div>
      </header>
      ${visibleQueryNotice()
        ? `<div class="query-notice" role="alert">
            <span class="query-notice-glyph">⚠</span>
            <span class="query-notice-text">${escapeHtml(visibleQueryNotice())}</span>
            <button id="dismiss-notice" type="button" aria-label="Dismiss">×</button>
          </div>`
        : ""}
      <main class="home-hero">
        <div class="home-copy">
          <p class="home-kicker">Browser-native · WebAssembly · zero install</p>
          <h1 class="home-title">Inspect any NuGet package: types, methods, metadata, decompilation.</h1>
          <p class="home-lede">Explore NuGet packages and the .NET platform — types, members, public API surface, dependencies, call graphs, and decompiled C# — all computed locally in your browser. Nothing to install, nothing uploaded.</p>
          <div class="home-search ${enginePending ? "engine-pending" : ""}" role="search" aria-busy="${enginePending}">
            ${spotlight.inlineHtml(enginePending, showReadyGlint)}
            ${enginePending
              ? `<div class="home-engine-status" role="status" aria-live="polite">
                  <span class="loader" aria-hidden="true"></span>
                  <strong>${escapeHtml(state.engineStatus)}</strong>
                </div>`
              : ""}
          </div>
          <p class="home-availability">Also available as a <a href="https://www.nuget.org/packages/dotnet-inspect" target="_blank" rel="noreferrer">CLI tool</a> and <a href="https://github.com/richlander/dotnet-skills" target="_blank" rel="noreferrer">agent skill</a>.</p>
          <p class="home-attribution">Built with .NET 11, WebAssembly, TypeScript 7, NuGet, and System.Reflection.Metadata. <a id="home-credits" href="/credits">Credits</a></p>
          <div class="home-demos">
            <span class="home-demos-label">Explore product demos</span>
            <div class="home-demo-row" aria-busy="${enginePending}">
              ${homeDemosEntryHtml(
                enginePending,
                productHomeDemoCatalogError,
                escapeHtml)}
            </div>
          </div>
        </div>
        <aside class="home-art ${enginePending ? "engine-pending" : "engine-ready"}" style="--home-bot-animation-delay: ${botAnimationDelay}ms">${homeArtSvg()}</aside>
      </main>
      ${statusBarHtml({
        variant: "home",
        ready: state.engineReady,
        buildIdentity: state.buildIdentity,
        diagnostics: state.diag,
        compactDiagnostics: true,
        expanded: state.statusBarExpanded,
      }, escapeHtml)}
    </div>
    ${state.settings ? renderSettingsViewHtml() : ""}`;
  bindHomeEvents();
  if (state.settings) {
    document.querySelector<HTMLElement>("#settings-title")
      ?.focus({ preventScroll: true });
  }
}

// The hero mascot: dotnet-bot inspecting through a magnifying glass (official dotnet/brand
// character, CC0). Rendered as a plain <img> so it scales crisply and keeps its transparent
// background on either theme; the .home-art frame reserves and centers the slot.
function homeArtSvg() {
  return `<img class="home-art-img" src="/assets/dotnet-inspect-bot.png" width="680" height="680" alt="dotnet-bot inspecting through a magnifying glass" />`;
}

const homeShellActions: HomeShellBindingActions = {
  onDismissNotice: dismissQueryNotice,
  onOpenDemos: openProductDemos,
  onOpenCredits: openCredits,
  onToggleTheme: toggleTheme,
};

function bindHomeEvents() {
  bindStatusBarEvents();
  bindSettingsPanelEvents();
  bindHomeShell(document, homeShellActions);
  spotlight.bind(document, "inline");
  afterCurrentNavigationFrame(() => {
    const input =
      document.querySelector<HTMLInputElement>("#spotlight-input");
    if (input
      && state.packageQueryReturnFocusPending
      && state.packageQueryReturnFocus === "home-search") {
      input.focus();
      state.packageQueryReturnFocus = null;
      state.packageQueryReturnFocusPending = false;
    } else if (state.packageQueryReturnFocusPending
      && state.packageQueryReturnFocus === "home-search"
      && focusLevelOneHeading()) {
      state.packageQueryReturnFocus = null;
      state.packageQueryReturnFocusPending = false;
    } else {
      input?.focus();
    }
  });
}

function openProductDemos(): void {
  navigationSequence.begin();
  state.loading = false;
  clearNavigationError();
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  state.home = false;
  state.credits = false;
  state.packageQueryOpen = false;
  packageQueryController.cancel();
  spotlight.reset();
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  workspaceLocation.push("/demos");
  render();
  afterCurrentNavigationFrame(() =>
    focusWorkspace(document));
}

// Workspace demo actions use product ids from engine `listHomeDemos` /
// `resolveHomeDemo` (`EcosystemPackCatalog` / CLI `demo <id>`). Type views
// restore via share deep links built from the resolved projection;
// member-bound Call Graph demos execute through one generated engine operation
// over the product-resolved workspace and view.
function openDefaultWorkspace(): void {
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  render();
  afterCurrentNavigationFrame(() =>
    focusWorkspace(document));
}

function runHomeDemo(kind: ProductHomeDemoId) {
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  const snapshot = captureCanonicalWorkspaceRestoreSnapshot();
  let resolveResult: BrowserHomeDemoResolveResult;
  try {
    resolveResult = inspectResolveHomeDemo(kind);
  } catch (error) {
    failDemoWorkspaceOpen(
      kind,
      errorMessage(error),
      snapshot,
      true);
    return;
  }
  const resolved = resolveResult.found ? resolveResult.demo : null;
  if (!resolved) {
    failDemoWorkspaceOpen(
      kind,
      `Unknown product home demo '${kind}'.`,
      snapshot,
      false);
    return;
  }
  state.home = false;
  let link: string | null;
  try {
    link = productHomeDemoLocationHref(
      resolved,
      inspectEncodeWorkspaceShareState);
  } catch (error) {
    failDemoWorkspaceOpen(
      kind,
      errorMessage(error),
      snapshot,
      false);
    return;
  }
  if (!link) {
    observeAsync(
      runCallGraphDemo(kind, snapshot),
      "Loading the call graph demo");
    return;
  }
  let destination: string;
  let loc: ParsedLocation;
  try {
    destination = new URL(link, location.href).toString();
    loc = parseWorkspaceHref(destination);
  } catch (error) {
    failDemoWorkspaceOpen(
      kind,
      errorMessage(error),
      snapshot,
      false);
    return;
  }
  const navigationSeq = beginDemoNavigation(destination);
  observeAsync(
    restoreHomeDemoWorkspace(
      kind,
      loc,
      navigationSeq,
      snapshot),
    "Loading the demo workspace");
}

async function restoreHomeDemoWorkspace(
  demoId: ProductHomeDemoId,
  loc: ParsedLocation,
  navigationSeq: number,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
): Promise<void> {
  try {
    await restoreWorkspaceFromLocation(
      loc,
      loc,
      navigationSeq,
      snapshot,
      true,
      message => failDemoWorkspaceOpen(
        demoId,
        message,
        snapshot,
        true));
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) {
      failDemoWorkspaceOpen(
        demoId,
        errorMessage(error),
        snapshot,
        true);
    }
  } finally {
    cancelDemoNavigation(navigationSeq);
  }
}

function failDemoWorkspaceOpen(
  demoId: ProductHomeDemoId,
  message: string,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  retryable: boolean,
): void {
  failWorkspaceCatalogAction(
    `Demo failed: ${message}`,
    snapshot,
    retryable ? () => runHomeDemo(demoId) : null,
    () => restoreWorkspaceFocus(document, { kind: "demo", id: demoId }),
  );
}

function failWorkspaceCatalogAction(
  message: string,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  retry: RetryAction,
  restoreFocus: () => boolean,
): void {
  restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
  state.credits = false;
  state.loading = false;
  state.home = false;
  state.error = "";
  state.errorTitle = "";
  state.errorDetail = "";
  state.retryAction = null;
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  appendQueryNotice(message, retry);
  render();
  afterCurrentNavigationFrame(() => {
    if (!restoreFocus()) {
      focusWorkspace(document);
    }
  });
}

// Return to the intro/home page without tearing down the warm engine or the loaded packages.
// Soft in-app navigation (pushState "/") so a refresh stays on home and Back returns to the
// workbench; the home search reuses the still-resident package list.
function goHome() {
  navigationSequence.begin();
  state.loading = false;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  invalidateGraphMemberNavigation();
  clearNavigationError();
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  state.packageQueryOpen = false;
  packageQueryController.cancel();
  state.credits = false;
  state.home = true;
  spotlight.reset();
  workspaceLocation.push("/");
  render();
}

function openCredits() {
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  navigationSequence.begin();
  state.loading = false;
  state.packageQueryOpen = false;
  packageQueryController.cancel();
  state.credits = true;
  state.home = true;
  spotlight.reset();
  workspaceLocation.push("/credits");
  render();
}

function renderCreditsView() {
  document.title = "Credits · dotnet-inspect";
  app.innerHTML = renderCreditsPage(state.theme === "light" ? "light" : "dark");
  bindCreditsPanel(document, {
    onClose: goHome,
    onToggleTheme: toggleCreditsTheme,
  });
}

function focusPackageQueryInput() {
  afterCurrentNavigationFrame(() =>
    document.querySelector<HTMLInputElement>("#package-query-prefix")?.focus());
}

function afterCurrentNavigationFrame(action: () => void) {
  const navigationSeq = navigationSequence.current();
  requestAnimationFrame(() => {
    if (navigationSequence.isCurrent(navigationSeq)) action();
  });
}

function focusLevelOneHeading(): boolean {
  const heading = document.querySelector<HTMLElement>("main h1");
  if (!heading) return false;
  heading.tabIndex = -1;
  heading.focus();
  return true;
}

function focusWorkbenchSearchOrHeading(): boolean {
  return focusWorkbenchSearch(document) || focusLevelOneHeading();
}

function focusInspectionResult(navigationSeq: number): void {
  afterCurrentNavigationFrame(() => {
    if (navigationSequence.isCurrent(navigationSeq)) {
      focusLevelOneHeading();
    }
  });
}

function restorePackageQueryReturnFocus() {
  if (!state.packageQueryReturnFocusPending) return;
  if (state.packageQueryReturnFocus === "application-query") {
    afterCurrentNavigationFrame(() => {
      const queryScope = document.querySelector<HTMLElement>(
        '[data-application-scope="query"]');
      if (focusRenderedElement(queryScope)) {
        state.packageQueryReturnFocus = null;
        state.packageQueryReturnFocusPending = false;
      } else if (focusLevelOneHeading()) {
        state.packageQueryReturnFocus = null;
        state.packageQueryReturnFocusPending = false;
      }
    });
    return;
  }
  if (state.packageQueryReturnFocus !== "package-search") return;
  afterCurrentNavigationFrame(() => {
    if (focusWorkbenchSearch(document)) {
      state.packageQueryReturnFocus = null;
      state.packageQueryReturnFocusPending = false;
    } else if (focusLevelOneHeading()) {
      state.packageQueryReturnFocus = null;
      state.packageQueryReturnFocusPending = false;
    }
  });
}

function restorePackageQueryWorkspaceFocus() {
  const navigationSeq = packageQueryWorkspaceFocusNavigationSeq;
  if (navigationSeq === null) return;
  packageQueryWorkspaceFocusNavigationSeq = null;
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  afterCurrentNavigationFrame(() => {
    if (!focusLevelOneHeading()) {
      document.querySelector<HTMLElement>("#type-list")?.focus();
    }
  });
}

function resetPackageQueryState() {
  const fresh = initialQueryState();
  state.packageQueryState.request = fresh.request;
  state.packageQueryState.outcome = fresh.outcome;
}

function resetPackageQueryAnnouncements() {
  packageQueryAnnouncements.reset();
  packageQueryLiveAnnouncer.reset();
}

function takePackageQueryAnnouncement(): string {
  return packageQueryAnnouncements.take({
    catalogError: state.packageQueryCatalogError,
    navigationError: state.packageQueryNavigationError,
    failures: state.packageQueryState.outcome.failures,
    terminalFailure:
      state.packageQueryState.outcome.completion.kind === "failed"
        ? state.packageQueryState.outcome.completion.reason
        : "",
  });
}

function ensureCurrentHistoryEntryId(): string | null {
  const current = historyEntryId(history.state);
  if (current) return current;
  const entryId = crypto.randomUUID();
  return workspaceLocation.replace(
    location.href,
    withHistoryEntryId(history.state, entryId))
    ? entryId
    : null;
}

function applyPackageQueryHistory(historyState: unknown) {
  const queryHistory = readPackageQueryHistory(historyState);
  state.packageQueryOpenedFromApp = queryHistory !== null;
  state.packageQueryPredecessorEntryId =
    queryHistory?.predecessorEntryId ?? null;
  state.packageQueryReturnFocus = queryHistory?.returnFocus ?? null;
  state.packageQueryReturnFocusPending = false;
}

function openPackageQueryRoute(
  seed = "",
  options: {
    preserveState?: boolean;
    returnFocus?: PackageQueryReturnFocus;
  } = {},
) {
  if (!state.engineReady || state.loading || state.error) return;
  dismissModalsForRoutedNavigation();
  navigationSequence.begin();
  packageQueryController.cancel();
  packageQueryHandoffNavigationSeq = null;
  if (!options.preserveState) {
    resetPackageQueryState();
  }
  resetPackageQueryAnnouncements();
  if (!options.preserveState || seed) {
    state.packageQueryPrefix = validPackageQueryPrefix(seed);
  }
  state.packageQueryNavigationError = "";
  const returnFocus: PackageQueryReturnFocus = options.returnFocus
    ?? (state.home ? "home-search" : "package-search");
  const predecessorEntryId = ensureCurrentHistoryEntryId();
  if (predecessorEntryId) {
    state.packageQueryOpenedFromApp = true;
    state.packageQueryPredecessorEntryId = predecessorEntryId;
    state.packageQueryReturnFocus = returnFocus;
    state.packageQueryReturnFocusPending = false;
  } else {
    applyPackageQueryHistory(null);
  }
  state.packageQueryOpen = true;
  state.credits = false;
  state.home = false;
  workspaceLocation.push(
    "/query",
    predecessorEntryId
      ? packageQueryHistoryState(
          null,
          crypto.randomUUID(),
          { predecessorEntryId, returnFocus })
      : null);
  render();
  focusPackageQueryInput();
}

function selectWorkspaceApplicationScope() {
  const pkg = state.package;
  if (!pkg) return;
  navigationSequence.begin();
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  const successor = resolvePackageQueryWorkspaceSuccessor(
    () => buildStateUrl(),
    () => {
      const fallback = buildPackageRootStateUrl(location.href, {
        package: pkg.id,
        version: pkg.version,
        framework: pkg.activeFramework,
        lens: state.packageLens,
      });
      fallback.hash = "workspace";
      return fallback;
    });
  if (!successor.projected) {
    appendQueryNotice(
      `Workspace opened, but its complete state could not be saved in the address bar: ${errorMessage(successor.projectionError)
        || "workspace URL encoding failed."}`);
  }
  workspaceLocation.push(successor.url.toString());
  render();
}

function closePackageQueryRoute() {
  navigationSequence.begin();
  if (state.packageQueryOpenedFromApp) {
    state.packageQueryReturnFocusPending =
      state.packageQueryReturnFocus !== null;
    history.back();
    return;
  }
  state.packageQueryOpen = false;
  packageQueryController.cancel();
  state.packageQueryOpenedFromApp = false;
  state.packageQueryPredecessorEntryId = null;
  state.packageQueryReturnFocus = null;
  state.packageQueryReturnFocusPending = false;
  state.credits = false;
  state.home = true;
  spotlight.reset();
  workspaceLocation.replace("/");
  render();
}

function runPackageQuery(prefix: string) {
  const validPrefix = validPackageQueryPrefix(prefix);
  state.packageQueryPrefix = prefix;
  if (!validPrefix) {
    state.packageQueryNavigationError =
      "Enter a non-empty package ID prefix of at most 100 characters.";
    render();
    focusPackageQueryInput();
    return;
  }

  state.packageQueryPrefix = validPrefix;
  state.packageQueryNavigationError = "";
  const request = state.packageQueryState.request
    ? withScopeQuery(state.packageQueryState.request, validPrefix)
    : createQueryRequest(validPrefix);
  packageQueryLiveAnnouncer.reset();
  void packageQueryController.run(request);
}

function togglePackageQueryFacet(facetKey: string, prefix: string) {
  const facet = state.packageQueryFacets.find(
    candidate => candidate.key === facetKey);
  const validPrefix = validPackageQueryPrefix(prefix);
  state.packageQueryPrefix = prefix;
  if (!facet || !validPrefix) {
    state.packageQueryNavigationError = facet
      ? "Enter a package ID prefix before selecting facets."
      : "The selected package-query facet is unavailable.";
    render();
    focusPackageQueryInput();
    return;
  }

  state.packageQueryPrefix = validPrefix;
  state.packageQueryNavigationError = "";
  const current = state.packageQueryState.request
    ? withScopeQuery(state.packageQueryState.request, validPrefix)
    : createQueryRequest(validPrefix);
  packageQueryLiveAnnouncer.reset();
  void packageQueryController.run(toggleFacet(current, facet));
}

async function openPackageQueryRow(
  packageId: string,
  version: string,
) {
  packageQueryController.cancel();
  state.packageQueryOpen = false;
  const navigationSeq = navigationSequence.begin();
  packageQueryHandoffNavigationSeq = navigationSeq;
  state.packageQueryNavigationError = "";
  packageQueryAnnouncements.beginNavigationAttempt();
  const loaded = await loadPackage(
    packageId,
    version,
    "",
    { navigationSeq });
  if (!navigationSequence.isCurrent(navigationSeq)) {
    if (packageQueryHandoffNavigationSeq === navigationSeq)
      packageQueryHandoffNavigationSeq = null;
    return;
  }
  if (!loaded) {
    packageQueryHandoffNavigationSeq = null;
    const failure = state.error || state.queryNotice
      || `Couldn’t open ${packageId}@${version} in the workspace.`;
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.packageQueryOpen = true;
    state.packageQueryNavigationError = failure;
    render();
    afterCurrentNavigationFrame(() =>
      document.querySelector<HTMLElement>(
        `[data-query-row-open="${cssEscape(packageId)}"][data-query-row-version="${cssEscape(version)}"]`)
        ?.focus());
    return;
  }

  packageQueryHandoffNavigationSeq = null;
  workspaceLocation.push(buildStateUrl().toString());
  render();
  focusTypeList();
}

const packageQueryActions: PackageQueryBindingActions = {
  onBack: closePackageQueryRoute,
  onCancel: () => packageQueryController.cancel(),
  onFacetToggle: togglePackageQueryFacet,
  onPrefixInput: prefix => {
    state.packageQueryPrefix = prefix;
    if (state.packageQueryState.request
      && state.packageQueryState.request.scopeQuery !== prefix
      && state.packageQueryState.outcome.completion.kind === "streaming") {
      packageQueryController.cancel();
    }
  },
  onRowOpen: (packageId, version) => {
    observeAsync(
      openPackageQueryRow(packageId, version),
      "Opening a queried package");
  },
  onRun: runPackageQuery,
};

function renderPackageQueryPage() {
  const focus = capturePackageQueryFocus(document);
  const scrollTop = capturePackageQueryScroll(document);
  const announcement = takePackageQueryAnnouncement();
  document.title = "Package query · dotnet-inspect";
  app.innerHTML = renderPackageQueryView({
    state: state.packageQueryState,
    prefix: state.packageQueryPrefix,
    availableFacets: state.packageQueryFacets,
    navigationError: [
      state.packageQueryCatalogError,
      state.packageQueryNavigationError,
    ].filter(Boolean).join(" "),
    escapeHtml,
  });
  bindPackageQueryView(document, packageQueryActions);
  const focusRestoration = restorePackageQueryFocus(document, focus);
  if (focusRestoration !== "fallback") {
    restorePackageQueryScroll(document, scrollTop);
  }
  packageQueryLiveAnnouncer.enqueue(announcement);
}

// The inspector-bot mascot series shown on interstitial (loading) screens. Each entry is a
// color variant of the same dotnet-bot-inspector character living in /assets/bots/. To grow
// the series, drop a new PNG in that folder and add its basename here — nothing else needed.
const BOT_ART = [
  "dotnet-inspect-bot-violet",
  "dotnet-inspect-bot-teal",
  "dotnet-inspect-bot-azure",
  "dotnet-inspect-bot-magenta",
  "dotnet-inspect-bot-crimson",
  "dotnet-inspect-bot-amber"
];

// One random bot is chosen per interstitial *appearance* and held for the life of that
// appearance (the loading message ticks re-render, but the bot must not flicker). It is reset
// to null whenever a non-loading view renders (see render()), so the NEXT loading screen picks
// a fresh random bot.
let loadingBotSrc: string | null = null;
function interstitialBotSrc(): string {
  if (!loadingBotSrc) {
    loadingBotSrc = `/assets/bots/${BOT_ART[Math.floor(Math.random() * BOT_ART.length)]}.png`;
  }
  return loadingBotSrc;
}

function openPackageQuery(query: ParsedPackageQuery) {
  const openPackage = findOpenPackageForQuery(state, query);
  if (openPackage) {
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    selectWorkspacePackage(openPackage);
    return;
  }

  if (!state.engineReady) {
    const url = new URL("/", window.location.href);
    url.searchParams.set("package", query.packageId);
    url.searchParams.set("version", query.version);
    window.location.assign(url);
    return;
  }
  observeAsync(
    loadPackage(query.packageId, query.version, ""),
    "Loading a package");
}

const loadErrorShellActions: LoadErrorShellBindingActions = {
  onOpenPackage: openPackageQuery,
  onRetry: () => {
    if (state.retryAction === retryUnavailable) return;
    observeAction(
      state.retryAction ?? bootstrap,
      "Retrying the inspection");
  },
};

function renderLoading() {
  app.innerHTML = `
    <div class="loading-screen">
      <a class="loading-brand" href="/" aria-label="dotnet inspect home"><span>◇</span> dotnet-inspect</a>
      ${state.error
        ? `<div class="load-error">
             <strong>${escapeHtml(state.errorTitle || "Inspection query failed")}</strong>
             <p class="load-error-message">${escapeHtml(state.error)}</p>
             <form class="load-error-query" id="error-package-query">
               <input id="error-package-input" placeholder="Package or Package@version" aria-label="Open a different NuGet package" autocomplete="off" spellcheck="false" value="${escapeHtml(state.requestedPackage || "")}" />
               <button type="submit">open</button>
             </form>
             <div class="load-error-actions">
               ${state.retryAction === retryUnavailable
                 ? ""
                 : `<button id="retry-load" type="button">retry</button>`}
               ${state.errorDetail ? `<button id="toggle-error-detail" type="button">details</button>` : ""}
             </div>
             ${state.errorDetail ? `<pre class="load-error-detail" hidden>${escapeHtml(state.errorDetail)}</pre>` : ""}
           </div>`
        : `<div class="load-progress"><img class="loading-bot" src="${interstitialBotSrc()}" width="200" height="200" alt="dotnet-bot inspector mascot" /><span class="loader"></span><strong>${escapeHtml(state.loadingMessage)}</strong><small>${state.loadingSubtitle ? escapeHtml(state.loadingSubtitle) : `${escapeHtml(state.requestedPackage)}@${escapeHtml(state.requestedVersion)} · ${escapeHtml(state.requestedFramework || "best framework")}`}</small></div>`}
    </div>`;
  bindLoadErrorShell(document, loadErrorShellActions);
}

async function loadSelectedMemberDocumentation() {
  const type = selectedType();
  const member = selectedMember(type);
  if (!type || !member) {
    render();
    return;
  }
  const overload =
    selectedConcreteOverload(member.overloads, state.selectedOverloadIndex);
  if (!overload) {
    render();
    return;
  }
  const signature = memberRequestSignature(type, overload);
  const pkg = currentPackage();
  return memberDetailInspection.loadDocumentation({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    overload,
    isRuntimePack: Boolean(state.package?.isRuntimePack),
    isCurrent: () => memberRequestIsCurrent(signature),
  });
}

async function loadSelectedMemberSource() {
  if (currentSourceOperationKind() !== "member") {
    render();
    return;
  }
  const type = selectedType();
  const member = selectedMember(type);
  if (!type || !member) {
    state.memberSourceError = "Select a concrete overload before opening Source.";
    render();
    return;
  }
  const overload =
    selectedConcreteOverload(member.overloads, state.selectedOverloadIndex);
  if (!overload) {
    render();
    return;
  }
  const signature = memberRequestSignature(type, overload, false, true);
  const pkg = currentPackage();
  return sourceInspection.loadMemberSource({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.definitionId ?? type.id,
    member: state.selectedBodyTarget?.memberName ?? overload.name,
    selectorKey:
      state.selectedBodyTarget?.selectorKey ?? overload.graphSelectorKey,
    // Preserve the exact MethodDef for same-image validation before structural
    // correspondence handles differing ref/lib row numbers.
    metadataToken:
      state.selectedBodyTarget?.metadataToken ?? overload.metadataToken ?? 0,
    taste: JSON.stringify(state.taste),
    isCurrent: () => memberRequestIsCurrent(signature, false, true),
  });
}

async function loadSelectedMemberAnnotatedSource() {
  const type = selectedType();
  const member = selectedMember(type);
  if (!type || !member) {
    state.memberAnnotatedError = "Select a concrete overload before opening Annotated source.";
    render();
    return;
  }
  const overload =
    selectedConcreteOverload(member.overloads, state.selectedOverloadIndex);
  if (!overload) {
    render();
    return;
  }
  const signature = memberRequestSignature(type, overload, true, true);
  const pkg = currentPackage();
  return memberDetailInspection.loadAnnotated({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    typeIdentity: type.definitionId ?? type.id,
    type: type.queryId ?? type.id,
    member: state.selectedBodyTarget?.memberName ?? overload.name,
    memberSignature: overload.signature,
    selectorKey:
      state.selectedBodyTarget?.selectorKey ?? overload.graphSelectorKey,
    metadataToken:
      state.selectedBodyTarget?.metadataToken ?? overload.metadataToken ?? 0,
    taste: JSON.stringify(state.taste),
    isCurrent: () => memberRequestIsCurrent(signature, true, true),
  });
}

function memberRequestSignature(
  type: AppTypeSurface,
  overload: AppMemberSurface,
  includeBody = false,
  includeTaste = false) {
  const pkg = state.package;
  const parts = [
    pkg?.id,
    pkg?.version,
    pkg?.activeFramework,
    type?.assembly,
    type?.queryId ?? type?.id,
    type?.definitionId ?? type?.id,
    overload?.stableSelector ?? overload?.canonicalSignature ?? overload?.signature
  ].map(value => value ?? "");
  if (includeBody) {
    parts.push(
      String(state.selectedBodyTarget?.metadataToken ?? ""),
      state.selectedBodyTarget?.selectorKey ?? "");
  }
  return memberRequestKey(parts, includeTaste ? state.taste : []);
}

function memberRequestIsCurrent(
  signature: string,
  includeBody = false,
  includeTaste = false) {
  const type = selectedType();
  if (!type) return false;
  const member = selectedMember(type);
  const overload = member
    ? selectedConcreteOverload(member.overloads, state.selectedOverloadIndex)
    : undefined;
  return overload != null
    && memberRequestSignature(type, overload, includeBody, includeTaste)
      === signature;
}

async function loadSelectedTypeSource() {
  if (currentSourceOperationKind() !== "type") {
    renderPreservingMemberFocus();
    return;
  }
  const type = selectedType();
  if (!type) {
    renderPreservingMemberFocus();
    return;
  }
  const pkg = currentPackage();
  const signature =
    typeSourceSignature(type, pkg, state.taste, memberRequestKey);
  return sourceInspection.loadTypeSource({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.definitionId ?? type.id,
    taste: JSON.stringify(state.taste),
    isVisible: () =>
      currentSourceOperationKind() === "type"
      && !workbenchModalOwnsFocus(),
  });
}

async function loadSelectedTypeMetadata() {
  const type = selectedType();
  if (!type) {
    renderPreservingMemberFocus();
    return;
  }
  const pkg = currentPackage();
  const signature = typeMetadataSignature(type, pkg);
  return metadataInspection.loadTypeMetadata({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.queryId ?? type.id,
    isVisible: () => {
      const currentType = selectedType();
      return !state.home
      && !state.settings
      && !state.keyboardHelp
      && !state.explorer?.open
      && !state.loading
      && !state.error
      && !workbenchOverlayOwnsFocus()
      && state.lens === "metadata"
      && !state.atPackageRoot
      && currentType != null
      && typeMetadataSignature(currentType, pkg) === signature;
    },
  });
}

// Projects the neutral type-relationship node/edge model into a Mermaid flowchart so it
// renders with the same pan/zoom/click affordances as the call graph.
async function renderTypeGraph() {
  const container =
    document.querySelector<HTMLElement>("#type-graph-diagram");
  if (!container || container.querySelector(".graph-viewport")) return;
  const meta = state.typeMetadata;
  const definition = meta ? buildTypeGraphMermaid(meta) : null;
  if (!meta || !definition) return;
  const graphNodeOf = new Map(
    (meta.graphNodes || []).map((node, index) => [`t${index}`, node]));
  try {
    mermaidModule ??= import("mermaid");
    const { default: mermaid } = await mermaidModule;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: state.theme === "light" ? "default" : "dark",
      themeVariables: { fontSize: "17px" },
      flowchart: { htmlLabels: false, curve: "basis" }
    });
    const id = `type-graph-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const resolved = definition.replace(
      /var\((--[\w-]+)\)/g,
      (whole: string, name: string) =>
        rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, resolved);
    if (document.querySelector("#type-graph-diagram") !== container) return;
    container.innerHTML =
      '<div class="graph-viewport"></div>'
      + '<div class="graph-controls">'
      + '<button type="button" data-zoom="in" title="Zoom in" aria-label="Zoom in">+</button>'
      + '<button type="button" data-zoom="out" title="Zoom out" aria-label="Zoom out">\u2212</button>'
      + '<button type="button" class="reset" data-zoom="reset" title="Reset view" aria-label="Reset view">fit</button>'
      + '</div>';
    const viewport =
      container.querySelector<HTMLElement>(".graph-viewport");
    if (!viewport) return;
    viewport.innerHTML = svg;
    bindGraphPanZoom(container, viewport, { keybindings });
    bindTypeGraphNodes(viewport, nodeId => {
      const graphNode = nodeId ? graphNodeOf.get(nodeId) : null;
      if (!graphNode) return null;
      const fullName = graphNode.id;
      const pkg = currentPackage();
      const target = graphNode.role === "self"
        ? selectedType()
        : uniqueTypeByQueryId(pkg.types, fullName);
      return target
        ? { onSelect: () => navigateToType(target) }
        : {
            unavailableLabel:
              `${fullName} — not in the browsable public surface`,
          };
    });
  } catch (error) {
    if (document.querySelector("#type-graph-diagram") === container) {
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(errorMessage(error))}</p></div>`;
    }
  }
}

function navigateToTypeByName(fullName: string) {
  const target = uniqueTypeByQueryId(currentPackage().types, fullName);
  if (!target) return;
  navigateToType(target);
}

function navigateToType(target: AppTypeSurface) {
  // Clicking a non-public related type (e.g. an internal derived implementer)
  // enables its accessibility bucket so it appears in the nav list rather than
  // being filtered out by the public-by-default view.
  revealTypeInFilters(target);
  state.selectedTypeId = target.id;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === target.id);
  render();
}

// A related type (interface / base / derived) is only openable if it is part of
// the loaded surface. Non-public implementers in the loaded assemblies are now
// included (with an accessibility filter), so only types in OTHER assemblies
// remain unbrowsable.
function typeIsNavigable(fullName: string) {
  return !!state.package && uniqueTypeByQueryId(state.package.types, fullName) !== null;
}

// Render a related-type chip: an active button when it resolves to a browsable
// type in the loaded surface, otherwise a static chip that explains why it can't
// be opened (it lives in another assembly).
function relatedTypeChip(name: string) {
  const short = escapeHtml(shortTypeName(name));
  if (typeIsNavigable(name)) {
    return `<button class="type-chip" data-graph-type="${escapeHtml(name)}" title="${escapeHtml(name)}">${short}</button>`;
  }
  return `<span class="type-chip is-static" title="${escapeHtml(name)} — not in the loaded surface (in another assembly)">${short}</span>`;
}

// Projects the current package and its transitive dependency neighbourhood into a
// call-graph-style Mermaid flowchart. Walks up to three levels of callees (from cached
// dependency manifests) and three levels of callers (open packages that transitively
// depend on the centre). Because only opened packages have cached manifests, the graph
// grows as the user clicks around and opens more of the neighbourhood.
async function renderDependencyGraph() {
  const container =
    document.querySelector<HTMLElement>("#dependency-graph-diagram");
  if (!container) return;
  const pending = createDependencyGraphPendingState(container.dataset);
  const groups = state.packageDependencies?.dependencyGroups || [];
  if (!groups.length) {
    depGraphRenderSequence.invalidate();
    pending.invalidate();
    return;
  }
  const pkg = state.package;
  if (!pkg) return;
  const built = buildDependencyGraphMermaid(
    {
      package: pkg,
      packages: state.packages,
      dependenciesGroupIndex: state.dependenciesGroupIndex,
      workspaceDependencies: state.workspaceDependencies,
      ...(state.packageDependencies
        ? { packageDependencies: state.packageDependencies }
        : {}),
    },
    (_packages, packageId, versionRange) =>
      uniqueCompatiblePackage(state.packages, packageId, versionRange));
  if (!built) {
    depGraphRenderSequence.invalidate();
    container.dataset.graphDef = "";
    pending.invalidate();
    container.innerHTML = '<p class="graph-empty">No connected packages for this framework. Open a package that depends on this one to see caller edges.</p>';
    return;
  }
  const signature = dependencyGraphRenderSignature(built);
  // Already showing exactly this graph — nothing to do.
  if (container.dataset.graphDef === signature && container.querySelector(".graph-viewport")) return;
  // A render for this exact definition is already in flight on this container; let it finish.
  // (renderDependencyGraph is invoked repeatedly per render cycle — from both
  // maybeAutoLoadPackageDependencies and ensureWorkspaceDependencies — so without this guard
  // two concurrent mermaid.render calls race and one's catch can clobber the other's graph.)
  if (pending.isPending(signature)) return;
  const seq = depGraphRenderSequence.begin();
  pending.begin(signature, seq);
  try {
    mermaidModule ??= import("mermaid");
    const { default: mermaid } = await mermaidModule;
    if (!depGraphRenderSequence.isCurrent(seq)) return;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: state.theme === "light" ? "default" : "dark",
      themeVariables: { fontSize: "16px" },
      flowchart: { htmlLabels: false, curve: "basis" }
    });
    const id = `dep-graph-${seq.toString(36)}-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const resolved = built.definition.replace(
      /var\((--[\w-]+)\)/g,
      (whole: string, name: string) =>
        rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, resolved);
    // A newer render superseded this one, or the container was swapped out — bail without touching the DOM.
    if (!depGraphRenderSequence.isCurrent(seq)) return;
    if (document.querySelector("#dependency-graph-diagram") !== container) return;
    container.innerHTML =
      '<div class="graph-viewport"></div>'
      + '<div class="graph-controls">'
      + '<button type="button" data-zoom="in" title="Zoom in" aria-label="Zoom in">+</button>'
      + '<button type="button" data-zoom="out" title="Zoom out" aria-label="Zoom out">\u2212</button>'
      + '<button type="button" class="reset" data-zoom="reset" title="Reset view" aria-label="Reset view">fit</button>'
      + '</div>'
      + (built.truncated
        ? `<div class="graph-drill-error graph-diagnostics" role="status">Dependency graph truncated at ${built.nodeLimit} nodes.</div>`
        : "");
    const viewport =
      container.querySelector<HTMLElement>(".graph-viewport");
    if (!viewport) return;
    viewport.innerHTML = svg;
    container.dataset.graphDef = signature;
    bindGraphPanZoom(container, viewport, { keybindings });
    bindDependencyGraphNodes(viewport, nodeId => {
      const info = nodeId ? built.nodeInfoById.get(nodeId) : null;
      if (!info || info.kind === "self") return null;
      return {
        onSelect: () => {
          if (info.kind === "open" && info.packageKey)
            switchToPackageForDependencies(info.packageKey);
          else if (info.id)
            observeAsync(
              openDependencyPackage(info.id, info.versionRange),
              "Opening a dependency package");
        },
      };
    });
  } catch (error) {
    // Only surface the error if this is still the latest render and nothing else has drawn a graph.
    if (depGraphRenderSequence.isCurrent(seq)
      && document.querySelector("#dependency-graph-diagram") === container
      && !container.querySelector(".graph-viewport")) {
      container.dataset.graphDef = "";
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(errorMessage(error))}</p></div>`;
    }
  } finally {
    pending.complete(signature, seq);
  }
}

function switchToPackageForDependencies(packageKey: string) {
  navigationSequence.invalidate();
  const target = state.packages.find(item =>
    packageIdentityKey(item) === packageKey);
  if (!target) return;
  state.loading = false;
  activatePackage(target, { resetAccessibility: true });
  state.atPackageRoot = true;
  state.packageLens = "dependencies";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.libraryScope = null;
  state.selectedTypeId = defaultVisibleTypeId(target);
  reconcileAccessibilityFilter(target.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  render();
}

async function openDependencyPackage(
  packageId: string,
  versionRange: string | null | undefined,
) {
  const existing = uniqueCompatiblePackage(
    state.packages,
    packageId,
    versionRange ?? null);
  if (existing) {
    switchToPackageForDependencies(packageIdentityKey(existing));
    return;
  }
  const navigationSeq = navigationSequence.begin();
  state.loading = true;
  state.error = "";
  state.retryAction = null;
  state.loadingMessage = `Resolving ${packageId}…`;
  state.loadingSubtitle = versionRange || "latest stable";
  render();
  try {
    const version =
      await resolveDependencyVersion(packageId, versionRange ?? null);
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    const model = await loadPackage(
      packageId,
      version,
      "",
      { navigationSeq });
    if (!model || !navigationSequence.isCurrent(navigationSeq)) return;
    state.atPackageRoot = true;
    state.packageLens = "dependencies";
    render();
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    state.loading = false;
    appendQueryNotice(
      friendlyLoadError(error, packageId, versionRange).message);
    render();
  }
}

function nextPaint() {
  // Resolve after the browser has had a chance to lay out and paint the current DOM.
  return new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(resolve, 0))));
}

async function loadSelectedMemberCallGraph() {
  const type = selectedType();
  const member = selectedMember(type);
  if (!type || !member) {
    state.memberCallGraphError = "Select a concrete overload before opening Call graph.";
    render();
    return;
  }
  const overload =
    selectedConcreteOverload(member.overloads, state.selectedOverloadIndex);
  if (!overload) {
    render();
    return;
  }
  const signature = memberRequestSignature(type, overload, true);
  const pkg = currentPackage();
  const platformAssembly = assemblyDescriptorForType(pkg.assemblies, type);
  let workspacePackages: AppPackage[];
  try {
    workspacePackages = selectedCallGraphWorkspacePackages();
  } catch (error) {
    state.memberCallGraphError = errorMessage(error);
    render();
    return;
  }
  const hasOtherLibraries = workspacePackages.length > 1;
  return callGraphInspection.load({
    signature,
    isRuntimePack: Boolean(state.package?.isRuntimePack),
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.queryId ?? type.id,
    typeIdentity: type.definitionId ?? type.id,
    platformType:
      type.definitionId ?? type.metadataId ?? type.queryId ?? type.id,
    platformPack:
      platformPackForAssembly(type.assembly, type.platformPack) ?? "",
    platformAssemblyVersion: platformAssembly?.version ?? null,
    platformAssemblyCulture: platformAssembly?.culture ?? null,
    platformAssemblyPublicKeyToken:
      platformAssembly?.publicKeyToken ?? null,
    member: state.selectedBodyTarget?.memberName ?? overload.name,
    memberSignature: overload.signature,
    selectorKey:
      state.selectedBodyTarget?.selectorKey ?? overload.graphSelectorKey,
    metadataToken:
      state.selectedBodyTarget?.metadataToken ?? overload.metadataToken ?? 0,
    workspacePackages: workspacePackages.map(packageItem => ({
      package: packageItem.id,
      version: packageItem.version,
      framework: packageItem.activeFramework,
    })),
    hasOtherLibraries,
    isCurrent: () => memberRequestIsCurrent(signature, true),
  });
}

// Update just the call-graph section in place so the stage-2 result doesn't flash
// the whole page. Leaves the stage-1 diagram untouched unless the graph changed.
function patchCallGraphSection(previousMermaid: string | undefined) {
  const section = document.querySelector(".call-graph-section");
  if (!section) return; // not on the call-graph view; state is cached for re-entry.
  const graph = state.memberCallGraph;
  const callers = graph?.callers?.children ?? [];
  const callees = graph?.callees?.children ?? [];
  const graphScope = graph?.scope;
  const countSpan = section.querySelector(".section-title span");
  if (countSpan) {
    countSpan.textContent =
      `${callers.length} caller${callers.length === 1 ? "" : "s"} · ${callees.length} callee${callees.length === 1 ? "" : "s"}`;
  }
  section.querySelector(".graph-expanding")?.remove();
  section.querySelector(".graph-diagnostics")?.remove();
  const scopeEl = section.querySelector(".graph-scope");
  const diagnosticsMessage = callGraphDiagnosticsMessage(graph?.diagnostics);
  if (diagnosticsMessage) {
    const warning = document.createElement("div");
    warning.className = "graph-drill-error graph-diagnostics";
    warning.textContent = diagnosticsMessage;
    scopeEl?.before(warning);
  }
  if (scopeEl && graphScope) {
    scopeEl.innerHTML =
      `<strong>Workspace callers</strong><span>${graphScope.packages} loaded packages · ${graphScope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(graphScope.calleeScope)} · depth 2</span>`;
  }
  const sourceCode = section.querySelector(".graph-mermaid pre code");
  if (sourceCode) sourceCode.textContent = graph?.mermaid ?? "";
  if (graph?.mermaid && graph.mermaid !== previousMermaid)
    observeAsync(renderMermaidCallGraph(), "Rendering the member call graph");
}

function renderMermaidCallGraph(): Promise<CallGraphRenderResult> {
  const container =
    document.querySelector<HTMLElement>("#call-graph-diagram");
  const active = currentCallGraph();
  if (!active) {
    return Promise.resolve({
      status: "failed",
      message: "The call graph was not available.",
    });
  }
  if (active.noBody) return Promise.resolve({ status: "rendered" });
  if (!active.mermaid) {
    return Promise.resolve({
      status: "failed",
      message: "The call graph did not include a diagram.",
    });
  }
  if (state.memberCallGraphLoading) {
    return Promise.resolve({ status: "superseded" });
  }
  if (!container) {
    return Promise.resolve(
      scope() === "member" && state.memberSection === "call-graph"
        ? {
            status: "failed",
            message: "The call graph diagram could not be mounted.",
          }
        : { status: "superseded" });
  }
  if (container.dataset.graphDef === active.mermaid
    && container.querySelector(".graph-viewport")) {
    return Promise.resolve({ status: "rendered" });
  }
  const theme: "light" | "dark" =
    state.theme === "light" ? "light" : "dark";
  const pending = callGraphRenderOperation;
  if (pending
    && pending.definition === active.mermaid
    && pending.theme === theme) {
    return pending.promise;
  }

  const seq = ++callGraphRenderSeq;
  const definition = active.mermaid;
  const promise = (async (): Promise<CallGraphRenderResult> => {
    try {
      mermaidModule ??= import("mermaid");
      const { default: mermaid } = await mermaidModule;
      if (seq !== callGraphRenderSeq) {
        return { status: "superseded" };
      }
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: "strict",
        theme: theme === "light" ? "default" : "dark",
        themeVariables: { fontSize: "17px" },
        flowchart: { htmlLabels: false, curve: "basis" }
      });
      const id = `call-graph-${Date.now().toString(36)}-${seq}`;
      const rootStyle = getComputedStyle(document.documentElement);
      const renderDefinition = definition.replace(
        /var\((--[\w-]+)\)/g,
        (whole: string, name: string) =>
          rootStyle.getPropertyValue(name).trim() || whole
      );
      const { svg } = await mermaid.render(id, renderDefinition);
      if (seq !== callGraphRenderSeq) {
        return { status: "superseded" };
      }
      const targetContainer =
        document.querySelector<HTMLElement>("#call-graph-diagram");
      const mounted = currentCallGraph();
      if (!targetContainer
        || mounted?.mermaid !== definition) {
        return { status: "superseded" };
      }
      targetContainer.innerHTML =
        '<div class="graph-viewport"></div>'
        + '<div class="graph-controls">'
        + '<button type="button" data-zoom="in" title="Zoom in" aria-label="Zoom in">+</button>'
        + '<button type="button" data-zoom="out" title="Zoom out" aria-label="Zoom out">\u2212</button>'
        + '<button type="button" class="reset" data-zoom="reset" title="Reset view" aria-label="Reset view">fit</button>'
        + '</div>';
      const viewport =
        targetContainer.querySelector<HTMLElement>(".graph-viewport");
      if (!viewport) {
        return {
          status: "failed",
          message: "The call graph diagram could not be mounted.",
        };
      }
      viewport.innerHTML = svg;
      targetContainer.dataset.graphDef = definition;
      bindGraphPanZoom(targetContainer, viewport, {
        keybindings,
        resolveCallGraphNode: nodeId =>
          callGraphNodeBinding(mounted, nodeId),
      });
      return { status: "rendered" };
    } catch (error) {
      const message = errorMessage(error);
      const targetContainer =
        document.querySelector<HTMLElement>("#call-graph-diagram");
      if (seq === callGraphRenderSeq
        && targetContainer
        && currentCallGraph()?.mermaid === definition
        && !targetContainer.querySelector(".graph-viewport")) {
        targetContainer.dataset.graphDef = "";
        targetContainer.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(message)}</p></div>`;
      }
      return { status: "failed", message };
    }
  })();
  callGraphRenderOperation = { definition, theme, promise };
  void promise.then(() => {
    if (callGraphRenderOperation?.promise === promise) {
      callGraphRenderOperation = null;
    }
    return null;
  });
  return promise;
}

function callGraphNodeBinding(
  callGraph: InspectedCallGraph,
  nodeId: string,
): CallGraphNodeBinding | null {
  const target =
    callGraph.targets?.find(candidate => candidate.id === nodeId) ?? null;
  if (!target) return null;
  return callGraphTargetBinding(target);
}

type CallGraphTargetDestination = "default" | "member" | "source";
type GraphNavigationFailureSurface = "call-graph" | "annotated";

function callGraphTargetBinding(
  target: InspectedCallGraphTarget,
  destination: CallGraphTargetDestination = "default",
  failureSurface: GraphNavigationFailureSurface = "call-graph",
): CallGraphNodeBinding | null {
  const typeId = callGraphTargetTypeId(target);

  // Inside a platform descent the whole graph lives in the runtime pack, not
  // the workspace, so clicked callees descend through the platform graph.
  const drilled =
    state.platformStack.length > 0 || Boolean(state.package?.isRuntimePack);
  if (drilled) {
    if (target.id === "n0" || !target.assembly || !typeId) return null;
    const pack = runtimePackForFramework(
      runtimePackPackage(),
      state.package?.activeFramework || "");
    const candidate = pack
      ? resolveRuntimeGraphTargetCandidate(pack, target)
      : { status: "missing" } as const;
    const resident = candidate.status === "unique" && pack
      ? findRuntimeMemberSelection(pack, target, candidate)
      : null;
    const assemblyResident =
      runtimeGraphTargetAssemblyIsResident(pack, target);
    const disposition = runtimeGraphTargetNavigationDisposition(
      candidate,
      target,
      Boolean(resident),
      assemblyResident);
    if (disposition === "blocked") {
      return blockedCallGraphNodeBinding(
        target,
        graphTargetBlockedReason(candidate, "runtime"),
        failureSurface);
    }
    if (disposition === "none") return null;
    if (destination === "source") {
      return blockedCallGraphNodeBinding(
        target,
        "Source navigation is unavailable for platform targets",
        failureSurface);
    }
    const runtimeSection = destination === "member"
      ? "overview"
      : "call-graph";
    return {
      label: `Open ${target.typeFullName}.${target.memberName}`,
      platform: disposition === "lookup",
      onSelect: () => {
        if (disposition === "member" && pack && resident) {
          navigateToRuntimeMember(
            pack,
            resident.type,
            resident.group,
            resident.overloadIndex,
            target,
            runtimeSection);
        } else if (disposition === "lookup") {
          observeAsync(
            navigateOrDrillPlatform(
              target,
              runtimeSection,
              failureSurface),
            "Opening a platform call-graph target");
        } else if (destination === "member") {
          observeAsync(
            navigateOrDrillPlatform(
              target,
              runtimeSection,
              failureSurface),
            "Opening a resident platform member");
        } else {
          observeAsync(
            startPlatformDrill(target),
            "Opening a resident platform call-graph target");
        }
      },
    };
  }

  const packages = [
    state.package,
    ...state.packages.filter(item => item !== state.package),
  ].filter((pkg): pkg is AppPackage => pkg != null);
  const candidate =
    resolveLoadedGraphTargetCandidate<AppPackage, AppTypeSurface>(
      packages,
      target);
  if (candidate.status === "resident" && destination !== "default") {
    const residentPackage = loadedGraphTargetPackage(packages, target);
    if (!residentPackage) {
      return blockedCallGraphNodeBinding(
        target,
        "the exact target assembly is not unique in the loaded package workspace",
        failureSurface);
    }
    const section = destination === "source" ? "source" : "overview";
    return {
      label: `Open ${target.typeFullName}.${target.memberName}`,
      platform: false,
      onSelect: () => {
        observeAsync(
          navigateToUnprojectedGraphMember(
            residentPackage,
            target,
            section,
            failureSurface),
          "Opening a package graph member");
      },
    };
  }
  const pack = runtimePackForFramework(
    runtimePackPackage(),
    state.package?.activeFramework || "");
  const runtimeCandidate = (candidate.status === "missing"
      || candidate.status === "skew") && pack
    ? resolveRuntimeGraphTargetCandidate(pack, target)
    : null;
  const resident = runtimeCandidate?.status === "unique" && pack
    ? findRuntimeMemberSelection(pack, target, runtimeCandidate)
    : null;
  const runtimeResident = runtimeCandidate != null
    && (runtimeCandidate.status === "unique"
      || runtimeGraphTargetAssemblyIsResident(pack, target));
  const disposition = combinedGraphTargetNavigationDisposition(
    candidate,
    runtimeCandidate,
    target,
    runtimeResident);
  if (disposition === "blocked") {
    const reason = runtimeCandidate?.status === "ambiguous"
        || runtimeCandidate?.status === "skew"
      ? graphTargetBlockedReason(runtimeCandidate, "runtime")
      : graphTargetBlockedReason(candidate, "package");
    return blockedCallGraphNodeBinding(
      target,
      reason,
      failureSurface);
  }
  if (disposition === "none") return null;
  const loaded = disposition === "loaded" && candidate.status === "unique"
    ? resolveLoadedGraphTarget(target, candidate)
    : null;
  if (destination === "source" && !loaded) {
    return blockedCallGraphNodeBinding(
      target,
      "Source navigation requires a target in a loaded package workspace",
      failureSurface);
  }
  if (destination === "source"
    && loaded
    && "group" in loaded
    && !memberSectionIdsFor(
      loaded.group,
      loaded.pkg.isRuntimePack,
      true).includes("source")) {
    return blockedCallGraphNodeBinding(
      target,
      "Source navigation is unavailable for this member",
      failureSurface);
  }
  const platform = disposition === "platform";
  const loadedSection = destination === "source" ? "source" : "overview";
  const runtimeSection = destination === "member" ? "overview" : "call-graph";
  return {
    label: `Open ${target.typeFullName}.${target.memberName}`,
    platform,
    onSelect: () => {
      if (loaded) {
        observeAsync(
          navigateToGraphMember(
            loaded,
            target,
            loadedSection,
            failureSurface),
          "Opening a graph member");
      } else if (disposition === "resident") {
        if (pack && resident) {
          navigateToRuntimeMember(
            pack,
            resident.type,
            resident.group,
            resident.overloadIndex,
            target,
            runtimeSection);
        } else {
          observeAsync(
            destination === "member"
              ? navigateOrDrillPlatform(
                target,
                runtimeSection,
                failureSurface)
              : startPlatformDrill(target),
            "Opening a resident platform call-graph target");
        }
      } else if (platform) {
        observeAsync(
          navigateOrDrillPlatform(
            target,
            runtimeSection,
            failureSurface),
          "Opening a platform call-graph target");
      }
    },
  };
}

function blockedCallGraphNodeBinding(
  target: InspectedCallGraphTarget,
  reason: string,
  failureSurface: GraphNavigationFailureSurface = "call-graph",
): CallGraphNodeBinding {
  return {
    label: `Cannot open ${target.typeFullName}.${target.memberName}: ${reason}`,
    blocked: true,
    onSelect: () => {
      if (failureSurface === "annotated") {
        state.annotatedDestinationError =
          `Could not open ${target.typeFullName}.${target.memberName}: ${reason}.`;
        renderAndFocusAnnotated({ kind: "explore" }, "embedded");
        return;
      }
      invalidateGraphMemberNavigation();
      state.memberCallGraphSeq++;
      state.memberCallGraphExpanding = false;
      state.platformDrillLoading = false;
      state.memberSection = "call-graph";
      state.graphMemberNavigationError =
        `Could not open ${target.typeFullName}.${target.memberName}: ${reason}.`;
      render();
    },
  };
}

function loadedGraphTargetPackage(
  packages: readonly AppPackage[],
  target: InspectedCallGraphTarget,
): AppPackage | null {
  const matches = packages.filter(pkg =>
    pkg.assemblies.some(assembly =>
      callGraphAssemblyIdentityMatches(target, assembly)));
  return matches.length === 1 ? matches[0] ?? null : null;
}

function currentCallGraph() {
  // Round 6 review (Claude Opus 5) caught this as the one `?? default` on the branch that
  // is not preceded by a presence test. `?.graph ?? …` cannot tell "no breadcrumb" from
  // "a breadcrumb whose graph is nullish", so the second case silently displayed the
  // workspace member call graph while the breadcrumb trail still claimed the platform
  // frame. Branching on the entry keeps the fallback for the empty stack only.
  const top = state.platformStack.at(-1);
  return top ? top.graph : state.memberCallGraph;
}

function callGraphExplorerKey(): string | null {
  if (state.home || state.loading || state.error || state.packageQueryOpen
    || state.credits || state.settings || state.keyboardHelp || state.explorer?.open
    || state.spotlightOpen || state.docViewerOpen || state.graphSourceOpen
    || state.memberAnnotatedModal || scope() !== "member"
    || state.memberSection !== "call-graph") return null;
  const type = selectedType();
  const member = selectedMember(type);
  const overload = member
    ? selectedConcreteOverload(member.overloads, state.selectedOverloadIndex)
    : null;
  return type && overload && state.package
    ? JSON.stringify([
        packageIdentityKey(state.package),
        memberRequestSignature(type, overload, true),
      ])
    : null;
}

function callGraphExplorerTarget() {
  const key = callGraphExplorerKey();
  const content = document.querySelector<HTMLElement>("[data-call-graph-surface]");
  const invoker = document.querySelector<HTMLElement>("#call-graph-explore");
  return key && content && invoker
    ? {
        key,
        title: "Call graph",
        context: currentInspectedSubjectPath().map(segment => segment.label).join(" > "),
        content,
        invoker,
      }
    : null;
}

function openCallGraphExplorer() {
  const target = callGraphExplorerTarget();
  if (!target) return;
  graphExplorerOriginKey = target.key;
  graphExplorer.open(target);
}

function restoreGraphExplorerNavigationFocus() {
  if (!graphExplorerNavigationFocusPending) return;
  if (state.settings || state.keyboardHelp || state.explorer?.open
    || workbenchModalOwnsFocus()) {
    graphExplorerNavigationFocusPending = false;
    return;
  }
  if (state.loading || state.graphMemberNavigationTitle || state.platformDrillLoading) return;
  graphExplorerNavigationFocusPending = false;
  const explore = document.querySelector<HTMLButtonElement>("#call-graph-explore");
  if (callGraphExplorerKey() === graphExplorerOriginKey && explore && !explore.disabled) {
    explore.focus({ preventScroll: true });
  } else {
    focusLevelOneHeading();
  }
  app.removeAttribute("tabindex");
}

function closeGraphExplorerForNavigation() {
  if (!graphExplorer.close(false)) return;
  graphExplorerNavigationFocusPending = true;
  app.tabIndex = -1;
  app.focus({ preventScroll: true });
}

function platformCrumbTrail() {
  const root = state.memberCallGraph?.callees?.label
    ? state.memberCallGraph.callees.label.replace(/\(.*$/, "")
    : "member";
  return [root, ...state.platformStack.map(entry => entry.title)].join(" › ");
}

function resolveLoadedGraphTarget(
  target: InspectedCallGraphTarget | GraphMemberShareIdentity,
  candidate: {
    status: "unique";
    pkg: AppPackage;
    type: AppTypeSurface;
  },
) {
  const { pkg, type } = candidate;
  const selection = findGraphMemberSelection(type, target);
  if (selection) return { pkg, type, ...selection };
  return {
    pkg,
    type,
    title: `${stripArity(type.name)}.${target.memberName}`,
    request: {
      packageId: pkg.id,
      version: pkg.version,
      framework: pkg.activeFramework,
      assembly: type.assembly,
      type: callGraphTargetTypeId(target),
      member: target.memberName,
      selectorKey: target.selectorKey,
      metadataToken: target.metadataToken ?? 0,
    }
  };
}

function findGraphMemberSelection(
  type: AppTypeSurface,
  target: GraphMemberTarget,
) {
  const groups = memberGroups(type);
  const selection = graphMemberSelection(groups, target);
  if (!selection) return null;
  const group = groups[selection.groupIndex];
  return group
    ? { group, overloadIndex: selection.overloadIndex }
    : null;
}

async function loadGraphMemberSurface(
  pkg: AppPackage,
  target: InspectedCallGraphTarget | GraphMemberShareIdentity,
  type: AppTypeSurface | null = null,
) {
  return inspectGraphMemberSurface(
    pkg.id,
    pkg.version,
    pkg.activeFramework,
    graphMemberSurfaceAssembly(target, type),
    target.typeDefinitionId ?? "",
    target.memberName,
    target.selectorKey,
    target.metadataToken ?? 0);
}

function singleProjectedGraphMember(
  type: InspectedTypeSurface,
): InspectedMemberSurface {
  const member = type.api[0];
  if (!member || type.api.length !== 1) {
    throw new Error(
      "The projected graph type did not retain exactly one selected member.");
  }
  return member;
}

function stageGraphMemberSelection(
  pkg: AppPackage,
  type: AppTypeSurface,
  target: InspectedCallGraphTarget | GraphMemberShareIdentity,
  surface: InspectedMemberSurface,
) {
  let member: AppMemberSurface | undefined = (type.api ?? []).find(candidate =>
    candidate.stableSelector === surface.stableSelector
    && candidate.canonicalSignature === surface.canonicalSignature);
  const isNew = !member;
  if (!member) {
    member = {
      ...createAppMemberSurface(surface),
      graphOnly: true,
      graphTarget: target,
    };
  }
  const stagedType = isNew
    ? { ...type, api: [...(type.api ?? []), member] }
    : type;
  const selection = resolveLoadedGraphTarget(
    target,
    { status: "unique", pkg, type: stagedType });
  if (!("group" in selection)) {
    throw new Error(
      `The graph target '${target.memberName}' did not resolve to the projected member.`);
  }
  return {
    isNew,
    member,
    selection: { ...selection, group: selection.group }
  };
}

function commitGraphMemberSelection(
  pkg: AppPackage,
  type: AppTypeSurface,
  target: InspectedCallGraphTarget | GraphMemberShareIdentity,
  staged: ReturnType<typeof stageGraphMemberSelection>,
) {
  retainGraphMemberProjection(pkg.types, staged.member);
  if (staged.isNew) {
    type.api ??= [];
    type.api.push(staged.member);
  }
  const selectedTarget = retainGraphOnlyImplementationBody(
    staged.member,
    target);
  const selection = resolveLoadedGraphTarget(
    target,
    { status: "unique", pkg, type });
  if (!("group" in selection)) {
    throw new Error(
      `The graph target '${target.memberName}' was lost while committing its projection.`);
  }
  return {
    ...selection,
    group: selection.group,
    selectedBodyTarget: selectedTarget,
  };
}

async function navigateToGraphMember(
  loaded: ReturnType<typeof resolveLoadedGraphTarget>,
  target: InspectedCallGraphTarget,
  section: "overview" | "source" = "overview",
  failureSurface: GraphNavigationFailureSurface = "call-graph",
) {
  closeGraphExplorerForNavigation();
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  if ("group" in loaded) {
    const overload = loaded.group.overloads[loaded.overloadIndex];
    const selectedBodyTarget = overload
      ? retainGraphOnlyImplementationBody(overload, target)
      : target;
    navigateToMember(
      loaded.pkg,
      loaded.type,
      loaded.group,
      loaded.overloadIndex,
      selectedBodyTarget,
      section);
    return;
  }

  await navigateToGraphMemberProjection(
    loaded.pkg,
    loaded.type,
    target,
    section,
    failureSurface);
}

async function navigateToUnprojectedGraphMember(
  pkg: AppPackage,
  target: InspectedCallGraphTarget,
  section: "overview" | "source",
  failureSurface: GraphNavigationFailureSurface = "call-graph",
) {
  closeGraphExplorerForNavigation();
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  await navigateToGraphMemberProjection(
    pkg,
    null,
    target,
    section,
    failureSurface);
}

async function navigateToGraphMemberProjection(
  pkg: AppPackage,
  existingType: AppTypeSurface | null,
  target: InspectedCallGraphTarget,
  section: "overview" | "source",
  failureSurface: GraphNavigationFailureSurface = "call-graph",
) {
  const seq = ++state.graphMemberNavigationSeq;
  const owner = captureViewOperation(seq);
  const packageKey = packageIdentityKey(pkg);
  const navigationIsCurrent = () =>
    ownsViewOperation(owner, state.graphMemberNavigationSeq)
    && state.packages.some(candidate =>
      packageIdentityKey(candidate) === packageKey);
  state.graphMemberNavigationTitle =
    `${stripArity(target.typeFullName.split(".").pop() ?? "")}.${target.memberName}`;
  state.graphMemberNavigationError = "";
  render();
  try {
    const projection = await loadGraphMemberSurface(
      pkg,
      target,
      existingType);
    if (!navigationIsCurrent()) {
      if (seq === state.graphMemberNavigationSeq) {
        state.graphMemberNavigationTitle = "";
        render();
      }
      return;
    }
    const selectedTarget = graphMemberTargetWithSelectedBody(
      target,
      projection.selectedBody);
    const projectedMember = singleProjectedGraphMember(projection.type);
    const projectedType = createAppTypeSurface(projection.type);
    if (!callGraphTargetMatchesType(target, projectedType)) {
      throw new Error(
        "The projected graph member did not retain the exact target type and member.");
    }
    const type = existingType ?? {
      ...projectedType,
      api: [],
      graphOnly: true,
    };
    const staged = stageGraphMemberSelection(
      pkg,
      type,
      selectedTarget,
      projectedMember);
    if (section === "source"
      && !memberSectionIdsFor(
        staged.selection.group,
        pkg.isRuntimePack,
        true).includes("source")) {
      showGraphMemberNavigationError(
        target,
        "Source navigation is unavailable for this member.",
        failureSurface);
      return;
    }
    if (!existingType) pkg.types.push(type);
    const selection = commitGraphMemberSelection(
      pkg,
      type,
      selectedTarget,
      staged);
    state.graphMemberNavigationTitle = "";
    navigateToMember(
      selection.pkg,
      selection.type,
      selection.group,
      selection.overloadIndex,
      selection.selectedBodyTarget,
      section);
  } catch (error) {
    if (!navigationIsCurrent()) {
      if (seq === state.graphMemberNavigationSeq) {
        state.graphMemberNavigationTitle = "";
        render();
      }
      return;
    }
    showGraphMemberNavigationError(
      target,
      errorMessage(error),
      failureSurface);
  }
}

function showGraphMemberNavigationError(
  target: InspectedCallGraphTarget,
  reason: string,
  failureSurface: GraphNavigationFailureSurface,
) {
  state.graphMemberNavigationTitle = "";
  const message =
    `Could not open ${target.typeFullName}.${target.memberName}: ${reason}`;
  if (failureSurface === "annotated") {
    state.annotatedDestinationError = message;
    renderAndFocusAnnotated({ kind: "explore" }, "embedded");
    return;
  }
  state.graphMemberNavigationError = message;
  render();
}

async function restorePendingGraphMember() {
  const pending = state.pendingGraphMemberDeepLink;
  const pkg = state.package;
  const type = selectedType();
  if (!pending || !pkg || !type) return;
  const seq = ++state.graphMemberNavigationSeq;
  const owner = captureViewOperation(seq);
  state.graphMemberNavigationTitle =
    `${type.displayName || type.id}.${pending.target.memberName}`;
  render();
  const restorationIsCurrent = () =>
    ownsViewOperation(owner, state.graphMemberNavigationSeq)
    && state.pendingGraphMemberDeepLink === pending
    && graphMemberPendingMatchesView(
      pending,
      packageIdentityKey(state.package),
      viewSignature());
  const discardIfOwned = () => {
    if (state.pendingGraphMemberDeepLink !== pending) return;
    state.pendingGraphMemberDeepLink = null;
    state.graphMemberNavigationTitle = "";
    render();
  };
  try {
    if (type.id !== pending.type) {
      throw new Error(
        "The graph member's declaring type is no longer available.");
    }
    const projection = await loadGraphMemberSurface(
      pkg,
      pending.target,
      type);
    if (!restorationIsCurrent()) {
      discardIfOwned();
      return;
    }
    const selectedTarget = graphMemberTargetWithSelectedBody(
      pending.target,
      projection.selectedBody);
    const projectedMember = singleProjectedGraphMember(projection.type);
    const staged = stageGraphMemberSelection(
      pkg,
      type,
      selectedTarget,
      projectedMember);
    if (staged.selection.group.key !== pending.member) {
      throw new Error("The shared member identity does not match the graph target.");
    }
    const selection = commitGraphMemberSelection(
      pkg,
      type,
      selectedTarget,
      staged);
    state.pendingGraphMemberDeepLink = null;
    state.graphMemberNavigationTitle = "";
    state.selectedMemberKey = selection.group.key;
    state.selectedOverloadIndex = selection.overloadIndex;
    state.memberSection = pending.section
      && isMemberSection(pending.section)
      && memberSectionIdsFor(
        selection.group,
        state.package?.isRuntimePack,
      true).includes(pending.section)
      ? pending.section
      : "overview";
    state.selectedBodyTarget = selection.selectedBodyTarget;
    normalizeCurrentNavEntry();
    render();
    observeAsync(loadSelectionData(), "Loading restored graph member data");
  } catch (error) {
    if (!restorationIsCurrent()) {
      discardIfOwned();
      return;
    }
    state.pendingGraphMemberDeepLink = null;
    state.graphMemberNavigationTitle = "";
    state.selectedMemberKey = "";
    state.selectedOverloadIndex = null;
    state.memberSection = "overview";
    state.selectedBodyTarget = null;
    normalizeCurrentNavEntry();
    appendQueryNotice(
      `The graph member could not be restored: ${errorMessage(error)}`);
    render();
  }
}

async function drillPlatformNode(
  node: InspectedCallGraphTarget,
  navigationIsCurrent: () => boolean = () => true,
) {
  if (!node.assembly || !node.memberName || !node.selectorKey) {
    await showPlatformTargetError(
      node,
      "the target does not carry a complete navigable identity");
    return;
  }
  const framework = currentPackage().activeFramework;
  const runtimePack = runtimePackForFramework(
    runtimePackPackage(),
    framework);
  const runtimeIndex = runtimePack
    ? state.packages.indexOf(runtimePack)
    : -1;
  const captured = capturedShareTabs();
  const platformVersion = retainedPlatformTargetVersion(
    captured.preservesBasis && runtimeIndex >= 0
      ? captured.tabs[runtimeIndex]
      : null,
    runtimePack,
    framework);
  return callGraphInspection.drill({
    framework,
    platformVersion,
    assembly: node.assembly,
    pack: platformPackForGraphAssembly(
      node.assembly,
      node.platformPack,
      runtimePackPackage(),
      currentPackage().activeFramework) ?? "",
    assemblyVersion: node.assemblyVersion,
    assemblyCulture: node.assemblyCulture,
    assemblyPublicKeyToken: node.assemblyPublicKeyToken,
    type: callGraphTargetTypeId(node),
    member: node.memberName,
    selectorKey: node.selectorKey,
    metadataToken: node.metadataToken ?? 0,
    title:
      `${stripArity(node.typeFullName.split(".").pop() ?? "")}.${node.memberName}`,
    errorTarget: `${node.typeFullName}.${node.memberName}`,
    isCurrent: navigationIsCurrent,
  });
}

async function startPlatformDrill(node: InspectedCallGraphTarget) {
  invalidateGraphMemberNavigation();
  const owner = captureViewOperation(++state.memberCallGraphSeq);
  const navigationIsCurrent = () =>
    ownsViewOperation(owner, state.memberCallGraphSeq);
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  await drillPlatformNode(node, navigationIsCurrent);
}

function popPlatformDrill() {
  invalidateGraphMemberNavigation();
  observeAsync(
    callGraphInspection.popDrill(),
    "Returning to the previous platform call graph");
}

// A clicked platform (BCL) call-graph node should land the user *inside* the resident
// runtime pack at that member — a first-class, refreshable location with its own header,
// member list, breadcrumb, and URL — rather than an in-place descent that stays pinned to
// the workspace package. A not-yet-resident sibling assembly is acquired first so its
// surface can resolve the target; in-place descent preserves the target's full assembly
// identity when that surface has no unique member match.
async function navigateOrDrillPlatform(
  node: InspectedCallGraphTarget,
  section: "overview" | "call-graph" = "call-graph",
  failureSurface: GraphNavigationFailureSurface = "call-graph",
) {
  invalidateGraphMemberNavigation();
  const seq = ++state.memberCallGraphSeq;
  const owner = captureViewOperation(seq);
  const navigationIsCurrent = () =>
    ownsViewOperation(owner, state.memberCallGraphSeq);
  const discardIfStale = (
    preservedFocus: MemberFocusSnapshot | null = null,
  ) => {
    if (navigationIsCurrent()) return false;
    if (seq === state.memberCallGraphSeq) {
      state.memberCallGraphExpanding = false;
      state.platformDrillLoading = false;
      state.platformDrillError = "";
      if (preservedFocus) renderPreservingMemberFocus(preservedFocus);
      else render();
    }
    return true;
  };
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  if (!node.assembly || !callGraphTargetTypeId(node)) {
    await showPlatformTargetError(
      node,
      "the graph target does not carry an exact assembly and type identity",
      failureSurface);
    return;
  }
  const framework = state.package?.activeFramework || "";
  let pack = runtimePackForFramework(
    runtimePackPackage(),
    framework);
  if (!pack) {
    const retainedPlatform = retainedMissingPlatformTarget(
      state.workspaceShareBasis?.tabs,
      resolvedWorkspaceShareTabs(),
      framework);
    state.platformDrillLoading = true;
    state.platformDrillError = "";
    const preservedFocus = renderPreservingMemberFocus();
    const targetPack =
      platformPackForGraphAssembly(
        node.assembly,
        node.platformPack,
        runtimePackPackage(),
        framework);
    const runtimeResult = await loadRuntimePackAssembly(
      framework,
      node.assembly.endsWith(".dll")
        ? node.assembly
        : `${node.assembly}.dll`,
      targetPack ?? "",
      navigationIsCurrent,
      retainedPlatform?.version ?? "");
    pack = runtimeResult.packageModel;
    if (discardIfStale(preservedFocus)) return;
    if (pack && retainedPlatform) {
      const currentIndex = state.packages.indexOf(pack);
      if (currentIndex >= 0 && currentIndex !== retainedPlatform.tabIndex) {
        const packages = state.packages.slice();
        packages.splice(currentIndex, 1);
        packages.splice(retainedPlatform.tabIndex, 0, pack);
        state.packages = packages;
      }
    }
    state.platformDrillLoading = false;
    if (!pack) {
      const message = runtimeResult.failureMessage
        || state.runtimePackError
        || `Could not load platform assembly ${node.assembly}.`;
      if (failureSurface === "annotated") {
        state.annotatedDestinationError = message;
        renderAndFocusAnnotated({ kind: "explore" }, "embedded");
      } else {
        state.platformDrillError = message;
        renderPreservingMemberFocus(preservedFocus);
        await renderMermaidCallGraph();
      }
      return;
    }
    recordPlatformRecent(node.assembly, targetPack);
  }
  let candidate = resolveRuntimeGraphTargetCandidate(pack, node);
  let assemblyResident = runtimeGraphTargetAssemblyIsResident(pack, node);
  if (candidate.status === "ambiguous" || candidate.status === "skew") {
    await showPlatformTargetError(
      node,
      graphTargetBlockedReason(candidate, "runtime"),
      failureSurface);
    return;
  }
  let selection = findRuntimeMemberSelection(pack, node, candidate);
  if (discardIfStale()) return;
  if (candidate.status === "missing" && !assemblyResident && node.assembly) {
    state.platformDrillLoading = true;
    state.platformDrillError = "";
    const preservedFocus = renderPreservingMemberFocus();
    const targetPack =
      platformPackForGraphAssembly(
        node.assembly,
        node.platformPack,
        runtimePackPackage(),
        framework);
    const runtimeResult = await loadRuntimePackAssembly(
      framework,
      node.assembly.endsWith(".dll")
        ? node.assembly
        : `${node.assembly}.dll`,
      targetPack ?? "",
      navigationIsCurrent,
      pack.version);
    pack = runtimeResult.packageModel;
    if (!navigationIsCurrent()) {
      if (seq === state.memberCallGraphSeq) {
        state.platformDrillLoading = false;
        renderPreservingMemberFocus(preservedFocus);
      }
      return;
    }
    state.platformDrillLoading = false;
    if (!pack) {
      const message = runtimeResult.failureMessage
        || state.runtimePackError
        || `Could not load platform assembly ${node.assembly}.`;
      if (failureSurface === "annotated") {
        state.annotatedDestinationError = message;
        renderAndFocusAnnotated({ kind: "explore" }, "embedded");
      } else {
        state.platformDrillError = message;
        renderPreservingMemberFocus(preservedFocus);
        await renderMermaidCallGraph();
      }
      return;
    }
    recordPlatformRecent(node.assembly, targetPack);
    candidate = resolveRuntimeGraphTargetCandidate(pack, node);
    assemblyResident = runtimeGraphTargetAssemblyIsResident(pack, node);
    if (candidate.status === "ambiguous" || candidate.status === "skew") {
      await showPlatformTargetError(
        node,
        graphTargetBlockedReason(candidate, "runtime"),
        failureSurface);
      return;
    }
    selection = findRuntimeMemberSelection(pack, node, candidate);
  }
  if (candidate.status === "resident"
      || (candidate.status === "missing" && assemblyResident)) {
    if (section === "overview") {
      await showPlatformTargetError(
        node,
        "the platform target does not expose a selectable member overview",
        failureSurface);
      return;
    }
    await drillPlatformNode(node, navigationIsCurrent);
    return;
  }
  if (candidate.status !== "unique") {
    await showPlatformTargetError(
      node,
      "the loaded platform assembly does not contain the exact target identity",
      failureSurface);
    return;
  }
  if (!navigationIsCurrent()) return;
  if (!selection) {
    if (section === "overview") {
      await showPlatformTargetError(
        node,
        "the platform target does not expose a selectable member overview",
        failureSurface);
      return;
    }
    await drillPlatformNode(node, navigationIsCurrent);
    return;
  }
  navigateToRuntimeMember(
    pack,
    selection.type,
    selection.group,
    selection.overloadIndex,
    node,
    section);
}

async function showPlatformTargetError(
  node: InspectedCallGraphTarget,
  reason: string,
  failureSurface: GraphNavigationFailureSurface = "call-graph",
) {
  state.platformDrillLoading = false;
  const message =
    `Could not open ${node.typeFullName}.${node.memberName}: ${reason}.`;
  if (failureSurface === "annotated") {
    state.annotatedDestinationError = message;
    renderAndFocusAnnotated({ kind: "explore" }, "embedded");
    return;
  }
  state.platformDrillError = message;
  render();
  focusPlatformGraphError(document);
  await renderMermaidCallGraph();
}

// Enter the resident runtime pack focused on one member. This mirrors
// navigateToMember while clearing any active platform descent so the selected section
// loads from a fresh runtime-member location.
function navigateToRuntimeMember(
  pack: AppPackage,
  type: AppTypeSurface,
  group: AppMemberGroup,
  overloadIndex: number,
  bodyTarget: BodyTarget | null = null,
  section: "overview" | "call-graph" = "call-graph",
) {
  closeGraphExplorerForNavigation();
  invalidateGraphMemberNavigation();
  activatePackage(pack);
  const targetLibrary = libraryKey(type);
  state.libraryScope = targetLibrary ? new Set([targetLibrary]) : null;
  state.accessibilityFilter = accessibilityFilterIncludingType(
    state.accessibilityFilter,
    type);
  state.atPackageRoot = false;
  state.lens = "api";
  state.selectedTypeId = type.id;
  resetMemberFilters();
  state.memberBrowseTypeId = type.id;
  state.selectedMemberKey = group.key;
  state.selectedOverloadIndex = overloadIndex ?? 0;
  state.memberSection = section;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.platformStack = [];
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberCallGraphExpanding = false;
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = bodyTarget;
  state.typeCursor = Math.max(0, filteredTypes().findIndex(item => item.id === type.id));
  if (section === "overview") {
    observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
  } else {
    observeAsync(loadSelectedMemberCallGraph(), "Loading the member call graph");
  }
}

// Resolve a platform call-graph node's structured identity to a concrete type, member
// group, and overload in the resident runtime pack.
function findRuntimeMemberSelection(
  pack: AppPackage,
  node: InspectedCallGraphTarget,
  candidate: ReturnType<
    typeof resolveRuntimeGraphTargetCandidate<AppTypeSurface>
  > = resolveRuntimeGraphTargetCandidate(pack, node),
) {
  if (candidate.status !== "unique") return null;
  const type = candidate.type;
  const groups = memberGroups(type);
  const selection = graphMemberSelection(groups, node);
  if (!selection) return null;
  const group = groups[selection.groupIndex];
  return group
    ? { type, group, overloadIndex: selection.overloadIndex }
    : null;
}

function stripArity(name: string) {
  const tick = name.indexOf("`");
  return tick < 0 ? name : name.slice(0, tick);
}

async function openGraphSource(request: GraphSourceRequest, title: string) {
  graphExplorer.close(false);
  return sourceInspection.openGraphSource(request, title);
}

function closeGraphSource() {
  sourceInspection.closeGraphSource();
}

// Lazily load marked + DOMPurify. Both are bundled dependencies, so Vite emits them as
// same-origin chunks fetched on demand rather than loading them from a CDN. marked renders
// GFM (tables, fenced code); DOMPurify strips embedded HTML/script so Markdown carried by a
// third-party package cannot inject active content.
//
// That sanitization claim is only as good as the DOMPurify build behind it, and a CDN URL had
// no gate at all: the pinned version was never checked against any advisory feed. The gate is
// now the lockfile pin plus Dependabot vulnerability alerts, which watch that lockfile against
// the same advisory database and open a security update when one lands. That is monitoring
// rather than a merge gate: an advisory is reported after the fact instead of failing a build,
// because `npm audit` reaching the registry is not something a merge can depend on.
async function markdownLibs() {
  markdownModule ??= Promise.all([
    import("marked"),
    import("dompurify")
  ]);
  const [{ marked }, { default: DOMPurify }] = await markdownModule;
  return { marked, DOMPurify };
}

// `MARKDOWN_SANITIZE_OPTIONS` is frozen so the allow list has one immutable definition, but
// DOMPurify's config type is mutable and its hooks have historically written back through it.
// Handing it a fresh copy each call keeps the frozen original as the single source of truth.
function markdownSanitizeConfig() {
  return {
    ALLOWED_TAGS: [...MARKDOWN_SANITIZE_OPTIONS.ALLOWED_TAGS],
    ALLOWED_ATTR: [...MARKDOWN_SANITIZE_OPTIONS.ALLOWED_ATTR],
    ALLOW_ARIA_ATTR: MARKDOWN_SANITIZE_OPTIONS.ALLOW_ARIA_ATTR,
    ALLOW_DATA_ATTR: MARKDOWN_SANITIZE_OPTIONS.ALLOW_DATA_ATTR,
  };
}

async function renderMarkdown(text: string) {
  const { marked, DOMPurify } = await markdownLibs();
  const html = marked.parse(text, { gfm: true, breaks: false, async: false });
  return DOMPurify.sanitize(html, markdownSanitizeConfig());
}

async function renderMarkdownInline(text: string) {
  const { marked, DOMPurify } = await markdownLibs();
  const html = marked.parseInline(text, { gfm: true, async: false });
  return DOMPurify.sanitize(html, markdownSanitizeConfig());
}

function openPackageDocument(path: string) {
  const pkg = state.package;
  const doc = (pkg?.documents || []).find(candidate => candidate.path === path);
  if (!pkg || !doc) return undefined;
  return documentInspection.open({
    packageId: pkg.id,
    version: pkg.version,
    document: doc,
  });
}

function closeDocViewer() {
  documentInspection.close();
}

function renderDocViewer() {
  return renderDocViewerPure({
    doc: state.docViewer,
    meta: state.docViewerMeta,
    loading: state.docViewerLoading,
    error: state.docViewerError,
    html: state.docViewerHtml,
    escapeHtml,
  });
}

function invalidateSourceCaches() {
  invalidateSourceDestinationWork(state);
  state.memberSource = null;
  state.memberSourceKey = "";
  state.memberSourceError = "";
  state.typeSource = null;
  state.typeSourceKey = "";
  state.typeSourceError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedKey = "";
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.memberAnnotatedEmbedded = null;
  state.memberAnnotatedModal = null;
}

function reloadVisibleSource() {
  switch (currentSourceReloadKind()) {
    case "graph":
      if (state.graphSourceRequest) {
        observeAsync(
          openGraphSource(
            state.graphSourceRequest.request,
            state.graphSourceRequest.title),
          "Reloading graph source");
      }
      break;
    case "type":
      observeAsync(loadSelectedTypeSource(), "Reloading type source");
      break;
    case "member":
      observeAsync(loadSelectedMemberSource(), "Reloading member source");
      break;
    case "annotated":
      observeAsync(
        loadSelectedMemberAnnotatedSource(),
        "Reloading annotated member source");
      break;
  }
}

function toggleTaste(id: string) {
  const option = (state.styleOptions || []).find(item => item.id === id);
  if (state.taste.includes(id)) {
    state.taste = state.taste.filter(item => item !== id);
  } else {
    if (option?.conflict_group) {
      const groupIds = (state.styleOptions || [])
        .filter(item => item.conflict_group === option.conflict_group)
        .map(item => item.id);
      state.taste = state.taste.filter(item => !groupIds.includes(item));
    }
    state.taste = [...state.taste, id];
  }
  localStorage.setItem("inspect-taste", JSON.stringify(state.taste));
  invalidateSourceCaches();
  reloadVisibleSource();
  render();
}

function clearTaste() {
  state.taste = [];
  localStorage.setItem("inspect-taste", "[]");
  invalidateSourceCaches();
  reloadVisibleSource();
  render();
}

function dispatchApplicationAction(action: ApplicationAction) {
  switch (action) {
    case "share":
      void share();
      return;
    case "settings":
      openSettings("workbench");
      return;
    case "keyboard-help":
      if (state.keyboardHelp) closeKeyboardHelp();
      else openKeyboardHelp();
      return;
  }
}

// Open Settings, remembering the logical control that receives focus after dismissal.
function openSettings(from: "home" | "workbench") {
  state.settingsReturn = from === "workbench" ? "workbench" : "home";
  state.keyboardHelp = false;
  state.settings = true;
  render();
}

function closeSettings() {
  state.settings = false;
  reloadVisibleSource();
  render();
  requestAnimationFrame(() => {
    const selector = state.settingsReturn === "workbench"
      ? "#application-menu-button"
      : "#home-settings";
    document.querySelector<HTMLElement>(selector)
      ?.focus({ preventScroll: true });
  });
}

function openKeyboardHelp() {
  state.settings = false;
  const graphViewport =
    document.querySelector<HTMLElement>(".graph-viewport");
  keyboardHelpBindings = [
    ...keybindings.availableBindingsFor(),
    ...(graphViewport
      ? keybindings.availableBindingsFor(graphViewport)
      : []),
  ];
  state.keyboardHelp = true;
  render();
}

function closeKeyboardHelp() {
  state.keyboardHelp = false;
  render();
  requestAnimationFrame(() =>
    document.querySelector<HTMLElement>("#application-menu-button")
      ?.focus({ preventScroll: true }));
}

function renderSettingsViewHtml() {
  return renderSettingsView({
    theme: state.theme,
    settingsReturn: state.settingsReturn,
    styleCatalog: {
      styleTiers: state.styleTiers,
      styleOptions: state.styleOptions,
      styleCatalogError: state.styleCatalogError,
      taste: state.taste,
    },
    escapeHtml,
  });
}

function renderGraphSource() {
  return renderGraphSourcePure({
    title: state.graphSourceTitle,
    loading: state.graphSourceLoading,
    source: state.graphSource,
    error: state.graphSourceError,
    escapeHtml,
    highlightCSharp,
  });
}

function navigateToMember(
  pkg: AppPackage,
  type: AppTypeSurface,
  group: AppMemberGroup,
  overloadIndex: number | null = null,
  bodyTarget: BodyTarget | null = null,
  section: "overview" | "source" = "overview",
) {
  closeGraphExplorerForNavigation();
  invalidateGraphMemberNavigation();
  let selectedBodyTarget = bodyTarget;
  if (overloadIndex != null) {
    const overload = group.overloads[overloadIndex];
    if (overload) {
      selectedBodyTarget = retainGraphOnlyImplementationBody(
        overload,
        bodyTarget);
    }
  }
  activatePackage(pkg);
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.libraryScope = null;
  state.accessibilityFilter = accessibilityFilterIncludingType(
    state.accessibilityFilter,
    type);
  state.atPackageRoot = false;
  state.lens = "api";
  state.selectedTypeId = type.id;
  resetMemberFilters();
  state.memberBrowseTypeId = type.id;
  state.selectedMemberKey = group.key;
  state.selectedOverloadIndex = overloadIndex;
  state.memberSection = section;
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = selectedBodyTarget;
  if (section === "source") {
    observeAsync(loadSelectedMemberSource(), "Loading member source");
  } else {
    observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
  }
}

async function loadSelectedMemberFacts() {
  const type = selectedType();
  const member = selectedMember(type);
  if (!type || !member) {
    state.memberFactsError = "Select a concrete overload before opening Facts.";
    render();
    return;
  }
  const overload =
    selectedConcreteOverload(member.overloads, state.selectedOverloadIndex);
  if (!overload) {
    render();
    return;
  }
  const signature = memberRequestSignature(type, overload, true);
  const pkg = currentPackage();
  const implementationBody = graphOnlyImplementationBody(overload);
  const implementationMetadataToken = implementationBody?.token ?? 0;
  const implementationBodySelected = implementationMetadataToken !== 0;
  return memberDetailInspection.loadFacts({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.queryId ?? type.id,
    typeIdentity: type.definitionId ?? type.id,
    member: implementationBody?.memberName
      ?? state.selectedBodyTarget?.memberName
      ?? overload.name,
    memberSignature: overload.signature,
    selectorKey: implementationBody?.selectorKey
      ?? state.selectedBodyTarget?.selectorKey
      ?? overload.graphSelectorKey,
    metadataToken: implementationMetadataToken,
    implementationBodySelected,
    isCurrent: () => memberRequestIsCurrent(signature, true),
  });
}

interface LoadPackageOptions {
  background?: boolean;
  navigationSeq?: number;
  queryNotice?: string;
  replacePackage?: AppPackage | null;
  deepLink?: DeepLink | null;
  retryAction?: RetryAction;
  invalidateWorkspaceShareBasis?: boolean;
  failureHandler?: (message: string) => void;
}

async function loadPackage(
  packageId: string,
  version: string,
  framework: string,
  options: LoadPackageOptions = {},
): Promise<AppPackage | null> {
  if (state.engineReady)
    clearWorkspaceOccurrenceView();

  // Background restores load a tab's data into state.packages (for the tab bar and
  // cross-package edges) WITHOUT stealing the main view: no focus switch, no selection
  // reset, no loading toggle, no render. The caller (workspace restore) keeps the loading
  // overlay up and focuses the real target once, so non-target tabs never flash into view.
  const background = options.background === true;
  const navigationSeq = options.navigationSeq
    ?? (background ? null : navigationSequence.begin());
  const prevPackage = state.package;
  const prevRequested = {
    package: state.requestedPackage,
    version: state.requestedVersion,
    framework: state.requestedFramework
  };
  if (!background) {
    state.loading = true;
    state.error = "";
    state.retryAction = null;
    state.home = false;
    state.queryNotice = options.queryNotice || "";
    state.queryNoticeRetryAction = null;
    state.requestedPackage = packageId;
    state.requestedVersion = version;
    state.requestedFramework = framework;
    state.loadingSubtitle = "";
    state.loadingMessage = `Querying ${packageId}@${version}…`;
    render();
  }

  try {
    const packageModel = await packageAcquisition.loadPackage({
      packageId,
      version,
      framework,
      ...(options.replacePackage !== undefined
        ? { replacePackage: options.replacePackage }
        : {}),
      ...(navigationSeq == null
        ? {}
        : { isCurrent: () => navigationSequence.isCurrent(navigationSeq) }),
    });
    if (!packageModel) return null;
    if (background) return packageModel;
    if (options.invalidateWorkspaceShareBasis)
      state.workspaceShareBasis = null;
    activatePackage(packageModel, { resetAccessibility: true });
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.libraryScope = null;
    state.accessibilityFilter = defaultAccessibilityFilter(packageModel);
    const deep = options.deepLink;
    if (deep && (deep.type || deep.member)) {
      applyDeepLink(deep);
    } else {
      resetMemberFilters();
      state.selectedTypeId = defaultVisibleTypeId(packageModel);
      reconcileAccessibilityFilter(packageModel.types.find(item => item.id === state.selectedTypeId));
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      state.selectedOverloadIndex = null;
      state.memberSection = "overview";
    }
    state.loading = false;
    const selectionData = loadSelectionData();
    render();
    await selectionData;
    return packageModel;
  } catch (error) {
    if (navigationSeq != null && !navigationSequence.isCurrent(navigationSeq))
      return null;
    const friendly = friendlyLoadError(error, packageId, version);
    if (background) {
      const failure =
        `Workspace restore was incomplete: ${packageId}@${version}: ${friendly.message}`;
      state.queryNotice = state.queryNotice
        ? `${state.queryNotice} ${failure}`
        : failure;
      return null;
    }
    state.loading = false;
    const retryOptions: LoadPackageOptions = { ...options };
    delete retryOptions.navigationSeq;
    if (options.failureHandler) {
      options.failureHandler(friendly.message);
      return null;
    }
    if (prevPackage) {
      // A failed *new* query must not blow away an already-open workbench and trap the user
      // on a full-screen error. Keep them in their current package and restore the requested
      // identity (so URL/retry stay pinned to the good package); surface a persistent,
      // dismissible notice banner so the failure is clearly explained, not silent.
      activatePackage(prevPackage);
      state.requestedPackage = prevRequested.package;
      state.requestedVersion = prevRequested.version;
      state.requestedFramework = prevRequested.framework;
      state.error = "";
      appendQueryNotice(
        friendly.message,
        options.retryAction
        ?? (() => loadPackage(
          packageId,
          version,
          framework,
          retryOptions)));
      render();
    } else {
      state.error = state.queryNotice
        ? `${state.queryNotice} ${friendly.message}`
        : friendly.message;
      state.errorTitle = friendly.title;
      state.errorDetail = error instanceof Error
        ? error.stack || error.message
        : String(error);
      state.retryAction = () => loadPackage(
        packageId,
        version,
        framework,
        retryOptions);
      render();
    }
    return null;
  }
}

function runtimePackLoaded() {
  return runtimePackIsResident(runtimePackPackage());
}

function platformSurfaceLoaded() {
  return runtimePackPackage() !== null;
}

function runtimePackPackage() {
  return state.packages.find(item => item.isRuntimePack) || null;
}

// Display name for a package. The resident runtime pseudo-package is presented as
// ".NET Platform"; its stable identity stays "Microsoft.NETCore.App" for the wire
// protocol, tab matching, and deep-link restore (see isRuntimePackId). Every other
// package shows its own id. Presentation only — never feed this back as an identity.
function packageDisplayName(pkg: AppPackage | null | undefined) {
  return pkg && pkg.isRuntimePack ? ".NET Platform" : (pkg ? pkg.id : "");
}

// Large-n library selector for the resident Platform pseudo-package: a single
// dropdown over the full static-index roster across both shared frameworks — the
// natural expansion of the small-n library chips. Picking a resident library scopes
// the workbench to it; picking one that is not yet loaded drills in (fetching just
// that assembly). Rendered both in the nav pane (where the small-n chips live) and on
// the overview page. Returns "" until the index is available.
type PlatformLibrary = ReturnType<typeof platformLibraryRoster>[number];

interface PlatformLibrarySelectOptions {
  dataAttr?: string;
  selected?: string | null;
  requireSelection?: boolean;
}

function platformLibrarySelectHtml(
  options: PlatformLibrarySelectOptions = {},
) {
  const dataAttr = options.dataAttr || "data-platform-library-select";
  const selectedKey = options.selected;
  const roster = platformLibraryRoster("");
  if (!roster.length) return "";
  const byAssembly = new Map(roster.map(lib => [lib.assembly, lib]));
  const scoped = selectedKey !== undefined
    ? selectedKey ?? ""
    : (state.libraryScope && state.libraryScope.size === 1 ? [...state.libraryScope][0] : "");
  // Recent = the loaded/most-recently-accessed libraries: the explicit MRU first
  // (persisted across sessions), then any other currently-loaded libraries such as
  // System.Private.CoreLib, which is always resident but never explicitly "opened".
  // Resolved against the active framework's roster so counts stay honest. Duplicates
  // the .NET / ASP.NET Core catalog groups by design.
  const recentKeys: string[] = [];
  const recent: PlatformLibrary[] = [];
  const pushRecent = (lib: PlatformLibrary | undefined) => {
    if (!lib || recentKeys.includes(lib.assembly)) return;
    recentKeys.push(lib.assembly);
    recent.push(lib);
  };
  for (const entry of state.platformRecent || []) pushRecent(byAssembly.get(entry.assembly));
  for (const lib of roster) if (lib.loaded) pushRecent(lib);
  const requiresSelection = options.requireSelection === true && !scoped;
  const current = requiresSelection
    ? ""
    : scoped || recent[0]?.assembly || roster[0]?.assembly || "";
  let selectedMarked = false;
  const option = (lib: PlatformLibrary) => {
    const isSel = !selectedMarked && lib.assembly === current;
    if (isSel) selectedMarked = true;
    return `<option value="${escapeHtml(lib.assembly)}" data-pack="${escapeHtml(lib.pack)}" ${isSel ? "selected" : ""}>${escapeHtml(lib.assembly)} · ${lib.publicTypes} types</option>`;
  };
  const recentGroup = recent.length
    ? `<optgroup label="Recent">${recent.map(option).join("")}</optgroup>`
    : "";
  const group = (pack: string, label: string) => {
    const rows = roster.filter(lib => lib.pack === pack).map(option).join("");
    return rows ? `<optgroup label="${escapeHtml(label)}">${rows}</optgroup>` : "";
  };
  return `<select class="scope-select platform-library-select" ${dataAttr} aria-label="Select a platform library" title="Pick a library to scope the type list to it. Recent lists the libraries currently loaded (most-recently accessed first); .NET and ASP.NET Core are the full catalog.">
      ${requiresSelection ? '<option value="" selected disabled>Choose a library</option>' : ""}
      ${recentGroup}
      ${group("netcore.app", ".NET")}
      ${group("aspnetcore.app", "ASP.NET Core")}
    </select>`;
}

// The resident runtime pseudo-package rides in the shared workspace/URL packet under the
// display id "Microsoft.NETCore.App", but it has no NuGet nupkg — restoring it means
// re-running LoadRuntimePack (per TFM), not GetPackageBytesAsync. This id test lets the
// restore path route it correctly instead of 404-ing on a nupkg fetch.
function isRuntimePackId(id: string | null | undefined) {
  return (id ?? "").toLowerCase() === "microsoft.netcore.app";
}

const packageAcquisition = createPackageAcquisition({
  queryPackage: (packageId, version, framework) =>
    inspectPackage(packageId, version, framework),
  loadRuntimePack: (framework, platformVersion) =>
    inspectLoadRuntimePack(framework, platformVersion),
  loadRuntimePackAssembly: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) => inspectLoadRuntimePackAssembly(
    framework,
    platformVersion,
    assemblyFileName,
    pack),
  parseRuntimeSurface: json => parseEngineJson<BrowserPackageSurface>(json),
  runtimePackage: runtimePackPackage,
  retainPackage: retainPackageModel,
  recordRecentPackage,
  refreshPackageStats,
  beginRuntimeLoad() {
    state.runtimePackLoading = true;
    state.runtimePackError = "";
  },
  failRuntimeLoad(error) {
    state.runtimePackError = errorMessage(error);
  },
  endRuntimeLoad() {
    state.runtimePackLoading = false;
  },
});

interface RuntimeLoadResult {
  packageModel: AppPackage | null;
  failureMessage: string;
}

async function loadRuntimePack(
  framework: string,
  isCurrent: () => boolean = () => true,
  platformVersion = "",
): Promise<RuntimeLoadResult> {
  const result = await packageAcquisition.loadRuntimePack(
    framework,
    isCurrent,
    platformVersion);
  return {
    packageModel: result.packageModel,
    failureMessage: result.error === null ? "" : errorMessage(result.error),
  };
}

async function loadRuntimePackAssembly(
  framework: string,
  assemblyFileName: string,
  pack: string,
  isCurrent: () => boolean = () => true,
  platformVersion = "",
): Promise<RuntimeLoadResult> {
  const result = await packageAcquisition.loadRuntimePackAssembly(
    framework,
    assemblyFileName,
    pack,
    isCurrent,
    platformVersion);
  return {
    packageModel: result.packageModel,
    failureMessage: result.error === null ? "" : errorMessage(result.error),
  };
}

async function runCallGraphDemo(
  demoId: ProductHomeDemoId,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
) {
  const navigationSeq = navigationSequence.begin();
  state.loading = true;
  state.error = "";
  state.errorDetail = "";
  state.retryAction = null;
  state.loadingMessage = "Loading call graph demo…";
  state.loadingSubtitle =
    "Resolving the product workspace and anchored member…";
  render();

  const fail = (error: unknown) => {
    failDemoWorkspaceOpen(
      demoId,
      errorMessage(error),
      snapshot,
      true);
  };
  let result: BrowserHomeDemoRunResult;
  try {
    result = await inspectRunHomeDemo(demoId);
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    fail(error);
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (!result.found) {
    failDemoWorkspaceOpen(
      demoId,
      `Unknown product home demo '${demoId}'.`,
      snapshot,
      false);
    return;
  }
  if (!result.activation || !result.callGraph) {
    fail("The engine returned an incomplete product home demo result.");
    return;
  }
  if (result.activation.memberSection !== "call-graph") {
    fail(
      `The engine returned unsupported demo section '${result.activation.memberSection}'.`,
    );
    return;
  }

  try {
    const packages = result.packages.map(createNuGetPackageModel);
    const activation = result.activation;
    const targetPackage = packages.find(item =>
      item.id === activation.focusPackage
      && item.version === activation.focusVersion
      && item.activeFramework === activation.focusFramework);
    const type = targetPackage?.types.find(item =>
      item.id === activation.typeId);
    const member = type && memberGroups(type).find(item =>
      item.name === activation.memberName
      && item.kind === activation.memberKind);
    const overloadIndex = member?.overloads.findIndex(item =>
      item.anchorDigest === activation.memberAnchorDigest) ?? -1;
    const overload = member?.overloads[overloadIndex];
    if (!targetPackage || !type || !member || !overload) {
      throw new Error(
        "The engine-run demo selection was not present in its returned package surfaces.");
    }

    clearWorkspacePackages();
    for (const packageModel of packages) {
      retainPackageModel(packageModel);
      recordRecentPackage(
        packageModel.id,
        packageModel.version,
        packageModel.activeFramework);
    }
    refreshPackageStats();

    activatePackage(targetPackage, { resetAccessibility: true });
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.libraryScope = null;
    state.selectedTypeId = type.id;
    state.atPackageRoot = false;
    state.lens = "api";
    state.packageLens = "overview";
    resetMemberFilters();
    resetMemberSectionState();
    state.platformStack = [];
    state.memberBrowseTypeId = type.id;
    state.selectedMemberKey = member.key;
    state.selectedOverloadIndex = overloadIndex;
    state.memberSection = "call-graph";
    const captured = capturedShareTabs();
    const activeIndex = state.packages.indexOf(targetPackage);
    const participantTabIds = packages.map(packageModel => {
      const index = state.packages.findIndex(candidate =>
        packageIdentityEquals(candidate, packageModel));
      const tab = captured.tabs[index];
      if (!tab) {
        throw new Error(
          "The product demo package is no longer part of the Browser workspace.");
      }
      return tab.id;
    });
    const topology = callGraphCaptureTopology(
      captured.tabs,
      activeIndex,
      participantTabIds);
    const activeTab = captured.tabs[activeIndex]!;
    state.workspaceShareBasis = {
      tabs: captured.tabs,
      contexts: topology.contexts,
      activeTabId: activeTab.id,
      selectedContextId: topology.selectedContextId,
      view: {
        lens: "api",
        type: type.id,
        memberAnchor: overload.anchorDigest,
        memberSignature: null,
        section: "call-graph",
        libraries: [],
      },
    };
    // This graph is scoped to the product-defined demo workspace, not any
    // unrelated tabs the user may already have open.
    state.memberCallGraph = result.callGraph;
    state.memberCallGraphError = "";
    state.memberCallGraphLoading = false;
    state.memberCallGraphExpanding = false;
    state.memberCallGraphKey = memberRequestSignature(
      type,
      overload,
      true);
    state.loading = false;
    stageDemoNavigation(navigationSeq, buildStateUrl().toString());
    render();
    let renderResult = await renderMermaidCallGraph();
    while (renderResult.status === "superseded"
      && navigationSequence.isCurrent(navigationSeq)
      && currentCallGraph()?.mermaid === result.callGraph.mermaid
      && document.querySelector("#call-graph-diagram")) {
      renderResult = await renderMermaidCallGraph();
    }
    if (!navigationSequence.isCurrent(navigationSeq)) {
      cancelDemoNavigation(navigationSeq);
      return;
    }
    if (renderResult.status === "superseded") {
      cancelDemoNavigation(navigationSeq);
      syncUrl();
      return;
    }
    if (renderResult.status === "failed") {
      throw new Error(renderResult.message);
    }
    if (!commitDemoNavigation(navigationSeq)) {
      cancelDemoNavigation(navigationSeq);
      return;
    }
    syncUrl();
    focusInspectionResult(navigationSeq);
  } catch (error) {
    cancelDemoNavigation(navigationSeq);
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    fail(error);
  }
}

// Loads the full open-tab set described by a parsed location (opaque workspace bucket, or a
// lone target), then restores the active tab's platform library scope and deep-link
// selection. Shared by boot restore, refreshed/shared links, and the in-app demo buttons.
async function restoreWorkspaceFromLocation(
  loc: ParsedLocation,
  deep: DeepLink,
  navigationSeq = navigationSequence.begin(),
  canonicalSnapshot = loc.hasWorkspaceState
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null,
  focusResult = false,
  failureHandler: WorkspaceRestoreFailureHandler | null = null,
) {
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (loc.routeFailure) {
    if (failureHandler) {
      failureHandler(loc.routeFailure.message);
    } else {
      failWorkspaceRoute(loc.routeFailure.message);
    }
    return;
  }
  if (!clearWorkspaceRouteFailure()) {
    if (failureHandler) {
      failureHandler("The existing package route could not be cleared.");
      return;
    }
    render();
    return;
  }
  if (loc.hasWorkspaceState && !loc.shareState) {
    const message = loc.workspaceNotice
      || "The shared workspace packet could not be restored.";
    if (failureHandler) {
      failureHandler(message);
    } else {
      failCanonicalWorkspaceRestore(
        loc,
        deep,
        message,
        canonicalSnapshot,
        null);
    }
    return;
  }
  if (!loc.package) {
    failureHandler?.(
      "The resolved product demo did not identify a package.");
    return;
  }
  const retryRestore = () => restoreWorkspaceFromLocation(
    loc,
    deep,
    undefined,
    undefined,
    focusResult,
    failureHandler);
  const failRestore = (message: string) => {
    if (failureHandler) {
      failureHandler(message);
    } else {
      failCanonicalWorkspaceRestore(
        loc,
        deep,
        message,
        canonicalSnapshot,
        retryRestore);
    }
  };
  state.queryNotice = loc.workspaceNotice || "";
  state.queryNoticeRetryAction = null;
  state.home = false;
  applyLocationView(loc);
  state.loading = true;
  state.error = "";
  state.retryAction = null;
  resetLocationFilters();
  clearWorkspacePackages();
  render();
  const target: WorkspaceCoordinate = {
    id: loc.package,
    version: loc.version || "latest",
    framework: loc.framework || ""
  };
  const tabs = (loc.tabs && loc.tabs.length)
    ? loc.tabs.slice(0, MAX_WORKSPACE_PACKAGES)
    : [target];
  type RestorableCoordinate = {
    id: string;
    version: string;
    framework?: string;
    activeFramework?: string;
  };
  const matchesFramework = (tab: RestorableCoordinate) =>
    !target.framework
    || (tab.framework || tab.activeFramework || "").toLowerCase()
      === target.framework.toLowerCase();
  const matchesTarget = (tab: RestorableCoordinate) =>
    isRuntimePackId(tab.id)
      ? isRuntimePackId(target.id)
        && (target.version.toLowerCase() === "latest"
          || tab.version.toLowerCase() === target.version.toLowerCase())
        && matchesFramework(tab)
      : (tab.id.toLowerCase() === target.id.toLowerCase()
        && tab.version.toLowerCase() === target.version.toLowerCase()
        && matchesFramework(tab));
  if (!tabs.some(matchesTarget)) {
    if (tabs.length === MAX_WORKSPACE_PACKAGES) {
      tabs.pop();
      const notice =
        `The shared workspace exceeds the ${MAX_WORKSPACE_PACKAGES}-package limit and was truncated to keep the requested package.`;
      state.queryNotice = state.queryNotice ? `${state.queryNotice} ${notice}` : notice;
    }
    tabs.push(target);
  }

  // Load every tab's data so the tab bar and cross-package edges come back, but keep the
  // main view under the loading overlay throughout: NuGet tabs load in the background (no
  // focus steal) and loadRuntimePack already never steals focus. The real target is focused
  // once, below — so a non-target tab (e.g. an STJ tab on a platform-library link) never
  // flashes into view before the target resolves.
  let loadedTargetModel: AppPackage | null = null;
  let runtimeFailureMessage = "";
  let failedTabCount = 0;
  for (const tab of tabs) {
    let loaded: AppPackage | null;
    if (isRuntimePackId(tab.id)) {
      const runtimeResult = await loadRuntimePack(
        tab.framework,
        () => navigationSequence.isCurrent(navigationSeq),
        tab.version);
      loaded = runtimeResult.packageModel;
      runtimeFailureMessage = runtimeResult.failureMessage;
      if (!loaded && navigationSequence.isCurrent(navigationSeq)) {
        const failure =
          `Workspace restore was incomplete: ${tab.id}: ${runtimeFailureMessage
            || state.runtimePackError
            || "runtime pack acquisition failed."}`;
        state.queryNotice = state.queryNotice
          ? `${state.queryNotice} ${failure}`
          : failure;
      }
    } else {
      loaded = await loadPackage(tab.id, tab.version, tab.framework, {
        background: true,
        navigationSeq
      });
    }
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (!loaded) failedTabCount++;
    if (loaded && matchesTarget(tab)) loadedTargetModel = loaded;
  }

  const resolvedTabs = resolvedWorkspaceShareTabs();
  const canonicalTabCountPreserved = !loc.shareState
    || loc.shareState.tabs.length === resolvedTabs.length;
  const canonicalTabsPreserved = !loc.shareState
    || workspaceShareTabsMatchResolved(loc.shareState.tabs, resolvedTabs);
  if (loc.shareState && (failedTabCount > 0 || !canonicalTabsPreserved)) {
    failRestore(
      state.queryNotice
      || (failedTabCount > 0
        ? "The shared workspace could not be restored completely."
        : canonicalTabCountPreserved
          ? "A shared workspace coordinate resolved to a different version or framework than the packet requested."
          : "The shared workspace coordinates did not remain distinct after resolution."));
    return;
  }

  const targetModel = loadedTargetModel ?? state.packages.find(matchesTarget);
  if (targetModel) {
    activatePackage(targetModel, { resetAccessibility: true });
    // Restore the platform library scope captured in the share packet before applying the
    // deep link, so a refreshed/shared platform-library link lands on that library. Called
    // unconditionally (not just when loc.library is set) so an aggregate/no-library restore
    // also clears any scope left over from a previous session -- applyPlatformLibraryScope's
    // own falsy-key branch handles that case synchronously.
    if (isRuntimePackId(targetModel.id)) {
      const scoped = await applyPlatformLibraryScope(
        loc.library,
        loc.libraryPack,
        navigationSeq,
        () => restoreWorkspaceFromLocation(
          loc,
          deep,
          undefined,
          canonicalSnapshot,
          focusResult,
          failureHandler));
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      if (!scoped) {
        if (loc.shareState) {
          failRestore(
            `The shared Platform library '${loc.library}' could not be restored.`);
        }
        return;
      }
    } else {
      const libraryFailure = applyLoadedPackageLibraryScope(
        targetModel,
        loc.library);
      if (loc.shareState && libraryFailure) {
        failRestore(libraryFailure);
        return;
      }
    }
    applyLocationView(loc);
    const viewFailure = loc.shareState
      ? canonicalViewRestorationFailure(targetModel, deep, loc.lens)
      : null;
    if (loc.shareState && viewFailure) {
      failRestore(viewFailure);
      return;
    }
    applyDeepLink(deep);
    commitWorkspaceShareBasis(loc.shareState);
    state.loading = false;
    render();
    await loadSelectionData();
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (failureHandler) {
      if (!commitDemoNavigation(navigationSeq)) return;
      syncUrl();
    }
    if (focusResult) {
      focusInspectionResult(navigationSeq);
    }
  } else if (!isRuntimePackId(target.id)) {
    // The focused NuGet target failed to load during the silent background pass; re-run it in
    // the foreground so its error (e.g. a 404) surfaces properly instead of a blank workbench.
    const loaded = await loadPackage(
      target.id,
      target.version,
      target.framework,
      {
        deepLink: deep,
        navigationSeq,
        queryNotice: state.queryNotice
      });
    if (loaded && focusResult && navigationSequence.isCurrent(navigationSeq)) {
        if (failureHandler) {
          if (!commitDemoNavigation(navigationSeq)) return;
          syncUrl();
        }
        render();
      focusInspectionResult(navigationSeq);
    } else if (!loaded && failureHandler
      && navigationSequence.isCurrent(navigationSeq)) {
      failRestore(
        state.error || state.queryNotice
        || `Couldn’t load ${target.id}@${target.version}.`);
    }
  } else {
    state.loading = false;
    const runtimeFailure =
      runtimeFailureMessage
      || state.runtimePackError
      || "Couldn’t load the requested .NET Platform.";
    const failure = state.queryNotice
      ? `${state.queryNotice} ${runtimeFailure}`
      : runtimeFailure;
    if (failureHandler) {
      failRestore(failure);
      return;
    }
    state.error = failure;
    state.errorTitle = "Platform failed";
    state.retryAction = retryRestore;
    render();
  }
}

function failWorkspaceRoute(message: string) {
  if (state.package) {
    state.credits = false;
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    failedWorkspaceUrlState = {
      kind: "route",
      notice: `Package route failed: ${message}`,
      url: location.href,
      projection: workspaceUrlProjection(),
      pathname: location.pathname,
      search: location.search,
      recoveryUrl: buildPackageRootStateUrl(location.href, {
        package: state.package.id,
        version: state.package.version,
        framework: state.package.activeFramework,
        lens: state.packageLens,
      }).toString(),
    };
    render();
    return;
  }
  clearWorkspacePackages();
  state.credits = false;
  state.loading = false;
  state.home = false;
  state.errorTitle = "Package route failed";
  state.error = message;
  state.errorDetail = "";
  state.retryAction = retryUnavailable;
  render();
}

function failCanonicalWorkspaceRestore(
  loc: ParsedLocation,
  deep: DeepLink,
  message: string,
  snapshot: CanonicalWorkspaceRestoreSnapshot | null = null,
  retryAction: RetryAction =
    () => restoreWorkspaceFromLocation(loc, deep),
) {
  const failedUrl = location.href;
  const ownedRetryAction = retryAction
    ? bindWorkspaceRetryToUrl(
      failedUrl,
      () => location.href,
      url => workspaceLocation.replace(url, history.state),
      retryAction)
    : null;
  if (snapshot?.hasWorkspace) {
    restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
    state.credits = false;
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    appendQueryNotice(
      `Workspace restore failed: ${message}`,
      ownedRetryAction);
    failedWorkspaceUrlState = {
      kind: "canonical",
      url: failedUrl,
      projection: workspaceUrlProjection(),
    };
    render();
    return;
  }
  clearWorkspacePackages();
  state.credits = false;
  state.loading = false;
  state.home = false;
  state.errorTitle = "Workspace restore failed";
  state.error = message;
  state.retryAction = ownedRetryAction;
  render();
}

function applyLocationView(loc: ParsedLocation) {
  state.lens = loc.lens || "api";
  state.atPackageRoot = loc.atPackageRoot || false;
  state.workspaceSubjectOpen =
    loc.workspaceSubjectOpen && state.atPackageRoot;
  state.packageLens = loc.packageLens || "overview";
}

// Restores the full open-tab set from the opaque workspace bucket (or just the visible
// target for a lone/legacy link), loading each tab in order so the tab bar and any
// cross-package dependency edges come back. Only the focused target restores its deep-link.
async function restoreInitialWorkspace() {
  const navigationSeq = navigationSequence.current();
  const loc = workspaceLocation.preflightCurrent().resolve();
  if (loc.routeFailure) {
    await restoreWorkspaceFromLocation(
      loc,
      deepLinkFromLocation(loc),
      navigationSeq);
    return;
  }
  if (loc.hasWorkspaceState && !loc.shareState) {
    await restoreWorkspaceFromLocation(
      loc,
      deepLinkFromLocation(loc),
      navigationSeq);
    return;
  }
  const packageId = loc.package;
  if (!packageId) {
    state.loading = false;
    state.home = true;
    state.queryNotice = loc.workspaceNotice || "";
    render();
    return;
  }
  const resolvedLocation = {
    ...loc,
    package: packageId,
    version: loc.version || "latest",
    framework: loc.framework || DEFAULT_REQUESTED_FRAMEWORK
  };
  state.requestedPackage = resolvedLocation.package;
  state.requestedVersion = resolvedLocation.version;
  state.requestedFramework = resolvedLocation.framework;
  await restoreWorkspaceFromLocation(
    resolvedLocation,
    deepLinkFromLocation(resolvedLocation),
    navigationSeq);
}

function isStyleTier(value: unknown): value is StyleTier {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.title === "string"
    && typeof value.summary === "string";
}

function isStyleOption(value: unknown): value is StyleOption {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.tier === "string"
    && typeof value.title === "string"
    && typeof value.summary === "string";
}

async function bootstrap() {
  state.loading = !state.home;
  state.engineReady = false;
  state.engineStartupFailed = false;
  state.engineStatus = "Loading browser WebAssembly…";
  state.error = "";
  state.retryAction = null;
  render();
  const tStart = performance.now();
  try {
    if (state.home) await waitForHomePaint();
    await loadEngineModule();
    const reportEngineStatus = (message: string) => {
      state.loadingMessage = message;
      state.engineStatus = message;
      if (!state.credits) render();
    };
    reportEngineStatus("Loading .NET WebAssembly…");
    await startEngine(window.location.origin);
    reportEngineStatus("Reading package assemblies…");
    state.buildIdentity = inspectBuildIdentity();
    const tEngine = performance.now();
    try {
      const vocabulary = inspectVocabulary();
      const sections = vocabulary?.sections || [];
      state.styleTiers = (
        sections.find(section => section.id === "csharp.style-tiers")?.values
        || []).filter(isStyleTier);
      state.styleOptions = (
        sections.find(section => section.id === "csharp.style-choices")?.values
        || []).filter(isStyleOption);
      const reconciledTaste = reconcileStyleTaste(
        state.taste,
        state.styleOptions);
      if (reconciledTaste.length !== state.taste.length) {
        state.taste = reconciledTaste;
        localStorage.setItem("inspect-taste", JSON.stringify(state.taste));
      }
    } catch (error) {
      state.styleTiers = [];
      state.styleOptions = [];
      state.styleCatalogError = errorMessage(error);
    }
    try {
      setProductHomeDemoCatalog(inspectListHomeDemos().demos ?? []);
      productHomeDemoCatalogError = "";
    } catch (error) {
      setProductHomeDemoCatalog([]);
      productHomeDemoCatalogError =
        `Product demos are unavailable: ${errorMessage(error) || "Unknown error."}`;
    }
    try {
      state.packageQueryFacets =
        packageQueryFacets(inspectListPackageQueryFacets());
    } catch (error) {
      state.packageQueryFacets = [];
      state.packageQueryCatalogError =
        `Package-query facets are unavailable: ${errorMessage(error) || "Unknown error."}`;
    }
    state.engineReady = true;
    state.engineStatus = "";
    if (state.home) {
      // Engine is warm and search is ready; show the intro/home page without loading a package.
      state.loading = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      if (!state.credits) render();
      return;
    }
    if (state.packageQueryOpen) {
      state.loading = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      focusPackageQueryInput();
      return;
    }
    if (isProductHomeDemosPath(location.pathname)) {
      state.loading = false;
      state.workspaceSubjectOpen = true;
      state.atPackageRoot = true;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      if (!state.packageQueryReturnFocusPending) {
        afterCurrentNavigationFrame(() =>
          focusWorkspace(document));
      }
      return;
    }
    await restoreInitialWorkspace();
    const tReady = performance.now();
    state.diag = computeDiagnostics(tStart, tEngine, tReady);
    render();
  } catch (error) {
    state.loading = false;
    state.engineReady = false;
    state.engineStartupFailed = true;
    state.engineStatus = "";
    state.error = "Couldn’t start the inspection engine. Retry, or open a different package.";
    state.errorTitle = "Startup failed";
    state.errorDetail = error instanceof Error
      ? error.stack || error.message
      : String(error);
    state.retryAction = () => window.location.reload();
    if (!state.credits) render();
  }
}

function computeDiagnostics(
  tStart: number,
  tEngine: number,
  tReady: number,
): Diagnostics {
  const assets = performance.getEntriesByType("resource")
    .filter((entry): entry is PerformanceResourceTiming =>
      entry instanceof PerformanceResourceTiming
      && entry.name.includes("/_framework/"));
  let firstStart = Infinity;
  let lastEnd = 0;
  let transfer = 0;
  let decoded = 0;
  for (const entry of assets) {
    firstStart = Math.min(firstStart, entry.startTime);
    lastEnd = Math.max(lastEnd, entry.responseEnd);
    transfer += entry.transferSize || 0;
    decoded += entry.decodedBodySize || 0;
  }
  const hasAssets = assets.length > 0 && Number.isFinite(firstStart);
  return {
    downloadMs: hasAssets ? lastEnd - firstStart : 0,
    startupMs: hasAssets ? Math.max(0, tEngine - lastEnd) : tEngine - tStart,
    precomputeMs: tReady - tEngine,
    totalMs: tReady,
    transfer,
    decoded,
    assets: assets.length
  };
}

function refreshPackageStats() {
  try {
    const stats = inspectPackageCacheStats();
    if (stats) state.packageCacheStats = stats;
  } catch {
    // Keep the last known counts; a stats read failure must not disrupt inspection.
  }
}


// A same-origin, unmodified `<a href>` click anywhere in the app takes over here instead
// of loading a new document — this is the single owner of in-app link navigation.
// `target="_blank"`, cross-origin hrefs, `download`, and modified clicks (new tab/window)
// keep their native browser behavior; the guard lives in `shouldInterceptLinkClick`.
function navigateInAppUrl(url: URL) {
  if (isCreditsPath(url.pathname)) {
    openCredits();
    return;
  }
  if (isProductHomeDemosPath(url.pathname)) {
    openProductDemos();
    return;
  }
  if (isPackageQueryPath(url.pathname)) {
    openPackageQueryRoute();
    return;
  }
  if (url.pathname === "/" && !url.search && !url.hash) {
    goHome();
    return;
  }
  const focusWorkspaceAfterQuery = state.packageQueryOpen;
  if (focusWorkspaceAfterQuery) {
    state.packageQueryOpen = false;
    packageQueryController.cancel();
    state.packageQueryNavigationError = "";
  }
  const navigationSeq = navigationSequence.begin();
  if (focusWorkspaceAfterQuery) {
    packageQueryWorkspaceFocusNavigationSeq = navigationSeq;
  }
  workspaceLocation.push(url.toString());
  const loc = parseLocation();
  observeAsync(
    restoreWorkspaceFromLocation(loc, loc, navigationSeq),
    "Navigating");
}

bindWorkspaceLinkNavigation(document, {
  currentOrigin: () => location.origin,
  resolve: href => new URL(href, location.href),
  navigate: navigateInAppUrl,
});

const containedShortcutKeys = ["f", "k", "p"] as const;
const alphabetKeys = "abcdefghijklmnopqrstuvwxyz".split("");

function registerContainedShortcuts(
  id: string,
  priority: number,
  when: () => boolean,
): void {
  keybindings.register({
    id,
    key: containedShortcutKeys,
    modifiers: { commandOrControl: true },
    allowExtraModifiers: true,
    priority,
    when,
    run: () => true,
  });
}

function workspaceKeyboardContextIsActive(): boolean {
  return !graphExplorer.isOpen
    && !state.explorer?.open
    && !state.settings
    && !state.keyboardHelp
    && !state.home
    && !state.packageQueryOpen
    && !state.loading
    && !state.error
    && !state.graphSourceOpen
    && !state.docViewerOpen
    && state.memberAnnotatedModal === null
    && !state.spotlightOpen;
}

const workspaceModalContextIsAvailable = () =>
  !state.home && !state.packageQueryOpen && !state.loading && !state.error;
const graphSourceContextIsActive = () =>
  workspaceModalContextIsAvailable() && state.graphSourceOpen;
const annotatedSourceContextIsActive = () =>
  workspaceModalContextIsAvailable() && state.memberAnnotatedModal !== null;
const embeddedAnnotatedSourceDetailContextIsActive = () =>
  workspaceKeyboardContextIsActive()
  && !workbenchOverlayOwnsFocus()
  && state.memberSection === "annotated"
  && Boolean(state.memberAnnotatedEmbedded?.detail);
const annotatedSourceEscapeContextIsActive = () =>
  annotatedSourceContextIsActive()
  || embeddedAnnotatedSourceDetailContextIsActive();
const documentViewerContextIsActive = () =>
  workspaceModalContextIsAvailable() && state.docViewerOpen;
const spotlightContextIsActive = () =>
  workspaceModalContextIsAvailable() && state.spotlightOpen;
const workspaceDrillOutIsAvailable = () =>
  workspaceKeyboardContextIsActive()
  && (navMode() === "member" || !state.atPackageRoot);
const inspectionNavigationIsAvailable = () =>
  workspaceKeyboardContextIsActive() && scope() !== "workspace";
const workspaceDrillInIsAvailable = () =>
  workspaceKeyboardContextIsActive() && state.package !== null;
const workspaceHistoryBackIsAvailable = () =>
  workspaceKeyboardContextIsActive() && navigationHistory.canBack();
const workspaceHistoryForwardIsAvailable = () =>
  workspaceKeyboardContextIsActive() && navigationHistory.canForward();

keybindings.register({
  id: "metadata-explorer.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.metadataExplorer,
  when: () => Boolean(state.explorer?.open),
  run: () => {
    if (!state.explorer!.overview) explorerShowOverview();
    else closeExplorer();
    return true;
  },
});
keybindings.register({
  id: "metadata-explorer.history",
  key: "Backspace",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.metadataExplorer,
  when: () => Boolean(state.explorer?.open),
  run: event => {
    if (event.shiftKey) explorerHistoryForward();
    else explorerHistoryBack();
    return true;
  },
});
registerContainedShortcuts(
  "metadata-explorer.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.metadataExplorer,
  () => Boolean(state.explorer?.open),
);

keybindings.register({
  id: "settings.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.settings,
  when: () => state.settings,
  run: () => {
    closeSettings();
    return true;
  },
});
registerContainedShortcuts(
  "settings.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.settings,
  () => state.settings,
);
keybindings.register({
  id: "keyboard-help.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.settings,
  when: () => state.keyboardHelp,
  run: () => {
    closeKeyboardHelp();
    return true;
  },
});
registerContainedShortcuts(
  "keyboard-help.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.settings,
  () => state.keyboardHelp,
);

const unavailableWorkspaceContext = () =>
  !state.home && (state.loading || Boolean(state.error));
registerContainedShortcuts(
  "unavailable-workspace.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.unavailableWorkspace,
  unavailableWorkspaceContext,
);
keybindings.register({
  id: "unavailable-workspace.contain-filter-shortcut",
  key: "/",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.unavailableWorkspace,
  when: unavailableWorkspaceContext,
  run: () => true,
});

keybindings.register({
  id: "graph-source.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.graphSource,
  when: graphSourceContextIsActive,
  run: () => {
    closeGraphSource();
    return true;
  },
});
registerContainedShortcuts(
  "graph-source.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.graphSource,
  graphSourceContextIsActive,
);
registerContainedShortcuts(
  "graph-explorer.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.graphSource,
  () => graphExplorer.isOpen,
);

keybindings.register({
  id: "annotated-source.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.annotatedSource,
  when: annotatedSourceEscapeContextIsActive,
  run: () => {
    if (!state.memberAnnotated) return false;
    const session =
      state.memberAnnotatedModal ?? state.memberAnnotatedEmbedded;
    if (!session) return false;
    let model: AnnotatedSourceViewerModel;
    try {
      model = createAnnotatedSourceViewerModel(state.memberAnnotated);
    } catch (error) {
      if (!(error instanceof TypeError) || session.surface !== "modal") throw error;
      return dismissAnnotatedSourceModal(true);
    }
    const escaped = escapeAnnotatedSource(model, session);
    if (session.surface === "modal") state.memberAnnotatedModal = escaped.state;
    else state.memberAnnotatedEmbedded = escaped.state;
    if (escaped.dismissModal) dismissAnnotatedSourceModal(true);
    else if (escaped.focus)
      renderAndFocusAnnotated(escaped.focus, session.surface);
    return escaped.handled;
  },
});
registerContainedShortcuts(
  "annotated-source.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.annotatedSource,
  annotatedSourceContextIsActive,
);

keybindings.register({
  id: "document-viewer.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.documentViewer,
  when: documentViewerContextIsActive,
  run: () => {
    closeDocViewer();
    return true;
  },
});
registerContainedShortcuts(
  "document-viewer.contain-browser-shortcut",
  WORKBENCH_KEYBINDING_PRIORITY.documentViewer,
  documentViewerContextIsActive,
);

keybindings.register({
  id: "spotlight.dismiss",
  key: "Escape",
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.spotlight,
  when: spotlightContextIsActive,
  run: () => {
    closeSpotlight();
    return true;
  },
});
keybindings.register({
  id: "spotlight.open-commands",
  key: "k",
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.spotlight,
  when: spotlightContextIsActive,
  run: () => {
    openSpotlight("", "commands");
    return true;
  },
});
keybindings.register({
  id: "spotlight.open-all",
  key: "p",
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.spotlight,
  when: spotlightContextIsActive,
  run: () => {
    openSpotlight();
    return true;
  },
});
keybindings.register({
  id: "spotlight.contain-browser-find",
  key: "f",
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.spotlight,
  when: spotlightContextIsActive,
  run: () => true,
});

keybindings.register({
  id: "workspace.drill-out-escape",
  key: "Escape",
  available: workspaceDrillOutIsAvailable,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: () => !isTextEntry(),
  run: () => {
    if (navMode() === "member") exitMemberScope();
    else drillOut();
    return true;
  },
});
keybindings.register({
  id: "workspace.open-commands",
  key: "k",
  available: workspaceKeyboardContextIsActive,
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  run: () => {
    openSpotlight("", "commands");
    return true;
  },
});
keybindings.register({
  id: "workspace.open-all",
  key: "p",
  available: workspaceKeyboardContextIsActive,
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  run: () => {
    openSpotlight();
    return true;
  },
});
keybindings.register({
  id: "workspace.focus-filter",
  key: "f",
  available: inspectionNavigationIsAvailable,
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  run: () => {
    focusFilter();
    return true;
  },
});

for (const [key, action, available] of [
  ["ArrowLeft", navBack, workspaceHistoryBackIsAvailable],
  ["ArrowRight", navForward, workspaceHistoryForwardIsAvailable],
] as const) {
  keybindings.register({
    id: `workspace.history-alt-${key}`,
    key,
    available,
    modifiers: { alt: true },
    allowExtraModifiers: true,
    priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
    when: event => !event.metaKey && !event.ctrlKey,
    run: () => {
      action();
      return true;
    },
  });
  keybindings.register({
    id: `workspace.history-shift-${key}`,
    key,
    available,
    modifiers: { shift: true },
    priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
    when: () => !isTextEntry(),
    run: () => {
      action();
      return true;
    },
  });
}

keybindings.register({
  id: "workspace.select-lens",
  key: ["1", "2", "3", "4", "5", "6", "7", "8", "9"],
  available: inspectionNavigationIsAvailable,
  allowExtraModifiers: true,
  preventDefault: false,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: event => !isTextEntry()
    && !event.metaKey
    && !event.ctrlKey,
  run: event => {
    selectScopeLensByIndex(Number(event.key) - 1, scope());
    return true;
  },
});
keybindings.register({
  id: "workspace.navigate-vertical",
  key: ["ArrowUp", "ArrowDown"],
  available: inspectionNavigationIsAvailable,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: event => !isTextEntry()
    && !event.metaKey
    && !event.ctrlKey
    && !event.altKey,
  run: event => {
    stepNav(event.key === "ArrowDown" ? 1 : -1);
    return true;
  },
});
keybindings.register({
  id: "workspace.navigate-horizontal",
  key: ["ArrowLeft", "ArrowRight"],
  available: inspectionNavigationIsAvailable,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: () => !isTextEntry(),
  run: event => {
    stepHorizontal(event.key === "ArrowRight" ? 1 : -1);
    return true;
  },
});
keybindings.register({
  id: "workspace.drill-in",
  key: "Enter",
  available: workspaceDrillInIsAvailable,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: event => !isTextEntry()
    && !event.metaKey
    && !event.ctrlKey
    && !event.altKey
    && !isInteractiveElement(
      event.target instanceof Element ? event.target : null),
  run: () => {
    drillIn();
    return true;
  },
});
keybindings.register({
  id: "workspace.drill-out-backspace",
  key: "Backspace",
  available: workspaceDrillOutIsAvailable,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: event => !isTextEntry()
    && !event.metaKey
    && !event.ctrlKey
    && !event.altKey,
  run: () => {
    drillOut();
    return true;
  },
});
keybindings.register({
  id: "workspace.focus-filter-slash",
  key: "/",
  available: inspectionNavigationIsAvailable,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: () => !isTextEntry(),
  run: () => {
    focusFilter();
    return true;
  },
});
keybindings.register({
  id: "workspace.seed-spotlight",
  key: alphabetKeys,
  allowExtraModifiers: true,
  priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
  when: event => workspaceKeyboardContextIsActive()
    && !isTextEntry()
    && !event.metaKey
    && !event.ctrlKey
    && !event.altKey,
  run: event => {
    openSpotlight(event.key);
    return true;
  },
});

keybindings.attach(document);
document.addEventListener("pointerdown", trackContentFramePointer);
document.addEventListener("focusin", trackContentFrameFocus);
document.addEventListener("focusout", releaseContentFrameFocusOwner);
bindContentFrameMedia(contentFrameMedia, handleContentFrameResize);

// Re-apply state when the address bar changes underneath us (browser back/forward, or a
// hand-edited URL). Within the loaded package we mutate selection directly; a different
// package is (re)loaded with the URL selection queued as a deep link.
function clearNavigationError() {
  if (state.engineStartupFailed) return;
  state.error = "";
  state.errorTitle = "";
  state.errorDetail = "";
  state.retryAction = null;
}

function dismissModalsForRoutedNavigation() {
  closeGraphExplorerForNavigation();
  const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal(false);
  state.settings = false;
  state.keyboardHelp = false;
  state.explorer = null;
  spotlight.reset();
  sourceInspection.clearGraphSource();
  documentInspection.clear();
  return dismissedAnnotatedSourceModal;
}

window.addEventListener("popstate", () => {
  const leftPackageQueryHandoff = currentPackageQueryHandoff();
  const navigationSeq = navigationSequence.begin();
  let leftPackageQueryForWorkspaceSuccessor = false;
  const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation();
  invalidateMemberDestinationWork(state);
  if (dismissedAnnotatedSourceModal) render({ synchronizeUrl: false });
  if (isPackageQueryPath(location.pathname)) {
    clearNavigationError();
    applyPackageQueryHistory(history.state);
    packageQueryHandoffNavigationSeq = null;
    state.packageQueryOpen = true;
    state.credits = false;
    state.home = false;
    state.loading = !state.engineReady;
    render();
    if (state.engineReady) focusPackageQueryInput();
    return;
  }
  state.loading = false;
  if (state.packageQueryOpen || leftPackageQueryHandoff) {
    state.packageQueryOpen = false;
    packageQueryHandoffNavigationSeq = null;
    packageQueryController.cancel();
    state.packageQueryReturnFocusPending =
      state.packageQueryReturnFocus !== null
      && isPackageQueryPredecessor(
        history.state,
        state.packageQueryPredecessorEntryId);
    leftPackageQueryForWorkspaceSuccessor =
      !state.packageQueryReturnFocusPending;
  }
  if (isCreditsPath(location.pathname)) {
    clearNavigationError();
    if (!clearWorkspaceRouteFailure()) {
      render();
      return;
    }
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.credits = true;
    state.home = true;
    spotlight.reset();
    render();
    return;
  }
  if (isProductHomeDemosPath(location.pathname)) {
    clearNavigationError();
    if (!clearWorkspaceRouteFailure()) {
      render();
      return;
    }
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.credits = false;
    state.home = false;
    state.workspaceSubjectOpen = true;
    state.atPackageRoot = true;
    state.loading = !state.engineReady;
    const focusWorkspaceOnEntry =
      !state.packageQueryReturnFocusPending;
    render();
    if (state.engineReady && focusWorkspaceOnEntry) {
      afterCurrentNavigationFrame(() =>
        focusWorkspace(document));
    }
    return;
  }
  if (leftPackageQueryForWorkspaceSuccessor) {
    packageQueryWorkspaceFocusNavigationSeq = navigationSeq;
  }
  if (!state.engineReady) {
    const pendingWorkspace = workspaceLocation.preflightCurrent();
    const pendingLocation = pendingWorkspace.visible;
    state.queryNotice = pendingLocation.workspaceNotice || "";
    state.queryNoticeRetryAction = null;
    state.credits = false;
    state.home =
      !pendingLocation.package
      && !pendingWorkspace.hasWorkspaceState
      && !pendingLocation.routeFailure;
    state.workspaceSubjectOpen = false;
    state.atPackageRoot = false;
    state.loading = !state.home;
    if (state.home) clearNavigationError();
    render();
    return;
  }
  const loc = parseLocation();
  if (loc.routeFailure) {
    failWorkspaceRoute(loc.routeFailure.message);
    return;
  }
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  const canonicalSnapshot = loc.hasWorkspaceState
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  state.queryNotice = loc.workspaceNotice || "";
  state.queryNoticeRetryAction = null;
  if (loc.hasWorkspaceState && !loc.shareState) {
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      loc.workspaceNotice
        || "The shared workspace packet could not be restored.",
      canonicalSnapshot,
      null);
    return;
  }
  const bareHome = !loc.package && !(loc.tabs && loc.tabs.length);
  if (bareHome) {
    // Navigated back to the bare root — show the intro/home page (engine stays warm).
    clearNavigationError();
    state.credits = false;
    state.home = true;
    spotlight.reset();
    render();
    return;
  }
  state.credits = false;
  resetLocationFilters();
  const deep = loc;
  if (!state.package) {
    observeAsync(
      restoreWorkspaceFromLocation(
        loc,
        deep,
        navigationSeq,
        canonicalSnapshot),
      "Restoring workspace history");
    return;
  }
  if (loc.tabs?.length && !workspaceCoordinatesMatch(state.packages, loc.tabs)) {
    observeAsync(
      restoreWorkspaceFromLocation(
        loc,
        deep,
        navigationSeq,
        canonicalSnapshot),
      "Restoring workspace history");
    return;
  }
  if (loc.tabs?.length) {
    const target = state.packages.find(candidate =>
      packageCoordinateMatchesLocation(candidate, loc));
    if (!target) {
      observeAsync(
        restoreWorkspaceFromLocation(
          loc,
          deep,
          navigationSeq,
          canonicalSnapshot),
        "Restoring workspace history");
      return;
    }
    activatePackage(target, { resetAccessibility: true });
  }
  state.home = false;
  applyLocationView(loc);
  const samePackage = packageCoordinateMatchesLocation(state.package, loc);
  if (samePackage || !loc.package) {
    if (isRuntimePackId(state.package.id)) {
      // Back/forward within the platform: re-scope to the target library (or the
      // aggregate) before restoring selection, since scope is part of the view.
      observeAsync(
        restorePlatformScopeThenDeepLink(
          loc,
          navigationSeq,
          canonicalSnapshot),
        "Restoring platform history");
    } else {
      const libraryFailure = applyLoadedPackageLibraryScope(
        state.package,
        loc.library);
      const viewFailure = loc.shareState
        ? canonicalViewRestorationFailure(state.package, loc, loc.lens)
        : null;
      const restorationFailure = libraryFailure ?? viewFailure;
      if (loc.shareState && restorationFailure) {
        failCanonicalWorkspaceRestore(
          loc,
          deep,
          restorationFailure,
          canonicalSnapshot);
        return;
      }
      commitWorkspaceShareBasis(loc.shareState);
      applyDeepLink(loc);
      render();
      observeAsync(loadSelectionData(), "Loading selection data");
    }
  } else if (isRuntimePackId(loc.package)) {
    // The runtime pack has no nupkg; rebuild it from its TFM instead of 404-ing
    // on a NuGet fetch when back/forward lands on a platform state.
    observeAsync(
      restoreRuntimePackFromHistory(
        loc,
        deep,
        navigationSeq,
        canonicalSnapshot),
      "Restoring runtime-pack history");
  } else {
    observeAsync(
      loadPackage(loc.package, loc.version || "latest", loc.framework || "", {
        deepLink: deep,
        navigationSeq,
        queryNotice: loc.workspaceNotice
      }),
      "Restoring package history");
  }
});

// Re-scope the active runtime pack to the platform library named in a share/history packet
// (lazily loading that assembly if needed via the same drill-in path as clicking it), or
// clear the scope for the aggregate platform, then restore the deep-linked selection.
async function restorePlatformScopeThenDeepLink(
  loc: ParsedLocation,
  navigationSeq: number,
  canonicalSnapshot: CanonicalWorkspaceRestoreSnapshot | null = null,
) {
  const scoped = await applyPlatformLibraryScope(
    loc.library,
    loc.libraryPack,
    navigationSeq,
    () => restorePlatformScopeThenDeepLink(
      loc,
      navigationSequence.current(),
      canonicalSnapshot));
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (!scoped) {
    if (loc.shareState) {
      failCanonicalWorkspaceRestore(
        loc,
        loc,
        `The shared Platform library '${loc.library}' could not be restored.`,
        canonicalSnapshot);
    }
    return;
  }
  const pkg = state.package;
  const viewFailure = pkg && loc.shareState
    ? canonicalViewRestorationFailure(pkg, loc, loc.lens)
    : null;
  if (loc.shareState && viewFailure) {
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      viewFailure,
      canonicalSnapshot);
    return;
  }
  commitWorkspaceShareBasis(loc.shareState);
  applyLocationView(loc);
  applyDeepLink(loc);
  state.loading = false;
  render();
  await loadSelectionData();
}

// Load and scope to a single platform library key (or clear the scope when null). Reuses
// openPlatformLibrary so a restored view matches clicking the library in the selector.
async function applyPlatformLibraryScope(
  requestedLibraryKey: string | null,
  libraryPack: PlatformPack | null = null,
  navigationSeq: number | null = null,
  retryAction: RetryAction = null,
) {
  if (navigationSeq != null && !navigationSequence.isCurrent(navigationSeq))
    return undefined;
  const key = (requestedLibraryKey ?? "").replace(/\.dll$/i, "");
  if (!key) { state.libraryScope = null; return true; }
  // The pack (CoreCLR vs ASP.NET Core) is resolved from the static index roster; ensure it
  // is loaded on a cold shared/refreshed link so the right assembly is fetched.
  if (!state.platformIndex) {
    try { state.platformIndex = await loadPlatformIndex(); } catch { /* product acquisition can resolve an unknown pack */ }
  }
  if (navigationSeq != null && !navigationSequence.isCurrent(navigationSeq))
    return undefined;
  return Boolean(await openPlatformLibrary(
    key,
    platformPackForAssembly(key, libraryPack) ?? "",
    {
      ...(navigationSeq === null ? {} : { navigationSeq }),
      retryAction,
      scopeOnly: true,
    }));
}

function applyLoadedPackageLibraryScope(
  pkg: AppPackage,
  requestedLibraryKey: string | null,
): string | null {
  const requested = (requestedLibraryKey ?? "").replace(/\.dll$/i, "");
  if (!requested) {
    state.libraryScope = null;
    return null;
  }
  const matchingType = pkg.types.find(type =>
    libraryKey(type).toLowerCase() === requested.toLowerCase());
  if (!matchingType) {
    return `The shared library '${requestedLibraryKey}' is not available in ${pkg.id}.`;
  }
  state.libraryScope = new Set([libraryKey(matchingType)]);
  return null;
}

// History (back/forward) landed on a .NET Platform state. Its resident pseudo-package
// has no nupkg, so restore it via loadRuntimePack (usually already resident, so instant),
// re-scope to the captured library, and re-apply the deep link, mirroring
// restoreInitialWorkspace's runtime-pack path.
async function restoreRuntimePackFromHistory(
  loc: ParsedLocation,
  deep: DeepLink,
  navigationSeq: number,
  canonicalSnapshot: CanonicalWorkspaceRestoreSnapshot | null = null,
) {
  const runtimeResult = await loadRuntimePack(
    loc.framework || "",
    () => navigationSequence.isCurrent(navigationSeq),
    loc.version || "");
  const pack = runtimeResult.packageModel;
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (pack) {
    activatePackage(pack, { resetAccessibility: true });
    // Always resolve scope from the history entry's own library field -- even when it's
    // empty (the aggregate view) -- so a stale scope from whatever the session was
    // previously viewing doesn't survive the restore. Mirrors restorePlatformScopeThenDeepLink.
    const scoped = await applyPlatformLibraryScope(
      loc.library,
      loc.libraryPack,
      navigationSeq,
      () => restoreRuntimePackFromHistory(
        loc,
        deep,
        navigationSequence.current(),
        canonicalSnapshot));
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (!scoped) {
      if (loc.shareState) {
        failCanonicalWorkspaceRestore(
          loc,
          deep,
          `The shared Platform library '${loc.library}' could not be restored.`,
          canonicalSnapshot);
      }
      return;
    }
    const viewFailure = loc.shareState
      ? canonicalViewRestorationFailure(pack, deep, loc.lens)
      : null;
    if (loc.shareState && viewFailure) {
      failCanonicalWorkspaceRestore(
        loc,
        deep,
        viewFailure,
        canonicalSnapshot);
      return;
    }
    commitWorkspaceShareBasis(loc.shareState);
    applyLocationView(loc);
    applyDeepLink(deep);
    state.loading = false;
  } else if (loc.shareState) {
    failCanonicalWorkspaceRestore(
      loc,
      deep,
      `The shared Platform could not be restored: ${runtimeResult.failureMessage
        || state.runtimePackError
        || "runtime pack acquisition failed."}`,
      canonicalSnapshot);
    return;
  } else {
    appendQueryNotice(
      `Workspace restore was incomplete: ${loc.package}: ${runtimeResult.failureMessage
        || state.runtimePackError
        || "runtime pack acquisition failed."}`);
  }
  render();
  await loadSelectionData();
}

observeAsync(bootstrap(), "Starting dotnet-inspect");

// Warm the static platform-assembly/facade index in the background. It is a
// hint layer (facade badges, per-library overview roster, library-scope
// selector) built on top of the app; prefetching keeps it ready without
// blocking boot. Cached on state once resolved; exposed for verification.
window.__platformIndex = loadPlatformIndex();
observeAsync(
  window.__platformIndex.then(index => {
    if (index) state.platformIndex = index;
    return undefined;
  }),
  "Loading the platform index");
