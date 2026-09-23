import {
  accessibilityFilterIncludingType,
  activeSourceOperationKind,
  assemblyDescriptorForType,
  assertNever,
  callGraphAssemblyIdentityMatches,
  callGraphDiagnosticsMessage,
  callGraphTargetPackageCoordinate,
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
  libraryLenses,
  memberSectionDefinitions,
  memberSectionIdsFor,
  packageCoordinateLabel,
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
  workspacePackageRemovalKey,
  runtimeGraphTargetAssemblyIsResident,
  runtimeGraphTargetNavigationDisposition,
  runtimePackForFramework,
  scopedRequestState,
  searchableMemberGroups,
  sourceReloadKind,
  spotlightCandidateKey,
  spotlightCandidateSignature,
  typeLensesFor,
  type DependencyGroupData,
  type GraphMemberTarget,
  type GraphMemberShareIdentity,
  type PackageIdentity,
  type PlatformPack,
  type WorkspaceCoordinate,
  uniqueWorkspaceTypeByQueryId,
  workspaceCoordinatesMatch
} from "./data.ts";
import type { EngineClient } from "./engine-client.ts";
import { retainDiagnosticDetail } from "./failure-detail.ts";
import {
  createPublishedRuntimeBenchmarkBridge,
  installPublishedRuntimeBenchmarkBridge,
} from "./published-runtime-benchmark-bridge.ts";
import type {
  LibraryLens,
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
  buildPackageRootStateUrl,
  callGraphCaptureTopology,
  createAsyncWorkspaceLocationPersistence,
  createNavigationHistory,
  createNavigationSequence,
  parseWorkspaceLocationAsync,
  recoverWorkspaceRouteFailure,
  retainedMissingPlatformTarget,
  resolvedPlatformTargetVersion,
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
  createWorkspaceFeedActivationCoordinator,
  type RetainedWorkspaceModels,
  type WorkspaceFeedActivationCoordinator,
  type WorkspaceFeedRollbackTransfer,
} from "./workspace-feed-activation.ts";
import {
  createAppMemberSurface,
  createAppTypeSurface,
  createPackageAcquisition,
  createNuGetPackageModel,
  createRuntimePackageModel,
  createUploadedLibraryModel,
  createWorkspaceOccurrencePackageModel,
  graphOnlyImplementationBody,
  retainGraphOnlyImplementationBody,
  resolvePackageLibrary,
  resolveReplacementPackageLibrary,
  runtimeAssemblyIsResident,
  type AppMemberSurface,
  type AppPackage,
  type AppTypeSurface,
  type InspectedMemberSurface,
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
  renderPackageNav,
  type PackageViewBindingActions,
} from "./package-view.ts";
import {
  alphabetizeLibrarySubjects,
  bindLibrarySubjectNav,
  librarySubjectDisplayLabels,
  preferredLibrarySubjectId,
  renderLibrarySubjectNav,
} from "./library-subject-nav.ts";
import {
  bindLibraryOpen,
  bindLibraryOpenDocument,
  renderLibraryOpenDialog,
  type LibraryOpenActions,
  type LibraryOpenInput,
} from "./library-open.ts";
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
  trapModalTab,
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
  prepareProductHomeDemoSource,
  productHomeDemosViewHtml,
  setProductHomeDemoCatalog,
  type PreparedProductHomeDemoSource,
  type ProductHomeDemoId,
} from "./product-home-demos.ts";
import { createSavedWorkspaces, type SavedWorkspace } from "./saved-workspaces.ts";
import { bindSavedWorkspaces, restoreSavedWorkspaceFocus } from "./saved-workspaces-view.ts";
import {
  createNavigationDescriptorPresentation,
  withNavigationPackageDetailFailure,
  withNavigationPlatformDetailFailure,
  type NavigationDescriptorPresentation,
} from "./navigation-descriptor-presentation.ts";
import {
  createNavigationLocationIntentArbiter,
  type InstalledLocationAssociation,
  type LocationIntentDeclaration,
} from "./navigation-location-intent.ts";
import {
  createRetainedWorkspaceActivationController,
  type RetainedWorkspaceActivationController,
} from "./retained-workspace-activation.ts";
import {
  createSourceInspectionCoordinator,
  graphSourceAutoLoadRequest,
  graphSourceIsOpen,
  graphSourceRequest,
  normalizeSourceResultSnapshot,
  sourceResultForSignature,
  sourceResultNeedsLoad,
  type GraphSourceRequest,
  type GraphSourceState,
  type SourceResultState,
  type TypeSourceView,
} from "./source-inspection.ts";
import { renderMemberContractSections } from "./member-overview.ts";
import {
  bindMemberFacts,
  renderMemberFacts,
} from "./member-facts.ts";
import { createOperationAuthorityPage } from "./operation-authority.ts";
import {
  createMetadataInspectionCoordinator,
  type AppExplorerState,
} from "./metadata-inspection.ts";
import {
  cancelFindingCensusRequest,
  createMemberDetailInspectionCoordinator,
  type MemberFacts,
} from "./member-detail-inspection.ts";
import {
  clearFindingSelection,
  selectAnnotatedSourceFact,
  selectFindingInstance,
  type MemberFindingInteraction,
} from "./finding-interaction.ts";
import {
  callGraphErrorForView,
  createCallGraphInspectionCoordinator,
  queryPlatformCallGraph,
  type InspectedCallGraph,
  type InspectedCallGraphTarget,
  type PlatformStackEntry,
} from "./call-graph-inspection.ts";
import {
  createDocumentInspectionCoordinator,
  documentViewerIsOpen,
  normalizeDocumentViewerSnapshot,
  type DocumentViewerState,
} from "./document-inspection.ts";
import {
  renderLibraryOverviewContent,
  renderOverviewSurface,
  renderPackageOverviewContent,
} from "./overview-surface.ts";
import { renderPackageInfo } from "./package-info.ts";
import { renderLibraryReferencesSurface } from "./library-references.ts";
import { renderLibraryIntegrationsSurface } from "./library-integrations.ts";
import {
  bindIntegrationTabs,
  isIntegrationMode,
  restoreIntegrationTabFocus,
  type IntegrationMode,
} from "./integration-inspector.ts";
import { renderLibraryAnalysisSurface } from "./library-analysis.ts";
import { renderLibraryMetricsSurface } from "./library-metrics.ts";
import {
  captureMemberFocus,
  createMemberFocusRestorer,
  focusPlatformGraphError,
  type MemberFocusSnapshot,
} from "./member-focus.ts";
import {
  buildAnnotatedRelationshipGraphMermaid,
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid,
  resolveMermaidCssVariables,
  styleCallGraphMermaid,
} from "./graph-mermaid.ts";
import {
  bindGraphBack,
  bindGraphPanZoom,
  graphControlsHtml,
  type GraphNodeBinding,
  type GraphBackBindingActions,
} from "./graph-interactions.ts";
import {
  callGraphLegendHtml,
  dependencyGraphLegendHtml,
} from "./graph-legends.ts";
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
  prismCSharp,
} from "./prism-csharp.ts";
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
  selectRelationshipPresentation,
  showRelationshipOccurrences,
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
  renderScopeBar as renderScopeBarPure,
  restoreScopeBarFocus,
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
  activateRetainedWorkspace as activateRetainedWorkspaceState,
  createRetainedWorkspaceCollection,
  deleteRetainedWorkspace as deleteRetainedWorkspaceState,
  detachActiveRetainedWorkspace,
  MAX_RETAINED_WORKSPACES,
  publishRetainedWorkspace,
  type RetainedWorkspaceCollection,
} from "./retained-workspaces.ts";
import {
  bindDocViewer,
  renderDocViewer as renderDocViewerPure,
  renderPackageDocuments,
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
  createMemberSourcePartSelector,
  renderGraphMemberPending,
  renderMemberNav,
  memberSourceText,
  renderSourcePageActions,
  renderSourceResult,
  renderTypeMetadata,
  renderTypeNav,
  renderTypeSource,
  TYPE_RELATIONSHIPS_GRAPH_SUMMARY,
  type MemberNavEntry,
  typeMetadataSignature,
  typeSourceSignature,
  typeCodeViewText,
} from "./type-panel.ts";
import {
  createPackageControls,
  findOpenPackageForQuery,
  parsePackageQuery,
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
  metadataRootSelection,
  renderMetadataExplorer as renderMetadataExplorerHtml,
  renderPackageMetadata as renderPackageMetadataHtml,
  sameFocus,
  selectMetadataImage,
  type ExplorerFocus,
  type MetadataRootSelection,
  type PackageMetadata,
} from "./metadata-viewer.ts";
import {
  bindSettingsPanel,
  reconcileStyleTaste,
  renderSettingsView,
  type StyleOption,
  type StyleTier,
} from "./settings-panel.ts";
import {
  bindProductNavigation,
  renderBrand,
  type ProductAction,
  type ProductDestination,
} from "./brand.ts";
import {
  DEFAULT_PLATFORM_FRAMEWORK, isExactPlatformPruningFramework,
  loadPlatformIndex, parsePlatformCatalogTarget,
  platformCatalogFramework, requirePlatformPackageSupplies,
  type PlatformAssemblyRow, type PlatformIndex, type PlatformCatalogTarget,
} from "./platform-index.ts";
import {
  bindPlatformSubject, renderPlatformSubject, platformInventory, platformLibraryKey,
  platformLibraryRole, platformTargetKey, parsePlatformVersions, requireMatchingPlatformTarget,
  platformSupportsRuntimeAcquisition,
  platformAssemblyRequest, platformGraphLibraryForTarget,
  platformLibraryMatchesDescriptor, rankPlatformLibraryMatches,
  type PlatformNavigationState, type PlatformSubjectStatus,
} from "./platform-subject.ts";
import {
  createSpotlight,
  type RemovableSpotlightResult,
  type SpotlightPackageResult,
  type SpotlightPackageHit,
  type SpotlightResult,
  type SpotlightScope,
} from "./spotlight.ts";
import {
  createSpotlightPackageSearch,
  normalizeSpotlightPackageSearchSnapshot,
  spotlightPackageSearchError,
  spotlightPackageSearchIsLoading,
  visibleSpotlightPackageHits,
  type SpotlightPackageSearchResultState,
} from "./spotlight-package-search.ts";
import { createPackageRemoval } from "./package-removal.ts";
import {
  createCatalogRequests,
  type CatalogPackage,
} from "./catalog-requests.ts";
import {
  bindPackageComparisonTargets,
  createPackageComparisonTargets,
  isCompareMode,
  renderPackageComparisonTargets,
  resolveEffectiveDiffTarget,
  type CompareMode,
} from "./package-comparison-targets.ts";
import {
  bindLibraryApiDiffRows,
  createLibraryApiDiffCoordinator,
  renderLibraryApiDiff,
  type LibraryApiDiffSelection,
  type LibraryApiDiffState,
  type LibraryApiDiffSubject,
} from "./library-api-diff.ts";
import {
  bindCompareFrame,
  restoreCompareTabFocus,
  type CompareSubjectKind,
} from "./compare-surface.ts";
import {
  bindCompareCloneRows,
  createCompareCloneCoordinator,
  renderCompareClone,
  type CompareCloneJoin,
  type CompareCloneJoinedMethod,
  type CompareCloneReconcileTarget,
  type CompareCloneSelection,
  type CompareCloneState,
} from "./compare-clone.ts";
import { dataBarHtml, fmtBytes } from "./data-bar.ts";
import {
  DIAGNOSTICS_PATH,
  diagnosticsHistoryState,
  isDiagnosticsHistoryEntry,
  isDiagnosticsPath,
} from "./diagnostics-route.ts";
import {
  bindDiagnosticsView,
  diagnosticsViewHtml,
  type DiagnosticsPackageCacheState,
  type DiagnosticsRuntimeState,
  type DiagnosticsBuildState,
  type RuntimeStartupDiagnostics,
} from "./diagnostics-view.ts";
import {
  bindCreditsPanel,
  isCreditsPath,
  renderCreditsPage,
} from "./credits-panel.ts";
import {
  createPackageQueryController,
  createQueryRequest,
  initialQueryState,
  shouldExecuteQuery,
  togglePreset,
  replaceTerm,
  withTerm,
  withoutTerm,
  withEditorDraft,
  withSourceSelection,
  withScopeQuery,
  type PackageQueryState,
  type QueryPreset,
  type QueryRequest,
  type QuerySourceSelection,
  type QueryTermDescriptor,
} from "./package-query.ts";
import {
  createPackageQueryLiveAnnouncer,
  createPackageQueryAnnouncementTracker,
} from "./package-query-announcements.ts";
import {
  createBrowserPackageQueryDataSource,
  packageQueryCatalog,
  type BrowserPackageQueryInspection,
} from "./package-query-source.ts";
import {
  bindPackageQueryView,
  capturePackageQueryFocus,
  patchPackageQueryStream,
  renderPackageQueryView,
  restorePackageQueryFocus,
  type PackageQueryBindingActions,
} from "./package-query-view.ts";
import {
  createPackageQueryRenderScheduler,
  packageQueryEditorCompositionActive,
} from "./package-query-editor-lifecycle.ts";
import {
  capturePackageQueryViewport,
  restorePackageQueryViewport,
  type PackageQueryViewportSnapshot,
} from "./package-query-window.ts";
import {
  createPackageChangesController,
  createPackageChangesRequest,
  initialPackageChangesState,
  type PackageChangesState,
} from "./package-changes.ts";
import {
  createBrowserPackageChangesDataSource,
  packageChangesPackageSets,
} from "./package-changes-source.ts";
import {
  bindPackageChangesView,
  patchPackageChangesStream,
  renderPackageChangesView,
} from "./package-changes-view.ts";
import {
  capturePackageChangesViewport,
  restorePackageChangesViewport,
  type PackageChangesViewportSnapshot,
} from "./package-changes-window.ts";
import {
  isPackageActivityPath,
  isPackageActivityPredecessor,
  packageActivityHistoryState,
  readPackageActivityHistory,
  PACKAGE_ACTIVITY_PATH,
  type PackageActivityReturnFocus,
} from "./package-activity-route.ts";
import {
  historyEntryId,
  isPackageQueryPath,
  isPackageQueryPredecessor,
  packageQueryHistoryState,
  readPackageQueryHistory,
  resolvePackageQueryWorkspaceSuccessor,
  validPackageQuerySearchText,
  withHistoryEntryId,
  type PackageQueryReturnFocus,
} from "./package-query-route.ts";
import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import type {
  BrowserPackageChangesPackageSetDescriptor,
  BrowserPackageCacheStats,
  BrowserPackageDependencies,
  BrowserPackageDependencyGroup,
  BrowserPackagePruningResult,
  BrowserPackageSurface,
  BrowserExactLibraryApiInspection,
  BrowserTypeCandidate,
  BrowserWorkspacePackageOccurrenceActivation,
  BrowserWorkspacePackageOccurrenceView,
} from "./facades/inspect-web-package.d.ts";
export type {
  BrowserUploadedLibraryInspection,
} from "./facades/inspect-web-library.d.ts";
import type {
  BrowserMemberDeclaration,
  BrowserTypeMetadata,
} from "./facades/inspect-web-metadata.d.ts";
import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "./facades/inspect-web-analysis.d.ts";
import type {
  BrowserMemberSource,
  BrowserTypeCodeView,
} from "./facades/inspect-web-source.d.ts";
import type {
  BrowserHomeDemoRunActivation,
  BrowserHomeDemoRunResult,
  BrowserPackageSurface as CatalogPackageSurface,
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspacePackage,
  BrowserRetainedWorkspacePackageInventory,
  BrowserRetainedWorkspacePlatform,
  BrowserRetainedWorkspacePlatformInventory,
  BrowserRetainedWorkspacePosting,
  BrowserWorkspaceShareState,
} from "./facades/inspect-web-catalog.d.ts";

type ProductionEngineWorkerModule =
  typeof import("./engine-worker-client.ts");

let startEngine: (origin: string) => Promise<void>;
let engineClient: EngineClient;
let cancelPackageActivity: EngineClient["package"]["cancelPackageActivity"];
let cancelPackageQuery: EngineClient["package"]["cancelPackageQuery"];
let inspectPackageDocument: EngineClient["package"]["getPackageDocument"];
let inspectLoadRuntimePack: EngineClient["package"]["loadRuntimePack"];
let inspectLoadRuntimePackAssembly:
  EngineClient["package"]["loadRuntimePackAssembly"];
let inspectPlatformVersions: EngineClient["package"]["getPlatformVersions"];
let inspectPlatformCatalog: EngineClient["package"]["getPlatformCatalog"];
let inspectPrefetchPlatformPacks:
  EngineClient["package"]["prefetchPlatformPacks"];
let inspectPackageCacheStats: EngineClient["package"]["packageCacheStats"];
let inspectMemberDocumentation:
  EngineClient["package"]["queryMemberDocumentation"];
let inspectPlatformMemberDocumentation:
  EngineClient["package"]["queryPlatformMemberDocumentation"];
let inspectPackage: EngineClient["package"]["queryPackage"];
let inspectPackageRoot: EngineClient["package"]["queryPackageRoot"];
let inspectOpenUploadedLibrary:
  EngineClient["library"]["openUploadedLibrary"];
let inspectLibraryApi: EngineClient["package"]["queryLibraryApi"];
let inspectPackageDependencies:
  EngineClient["package"]["queryPackageDependencies"];
let inspectPackagePruning:
  EngineClient["package"]["queryPackagePruning"];
let inspectPackageVersions: EngineClient["package"]["queryPackageVersions"];
let resolveDependencyVersion:
  EngineClient["package"]["resolvePackageDependencyVersion"];
let inspectRequestPackageQueryMatches:
  EngineClient["package"]["requestPackageQueryMatches"];
let inspectRunPackageQuery: EngineClient["package"]["runPackageQuery"];
let inspectRunPackageActivity: EngineClient["package"]["runPackageActivity"];
let inspectSearchTypes: EngineClient["package"]["searchTypes"];
let inspectQueryWorkspacePackageOccurrences:
  EngineClient["package"]["queryWorkspacePackageOccurrences"];
let inspectActivateWorkspacePackageOccurrence:
  EngineClient["package"]["activateWorkspacePackageOccurrence"];
let inspectClearWorkspacePackageOccurrences:
  EngineClient["package"]["clearWorkspacePackageOccurrences"];
let inspectGraphMemberSurface:
  EngineClient["metadata"]["queryGraphMemberSurface"];
let inspectMemberDeclaration:
  EngineClient["metadata"]["queryMemberDeclaration"];
let inspectPlatformMemberDeclaration:
  EngineClient["metadata"]["queryPlatformMemberDeclaration"];
let inspectPackageHeapEntries:
  EngineClient["metadata"]["queryPackageHeapEntries"];
let inspectPackageMetadata:
  EngineClient["metadata"]["queryPackageMetadata"];
let inspectPackageMetadataTable:
  EngineClient["metadata"]["queryPackageMetadataTable"];
let inspectPlatformHeapEntries:
  EngineClient["metadata"]["queryPlatformHeapEntries"];
let inspectPlatformMetadata:
  EngineClient["metadata"]["queryPlatformMetadata"];
let inspectPlatformMetadataTable:
  EngineClient["metadata"]["queryPlatformMetadataTable"];
let inspectTypeProjection: EngineClient["metadata"]["queryTypeProjection"];
let cancelLibraryApiDiff:
  EngineClient["metadata"]["cancelLibraryApiDiff"];
let inspectLibraryApiDiff:
  EngineClient["metadata"]["queryLibraryApiDiff"];
let inspectCloneCandidates:
  EngineClient["analysis"]["queryCloneCandidates"];
let inspectMemberFacts: EngineClient["analysis"]["queryMemberFacts"];
let inspectPackageIntegrations:
  EngineClient["analysis"]["queryPackageIntegrations"];
let inspectPackageOpportunities:
  EngineClient["analysis"]["queryPackageOpportunities"];
let inspectPackagePerformance:
  EngineClient["analysis"]["queryPackagePerformance"];
let inspectPackageLibraryMetrics:
  EngineClient["analysis"]["queryPackageLibraryMetrics"];
let inspectPlatformLibraryMetrics:
  EngineClient["analysis"]["queryPlatformLibraryMetrics"];
let inspectPlatformIntegrations:
  EngineClient["analysis"]["queryPlatformIntegrations"];
let inspectPlatformOpportunities:
  EngineClient["analysis"]["queryPlatformOpportunities"];
let inspectPlatformPerformance:
  EngineClient["analysis"]["queryPlatformPerformance"];
let cancelSourceInspection: EngineClient["source"]["cancelSourceQuery"];
let cancelTypeSourceInspection:
  EngineClient["source"]["cancelTypeSourceQuery"];
let inspectMemberFindingCensus:
  EngineClient["source"]["queryMemberFindingCensus"];
let inspectMemberSource: EngineClient["source"]["queryMemberSource"];
let inspectTypeMemberSource:
  EngineClient["source"]["queryTypeMemberSource"];
let inspectTypeSource: EngineClient["source"]["queryTypeSource"];
let inspectExpandPlatformCallGraph:
  EngineClient["callGraph"]["expandPlatformCallGraph"];
let inspectMemberCallGraph:
  EngineClient["callGraph"]["queryMemberCallGraph"];
let inspectDecodeWorkspaceShareState:
  EngineClient["catalog"]["decodeWorkspaceShareState"];
let inspectEncodeWorkspaceShareState:
  EngineClient["catalog"]["encodeWorkspaceShareState"];
let inspectRunHomeDemo: EngineClient["catalog"]["runHomeDemo"];
let productHomeDemoCatalogError = "";

// The Worker client stays off the first-paint path. It is imported after the
// home view paints, then starts exactly one epoch when bootstrap supplies the
// host origin.
async function loadEngineModule() {
  const workerModule: ProductionEngineWorkerModule =
    await import("./engine-worker-client.ts");
  let worker:
    ReturnType<ProductionEngineWorkerModule["createProductionEngineWorkerClient"]>
    | undefined;
  startEngine = async origin => {
    if (worker !== undefined) {
      await worker.ready;
      return;
    }
    worker = workerModule.createProductionEngineWorkerClient(origin, {
      callbacks: {
        failure: failure => {
          showEngineFailure(new Error(
            `Engine Worker failed (${failure.kind}): ${failure.diagnostic}`));
          return undefined;
        },
        diagnostic: diagnostic => {
          console.error("Engine Worker diagnostic.", diagnostic);
          return undefined;
        },
        realmReleased: () => undefined,
      },
      operationDiagnostic: diagnostic => {
        console.error("Engine operation failed.", diagnostic);
        return undefined;
      },
    });
    engineClient = worker.client;
    retainedWorkspaceActivation =
      createRetainedWorkspaceActivationController(engineClient.catalog, {
        post: postRetainedWorkspace,
        clear: clearRetainedWorkspacePosting,
        predecessorSettled: (_observation, result) => {
          if (result.status === "failed") {
            appendQueryNotice(
              result.settlement?.failure
                ?? "The previous Workspace could not be settled.",
              null,
            );
            render({ synchronizeUrl: false });
          }
        },
        predecessorObservationFailed: (_observation, error) => {
          appendQueryNotice(
            `The previous Workspace settlement could not be observed: ${
              errorMessage(error)
            }`,
            null,
          );
          render({ synchronizeUrl: false });
        },
      });
    installPublishedRuntimeBenchmarkBridge(
      window,
      window.location.search,
      createPublishedRuntimeBenchmarkBridge(engineClient),
    );
    cancelPackageQuery = (...args) =>
      engineClient.package.cancelPackageQuery(...args);
    cancelPackageActivity = (...args) =>
      engineClient.package.cancelPackageActivity(...args);
    inspectRequestPackageQueryMatches = (...args) =>
      engineClient.package.requestPackageQueryMatches(...args);
    cancelTypeSourceInspection = (...args) =>
      engineClient.source.cancelTypeSourceQuery(...args);
    ({
      getPackageDocument: inspectPackageDocument,
      loadRuntimePack: inspectLoadRuntimePack,
      loadRuntimePackAssembly: inspectLoadRuntimePackAssembly,
      getPlatformVersions: inspectPlatformVersions,
      getPlatformCatalog: inspectPlatformCatalog,
      prefetchPlatformPacks: inspectPrefetchPlatformPacks,
      packageCacheStats: inspectPackageCacheStats,
      queryLibraryApi: inspectLibraryApi,
      queryMemberDocumentation: inspectMemberDocumentation,
      queryPlatformMemberDocumentation:
        inspectPlatformMemberDocumentation,
      queryPackage: inspectPackage,
      queryPackageRoot: inspectPackageRoot,
      queryPackageDependencies: inspectPackageDependencies,
      queryPackagePruning: inspectPackagePruning,
      queryPackageVersions: inspectPackageVersions,
      resolvePackageDependencyVersion: resolveDependencyVersion,
      runPackageActivity: inspectRunPackageActivity,
      runPackageQuery: inspectRunPackageQuery,
      searchTypes: inspectSearchTypes,
      queryWorkspacePackageOccurrences:
        inspectQueryWorkspacePackageOccurrences,
      activateWorkspacePackageOccurrence:
        inspectActivateWorkspacePackageOccurrence,
      clearWorkspacePackageOccurrences:
        inspectClearWorkspacePackageOccurrences,
    } = engineClient.package);
    ({
      openUploadedLibrary: inspectOpenUploadedLibrary,
    } = engineClient.library);
    ({
      cancelLibraryApiDiff,
      queryLibraryApiDiff: inspectLibraryApiDiff,
      queryGraphMemberSurface: inspectGraphMemberSurface,
      queryMemberDeclaration: inspectMemberDeclaration,
      queryPlatformMemberDeclaration: inspectPlatformMemberDeclaration,
      queryPackageHeapEntries: inspectPackageHeapEntries,
      queryPackageMetadata: inspectPackageMetadata,
      queryPackageMetadataTable: inspectPackageMetadataTable,
      queryPlatformHeapEntries: inspectPlatformHeapEntries,
      queryPlatformMetadata: inspectPlatformMetadata,
      queryPlatformMetadataTable: inspectPlatformMetadataTable,
      queryTypeProjection: inspectTypeProjection,
    } = engineClient.metadata);
    ({
      queryCloneCandidates: inspectCloneCandidates,
      queryMemberFacts: inspectMemberFacts,
      queryPackageIntegrations: inspectPackageIntegrations,
      queryPackageOpportunities: inspectPackageOpportunities,
      queryPackagePerformance: inspectPackagePerformance,
      queryPackageLibraryMetrics: inspectPackageLibraryMetrics,
      queryPlatformLibraryMetrics: inspectPlatformLibraryMetrics,
      queryPlatformIntegrations: inspectPlatformIntegrations,
      queryPlatformOpportunities: inspectPlatformOpportunities,
      queryPlatformPerformance: inspectPlatformPerformance,
    } = engineClient.analysis);
    ({
      cancelSourceQuery: cancelSourceInspection,
      queryMemberFindingCensus: inspectMemberFindingCensus,
      queryMemberSource: inspectMemberSource,
      queryTypeMemberSource: inspectTypeMemberSource,
      queryTypeSource: inspectTypeSource,
    } = engineClient.source);
    ({
      expandPlatformCallGraph: inspectExpandPlatformCallGraph,
      queryMemberCallGraph: inspectMemberCallGraph,
    } = engineClient.callGraph);
    ({
      decodeWorkspaceShareState: inspectDecodeWorkspaceShareState,
      encodeWorkspaceShareState: inspectEncodeWorkspaceShareState,
      runHomeDemo: inspectRunHomeDemo,
    } = engineClient.catalog);
    await worker.ready;
  };
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
  candidates: ReadonlyArray<BrowserTypeCandidate>;
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

let spotlightCache: SpotlightCache | null = null;
const HOME_BOT_ANIMATION_DURATION_MS = 5500;
const DEFAULT_REQUESTED_FRAMEWORK = "net10.0";
let homeBotAnimationStartedAt: number | null = null;
let homeReadyGlintPending = true;
let homeFocusRenderGeneration = 0;
let pendingHomeFocusTarget: HomeFocusTarget | null = null;
type LibraryOpenReturnTarget = "home" | "product-navigation" | "surface";
const initialState = {
  theme: localStorage.getItem("inspect-theme") === "light" ? "light" : "dark",
  memberFiltersExpanded: false,
  typeFiltersExpanded: false,
  packages: [],
  package: null,
  uploadedLibrary: null,
  home: false,
  credits: false,
  packageQueryOpen: false,
  packageActivityOpen: false,
  packageQueryPrefix: "",
  packageQueryNavigationError: "",
  packageQueryCatalogError: "",
  packageChangesCatalogError: "",
  libraryOpen: false,
  libraryOpenBusy: false,
  libraryOpenError: "",
  libraryOpenReturn: "surface" as LibraryOpenReturnTarget,
  packageQueryOpenedFromApp: false,
  packageActivityOpenedFromApp: false,
  packageQueryPredecessorEntryId: null,
  packageQueryReturnFocus: null,
  packageQueryReturnFocusPending: false,
  packageActivityPredecessorEntryId: null,
  packageActivityReturnFocus: null,
  packageActivityReturnFocusPending: false,
  packageQueryState: initialQueryState(),
  packageQueryInspection: null,
  packageQueryPresets: [],
  packageQueryTerms: [],
  packageChangesPackageSets: [],
  packageChangesState: initialPackageChangesState(),
  platformIndex: null,
  rootKind: "package" as "package" | "platform" | "library",
  platformSelection: null,
  frameworkLibraryPresentation: null,
  platformPresentedAsRoot: false,
  platformSlot: -1,
  platformCatalogStatus: { loading: false, error: "" },
  platformOpeningStatus: { loading: false, error: "" },
  queryNotice: "",
  queryNoticeRetryAction: null,
  requestedPackage: "System.Text.Json",
  requestedVersion: "10.0.0",
  requestedFramework: DEFAULT_REQUESTED_FRAMEWORK,
  workspaceShareBasis: null,
  workspaceFeedUrl: null,
  selectedTypeId: "",
  selectedMemberKey: "",
  memberBrowseTypeId: "",
  selectedOverloadIndex: null,
  memberSection: "overview" as const,
  memberKindFilter: "all",
  memberAccessibilityFilter: "all",
  memberTraitFilter: "",
  memberTextFilter: "",
  memberSource: { status: "idle" as const },
  memberAnnotated: null,
  memberAnnotatedLoading: false,
  memberAnnotatedError: "",
  annotatedDestinationError: "",
  memberAnnotatedKey: "",
  memberAnnotatedEmbedded: null,
  memberAnnotatedModal: null,
  memberFindingInteraction: null,
  memberFindingSelectionError: "",
  typeSource: { status: "idle" as const },
  typeSourceView: "source" as const,
  typeMetadata: null,
  typeMetadataLoading: false,
  typeMetadataError: "",
  typeMetadataKey: "",
  typeMetadataGeneration: 0,
  libraryApiInspections:
    new Map<string, BrowserExactLibraryApiInspection>(),
  libraryApiLoads: new Set<string>(),
  libraryApiErrors: new Map<string, string>(),
  packageDependencies: null,
  packageDependenciesLoading: false,
  packageDependenciesError: "",
  packageDependenciesKey: "",
  packagePruning: null,
  packagePruningLoading: false,
  packagePruningError: "",
  packagePruningKey: "",
  packagePruningFamily: "Microsoft.NETCore.App",
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
  packageLibraryMetrics: null,
  packageLibraryMetricsLoading: false,
  packageLibraryMetricsError: "",
  packageLibraryMetricsKey: "",
  packageMetadata: null,
  packageMetadataLoading: false,
  packageMetadataError: "",
  packageMetadataKey: "",
  packageMetadataRoot: "cli" as MetadataRootSelection,
  explorer: null,
  memberCallGraph: null,
  memberCallGraphLoading: false,
  memberCallGraphError: "",
  graphMemberNavigationError: "",
  memberCallGraphKey: "",
  callGraphTraversalFramework: "net12.0",
  memberCallGraphExpanding: false,
  memberCallGraphSeq: 0,
  graphMemberNavigationSeq: 0,
  graphMemberNavigationTitle: "",
  pendingGraphMemberDeepLink: null,
  platformStack: [],
  platformDemoContextId: null,
  platformDrillLoading: false,
  platformDrillError: "",
  memberFacts: null,
  memberFactsLoading: false,
  memberFactsError: "",
  memberFactsKey: "",
  memberDocumentationLoading: false,
  memberDocumentationError: "",
  memberDocumentationKey: "",
  memberDeclaration: null as BrowserMemberDeclaration | null,
  memberDeclarationLoading: false,
  memberDeclarationError: "",
  memberDeclarationKey: "",
  lens: "api" as const,
  packageLens: "overview" as const,
  libraryLens: "overview" as const,
  libraryApiDiff: { status: "idle" as const },
  compareClone: { status: "idle" as const } as CompareCloneState,
  compareCloneSelectedRank: null as number | null,
  integrationMode: "integrations" as IntegrationMode,
  workspaceOccurrences: null,
  workspaceOccurrenceSignature: "",
  workspaceOccurrenceLoading: false,
  workspaceOccurrenceError: "",
  workspaceSubjectOpen: false,
  atPackageRoot: false,
  atLibraryRoot: false,
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
  spotlightPackageSearch: { status: "idle" as const },
  runtimePackLoading: false,
  runtimePackError: "",
  selectedBodyTarget: null,
  graphSource: { status: "closed" as const },
  docViewer: { status: "closed" as const },
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
  engineRuntimeReady: false,
  engineStartupFailed: false,
  engineStatus: "Loading browser WebAssembly…",
  error: "",
  errorTitle: "",
  errorDetail: "",
  retryAction: null,
  diag: null,
  buildIdentity: null,
  buildIdentityStatus: "loading" as "loading" | "ready" | "failed",
  buildIdentityError: "",
  packageCacheStats: null,
  packageCacheStatsStatus: "idle" as
    "idle" | "loading" | "ready" | "failed",
  packageCacheStatsError: "",
  diagnosticsCapturedAtUtc: null,
};

const memberSourcePartSelector = createMemberSourcePartSelector();

interface StateOverrides {
  packages: AppPackage[];
  package: AppPackage | null;
  uploadedLibrary: AppPackage | null;
  workspaceOccurrences: BrowserWorkspacePackageOccurrenceView | null;
  workspaceShareBasis: BrowserWorkspaceShareState | null;
  workspaceFeedUrl: string | null;
  platformIndex: PlatformIndex | null;
  platformSelection: PlatformNavigationState | null;
  frameworkLibraryPresentation: {
    tfm: string;
    version: string;
    libraryId: string;
  } | null;
  queryNoticeRetryAction: RetryAction;
  selectedOverloadIndex: number | null;
  memberSource: SourceResultState<BrowserMemberSource>;
  memberAnnotated: AnnotatedSourceResult | null;
  memberAnnotatedEmbedded: AnnotatedSourceSession | null;
  memberAnnotatedModal: AnnotatedSourceSession | null;
  memberFindingInteraction: MemberFindingInteraction | null;
  typeSource: SourceResultState<BrowserTypeCodeView>;
  typeSourceView: TypeSourceView;
  typeMetadata: BrowserTypeMetadata | null;
  libraryApiInspections:
    Map<string, BrowserExactLibraryApiInspection>;
  libraryApiLoads: Set<string>;
  libraryApiErrors: Map<string, string>;
  packageDependencies: BrowserPackageDependencies | null;
  packagePruning: BrowserPackagePruningResult | null;
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
  platformDemoContextId: string | null;
  memberFacts: MemberFacts | null;
  libraryScope: Set<string> | null;
  accessibilityFilter: Set<string>;
  spotlightPackageSearch: SpotlightPackageSearchResultState;
  spotlightFocus: "input" | "chips";
  spotlightScope: SpotlightScope;
  memberSection: MemberSection;
  lens: TypeLens;
  packageLens: PackageLens;
  libraryLens: LibraryLens;
  libraryApiDiff: LibraryApiDiffState;
  compareClone: CompareCloneState;
  compareCloneSelectedRank: number | null;
  platformRecent: PlatformRecent[];
  recentPackages: RecentPackage[];
  selectedBodyTarget: BodyTarget | null;
  graphSource: GraphSourceState;
  docViewer: DocumentViewerState;
  styleTiers: StyleTier[] | null;
  styleOptions: StyleOption[] | null;
  history: string[];
  retryAction: ErrorRetryAction;
  diag: RuntimeStartupDiagnostics | null;
  buildIdentity: BrowserBuildIdentity | null;
  buildIdentityStatus: "loading" | "ready" | "failed";
  packageCacheStats: BrowserPackageCacheStats | null;
  packageCacheStatsStatus: "idle" | "loading" | "ready" | "failed";
  diagnosticsCapturedAtUtc: string | null;
  packageQueryState: PackageQueryState;
  packageChangesCatalogError: string;
  packageChangesPackageSets: BrowserPackageChangesPackageSetDescriptor[];
  packageChangesState: PackageChangesState;
  packageQueryInspection: BrowserPackageQueryInspection | null;
  packageQueryPresets: QueryPreset[];
  packageQueryTerms: QueryTermDescriptor[];
  packageQueryPredecessorEntryId: string | null;
  packageQueryReturnFocus: PackageQueryReturnFocus | null;
  packageActivityPredecessorEntryId: string | null;
  packageActivityReturnFocus: PackageActivityReturnFocus | null;
}

type AppState = Omit<typeof initialState, keyof StateOverrides> & StateOverrides;

const state: AppState = initialState;
const scopeBarState = createScopeBarState();
let scopeBarBinding: ScopeBarBinding | null = null;
let workbenchShellBinding: WorkbenchShellBinding | null = null;
let disconnectLibraryOpen: (() => void) | null = null;
let libraryOpenSequence = 0;
const libraryEngineReadyWaiters: Array<{
  resolve: () => void;
  reject: (error: Error) => void;
}> = [];

function waitForLibraryEngineReady(): Promise<void> {
  if (state.engineReady) return Promise.resolve();
  if (state.engineStartupFailed) {
    return Promise.reject(new Error(
      "The browser inspection engine did not start."));
  }
  return new Promise<void>((resolve, reject) => {
    libraryEngineReadyWaiters.push({ resolve, reject });
  });
}

function settleLibraryEngineReadyWaiters(error?: Error) {
  for (const waiter of libraryEngineReadyWaiters.splice(0)) {
    if (error) waiter.reject(error);
    else waiter.resolve();
  }
}
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
let packageCacheStatsRequest = 0;
let diagnosticsHeadingFocusPending = false;
let diagnosticsDestinationFocusPending = false;
let diagnosticsDestinationFocusScheduled = false;
let diagnosticsDestinationFocusGeneration: number | null = null;
let platformLibraryRetry: RetryAction = null;
let platformCatalogRetry: RetryAction = null;

interface CanonicalWorkspaceRestoreSnapshot {
  state: AppState;
  hasWorkspace: boolean;
  url: string;
  navigation: NavigationHistorySnapshot<WorkspaceView>;
  activeRetainedWorkspacePosting: BrowserRetainedWorkspacePosting | null;
  retainedWorkspacePresentation: NavigationDescriptorPresentation | null;
  retainedWorkspaceInitialDetailAuthority: {
    realizationId: string;
    navigationSeq: number;
    presentationCurrent: boolean;
  } | null;
  installedRetainedLocation: InstalledLocationAssociation | null;
  failedWorkspaceUrlState: FailedWorkspaceUrlState | null;
  platformLibraryRetry: RetryAction;
  platformCatalogRetry: RetryAction;
}

let retainedWorkspaces =
  createRetainedWorkspaceCollection<CanonicalWorkspaceRestoreSnapshot>();
let retainedWorkspaceActivation: RetainedWorkspaceActivationController | null =
  null;
let activeRetainedWorkspacePosting: BrowserRetainedWorkspacePosting | null =
  null;
let retainedWorkspacePresentation: NavigationDescriptorPresentation | null =
  null;
let retainedWorkspaceInitialDetailAuthority: {
  realizationId: string;
  navigationSeq: number;
  presentationCurrent: boolean;
} | null = null;
const retainedWorkspacePostings =
  new Map<string, BrowserRetainedWorkspacePosting>();
const issuedManagedRetainedDefinitionIds = new Set<string>();
let failedManagedRetainedDefinitionId: string | null = null;
const retainedLocationIntents = createNavigationLocationIntentArbiter();
let installedRetainedLocation: InstalledLocationAssociation | null = null;
let activeWorkspaceUrl: string | null = null;
let workspaceFeedActivation:
  WorkspaceFeedActivationCoordinator<
    CanonicalWorkspaceRestoreSnapshot
  > | null = null;
const workspaceFeedRollbackTransfers = new WeakMap<
  CanonicalWorkspaceRestoreSnapshot,
  WorkspaceFeedRollbackTransfer<CanonicalWorkspaceRestoreSnapshot>
>();
let workspaceFeedRollbackTransfer:
  WorkspaceFeedRollbackTransfer<CanonicalWorkspaceRestoreSnapshot> | null =
    null;
let pendingWorkspaceConstruction: {
  navigationSeq: number;
  supersessionSnapshot: CanonicalWorkspaceRestoreSnapshot;
  retainedSnapshot: CanonicalWorkspaceRestoreSnapshot | null;
} | null = null;

function captureCanonicalWorkspaceUrl(): string {
  if (activeWorkspaceUrl !== null) return activeWorkspaceUrl;
  return state.package || state.platformSelection ? location.href : "/demos";
}

async function projectCurrentWorkspaceUrl(): Promise<string> {
  try {
    return state.package || state.platformSelection
      ? (await buildStateUrl()).toString()
      : "/demos";
  } catch {
    return location.href;
  }
}

function captureCanonicalWorkspaceRestoreSnapshot():
CanonicalWorkspaceRestoreSnapshot {
  sourceInspection.cancelCurrentRequest();
  libraryApiDiff.cancelCurrentRequest();
  cancelFindingCensusRequest(state);
  compareClone.cancelCurrentRequest();
  const packages = structuredClone(state.packages);
  const copies = new Map<AppPackage, AppPackage>();
  for (const [index, original] of state.packages.entries()) {
    const copy = packages[index];
    if (!copy) throw new Error("Workspace snapshot lost a Package.");
    copies.set(original, copy);
    catalogRequests.copyPackage(original, copy);
  }
  packageComparisonTargets.copyPackages(copies);
  const uploadedLibrary = state.uploadedLibrary
    ? structuredClone(state.uploadedLibrary)
    : null;
  const activeKey = state.package
    ? packageIdentityKey(state.package)
    : null;
  const snapshot = {
    state: {
      ...state,
      platformSelection: state.platformSelection ? { ...state.platformSelection } : null,
      frameworkLibraryPresentation: state.frameworkLibraryPresentation
        ? { ...state.frameworkLibraryPresentation }
        : null,
      platformCatalogStatus: { ...state.platformCatalogStatus },
      platformOpeningStatus: { ...state.platformOpeningStatus },
      packages,
      uploadedLibrary,
      package: state.rootKind === "library" && state.package
        ? uploadedLibrary
        : activeKey
          ? packages.find(pkg => packageIdentityKey(pkg) === activeKey) ?? null
          : null,
      workspaceDependencies: structuredClone(state.workspaceDependencies),
      workspaceDependencyErrors:
        structuredClone(state.workspaceDependencyErrors),
      workspaceDependencyLoads: new Set(state.workspaceDependencyLoads),
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
      spotlightPackageSearch:
        structuredClone(state.spotlightPackageSearch),
      history: [...state.history],
    },
    hasWorkspace:
      state.package !== null
      || state.platformSelection !== null
      || retainedWorkspaces.activeWorkspaceId !== null,
    url: captureCanonicalWorkspaceUrl(),
    navigation: navigationHistory.snapshot(),
    activeRetainedWorkspacePosting,
    retainedWorkspacePresentation,
    retainedWorkspaceInitialDetailAuthority,
    installedRetainedLocation,
    failedWorkspaceUrlState: failedWorkspaceUrlState
      ? structuredClone(failedWorkspaceUrlState)
      : null,
    platformLibraryRetry,
    platformCatalogRetry,
  };
  if (snapshot.state.packages.length === 0
    && !snapshot.state.platformSelection
    && !snapshot.state.uploadedLibrary) {
    snapshot.state.workspaceSubjectOpen = true;
    snapshot.state.atPackageRoot = true;
    snapshot.state.atLibraryRoot = false;
  }
  normalizeWorkspaceAsyncSnapshotState(snapshot.state);
  return snapshot;
}

function normalizeWorkspaceAsyncSnapshotState(
  snapshotState: AppState,
): void {
  const memberAnnotatedLoading = snapshotState.memberAnnotatedLoading;
  const typeMetadataLoading = snapshotState.typeMetadataLoading;
  const packageDependenciesLoading = snapshotState.packageDependenciesLoading;
  const packagePruningLoading = snapshotState.packagePruningLoading;
  const packageIntegrationsLoading = snapshotState.packageIntegrationsLoading;
  const packageOpportunitiesLoading = snapshotState.packageOpportunitiesLoading;
  const packagePerformanceLoading = snapshotState.packagePerformanceLoading;
  const packageMetadataLoading = snapshotState.packageMetadataLoading;
  const memberCallGraphLoading = snapshotState.memberCallGraphLoading;
  const memberCallGraphExpanding = snapshotState.memberCallGraphExpanding;
  const memberFactsLoading = snapshotState.memberFactsLoading;
  const memberDocumentationLoading = snapshotState.memberDocumentationLoading;
  const memberDeclarationLoading = snapshotState.memberDeclarationLoading;

  snapshotState.loading = false;
  snapshotState.memberAnnotatedLoading = false;
  snapshotState.typeMetadataLoading = false;
  snapshotState.packageDependenciesLoading = false;
  snapshotState.packagePruningLoading = false;
  snapshotState.packageIntegrationsLoading = false;
  snapshotState.packageOpportunitiesLoading = false;
  snapshotState.packagePerformanceLoading = false;
  snapshotState.packageMetadataLoading = false;
  snapshotState.memberCallGraphLoading = false;
  snapshotState.memberCallGraphExpanding = false;
  snapshotState.platformDrillLoading = false;
  snapshotState.memberFactsLoading = false;
  snapshotState.memberDocumentationLoading = false;
  snapshotState.memberDeclarationLoading = false;
  snapshotState.runtimePackLoading = false;
  settleInterruptedPlatformStatus(snapshotState);
  if (snapshotState.graphSource.status === "loading") {
    snapshotState.graphSource = {
      status: "cancelled",
      request: snapshotState.graphSource.request,
      title: snapshotState.graphSource.title,
    };
  }
  snapshotState.docViewer =
    normalizeDocumentViewerSnapshot(snapshotState.docViewer);
  snapshotState.spotlightPackageSearch =
    normalizeSpotlightPackageSearchSnapshot(
      snapshotState.spotlightPackageSearch,
    );
  snapshotState.memberSource =
    normalizeSourceResultSnapshot(snapshotState.memberSource);
  snapshotState.typeSource =
    normalizeSourceResultSnapshot(snapshotState.typeSource);
  snapshotState.libraryApiDiff = { status: "idle" };
  snapshotState.compareClone = { status: "idle" };
  snapshotState.workspaceOccurrenceLoading = false;
  snapshotState.workspaceDependencyLoads = new Set();
  snapshotState.typeMetadataGeneration++;
  snapshotState.memberCallGraphSeq++;
  snapshotState.graphMemberNavigationSeq++;

  if (memberAnnotatedLoading) snapshotState.memberAnnotatedKey = "";
  if (typeMetadataLoading) snapshotState.typeMetadataKey = "";
  if (packageDependenciesLoading) snapshotState.packageDependenciesKey = "";
  if (packagePruningLoading) snapshotState.packagePruningKey = "";
  if (packageIntegrationsLoading) snapshotState.packageIntegrationsKey = "";
  if (packageOpportunitiesLoading) snapshotState.packageOpportunitiesKey = "";
  if (packagePerformanceLoading) snapshotState.packagePerformanceKey = "";
  if (packageMetadataLoading) snapshotState.packageMetadataKey = "";
  if (memberCallGraphLoading || memberCallGraphExpanding) {
    snapshotState.memberCallGraphKey = "";
  }
  if (memberFactsLoading) snapshotState.memberFactsKey = "";
  if (memberDocumentationLoading) snapshotState.memberDocumentationKey = "";
  if (memberDeclarationLoading) snapshotState.memberDeclarationKey = "";
}

function settleInterruptedPlatformStatus(targetState: AppState): void {
  if (targetState.platformCatalogStatus.loading) {
    targetState.platformCatalogStatus = {
      loading: false,
      error: "Platform catalog loading was interrupted.",
    };
  }
  if (targetState.platformOpeningStatus.loading) {
    targetState.platformOpeningStatus = {
      loading: false,
      error: "Platform Library opening was interrupted.",
    };
  }
}

function restoreCanonicalWorkspaceRestoreSnapshot(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
) {
  const typeMetadataGeneration = state.typeMetadataGeneration;
  const memberCallGraphSeq = state.memberCallGraphSeq;
  const graphMemberNavigationSeq = state.graphMemberNavigationSeq;
  const platformIndex = state.platformIndex ?? snapshot.state.platformIndex;
  clearWorkspaceOccurrenceView();
  clearWorkspacePackages();
  Object.assign(state, snapshot.state);
  state.typeMetadataGeneration =
    Math.max(typeMetadataGeneration, snapshot.state.typeMetadataGeneration) + 1;
  state.memberCallGraphSeq =
    Math.max(memberCallGraphSeq, snapshot.state.memberCallGraphSeq) + 1;
  state.graphMemberNavigationSeq =
    Math.max(graphMemberNavigationSeq, snapshot.state.graphMemberNavigationSeq) + 1;
  state.platformIndex = platformIndex;
  state.workspaceOccurrenceSignature = "";
  state.workspaceOccurrenceLoading = false;
  state.workspaceOccurrences = null;
  state.workspaceOccurrenceError = "";
  navigationHistory.restore(snapshot.navigation);
  activeRetainedWorkspacePosting = snapshot.activeRetainedWorkspacePosting;
  retainedWorkspacePresentation = snapshot.retainedWorkspacePresentation;
  retainedWorkspaceInitialDetailAuthority =
    snapshot.retainedWorkspaceInitialDetailAuthority;
  installedRetainedLocation = snapshot.installedRetainedLocation;
  failedWorkspaceUrlState = snapshot.failedWorkspaceUrlState
    ? structuredClone(snapshot.failedWorkspaceUrlState)
    : null;
  platformLibraryRetry = snapshot.platformLibraryRetry;
  platformCatalogRetry = snapshot.platformCatalogRetry;
  spotlightCache = null;
  spotlightMemberCache = null;
  persistRecentPackages();
  persistPlatformRecent();
  refreshPackageStats();
}

function cloneCanonicalWorkspaceSnapshotForRetention(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
): CanonicalWorkspaceRestoreSnapshot {
  const platformIndex = snapshot.state.platformIndex;
  const cloned = structuredClone({
    ...snapshot.state,
    platformIndex: null,
    retryAction: null,
    queryNoticeRetryAction: null,
  });
  const retainedState: AppState = {
    ...cloned,
    platformIndex,
    retryAction: null,
    queryNoticeRetryAction: null,
  };

  const copies = new Map<AppPackage, AppPackage>();
  for (const [index, original] of snapshot.state.packages.entries()) {
    const copy = retainedState.packages[index];
    if (!copy) throw new Error("Retained Workspace snapshot lost a Package.");
    copies.set(original, copy);
    catalogRequests.copyPackage(original, copy);
  }
  packageComparisonTargets.copyPackages(copies);
  return {
    state: retainedState,
    hasWorkspace: snapshot.hasWorkspace,
    url: snapshot.url,
    navigation: structuredClone(snapshot.navigation),
    activeRetainedWorkspacePosting:
      snapshot.activeRetainedWorkspacePosting,
    retainedWorkspacePresentation: snapshot.retainedWorkspacePresentation,
    retainedWorkspaceInitialDetailAuthority:
      snapshot.retainedWorkspaceInitialDetailAuthority,
    installedRetainedLocation: snapshot.installedRetainedLocation,
    failedWorkspaceUrlState: snapshot.failedWorkspaceUrlState
      ? structuredClone(snapshot.failedWorkspaceUrlState)
      : null,
    platformLibraryRetry: snapshot.platformLibraryRetry,
    platformCatalogRetry: snapshot.platformCatalogRetry,
  };
}

function captureRetainedWorkspaceSnapshot():
CanonicalWorkspaceRestoreSnapshot {
  const snapshot = cloneCanonicalWorkspaceSnapshotForRetention(
    captureCanonicalWorkspaceRestoreSnapshot());
  invalidateWorkspaceAsyncOwners();
  return snapshot;
}

function invalidateWorkspaceAsyncOwners(): void {
  memberDetailInspection.invalidate();
  invalidateGraphMemberNavigation();
  invalidateMemberCallGraphWork(state);
  packageInspection.invalidatePackageResults();
  clearWorkspaceOccurrenceView();
}

function captureRetainedHostState() {
  return {
    theme: state.theme,
    home: state.home,
    credits: state.credits,
    packageQueryOpen: state.packageQueryOpen,
    packageActivityOpen: state.packageActivityOpen,
    packageQueryPrefix: state.packageQueryPrefix,
    packageQueryNavigationError: state.packageQueryNavigationError,
    packageQueryCatalogError: state.packageQueryCatalogError,
    packageChangesCatalogError: state.packageChangesCatalogError,
    packageQueryOpenedFromApp: state.packageQueryOpenedFromApp,
    packageActivityOpenedFromApp: state.packageActivityOpenedFromApp,
    packageQueryPredecessorEntryId: state.packageQueryPredecessorEntryId,
    packageQueryReturnFocus: state.packageQueryReturnFocus,
    packageQueryReturnFocusPending: state.packageQueryReturnFocusPending,
    packageActivityPredecessorEntryId:
      state.packageActivityPredecessorEntryId,
    packageActivityReturnFocus: state.packageActivityReturnFocus,
    packageActivityReturnFocusPending:
      state.packageActivityReturnFocusPending,
    packageQueryState: state.packageQueryState,
    packageQueryInspection: state.packageQueryInspection,
    packageQueryPresets: state.packageQueryPresets,
    packageQueryTerms: state.packageQueryTerms,
    packageChangesPackageSets: state.packageChangesPackageSets,
    packageChangesState: state.packageChangesState,
    platformIndex: state.platformIndex,
    platformRecent: state.platformRecent,
    recentPackages: state.recentPackages,
    spotlightOpen: state.spotlightOpen,
    spotlightQuery: state.spotlightQuery,
    spotlightIndex: state.spotlightIndex,
    spotlightScope: state.spotlightScope,
    spotlightFocus: state.spotlightFocus,
    spotlightChipIndex: state.spotlightChipIndex,
    spotlightPackageSearch: state.spotlightPackageSearch,
    styleTiers: state.styleTiers,
    styleOptions: state.styleOptions,
    styleCatalogError: state.styleCatalogError,
    taste: state.taste,
    settings: state.settings,
    settingsReturn: state.settingsReturn,
    keyboardHelp: state.keyboardHelp,
    engineReady: state.engineReady,
    engineRuntimeReady: state.engineRuntimeReady,
    engineStartupFailed: state.engineStartupFailed,
    engineStatus: state.engineStatus,
    diag: state.diag,
    buildIdentity: state.buildIdentity,
    buildIdentityStatus: state.buildIdentityStatus,
    buildIdentityError: state.buildIdentityError,
    packageCacheStats: state.packageCacheStats,
    packageCacheStatsStatus: state.packageCacheStatsStatus,
    packageCacheStatsError: state.packageCacheStatsError,
    diagnosticsCapturedAtUtc: state.diagnosticsCapturedAtUtc,
  };
}

function restoreRetainedWorkspaceSnapshot(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  restoreUrl = true,
): void {
  const hostState = captureRetainedHostState();
  restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
  Object.assign(state, hostState);
  activeWorkspaceUrl = snapshot.url;
  if (restoreUrl) {
    workspaceLocation.replace(
      snapshot.url,
      withPlatformRootParentHistory(
        history.state,
        navigationSnapshotHasPlatformRootParent(snapshot.navigation)));
  }
  persistRecentPackages();
  persistPlatformRecent();
  refreshPackageStats();
}

function captureWorkspaceConstructionSnapshots(navigationSeq: number) {
  const supersessionSnapshot = captureWorkspaceNavigationRollback();
  const hasActiveWorkspace = retainedWorkspaces.activeWorkspaceId !== null;
  const retainedSnapshot = hasActiveWorkspace
    ? cloneCanonicalWorkspaceSnapshotForRetention(supersessionSnapshot)
    : null;
  pendingWorkspaceConstruction = {
    navigationSeq,
    supersessionSnapshot,
    retainedSnapshot,
  };
  setWorkspaceConstructionPending(true);
  invalidateWorkspaceAsyncOwners();
  return {
    rollbackSnapshot: supersessionSnapshot,
    retainedSnapshot,
    supersessionSnapshot,
  };
}

function captureWorkspaceMutationSnapshot(
  navigationSeq: number,
): CanonicalWorkspaceRestoreSnapshot {
  const snapshot = captureWorkspaceNavigationRollback();
  pendingWorkspaceConstruction = {
    navigationSeq,
    supersessionSnapshot: snapshot,
    retainedSnapshot: null,
  };
  setWorkspaceConstructionPending(true);
  invalidateWorkspaceAsyncOwners();
  return snapshot;
}

function captureWorkspaceNavigationRollback():
CanonicalWorkspaceRestoreSnapshot {
  let transfer =
    workspaceFeedActivation?.transferCommittedRollback() ?? null;
  if (transfer === null && workspaceFeedRollbackTransfer !== null) {
    transfer = workspaceFeedRollbackTransfer.transfer();
    if (transfer === null) workspaceFeedRollbackTransfer = null;
  }
  if (transfer === null) {
    return captureCanonicalWorkspaceRestoreSnapshot();
  }
  workspaceFeedRollbackTransfer = transfer;
  workspaceFeedRollbackTransfers.set(transfer.snapshot, transfer);
  return transfer.snapshot;
}

async function restoreWorkspaceNavigationRollback(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
): Promise<boolean> {
  const transfer = workspaceFeedRollbackTransfers.get(snapshot);
  if (transfer === undefined) {
    restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
    return true;
  }
  const restored = await transfer.restore();
  if (workspaceFeedRollbackTransfers.get(snapshot) === transfer) {
    workspaceFeedRollbackTransfers.delete(snapshot);
  }
  if (workspaceFeedRollbackTransfer === transfer) {
    workspaceFeedRollbackTransfer = null;
  }
  return restored;
}

function recoverWorkspaceNavigationRollback(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  complete: () => void,
): void {
  setWorkspaceConstructionPending(true);
  observeAsync(
    restoreWorkspaceNavigationRollback(snapshot).then(
      restored => {
        if (restored) complete();
        return undefined;
      },
      (error: unknown) => reportWorkspaceNavigationRollbackFailure(
        snapshot,
        error,
        complete)),
    "Restoring the prior Workspace",
  );
}

function reportWorkspaceNavigationRollbackFailure(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  error: unknown,
  complete: () => void,
): void {
  discardPendingWorkspaceConstruction();
  clearWorkspacePackages();
  state.loading = false;
  state.home = false;
  state.errorTitle = "Workspace recovery failed";
  state.error =
    `The prior Workspace could not be restored: ${errorMessage(error)}`;
  state.retryAction = () =>
    recoverWorkspaceNavigationRollback(snapshot, complete);
  render();
}

function setWorkspaceConstructionPending(pending: boolean): void {
  app.inert = pending;
  if (pending) {
    app.setAttribute("aria-busy", "true");
  } else {
    app.removeAttribute("aria-busy");
  }
}

function cancelPendingWorkspaceConstruction(): void {
  const pending = pendingWorkspaceConstruction;
  if (!pending) return;
  pendingWorkspaceConstruction = null;
  memberDetailInspection.invalidate();
  if (pending.retainedSnapshot) {
    releaseRetainedWorkspaceSnapshot(pending.retainedSnapshot);
  }
  const transfer = workspaceFeedRollbackTransfers.get(
    pending.supersessionSnapshot);
  if (transfer !== undefined) {
    const successor = transfer.transfer();
    if (successor !== null) {
      workspaceFeedRollbackTransfer = successor;
      workspaceFeedRollbackTransfers.set(successor.snapshot, successor);
      return;
    }
  }
  restoreCanonicalWorkspaceRestoreSnapshot(pending.supersessionSnapshot);
  setWorkspaceConstructionPending(false);
}

function discardPendingWorkspaceConstruction(): void {
  const pending = pendingWorkspaceConstruction;
  pendingWorkspaceConstruction = null;
  if (pending) memberDetailInspection.invalidate();
  if (pending?.retainedSnapshot) {
    releaseRetainedWorkspaceSnapshot(pending.retainedSnapshot);
  }
  setWorkspaceConstructionPending(false);
}

function requireRetainedWorkspaceActivation():
RetainedWorkspaceActivationController {
  if (retainedWorkspaceActivation === null) {
    throw new Error("Wait until the Browser engine is ready before opening a saved Workspace.");
  }
  return retainedWorkspaceActivation;
}

function parkActiveCompatibilityWorkspace(): void {
  const activeId = retainedWorkspaces.activeWorkspaceId;
  if (activeId === null) return;
  const snapshot = captureRetainedWorkspaceSnapshot();
  retainedWorkspaces = {
    ...retainedWorkspaces,
    activeWorkspaceId: null,
    workspaces: retainedWorkspaces.workspaces.map(workspace =>
      workspace.id === activeId
        ? { ...workspace, snapshot }
        : workspace),
  };
}

function postRetainedWorkspace(
  posting: BrowserRetainedWorkspacePosting,
  presentationCurrent = true,
): void {
  if (presentationCurrent) {
    const focusedElement = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    if (captureWorkspaceFocus(focusedElement) !== null) {
      focusApplicationMenuButton(document);
    }
  }
  parkActiveCompatibilityWorkspace();
  activeRetainedWorkspacePosting = posting;
  retainedWorkspaceInitialDetailAuthority = {
    realizationId: posting.realizationId,
    navigationSeq: navigationSequence.current(),
    presentationCurrent,
  };
  retainedWorkspacePostings.set(posting.retainedDefinitionId, posting);
  retainedWorkspacePresentation =
    createNavigationDescriptorPresentation(posting);
  if (!presentationCurrent) return;
  prepareUnpublishedWorkspace();
  state.home = false;
  state.loading = true;
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  render({ synchronizeUrl: false });
}

function clearRetainedWorkspacePosting(presentationCurrent = true): void {
  activeRetainedWorkspacePosting = null;
  retainedWorkspacePresentation = null;
  retainedWorkspaceInitialDetailAuthority = null;
  installedRetainedLocation = null;
  if (!presentationCurrent) return;
  prepareUnpublishedWorkspace();
  state.loading = false;
  state.error = "The retained Workspace is unavailable.";
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  activeWorkspaceUrl = null;
  render({ synchronizeUrl: false });
}

async function admitRetainedPackage(
  posting: BrowserRetainedWorkspacePosting,
  inventory: BrowserRetainedWorkspacePackageInventory,
): Promise<BrowserRetainedWorkspacePackage> {
  let offset = 0;
  let admitted: BrowserRetainedWorkspacePackage | null = null;
  const types: CatalogPackageSurface["types"][number][] = [];
  for (;;) {
    const result = await engineClient.catalog.admitRetainedWorkspacePackage(
      posting.retainedDefinitionId,
      posting.realizationId,
      inventory.navigationId,
      offset,
    );
    if (result.status !== "admitted" || result.package === null) {
      throw new Error(
        result.message
          ?? `Package '${inventory.navigationId}' is unavailable.`,
      );
    }
    const page = result.package;
    if (page.typePage.offset !== offset) {
      throw new Error(
        `Package '${inventory.navigationId}' returned an unexpected Type page.`,
      );
    }
    admitted ??= page;
    types.push(...page.surface.types);
    if (page.typePage.nextOffset === null) break;
    if (page.typePage.nextOffset <= offset) {
      throw new Error(
        `Package '${inventory.navigationId}' returned an invalid Type continuation.`,
      );
    }
    offset = page.typePage.nextOffset;
  }
  return {
    ...admitted,
    surface: {
      ...admitted.surface,
      types,
    },
    typePage: {
      ...admitted.typePage,
      offset: 0,
      nextOffset: null,
    },
  };
}

async function admitRetainedPlatform(
  posting: BrowserRetainedWorkspacePosting,
  inventory: BrowserRetainedWorkspacePlatformInventory,
): Promise<BrowserRetainedWorkspacePlatform> {
  let offset = 0;
  let admitted: BrowserRetainedWorkspacePlatform | null = null;
  const types: CatalogPackageSurface["types"][number][] = [];
  for (;;) {
    const result = await engineClient.catalog.admitRetainedWorkspacePlatform(
      posting.retainedDefinitionId,
      posting.realizationId,
      inventory.navigationId,
      offset,
    );
    if (result.status !== "admitted" || result.platform === null) {
      throw new Error(
        result.message
          ?? `Platform '${inventory.navigationId}' is unavailable.`,
      );
    }
    const page = result.platform;
    if (page.typePage.offset !== offset) {
      throw new Error(
        `Platform '${inventory.navigationId}' returned an unexpected Type page.`,
      );
    }
    admitted ??= page;
    types.push(...page.surface.types);
    if (page.typePage.nextOffset === null) break;
    if (page.typePage.nextOffset <= offset) {
      throw new Error(
        `Platform '${inventory.navigationId}' returned an invalid Type continuation.`,
      );
    }
    offset = page.typePage.nextOffset;
  }
  return {
    ...admitted,
    surface: {
      ...admitted.surface,
      types,
    },
    typePage: {
      ...admitted.typePage,
      offset: 0,
      nextOffset: null,
    },
  };
}

function retainedLocationHistoryState(
  retainedDefinitionId: string,
): unknown {
  return {
    ...(isRecord(history.state) ? history.state : {}),
    [retainedWorkspaceHistoryKey]: retainedDefinitionId,
    [retainedWorkspaceHistorySessionKey]: retainedWorkspaceHistorySessionId,
  };
}

function retainedLocationFallbackAssociation():
InstalledLocationAssociation {
  return installedRetainedLocation ?? {
    identity: Symbol("compatibility-workspace"),
    canonicalLocation: activeWorkspaceUrl ?? "/demos",
    historyState: withRetainedWorkspaceHistoryId(history.state),
  };
}

function realignRetainedLocationIntent(
  intent: LocationIntentDeclaration,
  outcome: "unavailable" | "rejected" | "failed" | "aborted",
): void {
  const effect = retainedLocationIntents.classify(intent, {
    outcome,
    synchronization: "current",
    association: retainedLocationFallbackAssociation(),
  });
  if (!retainedLocationIntents.publish(effect, history)) {
    throw new Error(
      "The compatibility Workspace location intent was superseded.",
    );
  }
}

function retainedLocationPresentationCurrent(
  intent: LocationIntentDeclaration,
  canonicalLocation: string,
): boolean {
  return retainedLocationIntents.currentIntentId === intent.id
    || (retainedLocationIntents.currentIntentId === null
      && installedRetainedLocation?.canonicalLocation === canonicalLocation
      && location.href === canonicalLocation);
}

function supersedeRetainedLocationIntentForRoutedNavigation(): void {
  if (retainedLocationIntents.currentIntentId === null) return;
  const routeIntent = retainedLocationIntents.admitNonBrowser(
    "none",
    installedRetainedLocation,
    history,
  );
  const effect = retainedLocationIntents.classify(routeIntent, {
    outcome: "applied",
    synchronization: "current",
    association: retainedLocationFallbackAssociation(),
  });
  if (!retainedLocationIntents.publish(effect, history)) {
    throw new Error(
      "The routed Navigation location intent was superseded before settlement.",
    );
  }
}

async function installRetainedWorkspacePosting(
  posting: BrowserRetainedWorkspacePosting,
  locationIntent: LocationIntentDeclaration,
  browserRestoration?: "exact" | "changed",
): Promise<void> {
  const detailAuthority = retainedWorkspaceInitialDetailAuthority;
  if (detailAuthority?.realizationId !== posting.realizationId) {
    throw new Error(
      "The retained Workspace initial detail authority is unavailable.",
    );
  }
  const detailNavigationSeq = detailAuthority.navigationSeq;
  const admitInitialDetail = detailAuthority.presentationCurrent
    && navigationSequence.isCurrent(detailNavigationSeq);
  const activeTabId = posting.definition.activeTabId;
  const packageInventory = activeTabId === null
    ? posting.packages[0]
    : posting.packages.find(inventory =>
        inventory.navigationId === activeTabId);
  const selectedPlatformInventory = packageInventory === undefined
    ? activeTabId === null
      ? posting.platforms[0]
      : posting.platforms.find(inventory =>
          inventory.navigationId === activeTabId)
    : undefined;
  let admittedPackage: BrowserRetainedWorkspacePackage | null = null;
  let admittedPlatform: BrowserRetainedWorkspacePlatform | null = null;
  let detailFailure: string | null = null;
  if (admitInitialDetail) {
    try {
      if (packageInventory !== undefined) {
        admittedPackage = await admitRetainedPackage(posting, packageInventory);
      } else if (selectedPlatformInventory !== undefined) {
        admittedPlatform = await admitRetainedPlatform(
          posting,
          selectedPlatformInventory,
        );
      }
    } catch (error) {
      detailFailure = errorMessage(error) || "The active row details are unavailable.";
    }
  }
  if (activeRetainedWorkspacePosting?.realizationId !== posting.realizationId) {
    throw new Error("A newer retained Workspace replaced this installation.");
  }

  if (admitInitialDetail
    && navigationSequence.isCurrent(detailNavigationSeq)) {
    let packageModel: AppPackage | null = null;
    let platformModel: AppPackage | null = null;
    try {
      packageModel = admittedPackage
        ? createNuGetPackageModel(admittedPackage.surface)
        : null;
      platformModel = admittedPlatform
        ? createRuntimePackageModel(admittedPlatform.surface)
        : null;
    } catch (error) {
      detailFailure = errorMessage(error)
        || "The active row details could not be projected.";
    }
    state.packages = [
      ...(packageModel ? [packageModel] : []),
      ...(platformModel ? [platformModel] : []),
    ];
    state.package = packageModel ?? platformModel;
    state.platformSelection = admittedPlatform
      ? {
        tfm: admittedPlatform.surface.activeFramework,
        version: admittedPlatform.surface.version,
        includeAllLibraries: false,
        filter: "",
      }
      : null;
    state.rootKind = state.package?.isRuntimePack ? "platform" : "package";
    state.workspaceSubjectOpen = true;
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
    if (detailFailure !== null) {
      if (packageInventory !== undefined) {
        const presentation = retainedWorkspacePresentation;
        if (presentation !== null) {
          retainedWorkspacePresentation = withNavigationPackageDetailFailure(
            presentation,
            packageInventory.consumerPackageSubjectId,
            detailFailure,
          );
        }
      } else if (selectedPlatformInventory !== undefined) {
        const presentation = retainedWorkspacePresentation;
        if (presentation !== null) {
          retainedWorkspacePresentation = withNavigationPlatformDetailFailure(
            presentation,
            selectedPlatformInventory.navigationId,
            detailFailure,
          );
        }
      }
    }
  }
  state.loading = false;
  state.error = "";
  state.errorTitle = "";
  state.errorDetail = "";
  state.retryAction = null;
  activeWorkspaceUrl = posting.canonicalLocation;

  const association: InstalledLocationAssociation = {
    identity: Symbol(posting.retainedDefinitionId),
    canonicalLocation: posting.canonicalLocation,
    historyState: retainedLocationHistoryState(posting.retainedDefinitionId),
  };
  const effect = retainedLocationIntents.classify(locationIntent, {
    outcome: "applied",
    synchronization:
      posting.navigation.synchronization.toLowerCase()
        === "synchronizationrequired"
        ? "synchronization-required"
        : "current",
    association,
    ...(browserRestoration ? { browserRestoration } : {}),
  });
  if (effect.kind === "none" && effect.reason === "stale") {
    installedRetainedLocation = association;
    return;
  }
  if (!retainedLocationIntents.publish(effect, history)) {
    throw new Error(
      "The retained Workspace location intent was superseded before installation.",
    );
  }
  installedRetainedLocation = association;
  render({ synchronizeUrl: false });
}

function completeRetainedActivationPresentation(
  result: BrowserRetainedWorkspaceActivationResult,
  locationIntent: LocationIntentDeclaration,
  navigationSeq: number,
): void {
  if (result.posting === null
    || !navigationSequence.isCurrent(navigationSeq)
    || !retainedLocationPresentationCurrent(
      locationIntent,
      result.posting.canonicalLocation,
    )) {
    return;
  }
  render({ synchronizeUrl: false });
  afterCurrentNavigationFrame(focusWorkspaceOrHeading);
}

async function activateRetainedPackageAction(
  navigationId: string,
): Promise<void> {
  const posting = activeRetainedWorkspacePosting;
  const presentation = retainedWorkspacePresentation;
  if (posting === null || presentation === null) {
    throw new Error("The retained Workspace is no longer active.");
  }
  const navigationSeq = navigationSequence.begin();
  const inventory = posting.packages.find(
    candidate => candidate.navigationId === navigationId);
  const item = presentation.packages.find(
    candidate => candidate.navigationId === navigationId);
  if (item === undefined) {
    throw new Error("The retained Package action is no longer available.");
  }
  if (inventory === undefined) {
    throw new Error("The retained Package inventory is no longer available.");
  }
  let packageModel: AppPackage;
  try {
    const admitted = await admitRetainedPackage(posting, inventory);
    packageModel = createNuGetPackageModel(admitted.surface);
  } catch (error) {
    const currentPresentation = retainedWorkspacePresentation;
    const currentPosting = activeRetainedWorkspacePosting;
    if (currentPresentation !== null
      && navigationSequence.isCurrent(navigationSeq)
      && currentPosting?.realizationId === posting.realizationId) {
      if (currentPresentation.packages.some(
        candidate => candidate.navigationId === navigationId)) {
        retainedWorkspacePresentation = withNavigationPackageDetailFailure(
          currentPresentation,
          inventory.consumerPackageSubjectId,
          errorMessage(error) || "The Package details are unavailable.",
        );
        render({ synchronizeUrl: false });
      }
    }
    return;
  }
  const currentPresentation = retainedWorkspacePresentation;
  const currentPosting = activeRetainedWorkspacePosting;
  if (currentPresentation === null
    || !navigationSequence.isCurrent(navigationSeq)
    || currentPosting?.realizationId !== posting.realizationId) {
    return;
  }
  if (!currentPresentation.packages.some(
    candidate => candidate.navigationId === navigationId)) return;
  retainedWorkspacePresentation = withNavigationPackageDetailFailure(
    currentPresentation,
    inventory.consumerPackageSubjectId,
    null,
  );

  const existing = state.packages.find(candidate =>
    packageIdentityKey(candidate) === packageIdentityKey(packageModel));
  if (existing) {
    state.packages = state.packages.map(candidate =>
      candidate === existing ? packageModel : candidate);
  } else {
    state.packages = [...state.packages, packageModel];
  }
  selectWorkspacePackage(packageModel, {
    stayInWorkspace: false,
    navigationSeq,
    renderSelection: false,
  });
  state.atPackageRoot = true;
  render({ synchronizeUrl: false });
}

async function activateRetainedPlatformAction(
  navigationId: string,
): Promise<void> {
  const posting = activeRetainedWorkspacePosting;
  const inventory = posting?.platforms.find(
    candidate => candidate.navigationId === navigationId);
  if (posting === null || inventory === undefined) {
    throw new Error("The retained Platform is no longer available.");
  }
  const navigationSeq = navigationSequence.begin();

  let platformModel: AppPackage;
  try {
    const admitted = await admitRetainedPlatform(posting, inventory);
    platformModel = createRuntimePackageModel(admitted.surface);
  } catch (error) {
    const currentPresentation = retainedWorkspacePresentation;
    if (currentPresentation !== null
      && navigationSequence.isCurrent(navigationSeq)
      && activeRetainedWorkspacePosting?.realizationId
        === posting.realizationId) {
      retainedWorkspacePresentation = withNavigationPlatformDetailFailure(
        currentPresentation,
        navigationId,
        errorMessage(error) || "The Platform details are unavailable.",
      );
      render({ synchronizeUrl: false });
    }
    return;
  }
  const currentPresentation = retainedWorkspacePresentation;
  if (currentPresentation === null
    || !navigationSequence.isCurrent(navigationSeq)
    || activeRetainedWorkspacePosting?.realizationId
      !== posting.realizationId) {
    return;
  }
  retainedWorkspacePresentation = withNavigationPlatformDetailFailure(
    currentPresentation,
    navigationId,
    null,
  );

  const existing = state.packages.find(candidate =>
    packageIdentityKey(candidate) === packageIdentityKey(platformModel));
  state.packages = existing
    ? state.packages.map(candidate =>
        candidate === existing ? platformModel : candidate)
    : [...state.packages, platformModel];
  selectWorkspacePackage(platformModel, {
    stayInWorkspace: false,
    navigationSeq,
    renderSelection: false,
  });
  state.atPackageRoot = true;
  render({ synchronizeUrl: false });
}

function retainedWorkspaceItems() {
  const publishedActiveSnapshot = pendingWorkspaceConstruction
    ? pendingWorkspaceConstruction.retainedSnapshot
      ?? pendingWorkspaceConstruction.supersessionSnapshot
    : null;
  const compatibility = retainedWorkspaces.workspaces.map(workspace => {
    const workspaceState = workspace.id === retainedWorkspaces.activeWorkspaceId
      ? publishedActiveSnapshot?.state ?? state
      : workspace.snapshot?.state;
    return {
      id: workspace.id,
      label: workspace.label,
      packageCount: workspaceState
        ? workspaceState.packages.filter(pkg => pkg.source.kind !== "platform").length
          + (workspaceState.platformSelection ? 1 : 0)
        : 0,
      active: workspace.id === retainedWorkspaces.activeWorkspaceId,
    };
  });
  const managed = retainedWorkspaceActivation?.state.definitions
    .filter(definition =>
      workspaceFeedActivation?.ownsRetainedDefinition(definition.id) !== true)
    .map(
    definition => ({
      id: definition.id,
      label: definition.label,
      packageCount: (() => {
        const posting = retainedWorkspacePostings.get(definition.id);
        return posting ? posting.packages.length + posting.platforms.length : 0;
      })(),
      active: definition.id
        === retainedWorkspaceActivation?.state.activeDefinitionId,
      status: definition.id === retainedWorkspaceActivation?.state.deactivatingDefinitionId
        ? "Closing" as const
        : definition.id === retainedWorkspaceActivation?.state.pendingDefinitionId
        ? "Activating" as const
        : definition.id === failedManagedRetainedDefinitionId
          ? "Activation failed" as const
          : definition.id === retainedWorkspaceActivation?.state.activeDefinitionId
            ? state.loading
              ? "Activating" as const
              : "Active" as const
            : "Activate" as const,
      deletionDisabled:
        retainedWorkspaceActivation?.state.pendingDefinitionId === definition.id
        || retainedWorkspaceActivation?.state.deactivatingDefinitionId
          === definition.id
        || retainedWorkspaceActivation?.state.unsettledDefinitionIds.includes(
          definition.id) === true,
    })) ?? [];
  return [...compatibility, ...managed];
}

function ensureCurrentWorkspacePublished(): void {
  if (retainedWorkspaces.activeWorkspaceId !== null
    || (!state.package && !state.platformSelection)) return;
  retainedWorkspaces = publishRetainedWorkspace(retainedWorkspaces, null);
  const workspaceId = retainedWorkspaces.activeWorkspaceId;
  void projectCurrentWorkspaceUrl().then(url => {
    if (retainedWorkspaces.activeWorkspaceId === workspaceId)
      activeWorkspaceUrl = url;
    return undefined;
  });
}

function publishInitialLoadedWorkspace(
  rollbackSnapshot: CanonicalWorkspaceRestoreSnapshot,
): boolean {
  if (retainedWorkspaces.activeWorkspaceId !== null) return true;
  const navigationSeq = navigationSequence.current();
  void buildStateUrl().then(
    destination => {
      if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
      publishCurrentWorkspace(null);
      workspaceLocation.push(destination.toString());
      render({ synchronizeUrl: false });
      return undefined;
    },
    (error: unknown) => {
      if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
      failWorkspaceCatalogAction(
        `Couldn’t open Workspace: ${errorMessage(error)}`,
        rollbackSnapshot,
        null,
        focusWorkbenchSearchOrHeading,
      );
      return undefined;
    },
  );
  return true;
}

interface StagedWorkspacePublication {
  collection: RetainedWorkspaceCollection<CanonicalWorkspaceRestoreSnapshot>;
  url: string;
}

function stageCurrentWorkspacePublication(
  previousSnapshot: CanonicalWorkspaceRestoreSnapshot | null,
  url: string,
): StagedWorkspacePublication {
  return {
    collection: publishRetainedWorkspace(
      retainedWorkspaces,
      previousSnapshot),
    url,
  };
}

function commitCurrentWorkspacePublication(
  publication: StagedWorkspacePublication,
): void {
  retainedWorkspaces = publication.collection;
  activeWorkspaceUrl = publication.url;
  pendingWorkspaceConstruction = null;
  setWorkspaceConstructionPending(false);
}

function publishCurrentWorkspace(
  previousSnapshot: CanonicalWorkspaceRestoreSnapshot | null,
): void {
  retainedWorkspaces = publishRetainedWorkspace(
    retainedWorkspaces,
    previousSnapshot);
  const workspaceId = retainedWorkspaces.activeWorkspaceId;
  void projectCurrentWorkspaceUrl().then(url => {
    if (retainedWorkspaces.activeWorkspaceId === workspaceId)
      activeWorkspaceUrl = url;
    return undefined;
  });
  pendingWorkspaceConstruction = null;
  setWorkspaceConstructionPending(false);
}

function retainedWorkspaceCapacityMessage(): string {
  return `Inspect Web retains at most ${MAX_RETAINED_WORKSPACES} live Workspaces. Delete one before opening another.`;
}

function canPublishRetainedWorkspace(): boolean {
  const managedCount =
    retainedWorkspaceActivation?.state.definitions.length ?? 0;
  return retainedWorkspaces.workspaces.length + managedCount
    < MAX_RETAINED_WORKSPACES;
}

function releaseRetainedWorkspaceSnapshot(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
): void {
  for (const packageModel of snapshot.state.packages) {
    catalogRequests.forgetPackage(packageModel);
    packageComparisonTargets.forget(packageModel);
  }
}

function prepareUnpublishedWorkspace(): void {
  invalidateWorkspaceMembershipViews();
  navigationHistory.restore({ stack: [], index: -1 });
  resetLocationFilters();
  clearWorkspacePackages();
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  state.dependenciesGroupIndex = null;
  state.selectedTypeId = "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberSectionState();
  state.workspaceSubjectOpen = false;
  state.atPackageRoot = false;
  state.atLibraryRoot = false;
}

function selectRetainedWorkspace(workspaceId: string): void {
  navigationSequence.begin();
  if (retainedWorkspaceActivation?.state.definitions.some(
    definition => definition.id === workspaceId)) {
    observeAsync(
      activateManagedRetainedWorkspace(workspaceId, "push"),
      "Activating retained Workspace",
    );
    return;
  }
  const activeManagedDefinitionId =
    retainedWorkspaceActivation?.state.activeDefinitionId ?? null;
  if (retainedWorkspaceActivation !== null
    && activeManagedDefinitionId !== null
    && workspaceFeedActivation?.ownsRetainedDefinition(
      activeManagedDefinitionId) !== true) {
    observeAsync(
      activateCompatibilityRetainedWorkspace(workspaceId),
      "Activating compatibility Workspace",
    );
    return;
  }
  if (workspaceId === retainedWorkspaces.activeWorkspaceId) {
    openDefaultWorkspace();
    return;
  }
  try {
    if (!activateRetainedWorkspaceProjection(workspaceId, false)) return;
    workspaceLocation.push(
      activeWorkspaceUrl ?? "/demos",
      withPlatformRootParentHistory(
        history.state,
        navigationSnapshotHasPlatformRootParent(
          navigationHistory.snapshot())));
    render();
    restartRestoredWorkspaceSelectionData();
  } catch (error) {
    showToast(`Could not activate Workspace: ${errorMessage(error)}`);
  }
}

async function deleteActiveManagedWorkspace(
  controller: RetainedWorkspaceActivationController,
  retainedDefinitionId: string,
  completeDeactivation: () => void | Promise<void>,
): Promise<void> {
  if (workspaceFeedActivation?.ownsRetainedDefinition(
    retainedDefinitionId)) {
    await workspaceFeedActivation.deactivateRetainedDefinition(
      retainedDefinitionId,
      completeDeactivation);
    return;
  }
  await controller.delete(retainedDefinitionId, {
    successorDefinitionId: null,
    completeDeactivation,
  });
}

async function activateCompatibilityRetainedWorkspace(
  workspaceId: string,
  browserIntent?: {
    declaration: LocationIntentDeclaration;
    restoration: "exact" | "changed";
  },
): Promise<void> {
    const controller = requireRetainedWorkspaceActivation();
    if (controller.state.pendingDefinitionId !== null) {
      controller.cancelPending();
      await controller.waitForPendingCommit();
    }
    const activeDefinitionId = controller.state.activeDefinitionId;
    if (activeDefinitionId === null) {
      if (activateRetainedWorkspaceProjection(workspaceId, false)) {
        render({ synchronizeUrl: false });
      }
      return;
    }
    const locationIntent = browserIntent?.declaration
      ?? retainedLocationIntents.admitNonBrowser(
        "push",
        installedRetainedLocation,
        history,
      );
    await deleteActiveManagedWorkspace(
      controller,
      activeDefinitionId,
      () => {
        if (!activateRetainedWorkspaceProjection(workspaceId, false)) {
          throw new Error("The compatibility Workspace is no longer available.");
        }
        const association: InstalledLocationAssociation = {
          identity: Symbol(workspaceId),
          canonicalLocation: activeWorkspaceUrl ?? "/demos",
          historyState: withRetainedWorkspaceHistoryId(history.state),
        };
        const effect = retainedLocationIntents.classify(locationIntent, {
          outcome: "applied",
          synchronization: "current",
          association,
          ...(browserIntent
            ? { browserRestoration: browserIntent.restoration }
            : {}),
        });
        retainedLocationIntents.publish(effect, history);
        installedRetainedLocation = null;
        render({ synchronizeUrl: false });
      },
    );
    clearWorkspaceFeedIdentity();
    retainedWorkspacePostings.delete(activeDefinitionId);
}

async function activateManagedRetainedWorkspace(
  retainedDefinitionId: string,
  locationPolicy: "push" | "replace" | "none",
): Promise<void> {
    const controller = requireRetainedWorkspaceActivation();
    if (controller.state.activeDefinitionId === retainedDefinitionId) {
      openDefaultWorkspace();
      return;
    }
    if (controller.state.activeDefinitionId === null
      && retainedWorkspaces.activeWorkspaceId !== null) {
      workspaceLocation.replace(location.href, history.state);
    }
    const locationIntent = retainedLocationIntents.admitNonBrowser(
      locationPolicy,
      installedRetainedLocation,
      history,
    );
    const activationNavigationSeq = navigationSequence.current();
    let result: BrowserRetainedWorkspaceActivationResult;
    try {
      const activation = controller.activate(
        retainedDefinitionId,
        () => retainedLocationIntents.currentIntentId === locationIntent.id,
        posting => installRetainedWorkspacePosting(posting, locationIntent),
        undefined,
        undefined,
        posting => retainedLocationPresentationCurrent(
          locationIntent,
          posting.canonicalLocation,
        ),
      );
      render({ synchronizeUrl: false });
      result = await activation;
    } catch (error) {
      failedManagedRetainedDefinitionId = retainedDefinitionId;
      realignRetainedLocationIntent(locationIntent, "failed");
      render({ synchronizeUrl: false });
      throw error;
    }
    if (result.status === "failed") {
      failedManagedRetainedDefinitionId = retainedDefinitionId;
      realignRetainedLocationIntent(locationIntent, "failed");
      render({ synchronizeUrl: false });
      throw new Error(
        result.failure?.message ?? "The retained Workspace could not be activated.",
      );
    }
    if (result.status === "activated" || result.status === "noEffect") {
      failedManagedRetainedDefinitionId = null;
      completeRetainedActivationPresentation(
        result,
        locationIntent,
        activationNavigationSeq,
      );
      clearWorkspaceFeedIdentity();
    }
}

function activateRetainedWorkspaceProjection(
  workspaceId: string,
  restoreUrl = true,
): boolean {
  const sourceRollbackTransfer =
    workspaceFeedActivation?.transferCommittedRollback() ?? null;
  if (sourceRollbackTransfer !== null) {
    workspaceFeedRollbackTransfers.set(
      sourceRollbackTransfer.snapshot,
      sourceRollbackTransfer);
  }
  const currentSnapshot = sourceRollbackTransfer !== null
    ? cloneCanonicalWorkspaceSnapshotForRetention(
      sourceRollbackTransfer.snapshot)
    : captureRetainedWorkspaceSnapshot();
  if (sourceRollbackTransfer !== null) invalidateWorkspaceAsyncOwners();
  const priorCollection = retainedWorkspaces;
  try {
    const transition = activateRetainedWorkspaceState(
      retainedWorkspaces,
      workspaceId,
      currentSnapshot);
    if (!transition.activatedSnapshot) {
      if (sourceRollbackTransfer !== null) {
        releaseRetainedWorkspaceSnapshot(currentSnapshot);
        recoverWorkspaceNavigationRollback(
          sourceRollbackTransfer.snapshot,
          () => setWorkspaceConstructionPending(false));
      }
      return false;
    }
    restoreRetainedWorkspaceSnapshot(
      transition.activatedSnapshot,
      restoreUrl);
    retainedWorkspaces = transition.collection;
    workspaceFeedActivation?.clearActiveUrl();
    return true;
  } catch (error) {
    retainedWorkspaces = priorCollection;
    if (sourceRollbackTransfer !== null) {
      releaseRetainedWorkspaceSnapshot(currentSnapshot);
      recoverWorkspaceNavigationRollback(
        sourceRollbackTransfer.snapshot,
        () => setWorkspaceConstructionPending(false));
    }
    throw error;
  }
}

function rebindActiveWorkspaceHistory(): void {
  workspaceLocation.replace(
    activeWorkspaceUrl ?? (state.package || state.platformSelection ? location.href : "/demos"),
    history.state);
}

function restartRestoredWorkspaceSelectionData(): void {
  const load = loadSelectionData();
  if (load instanceof Promise) {
    observeAsync(load, "Restoring Workspace selection");
  }
}

function publishFreshEmptyWorkspaceFromHistory(
  destination = "/demos",
): boolean {
  if (!canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    showToast(retainedWorkspaceCapacityMessage());
    history.replaceState(history.state, "", destination);
    return false;
  }
  const previousSnapshot =
    retainedWorkspaces.activeWorkspaceId === null
      ? null
      : captureRetainedWorkspaceSnapshot();
  prepareUnpublishedWorkspace();
  state.loading = false;
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  publishCurrentWorkspace(previousSnapshot);
  workspaceLocation.replace(destination, history.state);
  return true;
}

function deleteRetainedWorkspace(workspaceId: string): void {
  if (retainedWorkspaceActivation?.state.definitions.some(
    definition => definition.id === workspaceId)) {
    observeAsync(
      deleteManagedRetainedWorkspace(workspaceId),
      "Deleting retained Workspace",
    );
    return;
  }
  try {
    navigationSequence.begin();
    const deletingActive =
      workspaceId === retainedWorkspaces.activeWorkspaceId;
    const currentSnapshot = deletingActive
      ? captureRetainedWorkspaceSnapshot()
      : null;
    const transition = deleteRetainedWorkspaceState(
      retainedWorkspaces,
      workspaceId,
      currentSnapshot);
    retainedWorkspaces = transition.collection;
    if (transition.activatedSnapshot) {
      restoreRetainedWorkspaceSnapshot(transition.activatedSnapshot);
    } else if (deletingActive) {
      clearWorkspaceOccurrenceView();
      clearWorkspacePackages();
      resetLocationFilters();
      state.package = null;
      state.loading = false;
      state.error = "";
      state.errorTitle = "";
      state.errorDetail = "";
      state.retryAction = null;
      state.queryNotice = "";
      state.queryNoticeRetryAction = null;
      state.workspaceSubjectOpen = true;
      state.atPackageRoot = true;
      state.atLibraryRoot = false;
      failedWorkspaceUrlState = null;
      navigationHistory.restore({ stack: [], index: -1 });
      activeWorkspaceUrl = null;
      workspaceLocation.replace("/demos", history.state);
    }
    if (transition.removedSnapshot) {
      releaseRetainedWorkspaceSnapshot(transition.removedSnapshot);
    }
    render();
    if (transition.activatedSnapshot) {
      restartRestoredWorkspaceSelectionData();
    }
  } catch (error) {
    showToast(`Could not delete Workspace: ${errorMessage(error)}`);
  }
}

async function deleteManagedRetainedWorkspace(
  retainedDefinitionId: string,
): Promise<void> {
  const controller = requireRetainedWorkspaceActivation();
  const wasActive =
    controller.state.activeDefinitionId === retainedDefinitionId;
  const definitionIndex = controller.state.definitions.findIndex(
    definition => definition.id === retainedDefinitionId);
  const successor = definitionIndex < 0
    ? undefined
    : controller.state.definitions[definitionIndex + 1]
      ?? controller.state.definitions[definitionIndex - 1];
  const compatibilitySuccessor =
    successor === undefined
      ? retainedWorkspaces.workspaces.at(-1)
      : undefined;
  const locationIntent = wasActive
    ? retainedLocationIntents.admitNonBrowser(
      "replace",
      installedRetainedLocation,
      history,
    )
    : null;
  await controller.delete(retainedDefinitionId, successor && locationIntent
    ? {
      successorDefinitionId: successor.id,
      acceptSuccessor: () =>
        retainedLocationIntents.currentIntentId === locationIntent.id,
      completeSuccessor: posting =>
        installRetainedWorkspacePosting(posting, locationIntent),
      isSuccessorPresentationCurrent: posting =>
        retainedLocationPresentationCurrent(
          locationIntent,
          posting.canonicalLocation,
        ),
    }
    : compatibilitySuccessor && locationIntent
      ? {
        successorDefinitionId: null,
        completeDeactivation: () => {
          if (!activateRetainedWorkspaceProjection(
            compatibilitySuccessor.id,
            false,
          )) {
            throw new Error(
              "The compatibility Workspace is no longer available.",
            );
          }
          const association: InstalledLocationAssociation = {
            identity: Symbol(compatibilitySuccessor.id),
            canonicalLocation: activeWorkspaceUrl ?? "/demos",
            historyState: withRetainedWorkspaceHistoryId(history.state),
          };
          const effect = retainedLocationIntents.classify(locationIntent, {
            outcome: "applied",
            synchronization: "current",
            association,
          });
          if (!retainedLocationIntents.publish(effect, history)) {
            throw new Error(
              "The compatibility successor location intent was superseded.",
            );
          }
          installedRetainedLocation = null;
        },
      }
    : {
      successorDefinitionId: null,
      completeDeactivation: () => {
        state.error = "";
        state.errorTitle = "";
        state.errorDetail = "";
        state.retryAction = null;
        state.workspaceSubjectOpen = true;
        state.atPackageRoot = true;
        state.atLibraryRoot = false;
        activeWorkspaceUrl = "/demos";
        if (locationIntent === null) return undefined;
        const association: InstalledLocationAssociation = {
          identity: Symbol("no-workspace"),
          canonicalLocation: "/demos",
          historyState: withRetainedWorkspaceHistoryId(history.state),
        };
        const effect = retainedLocationIntents.classify(locationIntent, {
          outcome: "applied",
          synchronization: "current",
          association,
        });
        if (!retainedLocationIntents.publish(effect, history)) {
          throw new Error(
            "The no-Workspace location intent was superseded.",
          );
        }
        installedRetainedLocation = null;
        return undefined;
      },
    });
  retainedWorkspacePostings.delete(retainedDefinitionId);
  render({ synchronizeUrl: false });
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
  queryTypeSource: (operationId, request) => inspectTypeSource(
    operationId,
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.taste,
    request.view),
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
  cancelEngineSourceRequest: () => {
    if (cancelSourceInspection) {
      observeAsync(
        cancelSourceInspection(),
        "Cancelling the Source request");
    }
  },
  cancelTypeSourceRequest: (operationId, reason) => {
    cancelTypeSourceInspection(operationId, reason);
  },
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
    cancel: (operationId, reason) =>
      cancelPackageQuery(operationId, reason),
    requestMatches: (operationId, additionalMatchCredit) =>
      inspectRequestPackageQueryMatches(operationId, additionalMatchCredit),
    run: (
      operationId,
      prefix,
      termsJson,
      targetFramework,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      initialMatchCredit,
      eventSink,
    ) => inspectRunPackageQuery(
      operationId,
      prefix,
      termsJson,
      targetFramework,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      initialMatchCredit,
      eventSink),
  }, {
    onInspection: inspection => {
      state.packageQueryInspection = inspection;
    },
    reportUnexpectedFailure: (operationId, error, diagnostic) => {
      console.error(
        `Package Query managed operation '${operationId}' failed unexpectedly.`,
        diagnostic ?? error);
    },
  }),
  updateKind => {
    if (updateKind === "reset") {
      state.packageQueryInspection = null;
      if (!state.packageQueryOpen) return;
      packageQueryViewport = null;
      render();
      return;
    }
    if (!state.packageQueryOpen) return;
    schedulePackageQueryStreamRender();
  },
);
const packageChangesController = createPackageChangesController(
  state.packageChangesState,
  createBrowserPackageChangesDataSource({
    cancel: (operationId, reason) =>
      cancelPackageActivity(operationId, reason),
    run: (operationId, requestJson, eventSink) =>
      inspectRunPackageActivity(operationId, requestJson, eventSink),
  }),
  () => {
    if (!state.packageActivityOpen) return;
    schedulePackageActivityStreamRender();
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
    request.type,
    request.typeIdentity,
    request.workspaceJson),
  queryPackageTable: (explorer, index, startRowId, maxRows) =>
    inspectPackageMetadataTable(
      explorer.packageId,
      explorer.version,
      explorer.framework,
      explorer.assemblyId,
      explorer.metadataRoot,
      index,
      startRowId,
      maxRows),
  queryPlatformTable: (explorer, index, startRowId, maxRows) =>
    inspectPlatformMetadataTable(
      explorer.framework,
      explorer.version,
      explorer.assemblyFileName,
      explorer.pack || "",
      explorer.metadataRoot,
      index,
      startRowId,
      maxRows),
  queryPackageHeap: (explorer, heapName) =>
    inspectPackageHeapEntries(
      explorer.packageId,
      explorer.version,
      explorer.framework,
      explorer.assemblyId,
      explorer.metadataRoot,
      heapName),
  queryPlatformHeap: (explorer, heapName) =>
    inspectPlatformHeapEntries(
      explorer.framework,
      explorer.version,
      explorer.assemblyFileName,
      explorer.pack || "",
      explorer.metadataRoot,
      heapName),
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
  scrollExplorerToFocus: explorerScrollToFocus,
});
const memberDetailInspection = createMemberDetailInspectionCoordinator({
  state,
  queryDeclaration: request =>
    request.isRuntimePack
      ? inspectPlatformMemberDeclaration(
          request.framework,
          request.version,
          request.assembly,
          request.platformPack,
          request.typeIdentity,
          request.member,
          request.selectorKey,
          request.metadataToken)
      : inspectMemberDeclaration(
          request.packageId,
          request.version,
          request.framework,
          request.assembly,
          request.typeIdentity,
          request.member,
          request.selectorKey,
          request.metadataToken,
          request.implementationMember),
  queryDocumentation: (request, documentationId) =>
    request.isRuntimePack
      ? inspectPlatformMemberDocumentation(
          request.framework,
          request.version,
          request.assembly,
          request.platformPack,
          documentationId)
      : inspectMemberDocumentation(
          request.packageId,
          request.version,
          request.framework,
          request.assembly,
          documentationId),
  queryFindingCensus: async request => {
    const result = await inspectMemberFindingCensus(
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
    const document = result.annotatedSource.document;
    validateAnnotatedSourceDocument(document);
    const findingEvidenceDocuments =
      result.annotatedSource.findingEvidenceDocuments.map(entry => {
        validateAnnotatedSourceDocument(entry.document);
        return {
          ...entry,
          document: entry.document,
        };
      });
    return {
      ...result,
      annotatedSource: {
        ...result.annotatedSource,
        document,
        findingEvidenceDocuments,
      },
    };
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
  queryPackage: request => inspectMemberCallGraph(
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
    request.traversalFramework),
  queryPlatform: request =>
    queryPlatformCallGraph(inspectExpandPlatformCallGraph, request),
  describeError: errorMessage,
  render,
  renderPreservingMemberFocus,
  renderCallGraph: async () => {
    await renderMermaidCallGraph();
  },
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
  if (!state.package && !state.platformSelection) return null;
  return {
    rootKind: state.rootKind,
    platform: state.platformSelection ? { ...state.platformSelection } : null,
    package: state.package?.id ?? "",
    packageKey: state.rootKind === "library"
      ? uploadedLibraryHistoryKey(state.package)
      : packageIdentityKey(state.package),
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
    atLibraryRoot: state.atLibraryRoot,
    packageLens: state.packageLens,
    libraryLens: state.libraryLens,
    libraryScope: captureLibraryScope(state.libraryScope),
    platformLibrary: state.rootKind === "platform" && !state.atPackageRoot
      ? selectedLibraryShareKey() || null
      : null,
    platformRootParent:
      state.rootKind === "platform"
      && !state.atPackageRoot
      && pendingWorkspaceConstruction === null
      && hasPlatformRootHistoryView(),
    platformPresentedAsRoot: platformIsPresentedAsRoot(),
  };
}

function uploadedLibraryHistoryKey(
  pkg: AppPackage | null | undefined,
): string {
  if (!pkg) return "";
  return [
    packageIdentityKey(pkg),
    encodeURIComponent(pkg.assemblyId.toLowerCase()),
  ].join("|");
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
  const capacityError = view.platform
    ? platformCoordinateCapacityError()
    : "";
  if (capacityError) {
    showToast(capacityError);
    return false;
  }
  workspaceLocation.replace(
    location.href,
    withPlatformRootParentHistory(
      history.state,
      view.platformRootParent === true));
  state.platformPresentedAsRoot =
    view.platformPresentedAsRoot === true;
  if (view.rootKind !== "platform" && view.platform) {
    const target = state.platformIndex?.target(
      view.platform.tfm,
      view.platform.version);
    if (!target) return false;
    retainPlatformPackageForTarget(target);
  }
  if (view.rootKind === "platform" && view.platform && view.atPackageRoot) {
    const target = state.platformIndex?.target(view.platform.tfm, view.platform.version);
    if (!target) return false;
    invalidateMemberDestinationWork(state);
    state.rootKind = "platform";
    state.platformSelection = { ...view.platform };
    state.package = retainPlatformPackageForTarget(target);
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
    state.workspaceSubjectOpen = view.workspaceSubjectOpen;
    state.selectedTypeId = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.libraryScope = null;
    state.loading = false;
    state.error = "";
    state.platformCatalogStatus = { loading: false, error: "" };
    state.platformOpeningStatus = { loading: false, error: "" };
    navigationHistory.normalizeCurrent();
    render();
    return true;
  }
  if (view.rootKind === "platform" && view.platform && view.platformLibrary) {
    const target = state.platformIndex?.target(
      view.platform.tfm,
      view.platform.version);
    const row = target?.rows.find(candidate =>
      platformLibraryKey(candidate) === view.platformLibrary);
    if (!target || !row) return false;
    const resident = runtimePackageForTarget(target);
    if (!resident?.assemblies.some(descriptor =>
      platformLibraryMatchesDescriptor(row, descriptor))) {
      const navigationSeq = navigationSequence.current();
      observeAsync(
        restorePlatformHistoryView(view, row, navigationSeq),
        "Restoring a Platform Library from navigation history");
      return true;
    }
  }
  const pkg = (view.rootKind === "library"
      && uploadedLibraryHistoryKey(state.uploadedLibrary) === view.packageKey
      ? state.uploadedLibrary
      : null)
    ?? packageForView(state.packages, view)
    ?? (view.rootKind === "platform" && view.platform ? runtimePackageForTarget(view.platform) : null);
  if (!pkg) return false;
  if (view.rootKind === "platform" && !state.packages.includes(pkg))
    retainPackageModel(pkg);
  invalidateMemberDestinationWork(state);
  activatePackage(pkg);
  state.rootKind = view.rootKind ?? (pkg.source.kind === "platform" ? "platform" : "package");
  if (view.platform) state.platformSelection = { ...view.platform };
  state.libraryScope = restoreLibraryScope(
    view.libraryScope,
    pkg.assemblies.map(assembly => assembly.id));
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
  state.selectedTypeId = type?.id ?? defaultVisibleTypeId(pkg);
  reconcileAccessibilityFilter(pkg.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = memberHistory.selectedMemberKey;
  state.memberBrowseTypeId = memberHistory.memberBrowseTypeId;
  state.memberKindFilter = memberHistory.memberKindFilter;
  state.memberAccessibilityFilter = memberHistory.memberAccessibilityFilter;
  state.memberTraitFilter = memberHistory.memberTraitFilter;
  state.memberTextFilter = memberHistory.memberTextFilter;
  state.selectedOverloadIndex = memberHistory.selectedOverloadIndex;
  state.memberSection = memberHistory.memberSection;
  state.atPackageRoot = view.atPackageRoot ?? false;
  state.atLibraryRoot = !state.atPackageRoot
    && (view.atLibraryRoot ?? false);
  state.workspaceSubjectOpen =
    view.workspaceSubjectOpen && state.atPackageRoot;
  state.packageLens = view.packageLens ?? "overview";
  state.libraryLens = view.libraryLens ?? "overview";
  state.memberSource = { status: "idle" };
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberDeclaration = null;
  state.memberDeclarationLoading = false;
  state.memberDeclarationError = "";
  state.memberDeclarationKey = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = memberHistory.selectedBodyTarget;
  if (!state.atPackageRoot && !state.atLibraryRoot) revealTypeInFilters(type);
  const requestedOverloadIndex = view.selectedOverloadIndex;
  const historyGraphTarget =
    graphMemberTargetFromShare(graphMemberShareTarget(view.bodyTarget));
  if (!state.atPackageRoot
    && !state.atLibraryRoot
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
    && !state.atLibraryRoot
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
  if (!state.atPackageRoot && !state.atLibraryRoot && state.lens === "api" && state.selectedMemberKey && member) {
    const section = state.memberSection;
    if (section === "source")
      observeAsync(loadSelectedMemberSource(), "Loading member source");
    else if (section === "annotated")
      observeAsync(loadSelectedMemberAnnotatedSource(), "Loading annotated member source");
    else if (section === "call-graph")
      observeAsync(loadSelectedMemberCallGraph(), "Loading the member call graph");
    else if (section === "facts")
      observeAsync(loadSelectedMemberFactsSurface(), "Loading member facts");
    else if (section === "overview")
      observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
    else if (section === "compare")
      render();
    else
      assertNever(section, "member section");
  } else {
    render();
  }
  return true;
}

async function restorePlatformHistoryView(
  view: WorkspaceView,
  row: PlatformAssemblyRow,
  navigationSeq: number,
) {
  const opened = await openPlatformLibrary(
    platformLibraryKey(row),
    row.pack,
    {
      deferPlatformPresentation: true,
      scopeOnly: true,
      navigationSeq,
      tfm: view.platform?.tfm,
      version: view.platform?.version,
      retryAction: () =>
        restorePlatformHistoryView(view, row, navigationSequence.current()),
    });
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (!opened) {
    render();
    return;
  }
  applyView(view);
}

const navigationHistory = createNavigationHistory({
  capture: captureView,
  signature: workspaceViewSignature,
  apply: applyView,
  onExhausted: render,
});
const innerNavigationSequence = createNavigationSequence();
let packageContentLoadingSequence: number | null = null;
type PackageLoadingFocusControl =
  "package-version" | "package-framework" | "framework";
let packageContentLoadingFocusControl: PackageLoadingFocusControl | null = null;
const navigationSequence = {
  begin(): number {
    if (packageContentLoadingSequence !== null
      && innerNavigationSequence.isCurrent(packageContentLoadingSequence)) {
      state.loading = false;
    }
    packageContentLoadingSequence = null;
    packageContentLoadingFocusControl = null;
    cancelPendingWorkspaceConstruction();
    settleInterruptedPlatformStatus(state);
    return innerNavigationSequence.begin();
  },
  invalidate(): void {
    cancelPendingWorkspaceConstruction();
    settleInterruptedPlatformStatus(state);
    innerNavigationSequence.invalidate();
  },
  current: () => innerNavigationSequence.current(),
  isCurrent: (candidate: number) =>
    innerNavigationSequence.isCurrent(candidate),
};

function currentPackageQueryHandoff() {
  return packageQueryHandoffNavigationSeq !== null
    && navigationSequence.isCurrent(packageQueryHandoffNavigationSeq);
}

function recordNav() {
  navigationHistory.record();
}

function navBack() {
  navigationSequence.begin();
  closeGraphExplorerForNavigation();
  dismissAnnotatedSourceModal(false);
  navigationHistory.back();
}

function navForward() {
  navigationSequence.begin();
  closeGraphExplorerForNavigation();
  dismissAnnotatedSourceModal(false);
  navigationHistory.forward();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

const retainedWorkspaceHistoryKey = "inspectWorkspaceId";
const retainedWorkspaceHistorySessionKey = "inspectWorkspaceSession";
const retainedWorkspaceHistorySessionId = crypto.randomUUID();
const platformRootParentHistoryKey = "inspectPlatformRootParent";

function retainedWorkspaceIdFromHistory(historyState: unknown): string | null {
  if (!isRecord(historyState)) return null;
  if (historyState[retainedWorkspaceHistorySessionKey]
    !== retainedWorkspaceHistorySessionId) return null;
  const value = historyState[retainedWorkspaceHistoryKey];
  return typeof value === "string" ? value : null;
}

function historyReferencesRetainedWorkspace(historyState: unknown): boolean {
  return retainedWorkspaceIdFromHistory(historyState) !== null;
}

function historyHasPlatformRootParent(historyState: unknown): boolean {
  return isRecord(historyState)
    && historyState[platformRootParentHistoryKey] === true;
}

function withPlatformRootParentHistory(
  historyState: unknown,
  present: boolean,
): unknown {
  if (present) {
    return {
      ...(isRecord(historyState) ? historyState : {}),
      [platformRootParentHistoryKey]: true,
    };
  }
  if (!isRecord(historyState)
    || !(platformRootParentHistoryKey in historyState)) {
    return historyState;
  }
  const next = { ...historyState };
  delete next[platformRootParentHistoryKey];
  return next;
}

function withRetainedWorkspaceHistoryId(historyState: unknown): unknown {
  const activeWorkspaceId =
    retainedWorkspaceActivation?.state.activeDefinitionId
    ?? retainedWorkspaces.activeWorkspaceId;
  if (!activeWorkspaceId) {
    if (!isRecord(historyState)
      || !(retainedWorkspaceHistoryKey in historyState)) {
      return historyState;
    }
    const next = { ...historyState };
    delete next[retainedWorkspaceHistoryKey];
    delete next[retainedWorkspaceHistorySessionKey];
    return next;
  }
  return {
    ...(isRecord(historyState) ? historyState : {}),
    [retainedWorkspaceHistoryKey]: activeWorkspaceId,
    [retainedWorkspaceHistorySessionKey]: retainedWorkspaceHistorySessionId,
  };
}

function memberSectionsFor(member: AppMemberGroup) {
  if (state.rootKind === "library") {
    return memberSectionDefinitions.filter(([id]) => id === "overview");
  }
  const allowed = new Set(
    memberSectionIdsFor(
      member,
      state.package?.isRuntimePack,
      memberHasSelectedBody(member)));
  if (!compareAvailableFor(state.package)) allowed.delete("compare");
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

const workspaceLocation = createAsyncWorkspaceLocationPersistence({
  current: () => ({
    href: location.href,
    pathname: location.pathname,
    search: location.search,
    hash: location.hash,
  }),
  replace: (url, historyState) =>
    history.replaceState(
      withRetainedWorkspaceHistoryId(historyState),
      "",
      url),
  push: (url, historyState) =>
    history.pushState(
      withRetainedWorkspaceHistoryId(historyState),
      "",
      url),
  decode: value => inspectDecodeWorkspaceShareState(value),
  encode: shareState => inspectEncodeWorkspaceShareState(shareState),
});
let pendingDemoNavigation: {
  navigationSeq: number;
  destination: string;
} | null = null;

function parseLocation() {
  return workspaceLocation.parseCurrent();
}

async function parseWorkspaceHref(href: string): Promise<ParsedLocation> {
  const url = new URL(href, location.href);
  return await parseWorkspaceLocationAsync({
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
  if (!workspaceLocation.push(pendingDemoNavigation.destination)) return false;
  pendingDemoNavigation = null;
  return true;
}

function commitStagedWorkspaceNavigation(
  navigationSeq: number,
  publication: StagedWorkspacePublication,
): boolean {
  const previousCollection = retainedWorkspaces;
  retainedWorkspaces = publication.collection;
  if (!commitDemoNavigation(navigationSeq)) {
    retainedWorkspaces = previousCollection;
    return false;
  }
  commitCurrentWorkspacePublication(publication);
  return true;
}

function cancelDemoNavigation(navigationSeq?: number): void {
  if (navigationSeq === undefined
    || pendingDemoNavigation?.navigationSeq === navigationSeq) {
    pendingDemoNavigation = null;
  }
}

function commitRestoredWorkspaceNavigation(
  navigationSeq: number,
  failureHandler: ((message: string) => void) | null = null,
  commitHistory = failureHandler !== null,
) {
  if (!failureHandler || !commitHistory) return true;
  if (!commitDemoNavigation(navigationSeq)) return false;
  syncUrl();
  return true;
}

type ParsedLocation = ParsedWorkspaceLocation;

const initialWorkspace = workspaceLocation.preflightCurrent();
const initialLocation = initialWorkspace.visible;
// A bare visit (no package, no shared workspace packet) lands on the intro/home page
// instead of auto-loading a package. Any deep link or shared link skips home and restores
// its workspace directly.
state.credits = isCreditsPath(location.pathname);
state.packageQueryOpen = isPackageQueryPath(location.pathname);
state.packageActivityOpen = isPackageActivityPath(location.pathname);
const diagnosticsOpen = isDiagnosticsPath(location.pathname);
const productHomeDemosOpen = isProductHomeDemosPath(location.pathname);
if (diagnosticsOpen) {
  state.diagnosticsCapturedAtUtc = new Date().toISOString();
  diagnosticsHeadingFocusPending = true;
}
if (state.packageQueryOpen) {
  applyPackageQueryHistory(history.state);
}
if (state.packageActivityOpen) {
  applyPackageActivityHistory(history.state);
}
state.home = state.credits
  || (!diagnosticsOpen
    && !state.packageQueryOpen
    && !state.packageActivityOpen
    && !productHomeDemosOpen
    && !initialLocation.package
    && !initialWorkspace.hasWorkspaceState
    && !initialLocation.routeFailure);
if (productHomeDemosOpen) {
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
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
  state.atLibraryRoot = false;
  state.packageLens = initialLocation.packageLens || "overview";
} else if (initialLocation.atLibraryRoot) {
  state.atPackageRoot = false;
  state.atLibraryRoot = true;
  state.libraryLens = initialLocation.libraryLens || "overview";
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
const productNavigationBinding = bindProductNavigation(app, {
  currentDestination: currentProductDestination,
  onAction: dispatchProductAction,
  onNavigate: navigateProductDestination,
  unavailableReason: productNavigationUnavailableReason,
});
const graphExplorer = createGraphExplorer(document);
let graphExplorerNavigationFocusPending = false;
let graphExplorerOriginKey: string | null = null;
type MermaidModule = typeof import("mermaid");
type MarkedModule = typeof import("marked");
type DomPurifyModule = typeof import("dompurify");
let mermaidModule: Promise<MermaidModule> | undefined;
let markdownModule: Promise<[MarkedModule, DomPurifyModule]> | undefined;
const depGraphRenderSequence = createDependencyGraphRenderSequence();
let mermaidRenderSequence = 0;
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
let workspaceProductFocusParkingActive = false;
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
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

const NUGET_DEFAULT_PACKAGE_ICON =
  "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png";

function renderInspectedSubjectIcon(pkg: AppPackage): string {
  if (scope() === "workspace")
    return '<span class="subject-icon" aria-hidden="true">W</span>';
  if (state.rootKind === "library")
    return '<span class="subject-icon" aria-hidden="true">◫</span>';
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
  queryPackageVersions: pkg => inspectPackageVersions(pkg.id, pkg.version),
  updatePackageVersionSelect: updateVersionSelect,
});
const packageComparisonTargets =
  createPackageComparisonTargets(() => state.packages);
const libraryApiDiff = createLibraryApiDiffCoordinator({
  state,
  operationAuthority,
  query: (operationId, requestJson) =>
    inspectLibraryApiDiff(operationId, requestJson),
  cancel: (operationId, reason) => {
    observeAsync(
      cancelLibraryApiDiff(operationId, reason),
      "Cancelling Library API Diff");
  },
  describeError: errorMessage,
  reportOperationDiagnostic: diagnostic => {
    console.error("Library API Diff operation authority failure.", diagnostic);
    return undefined;
  },
  render,
});
const compareClone = createCompareCloneCoordinator({
  state,
  operationAuthority,
  query: request => inspectCloneCandidates(request),
  describeError: errorMessage,
  reportOperationDiagnostic: diagnostic => {
    console.error("Compare Clone operation authority failure.", diagnostic);
    return undefined;
  },
  render,
});
const packageRemoval = createPackageRemoval({
  state,
  persistRecent: entries =>
    localStorage.setItem("inspect-recent-packages", JSON.stringify(entries)),
  activate: activateAfterPackageRemoval,
  release: finishPackageRemoval,
});
const savedWorkspaces = createSavedWorkspaces({
  read: () => localStorage.getItem("inspect-saved-workspaces"),
  write: value => localStorage.setItem("inspect-saved-workspaces", value),
  capture: captureSavedWorkspacePacket,
  open: openSavedWorkspaceEntry,
  render: focus => {
    render({ synchronizeUrl: false });
    if (focus) {
      afterCurrentNavigationFrame(() => restoreSavedWorkspaceFocus(document, focus));
    }
  },
});
const spotlight = createSpotlight({
  keybindings,
  state,
  lenses: () => availableTypeLenses(),
  escapeHtml,
  highlightRanges,
  kindIcon,
  searchResults: spotlightResults,
  pickResult: pickSpotlightResult,
  removeResult: removeSpotlightPackage,
  executeCommand,
  reportCommandError: error =>
    reportAsyncFailure("Running a Spotlight command", error),
  commandContext: () => !state.home && state.package
    ? { command: state.spotlightQuery, package: state.package }
    : null,
  schedulePackageFetch: () => spotlightPackageSearch.schedule(),
  resetPackageSearch: () => spotlightPackageSearch.reset(),
  packageSearchLoading: () =>
    spotlightPackageSearchIsLoading(state.spotlightPackageSearch),
  packageSearchError: () =>
    spotlightPackageSearchError(state.spotlightPackageSearch),
  packageCount: () => state.packages.length,
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
    && !state.spotlightOpen
    && !graphSourceIsOpen(state.graphSource)
    && !documentViewerIsOpen(state.docViewer)
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
    restoreOrdinaryModalDismissFocus(() => {
      if (contentFrameUsesPush() && contentFrameMedia.matches) {
        if (contentFramePane === "navigation")
          focusContentNavigation(document);
        else
          focusContentNavigationToggle(document);
        return;
      }
      focusContentNavigation(document);
    });
  });
}

function restoreOrdinaryModalDismissFocus(fallback: () => void) {
  if (!scopeBarBinding?.restoreOpenMenuFocus()) fallback();
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

function renderPreservingContentFrameFocus() {
  const pendingFocusGeneration = documentFocusGeneration;
  requestAnimationFrame(() => {
    const activeOwner = contentFrameFocusOwnerFor(document.activeElement);
    const owner = activeOwner
      ?? (pendingFocusGeneration === documentFocusGeneration
          && contentFrameMedia.matches
          && contentFramePane === "detail"
        ? "navigation-toggle"
        : null);
    const target = owner === "navigation" || owner === "detail-toggle"
      ? "navigation"
      : owner === "detail" || owner === "navigation-toggle"
        ? "navigation-toggle"
        : null;
    const focusGeneration = documentFocusGeneration;
    render({ synchronizeUrl: false });
    if (target && focusGeneration === documentFocusGeneration)
      focusContentFrameTarget(target);
  });
}

function trackContentFrameFocus(event: FocusEvent) {
  documentFocusGeneration++;
  if (workspaceProductFocusParkingActive
    && document.activeElement !== app) {
    workspaceProductFocusParkingActive = false;
    app.removeAttribute("tabindex");
  }
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
  selectFramework: (framework, source) => {
    if (contentFrameUsesPush()) contentFramePane = "detail";
    observeAsync(
      switchPackageFramework(
        framework,
        source === "legacy" ? "framework" : "package-framework"),
      "Switching the package framework");
  },
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
  const withinLibrary = (item: AppTypeSurface) =>
    !state.libraryScope || state.libraryScope.has(libraryKey(item));
  return state.package.types.find(item =>
      item.id === state.selectedTypeId && withinLibrary(item))
    || filteredTypes()[0]
    || state.package.types.find(withinLibrary)
    || null;
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
    return scoped?.id || "";
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
  if (`${item.name} ${item.namespace} ${item.kind} ${item.assembly}`.toLowerCase().includes(needle)) return true;
  const members = item.api?.filter(member => !member.graphOnly);
  if (!members || !members.length) return false;
  for (const member of members) {
    if ((member.name || "").toLowerCase().includes(needle)) return true;
  }
  return false;
}

// Scope follows the product-issued asset identity, not its display name.
function libraryKey(item: InspectedTypeSurface | null | undefined) {
  return item?.assemblyId ?? "";
}

// Libraries admitted by the selected package coordinate, sorted alphabetically
// by name. Assembly descriptors keep libraries with no public types visible
// instead of deriving the package inventory from type rows.
function packageLibraryInventory() {
  if (!state.package) return [];
  const libraries = state.package.assemblies.map(assembly => ({
    id: assembly.id,
    name: assembly.name.replace(/\.dll$/i, ""),
    asset: assembly.asset,
    version: assembly.version,
    culture: assembly.culture,
    publicKeyToken: assembly.publicKeyToken,
    types: assembly.publicTypes,
    members: assembly.publicMembers,
    platformPack: assembly.platformPack,
  }));
  return alphabetizeLibrarySubjects(libraries);
}

function packageLibraries() {
  return packageLibraryInventory();
}

function selectedLibraryName() {
  return selectedLibrary()?.name ?? "";
}

function packageLibraryDisplayLabels() {
  return librarySubjectDisplayLabels(packageLibraries());
}

function selectedLibraryDisplayLabel() {
  const library = selectedLibrary();
  return library
    ? packageLibraryDisplayLabels().get(library.id) ?? library.name
    : "";
}

function aggregateTypeLibraryLabels() {
  const labels = new Map<string, string>();
  if (!aggregateLibrarySubjectIsActive() || !state.package) return labels;

  const firstLibraryByDefinition = new Map<string, string>();
  const collidingDefinitions = new Set<string>();
  for (const item of state.package.types) {
    const definition = item.definitionId || item.id;
    const library = libraryKey(item);
    const firstLibrary = firstLibraryByDefinition.get(definition);
    if (firstLibrary !== undefined && firstLibrary !== library)
      collidingDefinitions.add(definition);
    else
      firstLibraryByDefinition.set(definition, library);
  }

  const libraryNames = packageLibraryDisplayLabels();
  for (const item of state.package.types) {
    if (!collidingDefinitions.has(item.definitionId || item.id)) continue;
    const library = libraryNames.get(libraryKey(item));
    if (library) labels.set(item.id, library);
  }
  return labels;
}

function typeDefiningLibraryLabel(
  item: AppTypeSurface | null | undefined,
) {
  return item ? aggregateTypeLibraryLabels().get(item.id) ?? "" : "";
}

function typeQualifiedLibraryLabel(
  item: AppTypeSurface | null | undefined,
) {
  const definingLibrary = typeDefiningLibraryLabel(item);
  if (definingLibrary) return definingLibrary;
  const selectedLabel = selectedLibraryDisplayLabel();
  return selectedLabel !== selectedLibraryName() ? selectedLabel : "";
}

function aggregateLibrarySubjectIsActive() {
  return state.rootKind === "package" && state.libraryScope === null;
}

function aggregateLibrarySubjectIsAvailable() {
  return state.rootKind === "package" && packageLibraries().length > 0;
}

function activeLibrarySubjectName() {
  return aggregateLibrarySubjectIsActive()
    ? "All libraries"
    : selectedLibraryDisplayLabel();
}

function selectedLibrary() {
  const libraries = packageLibraries();
  if (state.atLibraryRoot && aggregateLibrarySubjectIsActive()) return null;
  const key = state.libraryScope?.size === 1
    ? state.libraryScope.values().next().value
    : selectedType()?.assemblyId;
  return key
    ? libraries.find(library => library.id === key) ?? null
    : libraries[0] ?? null;
}

function selectedLibraryRequest() {
  return selectedLibrary()?.id ?? "";
}

function platformLibraryForRequest(pkg: AppPackage, key: string) {
  const descriptor = resolvePackageLibrary(pkg.assemblies, key);
  const row = state.platformIndex?.target(pkg.activeFramework, pkg.version)?.rows.find(candidate =>
    descriptor && platformLibraryMatchesDescriptor(candidate, descriptor));
  if (!row?.hasImplementation) throw new Error("The selected Library has no exact Platform catalog implementation.");
  return row;
}

function selectedLibraryShareKey() {
  if (state.atPackageRoot) return "";
  if (state.rootKind !== "platform" && state.libraryScope === null) return "";
  const library = selectedLibrary();
  if (state.rootKind !== "platform") return library?.id ?? "";
  if (!library) return "";
  return platformLibraryKey(platformLibraryForRequest(currentPackage(), library.id));
}

function selectedTypeMetadataLibraryIdentity() {
  return state.rootKind === "platform"
    ? selectedLibraryShareKey()
    : "";
}

function selectDefaultPackageSubject(pkg: AppPackage) {
  state.workspaceSubjectOpen = false;
  state.atLibraryRoot = Boolean(pkg.assemblyId);
  state.atPackageRoot = !state.atLibraryRoot;
  state.libraryScope = null;
  state.packageLens = "overview";
  state.libraryLens = "overview";
}

function selectLibrarySubject(
  key: string,
  options: { preserveView?: boolean; preserveLens?: boolean } = {},
) {
  const descriptor = resolvePackageLibrary(state.package?.assemblies ?? [], key);
  const library = packageLibraries().find(candidate => candidate.id === descriptor?.id);
  if (!library) {
    appendQueryNotice(`The library '${key}' is not uniquely available in this package.`);
    render();
    return false;
  }
  state.workspaceSubjectOpen = false;
  state.atPackageRoot = false;
  state.atLibraryRoot = true;
  state.libraryScope = new Set([library.id]);
  if (options.preserveView) {
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
  } else {
    if (!options.preserveLens) state.libraryLens = "overview";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.typeFilter = "";
    normalizeLibrarySelection();
  }
  if (state.package?.isRuntimePack) {
    recordPlatformRecent(
      library.name,
      library.platformPack || platformPackForAssembly(library.name));
  }
  return true;
}

function selectAggregateLibrarySubject(
  options: { preserveView?: boolean; preserveLens?: boolean } = {},
) {
  if (!aggregateLibrarySubjectIsAvailable()) return false;
  state.workspaceSubjectOpen = false;
  state.atPackageRoot = false;
  state.atLibraryRoot = true;
  state.libraryScope = null;
  if (options.preserveView) {
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
  } else {
    if (!options.preserveLens) state.libraryLens = "overview";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.typeFilter = "";
    normalizeLibrarySelection();
  }
  return true;
}

function enterRetainedLibrarySubject(
  options: { preserveView?: boolean; preserveLens?: boolean } = {},
) {
  return state.rootKind !== "platform" && state.libraryScope === null
    ? selectAggregateLibrarySubject(options)
    : selectLibrarySubject(selectedLibrary()?.id ?? "", options);
}

function enterTypeSubject(
  type: AppTypeSurface | null | undefined,
  options: { preserveAggregate?: boolean } = {},
) {
  if (!type) return false;
  const preserveAggregate =
    options.preserveAggregate ?? aggregateLibrarySubjectIsActive();
  revealTypeInFilters(type);
  state.workspaceSubjectOpen = false;
  state.atPackageRoot = false;
  state.atLibraryRoot = false;
  if (!preserveAggregate)
    state.libraryScope = new Set([libraryKey(type)]);
  state.selectedTypeId = type.id;
  return true;
}

// Reset the type cursor/selection to the first type in the selected Library.
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
    state.libraryScope = new Set([libraryKey(type)]);
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
  if (packageModel.source.kind === "platform") {
    platformPackages.set(platformTargetKey({
      tfm: packageModel.activeFramework, version: packageModel.version,
    }), packageModel);
    replacedPackage = state.packages.find(pkg => pkg.source.kind === "platform") ?? replacedPackage;
  }
  const activeWasReplaced = packageIdentityEquals(state.package, packageModel);
  let retained = retainWorkspacePackage(
    state.packages,
    state.package,
    packageModel,
    replacedPackage,
    MAX_WORKSPACE_PACKAGES);
  if (packageModel.source.kind !== "platform"
    && state.platformSelection
    && !retained.packages.some(pkg => pkg.source.kind === "platform")) {
    const platformAdjusted = retainWorkspacePackage(
      retained.packages,
      state.package,
      packageModel,
      null,
      MAX_WORKSPACE_PACKAGES - 1);
    retained = {
      packages: platformAdjusted.packages,
      evicted: [...retained.evicted, ...platformAdjusted.evicted],
    };
  }
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

  catalogRequests.forgetPackage(packageModel);
  packageComparisonTargets.forget(packageModel);
}

function activateAfterPackageRemoval(next: AppPackage | null): void {
  if (next) {
    activatePackage(next, { resetAccessibility: true });
    state.rootKind = next.source.kind === "platform" ? "platform" : "package";
  } else {
    state.package = null;
    state.rootKind = state.platformSelection ? "platform" : "package";
  }
  state.dependenciesGroupIndex = null;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.selectedTypeId = "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetLocationFilters();
  resetMemberSectionState();
  if (!state.home) state.workspaceSubjectOpen = true;
}

function invalidateWorkspaceMembershipViews(): void {
  invalidateGraphMemberNavigation();
  invalidateMemberCallGraphWork(state);
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.platformStack = [];
  state.workspaceShareBasis = null;
  state.workspaceFeedUrl = null;
  clearWorkspaceOccurrenceView();
  packageInspection.invalidatePackageResults();
  spotlightCache = null;
  spotlightMemberCache = null;
}

function finishPackageRemoval(removed: AppPackage): void {
  navigationSequence.begin();
  invalidateWorkspaceMembershipViews();
  releasePackageModelCaches(removed);
  if (!state.package && !state.platformSelection) {
    activeWorkspaceUrl = "/demos";
    if (!state.home) {
      state.workspaceSubjectOpen = true;
      workspaceLocation.replace("/demos", history.state);
    }
  }
  render();
  if (!state.home && scope() === "member" && state.memberSection === "call-graph") {
    observeAsync(loadSelectedMemberCallGraph(), "Refreshing the member call graph");
  }
}

function removeSpotlightPackage(result: RemovableSpotlightResult): boolean {
  try {
    if (result.kind === "pkg-recent") {
      packageRemoval.forgetRecent(result.entry.id);
    } else {
      packageRemoval.removeLoaded(workspacePackageRemovalKey({
        ...result.pkg,
        activeFramework: result.pkg.activeFramework ?? "",
      }));
    }
    return true;
  } catch (error) {
    showToast(`Could not remove package: ${errorMessage(error)}`);
    return false;
  }
}

function removeWorkspacePackageRow(key: string): void {
  try {
    packageRemoval.removeLoaded(key);
  } catch (error) {
    showToast(`Could not remove package: ${errorMessage(error)}`);
  }
}

function clearWorkspacePackages() {
  state.platformDemoContextId = null;
  const discarded = state.packages;
  state.packages = [];
  state.package = null;
  state.uploadedLibrary = null;
  state.workspaceShareBasis = null;
  state.workspaceFeedUrl = null;
  state.platformSelection = null;
  state.frameworkLibraryPresentation = null;
  state.platformPresentedAsRoot = false;
  state.platformSlot = -1;
  state.rootKind = "package";
  state.integrationMode = "integrations";
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
  {
    stayInWorkspace = false,
    publishInitial = false,
    navigationSeq,
    renderSelection = true,
  }: {
    stayInWorkspace?: boolean;
    publishInitial?: boolean;
    navigationSeq?: number;
    renderSelection?: boolean;
  } = {},
) {
  const packageModel = pkg
    ? state.packages.find(item => packageIdentityKey(item) === packageIdentityKey(pkg))
    : null;
  if (!packageModel) return;
  if (navigationSeq === undefined) navigationSequence.begin();
  else if (!navigationSequence.isCurrent(navigationSeq)) return;
  const rollbackSnapshot = publishInitial
    && retainedWorkspaces.activeWorkspaceId === null
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  activatePackage(packageModel, { resetAccessibility: true });
  state.home = false;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  if (packageModel.isRuntimePack) {
    const firstLibrary = packageLibraries()[0];
    state.libraryScope = firstLibrary ? new Set([firstLibrary.id]) : null;
  } else {
    selectDefaultPackageSubject(packageModel);
  }
  state.selectedTypeId = defaultVisibleTypeId(packageModel);
  reconcileAccessibilityFilter(
    packageModel.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberFilters();
  resetMemberSectionState();
  if (stayInWorkspace || packageModel.isRuntimePack) {
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
  }
  state.workspaceSubjectOpen = stayInWorkspace;
  if (rollbackSnapshot
    && !publishInitialLoadedWorkspace(rollbackSnapshot)) return;
  if (renderSelection)
    render({ synchronizeUrl: rollbackSnapshot === null });
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
  const request = workspaceOccurrenceRequest();
  const signature = JSON.stringify(request);
  if (state.workspaceOccurrenceLoading) return;
  if (signature === state.workspaceOccurrenceSignature) return;

  state.workspaceOccurrenceSignature = signature;
  void queryWorkspaceOccurrenceView(request, signature);
}

let workspaceOccurrenceRevision = 0;
let workspaceOccurrenceClearBarrier: Promise<void> = Promise.resolve();
let workspaceOccurrenceClearFailure: unknown = null;

async function awaitWorkspaceOccurrenceClear(): Promise<void> {
  await workspaceOccurrenceClearBarrier;
  if (workspaceOccurrenceClearFailure !== null) {
    const failure = workspaceOccurrenceClearFailure;
    throw failure instanceof Error
      ? failure
      : new Error(errorMessage(failure), { cause: failure });
  }
}

async function queryWorkspaceOccurrenceView(
  request: ReturnType<typeof workspaceOccurrenceRequest>,
  signature: string,
) {
  const revision = workspaceOccurrenceRevision;
  let superseded = false;
  state.workspaceOccurrenceLoading = true;
  state.workspaceOccurrenceError = "";
  try {
    await awaitWorkspaceOccurrenceClear();
    const view = await inspectQueryWorkspacePackageOccurrences(request);
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
    if (ownsCurrentRequest) render();
  }
}

function retryWorkspaceOccurrenceView() {
  state.workspaceOccurrenceSignature = "";
  ensureWorkspaceOccurrenceView();
  render();
}

function clearWorkspaceOccurrenceView() {
  const revision = workspaceOccurrenceRevision + 1;
  workspaceOccurrenceClearBarrier =
    workspaceOccurrenceClearBarrier.then(async () => {
      try {
        await inspectClearWorkspacePackageOccurrences();
        if (revision === workspaceOccurrenceRevision)
          workspaceOccurrenceClearFailure = null;
      } catch (error) {
        if (revision === workspaceOccurrenceRevision)
          workspaceOccurrenceClearFailure = error;
        reportAsyncFailure(
          "Clearing Workspace package occurrences",
          error);
      }
      return undefined;
    });
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
    packageActivityOpen: state.packageActivityOpen,
    loading: state.loading,
    error: state.error,
    home: state.home,
    hasPackage: state.packages.some(item => !item.isRuntimePack),
  });
}

async function activateWorkspacePackageOccurrence(action: string) {
  await awaitWorkspaceOccurrenceClear();
  const result: BrowserWorkspacePackageOccurrenceActivation =
    await inspectActivateWorkspacePackageOccurrence(action);
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

  const packageModel = createWorkspaceOccurrencePackageModel(
    result.package,
    state.package,
    state.packages);
  retainPackageModel(packageModel);
  selectWorkspacePackage(packageModel);
}

function activatePackage(
  pkg: AppPackage,
  { resetAccessibility = false }: { resetAccessibility?: boolean } = {},
) {
  if (state.package?.isRuntimePack && state.package !== pkg) {
    const library = selectedLibrary();
    if (library) {
      state.frameworkLibraryPresentation = {
        tfm: state.package.activeFramework,
        version: state.package.version,
        libraryId: library.id,
      };
    }
  }
  const changed = !packageIdentityEquals(state.package, pkg);
  state.workspaceSubjectOpen = false;
  state.package = pkg;
  if (changed) {
    state.dependenciesGroupIndex = null;
  }
  state.rootKind = pkg.source.kind === "platform" ? "platform" : "package";
  if (state.rootKind === "platform"
    && (state.platformSelection?.tfm !== pkg.activeFramework
      || state.platformSelection?.version !== pkg.version)) {
    state.platformSelection = {
      tfm: pkg.activeFramework, version: pkg.version,
      includeAllLibraries: false, filter: "",
    };
  }
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

// Selection sits on the structural subject ladder. Type and Member always retain
// the exact Library ancestor through libraryScope.
function scope(): WorkspaceScope {
  if (state.workspaceSubjectOpen && state.atPackageRoot) return "workspace";
  if (state.atPackageRoot) return state.rootKind;
  if (state.atLibraryRoot) return "library";
  return memberScopeIsActive(state, selectedType()?.id) ? "member" : "type";
}

function selectScopeLensByIndex(index: number, workspaceScope: WorkspaceScope): void {
  if (workspaceScope === "workspace" || workspaceScope === "platform") {
    return;
  } else if (workspaceScope === "package") {
    const selected = packageLensesFor(state.package)[index];
    if (selected) {
      state.packageLens = selected[0];
      render();
    }
  } else if (workspaceScope === "library") {
    const selected = libraryLensesFor(state.package)[index];
    if (selected) selectLibraryLens(selected[0]);
  } else if (workspaceScope === "type") {
    const selected = availableTypeLenses()[index];
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

// The resident runtime pseudo-package has no NuGet package dependency graph.
function packageLensesFor(pkg: AppPackage | null) {
  if (!pkg?.isRuntimePack) return packageLenses;
  return packageLenses.filter(([id]) => id === "overview");
}

function libraryLensesFor(pkg: AppPackage | null) {
  if (state.rootKind === "library")
    return libraryLenses.filter(([id]) => id === "overview");
  return libraryLenses.filter(([id]) => {
    if (id === "compare")
      return pkg?.source.kind === "nuget.org" && !pkg.isRuntimePack;
    return !pkg?.isRuntimePack || id !== "references";
  });
}

function availableTypeLenses() {
  return state.rootKind === "library"
    ? typeLensesFor({ isRuntimePack: true })
    : typeLensesFor(state.package);
}

function libraryLensRequiresExactLibrary(lens: LibraryLens) {
  switch (lens) {
    case "overview": return false;
    case "compare":
    case "references":
    case "integrations":
    case "analysis":
    case "metrics":
    case "metadata": return true;
    default: return assertNever(lens, "library lens");
  }
}

function selectLibraryLens(lens: LibraryLens) {
  if (state.libraryLens === lens) return;
  if (libraryLensRequiresExactLibrary(lens)
    && aggregateLibrarySubjectIsActive()) {
    const preferredLibraryId = preferredLibrarySubjectId(
      packageLibraries(),
      state.package?.id ?? "");
    if (preferredLibraryId) {
      selectLibrarySubject(
        preferredLibraryId,
        { preserveLens: true });
    }
  }
  state.libraryLens = lens;
  render();
}

// The exact subject Compare is presenting, or null when Compare is not the
// active working surface. Library, Type, and Member share one inspector; Diff
// projects the Library-root document at each level and Clone runs the
// subject's own scoped query.
type CompareSubject =
  | {
      readonly kind: "library";
      readonly pkg: AppPackage;
      readonly library: NonNullable<ReturnType<typeof selectedLibrary>>;
    }
  | {
      readonly kind: "type";
      readonly pkg: AppPackage;
      readonly library: NonNullable<ReturnType<typeof selectedLibrary>>;
      readonly type: AppTypeSurface;
    }
  | {
      readonly kind: "member";
      readonly pkg: AppPackage;
      readonly library: NonNullable<ReturnType<typeof selectedLibrary>>;
      readonly type: AppTypeSurface;
      readonly group: AppMemberGroup;
      readonly overload: AppMemberSurface | undefined;
    };

function compareAvailableFor(pkg: AppPackage | null | undefined): boolean {
  return pkg?.source.kind === "nuget.org" && !pkg.isRuntimePack;
}

function currentCompareSubject(): CompareSubject | null {
  const pkg = state.package;
  const library = selectedLibrary();
  if (!pkg
    || !library
    || state.home
    || state.credits
    || state.packageQueryOpen
    || state.packageActivityOpen
    || isDiagnosticsPath(location.pathname)
    || !state.engineReady
    || state.loading
    || Boolean(state.error)
    || !compareAvailableFor(pkg)
    || state.atPackageRoot) {
    return null;
  }
  if (state.atLibraryRoot) {
    return state.libraryLens === "compare"
      ? { kind: "library", pkg, library }
      : null;
  }
  const type = selectedType();
  if (!type) return null;
  if (scope() === "member") {
    if (state.lens !== "api" || state.memberSection !== "compare") return null;
    const group = selectedMember(type);
    if (!group) return null;
    return {
      kind: "member",
      pkg,
      library,
      type,
      group,
      overload: selectedConcreteOverload(
        group.overloads,
        state.selectedOverloadIndex),
    };
  }
  return state.lens === "compare"
    ? { kind: "type", pkg, library, type }
    : null;
}

function currentCompareMode(): CompareMode {
  const pkg = state.package;
  return pkg ? packageComparisonTargets.get(pkg).mode : "diff";
}

function typeIdentifierOf(type: AppTypeSurface): string {
  return type.definitionId ?? type.id;
}

function typeFullDisplay(type: AppTypeSurface): string {
  const name = typeDisplayName(type);
  return type.namespace ? `${type.namespace}.${name}` : name;
}

function compareSubjectLabel(subject: CompareSubject): string {
  switch (subject.kind) {
    case "library": return subject.library.name;
    case "type": return typeFullDisplay(subject.type);
    case "member":
      return `${typeFullDisplay(subject.type)}.${subject.group.name}`;
    default: return assertNever(subject, "Compare subject");
  }
}

function currentLibraryApiDiffSelection(): LibraryApiDiffSelection | null {
  const subject = currentCompareSubject();
  if (!subject || currentCompareMode() !== "diff") return null;
  const { pkg, library } = subject;
  return {
    packageModel: pkg,
    packageId: pkg.id,
    currentVersion: pkg.version,
    targetFramework: pkg.activeFramework,
    compileAssetId: library.id,
    target: resolveEffectiveDiffTarget(
      packageComparisonTargets.get(pkg).diff,
      catalogRequests.packageVersions(pkg),
    ),
  };
}

function cloneScopePackages(
  pkg: AppPackage,
): { packages: AppPackage[]; message: string } {
  const clone = packageComparisonTargets.get(pkg).clone;
  if (clone.kind === "workspace") {
    return {
      packages: state.packages.filter(item => !item.isRuntimePack),
      message: "",
    };
  }
  if (!state.packages.includes(clone.package)) {
    return {
      packages: [],
      message: `${clone.package.id} ${clone.package.version} is no longer in this Workspace. Choose another Clone scope.`,
    };
  }
  if (clone.package.isRuntimePack) {
    return {
      packages: [],
      message: "Clone search scope cannot be a platform runtime library.",
    };
  }
  return {
    packages: clone.package === pkg ? [pkg] : [pkg, clone.package],
    message: "",
  };
}

function currentCompareCloneTarget(): CompareCloneReconcileTarget {
  const subject = currentCompareSubject();
  if (!subject || currentCompareMode() !== "clone") return null;
  const { pkg, library } = subject;
  const scoped = cloneScopePackages(pkg);
  if (scoped.message) return { kind: "unavailable", message: scoped.message };
  const selectedPackageIndex = scoped.packages.indexOf(pkg);
  if (selectedPackageIndex < 0) {
    return {
      kind: "unavailable",
      message: "The selected Package is not part of the Clone scope.",
    };
  }
  let seed: CompareCloneSelection["seed"];
  if (subject.kind === "library") {
    seed = { kind: "Library", typeDefinitionId: null, member: null, body: null };
  } else {
    const typeDefinitionId = subject.type.definitionId;
    if (!typeDefinitionId) {
      return {
        kind: "unavailable",
        message: "The selected Type has no exact metadata identity.",
      };
    }
    if (subject.kind === "type") {
      seed = { kind: "Type", typeDefinitionId, member: null, body: null };
    } else {
      const overload = subject.overload;
      if (!overload) {
        return {
          kind: "unavailable",
          message: "Choose one overload to search from.",
        };
      }
      if (overload.graphOnly
        || !overload.stableSelector
        || !overload.canonicalSignature
        || !overload.anchorDigest
        || !overload.anchorTypeFullName) {
        return {
          kind: "unavailable",
          message: "The selected Member has no complete portable identity.",
        };
      }
      const selectedBody = state.selectedBodyTarget;
      const body = selectedBody?.memberName
        && selectedBody.selectorKey
        && selectedBody.metadataToken
        ? {
            memberName: selectedBody.memberName,
            selectorKey: selectedBody.selectorKey,
            metadataToken: selectedBody.metadataToken,
          }
        : null;
      seed = {
        kind: "Member",
        typeDefinitionId,
        member: {
          stableSelector: overload.stableSelector,
          canonicalSignature: overload.canonicalSignature,
          fingerprint: overload.anchorDigest,
          typeFullName: overload.anchorTypeFullName,
          memberName: overload.name,
        },
        body,
      };
    }
  }
  return {
    kind: "selection",
    selection: {
      packageModel: pkg,
      packages: scoped.packages.map(item => ({
        packageId: item.id,
        version: item.version,
        targetFramework: item.activeFramework,
      })),
      selectedPackageIndex,
      assembly: library.id,
      seed,
    },
  };
}

function compareTargetText(subject: CompareSubject, mode: CompareMode): string {
  const { pkg } = subject;
  if (mode === "diff") {
    const target = resolveEffectiveDiffTarget(
      packageComparisonTargets.get(pkg).diff,
      catalogRequests.packageVersions(pkg));
    return target.kind === "available"
      ? `${target.version} → ${pkg.version}`
      : target.message;
  }
  const scoped = cloneScopePackages(pkg);
  if (scoped.message) return scoped.message;
  const clone = packageComparisonTargets.get(pkg).clone;
  return clone.kind === "workspace"
    ? `Workspace: ${scoped.packages.length.toLocaleString()} loaded ${scoped.packages.length === 1 ? "Package" : "Packages"}`
    : `${clone.package.id} ${clone.package.version} (${clone.package.activeFramework})`;
}

// Exact loaded subjects the Compare rows may activate. Type identity is the
// metadata definition identity carried by both the loaded surface and the Diff
// document; Member identity is the anchor digest carried by both.
function compareLibraryTypes(subject: CompareSubject): AppTypeSurface[] {
  return subject.pkg.types.filter(type =>
    !type.graphOnly && libraryKey(type) === subject.library.id);
}

function compareCloneJoin(subject: CompareSubject): CompareCloneJoin {
  const methods = new Map<number, CompareCloneJoinedMethod>();
  const types = subject.kind === "library"
    ? compareLibraryTypes(subject)
    : [subject.type];
  for (const type of types) {
    const typeIdentifier = typeIdentifierOf(type);
    const typeDisplay = typeFullDisplay(type);
    for (const overload of type.api) {
      if (overload.graphOnly || !overload.anchorDigest) continue;
      const joined: CompareCloneJoinedMethod = {
        typeIdentifier,
        typeDisplay,
        memberFingerprint: overload.anchorDigest,
        memberDisplay: overload.signature,
      };
      const tokens = [
        overload.metadataToken,
        ...(overload.bodySelectors ?? []).map(body => body.token),
      ];
      for (const token of tokens) {
        if (typeof token === "number" && token !== 0 && !methods.has(token))
          methods.set(token, joined);
      }
    }
  }
  return {
    containingLibrary: {
      packageId: subject.pkg.id,
      version: subject.pkg.version,
      targetFramework: subject.pkg.activeFramework,
      assemblyName: subject.library.name,
    },
    methods,
  };
}

function renderCompareSurface(): string {
  const subject = currentCompareSubject();
  if (!subject) return "";
  const mode = currentCompareMode();
  const subjectLabel = compareSubjectLabel(subject);
  const targetText = compareTargetText(subject, mode);
  const subjectKind: CompareSubjectKind = subject.kind;
  if (mode === "clone") {
    return renderCompareClone(state.compareClone, escapeHtml, {
      subject: subjectKind,
      subjectLabel,
      targetText,
      join: compareCloneJoin(subject),
      selectedRank: state.compareCloneSelectedRank,
    });
  }
  let diffSubject: LibraryApiDiffSubject;
  let activatableTypes: ReadonlySet<string> | undefined;
  let activatableMembers: ReadonlySet<string> | undefined;
  if (subject.kind === "library") {
    diffSubject = { kind: "library" };
    activatableTypes = new Set(compareLibraryTypes(subject)
      .map(typeIdentifierOf));
  } else if (subject.kind === "type") {
    diffSubject = { kind: "type", typeIdentifier: typeIdentifierOf(subject.type) };
    // A moved Member's counterpart Type is activatable from the Type inventory.
    activatableTypes = new Set(compareLibraryTypes(subject)
      .map(typeIdentifierOf));
    activatableMembers = new Set(subject.type.api
      .filter(overload => !overload.graphOnly && overload.anchorDigest)
      .map(overload => overload.anchorDigest));
  } else {
    diffSubject = {
      kind: "member",
      typeIdentifier: typeIdentifierOf(subject.type),
      memberFingerprint: subject.overload?.anchorDigest ?? "",
    };
  }
  return renderLibraryApiDiff(state.libraryApiDiff, escapeHtml, {
    subject: diffSubject,
    subjectLabel,
    targetText,
    mode: "diff",
    ...(activatableTypes ? { activatableTypes } : {}),
    ...(activatableMembers ? { activatableMembers } : {}),
  });
}

// Drill-down keeps Compare and the retained mode active: the destination Type
// opens directly in its Compare inspector as one transition.
function activateCompareType(typeIdentifier: string) {
  const subject = currentCompareSubject();
  if (!subject) return;
  const matches = compareLibraryTypes(subject)
    .filter(type => typeIdentifierOf(type) === typeIdentifier);
  const target = matches.length === 1 ? matches[0] : undefined;
  if (!target) {
    showToast(matches.length === 0
      ? "That Type is not loaded in the selected Library."
      : "That Type identity is ambiguous in the selected Library.");
    return;
  }
  enterTypeSubject(target);
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === target.id);
  state.lens = "compare";
  state.compareCloneSelectedRank = null;
  render();
}

function activateCompareMember(memberFingerprint: string) {
  const subject = currentCompareSubject();
  if (!subject || subject.kind === "library") return;
  const type = subject.type;
  let match: { group: AppMemberGroup; overloadIndex: number } | null = null;
  let ambiguous = false;
  for (const group of publicMemberGroups(type)) {
    for (const [overloadIndex, overload] of group.overloads.entries()) {
      if (overload.anchorDigest !== memberFingerprint) continue;
      if (match) ambiguous = true;
      match = { group, overloadIndex };
    }
  }
  if (!match || ambiguous) {
    showToast(ambiguous
      ? "That Member identity is ambiguous in the selected Type."
      : "That Member is not loaded in the selected Type.");
    return;
  }
  state.compareCloneSelectedRank = null;
  navigateToMember(
    subject.pkg,
    type,
    match.group,
    match.group.overloads.length > 1 ? match.overloadIndex : null,
    null,
    "compare");
}

function selectCompareMode(mode: CompareMode) {
  const pkg = state.package;
  if (!pkg || currentCompareMode() === mode) return;
  packageComparisonTargets.selectMode(pkg, mode);
  state.compareCloneSelectedRank = null;
  render();
}

function scopedPlatformLibrary() {
  if (!state.package?.isRuntimePack) return null;
  return selectedLibraryName() || null;
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
  state.memberSource = { status: "idle" };
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
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
    observeAsync(loadSelectedMemberFactsSurface(), "Loading member facts");
  else if (id === "overview")
    observeAsync(loadSelectedMemberDocumentation(), "Loading member documentation");
  else if (id === "compare")
    render();
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

function enterMemberScope(
  options: { preserveAggregate?: boolean } = {},
) {
  const type = selectedType();
  if (!type) return false;
  const preserveAggregate =
    options.preserveAggregate ?? aggregateLibrarySubjectIsActive();
  const groups = memberGroups(type);
  if (!groups.length) {
    state.memberBrowseTypeId = "";
    return false;
  }
  state.atPackageRoot = false;
  state.atLibraryRoot = false;
  if (!preserveAggregate)
    state.libraryScope = new Set([libraryKey(type)]);
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

function stepPackageFrameworkFocus(
  delta: number,
  eventTarget: EventTarget | null,
) {
  const target = eventTarget instanceof Element ? eventTarget : null;
  const list = target?.closest<HTMLElement>('[data-nav-scope="frameworks"]');
  if (!list) return false;
  const rows = [
    ...list.querySelectorAll<HTMLElement>("[data-package-framework]"),
  ];
  if (!rows.length) return true;
  const focused = target?.closest<HTMLElement>("[data-package-framework]");
  const focusedIndex = focused ? rows.indexOf(focused) : -1;
  const activeIndex = rows.findIndex(
    row => row.getAttribute("aria-current") === "page");
  const currentIndex = focusedIndex >= 0
    ? focusedIndex
    : Math.max(activeIndex, 0);
  const nextIndex = Math.max(
    0,
    Math.min(rows.length - 1, currentIndex + delta));
  rows[nextIndex]?.focus({ preventScroll: true });
  return true;
}

// ↑/↓ always act on the visible nav list, whatever depth you are at.
function stepNav(delta: number, eventTarget: EventTarget | null) {
  if (stepPackageFrameworkFocus(delta, eventTarget)) return;
  if (navMode() === "member") stepMemberNav(delta, false);
  else stepTypeSelection(delta);
}

// ←/→ act on the horizontal tab strip at your depth: sections when a concrete
// overload is open, otherwise the lens strip.
function stepHorizontal(delta: number) {
  if (scope() === "workspace" || scope() === "platform") return;
  if (state.atPackageRoot) {
    const strip = packageLensesFor(state.package);
    const index = strip.findIndex(([id]) => id === state.packageLens);
    const next = strip[(index + delta + strip.length) % strip.length];
    if (!next) return;
    state.packageLens = next[0];
    render();
    return;
  }
  if (state.atLibraryRoot) {
    const strip = libraryLensesFor(state.package);
    const index = strip.findIndex(([id]) => id === state.libraryLens);
    const next = strip[(index + delta + strip.length) % strip.length];
    if (!next) return;
    selectLibraryLens(next[0]);
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
    const available = availableTypeLenses();
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
    state.atLibraryRoot = false;
    rebindActiveWorkspaceHistory();
    render();
    return;
  }
  if (state.atPackageRoot) {
    if (!enterRetainedLibrarySubject()) return;
    showContentDetailAfterRender();
    render();
    return;
  }
  if (state.atLibraryRoot) {
    if (!enterTypeSubject(selectedType())) return;
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
  if (!state.atPackageRoot && !state.atLibraryRoot) {
    state.atPackageRoot = !selectedLibrary();
    state.atLibraryRoot = !state.atPackageRoot;
    render();
    return true;
  }
  if (state.atLibraryRoot) {
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
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

type HomeFocusTarget =
  | {
    kind: "id";
    surface: "home" | "settings";
    id: string;
  }
  | {
    kind: "link";
    region: "home-bar" | "data-bar";
    href: string;
  }
  | { kind: "spotlight-scope"; scope: string }
  | { kind: "settings-theme"; theme: string }
  | { kind: "settings-taste"; taste: string };

function captureHomeFocus(
  focused: HTMLElement | null,
): HomeFocusTarget | null {
  if (!focused) return null;
  const surface = focused.closest("#settings-dialog")
    ? "settings"
    : focused.closest(".home")
      ? "home"
      : null;
  if (!surface) return null;
  if (focused.id) return { kind: "id", surface, id: focused.id };
  if (surface === "settings") {
    const theme = focused.dataset.theme;
    if (theme) return { kind: "settings-theme", theme };
    const taste = focused.dataset.taste;
    return taste ? { kind: "settings-taste", taste } : null;
  }
  const spotlightScope = focused.dataset.slScope;
  if (spotlightScope) {
    return { kind: "spotlight-scope", scope: spotlightScope };
  }
  if (!(focused instanceof HTMLAnchorElement)) return null;
  const region = focused.closest(".home-bar")
    ? "home-bar"
    : focused.closest(".data-bar")
      ? "data-bar"
      : null;
  const href = focused.getAttribute("href");
  return region && href ? { kind: "link", region, href } : null;
}

function restoreHomeFocus(target: HomeFocusTarget): boolean {
  let element: HTMLElement | null = null;
  if (target.kind === "id") {
    element = document.getElementById(target.id);
  } else if (target.kind === "spotlight-scope") {
    element = [...document.querySelectorAll<HTMLElement>("[data-sl-scope]")]
      .find(candidate => candidate.dataset.slScope === target.scope)
      ?? null;
  } else if (target.kind === "settings-theme") {
    element = [...document.querySelectorAll<HTMLElement>(
      "#settings-dialog [data-theme]",
    )]
      .find(candidate => candidate.dataset.theme === target.theme)
      ?? null;
  } else if (target.kind === "settings-taste") {
    element = [...document.querySelectorAll<HTMLElement>(
      "#settings-dialog [data-taste]",
    )]
      .find(candidate => candidate.dataset.taste === target.taste)
      ?? null;
  } else {
    element = [...document.querySelectorAll<HTMLAnchorElement>(
      `.${target.region} a[href]`,
    )].find(candidate => candidate.getAttribute("href") === target.href)
      ?? null;
  }
  if (!element) return false;
  element.focus({ preventScroll: true });
  return true;
}

function settingsOwnsHomeFocusTarget(target: HomeFocusTarget | null): boolean {
  return target?.kind === "settings-theme"
    || target?.kind === "settings-taste"
    || (target?.kind === "id" && target.surface === "settings");
}

function render(options: { synchronizeUrl?: boolean } = {}) {
  productNavigationBinding.beforeRender();
  try {
    renderCore(options);
  } finally {
    productNavigationBinding.afterRender();
  }
}

function renderCore(options: { synchronizeUrl?: boolean }) {
  sourceInspection.cancelHiddenRequest();
  libraryApiDiff.reconcile(currentLibraryApiDiffSelection());
  compareClone.reconcile(currentCompareCloneTarget());
  const graphExplorerWasOpen = graphExplorer.isOpen;
  graphExplorer.beforeRender(graphExplorerKey());
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
  document.body.classList.remove(
    "package-query-route",
    "package-activity-route");
  const applicationMenuHadFocus = applicationMenuOwnsFocus(document);
  const focusedElement = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null;
  const loadingPackageContent = state.loading && !state.error
    && state.package !== null
    && packageContentLoadingSequence !== null
    && navigationSequence.isCurrent(packageContentLoadingSequence);
  const packageLoadingHadFocus =
    focusedElement?.id === "package-content-loading";
  const packageFrameworkHadFocus =
    focusedElement?.dataset.packageFramework !== undefined;
  const packageLoadingControl = packageLoadingHadFocus
    ? focusedElement?.dataset.packageLoadingControl
    : packageFrameworkHadFocus
      ? "package-framework"
      : focusedElement?.id;
  const packageLoadingFramework = packageLoadingHadFocus
    ? focusedElement?.dataset.packageLoadingFramework
    : focusedElement?.dataset.packageFramework;
  const packageControlHadFocus = packageLoadingControl === "framework"
    || packageLoadingControl === "package-framework"
    || packageLoadingControl === "package-version";
  const packageRetryHadFocus = loadingPackageContent
    && focusedElement?.id === "retry-notice";
  const homeFocus =
    pendingHomeFocusTarget ?? captureHomeFocus(focusedElement);
  contentFrameFocusOwner = null;
  contentFrameReplacementAuthority = null;
  const scopeBarOwnsFocus =
    focusedElement?.closest("[data-scope-bar]") != null;
  const scopeBarFocus = focusedElement
    ? captureScopeBarFocus(focusedElement)
    : null;
  const workspaceFocus = captureWorkspaceFocus(focusedElement);
  const integrationTabFocus = focusedElement?.dataset.integrationMode;
  const compareTabFocus = focusedElement?.dataset.compareMode;
  const workbenchSearchHadFocus = focusedElement?.id === "open-search";
  const levelOneHeadingHadFocus =
    focusedElement?.matches("main h1") === true;
  scopeBarBinding?.disconnect();
  workbenchShellBinding?.disconnect();
  workbenchShellBinding = null;
  disconnectLibraryOpen?.();
  disconnectLibraryOpen = null;

  if (diagnosticsDestinationFocusPending
    && !isDiagnosticsPath(location.pathname)) {
    queueMicrotask(scheduleDiagnosticsDestinationFocus);
  }
  if (isDiagnosticsPath(location.pathname)) {
    loadingBotSrc = null;
    renderDiagnosticsPage();
    bindLibraryOpenEvents();
    return;
  }
  // The Metadata Explorer is a full-bleed "browse the database" view layered over the
  // package workbench. Like Settings it owns no URL and renders first, returning to the
  // Metadata lens on close.
  if (state.explorer?.open) {
    loadingBotSrc = null;
    renderMetadataExplorer();
    bindLibraryOpenEvents();
    return;
  }
  if (state.credits) {
    loadingBotSrc = null;
    renderCreditsView();
    bindLibraryOpenEvents();
    return;
  }
  if (state.packageQueryOpen
    && state.engineReady
    && !state.loading
    && !state.error) {
    document.body.classList.add("package-query-route");
    loadingBotSrc = null;
    renderPackageQueryPage();
    bindLibraryOpenEvents();
    return;
  }
  if (state.packageActivityOpen
    && state.engineReady
    && !state.loading
    && !state.error) {
    document.body.classList.add("package-activity-route");
    loadingBotSrc = null;
    renderPackageActivityPage();
    bindLibraryOpenEvents();
    return;
  }
  packageQueryLiveAnnouncer.reset();
  // A loading/interstitial view holds one random bot for its whole appearance; any non-loading
  // view resets it so the next interstitial picks a fresh random bot (see interstitialBotSrc).
  const retainedWorkspacePostingVisible =
    state.workspaceSubjectOpen
    && activeRetainedWorkspacePosting !== null;
  const workspaceCatalogVisible =
    state.workspaceSubjectOpen
    && (isProductHomeDemosPath(location.pathname)
      || state.platformSelection !== null
      || retainedWorkspacePostingVisible)
    && state.engineReady;
  const showingInterstitial =
    (state.loading
      && !loadingPackageContent
      && !retainedWorkspacePostingVisible)
    || state.error
    || (!state.home && !state.package && !workspaceCatalogVisible);
  if (!showingInterstitial) loadingBotSrc = null;
  if ((state.loading
      && !loadingPackageContent
      && !retainedWorkspacePostingVisible)
    || state.error) {
    renderLoading();
    return;
  }
  retainFailedWorkspaceUrl();
  if (state.workspaceSubjectOpen && isProductHomeDemosPath(location.pathname)) {
    renderProductDemosPage();
    if (state.settings) {
      document.querySelector<HTMLElement>("#settings-title")
        ?.focus({ preventScroll: true });
    } else if (state.keyboardHelp) {
      document.querySelector<HTMLElement>("#keyboard-help-title")
        ?.focus({ preventScroll: true });
    } else if (workspaceFocus) {
      restoreWorkspaceFocus(document, workspaceFocus);
    } else if (homeFocus) {
      restoreHomeFocus(homeFocus);
    } else if (levelOneHeadingHadFocus) {
      focusLevelOneHeading();
    }
    restorePackageRouteReturnFocus();
    return;
  }
  if (state.home) {
    renderHomeView(homeFocus);
    return;
  }
  if (scope() === "platform") {
    renderPlatformView();
    if (scopeBarFocus) restoreScopeBarFocus(document, scopeBarFocus);
    else if (applicationMenuHadFocus) focusApplicationMenuButton(document);
    else if (workbenchSearchHadFocus) focusWorkbenchSearch(document);
    else if (levelOneHeadingHadFocus) focusLevelOneHeading();
    restorePackageRouteReturnFocus();
    recordNav();
    if (options.synchronizeUrl !== false) syncUrl();
    return;
  }
  if (!state.package) {
    if (workspaceCatalogVisible) {
      renderWorkspaceCatalogView();
      if (state.settings) {
        focusSettingsEntry();
      } else if (state.keyboardHelp) {
        document.querySelector<HTMLElement>("#keyboard-help-title")
          ?.focus({ preventScroll: true });
      } else if (applicationMenuHadFocus) {
        focusApplicationMenuButton(document);
      } else if (workspaceFocus) {
        if (!restoreWorkspaceFocus(document, workspaceFocus)) {
          focusLevelOneHeading();
        }
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
      restorePackageRouteReturnFocus();
      recordNav();
      if (!isProductHomeDemosPath(location.pathname) && options.synchronizeUrl !== false) syncUrl();
      return;
    }
    renderLoading();
    return;
  }
  const pkg = state.package;
  const current = selectedType();
  if (!current) {
    if (!state.atPackageRoot && !state.atLibraryRoot) {
      state.atLibraryRoot = true;
    }
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
  if (state.atLibraryRoot
    && !libraryLensesFor(pkg).some(([id]) => id === state.libraryLens)) {
    state.libraryLens = "overview";
  }
  if (!state.atPackageRoot
    && scope() === "type"
    && !availableTypeLenses().some(([id]) => id === state.lens)) {
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
  const currentMember = current ? selectedMember(current) : undefined;
  const currentMemberOverload = currentMember
    ? selectedConcreteOverload(
        currentMember.overloads,
        state.selectedOverloadIndex)
    : undefined;
  const currentMemberSourceSignature =
    sourcePageKind === "member" && current && currentMemberOverload
      ? memberRequestSignature(current, currentMemberOverload, false, true)
      : "";
  const currentTypeSourceSignature = current
    ? typeSourceSignature(
        current,
        currentPackage(),
        state.taste,
        memberRequestKey,
        state.typeSourceView)
    : "";
  const sourcePageMemberSource =
    sourcePageKind === "member"
      ? sourceResultForSignature(
          state.memberSource,
          currentMemberSourceSignature)
      : null;
  const selectedSourcePart = memberSourcePartSelector.current(
    currentMemberSourceSignature,
    sourcePageMemberSource);
  const typeCodeView = sourcePageKind === "type"
    ? sourceResultForSignature(state.typeSource, currentTypeSourceSignature)
    : null;
  const sourcePageSource =
    sourcePageKind === "member"
      ? sourcePageMemberSource?.source ?? null
      : typeCodeView?.kind === "source"
        ? typeCodeView.value
        : null;
  const sourceWorkingSurface =
    sourcePageKind !== null
    && (sourcePageSource !== null || typeCodeViewText(typeCodeView) !== null);
  const apiWorkingSurface =
    activeScope === "type" && state.lens === "api";
  const metadataWorkingSurface =
    activeScope === "type" && state.lens === "metadata";
  const overviewWorkingSurface =
    (activeScope === "package" && state.packageLens === "overview")
    || (activeScope === "library" && state.libraryLens === "overview");
  const packageDependenciesWorkingSurface =
    activeScope === "package" && state.packageLens === "dependencies";
  const libraryMetadataWorkingSurface =
    activeScope === "library" && state.libraryLens === "metadata";
  const compareWorkingSurface = currentCompareSubject() !== null;
  const libraryReferencesWorkingSurface =
    activeScope === "library" && state.libraryLens === "references";
  const libraryIntegrationsWorkingSurface =
    activeScope === "library" && state.libraryLens === "integrations";
  const libraryOpportunitiesWorkingSurface =
    libraryIntegrationsWorkingSurface && state.integrationMode === "opportunities";
  const libraryAnalysisWorkingSurface =
    activeScope === "library" && state.libraryLens === "analysis";
  const libraryMetricsWorkingSurface =
    activeScope === "library" && state.libraryLens === "metrics";
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
  const subjectPathLabel = subjectPath.map(segment =>
    segment.qualifier
      ? `${segment.label} · ${segment.qualifier}`
      : segment.label).join(" > ");
  const contentFrameEnabled = activeScope !== "workspace";
  const contentNavigationLabel =
    activeScope === "package"
      ? "Frameworks"
      : activeScope === "library" && state.rootKind === "library"
        ? "Types"
      : activeScope === "library" && state.rootKind !== "platform"
        ? "Libraries"
      : navMode() === "member" && current ? "Members" : "Types";
  const contentNavigationIntegrated =
    apiWorkingSurface
    || metadataWorkingSurface
    || overviewWorkingSurface
    || packageDependenciesWorkingSurface
    || compareWorkingSurface
    || libraryMetadataWorkingSurface
    || libraryReferencesWorkingSurface
    || libraryIntegrationsWorkingSurface
    || libraryOpportunitiesWorkingSurface
    || libraryAnalysisWorkingSurface
    || libraryMetricsWorkingSurface
    || memberWorkingSurface;

  if (scopeBarOwnsFocus) {
    app.tabIndex = -1;
    app.focus({ preventScroll: true });
  }
  const applicationModalOpen =
    state.settings || state.keyboardHelp || state.libraryOpen;
  app.innerHTML = `
    <div class="workbench"${state.memberAnnotatedModal || applicationModalOpen ? " inert" : ""}>
      ${workbenchShellHtml({
        contextualActionsHtml: !loadingPackageContent && (annotatedPageContext || sourcePageKind || callGraphPageContext || packageDependenciesWorkingSurface || metadataWorkingSurface)
          ? `<div class="working-surface-actions" role="group" aria-label="${metadataWorkingSurface ? "Type graph actions" : packageDependenciesWorkingSurface ? "Dependency graph actions" : callGraphPageContext ? "Call graph actions" : annotatedPageContext ? "Annotated Source actions" : sourcePageKind ? "Source actions" : "Member actions"}">
              ${metadataWorkingSurface
                ? `<button type="button" id="type-graph-explore" data-graph-explore${typeGraphAvailable() ? "" : " disabled"}>Explore</button>`
                : ""}
              ${packageDependenciesWorkingSurface
                ? `<button type="button" id="dependency-graph-explore" data-graph-explore${dependencyGraphAvailable() ? "" : " disabled"}>Explore</button>`
                : ""}
              ${callGraphPageContext
                ? `<button type="button" id="call-graph-explore" data-graph-explore${currentCallGraph() && !currentCallGraph()?.noBody ? "" : " disabled"}>Explore</button>`
                : ""}
              ${annotatedPageContext
                ? renderAnnotatedSourcePageActions(annotatedWorkingSurface)
                : ""}
              ${sourcePageKind
                ? renderSourcePageActions({
                    source: sourcePageSource,
                    typeCodeView,
                    typeView: state.typeSourceView,
                    memberSource: sourcePageMemberSource,
                    selectedMemberPart: selectedSourcePart,
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
        >
        ${renderNavPane(current, visible)}

        <section class="detail-pane${contentFrameEnabled
          ? contentNavigationIntegrated
            ? " content-navigation-integrated"
            : " content-navigation-separated"
          : ""}">
          ${contentFrameEnabled
            ? renderContentNavigationBar(contentNavigationLabel)
            : ""}
          <article id="inspector-panel" ${loadingPackageContent ? 'aria-busy="true"' : ""} class="detail-scroll${annotatedWorkingSurface ? " annotated-working-surface" : ""}${sourceWorkingSurface ? " source-working-surface" : ""}${apiWorkingSurface ? " api-working-surface" : ""}${metadataWorkingSurface ? " metadata-working-surface" : ""}${overviewWorkingSurface ? " overview-working-surface" : ""}${packageDependenciesWorkingSurface ? " package-dependencies-working-surface" : ""}${compareWorkingSurface ? " library-api-diff-working-surface" : ""}${libraryMetadataWorkingSurface ? " package-metadata-working-surface" : ""}${libraryReferencesWorkingSurface ? " library-references-working-surface" : ""}${libraryIntegrationsWorkingSurface ? " library-integrations-working-surface" : ""}${libraryOpportunitiesWorkingSurface ? " library-opportunities-working-surface" : ""}${libraryAnalysisWorkingSurface || libraryMetricsWorkingSurface ? " library-analysis-working-surface" : ""}${memberWorkingSurface ? " member-working-surface" : ""}">
            ${loadingPackageContent
              ? `<div id="package-content-loading" class="package-content-loading" role="status" tabindex="-1" data-package-loading-control="${packageContentLoadingFocusControl ?? (state.requestedVersion !== pkg.version ? "package-version" : "package-framework")}"${state.requestedVersion !== pkg.version ? "" : ` data-package-loading-framework="${escapeHtml(state.requestedFramework)}"`}><span class="loader" aria-hidden="true"></span><span>Loading ${state.requestedVersion !== pkg.version ? `version ${escapeHtml(state.requestedVersion)}` : escapeHtml(state.requestedFramework)} content…</span></div>`
              : renderLens(current)}
          </article>
        </section>
      </main>

      ${dataBarHtml({
        buildIdentity: state.buildIdentity,
        producer: {
          kind: pkg.source.kind === "platform"
            || state.rootKind === "library"
            ? "acquisition"
            : "package",
          label: pkg.producerLabel,
        },
      }, escapeHtml)}
      ${state.spotlightOpen ? spotlight.modalHtml() : ""}
      ${graphSourceIsOpen(state.graphSource) ? renderGraphSource() : ""}
      ${documentViewerIsOpen(state.docViewer) ? renderDocViewer() : ""}
    </div>
    ${renderApplicationMenu(state.rootKind !== "library")}
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp
      ? renderKeyboardHelpDialog(keyboardHelpBindings)
      : ""}
    ${renderLibraryOpenDialog({
      open: state.libraryOpen,
      busy: state.libraryOpenBusy,
      error: state.libraryOpenError,
    }, escapeHtml)}
    ${renderAnnotatedSourceModal()}`;

  for (const packageIcon of document.querySelectorAll<HTMLImageElement>("[data-package-icon]")) {
    packageIcon.onerror = () => {
      if (packageIcon.getAttribute("src") === NUGET_DEFAULT_PACKAGE_ICON) return;
      packageIcon.src = NUGET_DEFAULT_PACKAGE_ICON;
    };
  }
  bindEvents();
  bindLibraryOpenEvents();
  if (loadingPackageContent) {
    for (const region of document.querySelectorAll(
      ".subject-inspector-region, #subject-panel > aside, .content-navigation-bar")) {
      region.setAttribute("inert", "");
    }
  }
  if (state.settings) {
    focusSettingsEntry();
  } else if (state.keyboardHelp) {
    document.querySelector<HTMLElement>("#keyboard-help-title")
      ?.focus({ preventScroll: true });
  } else if (applicationMenuHadFocus) {
    focusApplicationMenuButton(document);
  } else if (workspaceFocus) {
    if (!restoreWorkspaceFocus(document, workspaceFocus)) {
      focusLevelOneHeading();
    }
  } else if (workbenchSearchHadFocus) {
    focusWorkbenchSearch(document);
  } else if (levelOneHeadingHadFocus) {
    focusLevelOneHeading();
  } else if (isIntegrationMode(integrationTabFocus)) {
    restoreIntegrationTabFocus(document, integrationTabFocus);
  } else if (isCompareMode(compareTabFocus)) {
    restoreCompareTabFocus(document, compareTabFocus);
  } else if (packageRetryHadFocus || packageControlHadFocus) {
    const packageFrameworkControl = () =>
      contentFrameMedia.matches
        ? document.querySelector<HTMLElement>("#content-navigation-toggle")
        : [...document.querySelectorAll<HTMLElement>("[data-package-framework]")]
            .find(button =>
              button.dataset.packageFramework === packageLoadingFramework);
    const packageControl = loadingPackageContent
      ? document.querySelector<HTMLElement>("#package-content-loading")
      : packageLoadingControl === "package-framework"
        ? packageFrameworkControl()
        : document.querySelector<HTMLElement>(`#${packageLoadingControl}`)
          ?? (packageLoadingControl === "framework"
            ? packageFrameworkControl()
            : null);
    packageControl?.focus({ preventScroll: true });
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
  restorePackageRouteReturnFocus();
  graphExplorer.afterRender(graphExplorerTarget());
  if (loadingPackageContent) return;
  recordNav();
  const productDemosRouteVisible =
    scope() === "workspace"
    && isProductHomeDemosPath(location.pathname);
  if (productDemosRouteVisible) {
    document.title = "Demos — dotnet-inspect";
  } else if (state.rootKind !== "library"
    && options.synchronizeUrl !== false) {
    syncUrl();
  }
  if (state.rootKind !== "library") {
    maybeAutoLoadVisibleSource();
    maybeAutoLoadTypeMetadata();
    maybeAutoLoadLibraryApi();
    maybeAutoLoadPackageDependencies();
    maybeAutoLoadPackageIntegrations();
    maybeAutoLoadPackageOpportunities();
    maybeAutoLoadPackagePerformance();
    maybeAutoLoadPackageLibraryMetrics();
    maybeAutoLoadPackageMetadata();
  }
  if (scope() === "member"
    && state.memberSection === "call-graph"
    && currentCallGraph()?.mermaid) {
    observeAsync(renderMermaidCallGraph(), "Rendering the member call graph");
  }
  if (state.memberAnnotatedModal?.relationshipPresentation === "Diagram") {
    observeAsync(
      renderAnnotatedRelationshipDiagram(),
      "Rendering Annotated Source relationships");
  }
}

function renderWorkspaceCatalogView() {
  document.title = `${isProductHomeDemosPath(location.pathname) ? "Demos" : "Workspace"} — dotnet-inspect`;
  const subjectPath: readonly SubjectPathSegment[] = [{
    kind: "workspace",
    label: "Workspace",
    copyable: false,
  }];
  app.innerHTML = `
    <div class="workbench"${state.settings || state.keyboardHelp || state.libraryOpen ? " inert" : ""}>
      ${workbenchShellHtml({
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
      <main id="subject-panel" class="workspace">
        ${renderWorkspaceNavPane()}
        <section class="detail-pane">
          <article id="inspector-panel" class="detail-scroll">
            ${renderWorkspaceView()}
          </article>
        </section>
      </main>
      ${dataBarHtml({
        buildIdentity: state.buildIdentity,
      }, escapeHtml)}
      ${state.spotlightOpen ? spotlight.modalHtml() : ""}
    </div>
    ${renderApplicationMenu(false)}
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp
      ? renderKeyboardHelpDialog(keyboardHelpBindings)
      : ""}
    ${renderLibraryOpenDialog({
      open: state.libraryOpen,
      busy: state.libraryOpenBusy,
      error: state.libraryOpenError,
    }, escapeHtml)}`;
  bindScopeBarEvents();
  bindWorkspaceSubjectEvents();
  bindSettingsPanelEvents();
  workbenchShellBinding =
    bindWorkbenchShell(document, workbenchShellActions);
  bindLibraryOpenEvents();
  if (state.spotlightOpen) spotlight.bind(document, "modal");
}

function maybeAutoLoadVisibleSource() {
  const kind = currentSourceOperationKind();
  if (kind === "graph") {
    const target = graphSourceAutoLoadRequest(state.graphSource);
    if (target) {
      observeAsync(
        openGraphSource(
          target.request,
          target.title),
        "Loading graph source");
    }
    return;
  }
  const type = selectedType();
  if (!type) return;
  const pkg = currentPackage();
  if (kind === "type") {
    const signature = typeSourceSignature(type, pkg, state.taste, memberRequestKey, state.typeSourceView);
    if (sourceResultNeedsLoad(state.typeSource, signature)) {
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
    if (sourceResultNeedsLoad(state.memberSource, signature)) {
      observeAsync(loadSelectedMemberSource(), "Loading member source");
    }
  }
}

function maybeAutoLoadTypeMetadata() {
  if (state.lens !== "metadata") return;
  const type = selectedType();
  if (!type) return;
  const signature = currentTypeMetadataSignature(
    type,
    currentPackage(),
    selectedTypeMetadataLibraryIdentity());
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
  if (scope() === "package") {
    const pkg = currentPackage();
    return renderPackageNav({
      frameworks: pkg.frameworks,
      activeFramework: pkg.activeFramework,
      escapeHtml,
    });
  }
  if (scope() === "library" && state.rootKind === "library") {
    return renderTypeNavPane(current, visible);
  }
  if (scope() === "library" && state.rootKind !== "platform") {
    return renderLibrarySubjectNav({
      libraries: packageLibraries(),
      selectedLibraryId: state.libraryScope?.size === 1
        ? state.libraryScope.values().next().value ?? null
        : null,
      escapeHtml,
    });
  }
  return navMode() === "member" && current
    ? renderMemberNavPane(current)
    : renderTypeNavPane(current, visible);
}

type SubjectPathKind =
  | "workspace"
  | "package"
  | "platform"
  | "library"
  | "type"
  | "member";

interface SubjectPathSegment {
  kind: SubjectPathKind;
  label: string;
  copyable: boolean;
  qualifier?: string;
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
  const path: SubjectPathSegment[] = rootIsPresented()
    ? [{
        kind: state.rootKind,
        label: state.rootKind === "platform"
          ? platformTargetLabel()
          : state.rootKind === "library"
            ? activeLibrarySubjectName()
            : packageDisplayName(pkg),
        copyable: true,
      }]
    : [];
  if (state.atPackageRoot
    || (state.rootKind === "library" && state.atLibraryRoot)) return path;
  const library = activeLibrarySubjectName();
  if (library && state.rootKind !== "library") {
    path.push({
      kind: "library",
      label: library,
      copyable: true,
    });
  }
  if (state.atLibraryRoot || !current) return path;
  const definingLibrary = typeDefiningLibraryLabel(current);
  path.push({
    kind: "type",
    label: current.namespace
      ? `${current.namespace}.${typeDisplayName(current)}`
      : typeDisplayName(current),
    copyable: true,
    ...(definingLibrary
      ? { qualifier: definingLibrary }
      : {}),
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
  if (scope() === "platform") return [{ kind: "platform", label: platformTargetLabel(), copyable: true }];
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
    const qualifier = segment.qualifier
      ? `<span class="subject-path-qualifier" aria-label="Defining Library ${escapeHtml(segment.qualifier)}">· ${escapeHtml(segment.qualifier)}</span>`
      : "";
    return `${separator}${content}${qualifier}`;
  }).join("");
}

function renderWorkspaceNavPane() {
  return renderWorkspaceSubject({
    workspaces: retainedWorkspaceItems(),
    escapeHtml,
  });
}

function hasPlatformRootHistoryView() {
  return historyHasPlatformRootParent(history.state);
}

function platformIsPresentedAsRoot() {
  return state.platformSelection !== null
    && (state.platformPresentedAsRoot
    || (pendingWorkspaceConstruction === null
      && hasPlatformRootHistoryView()));
}

function rootIsPresented() {
  return state.rootKind !== "platform"
    || platformIsPresentedAsRoot();
}

function currentViewHasPlatformRootParent() {
  return pendingWorkspaceConstruction === null
    && hasPlatformRootHistoryView();
}

function navigationSnapshotHasPlatformRootParent(
  snapshot: NavigationHistorySnapshot<WorkspaceView>,
) {
  return snapshot.stack[snapshot.index]?.view.platformRootParent === true;
}

function renderTypeNavPane(
  current: AppTypeSurface | null | undefined,
  visible: readonly AppTypeSurface[],
) {
  const definingLibraries = aggregateTypeLibraryLabels();
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
    library: activeLibrarySubjectName(),
    parentSubject: state.atLibraryRoot
      ? state.rootKind === "platform" && !currentViewHasPlatformRootParent()
        ? null
        : state.rootKind
      : "library",
    filtersExpanded: state.typeFiltersExpanded,
    filterSummary: typeFilterSummary(),
    escapeHtml,
    typeDisplayName,
    typeLibraryLabel: item => definingLibraries.get(item.id) ?? "",
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

function renderScopeBar(
  availableScopes?: readonly WorkspaceScope[],
) {
  const sc = scope();
  const selected = selectedType();
  const rootScopes: readonly WorkspaceScope[] =
    state.rootKind === "platform"
      && !currentViewHasPlatformRootParent()
      ? []
      : [state.rootKind];
  const libraryScopeAvailable = state.rootKind !== "platform"
    ? state.rootKind !== "library" && aggregateLibrarySubjectIsAvailable()
    : Boolean(selectedLibrary());
  availableScopes ??= [
    ...rootScopes,
    ...(libraryScopeAvailable ? ["library" as const] : []),
    ...(selected ? ["type" as const] : []),
    ...(selected && memberGroups(selected).length ? ["member" as const] : []),
  ];
  const showMemberScope =
    !state.atPackageRoot
    && !state.atLibraryRoot
    && Boolean(selected && memberGroups(selected).length);
  if (sc === "workspace" || sc === "platform") {
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
      strip: packageLensesFor(state.package),
      activeStripId: state.packageLens,
      availableScopes,
      stripAttribute: "data-package-lens",
      panelId: "inspector-panel",
      showMemberScope,
      escapeHtml,
    });
  }
  if (sc === "library") {
    return renderScopeBarPure({
      scope: sc,
      strip: libraryLensesFor(state.package),
      activeStripId: state.libraryLens,
      availableScopes,
      stripAttribute: "data-library-lens",
      panelId: "inspector-panel",
      showMemberScope,
      escapeHtml,
    });
  }
  if (sc === "member") {
    const member = selectedMember(selected);
    return renderScopeBarPure({
      scope: sc,
      strip: member ? memberSectionsFor(member) : [],
      activeStripId: state.memberSection,
      availableScopes,
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
      strip: availableTypeLenses(),
      activeStripId: state.lens,
      availableScopes,
      stripAttribute: "data-lens",
      panelId: "inspector-panel",
      showMemberScope,
      escapeHtml,
    });
  }
  // A new scope used to render silently as the type strip.
  return assertNever(sc, "workspace scope");
}

function packageVersionField() {
  if (state.rootKind !== "package") return "";
  const pkg = currentPackage();
  return `<label class="version-select">
    <span>Version</span>
    <select id="package-version">
      ${versionOptionsHtml(pkg)}
    </select>
  </label>`;
}

function renderPackageView() {
  return packageLensBody();
}

function libraryIdentity(library: NonNullable<ReturnType<typeof selectedLibrary>>) {
  return `${library.name}, Version=${library.version}, Culture=${library.culture || "neutral"}, PublicKeyToken=${library.publicKeyToken || "null"}`;
}

function libraryHeading() {
  const library = selectedLibrary();
  if (!library) return "";
  return `<header class="type-heading">
    <div class="type-badge">◫</div>
    <div>
      <div class="type-namespace">${escapeHtml(library.asset || "Managed library")}</div>
      <h1>${escapeHtml(library.name)}</h1>
      <code class="type-signature">${escapeHtml(libraryIdentity(library))}</code>
    </div>
    <div class="type-metrics"><span><strong>${library.types}</strong> types</span><span><strong>${library.members.toLocaleString()}</strong> members</span></div>
  </header>`;
}

function renderLibraryView() {
  const body = libraryLensBody();
  if (state.libraryLens === "overview"
    || state.libraryLens === "compare"
    || state.libraryLens === "references"
    || state.libraryLens === "integrations"
    || state.libraryLens === "analysis"
    || state.libraryLens === "metrics"
    || state.libraryLens === "metadata") return body;
  return `${libraryHeading()}${body}`;
}

function renderWorkspaceView() {
  if (state.packages.some(item => !item.isRuntimePack)) ensureWorkspaceOccurrenceView();
  const presentPlatform = platformIsPresentedAsRoot();
  const frameworkPackage = !presentPlatform && state.platformSelection
    ? runtimePackageForTarget(state.platformSelection)
    : null;
  const currentFrameworkLibrary =
    frameworkPackage && state.package === frameworkPackage
      ? selectedLibrary()
      : null;
  const rememberedFrameworkLibrary =
    frameworkPackage
    && state.frameworkLibraryPresentation?.tfm === frameworkPackage.activeFramework
    && state.frameworkLibraryPresentation.version === frameworkPackage.version
      ? resolvePackageLibrary(
          frameworkPackage.assemblies,
          state.frameworkLibraryPresentation.libraryId)
      : null;
  const frameworkLibrary = currentFrameworkLibrary
    ?? rememberedFrameworkLibrary
    ?? (frameworkPackage
      ? resolvePackageLibrary(
          frameworkPackage.assemblies,
          frameworkPackage.assemblyId)
      : null);
  return renderWorkspaceViewPure({
    savedWorkspaces: {
      state: savedWorkspaces.state,
      canSave: state.engineReady
        && !state.loading
        && !state.error
        && (state.packages.length > 0
          || state.platformSelection !== null
          || activeRetainedWorkspacePosting !== null),
      canOpen: state.engineReady && !state.loading && !state.error,
    },
    occurrences: state.workspaceOccurrences?.occurrences ?? [],
    ...(retainedWorkspacePresentation
      ? {
          navigationPackages: retainedWorkspacePresentation.packages,
          navigationPlatforms: retainedWorkspacePresentation.platforms,
        }
      : {
          canAddPackage: state.engineReady && !state.loading && !state.error,
        }),
    packages: state.packages,
    platform: presentPlatform ? state.platformSelection : null,
    frameworkLibraries: frameworkLibrary ? [{
      name: frameworkLibrary.name,
      assembly: frameworkLibrary.name,
      pack: frameworkLibrary.platformPack ?? "",
      version: frameworkPackage?.version ?? "",
      framework: frameworkPackage?.activeFramework ?? "",
      source: frameworkLibrary.platformPack === "aspnetcore.app"
        ? "ASP.NET Core"
        : ".NET",
    }] : [],
    loading: state.workspaceOccurrenceLoading,
    error: state.workspaceOccurrenceError,
    escapeHtml,
  });
}

function packageLensBody() {
  switch (state.packageLens) {
    case "overview": return renderPackageOverview();
    case "dependencies": return renderPackageDependencies();
  }
  // `packageLenses` drives the rendered strip directly, so a new catalog entry is offered
  // to users the moment it is added. It used to fall through to a "not available"
  // placeholder here, which is indistinguishable from a lens that is wired but empty.
  return assertNever(state.packageLens, "package lens");
}

function libraryLensBody() {
  if (aggregateLibrarySubjectIsActive()
    && libraryLensRequiresExactLibrary(state.libraryLens)) {
    const label = libraryLenses.find(([id]) => id === state.libraryLens)?.[1]
      ?? "This inspector";
    return `<section class="document-section empty-document">
      <span class="large-glyph">◇</span>
      <h2>${escapeHtml(label)} requires one Library</h2>
      <p>Choose an exact Library from the Libraries navigation to use this inspector.</p>
    </section>`;
  }
  switch (state.libraryLens) {
    case "overview": return renderLibraryOverview();
    case "compare": return renderCompareSurface();
    case "references": return renderLibraryReferences();
    case "integrations": return state.integrationMode === "opportunities"
      ? renderPackageOpportunities()
      : renderPackageIntegrations();
    case "analysis": return renderPackagePerformance();
    case "metrics": return renderPackageLibraryMetrics();
    case "metadata": return renderPackageMetadata();
    default: return assertNever(state.libraryLens, "library lens");
  }
}

function packageDependenciesSignature() {
  const pkg = currentPackage();
  const library = selectedLibraryShareKey();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}#${library || pkg.assemblyId}`;
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
      <div class="package-coordinate-fields">${packageVersionField()}</div>
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
  const completion = data.declarationFailures.length > 0
    ? " · incomplete"
    : "";
  return `${dependencyCount} package${
    dependencyCount === 1 ? "" : "s"
  }${completion}`;
}

const DEPENDENCY_GRAPH_SUMMARY =
  "callers above · dependencies below · click a package to open";

const PACKAGE_PRUNING_FAMILIES = [
  { value: "Microsoft.NETCore.App", label: ".NET Runtime" },
  { value: "Microsoft.AspNetCore.App", label: "ASP.NET Core" },
] as const;

function packageDeclarationFailureReason(
  failure: BrowserPackageDependencies["declarationFailures"][number],
) {
  const framework = failure.framework
    ? ` in ${failure.framework}`
    : "";
  const occurrenceCount = failure.sourceOccurrenceCount;
  const occurrences = occurrenceCount
    ? ` (${occurrenceCount} source ${
      occurrenceCount === 1 ? "occurrence" : "occurrences"
    })`
    : "";
  if (failure.kind === "ConflictingPackageDeclaration") {
    return `Conflicting declarations for ${
      failure.package || "one package"
    }${framework} were omitted${occurrences}.`;
  }
  if (failure.kind === "InvalidPackageDeclaration") {
    return `An invalid dependency declaration${framework} was omitted${occurrences}.`;
  }
  return `A dependency declaration could not be normalized (${failure.kind}).`;
}

function renderPackageDeclarationFailures(
  failures: BrowserPackageDependencies["declarationFailures"],
) {
  if (!failures.length) return "";
  return `<div class="package-pruning-status package-pruning-error">
    <strong>Dependency declarations incomplete</strong>
    <ul>${failures.map(failure =>
      `<li>${escapeHtml(packageDeclarationFailureReason(failure))}</li>`)
      .join("")}</ul>
  </div>`;
}

function packagePruningSignature(family = state.packagePruningFamily) {
  const pkg = currentPackage();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}#${family}`;
}

function packagePruningReason(row: BrowserPackagePruningResult["rows"][number]) {
  if (row.disposition === "PlatformDelegation") {
    return "The platform supplies the candidate version or a newer one.";
  }
  if (row.disposition === "PackageRetained") {
    return row.platformSuppliedVersion
      ? "The selected candidate is newer than the platform-supplied version."
      : "The platform does not supply this package.";
  }
  if (row.disposition === "CandidateUnavailable") {
    return `Candidate resolution did not complete (${row.reason}).`;
  }
  return `Pruning was not evaluated (${row.reason}).`;
}

function packagePruningDisposition(
  row: BrowserPackagePruningResult["rows"][number],
) {
  switch (row.disposition) {
    case "PlatformDelegation": return "Use platform";
    case "PackageRetained": return "Keep package";
    case "CandidateUnavailable": return "Candidate unavailable";
    case "NotEvaluated": return "Not evaluated";
    default: return "Not evaluated";
  }
}

function renderPackagePruningSection(
  data: BrowserPackageDependencies,
): string {
  const pkg = currentPackage();
  const exactPlatformFramework =
    isExactPlatformPruningFramework(pkg.activeFramework);
  const activeGroup = data.dependencyGroups.find(group => group.isActive);
  if (pkg.isRuntimePack
    || !exactPlatformFramework
    || !activeGroup?.dependencies.length) {
    return "";
  }

  const signature = packagePruningSignature();
  const fresh = state.packagePruningKey === signature;
  const result = fresh ? state.packagePruning : null;
  const loading = fresh && state.packagePruningLoading;
  const error = fresh ? state.packagePruningError : "";
  const familyOptions = PACKAGE_PRUNING_FAMILIES.map(family =>
    `<option value="${family.value}"${family.value === state.packagePruningFamily ? " selected" : ""}>${family.label}</option>`)
    .join("");
  const resultRows = result?.rows.map(row => `
      <tr>
        <td><code>${escapeHtml(row.package)}</code><small>${escapeHtml(row.requestedRange || "*")}</small></td>
        <td>${row.candidateVersion ? `<code>${escapeHtml(row.candidateVersion)}</code>` : "—"}</td>
        <td>${row.platformSuppliedVersion ? `<code>${escapeHtml(row.platformSuppliedVersion)}</code>` : "—"}</td>
        <td><strong>${escapeHtml(packagePruningDisposition(row))}</strong><small>${escapeHtml(packagePruningReason(row))}</small></td>
      </tr>`)
    .join("") ?? "";
  const declarationFailures = result
    ? renderPackageDeclarationFailures(result.declarationFailures)
    : "";
  const basis = result
    ? `Evaluated active group <code>${escapeHtml(
      result.selectedFramework || activeGroup.framework,
    )}</code> against <code>${escapeHtml(
      result.family,
    )}</code> at <code>${escapeHtml(
      `${result.targetFramework}@${result.platformVersion}`,
    )}</code>.`
    : `Evaluation uses the active normalized group <code>${escapeHtml(
      activeGroup.framework,
    )}</code> and the selected platform family; choosing another displayed group does not change that input. Candidate version discovery may query nuget.org.`;
  const resultHtml = loading
    ? `<div class="package-pruning-status source-progress"><span class="loader"></span><span>Resolving dependency candidates…</span></div>`
    : error
      ? `<div class="package-pruning-status package-pruning-error"><strong>Pruning query failed</strong><span>${escapeHtml(error)}</span></div>`
      : result
        ? result.message
          ? `<div class="package-pruning-status"><span>${escapeHtml(result.message)}</span></div>${declarationFailures}`
          : `<div class="package-pruning-result">
              <p><strong>${result.summary.delegated}</strong> platform · <strong>${result.summary.retained}</strong> package · <strong>${result.summary.notEvaluated}</strong> not evaluated · <strong>${result.summary.failed}</strong> unresolved · <strong>${result.summary.declarationFailures}</strong> declaration failures</p>
              <div class="package-pruning-table-scroll">
                <table class="package-pruning-table">
                  <thead><tr><th>Dependency</th><th>Candidate</th><th>Supplied</th><th>Disposition</th></tr></thead>
                  <tbody>${resultRows}</tbody>
                </table>
              </div>
              ${declarationFailures}
            </div>`
        : "";

  return `
    <section class="document-section package-pruning-section">
      <div class="section-title"><h2>Platform pruning</h2><span>explicit evaluation · no graph changes</span></div>
      <div class="package-pruning-controls">
        <label>Platform family
          <select data-pruning-family${loading ? " disabled" : ""}>${familyOptions}</select>
        </label>
        <button type="button" data-pruning-evaluate${loading ? " disabled" : ""}>${result || error ? "Evaluate again" : "Evaluate"}</button>
      </div>
      <p class="package-pruning-intro">${basis}</p>
      ${resultHtml}
    </section>`;
}

function renderPackageDependencies() {
  const current = packageDependenciesSignature();
  const fresh = state.packageDependenciesKey === current;
  if (state.packageDependenciesLoading && fresh) {
    return renderPackageDependenciesSurface(
      `<section data-dependency-graph-surface class="document-section package-dependencies-state source-progress"><span class="loader"></span><h2>Reading dependencies…</h2><p>Parsing the package manifest.</p></section>`,
      "reading");
  }
  if (fresh && state.packageDependenciesError) {
    return renderPackageDependenciesSurface(
      `<section data-dependency-graph-surface class="document-section package-dependencies-state empty-document"><span class="large-glyph">⌘</span><h2>Dependency query failed</h2><p>${escapeHtml(state.packageDependenciesError)}</p></section>`,
      "query failed");
  }
  const data = fresh ? state.packageDependencies : null;
  if (!data) {
    return renderPackageDependenciesSurface(
      `<section data-dependency-graph-surface class="document-section package-dependencies-state empty-document"><span class="loader"></span><h2>Loading…</h2></section>`,
      "loading");
  }

  const groups = data.dependencyGroups || [];
  const dependencyGroupError = dependencyGroupSelectionMessage(data);
  const dependencyGroupNotice = dependencyGroupError
    ? `<section class="document-section empty-document"><span class="large-glyph">△</span><h2>No exact dependency group</h2><p>${escapeHtml(dependencyGroupError)}</p></section>`
    : "";
  const declarationFailureNotice =
    renderPackageDeclarationFailures(data.declarationFailures);
  if (!groups.length) {
    const emptyMessage = data.declarationFailures.length
      ? "No dependency rows could be normalized from the package manifest."
      : "The manifest declares no NuGet dependencies — a self-contained package.";
    return renderPackageDependenciesSurface(
      `<div data-dependency-graph-surface>${dependencyGroupNotice}${declarationFailureNotice}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No package dependencies</h2><p>${escapeHtml(emptyMessage)}</p></section></div>`,
      packageDependenciesStatus(data, null));
  }

  const selectedGroupIndex = resolveDependenciesGroupIndex(groups);
  const orderedGroups = groups;
  const selectorChips = orderedGroups
    .map(group => `<button class="type-chip ${group.index === selectedGroupIndex ? "active" : ""}" data-dep-group="${group.index}" aria-pressed="${group.index === selectedGroupIndex}">${escapeHtml(group.framework)}</button>`)
    .join("");
  const selector = `
    <section class="document-section dependency-group-selector">
      <div class="section-title"><h2>Target frameworks</h2><span>one framework at a time</span></div>
      <div class="type-chip-list" id="dep-tfm-chips">${selectorChips}</div>
    </section>`;

  const depList = dependencyListSectionHtml(groups, selectedGroupIndex);

  const graphSection = `
    <section class="document-section dependency-graph-section">
      <div class="section-title"><h2>Dependency graph</h2><span>${DEPENDENCY_GRAPH_SUMMARY}</span></div>
      ${workspaceDependencyErrorHtml()}
      <div id="dependency-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
      ${dependencyGraphLegendHtml()}
    </section>`;
  const pruningSection = renderPackagePruningSection(data);

  return renderPackageDependenciesSurface(
    `<div data-dependency-graph-surface>${dependencyGroupNotice}${declarationFailureNotice}${selector}${graphSection}</div>${pruningSection}${depList}`,
    packageDependenciesStatus(data, selectedGroupIndex));
}

function renderLibraryReferences() {
  const current = packageDependenciesSignature();
  const fresh = state.packageDependenciesKey === current;
  const library = selectedLibrary();
  const pkg = currentPackage();
  return renderLibraryReferencesSurface({
    assemblyIdentity: library ? libraryIdentity(library) : "No library selected",
    assetPath: library?.asset ?? "",
    coordinate: `${pkg.activeFramework} · ${pkg.id}@${pkg.version}`,
    loading: state.packageDependenciesLoading && fresh,
    error: fresh ? state.packageDependenciesError : "",
    data: fresh ? state.packageDependencies : null,
    escapeHtml,
  });
}

async function uniqueCompatiblePackage(
  packages: readonly AppPackage[],
  packageId: string,
  declaredRange: string | null | undefined,
) {
  const match = await engineClient.package.matchPackageDependencyCoordinate(
    packageId,
    declaredRange ?? null,
    dependencyCoordinateCandidates(packages));
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
  matches?: readonly (AppPackage | null)[],
) {
  const group = groups.find(candidate => candidate.index === selectedGroupIndex) || groups[0];
  if (!group) throw new Error("Cannot render a dependency list without a dependency group.");
  const deps = group.dependencies || [];
  return `
    <section class="document-section" id="dep-list-section" data-dependency-match-state="${matches || !deps.length ? "ready" : "pending"}" aria-busy="${!matches && deps.length > 0}">
      <div class="section-title"><h2>NuGet dependencies</h2><span>${escapeHtml(group.framework)} · ${deps.length} package${deps.length === 1 ? "" : "s"}</span></div>
      ${deps.length
        ? `<ul class="dep-list">${deps.map((dependency, index) => {
            const open = matches?.[index];
            const attrs = !matches
              ? 'disabled title="Matching open packages…"'
              : open
              ? `data-dep-open="${escapeHtml(packageIdentityKey(open))}" title="Switch to ${escapeHtml(dependency.id)}"`
              : `data-dep-load="${escapeHtml(dependency.id)}" data-dep-version="${escapeHtml(dependency.versionRange || "")}" title="Open ${escapeHtml(dependency.id)} in a new tab"`;
            return `<li><button class="dep-name as-link${open ? " is-open" : ""}" ${attrs}>${escapeHtml(dependency.id)}</button><code class="dep-version">${escapeHtml(dependency.versionRange || "*")}</code></li>`;
          }).join("")}</ul>`
        : `<div class="empty-list">No package dependencies declared for ${escapeHtml(group.framework)}.</div>`}
    </section>`;
}

async function renderPackageDependencyList() {
  const container = document.querySelector<HTMLElement>("#dep-list-section");
  if (!container || container.dataset.dependencyMatchState !== "pending") return;
  const data = state.packageDependencies;
  const groups = data?.dependencyGroups || [];
  const selectedGroupIndex = resolveDependenciesGroupIndex(groups);
  const group = groups.find(candidate => candidate.index === selectedGroupIndex) || groups[0];
  if (!group) throw new Error("Cannot match dependencies without a dependency group.");
  const packages = state.packages.map(pkg => ({ ...pkg }));
  const coordinates = JSON.stringify(dependencyCoordinateCandidates(packages));
  const isCurrent = () =>
    document.querySelector("#dep-list-section") === container
    && state.packageDependencies === data
    && resolveDependenciesGroupIndex(groups) === selectedGroupIndex
    && JSON.stringify(dependencyCoordinateCandidates(state.packages)) === coordinates;
  container.dataset.dependencyMatchState = "loading";
  try {
    const matches = await Promise.all((group.dependencies || []).map(dependency =>
      uniqueCompatiblePackage(packages, dependency.id, dependency.versionRange)));
    if (!isCurrent()) return;
    container.outerHTML = dependencyListSectionHtml(groups, selectedGroupIndex, matches);
    bindPackageDependencyListEvents();
  } catch (error) {
    if (!isCurrent()) return;
    container.dataset.dependencyMatchState = "failed";
    container.setAttribute("aria-busy", "false");
    container.insertAdjacentHTML("beforeend",
      `<p class="graph-render-error" role="alert">Dependency matching failed: ${escapeHtml(errorMessage(error))}</p>`);
  }
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
  document.querySelectorAll<HTMLElement>("#dep-tfm-chips [data-dep-group]").forEach(button => {
    const selected = Number(button.dataset.depGroup) === selectedGroupIndex;
    button.classList.toggle("active", selected);
    button.setAttribute("aria-pressed", String(selected));
  });
  status.textContent = packageDependenciesStatus(data, selectedGroupIndex);
  listSection.outerHTML = dependencyListSectionHtml(groups, selectedGroupIndex);
  bindPackageDependencyListEvents();
  observeAsync(renderPackageDependencyList(), "Matching dependency packages");
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
  queryPruning: async (packageModel, family) => {
    const target = await ensurePlatformCatalog(packageModel.activeFramework);
    return await inspectPackagePruning(
      packageModel.id,
      packageModel.version,
      packageModel.activeFramework,
      {
        schemaVersion: 1,
        family,
        targetFramework: target.tfm,
        platformVersion: target.version,
        supplies: requirePlatformPackageSupplies(target),
      });
  },
  queryPackageIntegrations: (packageModel, library) => inspectPackageIntegrations(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework,
    library),
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
  queryPackageOpportunities: (packageModel, library) => inspectPackageOpportunities(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework,
    library),
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
  queryPackagePerformance: (packageModel, library) => inspectPackagePerformance(
    packageModel.id,
    packageModel.version,
    packageModel.activeFramework,
    library),
  queryPackageLibraryMetrics: (packageModel, library) =>
    inspectPackageLibraryMetrics(
      packageModel.id,
      packageModel.version,
      packageModel.activeFramework,
      library),
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
  queryPlatformLibraryMetrics: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
  ) =>
    inspectPlatformLibraryMetrics(
      framework,
      platformVersion,
      assemblyFileName,
      pack),
  queryPackageMetadata: (packageModel, library) =>
    inspectPackageMetadata(
      packageModel.id,
      packageModel.version,
      packageModel.activeFramework,
      library),
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
  platformLibraryCoordinates: (pkg, key) => {
    const row = platformLibraryForRequest(pkg, key);
    return { assemblyFileName: platformAssemblyRequest(row), pack: row.pack };
  },
  describeError: errorMessage,
  refreshPackageStats,
  render: renderPreservingMemberFocus,
  renderDependencyGraph,
});

async function loadPackageDependencies() {
  const pkg = currentPackage();
  const library = selectedLibrary();
  return packageInspection.loadDependencies(
    {
      ...pkg,
      assemblyId: library?.id ?? pkg.assemblyId,
    },
    packageDependenciesSignature());
}

function maybeAutoLoadPackageDependencies() {
  const packageDependencies =
    state.atPackageRoot && state.packageLens === "dependencies";
  const libraryReferences =
    state.atLibraryRoot && state.libraryLens === "references";
  if (!packageDependencies && !libraryReferences) return;
  if (libraryReferences && aggregateLibrarySubjectIsActive()) return;
  if (state.packageDependenciesKey === packageDependenciesSignature()) {
    if (packageDependencies && state.packageDependencies) {
      observeAsync(renderPackageDependencyList(), "Matching dependency packages");
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
  const lib = selectedLibraryShareKey();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

function renderPackageIntegrations() {
  const pkg = currentPackage();
  const library = selectedLibrary();
  const scopedLib = scopedPlatformLibrary();
  const current = packageIntegrationsSignature();
  const fresh = state.packageIntegrationsKey === current;
  return renderLibraryIntegrationsSurface({
    libraryName: library?.name ?? "",
    assemblyIdentity: library ? libraryIdentity(library) : "No library selected",
    assetPath: library?.asset ?? "",
    coordinate: `${pkg.activeFramework} · ${pkg.id}@${pkg.version}`,
    requireLibrary: pkg.isRuntimePack && !scopedLib,
    pickerHtml: pkg.isRuntimePack && state.rootKind !== "platform"
      ? platformLibrarySelectHtml({ dataAttr: "data-platform-integrations-library", selected: scopedLib || "" })
      : "",
    loading: state.packageIntegrationsLoading && fresh,
    error: fresh ? state.packageIntegrationsError : "",
    data: fresh ? state.packageIntegrations : null,
    escapeHtml,
  });
}

async function loadPackageIntegrations() {
  const pkg = currentPackage();
  const scopedLib = selectedLibraryRequest() || null;
  return packageInspection.loadIntegrations(
    pkg,
    packageIntegrationsSignature(),
    scopedLib);
}

function maybeAutoLoadPackageIntegrations() {
  if (!state.atLibraryRoot || state.libraryLens !== "integrations") return;
  if (aggregateLibrarySubjectIsActive()) return;
  if (state.integrationMode !== "integrations") return;
  if (state.packageIntegrationsKey === packageIntegrationsSignature()) return;
  observeAsync(loadPackageIntegrations(), "Loading package integrations");
}

function packageScopeSignature() {
  const pkg = currentPackage();
  const lib = selectedLibraryShareKey();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

function renderPackageOpportunities() {
  const pkg = currentPackage();
  const library = selectedLibrary();
  const scopedLib = scopedPlatformLibrary();
  const current = packageScopeSignature();
  return renderPackageOpportunitiesPure({
    libraryName: library?.name ?? "",
    assemblyIdentity: library ? libraryIdentity(library) : "No library selected",
    assetPath: library?.asset ?? "",
    coordinate: `${pkg.activeFramework} · ${pkg.id}@${pkg.version}`,
    requireLibrary: pkg.isRuntimePack && !scopedLib,
    pickerHtml: pkg.isRuntimePack
      ? platformLibrarySelectHtml({ dataAttr: "data-platform-integrations-library", selected: scopedLib || "" })
      : "",
    fresh: state.packageOpportunitiesKey === current,
    loading: state.packageOpportunitiesLoading,
    error: state.packageOpportunitiesError,
    data: state.packageOpportunities,
    escapeHtml,
  });
}

async function loadPackageOpportunities() {
  const pkg = currentPackage();
  const scopedLib = selectedLibraryRequest() || null;
  return packageInspection.loadOpportunities(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackageOpportunities() {
  if (!state.atLibraryRoot || state.libraryLens !== "integrations") return;
  if (aggregateLibrarySubjectIsActive()) return;
  if (state.integrationMode !== "opportunities") return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageOpportunitiesKey === packageScopeSignature()) return;
  observeAsync(loadPackageOpportunities(), "Loading package opportunities");
}

function renderPackagePerformance() {
  const pkg = currentPackage();
  const library = selectedLibrary();
  const scopedLib = scopedPlatformLibrary();
  const current = packageScopeSignature();
  return renderLibraryAnalysisSurface({
    libraryName: library?.name ?? "",
    assemblyIdentity: library ? libraryIdentity(library) : "No library selected",
    assetPath: library?.asset ?? "",
    coordinate: `${pkg.activeFramework} · ${pkg.id}@${pkg.version}`,
    requireLibrary: pkg.isRuntimePack && !scopedLib,
    pickerHtml: pkg.isRuntimePack
      ? platformLibrarySelectHtml({
          dataAttr: "data-platform-analysis-library",
          selected: scopedLib || "",
        })
      : "",
    fresh: state.packagePerformanceKey === current,
    loading: state.packagePerformanceLoading,
    error: state.packagePerformanceError,
    data: state.packagePerformance,
    escapeHtml,
  });
}

function renderPackageLibraryMetrics() {
  const pkg = currentPackage();
  const library = selectedLibrary();
  const scopedLib = scopedPlatformLibrary();
  const current = packageScopeSignature();
  return renderLibraryMetricsSurface({
    libraryName: library?.name ?? "",
    assemblyIdentity: library ? libraryIdentity(library) : "No library selected",
    assetPath: library?.asset ?? "",
    coordinate: `${pkg.activeFramework} · ${pkg.id}@${pkg.version}`,
    requireLibrary: pkg.isRuntimePack && !scopedLib,
    pickerHtml: pkg.isRuntimePack
      ? platformLibrarySelectHtml({
          dataAttr: "data-platform-metrics-library",
          selected: scopedLib || "",
        })
      : "",
    fresh: state.packageLibraryMetricsKey === current,
    loading: state.packageLibraryMetricsLoading,
    error: state.packageLibraryMetricsError,
    data: state.packageLibraryMetrics,
    escapeHtml,
  });
}

async function loadPackagePerformance() {
  const pkg = currentPackage();
  const scopedLib = selectedLibraryRequest() || null;
  return packageInspection.loadPerformance(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackagePerformance() {
  if (!state.atLibraryRoot || state.libraryLens !== "analysis") return;
  if (aggregateLibrarySubjectIsActive()) return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packagePerformanceKey === packageScopeSignature()) return;
  observeAsync(loadPackagePerformance(), "Loading package analysis");
}

function loadPackageLibraryMetrics() {
  const pkg = currentPackage();
  const scopedLib = selectedLibraryRequest() || null;
  return packageInspection.loadLibraryMetrics(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackageLibraryMetrics() {
  if (!state.atLibraryRoot || state.libraryLens !== "metrics") return;
  if (aggregateLibrarySubjectIsActive()) return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageLibraryMetricsKey === packageScopeSignature()) return;
  observeAsync(loadPackageLibraryMetrics(), "Loading library metrics");
}

// The Library Metadata lens describes one image-level container: metadata format version,
// heap sizes, ECMA-335 table row counts, and PE/CLI header facts. This is the shape of the
// metadata itself, distinct from the API surface (the types within).
function renderPackageMetadata() {
  const pkg = currentPackage();
  const isPlatform = pkg.isRuntimePack;
  const fresh = state.packageMetadataKey === packageScopeSignature();
  const scopedLibrary = selectedLibraryName();
  const platformMetadataSelect = isPlatform && state.rootKind !== "platform"
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
    controlsHtml: metadataLibraryControl
      ? `<section class="package-metadata-controls" aria-label="Metadata library">
          <div class="package-coordinate-fields">${metadataLibraryControl}</div>
        </section>`
      : "",
    fresh,
    loading: state.packageMetadataLoading,
    error: state.packageMetadataError || "",
    metadata: state.packageMetadata || null,
    selectedRoot: state.packageMetadataRoot,
    escapeHtml,
    fmtBytes,
  });
}

async function loadPackageMetadata() {
  const pkg = currentPackage();
  const scopedLib = selectedLibraryRequest() || null;
  return packageInspection.loadMetadata(
    pkg,
    packageScopeSignature(),
    scopedLib);
}

function maybeAutoLoadPackageMetadata() {
  if (!state.atLibraryRoot || state.libraryLens !== "metadata") return;
  if (aggregateLibrarySubjectIsActive()) return;
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
function openExplorer(
  assemblyFileName: string,
  metadataRoot: MetadataRootSelection,
  tableIndex: number,
  rowId = 0,
) {
  const ex = buildBaseExplorer(assemblyFileName, metadataRoot);
  if (!ex) return;
  ex.history = [{ index: tableIndex, rowId: rowId || 0 }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

function openExplorerOverview(
  assemblyFileName: string,
  metadataRoot: MetadataRootSelection,
) {
  const ex = buildBaseExplorer(assemblyFileName, metadataRoot);
  if (!ex) return;
  ex.overview = true;
  state.explorer = ex;
  render();
}

// Opens the explorer focused on a heap card (#Strings / #Blob / #GUID / #US) rather than a table.
function openExplorerHeap(
  assemblyFileName: string,
  metadataRoot: MetadataRootSelection,
  heapName: string,
) {
  const ex = buildBaseExplorer(assemblyFileName, metadataRoot);
  if (!ex) return;
  ex.history = [{ heap: heapName }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

// The common explorer state: the table + heap directories drawn from the loaded overview, plus
// empty window caches. Focus is set by the caller (openExplorer / openExplorerHeap).
function buildBaseExplorer(
  assemblyFileName: string,
  metadataRoot: MetadataRootSelection,
): AppExplorerState | null {
  const data = state.packageMetadata;
  const asm = (data?.assemblies || []).find(a => a.assembly === assemblyFileName)
    || (data?.assemblies || [])[0];
  if (!asm) return null;
  const metadata = selectMetadataImage(
    asm.metadataRoots || [],
    metadataRoot);
  if (!metadata) return null;
  const effectiveRoot = metadataRootSelection(metadata.requestedRoot);
  if (!effectiveRoot) return null;
  const pkg = currentPackage();
  const library = selectedLibrary();
  if (!library) {
    appendQueryNotice("Choose a Library before opening its metadata explorer.");
    render();
    return null;
  }
  const isPlatform = pkg.isRuntimePack;
  const directory = (metadata.tables || [])
    .slice()
    .sort((a, b) => a.index - b.index)
    .map(t => ({ index: t.index, name: t.name, rowCount: t.rowCount, isProjected: t.isProjected }));
  const heaps = (metadata.heaps || [])
    .filter(h => h.sizeInBytes > 0)
    .map(h => ({ name: h.name, streamName: heapStreamName(h.name), sizeInBytes: h.sizeInBytes, addressing: h.addressing }));
  return {
    open: true,
    isPlatform,
    assemblyId: library.id,
    assemblyFileName: asm.assembly,
    metadataRoot: effectiveRoot,
    canonicalRoot: metadata.canonicalRoot ?? null,
    aliasesCliMetadata: metadata.aliasesCliMetadata,
    pack: isPlatform ? library.platformPack ?? null : null,
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
    onMetadataRootSelect: root => {
      state.packageMetadataRoot = root;
      state.explorer = null;
      render();
    },
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
  state.atLibraryRoot = false;
  state.libraryScope = new Set([libraryKey(targetType)]);
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

function libraryApiSignature(
  pkg: AppPackage,
  library: { id: string },
) {
  return `${packageIdentityKey(pkg)}|${library.id}`;
}

function currentLibraryApiInspection() {
  const pkg = state.package;
  const library = selectedLibrary();
  if (!pkg || !library) return null;
  return state.libraryApiInspections.get(
    libraryApiSignature(pkg, library)) ?? null;
}

async function loadLibraryApi(
  pkg: AppPackage,
  library: { id: string; name: string },
) {
  const key = libraryApiSignature(pkg, library);
  if (state.libraryApiInspections.has(key)
    || state.libraryApiLoads.has(key)) return;
  state.libraryApiLoads.add(key);
  state.libraryApiErrors.delete(key);
  try {
    const inspection = await inspectLibraryApi(
      pkg.id,
      pkg.version,
      pkg.activeFramework,
      library.id,
    );
    state.libraryApiInspections.set(key, inspection);
  } catch (error) {
    state.libraryApiErrors.set(
      key,
      errorMessage(error) || "Library API inspection failed.");
  } finally {
    state.libraryApiLoads.delete(key);
    if (state.package
      && packageIdentityEquals(state.package, pkg)
      && selectedLibrary()?.id === library.id
      && state.atLibraryRoot
      && state.libraryLens === "overview") {
      renderPreservingContentFrameFocus();
    }
  }
}

function maybeAutoLoadLibraryApi() {
  if (!state.atLibraryRoot || state.libraryLens !== "overview") return;
  const pkg = state.package;
  const library = selectedLibrary();
  if (!pkg || pkg.isRuntimePack || !library) return;
  const key = libraryApiSignature(pkg, library);
  if (state.libraryApiInspections.has(key)
    || state.libraryApiLoads.has(key)
    || state.libraryApiErrors.has(key)) return;
  observeAsync(
    loadLibraryApi(pkg, library),
    `Loading ${library.name} public API`);
}

function renderPackageOverview() {
  const pkg = currentPackage();
  const documentsSection =
    renderPackageDocuments(pkg.documents || [], escapeHtml);
  const comparisonHtml = `
    <section id="package-comparison-targets" class="document-section">
      ${packageComparisonControlsHtml(pkg)}
    </section>`;
  const contentHtml = renderPackageOverviewContent({
    packageInfoHtml: pkg.packageInfo
      ? renderPackageInfo(pkg.packageInfo, escapeHtml)
      : `<section class="document-section">
          <div class="section-title"><h2>Package info</h2></div>
          <p class="empty-list">Package metadata is unavailable for this coordinate.</p>
        </section>`,
    comparisonHtml,
    documentsHtml: documentsSection,
  });

  return renderOverviewSurface({
    subject: "package",
    subjectLabel: pkg.isRuntimePack ? "Shared framework" : "Package",
    displayName: packageDisplayName(pkg),
    iconHtml: renderInspectedSubjectIcon(pkg),
    packageId: pkg.id,
    packageVersion: pkg.version,
    activeFramework: pkg.activeFramework,
    totalTypes: pkg.totalTypes,
    totalMembers: pkg.totalMembers,
    coordinateFieldsHtml: packageVersionField(),
    contentHtml,
    escapeHtml,
  });
}

function renderLibraryCompositionOverview(
  pkg: AppPackage,
  library: ReturnType<typeof packageLibraries>[number] | null,
) {
  const libraries = packageLibraries();
  const kindPlural: Record<TypeKind, string> = {
    class: "classes",
    struct: "structs",
    interface: "interfaces",
    enum: "enums",
    delegate: "delegates",
  };
  const kinds = new Map<TypeKind, number>();
  const nsCounts = new Map<string, number>();
  for (const type of pkg.types) {
    if (!isDefaultAccessibility(type)
      || (library && libraryKey(type) !== library.id)) {
      continue;
    }
    const kind = typeKind(type.kind);
    kinds.set(kind, (kinds.get(kind) || 0) + 1);
    const ns = type.namespace || "global";
    nsCounts.set(ns, (nsCounts.get(ns) || 0) + 1);
  }
  const kindChips = KIND_ORDER
    .filter(kind => kinds.has(kind))
    .map(kind => `<button class="type-chip" data-kind-jump="${kind}"><span class="ns-count">${kinds.get(kind)}</span>${kindPlural[kind]}</button>`)
    .join("");
  const namespaceChips = [...nsCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 12)
    .map(([ns, count]) => `<button class="type-chip" data-namespace-jump="${escapeHtml(ns)}"><span class="ns-count">${count}</span>${escapeHtml(ns)}</button>`)
    .join("");
  const nsOverflow = nsCounts.size > 12
    ? `<span class="ns-overflow">+${nsCounts.size - 12} more</span>`
    : "";
  const contentHtml = renderLibraryOverviewContent({
    typeKindsHtml: `
      <section class="document-section">
        <div class="section-title"><h2>Type kinds</h2></div>
        <div class="type-chip-list">${kindChips || '<span class="empty-list">No public types.</span>'}</div>
      </section>`,
    namespacesHtml: `
      <section class="document-section">
        <div class="section-title"><h2>Namespaces</h2><span>${nsCounts.size} — click to filter</span></div>
        <div class="type-chip-list">${namespaceChips || '<span class="empty-list">No public namespaces.</span>'}${nsOverflow}</div>
      </section>`,
  });

  return renderOverviewSurface({
    subject: "library",
    subjectLabel: "Library",
    displayName: library?.name ?? "All libraries",
    iconHtml: renderInspectedSubjectIcon(pkg),
    details: library
      ? [library.asset || "Managed library", libraryIdentity(library)]
      : [`${libraries.length} managed ${libraries.length === 1 ? "library" : "libraries"}`, pkg.activeFramework],
    packageId: pkg.id,
    packageVersion: pkg.version,
    activeFramework: pkg.activeFramework,
    totalTypes: library
      ? library.types
      : libraries.reduce((sum, candidate) => sum + candidate.types, 0),
    totalMembers: library
      ? library.members
      : libraries.reduce((sum, candidate) => sum + candidate.members, 0),
    contentHtml,
    escapeHtml,
  });
}

function renderLibraryOverview() {
  const library = selectedLibrary();
  if (!library) {
    if (aggregateLibrarySubjectIsActive()) {
      return renderLibraryCompositionOverview(currentPackage(), null);
    }
    return `<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No library selected</h2><p>Choose an exact Library from the Libraries navigation.</p></section>`;
  }
  const pkg = currentPackage();
  if (state.rootKind === "library" || pkg.isRuntimePack) {
    return renderLibraryCompositionOverview(pkg, library);
  }
  const key = libraryApiSignature(pkg, library);
  const inspection = currentLibraryApiInspection();
  if (!inspection) {
    const error = state.libraryApiErrors.get(key);
    return `<section class="document-section empty-document">
      <span class="large-glyph">${error ? "!" : "◇"}</span>
      <h2>${error ? "Public API unavailable" : "Loading public API"}</h2>
      <p>${escapeHtml(error || `Inspecting ${library.name} through the shared exact-Library operation…`)}</p>
      ${error ? '<button type="button" data-library-api-retry>Retry</button>' : ""}
    </section>`;
  }
  const api = inspection.content;
  if (!api || !api.isAvailable || !api.inventory) {
    return `<section class="document-section empty-document">
      <span class="large-glyph">!</span>
      <h2>Public API unavailable</h2>
      <p>${escapeHtml(api?.failures[0]?.detail || `Could not inspect ${library.name}.`)}</p>
    </section>`;
  }
  const inventory = api.inventory;

  const kindChips = [...inventory.typeKinds]
    .sort((left, right) => left.weight - right.weight)
    .map(kind => `<button class="type-chip" data-kind-jump="${escapeHtml(kind.singularLabel)}"><span class="ns-count">${kind.count}</span>${escapeHtml(kind.count === 1 ? kind.singularLabel : kind.pluralLabel)}</button>`)
    .join("");
  const namespaceChips = [...inventory.namespaces]
    .sort((left, right) =>
      right.count - left.count || left.name.localeCompare(right.name))
    .slice(0, 12)
    .map(namespace => `<button class="type-chip" data-namespace-jump="${escapeHtml(namespace.name)}"><span class="ns-count">${namespace.count}</span>${escapeHtml(namespace.name || "(global namespace)")}</button>`)
    .join("");
  const nsOverflow = inventory.namespaces.length > 12
    ? `<span class="ns-overflow">+${inventory.namespaces.length - 12} more</span>`
    : "";

  const typeKindsHtml = `
    <section class="document-section">
      <div class="section-title"><h2>Type kinds</h2></div>
      <div class="type-chip-list">${kindChips || '<span class="empty-list">No public types.</span>'}</div>
    </section>`;
  const namespacesHtml = `
    <section class="document-section">
      <div class="section-title"><h2>Namespaces</h2><span>${inventory.namespaces.length} — click to filter</span></div>
      <div class="type-chip-list">${namespaceChips || '<span class="empty-list">No public namespaces.</span>'}${nsOverflow}</div>
    </section>`;
  const contentHtml = renderLibraryOverviewContent({
    namespacesHtml,
    typeKindsHtml,
  });
  const incompleteHtml = api.isComplete
    ? ""
    : `<section class="document-section metadata-warning" role="status">
        <strong>&#x26A0; This library could not be inspected completely</strong>
        ${api.failures.length > 0
          ? `<ul>${api.failures.map(failure =>
              `<li><code>${escapeHtml(failure.detail)}</code></li>`).join("")}</ul>`
          : ""}
      </section>`;

  return renderOverviewSurface({
    subject: "library",
    subjectLabel: "Library",
    displayName: library.name,
    iconHtml: renderInspectedSubjectIcon(pkg),
    details: [library.asset || "Managed library", libraryIdentity(library)],
    packageId: pkg.id,
    packageVersion: pkg.version,
    activeFramework: pkg.activeFramework,
    totalTypes: inventory.publicTypeCount,
    totalMembers: inventory.publicMemberCount,
    contentHtml: `${incompleteHtml}${contentHtml}`,
    escapeHtml,
  });
}

function renderGraphMemberPendingHtml(
  item: AppTypeSurface,
  title: string,
) {
  return renderGraphMemberPending({
    item,
    title,
    packageContext: currentPackage(),
    libraryLabel: typeQualifiedLibraryLabel(item) || item.assembly,
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
    libraryIdentity: selectedTypeMetadataLibraryIdentity(),
    workspaceIdentity: typeMetadataWorkspaceIdentity(),
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
    memberRequestKey,
    state.typeSourceView);
  return renderTypeSource({
    item,
    currentSignature,
    sourceState: state.typeSource,
    view: state.typeSourceView,
    escapeHtml,
    highlightCSharp,
  });
}

function renderMemberSourceHtml() {
  switch (state.memberSource.status) {
    case "idle":
      return `<section class="document-section empty-member-section"><h2>Source query failed</h2><p>No source result was returned.</p></section>`;
    case "loading":
      return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p></section>`;
    case "ready":
      {
        const type = selectedType();
        const member = selectedMember(type);
        const overload = member
          ? selectedConcreteOverload(
              member.overloads,
              state.selectedOverloadIndex)
          : undefined;
        const signature = type && overload
          ? memberRequestSignature(type, overload, false, true)
          : "";
        const source = sourceResultForSignature(
          state.memberSource,
          signature);
        if (source === null) {
          return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p></section>`;
        }
        const selectedPart = memberSourcePartSelector.current(
          signature,
          source);
        return renderSourceResult({
          source: source.source,
          text: memberSourceText(source, selectedPart),
          escapeHtml,
          highlightCSharp,
        });
      }
    case "failed":
      return `<section class="document-section empty-member-section"><h2>Source query failed</h2><p>${escapeHtml(state.memberSource.error || "No source result was returned.")}</p></section>`;
    default:
      return assertNever(state.memberSource, "member source result state");
  }
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
  if (state.atLibraryRoot) return renderLibraryView();
  if (!item) return "";
  switch (state.lens) {
    case "source":
      return renderTypeSourceHtml(item);
    case "metadata":
      return renderTypeMetadataHtml(item);
    case "compare":
      return renderCompareSurface();
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
  const definingLibrary = typeQualifiedLibraryLabel(item);
  const definingLibraryHtml = definingLibrary
    ? `<span data-type-library>· ${escapeHtml(definingLibrary)}</span>`
    : "";
  if (state.memberBrowseTypeId === item.id) {
    return `
      <section class="member-surface member-empty-surface" aria-labelledby="member-surface-title">
        <header class="api-surface-head member-surface-head">
          <h1 id="member-surface-title">Members</h1>
          <p>${visibleGroups.length} of ${publicGroups.length} member groups <span>· no member selected</span>${definingLibraryHtml}</p>
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
        <p>${visibleGroups.length} of ${publicGroups.length} member groups <span>· ${item.members} overloads</span>${definingLibraryHtml}</p>
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
  const declarationState = scopedRequestState(
    state.memberDeclarationKey,
    documentationKey,
    state.memberDeclarationLoading,
    state.memberDeclarationError);
  const selectedDeclaration =
    state.memberDeclarationKey === documentationKey
      ? state.memberDeclaration
      : null;
  const declaration = state.rootKind === "library"
    ? `<pre class="language-csharp signature-code"><code class="language-csharp">${highlightCSharp(overload.signature)}</code></pre>`
    : declarationState.loading
    ? '<p class="docs-loading">Loading C# declaration…</p>'
    : declarationState.error
      ? `<p class="docs-unavailable">Declaration query failed: ${escapeHtml(declarationState.error)}</p>`
      : selectedDeclaration?.text
        ? `<pre class="language-csharp signature-code"><code class="language-csharp">${highlightCSharp(selectedDeclaration.text)}</code></pre>`
        : `<p class="docs-unavailable">${escapeHtml(selectedDeclaration?.unavailable ?? "The selected declaration is unavailable.")}</p>`;
  const copyDeclaration = selectedDeclaration?.text
    && state.rootKind !== "library"
    ? '<button id="copy-signature" type="button" aria-label="Copy declaration">copy</button>'
    : "";
  let content;
  if (state.memberSection === "overview") {
    const parameters = overload.parameters ?? [];
    const documentationSummary = documentationLoading
      ? '<p class="docs-loading">Loading compiled documentation…</p>'
      : documentationError
        ? `<p class="docs-unavailable">Documentation query failed: ${escapeHtml(documentationError)}</p>`
        : overload.summary
          ? `<p class="api-summary">${escapeHtml(overload.summary)}</p>`
          : '<p class="docs-unavailable">No summary was found in compiled XML documentation.</p>';
    content = `
      <article class="learn-overview">
        <section class="learn-section member-overview-intro">
          <section class="signature-panel" aria-labelledby="member-declaration-title">
            <div class="signature-language">
              <h2 id="member-declaration-title"><span>C#</span><small>declaration</small></h2>
              ${copyDeclaration}
            </div>
            ${declaration}
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
    const graphScope = active?.scope;
    const breadcrumb = drilled
      ? `<div class="graph-breadcrumb">
          <button type="button" data-graph-back title="Back one level">‹ Back</button>
          <span class="graph-crumbs">${escapeHtml(platformCrumbTrail())}</span>
        </div>`
      : "";
    const traversalControl = platformView
      ? ""
      : `<label class="graph-traversal-framework">
          <span>Dependency TFM</span>
          <select data-call-graph-traversal-framework>
            ${["net12.0", "net11.0", "net10.0", "net9.0", "net8.0"]
              .map(framework =>
                `<option value="${framework}"${state.callGraphTraversalFramework === framework ? " selected" : ""}>${framework}</option>`)
              .join("")}
          </select>
        </label>`;
    const scopeLine = !graphScope
      ? ""
      : platformView
      ? `<div class="graph-scope"><strong>Platform${drilled ? " descent" : " workspace"}</strong><span>${graphScope.callerAssemblies} resident assemblies · ${graphScope.assemblies} participants</span><strong>Callees</strong><span>${escapeHtml(graphScope.calleeScope)} · depth 2</span></div>`
      : `<div class="graph-scope"><strong>Dependency scope</strong><span>${graphScope.packages} packages · ${graphScope.assemblies} assemblies</span><strong>Callees</strong><span>${escapeHtml(graphScope.calleeScope)} · depth 3</span></div>`;
    const diagnostics = active?.diagnostics;
    const diagnosticsMessage = callGraphDiagnosticsMessage(diagnostics);
    const incompleteGraph = diagnosticsMessage
      ? `<div class="graph-drill-error graph-diagnostics">${escapeHtml(diagnosticsMessage)}</div>`
      : "";
    content = state.memberCallGraphLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Building dependency-aware call graph…</h2><p>Resolving package dependencies under ${escapeHtml(state.callGraphTraversalFramework)} and scanning implementation IL.</p></section>`
      : active && active.noBody
        ? `<section class="document-section empty-member-section"><h2>No call graph</h2><p>${escapeHtml(active.callees?.memberName || "This member")} is an abstract or interface method — it declares no IL body, so it has no in-assembly callers or callees to graph.</p></section>`
        : active
        ? `<section class="document-section call-graph-section">
            <div class="section-title"><h2>Call graph</h2><span>${callGraphSummary(active)}</span></div>
            ${breadcrumb}
            ${traversalControl}
            ${state.platformDrillLoading
              ? `<div class="graph-expanding"><span class="loader"></span> Range-fetching the implementation assembly from the runtime pack…</div>`
              : ""}
            ${state.platformDrillError
              ? `<div id="platform-drill-error" class="graph-drill-error" role="alert" tabindex="-1">${escapeHtml(state.platformDrillError)}</div>`
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
            ${callGraphLegendHtml()}
          </section>`
        : `<section class="document-section empty-member-section"><h2>Call graph query failed</h2><p>${escapeHtml(callGraphError || "No call graph result was returned.")}</p></section>`;
    content = `<div data-call-graph-surface>${content}</div>`;
  } else if (state.memberSection === "facts") {
    content = renderMemberFacts(state);
  } else if (state.memberSection === "annotated") {
    const destinationError = state.annotatedDestinationError
      ? `<div id="annotated-destination-error" class="graph-drill-error" role="alert">${escapeHtml(state.annotatedDestinationError)}</div>`
      : "";
    const selectionError = state.memberFindingSelectionError
      ? `<div id="finding-selection-error" class="graph-drill-error" role="alert">${escapeHtml(state.memberFindingSelectionError)}</div>`
      : "";
    content = destinationError + selectionError + (state.memberAnnotatedLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Annotating member…</h2><p>Raising the selected overload to C#, interleaving its IL, and collecting the facts observed about it.</p></section>`
      : state.memberAnnotated
        ? renderAnnotatedSource(state.memberAnnotated)
        : `<section class="document-section empty-member-section"><h2>Annotated source query failed</h2><p>${escapeHtml(state.memberAnnotatedError || "No annotated source result was returned.")}</p></section>`);
  } else if (state.memberSection === "source") {
    content = renderMemberSourceHtml();
  } else if (state.memberSection === "compare") {
    content = renderCompareSurface();
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
  if (prismCSharp.languages.csharp) {
    return prismCSharp.highlight(
      source,
      prismCSharp.languages.csharp,
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
    prismCSharp,
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
  onPruningEvaluate: () =>
    observeAsync(
      packageInspection.loadPruning(
        currentPackage(),
        packagePruningSignature(),
        state.packagePruningFamily),
      "Evaluating platform pruning"),
  onPruningFamilySelect: family => {
    if (state.packagePruningFamily === family) return;
    state.packagePruningFamily = family;
    state.packagePruning = null;
    state.packagePruningError = "";
    state.packagePruningKey = "";
    renderPreservingMemberFocus();
  },
  onDependencyLoad: (id, version) =>
    observeAsync(
      openDependencyPackage(id, version),
      "Opening a dependency package"),
  onDependencyOpen: switchToPackageForDependencies,
  onGraphTypeSelect: navigateToTypeByName,
  onKindJump: kind => {
    state.atPackageRoot = false;
    state.atLibraryRoot = false;
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
    if (!library) return;
    if (!selectLibrarySubject(library)) return;
    if (kind) {
      state.atLibraryRoot = false;
      state.kindFilter = kind;
    }
    showContentDetailAfterRender();
    render();
  },
  onNamespaceJump: namespace => {
    state.atPackageRoot = false;
    state.atLibraryRoot = false;
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
  onLibraryApiRetry: () => {
    const pkg = state.package;
    const library = selectedLibrary();
    if (!pkg || !library) return;
    const key = libraryApiSignature(pkg, library);
    state.libraryApiErrors.delete(key);
    observeAsync(
      loadLibraryApi(pkg, library),
      `Retrying ${library.name} public API`);
    renderPreservingContentFrameFocus();
  },
  onLibraryChipSelect: library => {
    if (library && selectLibrarySubject(library)) render();
  },
  onLibraryJump: library => {
    if (library && selectLibrarySubject(library)) render();
  },
  onPlatformLibrarySelect: (name, pack) =>
    observeAsync(
      openPlatformLibrary(name, pack, { inPlace: true }),
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
    || !state.atLibraryRoot
    || state.libraryLens !== lens) {
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
    && state.atLibraryRoot
    && state.libraryLens === lens
    && packageIdentityEquals(state.package, originPackage);
  const key = name.replace(/\.dll$/i, "");
  const pack = selectedPack || platformPackForAssembly(key);
  const target = state.platformIndex?.target(
    originPackage.activeFramework,
    originPackage.version);
  const matches = target?.rows.filter(row =>
    row.hasImplementation
    && (!pack || row.pack === pack)
    && row.assembly.toLowerCase() === key.toLowerCase()) ?? [];
  if (matches.length !== 1) {
    appendQueryNotice(
      `The platform library '${key}' is not uniquely available in the exact catalog.`);
    render();
    return;
  }
  const row = matches[0]!;
  const resident = runtimeAssemblyIsResident(
    originPackage,
    row.assembly,
    row.pack);
  if (!resident) {
    const runtimeResult = await loadRuntimePackAssembly(
      originPackage.activeFramework,
      platformAssemblyRequest(row),
      row.pack,
      isCurrent,
      originPackage.version,
      row.file);
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
  const library = resolvePackageLibrary(currentPackage().assemblies, key);
  if (!library) {
    appendQueryNotice(`The loaded platform library '${key}' is not uniquely available.`);
    render();
    return;
  }
  state.libraryScope = new Set([library.id]);
  state.frameworkLibraryPresentation = {
    tfm: originPackage.activeFramework,
    version: originPackage.version,
    libraryId: library.id,
  };
  recordPlatformRecent(key, pack);
  state.atPackageRoot = false;
  state.atLibraryRoot = true;
  state.libraryLens = lens;
  state.namespaceFilter = "";
  state.typeFilter = "";
  state.kindFilter = "";
  normalizeLibrarySelection();
  if (lens === "integrations") {
    if (state.integrationMode === "opportunities") await loadPackageOpportunities();
    else await loadPackageIntegrations();
  }
  else if (lens === "analysis") await loadPackagePerformance();
  else if (lens === "metrics") await loadPackageLibraryMetrics();
  else await loadPackageMetadata();
}

function bindPackageViewEvents() {
  bindPackageView(document, packageViewActions);
}

function bindPackageDependencyListEvents() {
  bindPackageDependencyList(document, packageViewActions);
}

function bindLibrarySubjectNavEvents() {
  bindLibrarySubjectNav(document, {
    onSelect: (id: string | null) => {
      const selected = id === null
        ? selectAggregateLibrarySubject({ preserveLens: true })
        : selectLibrarySubject(id, { preserveLens: true });
      if (!selected) return;
      showContentDetailAfterRender();
      render();
      if (!contentFrameMedia.matches)
        document.querySelector<HTMLElement>(".library-subject-list")?.focus();
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
      const type = selectedType();
      const member = selectedMember(type);
      const overload = member
        ? selectedConcreteOverload(
            member.overloads,
            state.selectedOverloadIndex)
        : undefined;
      const signature = type && overload
        ? memberRequestSignature(type, overload, false, true)
        : "";
      const source = sourceResultForSignature(
        state.memberSource,
        signature);
      if (source !== null) {
        void copyText(
          memberSourceText(
            source,
            memberSourcePartSelector.current(signature, source)),
          "source copied");
      }
    },
    onMemberSourcePartSelect: part => {
      const type = selectedType();
      const member = selectedMember(type);
      const overload = member
        ? selectedConcreteOverload(
            member.overloads,
            state.selectedOverloadIndex)
        : undefined;
      if (!type || !overload) return;
      const signature =
        memberRequestSignature(type, overload, false, true);
      const source = sourceResultForSignature(
        state.memberSource,
        signature);
      if (source !== null
        && memberSourcePartSelector.select(signature, source, part)) {
        render();
      }
    },
    onCopySignature: () => {
      const type = selectedType();
      const member = selectedMember(type);
      const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
      const signature = type && overload
        ? memberRequestSignature(type, overload)
        : "";
      if (type
        && overload
        && state.memberDeclarationKey === signature
        && state.memberDeclaration?.text) {
        void copyText(state.memberDeclaration.text, "declaration copied");
      }
    },
    onCopyTypeSource: () => {
      if (state.typeSource.status !== "ready") return;
      const text = typeCodeViewText(state.typeSource.source);
      if (text !== null) void copyText(text, "source copied");
    },
    onTypeSourceViewSelect: view => {
      if (state.typeSourceView === view) return;
      state.typeSourceView = view;
      observeAsync(loadSelectedTypeSource(), "Loading type code view");
    },
    onExploreSource: () => openSettings("source"),
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
    onTypeNavBack: () => {
      if (state.atLibraryRoot && state.rootKind === "platform") {
        if (hasPlatformRootHistoryView()) showPlatformRoot();
        return;
      }
      const focusGeneration = beginSpotlightNavigation();
      state.workspaceSubjectOpen = false;
      state.atPackageRoot = state.atLibraryRoot;
      state.atLibraryRoot = !state.atPackageRoot;
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      state.selectedOverloadIndex = null;
      showContentDetailAfterRender();
      render();
      if (!contentFrameMedia.matches)
        restoreContentNavigationFocus(focusGeneration);
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
      state.atLibraryRoot = false;
      const type = state.package?.types.find(candidate => candidate.id === typeId);
      if (type
        && (state.rootKind === "platform" || state.libraryScope !== null)) {
        state.libraryScope = new Set([libraryKey(type)]);
      }
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
    onMemberSectionSelect: section => {
      contentFramePane = "detail";
      applyMemberSection(section);
    },
    onLibraryLensSelect: lens => {
      contentFramePane = "detail";
      selectLibraryLens(lens);
    },
    onPackageLensSelect: lens => {
      contentFramePane = "detail";
      state.packageLens = lens;
      render();
    },
    onScopeSelect: target => {
      if (target === "platform") {
        showPlatformRoot();
        return;
      }
      contentFramePane = "detail";
      if (target === "workspace") {
        navigationSequence.begin();
        state.workspaceSubjectOpen = true;
        state.atPackageRoot = true;
        state.atLibraryRoot = false;
        state.selectedMemberKey = "";
        state.memberBrowseTypeId = "";
        state.selectedOverloadIndex = null;
      } else if (target === "package") {
        state.workspaceSubjectOpen = false;
        state.atPackageRoot = true;
        state.atLibraryRoot = false;
      } else if (target === "library") {
        if (!enterRetainedLibrarySubject({ preserveView: true })) return;
      } else if (target === "type") {
        state.workspaceSubjectOpen = false;
        // Pop out to the type level: leave the package root and drop any open member so the
        // type lenses (API / Metadata / Source) take the strip. Ensure a type is selected.
        if (!enterTypeSubject(selectedType())) return;
        state.selectedMemberKey = "";
        state.memberBrowseTypeId = "";
        state.selectedOverloadIndex = null;
      } else if (target === "member") {
        state.workspaceSubjectOpen = false;
        state.atPackageRoot = false;
        state.atLibraryRoot = false;
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
    onOpenDiagnostics: openDiagnosticsRoute,
    onOpen: openSettings,
    onTasteClear: clearTaste,
    onTasteToggle: toggleTaste,
    onThemeSelect: setTheme,
  });
}

function bindIntegrationInspectorEvents() {
  bindIntegrationTabs(document, mode => {
    if (state.integrationMode === mode) return;
    state.integrationMode = mode;
    render();
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
  syncFindingSelectionFromAnnotatedSession(opened.modal);
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
  syncFindingSelectionFromAnnotatedSession(state.memberAnnotatedEmbedded);
  if (restoreExploreFocus) renderAndFocusAnnotated({ kind: "explore" }, "embedded");
  return true;
}

function syncFindingSelectionFromAnnotatedSession(
  session: AnnotatedSourceSession,
) {
  const interaction = state.memberFindingInteraction;
  if (!interaction) return;
  const factId =
    session.primary?.kind === "finding" ? session.primary.id : null;
  if (factId === null) {
    state.memberFindingInteraction = clearFindingSelection(interaction);
    state.memberFindingSelectionError = "";
    return;
  }
  const transition = selectAnnotatedSourceFact(interaction, factId);
  state.memberFindingInteraction = transition.accepted
    ? transition.interaction
    : clearFindingSelection(interaction);
  state.memberFindingSelectionError = transition.error ?? "";
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
    case "annotation-open": {
      const next = selectFinding(session, action.opener);
      setSession(next);
      syncFindingSelectionFromAnnotatedSession(next);
      renderAndFocusAnnotated("#annotated-detail-title", surface, true);
      return;
    }
    case "inspector-open": {
      const next = selectFinding(session, {
        kind: "inspector",
        factId: action.factId,
      });
      setSession(next);
      syncFindingSelectionFromAnnotatedSession(next);
      renderAndFocusAnnotated("#annotated-detail-title", "modal", true);
      return;
    }
    case "relationship-open": {
      const next = selectFinding(session, {
        kind: "relationship",
        factId: action.factId,
      });
      setSession(next);
      syncFindingSelectionFromAnnotatedSession(next);
      renderAndFocusAnnotated("#annotated-detail-title", "modal", true);
      return;
    }
    case "relationship-occurrences-open": {
      const transition = showRelationshipOccurrences(session, action.factId);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus, "modal");
      return;
    }
    case "relationship-presentation": {
      const transition =
        selectRelationshipPresentation(session, action.value);
      setSession(transition.state);
      renderAndFocusAnnotated(transition.focus, "modal");
      return;
    }
    case "annotation-set": {
      const transition = action.value === "Default"
        ? selectDefaultAnnotations(model, session)
        : action.value === "All"
          ? selectAllAnnotations(model, session)
          : clearAnnotations(session);
      setSession(transition.state);
      syncFindingSelectionFromAnnotatedSession(transition.state);
      renderAndFocusAnnotated(transition.focus);
      return;
    }
    case "finding-toggle": {
      const transition =
        toggleFindingAnnotation(model, session, action.factId);
      setSession(transition.state);
      syncFindingSelectionFromAnnotatedSession(transition.state);
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
    case "relationship-destination-open": {
      const relationship =
        model.callRelationships[action.relationshipIndex];
      if (!relationship) return;
      invalidateMemberDestinationWork(state);
      state.annotatedDestinationError = "";
      const binding =
        callGraphTargetBinding(
          relationship.target,
          action.destination,
          "annotated")
        ?? blockedCallGraphNodeBinding(
          relationship.target,
          "the exact target is unavailable in the current workspace",
          "annotated");
      dismissAnnotatedSourceModal(false);
      binding.onSelect();
      return;
    }
    case "finding-evidence-open": {
      const evidence =
        model.findingEvidenceByFactId.get(action.factId);
      if (!evidence) return;
      invalidateMemberDestinationWork(state);
      state.annotatedDestinationError = "";
      const binding =
        callGraphTargetBinding(
          evidence.target,
          action.destination,
          "annotated")
        ?? blockedCallGraphNodeBinding(
          evidence.target,
          "the exact callee is unavailable in the current workspace",
          "annotated");
      dismissAnnotatedSourceModal(false);
      binding.onSelect();
      return;
    }
    case "node-select": {
      const next = selectAnnotatedNode(session, action.nodeId);
      setSession(next);
      syncFindingSelectionFromAnnotatedSession(next);
      renderAndFocusAnnotated({ kind: "node", nodeId: action.nodeId });
      return;
    }
    case "source-select": {
      const node =
        hitTestAnnotatedNode(model, action.offset, action.medium);
      if (!node) return;
      const next = selectAnnotatedNode(session, node.id);
      setSession(next);
      syncFindingSelectionFromAnnotatedSession(next);
      renderAndFocusAnnotated({ kind: "node", nodeId: node.id });
      return;
    }
  }
}

function openFindingInstanceFromFacts(receipt: string, instanceKey: number) {
  const interaction = state.memberFindingInteraction;
  if (!interaction || !state.memberAnnotated) {
    state.memberFindingSelectionError =
      "The active Finding census is unavailable.";
    renderPreservingMemberFocus();
    return;
  }
  const transition = selectFindingInstance(
    interaction,
    receipt,
    instanceKey,
  );
  if (!transition.accepted) {
    state.memberFindingSelectionError = transition.error;
    renderPreservingMemberFocus();
    return;
  }

  state.memberFindingInteraction = transition.interaction;
  state.memberFindingSelectionError = "";
  const model = createAnnotatedSourceViewerModel(state.memberAnnotated);
  const embedded = state.memberAnnotatedEmbedded
    ?? createEmbeddedSession(model);
  const opened = openModalSession(model, embedded);
  const modal = selectFinding(opened.modal, {
    kind: "inspector",
    factId: transition.factId,
  });
  state.memberAnnotatedEmbedded = opened.embedded;
  state.memberAnnotatedModal = modal;
  state.memberSection = "annotated";
  contentFramePane = "detail";
  renderAndFocusAnnotated("#annotated-detail-title", "modal", true);
}

function bindMemberFactsEvents() {
  bindMemberFacts(document, {
    onSelectFinding: openFindingInstanceFromFacts,
  });
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
  bindSavedWorkspaces(document, savedWorkspaces);
  bindWorkspaceSubject(document, {
    onSelect: selectRetainedWorkspace,
    onActivateWorkspace: selectRetainedWorkspace,
    onDeleteWorkspace: deleteRetainedWorkspace,
    onProductPackageAction: navigationId => observeAsync(
      activateRetainedPackageAction(navigationId),
      "Activating retained Package",
    ),
    onProductPlatformAction: navigationId => observeAsync(
      activateRetainedPlatformAction(navigationId),
      "Activating retained Platform",
    ),
    onActivate: action =>
      observeAction(
        () => activateWorkspacePackageOccurrence(action),
        "Opening the Workspace package"),
    onDemo: runHomeDemo,
    onRetry: retryWorkspaceOccurrenceView,
    onRemove: removeWorkspacePackageRow,
    onAddPackage: openWorkspacePackagePicker,
    onPlatform: showPlatformRoot,
    onFrameworkLibrary: (assembly, pack, tfm, version) =>
      observeAsync(
        openPlatformLibrary(assembly, pack, {
          deferPlatformPresentation: true,
          inPlace: true,
          tfm,
          version,
        }),
        "Opening a framework Library"),
  });
}

function bindEvents() {
  packageControls.bind(document);
  bindWorkspaceSubjectEvents();
  bindTypePanelEvents();
  bindScopeBarEvents();
  bindSettingsPanelEvents();
  bindMetadataViewerEvents();
  bindIntegrationInspectorEvents();
  bindPackageOpportunitiesEvents();
  bindGraphSourceEvents();
  bindDocViewerEvents();
  bindMemberFactsEvents();
  bindAnnotatedSourceEvents();
  bindPackageViewEvents();
  bindLibrarySubjectNavEvents();
  bindPackageComparisonControls();
  bindCompareEvents();
  bindLibraryControlsEvents();
  workbenchShellBinding =
    bindWorkbenchShell(document, workbenchShellActions);
  bindGraphBack(document, graphBackActions);
  bindGraphExplore(document, openGraphExplorer);
  bindCallGraphTraversalFramework();
  bindContentFrameEvents();
  observeAsync(ensurePackageVersions(state.package), "Loading package versions");
  if (state.spotlightOpen) spotlight.bind(document, "modal");
}

function bindCallGraphTraversalFramework() {
  const selector = document.querySelector<HTMLSelectElement>(
    "[data-call-graph-traversal-framework]");
  selector?.addEventListener("change", () => {
    const framework = selector.value.trim();
    if (!framework
      || framework === state.callGraphTraversalFramework) return;
    state.callGraphTraversalFramework = framework;
    invalidateMemberCallGraphWork(state);
    state.memberCallGraph = null;
    state.memberCallGraphError = "";
    state.graphMemberNavigationError = "";
    render();
    observeAsync(
      loadSelectedMemberCallGraph(),
      "Changing call-graph dependency target framework");
  });
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
    candidates,
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
let spotlightTypeRanking:
  | {
      readonly key: string;
      readonly matches: Array<SpotlightCache["pool"][number] & {
        ranges: HighlightRange[];
      }>;
    }
  | null = null;
let pendingSpotlightTypeRankingKey = "";

function spotlightTypeMatches(query: string) {
  const cache = spotlightCandidates();
  if (!query) {
    return cache.pool
      .filter(item => item.pkg === state.package)
      .sort((a, b) => a.type.name.localeCompare(b.type.name))
      .map(item => ({ ...item, ranges: [] }));
  }
  if (!state.engineReady) return spotlightFallbackMatches(query, cache.pool);
  const key = `${cache.signature}\n${query}`;
  if (spotlightTypeRanking?.key === key)
    return spotlightTypeRanking.matches;
  if (pendingSpotlightTypeRankingKey !== key) {
    pendingSpotlightTypeRankingKey = key;
    void inspectSearchTypes(query, cache.candidates).then(
      hits => {
        if (pendingSpotlightTypeRankingKey !== key) return undefined;
        const lowerQuery = query.toLowerCase();
        const matches: Array<SpotlightCache["pool"][number] & {
          ranges: HighlightRange[];
        }> = [];
        for (const hit of hits) {
          const item = cache.keyMap.get(hit.key);
          if (!item) continue;
          matches.push({
            ...item,
            ranges: computeHighlightRanges(item.type.name, lowerQuery),
          });
        }
        spotlightTypeRanking = { key, matches };
        pendingSpotlightTypeRankingKey = "";
        spotlight.updateResults();
        return undefined;
      },
      () => {
        if (pendingSpotlightTypeRankingKey !== key) return undefined;
        spotlightTypeRanking = {
          key,
          matches: spotlightFallbackMatches(query, cache.pool),
        };
        pendingSpotlightTypeRankingKey = "";
        spotlight.updateResults();
        return undefined;
      },
    );
  }
  return spotlightFallbackMatches(query, cache.pool);
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
    .filter(pkg => pkg.source.kind !== "platform"
      && (!lowerQuery || pkg.id.toLowerCase().includes(lowerQuery)))
    .map(pkg => ({ pkg, ranges: computeHighlightRanges(pkg.id, lowerQuery) }));
}

// Platform selection is independent of the focused NuGet package.
function platformScopeTfm(): string {
  return state.platformSelection?.tfm ?? DEFAULT_PLATFORM_FRAMEWORK;
}

const platformVersions = new Map<string, { values: string[] } & PlatformSubjectStatus>();
const platformWarmups = new Map<string, PlatformSubjectStatus>();
const platformPackages = new Map<string, AppPackage>();
let platformCatalogSequence = 0;

function selectedPlatformTarget(): PlatformCatalogTarget | null {
  const selection = state.platformSelection;
  return state.platformIndex?.target(
    selection?.tfm ?? DEFAULT_PLATFORM_FRAMEWORK,
    selection?.version) ?? null;
}

function platformTargetLabel() {
  const target = state.platformSelection;
  return target ? `.NET Platform ${target.tfm} · ${target.version}` : ".NET Platform";
}

function runtimePackageForTarget(target: { tfm: string; version: string }): AppPackage | null {
  return state.packages.find(pkg => pkg.source.kind === "platform"
    && pkg.activeFramework === target.tfm && pkg.version === target.version)
    ?? platformPackages.get(platformTargetKey(target)) ?? null;
}

function retainPlatformPackageForTarget(
  target: { tfm: string; version: string },
): AppPackage | null {
  const basis = state.workspaceShareBasis;
  const packageModel = runtimePackageForTarget(target);
  const previousPackages = state.packages;
  if (packageModel) {
    if (!state.packages.includes(packageModel))
      retainPackageModel(packageModel);
  } else {
    const discarded = state.packages.filter(pkg => pkg.source.kind === "platform");
    if (discarded.length > 0) {
      state.packages = state.packages.filter(pkg => pkg.source.kind !== "platform");
      for (const previous of discarded)
        releasePackageModelCaches(previous);
    }
  }
  if (state.packages !== previousPackages) {
    invalidateWorkspaceMembershipViews();
    if (basis
      && workspaceShareTabsMatchResolved(
        basis.tabs,
        resolvedWorkspaceShareTabs())) {
      state.workspaceShareBasis = basis;
    }
  }
  return packageModel;
}

async function ensurePlatformCatalog(tfm: string, version?: string): Promise<PlatformCatalogTarget> {
  const catalogTfm = platformCatalogFramework(tfm);
  state.platformIndex ??= await loadPlatformIndex();
  if (!state.platformIndex) throw new Error("The Platform catalog could not be loaded.");
  const bundled = state.platformIndex.target(catalogTfm, version);
  if (bundled) return bundled;
  if (!version) throw new Error(`The Platform catalog has no target for ${catalogTfm}.`);
  const target = requireMatchingPlatformTarget(
    parsePlatformCatalogTarget(await inspectPlatformCatalog(catalogTfm, version)),
    catalogTfm, version);
  state.platformIndex.addTarget(target);
  return target;
}

function installPlatformTarget(
  target: PlatformCatalogTarget,
  presentAsRoot = true,
) {
  const capacityError = platformCoordinateCapacityError();
  if (capacityError) throw new Error(capacityError);
  const basis = state.workspaceShareBasis;
  const packageModel = retainPlatformPackageForTarget(target);
  const previous = state.platformSelection;
  if (previous?.tfm !== target.tfm || previous.version !== target.version)
    state.frameworkLibraryPresentation = null;
  state.rootKind = "platform";
  state.platformPresentedAsRoot = presentAsRoot;
  state.platformSelection = {
    tfm: target.tfm, version: target.version,
    includeAllLibraries: previous?.includeAllLibraries ?? false,
    filter: previous?.filter ?? "",
  };
  state.package = packageModel;
  state.workspaceShareBasis = basis
    && workspaceShareTabsMatchResolved(
      basis.tabs,
      resolvedWorkspaceShareTabs())
      ? basis
      : null;
  state.workspaceSubjectOpen = false;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.home = false;
  state.credits = false;
  state.loading = false;
  state.error = "";
  state.libraryScope = null;
  state.selectedTypeId = "";
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetMemberSectionState();
}

function platformCoordinateCapacityError() {
  return !state.platformSelection
    && workspaceCoordinateCount() >= MAX_WORKSPACE_PACKAGES
      ? `Workspace holds at most ${MAX_WORKSPACE_PACKAGES} coordinates. `
        + "Remove a package before opening Platform."
      : "";
}

function refreshPlatformStatus() {
  if (state.rootKind === "platform" && !state.home && scope() === "platform") {
    const focused = document.activeElement instanceof HTMLElement ? document.activeElement.id : "";
    render();
    if (focused) document.getElementById(focused)?.focus({ preventScroll: true });
  }
}

async function discoverPlatformVersions(tfm: string, retry = false) {
  const existing = platformVersions.get(tfm);
  if (existing && (!retry || existing.loading)) return;
  const request = { values: existing?.values ?? [], loading: true, error: "" };
  platformVersions.set(tfm, request);
  refreshPlatformStatus();
  try {
    request.values = parsePlatformVersions(await inspectPlatformVersions(tfm));
  } catch (error) {
    request.error = `Could not discover Platform versions: ${errorMessage(error)}`;
  } finally {
    request.loading = false;
    refreshPlatformStatus();
  }
}

async function warmPlatformTarget(target: PlatformCatalogTarget, retry = false) {
  const key = platformTargetKey(target);
  const existing = platformWarmups.get(key);
  if (existing && (!retry || existing.loading)) return;
  const request = { loading: true, error: "" };
  platformWarmups.set(key, request);
  refreshPlatformStatus();
  try {
    await inspectPrefetchPlatformPacks(target.tfm, target.version);
  } catch (error) {
    request.error = `Runtime pack download failed: ${errorMessage(error)}`;
  } finally {
    request.loading = false;
    refreshPlatformStatus();
  }
}

function startPlatformTargetWork(target: PlatformCatalogTarget) {
  if (!platformSupportsRuntimeAcquisition(target)) return;
  observeAsync(discoverPlatformVersions(target.tfm), "Discovering Platform versions");
  observeAsync(warmPlatformTarget(target), "Warming Platform runtime packs");
}

async function openPlatformSubject(
  tfm = state.platformSelection?.tfm ?? DEFAULT_PLATFORM_FRAMEWORK,
  version = state.platformSelection?.version,
) {
  const capacityError = platformCoordinateCapacityError();
  if (capacityError) {
    showToast(capacityError);
    return;
  }
  if (retainedWorkspaces.activeWorkspaceId === null && !canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    render({ synchronizeUrl: false });
    return;
  }
  const sequence = navigationSequence.begin();
  const request = ++platformCatalogSequence;
  spotlight.reset();
  state.home = false;
  state.loading = false;
  state.error = "";
  // A cold root has no acquisition surface; catalog failure still belongs to Platform.
  if (!state.platformSelection) {
    state.rootKind = "platform";
    state.package = null;
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
    state.workspaceSubjectOpen = false;
  }
  state.platformCatalogStatus = { loading: true, error: "" };
  platformCatalogRetry = () => openPlatformSubject(tfm, version);
  render();
  try {
    const target = await ensurePlatformCatalog(tfm, version);
    if (!navigationSequence.isCurrent(sequence) || request !== platformCatalogSequence) return;
    installPlatformTarget(target);
    state.platformOpeningStatus = { loading: false, error: "" };
    state.platformCatalogStatus = { loading: false, error: "" };
    ensureCurrentWorkspacePublished();
    render();
    startPlatformTargetWork(target);
    focusLevelOneHeading();
  } catch (error) {
    if (!navigationSequence.isCurrent(sequence) || request !== platformCatalogSequence) return;
    state.platformCatalogStatus = { loading: false, error: errorMessage(error) };
    render();
  }
}

function showPlatformRoot() {
  const target = selectedPlatformTarget();
  if (!target) {
    observeAsync(openPlatformSubject(), "Opening Platform");
    return;
  }
  navigationSequence.begin();
  installPlatformTarget(target);
  state.platformOpeningStatus = { loading: false, error: "" };
  render();
  document.querySelector<HTMLElement>(".platform-library-list")?.focus();
}

function renderPlatformView() {
  const target = selectedPlatformTarget();
  const idle = { loading: false, error: "" };
  app.innerHTML = `<div class="workbench"${state.settings || state.keyboardHelp || state.libraryOpen ? " inert" : ""}>
    ${workbenchShellHtml({
      inspectedTargetHtml: `<div class="inspected-target"><span class="subject-icon" aria-hidden="true">.NET</span><div class="subject-path">${renderInspectedSubjectPath(currentInspectedSubjectPath())}</div></div>`,
      subjectInspectorHtml: renderScopeBar(["platform"]),
      titleNavigationHtml: renderTitleNavigation(navigationHistory.canBack(), navigationHistory.canForward()),
    })}
    <div class="notice-stack">${renderQueryNotice()}</div>
    <main id="subject-panel" class="workspace platform-workspace" role="tabpanel" aria-labelledby="active-subject-tab">
      <section class="detail-pane"><article id="inspector-panel" class="detail-scroll">
        ${renderPlatformSubject({
          target, selection: state.platformSelection,
          frameworks: [...new Set(state.platformIndex?.targets()
            .filter(platformSupportsRuntimeAcquisition).map(candidate => candidate.tfm) ?? [])],
          versions: target ? platformVersions.get(target.tfm)?.values ?? [] : [],
          catalog: state.platformCatalogStatus,
          discovery: target ? platformVersions.get(target.tfm) ?? idle : idle,
          warmup: target ? platformWarmups.get(platformTargetKey(target)) ?? idle : idle,
          opening: state.platformOpeningStatus, escapeHtml,
        })}
      </article></section>
    </main>
    ${dataBarHtml({
      buildIdentity: state.buildIdentity,
      producer: { kind: "acquisition", label: "Platform" },
    }, escapeHtml)}
    ${state.spotlightOpen ? spotlight.modalHtml() : ""}
    </div>${renderApplicationMenu(true)}
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp ? renderKeyboardHelpDialog(keyboardHelpBindings) : ""}
    ${renderLibraryOpenDialog({
      open: state.libraryOpen,
      busy: state.libraryOpenBusy,
      error: state.libraryOpenError,
    }, escapeHtml)}`;
  bindScopeBarEvents();
  bindSettingsPanelEvents();
  workbenchShellBinding = bindWorkbenchShell(document, workbenchShellActions);
  bindLibraryOpenEvents();
  if (state.spotlightOpen) spotlight.bind(document, "modal");
  bindPlatformSubject(document, {
    onFramework: tfm => observeAsync(openPlatformSubject(tfm, state.platformIndex?.target(tfm)?.version), "Changing Platform target"),
    onVersion: version => observeAsync(openPlatformSubject(platformScopeTfm(), version), "Changing Platform version"),
    onFilter: filter => {
      if (!state.platformSelection) return;
      state.platformSelection.filter = filter;
      render();
      document.querySelector<HTMLElement>("#platform-filter")?.focus();
    },
    onIncludeAll: include => {
      if (!state.platformSelection) return;
      state.platformSelection.includeAllLibraries = include;
      render();
      document.querySelector<HTMLElement>("#platform-include-all")?.focus();
    },
    onLibrary: key => {
      const row = target?.rows.find(candidate => platformLibraryKey(candidate) === key);
      if (!row) throw new Error("The selected Platform library is no longer in this target.");
      observeAsync(openPlatformLibrary(row.assembly, row.pack, { inPlace: true }), "Opening Platform Library");
    },
    onRetry: kind => {
      if (kind === "catalog" && platformCatalogRetry) observeAction(platformCatalogRetry, "Retrying Platform catalog");
      else if (kind === "versions" && target) observeAsync(discoverPlatformVersions(target.tfm, true), "Retrying version discovery");
      else if (kind === "warmup" && target) observeAsync(warmPlatformTarget(target, true), "Retrying runtime download");
      else if (kind === "library" && platformLibraryRetry) observeAction(platformLibraryRetry, "Retrying Platform Library");
    },
  });
}

// Search uses the exact catalog target without acquiring inspected bytes.
function platformLibraryRoster(query: string) {
  const idx = state.platformIndex;
  if (!idx) return [];
  const target = selectedPlatformTarget();
  if (!target) return [];
  const lower = query.trim().toLowerCase();
  const rt = runtimePackPackage();
  const rows = platformInventory(target, true, lower).map(row => ({
    ...row, role: platformLibraryRole(row).label,
    version: target.version,
    loaded: runtimeAssemblyIsResident(rt, row.assembly, row.pack),
    ranges: computeHighlightRanges(row.assembly, lower),
  }));
  return rankPlatformLibraryMatches(rows, lower);
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

function frameworkLibrarySpotlightResults(query: string): SpotlightResult[] {
  const results: SpotlightResult[] = [];
  const roster = platformLibraryRoster(query);
  for (const lib of roster.filter(row => row.hasImplementation).slice(0, 200)) {
    results.push({ ...lib, kind: "framework-lib" });
  }
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
  return results;
}

function spotlightResults(): SpotlightResult[] {
  const query = state.spotlightQuery.trim();
  const spotlightScope = state.spotlightScope;
  // Exhaustive scope dispatch. "all" blends the package, type and member scopes, so those
  // arms share the composed body below instead of each owning a renderer. Adding an entry to the
  // spotlight scope catalog offers it to users immediately, so it must fail compilation here
  // until it declares which of these shapes it is.
  switch (spotlightScope) {
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
    const parsedPackageQuery = parsePackageQuery(query);
    if (parsedPackageQuery?.explicitVersion) {
      const openPackage = findOpenPackageForQuery(state, parsedPackageQuery);
      if (openPackage) {
        results.push({
          kind: "pkg-loaded",
          pkg: openPackage,
          ranges: [[0, openPackage.id.length]],
        });
      } else {
        results.push({
          kind: "pkg-nuget",
          hit: {
            id: parsedPackageQuery.packageId,
            version: parsedPackageQuery.version,
            exact: true,
          },
          ranges: [[0, parsedPackageQuery.packageId.length]],
        });
      }
      return results;
    }
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
      state.spotlightPackageSearch,
      query,
    );
    for (const hit of packageHits) {
      if (openIds.has(hit.id.toLowerCase()) || recentShown.has(hit.id.toLowerCase())) continue;
      results.push({ kind: "pkg-nuget", hit, ranges: computeHighlightRanges(hit.id, query.toLowerCase()) });
      if (all && ++added >= 4) break;
    }
    results.push({
      kind: "package-query",
      prefix: validPackageQuerySearchText(query),
    });
    results.push({ kind: "package-activity" });
  }
  if ((all || spotlightScope === "types") && query) {
    for (const match of spotlightTypeMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "type" });
  } else if (spotlightScope === "types" && !query) {
    for (const match of spotlightTypeMatches("").slice(0, 40)) results.push({ ...match, kind: "type" });
  }
  if ((all || spotlightScope === "members") && query) {
    for (const match of spotlightMemberMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "member" });
  }
  if (all) {
    results.push(...frameworkLibrarySpotlightResults(query).slice(0, 5));
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

function isNugetSearchResult(value: unknown): value is NugetSearchResult {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.version === "string"
    && (value.description === undefined || typeof value.description === "string");
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
// version (even before the version inventory arrives) so the control is never empty.
function versionOptionsHtml(pkg: AppPackage) {
  const entry = catalogRequests.packageVersions(pkg);
  const versions = entry.status === "available" ? [...entry.inventory.versions] : [pkg.version];
  if (entry.status === "available"
      && !versions.some(v => v.toLowerCase() === pkg.version.toLowerCase())) {
    versions.splice(entry.inventory.currentVersionInsertionIndex, 0, pkg.version);
  }
  return versions
    .map(v => `<option value="${escapeHtml(v)}" ${v.toLowerCase() === pkg.version.toLowerCase() ? "selected" : ""}>${escapeHtml(v)}</option>`)
    .join("");
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
  state.atLibraryRoot = false;
  state.packageLens = "overview";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  const firstLibrary = packageLibraries()[0];
  state.libraryScope = firstLibrary ? new Set([firstLibrary.id]) : null;
  state.selectedTypeId = defaultVisibleTypeId(loaded);
  reconcileAccessibilityFilter(loaded.types.find(item => item.id === state.selectedTypeId));
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  render();
  observeAsync(loadSelectionData(), "Loading selection data");
}

function ensurePackageVersions(pkg: AppPackage | null) {
  if (pkg?.source.kind !== "nuget.org") return Promise.resolve();
  return catalogRequests.ensurePackageVersions(pkg);
}

function packageComparisonControlsHtml(pkg: AppPackage) {
  return renderPackageComparisonTargets({
    package: pkg,
    packages: state.packages,
    ...packageComparisonTargets.get(pkg),
    versions: catalogRequests.packageVersions(pkg),
  }, escapeHtml);
}

function updatePackageComparisonControls() {
  const pkg = state.package;
  const controls = document.querySelector("#package-comparison-targets");
  if (!pkg || !controls) return;
  const focused = document.activeElement instanceof HTMLElement
    && controls.contains(document.activeElement) ? document.activeElement.id : "";
  controls.innerHTML = packageComparisonControlsHtml(pkg);
  bindPackageComparisonControls();
  if (focused) {
    const next = document.getElementById(focused)
      ?? controls.querySelector<HTMLElement>("#package-diff-target");
    next?.focus();
  }
}

function bindPackageComparisonControls() {
  const pkg = state.package;
  const controls = document.querySelector("#package-comparison-targets");
  if (!pkg || !controls) return;
  const apply = (change: () => void) => {
    try {
      change();
      updatePackageComparisonControls();
    } catch (error: unknown) {
      showToast(errorMessage(error));
    }
  };
  bindPackageComparisonTargets(controls, [...state.packages], {
    selectDiff: target => apply(() =>
      packageComparisonTargets.selectDiff(
        pkg, target, catalogRequests.packageVersions(pkg))),
    selectClone: target => apply(() =>
      packageComparisonTargets.selectClone(pkg, target)),
    retry: () => {
      catalogRequests.forgetPackage(pkg);
      observeAsync(ensurePackageVersions(pkg), "Loading package versions");
      updatePackageComparisonControls();
    },
  });
}

function bindCompareEvents() {
  bindCompareFrame(document, {
    selectMode: selectCompareMode,
    changeTarget: () => {
      // Change target returns to Package Overview's Comparison targets work
      // area; Compare renders no second target editor of its own.
      const control = currentCompareMode() === "clone"
        ? "#package-clone-target"
        : "#package-diff-target";
      state.atPackageRoot = true;
      state.atLibraryRoot = false;
      state.packageLens = "overview";
      contentFramePane = "detail";
      render();
      afterCurrentNavigationFrame(() => {
        document.querySelector<HTMLElement>(control)
          ?.focus({ preventScroll: true });
      });
    },
    retry: () => {
      if (currentCompareMode() === "clone") {
        const target = currentCompareCloneTarget();
        if (target?.kind !== "selection") return;
        compareClone.retry(target.selection);
        render();
        return;
      }
      const selection = currentLibraryApiDiffSelection();
      if (!selection || selection.target.kind !== "available") return;
      libraryApiDiff.retry(selection);
      render();
    },
  });
  bindLibraryApiDiffRows(document, {
    activateType: activateCompareType,
    activateMember: activateCompareMember,
  });
  bindCompareCloneRows(document, {
    selectRank: rank => {
      if (state.compareCloneSelectedRank === rank) return;
      state.compareCloneSelectedRank = rank;
      render();
    },
  });
}

// Repaint just the version <select> options without a full re-render, so an async index
// fetch never disturbs focus, scroll, or the rest of the workbench.
function updateVersionSelect(pkg: CatalogPackage) {
  if (!state.package || state.package !== pkg) return;
  if (currentCompareSubject() !== null) {
    render();
    return;
  }
  const select = document.querySelector("#package-version");
  if (select) select.innerHTML = versionOptionsHtml(state.package);
  updatePackageComparisonControls();
}

function capturePackageCoordinateView(): Pick<
  LoadPackageOptions, "packageLens" | "librarySelection"
> {
  const preserveLibrarySelection =
    state.atLibraryRoot
    || (state.rootKind === "package" && state.libraryScope?.size === 1);
  const library = preserveLibrarySelection
    && !aggregateLibrarySubjectIsActive()
    ? selectedLibrary()
    : null;
  return {
    packageLens: state.atPackageRoot ? state.packageLens : "overview",
    ...(preserveLibrarySelection ? {
      librarySelection: {
        id: library?.id ?? null,
        name: library?.name ?? null,
        asset: library?.asset ?? null,
        lens: state.libraryLens,
        activate: state.atLibraryRoot,
      },
    } : {}),
  };
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
    ...capturePackageCoordinateView(),
    invalidateWorkspaceShareBasis: true,
    loadingPresentation: "content",
    loadingFocusControl: "package-version",
  });
}

async function switchPackageFramework(
  newFramework: string,
  loadingFocusControl: PackageLoadingFocusControl = "package-framework",
) {
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
      ...capturePackageCoordinateView(),
      invalidateWorkspaceShareBasis: true,
      loadingPresentation: "content",
      loadingFocusControl,
    });
}


// Routes a blended result to the right navigation path per its kind.
function pickSpotlightResult(result: SpotlightResult) {
  if (!result) { closeSpotlight(); return; }
  switch (result.kind) {
    case "package-query":
      openPackageQueryRoute(result.prefix);
      break;
    case "package-activity":
      openPackageActivityRoute();
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
    case "framework-lib":
      observeAsync(
        openPlatformLibrary(
          result.assembly,
          result.pack,
          {
            deferPlatformPresentation: true,
            inPlace: result.loaded === true,
            tfm: result.tfm,
            version: result.version,
          }),
        "Opening a platform library");
      break;
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
  if (!canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    spotlight.reset();
    render({ synchronizeUrl: false });
    afterCurrentNavigationFrame(focusWorkbenchSearchOrHeading);
    return;
  }
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  const navigationSeq = navigationSequence.begin();
  spotlight.reset();
  const { rollbackSnapshot, retainedSnapshot } =
    captureWorkspaceConstructionSnapshots(navigationSeq);
  prepareUnpublishedWorkspace();
  let loadFailed = false;
  const loaded = await loadPackage(
    id,
    version,
    framework,
    {
      navigationSeq,
      deferWorkspacePublication: true,
      failureHandler: (message: string) => {
        loadFailed = true;
        failWorkspaceCatalogAction(
          message,
          rollbackSnapshot,
          () => loadPackageFromSpotlight(id, version, framework),
          focusWorkbenchSearchOrHeading,
        );
      },
    });
  if (!loaded && !loadFailed && navigationSequence.isCurrent(navigationSeq)) {
    failWorkspaceCatalogAction(
      `Couldn’t open ${id}@${version}.`,
      rollbackSnapshot,
      () => loadPackageFromSpotlight(id, version, framework),
      focusWorkbenchSearchOrHeading,
    );
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (loaded) {
    let destination: string;
    try {
      destination = (await buildStateUrl()).toString();
    } catch (error) {
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      failWorkspaceCatalogAction(
        `Couldn’t open ${id}@${version}: ${errorMessage(error)}`,
        rollbackSnapshot,
        () => loadPackageFromSpotlight(id, version, framework),
        focusWorkbenchSearchOrHeading,
      );
      return;
    }
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    publishCurrentWorkspace(retainedSnapshot);
    workspaceLocation.push(destination);
    render({ synchronizeUrl: false });
    focusTypeList(navigationGeneration, focusGeneration);
  }
}

// Only an exact catalog row creates demand for the shared Library inspection surface.
interface OpenPlatformLibraryOptions {
  deferPlatformPresentation?: boolean;
  scopeOnly?: boolean;
  inPlace?: boolean;
  navigationSeq?: number;
  retryAction?: RetryAction;
  tfm?: string | undefined;
  version?: string | undefined;
}

async function openPlatformLibrary(
  assembly: string,
  pack: string,
  options: OpenPlatformLibraryOptions = {},
) {
  const deferPlatformPresentation = options.deferPlatformPresentation === true;
  const scopeOnly = options.scopeOnly === true;
  const createsWorkspace = !scopeOnly && options.inPlace !== true;
  const capacityError = createsWorkspace ? "" : platformCoordinateCapacityError();
  if (capacityError) {
    showToast(capacityError);
    return undefined;
  }
  if (createsWorkspace && !canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    spotlight.reset();
    render({ synchronizeUrl: false });
    afterCurrentNavigationFrame(focusWorkbenchSearchOrHeading);
    return undefined;
  }
  const navigationGeneration = scopeOnly ? null : beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  const navigationSeq = options.navigationSeq ?? navigationSequence.begin();
  if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
  const hasPlatformRootParent =
    state.rootKind === "platform"
    && state.atPackageRoot
    && platformIsPresentedAsRoot();
  const tfm = options.tfm ?? platformScopeTfm();
  const version = options.version ?? state.platformSelection?.version;
  if (createsWorkspace) spotlight.reset();
  const construction = createsWorkspace
    ? captureWorkspaceConstructionSnapshots(navigationSeq)
    : null;
  const deferredRollbackSnapshot = deferPlatformPresentation && !construction
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  if (construction) prepareUnpublishedWorkspace();
  else spotlight.reset();
  try {
    const target = await ensurePlatformCatalog(tfm, version);
    if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
    const matches = target.rows.filter(row =>
      (!pack || row.pack === pack)
      && (platformLibraryKey(row) === assembly
        || row.assembly.toLowerCase() === assembly.replace(/\.dll$/i, "").toLowerCase()));
    if (matches.length !== 1) throw new Error(`Platform library '${assembly}' is not uniquely available in this target.`);
    const row = matches[0]!;
    if (!row.hasImplementation) throw new Error(`${row.assembly} has no runtime implementation to inspect.`);
    if (!deferPlatformPresentation) {
      installPlatformTarget(target);
      state.platformOpeningStatus = { loading: true, error: "" };
      platformLibraryRetry = () => openPlatformLibrary(assembly, pack, {
        ...options, tfm: target.tfm, version: target.version,
      });
      if (!scopeOnly) render();
    }
    startPlatformTargetWork(target);
    let pkg = runtimePackageForTarget(target);
    const alreadyLoaded = pkg?.assemblies.some(item => platformLibraryMatchesDescriptor(row, item));
    if (!alreadyLoaded) {
      const runtimeResult = await loadRuntimePackAssembly(
        target.tfm, platformAssemblyRequest(row), row.pack,
        () => navigationSequence.isCurrent(navigationSeq), target.version,
        row.file);
      if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
      pkg = runtimeResult.packageModel;
      if (!pkg) throw new Error(runtimeResult.failureMessage || `Could not inspect ${row.assembly}.`);
    }
    if (!pkg) throw new Error("The Platform inspection did not return a Library.");
    if (pkg.version !== target.version || pkg.activeFramework !== target.tfm) {
      throw new Error("The inspected Library does not match the selected Platform target.");
    }
    const libraries = pkg.assemblies.filter(item => platformLibraryMatchesDescriptor(row, item));
    if (libraries.length !== 1) throw new Error(`The Platform inspection did not return an exact descriptor for ${row.assembly}.`);
    const library = libraries[0]!;
    platformPackages.set(platformTargetKey(target), pkg);
    if (deferPlatformPresentation) installPlatformTarget(target, false);
    if (!state.packages.includes(pkg)) retainPackageModel(pkg);
    activatePackage(pkg, { resetAccessibility: true });
    state.libraryScope = new Set([library.id]);
    state.frameworkLibraryPresentation = {
      tfm: target.tfm,
      version: target.version,
      libraryId: library.id,
    };
    recordPlatformRecent(library.name, row.pack);
    state.platformOpeningStatus = { loading: false, error: "" };
    if (scopeOnly) return pkg;
    state.loading = false;
    state.atPackageRoot = false;
    state.atLibraryRoot = true;
    state.libraryLens = "overview";
    state.namespaceFilter = "";
    state.typeFilter = "";
    state.kindFilter = "";
    state.selectedTypeId = filteredTypes()[0]?.id ?? "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
    resetMemberFilters();
    if (!construction) {
      workspaceLocation.replace(
        location.href,
        withPlatformRootParentHistory(history.state, hasPlatformRootParent));
    }
    render();
    await loadSelectionData();
    if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
    if (construction) {
      const destination = (await buildStateUrl()).toString();
      if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
      publishCurrentWorkspace(construction.retainedSnapshot);
      workspaceLocation.push(destination);
      render({ synchronizeUrl: false });
    } else {
      ensureCurrentWorkspacePublished();
    }
    if (navigationGeneration != null)
      focusTypeList(navigationGeneration, focusGeneration);
    return pkg;
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return undefined;
    const rollbackSnapshot = construction?.rollbackSnapshot
      ?? deferredRollbackSnapshot;
    if (rollbackSnapshot) {
      failWorkspaceCatalogAction(
        `${deferPlatformPresentation ? "Could not open Library" : "Could not open Platform Library"}: ${errorMessage(error)}`,
        rollbackSnapshot,
        () => openPlatformLibrary(assembly, pack, { ...options, tfm, version }),
        focusWorkbenchSearchOrHeading);
      return undefined;
    }
    state.loading = false;
    state.platformOpeningStatus = { loading: false, error: `Could not open Platform Library: ${errorMessage(error)}` };
    platformLibraryRetry = options.retryAction ?? (() => openPlatformLibrary(assembly, pack, options));
    if (!selectedPlatformTarget()) {
      state.platformCatalogStatus = { ...state.platformOpeningStatus };
      platformCatalogRetry = platformLibraryRetry;
    }
    if (!scopeOnly) {
      if (scope() !== "platform") {
        state.rootKind = "platform";
        state.atPackageRoot = true;
        state.atLibraryRoot = false;
        state.workspaceSubjectOpen = false;
        state.home = false;
      }
      render();
    }
    return undefined;
  }
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
  spotlight.reset();
  selectWorkspacePackage(target, { publishInitial: true });
  if (retainedWorkspaces.activeWorkspaceId === null) return;
  focusTypeList(focusGeneration);
}

async function spotlightPlatformTypeIsAvailable(
  pkg: AppPackage,
  type: AppTypeSurface,
  navigationIsCurrent: () => boolean,
): Promise<boolean> {
  if (pkg.source.kind !== "platform") return true;
  try {
    await exactPlatformCatalogForType(pkg, type);
    return navigationIsCurrent();
  } catch (error) {
    if (navigationIsCurrent()) {
      showToast(
        `Could not open ${type.name}: the exact Platform target is unavailable: `
        + errorMessage(error));
    }
    return false;
  }
}

function activateSpotlightTypePackage(pkg: AppPackage) {
  const platformRootParent =
    pkg.source.kind === "platform"
    && state.rootKind === "platform"
    && platformIsPresentedAsRoot();
  activatePackage(pkg);
  state.platformPresentedAsRoot = platformRootParent;
  workspaceLocation.replace(
    location.href,
    withPlatformRootParentHistory(history.state, platformRootParent));
}

function navigationPreservesAggregateLibraryScope(pkg: AppPackage) {
  return packageIdentityEquals(state.package, pkg)
    && aggregateLibrarySubjectIsActive();
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
  const navigationSeq = navigationSequence.begin();
  if (!await spotlightPlatformTypeIsAvailable(
      pkg,
      type,
      () => navigationSequence.isCurrent(navigationSeq))) {
    return;
  }
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  spotlight.reset();
  const rollbackSnapshot = retainedWorkspaces.activeWorkspaceId === null
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  const preserveAggregate = navigationPreservesAggregateLibraryScope(pkg);
  state.home = false;
  activateSpotlightTypePackage(pkg);
  enterTypeSubject(type, { preserveAggregate });
  resetMemberFilters();
  state.selectedMemberKey = result.memberKey;
  state.selectedOverloadIndex = null;
  enterMemberScope({ preserveAggregate });
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  resetMemberSectionState();
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  if (rollbackSnapshot
    && !publishInitialLoadedWorkspace(rollbackSnapshot)) return;
  render({ synchronizeUrl: rollbackSnapshot === null });
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
  const navigationSeq = navigationSequence.begin();
  if (!await spotlightPlatformTypeIsAvailable(
      pkg,
      type,
      () => navigationSequence.isCurrent(navigationSeq))) {
    return;
  }
  const navigationGeneration = beginSpotlightNavigation();
  const focusGeneration = documentFocusGeneration;
  spotlight.reset();
  const rollbackSnapshot = retainedWorkspaces.activeWorkspaceId === null
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  const preserveAggregate = navigationPreservesAggregateLibraryScope(pkg);
  state.home = false;
  activateSpotlightTypePackage(pkg);
  enterTypeSubject(type, { preserveAggregate });
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  state.selectedBodyTarget = null;
  state.memberSource = { status: "idle" };
  state.memberCallGraph = null;
  state.memberCallGraphKey = "";
  state.memberCallGraphError = "";
  state.graphMemberNavigationError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
  state.annotatedDestinationError = "";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  if (rollbackSnapshot
    && !publishInitialLoadedWorkspace(rollbackSnapshot)) return;
  const selectionData = loadSelectionData();
  render({ synchronizeUrl: rollbackSnapshot === null });
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
  const [verb, ...rest] = value.split(/\s+/);
  const pkg = currentPackage();
  beginSpotlightNavigation();
  const argument = rest.join(" ");
  let operation;
  if (verb === "type") {
    const match = result?.targetTypeId
      ? pkg.types.find(item => item.id === result.targetTypeId)
      : pkg.types.find(item => item.name.toLowerCase() === argument.toLowerCase())
        || pkg.types.find(item => item.name.toLowerCase().includes(argument.toLowerCase()));
    if (match) {
      navigationSequence.begin();
      enterTypeSubject(match);
      state.selectedMemberKey = "";
      state.memberBrowseTypeId = "";
      state.selectedOverloadIndex = null;
      resetMemberFilters();
      operation = loadSelectionData();
    }
  } else if (verb === "show") {
    const match = availableTypeLenses()
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
    if (id) operation = loadPackageFromSpotlight(id, version, "");
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
  return state.libraryOpen
    || state.spotlightOpen
    || graphSourceIsOpen(state.graphSource)
    || documentViewerIsOpen(state.docViewer)
    || state.memberAnnotatedModal !== null
    || graphExplorer.isOpen;
}

function resolvedWorkspaceShareTabs():
BrowserWorkspaceShareState["tabs"] {
  const tabs: Array<BrowserWorkspaceShareState["tabs"][number]> = state.packages.filter(pkg => pkg.source.kind !== "platform").map((pkg, index) => ({
    id: `t${index}`,
    kind: pkg.isRuntimePack ? "group" : "package",
    source: pkg.isRuntimePack ? ":Platform" : pkg.id,
    version: pkg.version || null,
    framework: pkg.activeFramework || null,
    runtimeIdentifier: null,
  }));
  if (state.platformSelection && !tabs.some(tab => tab.kind === "group" && tab.source === ":Platform")) {
    tabs.splice(state.platformSlot < 0 ? tabs.length : Math.min(state.platformSlot, tabs.length), 0, {
      id: `t${tabs.length}`, kind: "group", source: ":Platform",
      version: state.platformSelection.version, framework: state.platformSelection.tfm,
      runtimeIdentifier: null,
    });
  }
  return tabs.map((tab, index) => ({ ...tab, id: `t${index}` }));
}

function workspaceCoordinateCount() {
  return resolvedWorkspaceShareTabs().length;
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
  return { tabs, resolvedTabs, preservesBasis };
}

function commitWorkspaceShareBasis(
  basis: BrowserWorkspaceShareState | null,
) {
  state.workspaceShareBasis = basis;
  sourceInspection.clearGraphSource();
}

function selectedTypeMetadataWorkspacePackages(): AppPackage[] {
  const active = currentPackage();
  if (active.isRuntimePack || active.source.kind === "platform")
    return [active];
  return state.packages
    .filter(pkg =>
      (pkg.id === active.id
        && pkg.version === active.version
        && pkg.activeFramework === active.activeFramework)
      || (!pkg.isRuntimePack && pkg.source.kind !== "platform"))
    .sort((left, right) =>
      left.id.localeCompare(right.id)
      || left.version.localeCompare(right.version)
      || left.activeFramework.localeCompare(right.activeFramework));
}

function typeMetadataWorkspaceIdentity(): string {
  return JSON.stringify(
    selectedTypeMetadataWorkspacePackages().map(pkg => ({
      package: pkg.id,
      version: pkg.version,
      framework: pkg.activeFramework,
    })));
}

function currentTypeMetadataSignature(
  type: AppTypeSurface,
  pkg: AppPackage,
  metadataLibraryIdentity = "",
): string {
  return typeMetadataSignature(
    type,
    pkg,
    metadataLibraryIdentity,
    typeMetadataWorkspaceIdentity());
}

function activeShareTabIndex(
  tabs: BrowserWorkspaceShareState["tabs"],
  resolvedTabs = tabs,
) {
  return state.rootKind === "platform"
    ? tabs.findIndex(tab => tab.kind === "group" && tab.source === ":Platform")
    : resolvedTabs.findIndex(tab => tab.kind === "package" && tab.source === state.package?.id
      && tab.version === state.package?.version && tab.framework === state.package?.activeFramework);
}

function captureWorkspaceUrlState(): WorkspaceUrlState | null {
  if (state.rootKind === "library") return null;
  if (!state.package && !state.platformSelection) return null;
  const currentScope = scope();
  const workspaceSubjectOpen = currentScope === "workspace";
  const platformRoot = currentScope === "platform";
  const packageSubjectOpen = currentScope === "package";
  const librarySubjectOpen = currentScope === "library";
  const structuralRootOpen =
    workspaceSubjectOpen || platformRoot || packageSubjectOpen || librarySubjectOpen;
  if (!workspaceSubjectOpen && state.pendingGraphMemberDeepLink) {
    throw new Error(
      "The pending graph member must resolve before this workspace can be shared.");
  }

  const captured = capturedShareTabs();
  const tabs = captured.tabs;
  const activeIndex = activeShareTabIndex(tabs, captured.resolvedTabs);
  const activeTab = tabs[activeIndex];
  if (!activeTab) return null;

  const basis = state.workspaceShareBasis;
  const { contexts, selectedContextId } = workspaceShareCaptureTopology(
    tabs,
    activeIndex,
    basis,
    captured.preservesBasis,
    !structuralRootOpen && state.memberSection === "call-graph");

  if (packageSubjectOpen
    && state.packageLens === "dependencies"
    && state.dependenciesGroupIndex !== null) {
    const selectedDependencyGroup =
      state.packageDependencies?.dependencyGroups.find(
        group => group.index === state.dependenciesGroupIndex);
    if (selectedDependencyGroup && !selectedDependencyGroup.isActive) {
      throw new Error(
        "The selected dependency group differs from the package target framework and cannot be shared.");
    }
  }
  const type = structuralRootOpen
    ? null
    : selectedType();
  const member = structuralRootOpen
    ? null
    : selectedMember(type);
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

  if (!packageSubjectOpen
    && state.libraryScope
    && state.libraryScope.size > 1) {
    throw new Error(
      "Select one library before sharing this Browser workspace.");
  }
  const library = selectedLibraryShareKey();
  const libraries =
    workspaceSubjectOpen || platformRoot || packageSubjectOpen || !library
      ? []
      : [library];
  return {
    package: state.rootKind === "platform" ? "" : state.package?.id ?? "",
    subject: workspaceSubjectOpen ? "workspace" : null,
    tabs,
    contexts,
    activeTabId: activeTab.id,
    selectedContextId,
    view: {
      lens: workspaceSubjectOpen || platformRoot
        ? null
        : packageSubjectOpen
          ? state.packageLens
          : librarySubjectOpen
            ? `library:${state.libraryLens}`
            : state.lens,
      type: structuralRootOpen
        ? null
        : state.selectedTypeId || null,
      memberAnchor,
      memberSignature,
      section: member && state.memberSection !== "overview"
        ? state.memberSection
        : null,
      libraries,
    },
  };
}

async function captureSavedWorkspacePacket(): Promise<string> {
  if (state.home || state.credits || state.packageQueryOpen
    || state.packageActivityOpen
    || scope() !== "workspace") {
    throw new Error("Save is only available on the Workspace page.");
  }
  if (!state.engineReady || state.loading || state.error) {
    throw new Error("Wait until the Workspace is ready before saving.");
  }
  if (activeRetainedWorkspacePosting !== null) {
    return activeRetainedWorkspacePosting.canonicalPacket;
  }
  const snapshot = captureWorkspaceUrlState();
  if (!snapshot || snapshot.tabs.length === 0) {
    throw new Error("Load a package before saving the Workspace.");
  }
  const resolvedTabs = resolvedWorkspaceShareTabs();
  const tabs = snapshot.tabs.map((tab, index) => {
    const resolvedTab = resolvedTabs[index];
    if (tab.kind === "group" && tab.source === ":Platform") {
      if (resolvedTab?.kind !== "group"
        || resolvedTab.source !== ":Platform"
        || !resolvedTab.version
        || !resolvedTab.framework) {
        throw new Error("The Platform target must be pinned before saving.");
      }
      return {
        ...tab,
        version: resolvedTab.version,
        framework: resolvedTab.framework,
      };
    }
    const pkg = resolvedTab?.kind === "package"
      ? state.packages.find(candidate => candidate.id === resolvedTab.source
        && candidate.version === resolvedTab.version
        && candidate.activeFramework === resolvedTab.framework)
      : null;
    if (!pkg?.version || !pkg.activeFramework) {
      throw new Error("Every saved coordinate must have a resolved version and framework.");
    }
    return { ...tab, version: pkg.version, framework: pkg.activeFramework };
  });
  const captured = await engineClient.catalog.captureCompleteWorkspaceShareState({
    tabs,
    contexts: snapshot.contexts,
    activeTabId: snapshot.activeTabId,
    selectedContextId: snapshot.selectedContextId,
    view: snapshot.view,
  });
  if (!captured.succeeded || !captured.packet) {
    throw new Error(
      captured.failure?.message
        ?? "The Workspace could not be captured as a complete share packet.",
    );
  }
  return captured.packet;
}

async function buildStateUrl(base = location.href): Promise<URL> {
  const snapshot = captureWorkspaceUrlState();
  return snapshot
    ? await workspaceLocation.build(snapshot, base)
    : new URL(base);
}

async function buildShareUrl(base = location.href): Promise<URL> {
  if (state.workspaceFeedUrl)
    return new URL(state.workspaceFeedUrl);
  const snapshot = captureWorkspaceUrlState();
  return snapshot
    ? await workspaceLocation.build(snapshot, base)
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

let syncUrlRevision = 0;

function syncUrl() {
  if (activeRetainedWorkspacePosting !== null) return;
  if (currentPackageQueryHandoff()) return;
  if (pendingDemoNavigation
    && navigationSequence.isCurrent(pendingDemoNavigation.navigationSeq)) return;
  if (pendingWorkspaceConstruction
    && navigationSequence.isCurrent(
      pendingWorkspaceConstruction.navigationSeq)) return;
  if (workspaceFeedActivation?.blocksUrlSynchronization) return;
  if (retainFailedWorkspaceUrl()) return;
  const pushFromProductDemos =
    isProductHomeDemosPath(location.pathname);
  if (state.loading) return;
  document.title = `dotnet-inspect -- ${packageDisplayName(state.package)}`;
  if (state.workspaceFeedUrl) {
    workspaceLocation.replace(
      state.workspaceFeedUrl,
      history.state);
    return;
  }
  const navigationSeq = navigationSequence.current();
  if (state.atPackageRoot && state.package) {
    const revision = ++syncUrlRevision;
    void buildStateUrl().then(
      url => {
        if (revision !== syncUrlRevision
          || !navigationSequence.isCurrent(navigationSeq)) return undefined;
        const destination = url.toString();
        if (pushFromProductDemos) {
          workspaceLocation.push(destination);
        } else {
          workspaceLocation.replace(destination, history.state);
        }
        if (retainedWorkspaces.activeWorkspaceId !== null)
          activeWorkspaceUrl = destination;
        return undefined;
      },
      () => {
        // Keep the current URL while the package view is not projectable.
        return undefined;
      },
    );
    return;
  }
  let snapshot: WorkspaceUrlState | null;
  try {
    snapshot = captureWorkspaceUrlState();
  } catch {
    return;
  }
  if (!snapshot) return;
  const revision = ++syncUrlRevision;
  const publish = (url: URL) => {
    if (revision !== syncUrlRevision
      || !navigationSequence.isCurrent(navigationSeq)) return;
    const destination = url.toString();
    if (pushFromProductDemos) {
      workspaceLocation.push(destination);
    } else {
      workspaceLocation.replace(destination, history.state);
    }
    if (retainedWorkspaces.activeWorkspaceId !== null)
      activeWorkspaceUrl = destination;
  };
  if (!pushFromProductDemos) {
    void workspaceLocation.build(snapshot).then(
      publish,
      () => {
        // Keep the current URL while the active Browser state is not projectable.
      },
    );
    return;
  }
  void workspaceLocation.build(snapshot).then(
    publish,
    () => {
      // Keep the current URL while the active Browser state is not projectable.
      return undefined;
    },
  );
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
  requestedLibraryLens: LibraryLens | null = null,
  requestedPackageLens: PackageLens | null = null,
): string | null {
  if (requestedPackageLens) return null;
  if (requestedLibraryLens) {
    if (!libraryLensesFor(pkg).some(([id]) => id === requestedLibraryLens)) {
      return `The shared Library '${requestedLibraryLens}' inspector is not available for ${pkg.id}.`;
    }
    const aggregateLibrarySubject =
      state.rootKind === "package"
      && state.libraryScope === null
      && aggregateLibrarySubjectIsAvailable();
    if (!aggregateLibrarySubject
      && (state.libraryScope?.size !== 1 || !selectedLibrary())) {
      return "The shared Library view requires one available library.";
    }
    if (deep.type || deep.memberAnchor || deep.memberSignature || deep.section) {
      return "The shared Library view cannot also select a type or member.";
    }
    return null;
  }
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
  state.memberSource = { status: "idle" };
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
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
  // aligned, while preserving the package-backed aggregate Library scope.
  const selected = pkg.types.find(item => item.id === state.selectedTypeId);
  if (selected) {
    reconcileAccessibilityFilter(selected);
    if (!state.atPackageRoot
      && !state.atLibraryRoot
      && (state.rootKind === "platform" || state.libraryScope !== null)) {
      state.libraryScope = new Set([libraryKey(selected)]);
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
    // Compare work is reconciled by render() from the retained Package mode.
    case "compare": return undefined;
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
  if (state.atPackageRoot || state.atLibraryRoot) return undefined;
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
    case "facts": return loadSelectedMemberFactsSurface();
    case "overview": return loadSelectedMemberDocumentation();
    case "compare": return undefined;
    default: return assertNever(state.memberSection, "member section");
  }
}

async function share() {
  const focusOwner = captureApplicationMenuFocusOwner(document);
  try {
    if (!navigator.clipboard
      || typeof navigator.clipboard.write !== "function"
      || typeof ClipboardItem !== "function") {
      throw new Error(
        "This browser cannot copy an asynchronously generated Workspace link.");
    }
    const content = buildShareUrl().then(url =>
      new Blob([url.toString()], { type: "text/plain" }));
    await navigator.clipboard.write([
      new ClipboardItem({ "text/plain": content }),
    ]);
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
  const detail = errorMessage(error);
  const summary = detail.split(/\r?\n/u, 1)[0]?.trim() || detail.trim();
  if (/\b404\b|not\s*found/i.test(summary)) {
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
    message: `Couldn’t load “${packageId}”: ${summary || "unknown error"}`
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
function renderHomeView(preservedFocus: HomeFocusTarget | null) {
  document.title = "dotnet-inspect -- Inspect .NET packages and libraries in your browser.";
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
    <div class="home"${state.settings || state.libraryOpen ? " inert" : ""}>
      <header class="home-bar">
        ${renderBrand()}
        <div class="home-bar-actions">
          <a class="home-link" href="https://github.com/richlander/dotnet-inspect" target="_blank" rel="noreferrer">GitHub</a>
          <button id="home-open-library" type="button"
            ${enginePending ? "disabled" : ""}>Open Library…</button>
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
          <h1 class="home-title">Inspect .NET packages and libraries in your browser.</h1>
          <p class="home-lede">
            <span class="home-lede-wide">Search a package, type, or member. Explore APIs, metadata, dependencies, call graphs, and decompiled C# — computed locally in this tab.</span>
            <span class="home-lede-narrow">Search packages, types, and members, then explore APIs, metadata, dependencies, and decompiled C#.</span>
          </p>
          <div class="home-search ${enginePending ? "engine-pending" : ""}" role="search" aria-busy="${enginePending}">
            ${spotlight.inlineHtml(enginePending, showReadyGlint)}
            ${enginePending
              ? `<div class="home-engine-status" role="status" aria-live="polite">
                  <span class="loader" aria-hidden="true"></span>
                  <strong>${escapeHtml(state.engineStatus)}</strong>
                </div>`
              : ""}
          </div>
          <div class="home-demos">
            <div class="home-demos-copy">
              <strong>Product demos</strong>
              <span>Start from a curated package query.</span>
            </div>
            <div class="home-demo-row" aria-busy="${enginePending}">
              ${homeDemosEntryHtml(
                enginePending,
                productHomeDemoCatalogError,
                escapeHtml)}
            </div>
          </div>
          <p class="home-trust">Nothing to install. Inspected content stays in your browser.</p>
        </div>
        <aside class="home-art ${enginePending ? "engine-pending" : "engine-ready"}" style="--home-bot-animation-delay: ${botAnimationDelay}ms">
          ${homeArtSvg()}
          <p class="home-art-caption">Types, methods, metadata, dependencies, source, analysis, and diffs.</p>
        </aside>
      </main>
      ${dataBarHtml({
        buildIdentity: state.buildIdentity,
      }, escapeHtml)}
    </div>
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${renderLibraryOpenDialog({
      open: state.libraryOpen,
      busy: state.libraryOpenBusy,
      error: state.libraryOpenError,
    }, escapeHtml)}`;
  bindHomeEvents(preservedFocus);
  bindLibraryOpenEvents();
  if (state.settings) {
    if (!preservedFocus
      || !settingsOwnsHomeFocusTarget(preservedFocus)
      || !restoreHomeFocus(preservedFocus)) {
      focusSettingsEntry();
    }
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
  onOpenLibrary: () => openLibraryDialog("home"),
  onToggleTheme: toggleTheme,
};

function bindHomeEvents(preservedFocus: HomeFocusTarget | null) {
  const focusRenderGeneration = ++homeFocusRenderGeneration;
  bindSettingsPanelEvents();
  bindHomeShell(document, homeShellActions);
  spotlight.bind(document, "inline");
  if (state.settings) return;
  if (diagnosticsDestinationFocusPending) return;
  const destinationFocusGeneration = diagnosticsDestinationFocusGeneration;
  if (destinationFocusGeneration !== null) {
    if (documentFocusGeneration !== destinationFocusGeneration) {
      diagnosticsDestinationFocusGeneration = null;
    } else {
      afterCurrentNavigationFrame(() => {
        if (diagnosticsDestinationFocusGeneration
          !== destinationFocusGeneration) return;
        if (documentFocusGeneration !== destinationFocusGeneration) {
          diagnosticsDestinationFocusGeneration = null;
          return;
        }
        if (focusLevelOneHeading()) {
          diagnosticsDestinationFocusGeneration = documentFocusGeneration;
        }
      });
      return;
    }
  }
  if (preservedFocus && restoreHomeFocus(preservedFocus)) {
    if (preservedFocus === pendingHomeFocusTarget) {
      pendingHomeFocusTarget = null;
    }
    return;
  }
  const focusGeneration = documentFocusGeneration;
  afterCurrentNavigationFrame(() => {
    if (focusRenderGeneration !== homeFocusRenderGeneration) return;
    if (pendingHomeFocusTarget) return;
    if (focusGeneration !== documentFocusGeneration) return;
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
    } else if (input
      && state.packageActivityReturnFocusPending
      && state.packageActivityReturnFocus === "home-search") {
      input.focus();
      state.packageActivityReturnFocus = null;
      state.packageActivityReturnFocusPending = false;
    } else if (state.packageActivityReturnFocusPending
      && state.packageActivityReturnFocus === "home-search"
      && focusLevelOneHeading()) {
      state.packageActivityReturnFocus = null;
      state.packageActivityReturnFocusPending = false;
    } else {
      input?.focus();
    }
  });
}

function focusWorkspaceOrHeading(): void {
  if (!focusWorkspace(document)) {
    focusLevelOneHeading();
  }
}

function openProductDemos(): void {
  dismissModalsForRoutedNavigation();
  navigationSequence.begin();
  state.loading = false;
  clearNavigationError();
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  supersedeRetainedLocationIntentForRoutedNavigation();
  state.home = false;
  state.credits = false;
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  spotlight.reset();
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  workspaceLocation.push("/demos");
  render();
  afterCurrentNavigationFrame(() =>
    focusWorkspaceOrHeading());
}

function renderProductDemosPage(): void {
  document.title = "Demos — dotnet-inspect";
  app.innerHTML = `
    <div class="home demos-page"${state.settings || state.keyboardHelp || state.libraryOpen ? " inert" : ""}>
      <header class="home-bar">
        ${renderBrand()}
        <div class="home-bar-actions">
          <a class="home-link" href="/">Home</a>
          <button id="home-open-library" type="button">Open Library…</button>
          <button id="home-settings" aria-label="Open settings" title="Settings">⚙</button>
          <button id="home-theme" aria-label="Switch theme">${state.theme === "dark" ? "light" : "dark"}</button>
        </div>
      </header>
      <div class="notice-stack">${renderQueryNotice()}</div>
      <main class="detail-scroll demos-content">
        ${productHomeDemosViewHtml(escapeHtml, productHomeDemoCatalogError)}
      </main>
      ${dataBarHtml({ buildIdentity: state.buildIdentity }, escapeHtml)}
      ${state.spotlightOpen ? spotlight.modalHtml() : ""}
    </div>
    ${state.settings ? renderSettingsViewHtml() : ""}
    ${state.keyboardHelp ? renderKeyboardHelpDialog(keyboardHelpBindings) : ""}
    ${renderLibraryOpenDialog({
      open: state.libraryOpen,
      busy: state.libraryOpenBusy,
      error: state.libraryOpenError,
    }, escapeHtml)}`;
  bindHomeShell(document, homeShellActions);
  bindLibraryOpenEvents();
  bindWorkspaceSubjectEvents();
  bindSettingsPanelEvents();
  if (state.spotlightOpen) spotlight.bind(document, "modal");
}

// Workspace demo actions use product ids from engine `listHomeDemos` /
// `RunHomeDemo` (`EcosystemPackCatalog` / CLI `demo <id>`). Every demo executes
// through the product-resolved workspace and typed activation result.
function openDefaultWorkspace(): void {
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  render();
  afterCurrentNavigationFrame(() =>
    focusWorkspaceOrHeading());
}

function runHomeDemo(kind: ProductHomeDemoId) {
  observeAsync(resolveAndRunHomeDemo(kind), "Loading the demo workspace");
}

async function resolveAndRunHomeDemo(kind: ProductHomeDemoId): Promise<void> {
  if (!canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    render({ synchronizeUrl: false });
    return;
  }
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  const navigationSeq = beginDemoNavigation(location.href);
  const snapshot = captureCanonicalWorkspaceRestoreSnapshot();
  try {
    state.loading = true;
    state.loadingMessage = "Loading product demo…";
    state.loadingSubtitle = "Resolving the product workspace and view…";
    render();
    const construction =
      captureWorkspaceConstructionSnapshots(navigationSeq);
    state.home = false;
    prepareUnpublishedWorkspace();
    await runEngineHomeDemo(
      kind,
      construction.rollbackSnapshot ?? snapshot,
      construction.retainedSnapshot,
      navigationSeq,
    );
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    failDemoWorkspaceOpen(
      kind,
      errorMessage(error),
      snapshot,
      true);
  } finally {
    cancelDemoNavigation(navigationSeq);
  }
}

function openWorkspacePackagePicker(): void {
  if (!state.engineReady || state.loading || state.error) {
    showToast("Workspace is not ready to add a package.");
    return;
  }
  beginSpotlightNavigation();
  spotlight.openForPackageAddition({
    pickResult: result =>
      observeAsync(addWorkspacePackage(result), "Adding the Workspace package"),
    focusAfterDismiss: () => {
      const navigationGeneration = spotlightFocusGeneration;
      const focusGeneration = documentFocusGeneration;
      afterCurrentNavigationFrame(() => {
        if (canRestoreWorkbenchFocus(navigationGeneration, focusGeneration)) {
          restoreWorkspaceFocus(document, { kind: "add-package" });
        }
      });
    },
  });
}

async function addWorkspacePackage(result: SpotlightPackageResult): Promise<void> {
  const coordinate = result.kind === "pkg-loaded"
    ? { id: result.pkg.id, version: result.pkg.version,
        framework: result.pkg.activeFramework ?? "" }
    : result.kind === "pkg-recent"
      ? { id: result.entry.id, version: result.entry.version ?? "latest",
          framework: result.entry.framework ?? "" }
      : { id: result.hit.id, version: result.hit.version ?? "latest", framework: "" };
  beginSpotlightNavigation();
  spotlight.reset();
  const existing = state.packages.find(pkg =>
    pkg.id.toLowerCase() === coordinate.id.toLowerCase()
    && pkg.version.toLowerCase() === coordinate.version.toLowerCase()
    && (!coordinate.framework
      || pkg.activeFramework.toLowerCase() === coordinate.framework.toLowerCase()));
  if (existing) {
    render({ synchronizeUrl: false });
    showToast(`${existing.id} is already in Workspace.`);
    focusInspectionResult(navigationSequence.current());
    return;
  }
  const limitMessage =
    `Workspace holds at most ${MAX_WORKSPACE_PACKAGES} coordinates. Remove a package before adding another.`;
  if (workspaceCoordinateCount() >= MAX_WORKSPACE_PACKAGES) {
    appendQueryNotice(limitMessage, null);
    render({ synchronizeUrl: false });
    afterCurrentNavigationFrame(() =>
      restoreWorkspaceFocus(document, { kind: "add-package" }));
    return;
  }

  const navigationSeq = beginDemoNavigation(location.href);
  const snapshot = captureCanonicalWorkspaceRestoreSnapshot();
  state.loading = true;
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  render();
  let packageModel: AppPackage | null = null;
  try {
    packageModel = await packageAcquisition.loadPackage({
      packageId: coordinate.id,
      version: coordinate.version,
      framework: coordinate.framework,
      // Background acquisition can fill the last slot while this request waits.
      isCurrent: () => navigationSequence.isCurrent(navigationSeq)
        && workspaceCoordinateCount() < MAX_WORKSPACE_PACKAGES,
    });
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (!packageModel) throw new Error(limitMessage);
    invalidateWorkspaceMembershipViews();
    if (!state.package) {
      activatePackage(packageModel, { resetAccessibility: true });
      state.requestedPackage = packageModel.id;
      state.requestedVersion = packageModel.version;
      state.requestedFramework = packageModel.activeFramework;
    }
    state.workspaceSubjectOpen = true;
    state.atPackageRoot = true;
    state.loading = false;
    const destination = (await buildStateUrl()).toString();
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    ensureCurrentWorkspacePublished();
    stageDemoNavigation(navigationSeq, destination);
    if (!commitDemoNavigation(navigationSeq)) return;
    render();
    focusInspectionResult(navigationSeq);
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    failWorkspaceCatalogAction(
      `Adding ${coordinate.id} failed: ${errorMessage(error)}`,
      packageModel ? snapshot : null,
      () => observeAsync(addWorkspacePackage(result), "Retrying the Workspace package"),
      () => restoreWorkspaceFocus(document, { kind: "add-package" }),
    );
  } finally {
    cancelDemoNavigation(navigationSeq);
  }
}

function openSavedWorkspace(entry: SavedWorkspace): void {
  observeAsync(
    openSavedWorkspaceEntry(entry),
    "Opening saved Workspace",
  );
}

async function openSavedWorkspaceEntry(entry: SavedWorkspace): Promise<void> {
  if (entry.kind !== "complete") {
    const controller = retainedWorkspaceActivation;
    if (controller === null) {
      await openSavedWorkspaceCore(entry);
      return;
    }
    if (controller.state.pendingDefinitionId !== null) {
      controller.cancelPending();
      await controller.waitForPendingCommit();
    }
    const activeDefinitionId = controller.state.activeDefinitionId;
    if (activeDefinitionId === null) {
      await openSavedWorkspaceCore(entry);
      return;
    }
    await deleteActiveManagedWorkspace(
      controller,
      activeDefinitionId,
      () => openSavedWorkspaceCore(entry),
    );
    clearWorkspaceFeedIdentity();
    retainedWorkspacePostings.delete(activeDefinitionId);
    return;
  }

  if (!canPublishRetainedWorkspace()) {
    throw new Error(retainedWorkspaceCapacityMessage());
  }
  const activationNavigationSeq = navigationSequence.begin();
  const controller = requireRetainedWorkspaceActivation();
  if (controller.state.activeDefinitionId === null
    && retainedWorkspaces.activeWorkspaceId !== null) {
    workspaceLocation.replace(location.href, history.state);
  }
  const url = new URL("/", location.origin);
  url.searchParams.set("w", entry.packet);
  url.hash = "workspace";
  const destination = url.toString();
  const locationIntent = retainedLocationIntents.admitNonBrowser(
    "push",
    installedRetainedLocation,
    history,
  );
  const definition = controller.retain({
    label: entry.name,
    canonicalLocation: destination,
    canonicalPacket: entry.packet,
  });
  issuedManagedRetainedDefinitionIds.add(definition.id);
  let result: BrowserRetainedWorkspaceActivationResult;
  try {
    const activation = controller.activate(
      definition.id,
      () => retainedLocationIntents.currentIntentId === locationIntent.id,
      posting => installRetainedWorkspacePosting(posting, locationIntent),
      undefined,
      undefined,
      posting => retainedLocationPresentationCurrent(
        locationIntent,
        posting.canonicalLocation,
      ),
    );
    render({ synchronizeUrl: false });
    result = await activation;
  } catch (error) {
    failedManagedRetainedDefinitionId = definition.id;
    realignRetainedLocationIntent(locationIntent, "failed");
    render({ synchronizeUrl: false });
    throw error;
  }
  if (result.status === "failed") {
    failedManagedRetainedDefinitionId = definition.id;
    realignRetainedLocationIntent(locationIntent, "failed");
    render({ synchronizeUrl: false });
    throw new Error(
      result.failure?.message
        ?? "The complete saved Workspace could not be activated.",
    );
  }
  if (result.status === "activated" || result.status === "noEffect") {
    failedManagedRetainedDefinitionId = null;
    completeRetainedActivationPresentation(
      result,
      locationIntent,
      activationNavigationSeq,
    );
    clearWorkspaceFeedIdentity();
  }
}

async function openSavedWorkspaceCore(entry: SavedWorkspace): Promise<void> {
  if (!canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    render({ synchronizeUrl: false });
    return;
  }
  let rollbackSnapshot: CanonicalWorkspaceRestoreSnapshot | null = null;
  const fail = (message: string, retryable: boolean) =>
    failWorkspaceCatalogAction(
      `Saved Workspace "${entry.name}" failed: ${message}`,
      rollbackSnapshot ?? captureCanonicalWorkspaceRestoreSnapshot(),
      retryable ? () => openSavedWorkspace(entry) : null,
      () => restoreWorkspaceFocus(
        document, { kind: "saved-open", name: entry.name, index: 0 }));
  const url = new URL("/", location.origin);
  url.searchParams.set("w", entry.packet);
  url.hash = "workspace";
  const destination = url.toString();
  const navigationSeq = beginDemoNavigation(destination);
  let loc: ParsedLocation;
  try {
    loc = await parseWorkspaceHref(destination);
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) {
      cancelDemoNavigation(navigationSeq);
      return;
    }
    try {
      fail(errorMessage(error), false);
    } finally {
      cancelDemoNavigation(navigationSeq);
    }
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) {
    cancelDemoNavigation(navigationSeq);
    return;
  }
  const construction =
    captureWorkspaceConstructionSnapshots(navigationSeq);
  prepareUnpublishedWorkspace();
  rollbackSnapshot =
    construction.rollbackSnapshot
    ?? construction.supersessionSnapshot;
  await restoreWorkspaceCatalogEntry(
    loc,
    navigationSeq,
    rollbackSnapshot,
    construction.retainedSnapshot,
    message => fail(message, true));
}

async function restoreWorkspaceCatalogEntry(
  loc: ParsedLocation,
  navigationSeq: number,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  previousSnapshot: CanonicalWorkspaceRestoreSnapshot | null,
  failureHandler: WorkspaceRestoreFailureHandler,
): Promise<void> {
  let failed = false;
  const fail = (message: string) => {
    failed = true;
    failureHandler(message);
  };
  try {
    await restoreWorkspaceFromLocation(
      loc,
      loc,
      navigationSeq,
      snapshot,
      false,
      fail,
      false);
    if (!failed
      && navigationSequence.isCurrent(navigationSeq)
      && (state.package || state.platformSelection)) {
      const destination = (await buildStateUrl()).toString();
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      const publication = stageCurrentWorkspacePublication(
        previousSnapshot,
        destination);
      if (!commitStagedWorkspaceNavigation(navigationSeq, publication)) {
        fail("The saved Workspace could not commit its destination.");
        return;
      }
      syncUrl();
      render({ synchronizeUrl: false });
      focusInspectionResult(navigationSeq);
    }
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) {
      fail(errorMessage(error));
    }
  } finally {
    cancelDemoNavigation(navigationSeq);
  }
}

async function restoreFreshWorkspaceFromHistory(
  loc: ParsedLocation,
  navigationSeq: number,
): Promise<void> {
  if (!canPublishRetainedWorkspace()) {
    const snapshot = captureCanonicalWorkspaceRestoreSnapshot();
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      retainedWorkspaceCapacityMessage(),
      snapshot,
      () => {
        const retrySeq = navigationSequence.begin();
        observeAsync(
          restoreFreshWorkspaceFromHistory(loc, retrySeq),
          "Retrying workspace history");
      });
    return;
  }
  const construction =
    captureWorkspaceConstructionSnapshots(navigationSeq);
  prepareUnpublishedWorkspace();
  const rollbackSnapshot = construction.supersessionSnapshot;
  let failed = false;
  const fail = (message: string) => {
    failed = true;
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      message,
      rollbackSnapshot,
      () => {
        const retrySeq = navigationSequence.begin();
        observeAsync(
          restoreFreshWorkspaceFromHistory(loc, retrySeq),
          "Retrying workspace history");
      });
  };
  try {
    await restoreWorkspaceFromLocation(
      loc,
      loc,
      navigationSeq,
      rollbackSnapshot,
      false,
      fail,
      false);
    if (!failed
      && navigationSequence.isCurrent(navigationSeq)
      && (state.package || state.platformSelection)) {
      const destination = (await buildStateUrl()).toString();
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      clearWorkspaceFeedIdentity();
      publishCurrentWorkspace(construction.retainedSnapshot);
      workspaceLocation.replace(destination, history.state);
      render({ synchronizeUrl: false });
    }
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) {
      fail(errorMessage(error));
    }
  }
}

async function restoreRetainedWorkspaceFromHistory(
  loc: ParsedLocation,
  navigationSeq: number,
): Promise<void> {
  const rollbackSnapshot = captureWorkspaceMutationSnapshot(navigationSeq);
  let failed = false;
  const fail = (message: string) => {
    failed = true;
    discardPendingWorkspaceConstruction();
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      message,
      rollbackSnapshot,
      () => {
        const retrySeq = navigationSequence.begin();
        observeAsync(
          restoreRetainedWorkspaceFromHistory(loc, retrySeq),
          "Retrying workspace history");
      });
  };
  try {
    await restoreWorkspaceFromLocation(
      loc,
      loc,
      navigationSeq,
      rollbackSnapshot,
      false,
      fail,
      false);
    if (!failed && navigationSequence.isCurrent(navigationSeq)) {
      const destination = (await buildStateUrl()).toString();
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      clearWorkspaceFeedIdentity();
      discardPendingWorkspaceConstruction();
      activeWorkspaceUrl = destination;
      workspaceLocation.replace(destination, history.state);
      render({ synchronizeUrl: false });
    }
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) {
      fail(errorMessage(error));
    }
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
  snapshot: CanonicalWorkspaceRestoreSnapshot | null,
  retry: RetryAction,
  restoreFocus: () => boolean,
): void {
  if (snapshot && workspaceFeedRollbackTransfers.has(snapshot)) {
    recoverWorkspaceNavigationRollback(
      snapshot,
      () => failWorkspaceCatalogAction(
        message,
        snapshot,
        retry,
        restoreFocus));
    return;
  }
  discardPendingWorkspaceConstruction();
  if (snapshot) restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
  state.loading = false;
  state.error = "";
  state.errorTitle = "";
  state.errorDetail = "";
  state.retryAction = null;
  state.queryNotice = "";
  state.queryNoticeRetryAction = null;
  appendQueryNotice(message, retry);
  render();
  if (snapshot) restartRestoredWorkspaceSelectionData();
  afterCurrentNavigationFrame(() => {
    if (!restoreFocus()) {
      focusWorkspaceOrHeading();
    }
  });
}

// Return to the intro/home page without tearing down the warm engine or the loaded packages.
// Soft in-app navigation (pushState "/") so a refresh stays on home and Back returns to the
// workbench; the home search reuses the still-resident package list.
function goHome(): boolean {
  if (!clearWorkspaceRouteFailure()) {
    render();
    return false;
  }
  const navigationSeq = navigationSequence.begin();
  const initiatingFocusGeneration = documentFocusGeneration;
  try {
    supersedeRetainedLocationIntentForRoutedNavigation();
  } catch (error) {
    reportProductNavigationFailure(
      "home",
      error,
      navigationSeq,
      initiatingFocusGeneration,
      true,
    );
    return false;
  }
  if (!workspaceLocation.push("/")) {
    reportProductNavigationFailure(
      "home",
      new Error("Browser history could not be updated."),
      navigationSeq,
      initiatingFocusGeneration,
      true,
    );
    return false;
  }
  state.loading = false;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  invalidateGraphMemberNavigation();
  clearNavigationError();
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  state.credits = false;
  state.home = true;
  spotlight.reset();
  render();
  return true;
}

function currentProductDestination(): ProductDestination | null {
  if (isDiagnosticsPath(location.pathname)
    || isProductHomeDemosPath(location.pathname)
    || state.credits) return null;
  if (state.packageQueryOpen) return "query";
  if (state.packageActivityOpen) return "activity";
  if (state.workspaceSubjectOpen && !state.home) return "workspace";
  if (state.home && !state.credits) return "home";
  return null;
}

function productNavigationUnavailableReason(
  destination: ProductDestination,
): string | null {
  if (destination === "workspace"
    && state.package === null
    && state.platformSelection === null) {
    return "No workspace is open";
  }
  if (destination !== "query" && destination !== "activity") return null;
  if (!state.engineReady) return "Available after runtime startup completes";
  if (state.loading) return "Available after the current inspection loads";
  if (state.error) return "Available after the current inspection error is resolved";
  return null;
}

function focusProductNavigationButton(): void {
  document.querySelector<HTMLElement>("[data-product-navigation-button]")
    ?.focus({ preventScroll: true });
}

function productNavigationOwnsFocus(): boolean {
  return document.activeElement instanceof Element
    && (document.activeElement.closest("[data-product-navigation-button]")
        !== null
      || document.activeElement.closest("[data-product-navigation-menu]")
        !== null);
}

function navigateProductDestination(destination: ProductDestination): void {
  if (destination === currentProductDestination()) {
    focusProductNavigationButton();
    return;
  }
  if (destination === "home") {
    if (goHome()) {
      afterCurrentNavigationFrame(() => focusLevelOneHeading());
    }
    return;
  }
  if (destination === "query") {
    openPackageQueryRoute("", {
      preserveState: true,
      returnFocus: "application-query",
    });
    return;
  }
  if (destination === "activity") {
    openPackageActivityRoute("application-activity");
    return;
  }
  observeAsync(
    openWorkspaceProductDestination().then(completion => {
      if (completion === null) return undefined;
      requestAnimationFrame(() => {
        const releaseFocusParking = () => {
          if (completion.focusGeneration === null) return;
          if (document.activeElement === app) {
            return;
          }
          workspaceProductFocusParkingActive = false;
          app.removeAttribute("tabindex");
        };
        if (!navigationSequence.isCurrent(completion.navigationSeq)
          || completion.focusGeneration === null
          || !completion.restoreDestinationFocus
          || completion.focusGeneration !== documentFocusGeneration) {
          releaseFocusParking();
          return;
        }
        focusWorkspaceOrHeading();
        releaseFocusParking();
      });
      return undefined;
    }),
    "Opening the Workspace destination");
}

function openCredits() {
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  supersedeRetainedLocationIntentForRoutedNavigation();
  navigationSequence.begin();
  state.loading = false;
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
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

function diagnosticsRuntimeState(): DiagnosticsRuntimeState {
  if (state.engineStartupFailed) {
    return {
      kind: "failed",
      message: "The browser inspection engine did not start.",
      detail: state.errorDetail || state.error || null,
    };
  }
  if (state.engineRuntimeReady) {
    return {
      kind: "ready",
      message: ".NET is running in WebAssembly and ready for inspection.",
      diagnostics: state.diag,
      measurementsPending: !state.engineReady,
    };
  }
  return {
    kind: "loading",
    message: state.engineStatus
      || state.loadingMessage
      || "Starting the browser inspection engine.",
  };
}

function diagnosticsPackageCacheState(): DiagnosticsPackageCacheState {
  if (state.engineStartupFailed) {
    return {
      kind: "failed",
      message: "Package-cache statistics are unavailable because the inspection engine did not start.",
    };
  }
  if (state.packageCacheStatsStatus === "failed") {
    return {
      kind: "failed",
      message: state.packageCacheStatsError
        || "Package-cache statistics are unavailable.",
    };
  }
  if (state.packageCacheStatsStatus === "ready"
    && state.packageCacheStats) {
    return {
      kind: "ready",
      stats: state.packageCacheStats,
    };
  }
  return { kind: "loading" };
}

function diagnosticsBuildState(): DiagnosticsBuildState {
  if (state.buildIdentityStatus === "failed") {
    return {
      kind: "failed",
      message: state.buildIdentityError
        || "Product build identity is unavailable.",
    };
  }
  if (state.buildIdentityStatus === "ready" && state.buildIdentity) {
    return {
      kind: "ready",
      identity: state.buildIdentity,
    };
  }
  return { kind: "loading" };
}

function renderDiagnosticsPage() {
  const activeElement = document.activeElement;
  const activeId = activeElement?.id;
  const productNavigationFocused = Boolean(
    activeElement?.closest("[data-product-navigation-menu]"));
  const diagnosticsFocusWillBeReplaced =
    productNavigationFocused
    || activeId === "diagnostics-heading"
    || activeId === "diagnostics-product"
    || activeId === "diagnostics-commit"
    || activeId === "diagnostics-back";
  const focusTargetId = diagnosticsHeadingFocusPending
    || activeId === "diagnostics-heading"
    ? "diagnostics-heading"
    : productNavigationFocused
      ? "diagnostics-product"
      : activeId === "diagnostics-product"
      || activeId === "diagnostics-commit"
      || activeId === "diagnostics-back"
      ? activeId
      : null;
  if (diagnosticsFocusWillBeReplaced) {
    app.tabIndex = -1;
    app.focus({ preventScroll: true });
  }
  state.diagnosticsCapturedAtUtc ??= new Date().toISOString();
  document.title = "Diagnostics · dotnet-inspect";
  app.innerHTML = diagnosticsViewHtml({
    runtime: diagnosticsRuntimeState(),
    build: diagnosticsBuildState(),
    packageCache: diagnosticsPackageCacheState(),
    capturedAtUtc: state.diagnosticsCapturedAtUtc,
  }, value => escapeHtml(value));
  bindDiagnosticsView(document, {
    onBack: closeDiagnosticsRoute,
  });
  if (focusTargetId) {
    const focusGeneration = documentFocusGeneration;
    requestAnimationFrame(() => {
      const releaseFocusParking = () => {
        if (diagnosticsFocusWillBeReplaced) {
          app.removeAttribute("tabindex");
        }
      };
      if (!isDiagnosticsPath(location.pathname)) {
        diagnosticsHeadingFocusPending = false;
        releaseFocusParking();
        return;
      }
      if (focusGeneration !== documentFocusGeneration) {
        diagnosticsHeadingFocusPending = false;
        releaseFocusParking();
        return;
      }
      if (focusTargetId === "diagnostics-heading") {
        diagnosticsHeadingFocusPending = false;
        focusLevelOneHeading();
        releaseFocusParking();
        return;
      }
      document.getElementById(focusTargetId)?.focus({ preventScroll: true });
      releaseFocusParking();
    });
  }
}

function openDiagnosticsRoute() {
  dismissModalsForRoutedNavigation();
  supersedeRetainedLocationIntentForRoutedNavigation();
  navigationSequence.begin();
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  state.credits = false;
  state.home = false;
  state.diagnosticsCapturedAtUtc = new Date().toISOString();
  diagnosticsHeadingFocusPending = true;
  diagnosticsDestinationFocusPending = false;
  diagnosticsDestinationFocusGeneration = null;
  if (state.engineRuntimeReady) {
    state.packageCacheStatsStatus = "loading";
    state.packageCacheStatsError = "";
  }
  workspaceLocation.push(
    DIAGNOSTICS_PATH,
    diagnosticsHistoryState(history.state));
  render();
  if (state.engineRuntimeReady) refreshPackageStats();
}

function closeDiagnosticsRoute() {
  diagnosticsDestinationFocusPending = true;
  if (isDiagnosticsHistoryEntry(history.state)) {
    history.back();
    return;
  }
  replaceDiagnosticsWithHome();
}

function replaceDiagnosticsWithHome() {
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
  supersedeRetainedLocationIntentForRoutedNavigation();
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  state.credits = false;
  state.home = true;
  spotlight.reset();
  workspaceLocation.replace("/", history.state);
  render();
}

function focusPackageQueryInput() {
  afterCurrentNavigationFrame(() => {
    document.querySelector<HTMLElement>("#package-query-prefix")?.focus();
  });
}

function focusPackageActivityInput() {
  afterCurrentNavigationFrame(() => {
    const packageSet = document.querySelector<HTMLSelectElement>(
      "#package-changes-package-set");
    if (packageSet && !packageSet.disabled) {
      packageSet.focus();
      if (document.activeElement === packageSet) return;
    }
    focusLevelOneHeading();
  });
}

function afterNavigationFrame(navigationSeq: number, action: () => void) {
  requestAnimationFrame(() => {
    if (navigationSequence.isCurrent(navigationSeq)) action();
  });
}

function afterCurrentNavigationFrame(action: () => void) {
  const navigationSeq = navigationSequence.current();
  requestAnimationFrame(() => {
    if (navigationSequence.isCurrent(navigationSeq)) action();
  });
}

function scheduleDiagnosticsDestinationFocus() {
  if (!diagnosticsDestinationFocusPending
    || diagnosticsDestinationFocusScheduled
    || isDiagnosticsPath(location.pathname)) {
    return;
  }
  const navigationSeq = navigationSequence.current();
  const focusGeneration = documentFocusGeneration;
  diagnosticsDestinationFocusScheduled = true;
  requestAnimationFrame(() => {
    diagnosticsDestinationFocusScheduled = false;
    if (!diagnosticsDestinationFocusPending) return;
    if (!navigationSequence.isCurrent(navigationSeq)
      || isDiagnosticsPath(location.pathname)) {
      diagnosticsDestinationFocusPending = false;
      return;
    }
    const heading = document.querySelector<HTMLElement>("main h1");
    if (!heading) return;
    diagnosticsDestinationFocusPending = false;
    if (focusGeneration !== documentFocusGeneration) return;
    if (focusLevelOneHeading()) {
      diagnosticsDestinationFocusGeneration = documentFocusGeneration;
    }
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

function restorePackageRouteReturnFocus() {
  if (pendingWorkspaceConstruction !== null) return;
  restorePackageQueryReturnFocus();
  restorePackageActivityReturnFocus();
  restorePackageQueryWorkspaceFocus();
}

function restorePackageQueryReturnFocus() {
  if (!state.packageQueryReturnFocusPending) return;
  if (state.packageQueryReturnFocus === "application-query") {
    afterCurrentNavigationFrame(() => {
      if (focusRenderedElement(document.querySelector<HTMLElement>(
        "[data-product-navigation-button]"))) {
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

function restorePackageActivityReturnFocus() {
  if (!state.packageActivityReturnFocusPending) return;
  if (state.packageActivityReturnFocus === "application-activity") {
    afterCurrentNavigationFrame(() => {
      afterCurrentNavigationFrame(() => {
        if (focusRenderedElement(document.querySelector<HTMLElement>(
          "[data-product-navigation-button]")) || focusLevelOneHeading()) {
          state.packageActivityReturnFocus = null;
          state.packageActivityReturnFocusPending = false;
        }
      });
    });
    return;
  }
  if (state.packageActivityReturnFocus !== "package-search") return;
  afterCurrentNavigationFrame(() => {
    if (focusWorkbenchSearch(document) || focusLevelOneHeading()) {
      state.packageActivityReturnFocus = null;
      state.packageActivityReturnFocusPending = false;
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
  state.packageQueryState.termDraft = fresh.termDraft ?? null;
  state.packageQueryState.termEdits = fresh.termEdits ?? [];
  state.packageQueryInspection = null;
  packageQueryViewport = null;
}

function discardPackageQueryTermEditors() {
  state.packageQueryState.termDraft = null;
  state.packageQueryState.termEdits = [];
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

function applyPackageActivityHistory(historyState: unknown) {
  const activityHistory = readPackageActivityHistory(historyState);
  state.packageActivityOpenedFromApp = activityHistory !== null;
  state.packageActivityPredecessorEntryId =
    activityHistory?.predecessorEntryId ?? null;
  state.packageActivityReturnFocus = activityHistory?.returnFocus ?? null;
  state.packageActivityReturnFocusPending = false;
}

function openPackageQueryRoute(
  seed = "",
  options: {
    preserveState?: boolean;
    returnFocus?: PackageQueryReturnFocus;
  } = {},
): boolean {
  if (!state.engineReady || state.loading || state.error) return false;
  const returnFocus: PackageQueryReturnFocus = options.returnFocus
    ?? (state.home ? "home-search" : "package-search");
  const navigationSeq = navigationSequence.begin();
  const initiatingFocusGeneration = documentFocusGeneration;
  try {
    supersedeRetainedLocationIntentForRoutedNavigation();
  } catch (error) {
    reportProductNavigationFailure(
      "query",
      error,
      navigationSeq,
      initiatingFocusGeneration,
      returnFocus === "application-query",
    );
    return false;
  }
  const predecessorEntryId = ensureCurrentHistoryEntryId();
  const successorState = predecessorEntryId
    ? packageQueryHistoryState(
        null,
        crypto.randomUUID(),
        { predecessorEntryId, returnFocus })
    : null;
  if (!workspaceLocation.push("/query", successorState)) {
    reportProductNavigationFailure(
      "query",
      new Error("Browser history could not be updated."),
      navigationSeq,
      initiatingFocusGeneration,
      returnFocus === "application-query",
    );
    return false;
  }
  dismissModalsForRoutedNavigation();
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  packageQueryHandoffNavigationSeq = null;
  if (!options.preserveState) {
    resetPackageQueryState();
  }
  resetPackageQueryAnnouncements();
  if (!options.preserveState || seed) {
    state.packageQueryPrefix = validPackageQuerySearchText(seed);
  }
  state.packageQueryNavigationError = "";
  if (predecessorEntryId) {
    state.packageQueryOpenedFromApp = true;
    state.packageQueryPredecessorEntryId = predecessorEntryId;
    state.packageQueryReturnFocus = returnFocus;
    state.packageQueryReturnFocusPending = false;
  } else {
    applyPackageQueryHistory(null);
  }
  state.packageQueryOpen = true;
  state.packageActivityOpen = false;
  state.credits = false;
  state.home = false;
  render();
  focusPackageQueryInput();
  return true;
}

function openPackageActivityRoute(
  returnFocus: PackageActivityReturnFocus =
  state.home ? "home-search" : "package-search",
): boolean {
  if (!state.engineReady || state.loading || state.error) return false;
  const navigationSeq = navigationSequence.begin();
  const initiatingFocusGeneration = documentFocusGeneration;
  try {
    supersedeRetainedLocationIntentForRoutedNavigation();
  } catch (error) {
    reportProductNavigationFailure(
      "activity",
      error,
      navigationSeq,
      initiatingFocusGeneration,
      returnFocus === "application-activity",
    );
    return false;
  }
  const predecessorEntryId = ensureCurrentHistoryEntryId();
  const successorState = predecessorEntryId
    ? packageActivityHistoryState(
        null,
        crypto.randomUUID(),
        { predecessorEntryId, returnFocus })
    : null;
  if (!workspaceLocation.push(PACKAGE_ACTIVITY_PATH, successorState)) {
    reportProductNavigationFailure(
      "activity",
      new Error("Browser history could not be updated."),
      navigationSeq,
      initiatingFocusGeneration,
      returnFocus === "application-activity",
    );
    return false;
  }
  dismissModalsForRoutedNavigation();
  packageQueryController.cancel();
  packageChangesController.cancel("superseded");
  discardPackageQueryTermEditors();
  if (predecessorEntryId) {
    state.packageActivityOpenedFromApp = true;
    state.packageActivityPredecessorEntryId = predecessorEntryId;
    state.packageActivityReturnFocus = returnFocus;
    state.packageActivityReturnFocusPending = false;
  } else {
    applyPackageActivityHistory(null);
  }
  state.packageQueryOpen = false;
  state.packageActivityOpen = true;
  state.credits = false;
  state.home = false;
  render();
  focusPackageActivityInput();
  return true;
}

function reportProductNavigationFailure(
  destination: ProductDestination,
  error: unknown,
  navigationSeq: number,
  initiatingFocusGeneration: number,
  restoreProductNavigationFocus: boolean,
): void {
  const label = destination[0]!.toUpperCase() + destination.slice(1);
  console.error(`Opening ${label} failed.`, error);
  const message =
    `Opening ${label} failed: ${errorMessage(error) || "Unknown error."}`;
  const restoreInvokerFocus =
    restoreProductNavigationFocus
    && initiatingFocusGeneration === documentFocusGeneration;
  if (state.packageQueryOpen) {
    state.packageQueryNavigationError = message;
  } else {
    appendQueryNotice(message);
  }
  render();
  showToast(message);
  if (!restoreInvokerFocus) return;
  const focusGeneration = documentFocusGeneration;
  afterNavigationFrame(navigationSeq, () => {
    if (focusGeneration === documentFocusGeneration) {
      focusProductNavigationButton();
    }
  });
}

async function openWorkspaceProductDestination(): Promise<{
  readonly navigationSeq: number;
  readonly focusGeneration: number | null;
  readonly restoreDestinationFocus: boolean;
} | null> {
  const pkg = state.package;
  if (!pkg && !state.platformSelection) return null;
  const fallbackPackage = pkg?.source.kind === "platform" ? null : pkg;

  dismissModalsForRoutedNavigation();
  const navigationSeq = navigationSequence.begin();
  const initiatingFocusGeneration = documentFocusGeneration;
  const routeState = {
    packageQueryOpen: state.packageQueryOpen,
    packageActivityOpen: state.packageActivityOpen,
    credits: state.credits,
    home: state.home,
    workspaceSubjectOpen: state.workspaceSubjectOpen,
    atPackageRoot: state.atPackageRoot,
    atLibraryRoot: state.atLibraryRoot,
    selectedMemberKey: state.selectedMemberKey,
    memberBrowseTypeId: state.memberBrowseTypeId,
    selectedOverloadIndex: state.selectedOverloadIndex,
  };
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  state.credits = false;
  state.home = false;
  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  const projection = buildStateUrl();
  Object.assign(state, routeState);

  let projected: URL | null = null;
  let projectionError: unknown = null;
  try {
    projected = await projection;
  } catch (error) {
    projectionError = error;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return null;
  if (!fallbackPackage && projectionError !== null) {
    reportProductNavigationFailure(
      "workspace",
      projectionError,
      navigationSeq,
      initiatingFocusGeneration,
      true,
    );
    return null;
  }

  const successor = resolvePackageQueryWorkspaceSuccessor(
    () => {
      if (projectionError !== null) {
        throw projectionError instanceof Error
          ? projectionError
          : new Error(
            errorMessage(projectionError) || "Workspace URL encoding failed.");
      }
      if (!projected) throw new Error("Workspace URL projection did not complete.");
      return projected;
    },
    () => {
      if (!fallbackPackage) {
        throw new Error("Package Workspace fallback is unavailable.");
      }
      const fallback = buildPackageRootStateUrl(location.href, {
        package: fallbackPackage.id,
        version: fallbackPackage.version,
        framework: fallbackPackage.activeFramework,
        lens: state.packageLens,
      });
      fallback.hash = "workspace";
      return fallback;
    });
  if (!workspaceLocation.push(successor.url.toString())) {
    reportProductNavigationFailure(
      "workspace",
      new Error("Browser history could not be updated."),
      navigationSeq,
      initiatingFocusGeneration,
      true,
    );
    return null;
  }

  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  state.packageActivityOpen = false;
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  state.packageQueryNavigationError = "";
  state.credits = false;
  state.home = false;
  spotlight.reset();

  state.workspaceSubjectOpen = true;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  if (!successor.projected) {
    appendQueryNotice(
      `Workspace opened, but its complete state could not be saved in the address bar: ${errorMessage(successor.projectionError)
        || "workspace URL encoding failed."}`);
  }
  const restoreDestinationFocus =
    initiatingFocusGeneration === documentFocusGeneration
    && document.activeElement instanceof Element
    && document.activeElement.closest("[data-product-navigation-button]")
      !== null;
  let focusGeneration: number | null = null;
  if (productNavigationOwnsFocus()) {
    workspaceProductFocusParkingActive = true;
    app.tabIndex = -1;
    app.focus({ preventScroll: true });
    focusGeneration = documentFocusGeneration;
  }
  render();
  return { navigationSeq, focusGeneration, restoreDestinationFocus };
}

function closePackageQueryRoute() {
  navigationSequence.begin();
  discardPackageQueryTermEditors();
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  if (state.packageQueryOpenedFromApp) {
    state.packageQueryReturnFocusPending =
      state.packageQueryReturnFocus !== null;
    history.back();
    return;
  }
  state.packageQueryOpen = false;
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

function closePackageActivityRoute() {
  navigationSequence.begin();
  packageChangesController.cancel("disposed");
  if (state.packageActivityOpenedFromApp) {
    state.packageActivityReturnFocusPending =
      state.packageActivityReturnFocus !== null;
    history.back();
    return;
  }
  state.packageActivityOpen = false;
  state.packageActivityOpenedFromApp = false;
  state.packageActivityPredecessorEntryId = null;
  state.packageActivityReturnFocus = null;
  state.packageActivityReturnFocusPending = false;
  state.credits = false;
  state.home = true;
  spotlight.reset();
  workspaceLocation.replace("/");
  render();
}

function preparePackageQueryRequest(
  text: string,
): QueryRequest {
  const validText = validPackageQuerySearchText(text);
  state.packageQueryPrefix = validText;
  state.packageQueryNavigationError = "";
  const request = state.packageQueryState.request
    ? withScopeQuery(state.packageQueryState.request, validText)
    : createQueryRequest(validText);
  return request;
}

function submitPackageQueryRequest(request: QueryRequest) {
  packageQueryLiveAnnouncer.reset();
  if (!shouldExecuteQuery(request)) {
    packageQueryController.configure(request);
    return;
  }
  void packageQueryController.run(request);
}

function runPackageQuery(text: string) {
  const request = preparePackageQueryRequest(text);
  submitPackageQueryRequest(request);
}

function runPackageChanges(
  packageSetId: string,
  fromExclusive: string | null,
  throughInclusive: string | null,
  securityOnly: boolean,
  maximumRows: number,
) {
  if (!state.packageChangesPackageSets.some(
    packageSet => packageSet.id === packageSetId)) {
    state.packageChangesCatalogError =
      "The selected product package set is unavailable.";
    render();
    return;
  }
  state.packageChangesCatalogError = "";
  void packageChangesController.run(createPackageChangesRequest(
    packageSetId,
    {
      fromExclusive,
      throughInclusive,
      securityOnly,
      maximumRows,
    }));
}

function preparePackageQueryControlRequest(
  text: string,
): QueryRequest {
  return preparePackageQueryRequest(text);
}

function changePackageQuerySource(
  selection: Partial<QuerySourceSelection>,
  text: string,
) {
  const request = preparePackageQueryControlRequest(text);
  submitPackageQueryRequest(withSourceSelection(request, selection));
}

function togglePackageQueryPreset(presetId: string, text: string) {
  const preset = state.packageQueryPresets.find(
    candidate => candidate.id === presetId);
  if (!preset) {
    state.packageQueryNavigationError =
      "The selected package-query fact is unavailable.";
    render();
    focusPackageQueryInput();
    return;
  }

  const current = preparePackageQueryControlRequest(text);
  submitPackageQueryRequest(togglePreset(current, preset));
}

function addPackageQueryTerm(termKey: string) {
  const descriptor = state.packageQueryTerms.find(
    candidate => candidate.key === termKey);
  if (!descriptor || descriptor.operators.length === 0) {
    state.packageQueryNavigationError =
      "The selected package-query term is unavailable.";
    render();
    return;
  }

  state.packageQueryState.termDraft = {
    descriptor,
    operator: descriptor.operators[0] ?? "",
    value: "",
  };
  state.packageQueryNavigationError = "";
  render();
  afterCurrentNavigationFrame(() =>
    document.querySelector<HTMLElement>("[data-query-term-draft-value]")
      ?.focus());
}

function applyPackageQueryTerm(
  index: number | null,
  operator: string,
  value: string,
  text: string,
) {
  const descriptor = index === null
    ? state.packageQueryState.termDraft?.descriptor
    : state.packageQueryState.request?.terms[index]?.descriptor;
  if (!descriptor) {
    state.packageQueryNavigationError =
      "The selected package-query term is unavailable.";
    render();
    return;
  }

  const current = preparePackageQueryControlRequest(text);
  const request = index === null
    ? withTerm(current, descriptor, operator, value)
    : replaceTerm(current, index, operator, value);
  if (index === null) {
    state.packageQueryState.termDraft = null;
  } else {
    const edits = [...(state.packageQueryState.termEdits ?? [])];
    edits[index] = null;
    state.packageQueryState.termEdits = edits;
  }
  submitPackageQueryRequest(request);
}

function removePackageQueryTerm(index: number, text: string) {
  const current = preparePackageQueryControlRequest(text);
  state.packageQueryState.termEdits =
    (state.packageQueryState.termEdits ?? []).filter(
      (_edit, termIndex) => termIndex !== index);
  submitPackageQueryRequest(withoutTerm(current, index));
}

function editPackageQueryTerm(
  index: number | null,
  operator: string,
  value: string,
) {
  if (index === null) {
    const draft = state.packageQueryState.termDraft;
    if (draft) state.packageQueryState.termDraft = {
      ...draft,
      operator,
      value,
    };
    return;
  }
  if (!state.packageQueryState.request?.terms[index]) return;
  const edits = [...(state.packageQueryState.termEdits ?? [])];
  edits[index] = { operator, value };
  state.packageQueryState.termEdits = edits;
}

function cancelPackageQueryTermDraft() {
  const descriptor = state.packageQueryState.termDraft?.descriptor;
  state.packageQueryState.termDraft = null;
  render();
  if (!descriptor) return;
  afterCurrentNavigationFrame(() =>
    document.querySelector<HTMLElement>(
      `[data-query-term-add="${cssEscape(descriptor.key)}"]`)?.focus());
}

async function openPackageQueryRow(
  packageId: string,
  version: string,
  rootRequest?: string,
) {
  packageQueryViewport =
    capturePackageQueryViewport(document) ?? packageQueryViewport;
  if (!canPublishRetainedWorkspace()) {
    state.packageQueryNavigationError = retainedWorkspaceCapacityMessage();
    render();
    return;
  }
  packageQueryController.cancel();
  packageChangesController.cancel("disposed");
  discardPackageQueryTermEditors();
  state.packageQueryOpen = false;
  const navigationSeq = navigationSequence.begin();
  const { rollbackSnapshot, retainedSnapshot } =
    captureWorkspaceConstructionSnapshots(navigationSeq);
  prepareUnpublishedWorkspace();
  packageQueryHandoffNavigationSeq = navigationSeq;
  state.packageQueryNavigationError = "";
  packageQueryAnnouncements.beginNavigationAttempt();
  let loadFailure = "";
  const loaded = await loadPackage(
    packageId,
    version,
    "",
    {
      navigationSeq,
      deferWorkspacePublication: true,
      ...(rootRequest === undefined ? {} : { rootRequest }),
      failureHandler: (message: string) => {
        loadFailure = message;
      },
    });
  if (!navigationSequence.isCurrent(navigationSeq)) {
    if (packageQueryHandoffNavigationSeq === navigationSeq)
      packageQueryHandoffNavigationSeq = null;
    return;
  }
  if (!loaded) {
    try {
      if (!await restoreWorkspaceNavigationRollback(rollbackSnapshot)) return;
    } catch (error) {
      reportWorkspaceNavigationRollbackFailure(
        rollbackSnapshot,
        error,
        () => observeAsync(
          openPackageQueryRow(packageId, version, rootRequest),
          "Retrying Package query navigation"));
      return;
    }
    discardPendingWorkspaceConstruction();
    packageQueryHandoffNavigationSeq = null;
    const failure = loadFailure || state.error || state.queryNotice
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
  let destination: string;
  try {
    destination = (await buildStateUrl()).toString();
  } catch (error) {
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    try {
      if (!await restoreWorkspaceNavigationRollback(rollbackSnapshot)) return;
    } catch (restoreError) {
      reportWorkspaceNavigationRollbackFailure(
        rollbackSnapshot,
        restoreError,
        () => observeAsync(
          openPackageQueryRow(packageId, version, rootRequest),
          "Retrying Package query navigation"));
      return;
    }
    discardPendingWorkspaceConstruction();
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.packageQueryOpen = true;
    state.packageQueryNavigationError =
      `Couldn’t open ${packageId}@${version}: ${errorMessage(error)}`;
    render();
    afterCurrentNavigationFrame(() =>
      document.querySelector<HTMLElement>(
        `[data-query-row-open="${cssEscape(packageId)}"][data-query-row-version="${cssEscape(version)}"]`)
        ?.focus());
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  publishCurrentWorkspace(retainedSnapshot);
  workspaceLocation.push(destination);
  render();
  focusTypeList();
}

const packageQueryActions: PackageQueryBindingActions = {
  onBack: closePackageQueryRoute,
  onCancel: () => packageQueryController.cancel(),
  onPresetToggle: togglePackageQueryPreset,
  onLibraryTargetInput: targetFramework => {
    const current = state.packageQueryState.request
      ?? createQueryRequest(state.packageQueryPrefix);
    const configured = {
      ...current,
      targetFramework,
    };
    if (configured.targetFramework === current.targetFramework
      && state.packageQueryState.outcome.completion.kind === "idle") return;
    state.packageQueryNavigationError = "";
    packageQueryController.configure(configured);
  },
  onTermAdd: addPackageQueryTerm,
  onTermApply: applyPackageQueryTerm,
  onTermEdit: editPackageQueryTerm,
  onTermDraftCancel: cancelPackageQueryTermDraft,
  onTermRemove: removePackageQueryTerm,
  onSourceChange: changePackageQuerySource,
  onEditorCompositionEnd: resumePackageQueryRender,
  onPrefixInput: prefix => {
    state.packageQueryPrefix = prefix;
    const current = state.packageQueryState.request
      ?? createQueryRequest(prefix);
    const configured = withEditorDraft(current, prefix);
    if (configured.scopeQuery === current.scopeQuery
      && state.packageQueryState.outcome.completion.kind === "idle") return;
    state.packageQueryNavigationError = "";
    packageQueryController.configure(configured);
  },
  onResultPressure: () => packageQueryController.requestMore(),
  onResultViewportChange: schedulePackageQueryStreamRender,
  onRowOpen: (packageId, version, rootRequest) => {
    observeAsync(
      openPackageQueryRow(packageId, version, rootRequest),
      "Opening a queried package");
  },
  onRun: runPackageQuery,
};
const packageChangesActions = {
  onBack: closePackageActivityRoute,
  onCancel: () => packageChangesController.cancel("user"),
  onResultViewportChange: schedulePackageActivityStreamRender,
  onRun: runPackageChanges,
};

let packageActivityStreamRenderFrame: number | null = null;
let packageQueryViewport: PackageQueryViewportSnapshot | null = null;
let packageChangesViewport: PackageChangesViewportSnapshot | null = null;

const packageQueryRender =
  createPackageQueryRenderScheduler({
    requestFrame: callback => requestAnimationFrame(callback),
    cancelFrame: frame => cancelAnimationFrame(frame),
    shouldRender: () => state.packageQueryOpen
      && state.engineReady
      && !state.loading
      && !state.error,
    compositionActive: () =>
      packageQueryEditorCompositionActive(document),
    renderStream: patchPackageQueryPage,
    renderFull: replacePackageQueryPage,
  });

function resumePackageQueryRender() {
  packageQueryRender.resume();
}

function schedulePackageQueryStreamRender() {
  packageQueryRender.scheduleStream();
}

function cancelPackageActivityStreamRender() {
  if (packageActivityStreamRenderFrame === null) return;
  cancelAnimationFrame(packageActivityStreamRenderFrame);
  packageActivityStreamRenderFrame = null;
}

function schedulePackageActivityStreamRender() {
  if (packageActivityStreamRenderFrame !== null) return;
  packageActivityStreamRenderFrame = requestAnimationFrame(() => {
    packageActivityStreamRenderFrame = null;
    if (state.packageActivityOpen) patchPackageActivityPage();
  });
}

function patchPackageQueryPage() {
  const focus = capturePackageQueryFocus(document);
  const viewport =
    capturePackageQueryViewport(document) ?? packageQueryViewport;
  const announcement = takePackageQueryAnnouncement();
  const patched = patchPackageQueryStream(
    document,
    {
      state: state.packageQueryState,
      escapeHtml,
      viewport,
    },
    packageQueryActions);
  if (!patched) {
    render();
    return;
  }
  restorePackageQueryViewport(document, viewport);
  packageQueryViewport =
    capturePackageQueryViewport(document) ?? viewport;
  restorePackageQueryFocus(document, focus);
  packageQueryLiveAnnouncer.enqueue(announcement);
}

function patchPackageActivityPage() {
  const viewport =
    capturePackageChangesViewport(document, packageChangesViewport)
    ?? packageChangesViewport;
  const patched = patchPackageChangesStream(document, {
    state: state.packageChangesState,
    escapeHtml,
    viewport,
  });
  if (!patched) {
    render();
    return;
  }
  packageChangesViewport =
    capturePackageChangesViewport(document, viewport) ?? viewport;
}

function renderPackageQueryPage() {
  packageQueryRender.renderFull();
}

function replacePackageQueryPage() {
  const focus = capturePackageQueryFocus(document);
  const viewport =
    capturePackageQueryViewport(document) ?? packageQueryViewport;
  const announcement = takePackageQueryAnnouncement();
  document.title = "Package query · dotnet-inspect";
  app.innerHTML = renderPackageQueryView({
    state: state.packageQueryState,
    prefix: state.packageQueryPrefix,
    availablePresets: state.packageQueryPresets,
    availableTerms: state.packageQueryTerms,
    navigationError: [
      state.packageQueryCatalogError,
      state.packageQueryNavigationError,
    ].filter(Boolean).join(" "),
    escapeHtml,
    viewport,
  });
  bindPackageQueryView(document, packageQueryActions);
  restorePackageQueryViewport(document, viewport);
  packageQueryViewport =
    capturePackageQueryViewport(document) ?? viewport;
  restorePackageQueryFocus(document, focus);
  packageQueryLiveAnnouncer.enqueue(announcement);
}

function renderPackageActivityPage() {
  cancelPackageActivityStreamRender();
  const viewport =
    capturePackageChangesViewport(document, packageChangesViewport)
    ?? packageChangesViewport;
  document.title = "Package Activity · dotnet-inspect";
  app.innerHTML = renderPackageChangesView({
    state: state.packageChangesState,
    packageSets: state.packageChangesPackageSets,
    catalogError: state.packageChangesCatalogError,
    escapeHtml,
    viewport,
  });
  bindPackageChangesView(document, packageChangesActions);
  restorePackageChangesViewport(document, viewport);
  packageChangesViewport =
    capturePackageChangesViewport(document, viewport) ?? viewport;
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
    loadPackageFromSpotlight(query.packageId, query.version, ""),
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
  bindLibraryOpenEvents();
}

async function loadSelectedMemberDocumentation() {
  if (state.rootKind === "library") {
    render({ synchronizeUrl: false });
    return;
  }
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
  await Promise.all([
    memberDetailInspection.loadDocumentation({
      signature,
      packageId: pkg.id,
      version: pkg.version,
      framework: pkg.activeFramework,
      assembly: type.assembly,
      platformPack: pkg.isRuntimePack
        ? platformPackForAssembly(type.assembly, type.platformPack) ?? ""
        : "",
      overload,
      isRuntimePack: Boolean(state.package?.isRuntimePack),
      isCurrent: () => memberRequestIsCurrent(signature),
    }),
    memberDetailInspection.loadDeclaration({
      signature,
      packageId: pkg.id,
      version: pkg.version,
      framework: pkg.activeFramework,
      assembly: type.assembly,
      isRuntimePack: pkg.isRuntimePack,
      platformPack: pkg.isRuntimePack
        ? platformPackForAssembly(type.assembly, type.platformPack) ?? ""
        : "",
      typeIdentity: type.definitionId ?? type.id,
      member: overload.name,
      selectorKey: overload.graphSelectorKey,
      metadataToken:
        overload.declarationMetadataToken ?? overload.metadataToken ?? 0,
      implementationMember: Boolean(overload.graphOnly),
      isCurrent: () => memberRequestIsCurrent(signature),
    }),
  ]);
}

async function loadSelectedMemberSource() {
  if (currentSourceOperationKind() !== "member") {
    render();
    return;
  }
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
  return memberDetailInspection.loadFindingCensus({
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
    pkg?.isRuntimePack ? state.platformDemoContextId : null,
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
    typeSourceSignature(type, pkg, state.taste, memberRequestKey, state.typeSourceView);
  return sourceInspection.loadTypeSource({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.definitionId ?? type.id,
    taste: JSON.stringify(state.taste),
    view: state.typeSourceView,
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
  const metadataLibraryIdentity = selectedTypeMetadataLibraryIdentity();
  const workspaceJson = typeMetadataWorkspaceIdentity();
  const signature = typeMetadataSignature(
    type,
    pkg,
    metadataLibraryIdentity,
    workspaceJson);
  return metadataInspection.loadTypeMetadata({
    signature,
    packageId: pkg.id,
    version: pkg.version,
    framework: pkg.activeFramework,
    assembly: type.assembly,
    type: type.queryId ?? type.id,
    typeIdentity: type.definitionId ?? type.id,
    workspaceJson,
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
      && !state.atLibraryRoot
      && currentType != null
      && currentTypeMetadataSignature(
        currentType,
        pkg,
        selectedTypeMetadataLibraryIdentity()) === signature;
    },
  });
}

async function renderMermaidDefinition(
  idPrefix: string,
  definition: string,
): Promise<string> {
  mermaidModule ??= import("mermaid");
  const { default: mermaid } = await mermaidModule;
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: "strict",
    theme: state.theme === "light" ? "default" : "dark",
    themeVariables: { fontSize: "17px" },
    flowchart: { htmlLabels: false, curve: "basis" }
  });
  const id =
    `${idPrefix}-${Date.now().toString(36)}-${++mermaidRenderSequence}`;
  const rootStyle = getComputedStyle(document.documentElement);
  const resolved = resolveMermaidCssVariables(
    definition, name => rootStyle.getPropertyValue(name));
  return (await mermaid.render(id, resolved)).svg;
}

async function renderAnnotatedRelationshipDiagram() {
  const container =
    document.querySelector<HTMLElement>("#annotated-relationship-diagram");
  const result = state.memberAnnotated;
  const session = state.memberAnnotatedModal;
  if (!container
    || !result
    || session?.relationshipPresentation !== "Diagram") return;

  const model = createAnnotatedSourceViewerModel(result);
  const graph = buildAnnotatedRelationshipGraphMermaid(
    model.callRelationships);
  if (!graph) return;
  if (container.dataset.graphDef === graph.definition
    && container.querySelector(".graph-viewport")) return;

  try {
    const svg = await renderMermaidDefinition(
      "annotated-relationships",
      graph.definition);
    const targetContainer =
      document.querySelector<HTMLElement>("#annotated-relationship-diagram");
    if (targetContainer !== container
      || state.memberAnnotatedModal?.relationshipPresentation !== "Diagram") {
      return;
    }
    targetContainer.innerHTML =
      '<div class="graph-viewport" aria-label="Direct call diagram"></div>'
      + graphControlsHtml();
    const viewport =
      targetContainer.querySelector<HTMLElement>(".graph-viewport");
    if (!viewport) return;
    viewport.innerHTML = svg;
    targetContainer.dataset.graphDef = graph.definition;
    bindGraphPanZoom(targetContainer, viewport, { keybindings });
  } catch (error) {
    if (document.querySelector("#annotated-relationship-diagram") === container) {
      container.innerHTML =
        `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(errorMessage(error))}</p></div>`;
    }
  }
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
    const svg = await renderMermaidDefinition("type-graph", definition);
    if (document.querySelector("#type-graph-diagram") !== container) return;
    container.innerHTML =
      '<div class="graph-viewport"></div>'
      + graphControlsHtml();
    const viewport =
      container.querySelector<HTMLElement>(".graph-viewport");
    if (!viewport) return;
    viewport.innerHTML = svg;
    bindGraphPanZoom(container, viewport, {
      keybindings,
      resolveTypeGraphNode: nodeId => {
        const graphNode = nodeId ? graphNodeOf.get(nodeId) : null;
        if (!graphNode) return null;
        const fullName = graphNode.id;
        const currentType = selectedType();
        const candidate = graphNode.role === "self"
          ? currentType
            ? { pkg: currentPackage(), type: currentType }
            : null
          : uniqueWorkspaceTypeByQueryId<AppTypeSurface, AppPackage>(
              state.packages,
              fullName);
        if (!candidate) {
          return {
            unavailableLabel:
              `${fullName} — not uniquely available in the loaded Workspace surfaces`,
          };
        }
        return {
          onSelect: () => {
            closeGraphExplorerForNavigation();
            navigateToWorkspaceType(candidate.pkg, candidate.type);
          },
          label: `Open ${graphNode.displayName}`,
        };
      },
    });
  } catch (error) {
    if (document.querySelector("#type-graph-diagram") === container) {
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(errorMessage(error))}</p></div>`;
    }
  }
}

function navigateToTypeByName(fullName: string) {
  const candidate =
    uniqueWorkspaceTypeByQueryId<AppTypeSurface, AppPackage>(
      state.packages,
      fullName);
  if (!candidate) return;
  navigateToWorkspaceType(candidate.pkg, candidate.type);
}

function navigateToWorkspaceType(
  pkg: AppPackage,
  target: AppTypeSurface,
) {
  const preserveAggregate = navigationPreservesAggregateLibraryScope(pkg);
  if (state.package !== pkg)
    selectWorkspacePackage(pkg, { renderSelection: false });
  navigateToType(target, { preserveAggregate });
}

function navigateToType(
  target: AppTypeSurface,
  options: { preserveAggregate?: boolean } = {},
) {
  // Clicking a non-public related type (e.g. an internal derived implementer)
  // enables its accessibility bucket so it appears in the nav list rather than
  // being filtered out by the public-by-default view.
  enterTypeSubject(target, options);
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  resetMemberFilters();
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === target.id);
  render();
}

// A related type is openable only when one loaded Workspace surface owns its
// exact query identity. Ambiguous or external relationships stay static.
function typeIsNavigable(fullName: string) {
  return uniqueWorkspaceTypeByQueryId<AppTypeSurface, AppPackage>(
    state.packages,
    fullName) !== null;
}

// Render a related-type chip as an active button only for a unique loaded
// Workspace type.
function relatedTypeChip(name: string) {
  const short = escapeHtml(shortTypeName(name));
  if (typeIsNavigable(name)) {
    return `<button class="type-chip" data-graph-type="${escapeHtml(name)}" title="${escapeHtml(name)}">${short}</button>`;
  }
  return `<span class="type-chip is-static" title="${escapeHtml(name)} — not uniquely available in the loaded Workspace surfaces">${short}</span>`;
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
  const packages = state.packages.map(candidate => ({ ...candidate }));
  const model = {
    package: { id: pkg.id, version: pkg.version, activeFramework: pkg.activeFramework },
    packages,
    dependenciesGroupIndex: state.dependenciesGroupIndex,
    workspaceDependencies: { ...state.workspaceDependencies },
    ...(state.packageDependencies
      ? { packageDependencies: state.packageDependencies }
      : {}),
  };
  const requestSignature = JSON.stringify([
    packageIdentityKey(model.package),
    dependencyCoordinateCandidates(packages),
    model.dependenciesGroupIndex,
    model.packageDependencies,
    model.workspaceDependencies,
  ]);
  // Deduplicate before matching: duplicate renders must not supersede their own
  // pending Mermaid work while awaiting the same coordinate results.
  if (pending.isPending(requestSignature)) return;
  const seq = depGraphRenderSequence.begin();
  pending.begin(requestSignature, seq);
  let phase = "Dependency matching";
  try {
    const built = await buildDependencyGraphMermaid(
      model,
      (_packages, packageId, versionRange) =>
        uniqueCompatiblePackage(packages, packageId, versionRange),
      async (inspectedPackageId, packageIds) => {
        phase = "Dependency classification";
        const roles =
          await engineClient.package.classifyPackageGraphIdentities(
            inspectedPackageId,
            packageIds,
          );
        return roles.map(role => {
          switch (role) {
            case "Inspected": return "inspected";
            case "SamePrefix": return "samePrefix";
            case "External": return "external";
            default:
              throw new Error(`Package graph classification returned invalid role '${role}'.`);
          }
        });
      });
    if (!depGraphRenderSequence.isCurrent(seq)
      || document.querySelector("#dependency-graph-diagram") !== container) return;
    if (!built) {
      depGraphRenderSequence.invalidate();
      container.dataset.graphDef = "";
      pending.invalidate();
      container.innerHTML = '<p class="graph-empty">No connected packages for this framework. Open a package that depends on this one to see caller edges.</p>';
      return;
    }
    const signature = dependencyGraphRenderSignature(built);
    if (container.dataset.graphDef === signature && container.querySelector(".graph-viewport")) {
      container.querySelector(".graph-render-error")?.remove();
      return;
    }
    phase = "Diagram rendering";
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
    const resolved = resolveMermaidCssVariables(
      built.definition, name => rootStyle.getPropertyValue(name));
    const { svg } = await mermaid.render(id, resolved);
    // A newer render superseded this one, or the container was swapped out — bail without touching the DOM.
    if (!depGraphRenderSequence.isCurrent(seq)) return;
    if (document.querySelector("#dependency-graph-diagram") !== container) return;
    container.innerHTML =
      '<div class="dependency-graph-stage"><div class="graph-viewport"></div>'
      + graphControlsHtml()
      + '</div>'
      + (built.truncated
        ? `<div class="graph-drill-error graph-diagnostics" role="status">Dependency graph truncated at ${built.nodeLimit} nodes.</div>`
        : "");
    const viewport =
      container.querySelector<HTMLElement>(".graph-viewport");
    if (!viewport) return;
    viewport.innerHTML = svg;
    container.dataset.graphDef = signature;
    bindGraphPanZoom(container, viewport, {
      keybindings,
      resolveDependencyGraphNode: nodeId => {
        const info = nodeId ? built.nodeInfoById.get(nodeId) : null;
        if (!info || info.kind === "self") return null;
        const loaded = state.packages.find(candidate =>
          packageIdentityKey(candidate) === info.packageKey);
        return {
          label: loaded
            ? `Open ${packageDisplayName(loaded)}@${loaded.version} · ${loaded.activeFramework}`
            : `Load ${info.id} ${info.versionRange || "latest stable"}`,
          onSelect: () => {
            if (info.kind === "open" && info.packageKey)
              switchToPackageForDependencies(info.packageKey);
            else if (info.id)
              observeAsync(
                openDependencyPackage(info.id, info.versionRange),
                "Opening a dependency package");
          },
        };
      },
    });
  } catch (error) {
    if (depGraphRenderSequence.isCurrent(seq)
      && document.querySelector("#dependency-graph-diagram") === container) {
      const message = `<div class="graph-render-error" role="alert"><strong>${phase} failed</strong><p>${escapeHtml(errorMessage(error))}</p></div>`;
      if (container.querySelector(".graph-viewport")) {
        container.querySelector(".graph-render-error")?.remove();
        container.insertAdjacentHTML("beforeend", message);
      } else {
        container.dataset.graphDef = "";
        container.innerHTML = message;
      }
    }
  } finally {
    pending.complete(requestSignature, seq);
  }
}

function switchToPackageForDependencies(packageKey: string) {
  navigationSequence.invalidate();
  const target = state.packages.find(item =>
    packageIdentityKey(item) === packageKey);
  if (!target) return;
  closeGraphExplorerForNavigation();
  state.loading = false;
  activatePackage(target, { resetAccessibility: true });
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.packageLens = "dependencies";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  const firstLibrary = packageLibraries()[0];
  state.libraryScope = firstLibrary ? new Set([firstLibrary.id]) : null;
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
  closeGraphExplorerForNavigation();
  const navigationSeq = navigationSequence.begin();
  state.loading = true;
  state.error = "";
  state.retryAction = null;
  state.loadingMessage = `Resolving ${packageId}…`;
  state.loadingSubtitle = versionRange || "latest stable";
  render();
  try {
    const existing = await uniqueCompatiblePackage(
      state.packages.map(pkg => ({ ...pkg })),
      packageId,
      versionRange ?? null);
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (existing) {
      switchToPackageForDependencies(packageIdentityKey(existing));
      return;
    }
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
    state.atLibraryRoot = false;
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
  const traversalFramework = state.callGraphTraversalFramework;
  const memberSignature =
    memberRequestSignature(type, overload, true);
  const signature =
    `${memberSignature}|traversal:${traversalFramework}`;
  const pkg = currentPackage();
  const platformAssembly = assemblyDescriptorForType(pkg.assemblies, type);
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
    platformContextId: state.platformDemoContextId,
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
    traversalFramework,
    isCurrent: () =>
      memberRequestIsCurrent(memberSignature, true)
      && state.callGraphTraversalFramework === traversalFramework,
  });
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
      const renderDefinition = resolveMermaidCssVariables(
        styleCallGraphMermaid(definition, active.targets),
        name => rootStyle.getPropertyValue(name));
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
        + graphControlsHtml();
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
): GraphNodeBinding | null {
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
): GraphNodeBinding | null {
  const typeId = callGraphTargetTypeId(target);

  // Inside a platform descent the whole graph lives in the runtime pack, not
  // the workspace, so clicked callees descend through the platform graph.
  const drilled =
    state.platformStack.length > 0 || Boolean(state.package?.isRuntimePack);
  if (drilled) {
    if (target.id === "n0" || !target.assembly || !typeId) return null;
    const pack = runtimePackForFramework(
      runtimePackPackage(),
      platformCatalogFramework(state.package?.activeFramework || ""));
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
          navigationSequence.begin();
          let owner = captureViewOperation(state.memberCallGraphSeq);
          observeAsync(
            openRuntimeMemberFromGraph(
              pack,
              resident.type,
              resident.group,
              resident.overloadIndex,
              target,
              runtimeSection,
              () => ownsViewOperation(owner, state.memberCallGraphSeq),
              () => {
                owner = captureViewOperation(state.memberCallGraphSeq);
              },
              failureSurface),
            "Opening a platform call-graph member");
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
  const packageCoordinate = callGraphTargetPackageCoordinate(target);
  const coordinatePackages = packageCoordinate
    ? packages.filter(pkg =>
        pkg.id.toLowerCase() === packageCoordinate.id.toLowerCase()
        && pkg.version.toLowerCase() === packageCoordinate.version.toLowerCase()
        && pkg.activeFramework === packageCoordinate.framework)
    : [];
  if (coordinatePackages.length > 1) {
    return blockedCallGraphNodeBinding(
      target,
      "the exact target package coordinate is not unique in the loaded workspace",
      failureSurface);
  }
  const coordinatePackage = coordinatePackages[0] ?? null;
  const candidate =
    resolveLoadedGraphTargetCandidate<AppPackage, AppTypeSurface>(
      coordinatePackage ? [coordinatePackage] : packages,
      target);
  if (candidate.status === "resident"
      && (coordinatePackage !== null || destination !== "default")) {
    const residentPackage =
      coordinatePackage ?? loadedGraphTargetPackage(packages, target);
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
  const packageAvailable =
    packageCoordinate !== null && coordinatePackage === null;
  const pack = runtimePackForFramework(
    runtimePackPackage(),
    platformCatalogFramework(state.package?.activeFramework || ""));
  const runtimeCandidate = !packageAvailable
    && (candidate.status === "missing"
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
    runtimeResident,
    packageAvailable);
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
      } else if (disposition === "package" && packageCoordinate) {
        observeAsync(
          openPackageGraphMember(
            packageCoordinate,
            target,
            loadedSection,
            failureSurface),
          "Opening a dependency package graph member");
      } else if (disposition === "resident") {
        if (pack && resident) {
          navigationSequence.begin();
          let owner = captureViewOperation(state.memberCallGraphSeq);
          observeAsync(
            openRuntimeMemberFromGraph(
              pack,
              resident.type,
              resident.group,
              resident.overloadIndex,
              target,
              runtimeSection,
              () => ownsViewOperation(owner, state.memberCallGraphSeq),
              () => {
                owner = captureViewOperation(state.memberCallGraphSeq);
              },
              failureSurface),
            "Opening a resident platform call-graph member");
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

async function openPackageGraphMember(
  coordinate: {
    id: string;
    version: string;
    framework: string;
  },
  target: InspectedCallGraphTarget,
  section: "overview" | "source",
  failureSurface: GraphNavigationFailureSurface,
) {
  closeGraphExplorerForNavigation();
  if (retainedWorkspaces.activeWorkspaceId === null
      && !canPublishRetainedWorkspace()) {
    showGraphMemberNavigationError(
      target,
      retainedWorkspaceCapacityMessage(),
      failureSurface);
    return;
  }
  const pkg = await loadPackage(
    coordinate.id,
    coordinate.version,
    coordinate.framework);
  if (!pkg) return;
  await navigateToUnprojectedGraphMember(
    pkg,
    target,
    section,
    failureSurface);
}

function blockedCallGraphNodeBinding(
  target: InspectedCallGraphTarget,
  reason: string,
  failureSurface: GraphNavigationFailureSurface = "call-graph",
): GraphNodeBinding {
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

function callGraphSummary(graph: ReturnType<typeof currentCallGraph>) {
  const callerCount = graph?.callers.children.length ?? 0;
  const calleeCount = graph?.callees.children.length ?? 0;
  return `${callerCount} caller${callerCount === 1 ? "" : "s"} · ${calleeCount} callee${calleeCount === 1 ? "" : "s"}`;
}

function dependencyGraphAvailable() {
  return state.packageDependenciesKey === packageDependenciesSignature()
    && !state.packageDependenciesLoading
    && !state.packageDependenciesError
    && Boolean(state.packageDependencies?.dependencyGroups?.length);
}

function typeGraphAvailable() {
  const type = selectedType();
  return Boolean(type && state.package
    && state.typeMetadataKey === currentTypeMetadataSignature(
      type,
      state.package,
      selectedTypeMetadataLibraryIdentity())
    && !state.typeMetadataLoading
    && !state.typeMetadataError
    && state.typeMetadata && state.typeMetadata.graphNodes.length > 1);
}

function graphExplorerKey(): string | null {
  if (state.home || state.loading || state.error || state.packageQueryOpen
    || state.packageActivityOpen
    || state.credits || state.settings || state.keyboardHelp || state.explorer?.open
    || state.spotlightOpen
    || documentViewerIsOpen(state.docViewer)
    || graphSourceIsOpen(state.graphSource)
    || state.memberAnnotatedModal) return null;
  if (scope() === "package" && state.packageLens === "dependencies") {
    return JSON.stringify(["dependencies", packageDependenciesSignature()]);
  }
  const type = selectedType();
  if (scope() === "type" && state.lens === "metadata" && type && state.package) {
    const signature = currentTypeMetadataSignature(
      type,
      state.package,
      selectedTypeMetadataLibraryIdentity());
    const metadata = state.typeMetadataKey === signature ? state.typeMetadata : null;
    if (metadata && metadata.graphNodes.length < 2) return null;
    return JSON.stringify(["type", signature]);
  }
  if (scope() !== "member" || state.memberSection !== "call-graph") return null;
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

function graphExplorerTarget() {
  const key = graphExplorerKey();
  const dependencies = scope() === "package";
  const typeRelationships = scope() === "type";
  const content = document.querySelector<HTMLElement>(
    dependencies ? "[data-dependency-graph-surface]"
      : typeRelationships ? "[data-type-graph-surface]" : "[data-call-graph-surface]");
  const invoker = document.querySelector<HTMLElement>("[data-graph-explore]");
  if (!key || !content || !invoker) return null;

  const pkg = currentPackage();
  if (dependencies) {
    return {
      key,
      kind: "Dependency graph",
      subject: packageCoordinateLabel(pkg),
      context: `Target framework ${pkg.activeFramework}`,
      summary: DEPENDENCY_GRAPH_SUMMARY,
      content,
      invoker,
    };
  }

  const path = currentInspectedSubjectPath();
  if (typeRelationships) {
    return {
      key,
      kind: "Type relationships",
      subject: path.at(-1)?.label ?? "Selected type",
      context: packageCoordinateLabel(pkg),
      summary: TYPE_RELATIONSHIPS_GRAPH_SUMMARY,
      content,
      invoker,
    };
  }

  const selected = selectedType();
  const member = selectedMember(selected);
  const overload = member
    ? selectedConcreteOverload(member.overloads, state.selectedOverloadIndex)
    : null;
  const parent = selected
    ? (selected.namespace
      ? `${selected.namespace}.${typeDisplayName(selected)}`
      : typeDisplayName(selected))
    : "Selected type";
  const packageContext = pkg.isRuntimePack
    ? platformTargetLabel()
    : packageCoordinateLabel(pkg);
  return {
    key,
    kind: "Call graph",
    subject: overload?.signature ?? path.at(-1)?.label ?? "Selected member",
    context: `${packageContext} · ${parent}`,
    summary: callGraphSummary(currentCallGraph()),
    content,
    invoker,
  };
}

function openGraphExplorer() {
  const target = graphExplorerTarget();
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
  if (scope() === "type" && state.lens === "metadata" && state.typeMetadataLoading) return;
  graphExplorerNavigationFocusPending = false;
  const explore = document.querySelector<HTMLButtonElement>("[data-graph-explore]");
  if (graphExplorerKey() === graphExplorerOriginKey && explore && !explore.disabled) {
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
  const framework = platformCatalogFramework(currentPackage().activeFramework);
  const runtimePack = runtimePackForFramework(
    runtimePackPackage(),
    framework);
  const captured = capturedShareTabs();
  const platformVersion = resolvedPlatformTargetVersion(
    captured.resolvedTabs,
    runtimePack,
    framework);
  return callGraphInspection.drill({
    contextId: state.package?.isRuntimePack
      ? state.platformDemoContextId
      : null,
    framework,
    platformVersion,
    assembly: node.assembly,
    pack: platformPackForGraphAssembly(
      node.assembly,
      node.platformPack,
      runtimePackPackage(),
      framework) ?? "",
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
  let owner = captureViewOperation(seq);
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
  const framework = platformCatalogFramework(
    state.package?.activeFramework || "");
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
    const runtimeResult = await loadRuntimeGraphAssembly(
      framework,
      retainedPlatform?.version ?? "",
      node.assembly,
      targetPack,
      navigationIsCurrent);
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
    const runtimeResult = await loadRuntimeGraphAssembly(
      framework,
      pack.version,
      node.assembly,
      targetPack,
      navigationIsCurrent);
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
  await openRuntimeMemberFromGraph(
    pack,
    selection.type,
    selection.group,
    selection.overloadIndex,
    node,
    section,
    navigationIsCurrent,
    () => {
      owner = captureViewOperation(state.memberCallGraphSeq);
    },
    failureSurface);
}

async function openRuntimeMemberFromGraph(
  pack: AppPackage,
  type: AppTypeSurface,
  group: AppMemberGroup,
  overloadIndex: number,
  node: InspectedCallGraphTarget,
  section: "overview" | "call-graph",
  navigationIsCurrent: () => boolean,
  renewNavigationOwnership: () => void,
  failureSurface: GraphNavigationFailureSurface,
): Promise<void> {
  try {
    const target = await exactPlatformCatalogForType(pack, type);
    if (!navigationIsCurrent()) return;
    const alreadyRetained = state.packages.includes(pack);
    if (retainPlatformPackageForTarget(target) !== pack) {
      throw new Error(
        "The matching Platform runtime model is unavailable.");
    }
    if (!alreadyRetained) renewNavigationOwnership();
  } catch (error) {
    if (!navigationIsCurrent()) return;
    if (pack.source.kind === "platform" && state.packages.includes(pack)) {
      state.packages = state.packages.filter(candidate => candidate !== pack);
      invalidateWorkspaceMembershipViews();
    }
    await showPlatformTargetError(
      node,
      `the exact Platform target is unavailable: ${errorMessage(error)}`,
      failureSurface);
    return;
  }
  if (!navigationIsCurrent()) return;
  navigateToRuntimeMember(
    pack,
    type,
    group,
    overloadIndex,
    node,
    section);
}

async function exactPlatformCatalogForType(
  pack: AppPackage,
  type: AppTypeSurface,
): Promise<PlatformCatalogTarget> {
  const target = await ensurePlatformCatalog(
    pack.activeFramework,
    pack.version);
  const library = resolvePackageLibrary(pack.assemblies, libraryKey(type));
  if (!library || !target.rows.some(row =>
    row.hasImplementation
    && platformLibraryMatchesDescriptor(row, library))) {
    throw new Error(
      "The matching Platform catalog does not contain the selected Library.");
  }
  return target;
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
  state.atLibraryRoot = false;
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
  state.memberSource = { status: "idle" };
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberCallGraphExpanding = false;
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
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
  if (!documentViewerIsOpen(state.docViewer)) return "";
  return renderDocViewerPure({
    state: state.docViewer,
    escapeHtml,
  });
}

function invalidateSourceCaches() {
  invalidateSourceDestinationWork(state);
  state.memberSource = { status: "idle" };
  state.typeSource = { status: "idle" };
  state.memberAnnotated = null;
  state.memberAnnotatedKey = "";
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
  state.annotatedDestinationError = "";
  state.memberAnnotatedEmbedded = null;
  state.memberAnnotatedModal = null;
}

function reloadVisibleSource() {
  switch (currentSourceReloadKind()) {
    case "graph":
      {
        const target = graphSourceRequest(state.graphSource);
        if (!target) break;
        observeAsync(
          openGraphSource(
            target.request,
            target.title),
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
      if (state.rootKind === "library") {
        showToast("Uploaded Libraries cannot be shared.");
        return;
      }
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

function dispatchProductAction(action: ProductAction) {
  switch (action) {
    case "open-library":
      openLibraryDialog("product-navigation");
      return;
  }
}

function prepareLibraryOpen(returnTarget: LibraryOpenReturnTarget) {
  graphExplorer.close(false);
  graphExplorerNavigationFocusPending = false;
  dismissAnnotatedSourceModal(false);
  state.settings = false;
  state.keyboardHelp = false;
  state.explorer = null;
  spotlight.reset();
  sourceInspection.clearGraphSource();
  documentInspection.clear();
  if (!state.libraryOpen) {
    state.libraryOpenReturn = returnTarget;
  }
  state.libraryOpen = true;
}

function openLibraryDialog(
  returnTarget: LibraryOpenReturnTarget = "surface",
) {
  prepareLibraryOpen(returnTarget);
  state.libraryOpenBusy = false;
  state.libraryOpenError = state.engineReady
    ? ""
    : "Wait for the browser inspection engine to finish starting.";
  render({ synchronizeUrl: false });
}

function closeLibraryDialog() {
  if (state.libraryOpenBusy) return;
  const returnTarget = state.libraryOpenReturn;
  libraryOpenSequence++;
  state.libraryOpen = false;
  state.libraryOpenError = "";
  render({ synchronizeUrl: false });
  requestAnimationFrame(() => {
    if (returnTarget === "home") {
      document.querySelector<HTMLElement>("#home-open-library")
        ?.focus({ preventScroll: true });
    } else if (returnTarget === "product-navigation") {
      restoreOrdinaryModalDismissFocus(() =>
        document.querySelector<HTMLElement>(
          "[data-product-navigation-button]")
          ?.focus({ preventScroll: true }));
    } else {
      focusLevelOneHeading();
    }
  });
}

function bindLibraryOpenEvents() {
  if (state.libraryOpen
    && !document.querySelector("#library-open-dialog")) {
    app.insertAdjacentHTML(
      "beforeend",
      renderLibraryOpenDialog(currentLibraryOpenView(), escapeHtml));
  }
  if (state.libraryOpen) {
    const backdrop = app.querySelector("#library-open-backdrop");
    for (const child of app.children) {
      if (child !== backdrop) child.setAttribute("inert", "");
    }
  }
  disconnectLibraryOpen = bindLibraryOpen(
    document,
    currentLibraryOpenView(),
    libraryOpenActions);
}

function currentLibraryOpenView() {
  return {
    open: state.libraryOpen,
    busy: state.libraryOpenBusy,
    error: state.libraryOpenError,
  };
}

const libraryOpenActions: LibraryOpenActions = {
  onDismiss: closeLibraryDialog,
  onReject: (message, input) => {
    if (input === "drop") prepareLibraryOpen("surface");
    state.libraryOpenError = message;
    render({ synchronizeUrl: false });
  },
  onFile: (file, input) =>
    observeAsync(
      openUploadedLibraryFile(file, input),
      "Opening uploaded Library"),
};

async function openUploadedLibraryFile(
  file: File,
  input: LibraryOpenInput,
) {
  const operationSequence = ++libraryOpenSequence;
  const navigationSeq = navigationSequence.begin();
  const isCurrent = () =>
    operationSequence === libraryOpenSequence
    && navigationSequence.isCurrent(navigationSeq);
  if (input === "drop") prepareLibraryOpen("surface");
  else state.libraryOpen = true;
  state.libraryOpenBusy = state.engineReady;
  state.libraryOpenError = state.engineReady
    ? ""
    : "Wait for the browser inspection engine to finish starting.";
  render({ synchronizeUrl: false });

  let activatedLibrary = false;
  try {
    await waitForLibraryEngineReady();
    if (!isCurrent()) return;
    state.libraryOpenBusy = true;
    state.libraryOpenError = "";
    render({ synchronizeUrl: false });
    const content = Array.from(new Uint8Array(await file.arrayBuffer()));
    if (!isCurrent()) return;
    const inspection = await inspectOpenUploadedLibrary(file.name, content);
    if (!isCurrent()) return;
    if (inspection.content.outcome !== "Available") {
      const failure = inspection.content.failure;
      state.libraryOpenError = failure?.detail
        || "The selected file could not be opened as a managed Library.";
      return;
    }

    const retainedSnapshot = retainedWorkspaces.activeWorkspaceId === null
      ? null
      : captureRetainedWorkspaceSnapshot();
    const packageModel =
      createUploadedLibraryModel(inspection.content);
    activatePackage(packageModel, { resetAccessibility: true });
    state.uploadedLibrary = packageModel;
    state.rootKind = "library";
    state.home = false;
    state.loading = false;
    state.error = "";
    state.errorTitle = "";
    state.errorDetail = "";
    state.retryAction = null;
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.workspaceSubjectOpen = false;
    state.atPackageRoot = false;
    state.atLibraryRoot = true;
    state.libraryScope = new Set([packageModel.assemblyId]);
    state.libraryLens = "overview";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.typeFilter = "";
    state.selectedTypeId = "";
    state.selectedMemberKey = "";
    state.memberBrowseTypeId = "";
    state.selectedOverloadIndex = null;
    state.libraryOpen = false;
    state.libraryOpenError = "";
    if (retainedSnapshot !== null) {
      retainedWorkspaces = detachActiveRetainedWorkspace(
        retainedWorkspaces,
        retainedSnapshot);
    }
    activeWorkspaceUrl = null;
    activatedLibrary = true;
    workspaceLocation.replace("/");
  } catch (error) {
    if (!isCurrent()) return;
    state.libraryOpenError =
      `${input === "drop" ? "Dropped" : input === "paste" ? "Pasted" : "Selected"} `
      + `Library failed to open: ${errorMessage(error)}`;
  } finally {
    if (isCurrent()) {
      state.libraryOpenBusy = false;
      render({ synchronizeUrl: false });
      if (activatedLibrary) {
        afterCurrentNavigationFrame(() => focusLevelOneHeading());
      }
    }
  }
}

bindLibraryOpenDocument(
  document,
  currentLibraryOpenView,
  libraryOpenActions);

function focusSettingsEntry() {
  const selector = state.settingsReturn === "source"
    ? "#settings-decompiler-title"
    : "#settings-title";
  document.querySelector<HTMLElement>(selector)
    ?.focus({ preventScroll: true });
}

// Open Settings, remembering the logical control that receives focus after dismissal.
function openSettings(from: "home" | "workbench" | "source") {
  state.settingsReturn = from;
  state.keyboardHelp = false;
  state.settings = true;
  render();
}

function closeSettings() {
  state.settings = false;
  reloadVisibleSource();
  if (state.settingsReturn === "home") {
    pendingHomeFocusTarget = {
      kind: "id",
      surface: "home",
      id: "home-settings",
    };
    render();
    return;
  }
  render();
  requestAnimationFrame(() => {
    restoreOrdinaryModalDismissFocus(() => {
      const selectors = state.settingsReturn === "source"
        ? ["#explore-source", "#application-menu-button"]
        : state.settingsReturn === "workbench"
          ? ["#application-menu-button"]
          : ["#home-settings"];
      selectors.some(selector => {
        const target = document.querySelector<HTMLElement>(selector);
        if (!target) return false;
        target.focus({ preventScroll: true });
        return true;
      });
    });
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
  requestAnimationFrame(() => {
    restoreOrdinaryModalDismissFocus(() =>
      document.querySelector<HTMLElement>("#application-menu-button")
        ?.focus({ preventScroll: true }));
  });
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
  const graphSource = state.graphSource;
  if (!graphSourceIsOpen(graphSource)) return "";
  return renderGraphSourcePure({
    state: graphSource,
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
  section: "overview" | "source" | "compare" = "overview",
) {
  closeGraphExplorerForNavigation();
  invalidateGraphMemberNavigation();
  const preserveAggregate = navigationPreservesAggregateLibraryScope(pkg);
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
  state.accessibilityFilter = accessibilityFilterIncludingType(
    state.accessibilityFilter,
    type);
  enterTypeSubject(type, { preserveAggregate });
  resetMemberFilters();
  state.selectedMemberKey = group.key;
  state.selectedOverloadIndex = overloadIndex;
  if (!enterMemberScope({ preserveAggregate })) return;
  state.memberSection = section;
  state.memberSource = { status: "idle" };
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphKey = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.memberFindingInteraction = null;
  state.memberFindingSelectionError = "";
  state.annotatedDestinationError = "";
  state.selectedBodyTarget = selectedBodyTarget;
  if (section === "source") {
    observeAsync(loadSelectedMemberSource(), "Loading member source");
  } else if (section === "compare") {
    render();
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

async function loadSelectedMemberFactsSurface() {
  await Promise.all([
    loadSelectedMemberFacts(),
    loadSelectedMemberAnnotatedSource(),
  ]);
}

interface LoadPackageOptions {
  rootRequest?: string;
  background?: boolean;
  loadingPresentation?: "content";
  loadingFocusControl?: PackageLoadingFocusControl;
  navigationSeq?: number;
  queryNotice?: string;
  replacePackage?: AppPackage | null;
  packageLens?: PackageLens;
  librarySelection?: {
    id: string | null;
    name: string | null;
    asset: string | null;
    lens: LibraryLens;
    activate: boolean;
  };
  location?: ParsedLocation;
  retryAction?: RetryAction;
  invalidateWorkspaceShareBasis?: boolean;
  deferWorkspacePublication?: boolean;
  failureHandler?: (message: string) => void;
  retainFailureDetail?: boolean;
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
    packageContentLoadingSequence =
      options.loadingPresentation === "content" && prevPackage
        ? navigationSeq
        : null;
    packageContentLoadingFocusControl = packageContentLoadingSequence === null
      ? null
      : options.loadingFocusControl
        ?? (version.toLowerCase() === prevPackage?.version.toLowerCase()
          ? "package-framework"
          : "package-version");
    state.loading = true;
    state.error = "";
    if (!options.retainFailureDetail) state.errorDetail = "";
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
      ...(options.rootRequest === undefined
        ? {}
        : { rootRequest: options.rootRequest }),
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
    const deep = options.location;
    if (deep) {
      const libraryFailure = applyLoadedPackageLibraryScope(
        packageModel,
        deep.library);
      if (libraryFailure) appendQueryNotice(libraryFailure);
      applyLocationView(deep);
    } else if (!options.replacePackage) {
      selectDefaultPackageSubject(packageModel);
    } else {
      state.atPackageRoot = true;
      state.atLibraryRoot = false;
      state.packageLens = options.packageLens ?? "overview";
      if (options.librarySelection) {
        const { id, name, asset, lens, activate } = options.librarySelection;
        const library = resolveReplacementPackageLibrary(
          packageModel.assemblies,
          { id, name, asset });
        if (!id && !name && !packageModel.isRuntimePack) {
          state.libraryScope = null;
          if (activate) {
            state.atPackageRoot = false;
            state.atLibraryRoot = true;
            state.libraryLens = lens;
          }
        } else if (library) {
          state.libraryScope = new Set([library.id]);
          if (activate) {
            state.atPackageRoot = false;
            state.atLibraryRoot = true;
            state.libraryLens = lens;
          }
        } else {
          appendQueryNotice(
            `The library '${name ?? id}' is not uniquely available in `
            + `${packageModel.id}@${packageModel.version} (${packageModel.activeFramework}). `
            + "Showing Package Overview.");
        }
      }
    }
    if (deep) {
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
    packageContentLoadingSequence = null;
    if (!options.deferWorkspacePublication)
      ensureCurrentWorkspacePublished();
    const selectionData = loadSelectionData();
    render();
    await selectionData;
    return packageModel;
  } catch (error) {
    if (navigationSeq != null && !navigationSequence.isCurrent(navigationSeq))
      return null;
    state.errorDetail = retainDiagnosticDetail(state.errorDetail, error);
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
    packageContentLoadingSequence = null;
    const retryOptions: LoadPackageOptions = { ...options };
    delete retryOptions.navigationSeq;
    delete retryOptions.retainFailureDetail;
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

function platformSurfaceLoaded() {
  return runtimePackPackage() !== null;
}

function runtimePackPackage() {
  if (state.platformSelection) return runtimePackageForTarget(state.platformSelection);
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
    : selectedLibraryName();
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
  queryPackageRoot: rootRequest => inspectPackageRoot(rootRequest),
  loadRuntimePack: (framework, platformVersion) =>
    inspectLoadRuntimePack(framework, platformVersion),
  loadRuntimePackAssembly: (
    framework,
    platformVersion,
    assemblyFileName,
    pack,
    assetFileName,
  ) => inspectLoadRuntimePackAssembly(
    framework,
    platformVersion,
    assemblyFileName,
    pack,
    assetFileName),
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
  assetFileName = assemblyFileName,
): Promise<RuntimeLoadResult> {
  const result = await packageAcquisition.loadRuntimePackAssembly(
    framework,
    assemblyFileName,
    pack,
    isCurrent,
    platformVersion,
    assetFileName);
  return {
    packageModel: result.packageModel,
    failureMessage: result.error === null ? "" : errorMessage(result.error),
  };
}

async function loadRuntimeGraphAssembly(
  framework: string,
  platformVersion: string,
  assembly: string,
  pack: PlatformPack | null,
  isCurrent: () => boolean,
): Promise<RuntimeLoadResult> {
  try {
    const target = await ensurePlatformCatalog(
      platformCatalogFramework(framework),
      platformVersion);
    if (!isCurrent()) return { packageModel: null, failureMessage: "" };
    const row = platformGraphLibraryForTarget(target, assembly, pack);
    if (!row) {
      const family = pack ? ` in ${pack}` : "";
      return {
        packageModel: null,
        failureMessage:
          `The exact Platform catalog does not uniquely identify an implementation for ${assembly}${family}.`,
      };
    }
    return loadRuntimePackAssembly(
      target.tfm,
      platformAssemblyRequest(row),
      row.pack,
      isCurrent,
      target.version,
      row.file);
  } catch (error) {
    return { packageModel: null, failureMessage: errorMessage(error) };
  }
}

interface ProductHomeDemoSelection {
  type: AppTypeSurface;
  member: AppMemberGroup | null;
  overloadIndex: number | null;
  overload: AppMemberSurface | null;
}

function selectProductHomeDemoTarget(
  packageModel: AppPackage,
  activation: BrowserHomeDemoRunActivation,
  focusAssembly: string | null,
  focusPack: PlatformPack | null,
): ProductHomeDemoSelection {
  const types = packageModel.types.filter(item =>
    item.id === activation.typeId
    && (!focusAssembly
      || (item.assemblyName.toLowerCase() === focusAssembly.toLowerCase()
        && item.platformPack === focusPack)));
  if (types.length !== 1) {
    throw new Error(
      `The engine-run demo type '${activation.typeId}' matched ${types.length} returned rows.`);
  }
  const type = types[0]!;
  if (activation.memberSection === null) {
    if (activation.memberName !== null
      || activation.memberKind !== null
      || activation.memberAnchorDigest !== null) {
      throw new Error(
        "The engine-run Methods demo returned an unexpected member selection.");
    }
    return {
      type,
      member: null,
      overloadIndex: null,
      overload: null,
    };
  }

  if (!activation.memberName
    || !activation.memberKind
    || !activation.memberAnchorDigest) {
    throw new Error(
      "The engine-run Call Graph demo returned an incomplete member selection.");
  }
  const members = memberGroups(type).filter(item =>
    item.name === activation.memberName
    && item.kind === activation.memberKind);
  if (members.length !== 1) {
    throw new Error(
      "The engine-run demo member was not uniquely present in its returned surface.");
  }
  const member = members[0]!;
  const overloads = member.overloads
    .map((overload, index) => ({ overload, index }))
    .filter(item =>
      item.overload.anchorDigest === activation.memberAnchorDigest);
  if (overloads.length !== 1) {
    throw new Error(
      "The engine-run demo overload was not uniquely present in its returned surface.");
  }
  return {
    type,
    member,
    overloadIndex: overloads[0]!.index,
    overload: overloads[0]!.overload,
  };
}

function installPackageHomeDemoSource(
  source: Extract<PreparedProductHomeDemoSource, { kind: "package" }>,
) {
  clearWorkspacePackages();
  for (const packageModel of source.packages) {
    retainPackageModel(packageModel);
    recordRecentPackage(
      packageModel.id,
      packageModel.version,
      packageModel.activeFramework);
  }
  if (state.packages.length !== source.packages.length
    || !state.packages.every((packageModel, index) =>
      packageIdentityEquals(packageModel, source.packages[index]))) {
    throw new Error(
      "The product demo package workspace did not retain its exact returned coordinates.");
  }
  refreshPackageStats();
  activatePackage(source.focusPackage, { resetAccessibility: true });
}

async function installPlatformHomeDemoSource(
  source: Extract<PreparedProductHomeDemoSource, { kind: "platform" }>,
  activation: BrowserHomeDemoRunActivation,
  demoId: ProductHomeDemoId,
  navigationSeq: number,
) {
  const target = await ensurePlatformCatalog(
    activation.focusFramework,
    activation.focusVersion);
  if (!navigationSequence.isCurrent(navigationSeq)) return false;
  const row = platformGraphLibraryForTarget(
    target,
    source.focusAssembly,
    source.focusPack);
  if (!row) {
    throw new Error(
      "The exact Platform catalog did not retain the engine-run demo focus.");
  }
  const descriptors = source.package.assemblies.filter(item =>
    platformLibraryMatchesDescriptor(row, item));
  if (descriptors.length !== 1) {
    throw new Error(
      "The engine-run Platform demo focus did not match one exact catalog Library.");
  }

  clearWorkspacePackages();
  retainPackageModel(source.package);
  const opened = await openPlatformLibrary(
    source.focusAssembly,
    source.focusPack,
    {
      scopeOnly: true,
      navigationSeq,
      tfm: target.tfm,
      version: target.version,
      retryAction: () => runHomeDemo(demoId),
    });
  if (!navigationSequence.isCurrent(navigationSeq)) return false;
  if (!opened || opened !== source.package) {
    throw new Error(
      "The native Platform Library path did not retain the engine-run demo surface.");
  }
  state.platformDemoContextId = source.contextId;
  return true;
}

function applyProductHomeDemoSelection(
  selection: ProductHomeDemoSelection,
  result: BrowserHomeDemoRunResult,
) {
  const { type, member, overloadIndex, overload } = selection;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.libraryScope = new Set([libraryKey(type)]);
  revealTypeInFilters(type);
  state.selectedTypeId = type.id;
  state.typeCursor = Math.max(
    0,
    filteredTypes().findIndex(item => item === type));
  state.atPackageRoot = false;
  state.atLibraryRoot = false;
  state.workspaceSubjectOpen = false;
  state.lens = "api";
  state.packageLens = "overview";
  resetMemberFilters();
  resetMemberSectionState();
  state.platformStack = [];
  state.memberBrowseTypeId = member ? type.id : "";
  state.selectedMemberKey = member?.key ?? "";
  state.selectedOverloadIndex = overloadIndex;

  if (!member || !overload) return;
  state.memberSection = "call-graph";
  state.memberCallGraph = result.callGraph;
  state.memberCallGraphError = "";
  state.memberCallGraphLoading = false;
  state.memberCallGraphExpanding = false;
  state.memberCallGraphKey = memberRequestSignature(
    type,
    overload,
    true);
}

function retainPackageHomeDemoShareBasis(
  source: Extract<PreparedProductHomeDemoSource, { kind: "package" }>,
  selection: ProductHomeDemoSelection,
) {
  const captured = capturedShareTabs();
  const activeIndex = state.packages.indexOf(source.focusPackage);
  const participantTabIds = source.packages.map(packageModel => {
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
  const activeTab = captured.tabs[activeIndex];
  if (!activeTab) {
    throw new Error(
      "The product demo focus is no longer part of the Browser workspace.");
  }
  state.workspaceShareBasis = {
    tabs: captured.tabs,
    contexts: topology.contexts,
    activeTabId: activeTab.id,
    selectedContextId: topology.selectedContextId,
    view: {
      lens: "api",
      type: selection.type.id,
      memberAnchor: selection.overload?.anchorDigest ?? null,
      memberSignature: null,
      section: selection.member ? "call-graph" : null,
      libraries: [],
    },
  };
}

async function runEngineHomeDemo(
  demoId: ProductHomeDemoId,
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  previousSnapshot: CanonicalWorkspaceRestoreSnapshot | null,
  navigationSeq: number,
) {
  state.loading = true;
  state.error = "";
  state.errorDetail = "";
  state.retryAction = null;
  state.loadingMessage = "Loading product demo…";
  state.loadingSubtitle =
    "Resolving the product workspace and selected view…";
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
  if (!result.activation) {
    fail("The engine returned an incomplete product home demo result.");
    return;
  }
  const activation = result.activation;
  const isMethods = activation.section === "Methods";
  const isCallGraph = activation.section === "Call Graph";
  if (!isMethods && !isCallGraph) {
    fail(
      `The engine returned unsupported demo section '${activation.section}'.`,
    );
    return;
  }
  if (isMethods
    && (activation.memberSection !== null || result.callGraph !== null)) {
    fail("The engine returned member or graph state for a Methods demo.");
    return;
  }
  if (isCallGraph
    && (activation.memberSection !== "call-graph" || !result.callGraph)) {
    fail("The engine returned an incomplete Call Graph demo result.");
    return;
  }
  try {
    const source = prepareProductHomeDemoSource(result);
    let selection: ProductHomeDemoSelection;
    if (source.kind === "package") {
      selection = selectProductHomeDemoTarget(
        source.focusPackage,
        activation,
        null,
        null);
      installPackageHomeDemoSource(source);
    } else {
      selection = selectProductHomeDemoTarget(
        source.package,
        activation,
        source.focusAssembly,
        source.focusPack);
      if (!await installPlatformHomeDemoSource(
        source,
        activation,
        demoId,
        navigationSeq)) return;
    }
    applyProductHomeDemoSelection(selection, result);
    if (source.kind === "package")
      retainPackageHomeDemoShareBasis(source, selection);
    else
      state.workspaceShareBasis = null;
    state.loading = false;
    const destination = (await buildStateUrl()).toString();
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    stageDemoNavigation(navigationSeq, destination);
    render();
    if (isCallGraph && result.callGraph) {
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
        fail("The call graph demo was superseded before publication.");
        return;
      }
      if (renderResult.status === "failed") {
        throw new Error(renderResult.message);
      }
    }
    const publication = stageCurrentWorkspacePublication(
      previousSnapshot,
      destination);
    if (!commitStagedWorkspaceNavigation(navigationSeq, publication)) {
      if (navigationSequence.isCurrent(navigationSeq)) {
        fail("The product demo could not commit its destination.");
      }
      return;
    }
    syncUrl();
    render({ synchronizeUrl: false });
    focusInspectionResult(navigationSeq);
  } catch (error) {
    cancelDemoNavigation(navigationSeq);
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    fail(error);
  }
}

// Loads the full open-tab set described by a parsed location (opaque workspace bucket, or a
// lone target), then restores the active tab's platform library scope and deep-link
// selection. Shared by boot restore and refreshed/shared links.
async function restoreWorkspaceFromLocation(
  loc: ParsedLocation,
  deep: DeepLink,
  navigationSeq = navigationSequence.begin(),
  canonicalSnapshot = loc.hasWorkspaceState
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null,
  focusResult = false,
  failureHandler: WorkspaceRestoreFailureHandler | null = null,
  commitHistory = failureHandler !== null,
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
    failureHandler,
    commitHistory);
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
  state.errorDetail = "";
  state.retryAction = null;
  resetLocationFilters();
  clearWorkspacePackages();
  render();
  const target: WorkspaceCoordinate = {
    id: loc.package,
    version: loc.version || "latest",
    framework: loc.framework || "",
    ...(loc.shareState ? {
      shareKind: loc.rootKind === "platform" ? "group" as const : "package" as const,
      shareSource: loc.rootKind === "platform" ? ":Platform" : "nuget.org",
    } : {}),
  };
  const tabs = (loc.tabs && loc.tabs.length)
    ? loc.tabs.slice(0, MAX_WORKSPACE_PACKAGES)
    : [target];
  type RestorableCoordinate = {
    id: string;
    version: string;
    framework?: string;
    activeFramework?: string;
    shareKind?: "package" | "group";
    shareSource?: string;
    source?: AppPackage["source"];
  };
  const platformCoordinate = (coordinate: RestorableCoordinate) =>
    coordinate.source ? coordinate.source.kind === "platform"
      : coordinate.shareKind ? coordinate.shareKind === "group" && coordinate.shareSource === ":Platform"
        : isRuntimePackId(coordinate.id);
  const matchesFramework = (tab: RestorableCoordinate) =>
    !target.framework
    || (tab.framework || tab.activeFramework || "").toLowerCase()
      === target.framework.toLowerCase();
  const matchesTarget = (tab: RestorableCoordinate) =>
    platformCoordinate(tab)
      ? platformCoordinate(target)
        && (target.version.toLowerCase() === "latest"
          || tab.version.toLowerCase() === target.version.toLowerCase())
        && matchesFramework(tab)
      : (!platformCoordinate(target) && tab.id.toLowerCase() === target.id.toLowerCase()
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
  let loadedPlatformTarget: PlatformCatalogTarget | null = null;
  let runtimeFailureMessage = "";
  let failedTabCount = 0;
  for (const tab of tabs) {
    let loaded: AppPackage | null;
    if (platformCoordinate(tab)) {
      try {
        const catalog = await ensurePlatformCatalog(
          tab.framework || DEFAULT_PLATFORM_FRAMEWORK,
          tab.version === "latest" ? undefined : tab.version);
        if (!navigationSequence.isCurrent(navigationSeq)) return;
        state.platformSelection = { tfm: catalog.tfm, version: catalog.version, includeAllLibraries: false, filter: "" };
        state.platformSlot = tabs.indexOf(tab);
        if (matchesTarget(tab)) loadedPlatformTarget = catalog;
        continue;
      } catch (error) {
        if (!navigationSequence.isCurrent(navigationSeq)) return;
        loaded = null;
        runtimeFailureMessage = errorMessage(error);
        state.queryNotice = `Workspace restore was incomplete: ${runtimeFailureMessage}`;
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
  if (loadedPlatformTarget) {
    if (loc.library) {
      const opened = await openPlatformLibrary(loc.library, loc.libraryPack ?? "", {
        deferPlatformPresentation: true,
        scopeOnly: true,
        navigationSeq,
        tfm: loadedPlatformTarget.tfm, version: loadedPlatformTarget.version,
      });
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      if (!opened) {
        failRestore(state.platformOpeningStatus.error || `Could not restore Platform Library '${loc.library}'.`);
        return;
      }
      applyLocationView(loc);
      const failure = canonicalViewRestorationFailure(opened, deep, loc.lens, loc.libraryLens);
      if (failure) { failRestore(failure); return; }
      applyDeepLink(deep);
    } else {
      installPlatformTarget(loadedPlatformTarget);
      if (loc.type || loc.memberAnchor || loc.memberSignature || loc.section || loc.lens || loc.libraryLens) {
        failRestore("A shared Platform inspection requires an exact Library.");
        return;
      }
      state.workspaceSubjectOpen = loc.workspaceSubjectOpen;
    }
    commitWorkspaceShareBasis(loc.shareState);
    if (!failureHandler) {
      ensureCurrentWorkspacePublished();
    }
    state.loading = false;
    render();
    startPlatformTargetWork(loadedPlatformTarget);
    await loadSelectionData();
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (!commitRestoredWorkspaceNavigation(navigationSeq, failureHandler, commitHistory)) return;
    if (focusResult) focusInspectionResult(navigationSeq);
    if (!failureHandler) {
      workspaceLocation.replace(location.href, history.state);
    }
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
    if (targetModel.source.kind === "platform") {
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
          failureHandler,
          commitHistory));
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
      ? canonicalViewRestorationFailure(
          targetModel,
          deep,
          loc.lens,
          loc.libraryLens,
          loc.atPackageRoot && !loc.workspaceSubjectOpen
            ? loc.packageLens
            : null)
      : null;
    if (loc.shareState && viewFailure) {
      failRestore(viewFailure);
      return;
    }
    applyDeepLink(deep);
    commitWorkspaceShareBasis(loc.shareState);
    if (!failureHandler) {
      ensureCurrentWorkspacePublished();
    }
    state.loading = false;
    render();
    await loadSelectionData();
    if (!navigationSequence.isCurrent(navigationSeq)) return;
    if (!commitRestoredWorkspaceNavigation(navigationSeq, failureHandler, commitHistory)) return;
    if (focusResult) {
      focusInspectionResult(navigationSeq);
    }
    if (!failureHandler) {
      workspaceLocation.replace(location.href, history.state);
    }
  } else if (!platformCoordinate(target)) {
    // The focused NuGet target failed to load during the silent background pass; re-run it in
    // the foreground so its error (e.g. a 404) surfaces properly instead of a blank workbench.
    const loaded = await loadPackage(
      target.id,
      target.version,
      target.framework,
      {
        location: loc,
        navigationSeq,
        queryNotice: state.queryNotice,
        deferWorkspacePublication: failureHandler !== null,
        retainFailureDetail: true,
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
    restartRestoredWorkspaceSelectionData();
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
  if (snapshot && workspaceFeedRollbackTransfers.has(snapshot)) {
    recoverWorkspaceNavigationRollback(
      snapshot,
      () => failCanonicalWorkspaceRestore(
        loc,
        deep,
        message,
        snapshot,
        retryAction));
    return;
  }
  discardPendingWorkspaceConstruction();
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
    restartRestoredWorkspaceSelectionData();
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
  state.rootKind = loc.rootKind;
  state.lens = loc.lens || "api";
  state.atPackageRoot = loc.atPackageRoot || false;
  if (loc.hasWorkspaceState
    && state.atPackageRoot
    && loc.packageLens === "dependencies") {
    state.dependenciesGroupIndex = null;
  }
  state.atLibraryRoot = !state.atPackageRoot
    && (loc.atLibraryRoot || false);
  state.workspaceSubjectOpen =
    loc.workspaceSubjectOpen && state.atPackageRoot;
  const pkg = state.package;
  if (pkg && !pkg.isRuntimePack && !loc.hasWorkspaceState
    && !loc.atPackageRoot && !loc.type && !loc.member && !loc.lens) {
    if (loc.library) {
      state.atLibraryRoot = true;
    } else {
      selectDefaultPackageSubject(pkg);
    }
  }
  state.packageLens = loc.packageLens || "overview";
  state.libraryLens = loc.libraryLens || "overview";
}

function workspaceFeedActivationCoordinator():
  WorkspaceFeedActivationCoordinator<
    CanonicalWorkspaceRestoreSnapshot
  > {
  workspaceFeedActivation ??= createWorkspaceFeedActivationCoordinator({
      client: engineClient.catalog,
      activationController: requireRetainedWorkspaceActivation(),
      document,
      applicationRoot: app,
      maxVisibleModels: MAX_WORKSPACE_PACKAGES,
      isCurrent: sequence => navigationSequence.isCurrent(sequence),
      beginNavigation: () => navigationSequence.begin(),
      hasVisibleWorkspace: () =>
        state.package !== null || state.platformSelection !== null,
      captureRollback: captureCanonicalWorkspaceRestoreSnapshot,
      cloneRollback: cloneCanonicalWorkspaceSnapshotForRetention,
      restoreRollback: restoreWorkspaceFeedRollback,
      releaseRollback: releaseRetainedWorkspaceSnapshot,
      publish: publishSourceBearingWorkspace,
      setLoading() {
        state.loading = true;
        state.loadingMessage = "Opening shared Workspace…";
        state.loadingSubtitle =
          "Loading package surfaces from the declared sources…";
        render({ synchronizeUrl: false });
      },
      pushLocation(destination) {
        activeWorkspaceUrl = destination;
        workspaceLocation.push(destination, history.state);
      },
      reportFailure(message, retry) {
        state.loading = false;
        if (state.package || state.platformSelection) {
          appendQueryNotice(`Workspace restore failed: ${message}`, retry);
        } else {
          state.errorTitle = "Workspace restore failed";
          state.error = message;
          state.retryAction = retry;
        }
        render({ synchronizeUrl: false });
      },
      reportBlockingFailure(message, retry) {
        discardPendingWorkspaceConstruction();
        clearWorkspacePackages();
        activeRetainedWorkspacePosting = null;
        retainedWorkspacePresentation = null;
        retainedWorkspaceInitialDetailAuthority = null;
        installedRetainedLocation = null;
        activeWorkspaceUrl = null;
        state.workspaceFeedUrl = null;
        state.loading = false;
        state.home = false;
        state.errorTitle = "Workspace recovery failed";
        state.error = message;
        state.retryAction = retry;
        render({ synchronizeUrl: false });
      },
      successorCommitted() {
        discardPendingWorkspaceConstruction();
      },
      reportPredecessorFailure(error) {
        showToast(
          `Could not confirm the replaced Workspace was released: ${
            errorMessage(error)
          }`);
      },
      observe: observeAsync,
      errorMessage,
      escapeHtml,
      trapModalTab,
    });
  return workspaceFeedActivation;
}

async function restoreWorkspaceFeedRollback(
  snapshot: CanonicalWorkspaceRestoreSnapshot,
  isCurrent: () => boolean,
): Promise<void> {
  if (!isCurrent()) return;
  const priorPosting = snapshot.activeRetainedWorkspacePosting;
  if (priorPosting !== null
    && retainedWorkspaceActivation?.state.definitions.some(
      definition => definition.id
        === priorPosting.retainedDefinitionId)) {
    const restored = await workspaceFeedActivationCoordinator()
      .reactivateRetainedDefinition(
        priorPosting.retainedDefinitionId,
        navigationSequence.current(),
        isCurrent,
        posting => {
          if (!isCurrent()) {
            throw new Error(
              "The incumbent Workspace recovery was superseded.");
          }
          snapshot.activeRetainedWorkspacePosting = posting;
          snapshot.retainedWorkspaceInitialDetailAuthority = null;
          restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
          activeWorkspaceUrl = snapshot.url;
          render({ synchronizeUrl: false });
        });
    if (!isCurrent()) return;
    if (!restored) {
      throw new Error(
        "The incumbent retained Workspace could not be reactivated.");
    }
    return;
  }
  if (!isCurrent()) return;
  restoreCanonicalWorkspaceRestoreSnapshot(snapshot);
  activeWorkspaceUrl = snapshot.url;
  render({ synchronizeUrl: false });
}

async function tryOpenSourceBearingWorkspace(
  url: URL,
  navigationSeq: number,
  commitHistory = false,
): Promise<boolean> {
  let inheritedRollback:
    WorkspaceFeedRollbackTransfer<CanonicalWorkspaceRestoreSnapshot> | null =
      null;
  if (workspaceFeedRollbackTransfer !== null) {
    inheritedRollback = workspaceFeedRollbackTransfer.transfer();
    if (inheritedRollback !== null) {
      workspaceFeedRollbackTransfer = inheritedRollback;
      workspaceFeedRollbackTransfers.set(
        inheritedRollback.snapshot,
        inheritedRollback);
    }
  }
  const handled = await workspaceFeedActivationCoordinator().tryOpen(
    url,
    navigationSeq,
    commitHistory,
    inheritedRollback);
  if (handled
    && inheritedRollback !== null
    && workspaceFeedRollbackTransfer === inheritedRollback) {
    workspaceFeedRollbackTransfer = null;
    if (workspaceFeedRollbackTransfers.get(inheritedRollback.snapshot)
      === inheritedRollback) {
      workspaceFeedRollbackTransfers.delete(inheritedRollback.snapshot);
    }
  }
  return handled;
}

function cancelWorkspaceCredentialPrompt(showFailure = true): void {
  workspaceFeedActivation?.cancelPrompt(showFailure);
}

function clearWorkspaceFeedIdentity(): void {
  if (activeRetainedWorkspacePosting !== null
    && workspaceFeedActivation?.ownsRetainedDefinition(
      activeRetainedWorkspacePosting.retainedDefinitionId)) {
    activeRetainedWorkspacePosting = null;
    retainedWorkspacePresentation = null;
    retainedWorkspaceInitialDetailAuthority = null;
    installedRetainedLocation = null;
  }
  workspaceFeedActivation?.clearActiveUrl();
  state.workspaceFeedUrl = null;
}

function publishSourceBearingWorkspace(
  posting: BrowserRetainedWorkspacePosting,
  models: RetainedWorkspaceModels,
): void {
  const allModels = [
    ...models.packages.map(item => item.packageModel),
    ...models.platforms.map(item => item.packageModel),
  ];
  if (allModels.length === 0) {
    throw new Error("The retained Workspace did not publish a package surface.");
  }

  clearWorkspacePackages();
  for (const packageModel of allModels) retainPackageModel(packageModel);
  activeRetainedWorkspacePosting = posting;
  retainedWorkspacePresentation =
    createNavigationDescriptorPresentation(posting);
  retainedWorkspaceInitialDetailAuthority = null;
  installedRetainedLocation = null;
  const activeTab = posting.definition.activeTabId === null
    ? null
    : posting.definition.tabs.find(
        tab => tab.id === posting.definition.activeTabId);
  const activePackageSubjectId =
    posting.navigation.snapshot.activePackage;
  const activePackage = models.packages.find(item =>
    item.consumerPackageSubjectId === activePackageSubjectId)?.packageModel
    ?? (activeTab?.kind === "package"
    ? models.packages.find(item =>
        item.packageModel.id.toLowerCase() === activeTab.source.toLowerCase()
        && (!activeTab.version
          || item.packageModel.version.toLowerCase()
            === activeTab.version.toLowerCase())
        && (!activeTab.framework
          || item.packageModel.activeFramework.toLowerCase()
            === activeTab.framework.toLowerCase()))?.packageModel
    : activeTab?.kind === "group"
      ? models.platforms[0]?.packageModel
      : null);
  const selected = activePackage ?? allModels[0]!;
  activatePackage(selected, { resetAccessibility: true });
  state.home = false;
  state.credits = false;
  state.loading = false;
  state.error = "";
  state.errorTitle = "";
  state.errorDetail = "";
  state.retryAction = null;
  state.requestedPackage = selected.id;
  state.requestedVersion = selected.version;
  state.requestedFramework = selected.activeFramework;
  state.workspaceSubjectOpen = activeTab === null;
  state.atPackageRoot = true;
  state.atLibraryRoot = false;
  state.packageLens = "overview";
  state.libraryLens = "overview";
  state.selectedTypeId = defaultVisibleTypeId(selected);
  state.selectedMemberKey = "";
  state.memberBrowseTypeId = "";
  state.selectedOverloadIndex = null;
  resetLocationFilters();
  resetMemberSectionState();
  commitWorkspaceShareBasis(null);
  state.workspaceFeedUrl = posting.canonicalLocation;
  activeWorkspaceUrl = posting.canonicalLocation;
  render({ synchronizeUrl: false });
}

// Restores the full open-tab set from the opaque workspace bucket (or just the visible
// target for a lone/legacy link), loading each tab in order so the tab bar and any
// cross-package dependency edges come back. Only the focused target restores its deep-link.
async function restoreInitialWorkspace() {
  const navigationSeq = navigationSequence.current();
  const preflight = workspaceLocation.preflightCurrent();
  if (await tryOpenSourceBearingWorkspace(
    new URL(location.href),
    navigationSeq)) {
    return;
  }
  const loc = await preflight.resolve();
  if (!navigationSequence.isCurrent(navigationSeq)) return;
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

function showEngineFailure(error: unknown) {
  state.loading = false;
  state.engineReady = false;
  state.engineRuntimeReady = false;
  state.engineStartupFailed = true;
  settleLibraryEngineReadyWaiters(error instanceof Error
    ? error
    : new Error(String(error)));
  state.engineStatus = "";
  state.error =
    "Couldn’t start the inspection engine. Retry, or open a different package.";
  state.errorTitle = "Startup failed";
  state.errorDetail = error instanceof Error
    ? error.stack || error.message
    : String(error);
  if (state.buildIdentityStatus !== "ready") {
    state.buildIdentity = null;
    state.buildIdentityStatus = "failed";
    state.buildIdentityError =
      "Product build identity is unavailable because the inspection engine did not start.";
  }
  state.retryAction = () => window.location.reload();
  if (!state.credits) render();
}

async function loadBuildIdentity() {
  try {
    state.buildIdentity = await engineClient.host.buildIdentity();
    state.buildIdentityStatus = "ready";
  } catch (error) {
    state.buildIdentity = null;
    state.buildIdentityStatus = "failed";
    state.buildIdentityError =
      `Product build identity is unavailable: ${errorMessage(error)}`;
    console.error("Product build identity is unavailable.", error);
  }
  if (isDiagnosticsPath(location.pathname)) {
    state.diagnosticsCapturedAtUtc = new Date().toISOString();
    render();
  } else if (state.engineReady && !state.credits) {
    render();
  }
}

async function bootstrap() {
  state.loading = !state.home;
  state.engineReady = false;
  state.engineRuntimeReady = false;
  state.engineStartupFailed = false;
  state.engineStatus = "Loading browser WebAssembly…";
  state.buildIdentity = null;
  state.buildIdentityStatus = "loading";
  state.buildIdentityError = "";
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
    const tEngine = performance.now();
    state.engineRuntimeReady = true;
    reportEngineStatus("Reading package assemblies…");
    void loadBuildIdentity();
    if (isDiagnosticsPath(location.pathname)) {
      state.loading = false;
      refreshPackageStats();
    }
    try {
      const vocabulary = await engineClient.catalog.listVocabulary();
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
      setProductHomeDemoCatalog((await engineClient.catalog.listHomeDemos()).demos ?? []);
      productHomeDemoCatalogError = "";
    } catch (error) {
      setProductHomeDemoCatalog([]);
      productHomeDemoCatalogError =
        `Product demos are unavailable: ${errorMessage(error) || "Unknown error."}`;
    }
    try {
      state.packageChangesPackageSets = packageChangesPackageSets(
        await engineClient.package.listPackageActivityPackageSets());
      state.packageChangesCatalogError = "";
    } catch (error) {
      state.packageChangesPackageSets = [];
      state.packageChangesCatalogError =
        `Package Activity package sets are unavailable: ${errorMessage(error) || "Unknown error."}`;
    }
    try {
      const catalog =
        packageQueryCatalog(await engineClient.package.listPackageQueryCatalog());
      state.packageQueryPresets = catalog.presets;
      state.packageQueryTerms = catalog.terms;
    } catch (error) {
      state.packageQueryPresets = [];
      state.packageQueryTerms = [];
      state.packageQueryCatalogError =
        `Package-query vocabulary is unavailable: ${errorMessage(error) || "Unknown error."}`;
    }
    state.engineReady = true;
    settleLibraryEngineReadyWaiters();
    state.engineStatus = "";
    if (isDiagnosticsPath(location.pathname)) {
      state.loading = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      return;
    }
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
    if (state.packageActivityOpen) {
      state.loading = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      focusPackageActivityInput();
      return;
    }
    if (isProductHomeDemosPath(location.pathname)) {
      state.loading = false;
      state.workspaceSubjectOpen = true;
      state.atPackageRoot = true;
      state.atLibraryRoot = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      if (!state.packageQueryReturnFocusPending) {
        afterCurrentNavigationFrame(() =>
          focusWorkspaceOrHeading());
      }
      return;
    }
    await restoreInitialWorkspace();
    const tReady = performance.now();
    state.diag = computeDiagnostics(tStart, tEngine, tReady);
    render();
  } catch (error) {
    console.error("Inspection engine startup failed.", error);
    showEngineFailure(error);
  }
}

function computeDiagnostics(
  tStart: number,
  tEngine: number,
  tReady: number,
): RuntimeStartupDiagnostics {
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
    downloadMs: hasAssets ? lastEnd - firstStart : null,
    startupMs: hasAssets ? Math.max(0, tEngine - lastEnd) : tEngine - tStart,
    precomputeMs: tReady - tEngine,
    totalMs: tReady - tStart,
    transfer: hasAssets ? transfer : null,
    decoded: hasAssets ? decoded : null,
    assets: hasAssets ? assets.length : null
  };
}

function refreshPackageStats() {
  const request = ++packageCacheStatsRequest;
  state.packageCacheStatsStatus = "loading";
  state.packageCacheStatsError = "";
  if (isDiagnosticsPath(location.pathname)) render();
  void inspectPackageCacheStats().then(
    stats => {
      if (request !== packageCacheStatsRequest) return undefined;
      state.packageCacheStats = stats;
      state.packageCacheStatsStatus = "ready";
      state.diagnosticsCapturedAtUtc = new Date().toISOString();
      if (isDiagnosticsPath(location.pathname)) render();
      return undefined;
    },
    (error: unknown) => {
      if (request !== packageCacheStatsRequest) return undefined;
      state.packageCacheStats = null;
      state.packageCacheStatsStatus = "failed";
      state.packageCacheStatsError =
        `Package-cache statistics are unavailable: ${errorMessage(error)}`;
      state.diagnosticsCapturedAtUtc = new Date().toISOString();
      console.error("Package-cache statistics are unavailable.", error);
      if (isDiagnosticsPath(location.pathname)) render();
      return undefined;
    },
  );
}


// A same-origin, unmodified `<a href>` click anywhere in the app takes over here instead
// of loading a new document — this is the single owner of in-app link navigation.
// `target="_blank"`, cross-origin hrefs, `download`, and modified clicks (new tab/window)
// keep their native browser behavior; the guard lives in `shouldInterceptLinkClick`.
async function navigateWithinCurrentWorkspace(
  loc: ParsedLocation,
  navigationSeq: number,
): Promise<void> {
  if (loc.rootKind === "platform") {
    await restoreRetainedWorkspaceFromHistory(loc, navigationSeq);
    return;
  }
  const canonicalSnapshot = loc.hasWorkspaceState
    ? captureWorkspaceNavigationRollback()
    : null;
  state.credits = false;
  resetLocationFilters();
  const target = loc.package
    ? state.packages.find(candidate =>
      packageCoordinateMatchesLocation(candidate, loc))
    : null;
  if (loc.package && !target) {
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      "The selected package is not available in this Workspace.",
      canonicalSnapshot);
    return;
  }
  if (target) {
    activatePackage(target, { resetAccessibility: true });
  }
  state.home = false;
  if (isRuntimePackId(state.package?.id ?? "")) {
    applyLocationView(loc);
    await restorePlatformScopeThenDeepLink(
      loc,
      navigationSeq,
      canonicalSnapshot,
      true);
    return;
  }
  const pkg = state.package;
  if (!pkg) return;
  const libraryFailure = applyLoadedPackageLibraryScope(pkg, loc.library);
  applyLocationView(loc);
  const viewFailure = loc.shareState
    ? canonicalViewRestorationFailure(
        pkg,
        loc,
        loc.lens,
        loc.libraryLens,
        loc.atPackageRoot && !loc.workspaceSubjectOpen
          ? loc.packageLens
          : null)
    : null;
  const restorationFailure = libraryFailure ?? viewFailure;
  if (loc.shareState && restorationFailure) {
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      restorationFailure,
      canonicalSnapshot);
    return;
  }
  clearWorkspaceFeedIdentity();
  commitWorkspaceShareBasis(loc.shareState);
  applyDeepLink(loc);
  render();
  await loadSelectionData();
}

async function openFreshWorkspaceLink(
  loc: ParsedLocation,
  navigationSeq: number,
): Promise<void> {
  if (!canPublishRetainedWorkspace()) {
    appendQueryNotice(retainedWorkspaceCapacityMessage(), null);
    render({ synchronizeUrl: false });
    return;
  }
  const construction =
    captureWorkspaceConstructionSnapshots(navigationSeq);
  prepareUnpublishedWorkspace();
  let failed = false;
  const fail = (message: string) => {
    failed = true;
    failWorkspaceCatalogAction(
      `Workspace link failed: ${message}`,
      construction.supersessionSnapshot,
      () => {
        const retrySeq = navigationSequence.begin();
        observeAsync(
          openFreshWorkspaceLink(loc, retrySeq),
          "Retrying workspace link");
      },
      focusWorkbenchSearchOrHeading,
    );
  };
  try {
    await restoreWorkspaceFromLocation(
      loc,
      loc,
      navigationSeq,
      construction.supersessionSnapshot,
      false,
      fail,
      false);
    if (!failed
      && navigationSequence.isCurrent(navigationSeq)
      && (state.package || state.platformSelection)) {
      const destination = (await buildStateUrl()).toString();
      if (!navigationSequence.isCurrent(navigationSeq)) return;
      clearWorkspaceFeedIdentity();
      publishCurrentWorkspace(construction.retainedSnapshot);
      workspaceLocation.push(destination);
      render({ synchronizeUrl: false });
    }
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) {
      fail(errorMessage(error));
    }
  }
}

async function navigateInAppUrl(url: URL) {
  if (isDiagnosticsPath(url.pathname)) {
    openDiagnosticsRoute();
    return;
  }
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
  if (isPackageActivityPath(url.pathname)) {
    openPackageActivityRoute();
    return;
  }
  if (url.pathname === "/" && !url.search && !url.hash) {
    goHome();
    return;
  }
  const focusWorkspaceAfterRoutedPage =
    state.packageQueryOpen || state.packageActivityOpen;
  if (focusWorkspaceAfterRoutedPage) {
    discardPackageQueryTermEditors();
    state.packageQueryOpen = false;
    state.packageActivityOpen = false;
    packageQueryController.cancel();
    packageChangesController.cancel("disposed");
    state.packageQueryNavigationError = "";
  }
  const navigationSeq = navigationSequence.begin();
  cancelWorkspaceCredentialPrompt(false);
  if (focusWorkspaceAfterRoutedPage) {
    packageQueryWorkspaceFocusNavigationSeq = navigationSeq;
  }
  if (await tryOpenSourceBearingWorkspace(url, navigationSeq, true)) return;
  let loc: ParsedLocation;
  try {
    loc = await parseWorkspaceHref(url.toString());
  } catch (error) {
    if (navigationSequence.isCurrent(navigationSeq)) throw error;
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (loc.hasWorkspaceState && !loc.shareState) {
    appendQueryNotice(
      `Workspace restore failed: ${loc.workspaceNotice
        || "The shared workspace packet could not be restored."}`,
      null);
    render({ synchronizeUrl: false });
    return;
  }
  const tablessTarget = !loc.tabs.length && loc.package
    ? state.packages.find(candidate =>
      packageCoordinateMatchesLocation(candidate, loc))
    : undefined;
  const sameWorkspace = loc.tabs.length
    ? workspaceCoordinatesMatch(state.packages, loc.tabs)
    : tablessTarget !== undefined;
  if (!sameWorkspace) {
    observeAsync(
      openFreshWorkspaceLink(loc, navigationSeq),
      "Opening workspace link");
  } else {
    workspaceLocation.push(url.toString());
    observeAsync(
      navigateWithinCurrentWorkspace(loc, navigationSeq),
      "Navigating");
  }
}

bindWorkspaceLinkNavigation(document, {
  currentOrigin: () => location.origin,
  resolve: href => new URL(href, location.href),
  navigate: url => observeAsync(
    navigateInAppUrl(url),
    "Opening the selected link"),
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
  return pendingWorkspaceConstruction === null
    && !graphExplorer.isOpen
    && !state.explorer?.open
    && !state.settings
    && !state.keyboardHelp
    && !state.home
    && !state.packageQueryOpen
    && !state.packageActivityOpen
    && !state.loading
    && !state.error
    && !graphSourceIsOpen(state.graphSource)
    && !documentViewerIsOpen(state.docViewer)
    && state.memberAnnotatedModal === null
    && !state.spotlightOpen;
}

const workspaceModalContextIsAvailable = () =>
  pendingWorkspaceConstruction === null
  && !state.home
  && !state.packageQueryOpen
  && !state.packageActivityOpen
  && !state.loading
  && !state.error;
const graphSourceContextIsActive = () =>
  workspaceModalContextIsAvailable() && graphSourceIsOpen(state.graphSource);
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
  workspaceModalContextIsAvailable() && documentViewerIsOpen(state.docViewer);
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
    stepNav(event.key === "ArrowDown" ? 1 : -1, event.target);
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
  cancelWorkspaceCredentialPrompt(false);
  closeGraphExplorerForNavigation();
  const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal(false);
  state.settings = false;
  state.keyboardHelp = false;
  libraryOpenSequence++;
  state.libraryOpen = false;
  state.libraryOpenBusy = false;
  state.libraryOpenError = "";
  state.explorer = null;
  spotlight.reset();
  sourceInspection.clearGraphSource();
  documentInspection.clear();
  return dismissedAnnotatedSourceModal;
}

window.addEventListener("popstate", () => {
  void (async () => {
  if (!isDiagnosticsPath(location.pathname)
    && document.querySelector(".diagnostics-view")) {
    diagnosticsDestinationFocusPending = true;
  }
  const leftPackageQueryHandoff = currentPackageQueryHandoff();
  const navigationSeq = navigationSequence.begin();
  let leftPackageQueryForWorkspaceSuccessor = false;
  let unavailableWorkspaceAdmissionRejected = false;
  const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation();
  invalidateMemberDestinationWork(state);
  const historyWorkspaceId =
    retainedWorkspaceIdFromHistory(history.state);
  const historyWorkspaceReferenced =
    historyReferencesRetainedWorkspace(history.state);
  const sourceHistoryWorkspaceAvailable = historyWorkspaceId !== null
    && workspaceFeedActivation?.ownsRetainedDefinition(
      historyWorkspaceId) === true;
  const managedHistoryWorkspaceAvailable = !sourceHistoryWorkspaceAvailable
    && historyWorkspaceId !== null
    && retainedWorkspaceActivation?.state.definitions.some(
      definition => definition.id === historyWorkspaceId) === true;
  let restoredActiveManagedWorkspace = false;
  if (managedHistoryWorkspaceAvailable && historyWorkspaceId !== null) {
    const locationIntent = retainedLocationIntents.selectBrowserEntry({
      url: location.href,
      historyState: history.state,
      retainedDefinitionId: historyWorkspaceId,
      incumbent: installedRetainedLocation,
    });
    try {
      if (retainedWorkspaceActivation?.state.activeDefinitionId
          === historyWorkspaceId) {
        const effect = retainedLocationIntents.classify(locationIntent, {
          outcome: "applied",
          synchronization: "current",
          association: retainedLocationFallbackAssociation(),
          browserRestoration: "exact",
        });
        retainedLocationIntents.publish(effect, history);
        restoredActiveManagedWorkspace = true;
      } else {
        const result = await requireRetainedWorkspaceActivation().activate(
          historyWorkspaceId,
          () => retainedLocationIntents.currentIntentId === locationIntent.id,
          posting => installRetainedWorkspacePosting(
            posting,
            locationIntent,
            posting.canonicalLocation === location.href ? "exact" : "changed",
          ),
          undefined,
          undefined,
          posting => retainedLocationPresentationCurrent(
            locationIntent,
            posting.canonicalLocation,
          ),
        );
        if (result.status === "failed") {
          realignRetainedLocationIntent(locationIntent, "failed");
          showToast(
            result.failure?.message
              ?? "Could not restore the retained Workspace.",
          );
        }
      }
    } catch (error) {
      realignRetainedLocationIntent(locationIntent, "failed");
      showToast(`Could not restore Workspace: ${errorMessage(error)}`);
    }
    if (!restoredActiveManagedWorkspace) return;
  }
  const historyWorkspaceAvailable = historyWorkspaceId !== null
    && retainedWorkspaces.workspaces.some(
      workspace => workspace.id === historyWorkspaceId);
  const deferHistoryWorkspaceActivation =
    location.pathname === "/"
    && new URL(location.href).searchParams.has("w");
  if (historyWorkspaceAvailable
    && historyWorkspaceId !== null
    && retainedWorkspaceActivation !== null
    && (retainedWorkspaceActivation.state.activeDefinitionId !== null
      || retainedWorkspaceActivation.state.pendingDefinitionId !== null)
    && !deferHistoryWorkspaceActivation) {
    const locationIntent = retainedLocationIntents.selectBrowserEntry({
      url: location.href,
      historyState: history.state,
      retainedDefinitionId: historyWorkspaceId,
      incumbent: installedRetainedLocation,
    });
    try {
      await activateCompatibilityRetainedWorkspace(historyWorkspaceId, {
        declaration: locationIntent,
        restoration:
          activeWorkspaceUrl === location.href ? "exact" : "changed",
      });
    } catch (error) {
      realignRetainedLocationIntent(locationIntent, "failed");
      showToast(`Could not restore Workspace: ${errorMessage(error)}`);
    }
    return;
  }
  if (historyWorkspaceId !== null
    && issuedManagedRetainedDefinitionIds.has(historyWorkspaceId)
    && !managedHistoryWorkspaceAvailable) {
    const locationIntent = retainedLocationIntents.selectBrowserEntry({
      url: location.href,
      historyState: history.state,
      retainedDefinitionId: historyWorkspaceId,
      incumbent: installedRetainedLocation,
    });
    realignRetainedLocationIntent(locationIntent, "unavailable");
    showToast("That retained Workspace is no longer available.");
    return;
  }
  if (historyWorkspaceAvailable
    && historyWorkspaceId !== retainedWorkspaces.activeWorkspaceId
    && !deferHistoryWorkspaceActivation) {
    try {
      activateRetainedWorkspaceProjection(historyWorkspaceId, false);
    } catch (error) {
      showToast(`Could not activate Workspace: ${errorMessage(error)}`);
      return;
    }
  }
  const unavailableGlobalWorkspace =
    historyWorkspaceReferenced
    && !managedHistoryWorkspaceAvailable
    && !historyWorkspaceAvailable
    && (isDiagnosticsPath(location.pathname)
      || isPackageQueryPath(location.pathname)
      || isPackageActivityPath(location.pathname)
      || isCreditsPath(location.pathname)
      || isProductHomeDemosPath(location.pathname));
  if (unavailableGlobalWorkspace) {
    unavailableWorkspaceAdmissionRejected =
      !publishFreshEmptyWorkspaceFromHistory(location.href);
  }
  if (dismissedAnnotatedSourceModal) render({ synchronizeUrl: false });
  if (isDiagnosticsPath(location.pathname)) {
    diagnosticsDestinationFocusPending = false;
    diagnosticsDestinationFocusGeneration = null;
    clearNavigationError();
    discardPackageQueryTermEditors();
    state.packageQueryOpen = false;
    state.packageActivityOpen = false;
    packageQueryController.cancel();
    packageChangesController.cancel("disposed");
    state.credits = false;
    state.home = false;
    state.loading = false;
    state.diagnosticsCapturedAtUtc = new Date().toISOString();
    diagnosticsHeadingFocusPending = true;
    if (state.engineRuntimeReady) {
      state.packageCacheStatsStatus = "loading";
      state.packageCacheStatsError = "";
    }
    render();
    if (state.engineRuntimeReady) refreshPackageStats();
    return;
  }
  if (isPackageQueryPath(location.pathname)) {
    clearNavigationError();
    applyPackageQueryHistory(history.state);
    packageQueryHandoffNavigationSeq = null;
    state.packageQueryOpen = true;
    state.packageActivityOpen = false;
    packageChangesController.cancel("disposed");
    state.credits = false;
    state.home = false;
    state.loading = !state.engineReady;
    render();
    if (state.engineReady) focusPackageQueryInput();
    return;
  }
  if (isPackageActivityPath(location.pathname)) {
    clearNavigationError();
    applyPackageActivityHistory(history.state);
    packageQueryHandoffNavigationSeq = null;
    state.packageQueryOpen = false;
    state.packageActivityOpen = true;
    packageQueryController.cancel();
    discardPackageQueryTermEditors();
    state.credits = false;
    state.home = false;
    state.loading = !state.engineReady;
    render();
    if (state.engineReady) focusPackageActivityInput();
    return;
  }
  state.loading = false;
  if (state.packageQueryOpen || leftPackageQueryHandoff) {
    discardPackageQueryTermEditors();
    state.packageQueryOpen = false;
    packageQueryHandoffNavigationSeq = null;
    packageQueryController.cancel();
    packageChangesController.cancel("disposed");
    state.packageQueryReturnFocusPending =
      state.packageQueryReturnFocus !== null
      && isPackageQueryPredecessor(
        history.state,
        state.packageQueryPredecessorEntryId);
    leftPackageQueryForWorkspaceSuccessor =
      !state.packageQueryReturnFocusPending;
  }
  if (state.packageActivityOpen) {
    state.packageActivityOpen = false;
    packageChangesController.cancel("disposed");
    state.packageActivityReturnFocusPending =
      state.packageActivityReturnFocus !== null
      && isPackageActivityPredecessor(
        history.state,
        state.packageActivityPredecessorEntryId);
    leftPackageQueryForWorkspaceSuccessor =
      leftPackageQueryForWorkspaceSuccessor
      || !state.packageActivityReturnFocusPending;
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
    render({
      synchronizeUrl: !unavailableWorkspaceAdmissionRejected,
    });
    return;
  }
  if (isProductHomeDemosPath(location.pathname)) {
    clearNavigationError();
    if (!clearWorkspaceRouteFailure()) {
      render();
      return;
    }
    const focusWorkspaceOnEntry =
      !state.packageQueryReturnFocusPending
      && !state.packageActivityReturnFocusPending;
    state.queryNotice = "";
    state.queryNoticeRetryAction = null;
    state.credits = false;
    state.home = false;
    state.workspaceSubjectOpen = true;
    state.atPackageRoot = true;
    state.atLibraryRoot = false;
    state.loading = !state.engineReady;
    render();
    if (state.engineReady && focusWorkspaceOnEntry) {
      afterCurrentNavigationFrame(() =>
        focusWorkspaceOrHeading());
    }
    return;
  }
  if (restoredActiveManagedWorkspace
    && activeRetainedWorkspacePosting?.canonicalLocation === location.href) {
    state.credits = false;
    state.home = false;
    render({ synchronizeUrl: false });
    return;
  }
  if (leftPackageQueryForWorkspaceSuccessor) {
    packageQueryWorkspaceFocusNavigationSeq = navigationSeq;
  }
  if (!state.engineReady) {
    if (deferHistoryWorkspaceActivation
      && historyWorkspaceAvailable
      && historyWorkspaceId !== retainedWorkspaces.activeWorkspaceId) {
      try {
        activateRetainedWorkspaceProjection(historyWorkspaceId, false);
      } catch (error) {
        showToast(`Could not activate Workspace: ${errorMessage(error)}`);
        return;
      }
    }
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
    state.atLibraryRoot = false;
    state.loading = !state.home;
    if (state.home) clearNavigationError();
    render();
    return;
  }
  if (await tryOpenSourceBearingWorkspace(
    new URL(location.href),
    navigationSeq)) {
    return;
  }
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (deferHistoryWorkspaceActivation
    && historyWorkspaceAvailable
    && historyWorkspaceId !== retainedWorkspaces.activeWorkspaceId) {
    if (historyWorkspaceId !== null
      && retainedWorkspaceActivation !== null
      && (retainedWorkspaceActivation.state.activeDefinitionId !== null
        || retainedWorkspaceActivation.state.pendingDefinitionId !== null)) {
      const locationIntent = retainedLocationIntents.selectBrowserEntry({
        url: location.href,
        historyState: history.state,
        retainedDefinitionId: historyWorkspaceId,
        incumbent: installedRetainedLocation,
      });
      try {
        await activateCompatibilityRetainedWorkspace(historyWorkspaceId, {
          declaration: locationIntent,
          restoration:
            activeWorkspaceUrl === location.href ? "exact" : "changed",
        });
      } catch (error) {
        realignRetainedLocationIntent(locationIntent, "failed");
        showToast(`Could not restore Workspace: ${errorMessage(error)}`);
      }
      return;
    }
    try {
      activateRetainedWorkspaceProjection(historyWorkspaceId, false);
    } catch (error) {
      showToast(`Could not activate Workspace: ${errorMessage(error)}`);
      return;
    }
  }
  const loc = await parseLocation();
  if (!navigationSequence.isCurrent(navigationSeq)) return;
  if (loc.routeFailure) {
    failWorkspaceRoute(loc.routeFailure.message);
    return;
  }
  if (!clearWorkspaceRouteFailure()) {
    render();
    return;
  }
  state.queryNotice = loc.workspaceNotice || "";
  state.queryNoticeRetryAction = null;
  if (loc.hasWorkspaceState && !loc.shareState) {
    const invalidSnapshot = captureCanonicalWorkspaceRestoreSnapshot();
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      loc.workspaceNotice
        || "The shared workspace packet could not be restored.",
      invalidSnapshot,
      null);
    return;
  }
  const bareHome = !loc.package && !(loc.tabs && loc.tabs.length);
  if (bareHome) {
    if (historyWorkspaceReferenced
      && !managedHistoryWorkspaceAvailable
      && !historyWorkspaceAvailable) {
      unavailableWorkspaceAdmissionRejected =
        !publishFreshEmptyWorkspaceFromHistory(location.href);
    }
    // Navigated back to the bare root — show the intro/home page (engine stays warm).
    clearNavigationError();
    state.credits = false;
    state.home = true;
    spotlight.reset();
    clearWorkspaceFeedIdentity();
    render({
      synchronizeUrl: !unavailableWorkspaceAdmissionRejected,
    });
    return;
  }
  if (historyWorkspaceReferenced && !historyWorkspaceAvailable) {
    observeAsync(
      restoreFreshWorkspaceFromHistory(loc, navigationSeq),
      "Restoring workspace history");
    return;
  }
  const canonicalSnapshot = loc.hasWorkspaceState
    ? captureCanonicalWorkspaceRestoreSnapshot()
    : null;
  state.credits = false;
  resetLocationFilters();
  const deep = loc;
  const restoreHistoryWorkspace = () => historyWorkspaceAvailable
    ? restoreRetainedWorkspaceFromHistory(loc, navigationSeq)
    : historyWorkspaceReferenced
      || retainedWorkspaces.activeWorkspaceId === null
      ? restoreFreshWorkspaceFromHistory(loc, navigationSeq)
      : restoreRetainedWorkspaceFromHistory(loc, navigationSeq);
  if (loc.rootKind === "platform") {
    observeAsync(restoreHistoryWorkspace(), "Restoring Platform history");
    return;
  }
  if (!state.package) {
    observeAsync(
      restoreHistoryWorkspace(),
      "Restoring workspace history");
    return;
  }
  if (loc.tabs?.length && !workspaceCoordinatesMatch(state.packages, loc.tabs)) {
    observeAsync(
      restoreHistoryWorkspace(),
      "Restoring workspace history");
    return;
  }
  const target = loc.package
    ? state.packages.find(candidate =>
      packageCoordinateMatchesLocation(candidate, loc))
    : null;
  if (loc.tabs?.length && !target) {
    observeAsync(
      restoreHistoryWorkspace(),
      "Restoring workspace history");
    return;
  }
  if (target) {
    activatePackage(target, { resetAccessibility: true });
  }
  state.home = false;
  const samePackage = packageCoordinateMatchesLocation(state.package, loc);
  if (samePackage || !loc.package) {
    if (isRuntimePackId(state.package.id)) {
      applyLocationView(loc);
      // Back/forward within the platform: re-scope to the target library (or the
      // aggregate) before restoring selection, since scope is part of the view.
      observeAsync(
        restorePlatformScopeThenDeepLink(
          loc,
          navigationSeq,
          canonicalSnapshot,
          true),
        "Restoring platform history");
    } else {
      const libraryFailure = applyLoadedPackageLibraryScope(
        state.package,
        loc.library);
      applyLocationView(loc);
      const viewFailure = loc.shareState
        ? canonicalViewRestorationFailure(
            state.package,
            loc,
            loc.lens,
            loc.libraryLens,
            loc.atPackageRoot && !loc.workspaceSubjectOpen
              ? loc.packageLens
              : null)
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
      clearWorkspaceFeedIdentity();
      commitWorkspaceShareBasis(loc.shareState);
      applyDeepLink(loc);
      render();
      observeAsync(loadSelectionData(), "Loading selection data");
    }
  } else if (isRuntimePackId(loc.package)) {
    observeAsync(
      restoreHistoryWorkspace(),
      "Restoring workspace history");
  } else {
    observeAsync(
      restoreHistoryWorkspace(),
      "Restoring package history");
  }
  })().catch((error: unknown) => {
    reportAsyncFailure("Navigating browser history", error);
  });
});

// Re-scope the active runtime pack to the platform library named in a share/history packet
// (lazily loading that assembly if needed via the same drill-in path as clicking it), or
// clear the scope for the aggregate platform, then restore the deep-linked selection.
async function restorePlatformScopeThenDeepLink(
  loc: ParsedLocation,
  navigationSeq: number,
  canonicalSnapshot: CanonicalWorkspaceRestoreSnapshot | null = null,
  retireWorkspaceFeed = false,
) {
  const scoped = await applyPlatformLibraryScope(
    loc.library,
    loc.libraryPack,
    navigationSeq,
    () => restorePlatformScopeThenDeepLink(
      loc,
      navigationSequence.current(),
      canonicalSnapshot,
      retireWorkspaceFeed));
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
    ? canonicalViewRestorationFailure(
        pkg,
        loc,
        loc.lens,
        loc.libraryLens,
        loc.atPackageRoot && !loc.workspaceSubjectOpen
          ? loc.packageLens
          : null)
    : null;
  if (loc.shareState && viewFailure) {
    failCanonicalWorkspaceRestore(
      loc,
      loc,
      viewFailure,
      canonicalSnapshot);
    return;
  }
  if (retireWorkspaceFeed) clearWorkspaceFeedIdentity();
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
  if (navigationSeq != null && !navigationSequence.isCurrent(navigationSeq))
    return undefined;
  return Boolean(await openPlatformLibrary(
    key,
    libraryPack ?? "",
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
  const requested = requestedLibraryKey ?? "";
  if (!requested) {
    state.libraryScope = null;
    return null;
  }
  const matchingLibrary = resolvePackageLibrary(pkg.assemblies, requested);
  if (!matchingLibrary) {
    return `The shared library '${requestedLibraryKey}' is not uniquely available in ${pkg.id}.`;
  }
  state.libraryScope = new Set([matchingLibrary.id]);
  return null;
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
    if (state.spotlightOpen) spotlight.refresh();
    return undefined;
  }),
  "Loading the platform index");
