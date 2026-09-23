import { dotnet } from "./runtime-loader.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserWorkspacePackageSourceAuthentication = "Anonymous" | "AuthenticationRequired" | number;

export type JsonValueKind = number;

export interface BrowserAccessibilityDescriptor {
  readonly id: string;
  readonly label: string;
  readonly order: number;
  readonly isDefault: boolean;
  readonly count: number;
}

export interface BrowserAssemblySurface {
  readonly id: string;
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
  readonly asset: string;
  readonly publicTypes: number;
  readonly publicMembers: number;
  readonly platformPack: string | null;
}

export interface BrowserCallGraph {
  readonly mermaid: string;
  readonly callers: BrowserCallGraphNode;
  readonly callees: BrowserCallGraphNode;
  readonly scope: BrowserCallGraphScope;
  readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
  readonly diagnostics: BrowserCallGraphDiagnostics;
  readonly noBody: boolean;
}

export interface BrowserCallGraphDiagnostics {
  readonly incompleteNodes: number;
  readonly incompleteEdges: number;
  readonly bindingIdentityConflicts: number;
  readonly hasUnexploredTraversalBoundary: boolean;
  readonly hasAnalysisFailureBoundary: boolean;
  readonly unavailableDependencyRoutes: number;
  readonly isIncomplete: boolean;
}

export interface BrowserCallGraphNode {
  readonly label: string;
  readonly status: string;
  readonly inLoop: boolean;
  readonly source: string | null;
  readonly children: ReadonlyArray<BrowserCallGraphNode>;
  readonly assembly: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface BrowserCallGraphScope {
  readonly packages: number;
  readonly assemblies: number;
  readonly callerAssemblies: number;
  readonly calleeScope: string;
}

export interface BrowserCallGraphTarget {
  readonly id: string;
  readonly assembly: string;
  readonly assemblyVersion: string | null;
  readonly assemblyCulture: string | null;
  readonly assemblyPublicKeyToken: string | null;
  readonly typeFullName: string;
  readonly typeMetadataId: string | null;
  readonly typeDefinitionId: string | null;
  readonly memberName: string;
  readonly parameterTypes: ReadonlyArray<string>;
  readonly returnType: string;
  readonly genericArity: number;
  readonly metadataToken: number | null;
  readonly selectorKey: string;
  readonly kind: string;
  readonly platformPack: string | null;
  readonly surfaceAssemblyId: string | null;
  readonly packageId: string | null;
  readonly packageVersion: string | null;
  readonly packageFramework: string | null;
}

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserHomeDemoCatalog {
  readonly demos: ReadonlyArray<BrowserHomeDemoCatalogEntry>;
}

export interface BrowserHomeDemoCatalogEntry {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
}

export interface BrowserHomeDemoMember {
  readonly kind: string;
  readonly id: string;
  readonly version: string | null;
  readonly framework: string | null;
  readonly assembly: string | null;
}

export interface BrowserHomeDemoNavigationTab {
  readonly id: string;
  readonly member: BrowserHomeDemoMember;
}

export interface BrowserHomeDemoResolveResult {
  readonly found: boolean;
  readonly demo: BrowserHomeDemoResolved | null;
}

export interface BrowserHomeDemoResolved {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
  readonly workspaceMembers: ReadonlyArray<BrowserHomeDemoMember>;
  readonly tabs: ReadonlyArray<BrowserHomeDemoNavigationTab>;
  readonly focusTabIndex: number;
  readonly view: BrowserHomeDemoView;
}

export interface BrowserHomeDemoRunActivation {
  readonly focusKind: string;
  readonly focusId: string;
  readonly focusVersion: string;
  readonly focusFramework: string;
  readonly focusAssembly: string | null;
  readonly typeId: string;
  readonly section: string;
  readonly memberName: string | null;
  readonly memberKind: string | null;
  readonly memberAnchorDigest: string | null;
  readonly memberSection: string | null;
  readonly platformContextId: string | null;
}

export interface BrowserHomeDemoRunResult {
  readonly found: boolean;
  readonly packages: ReadonlyArray<BrowserPackageSurface>;
  readonly activation: BrowserHomeDemoRunActivation | null;
  readonly callGraph: BrowserCallGraph | null;
}

export interface BrowserHomeDemoView {
  readonly library: string | null;
  readonly type: string | null;
  readonly memberAnchor: string | null;
  readonly memberKey: string | null;
  readonly section: string | null;
}

export interface BrowserMemberBodySelector {
  readonly token: number;
  readonly memberName: string;
  readonly selectorKey: string;
}

export interface BrowserMemberSurface {
  readonly name: string;
  readonly kind: string;
  readonly signature: string;
  readonly accessibility: string;
  readonly isStatic: boolean;
  readonly isUnsafe: boolean;
  readonly isVirtual: boolean;
  readonly isAbstract: boolean;
  readonly isOverride: boolean;
  readonly isExtension: boolean;
  readonly isObsolete: boolean;
  readonly genericArity: number;
  readonly metadataToken: number | null;
  readonly declarationMetadataToken: number | null;
  readonly returnType: string | null;
  readonly parameters: ReadonlyArray<BrowserParameterSurface>;
  readonly documentationId: string | null;
  readonly summary: string | null;
  readonly returns: string | null;
  readonly exceptions: ReadonlyArray<BrowserExceptionSurface>;
  readonly stableSelector: string;
  readonly anchorDigest: string;
  readonly canonicalSignature: string;
  readonly anchorTypeFullName: string;
  readonly graphSelectorKey: string;
  readonly bodySelectors: ReadonlyArray<BrowserMemberBodySelector>;
}

export interface BrowserPackageDocument {
  readonly kind: string;
  readonly name: string;
  readonly path: string;
  readonly size: number;
}

export interface BrowserPackageIcon {
  readonly mediaType: string;
  readonly base64: string;
}

export interface BrowserPackageSurface {
  readonly package: string;
  readonly version: string;
  readonly frameworks: ReadonlyArray<string>;
  readonly activeFramework: string;
  readonly icon: BrowserPackageIcon | null;
  readonly defaultAssemblyId: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
  readonly assemblies: ReadonlyArray<BrowserAssemblySurface>;
  readonly types: ReadonlyArray<BrowserTypeSurface>;
  readonly accessibility: ReadonlyArray<BrowserAccessibilityDescriptor>;
  readonly totalMembers: number;
  readonly documents: ReadonlyArray<BrowserPackageDocument>;
  readonly inspectionErrors: ReadonlyArray<string>;
  readonly inspectionError: string | null;
}

export interface BrowserParameterSurface {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
}

export interface BrowserRetainedNavigationAction {
  readonly session: string;
  readonly generation: string;
  readonly id: string;
  readonly source: string;
  readonly kind: string;
}

export interface BrowserRetainedNavigationAuthority {
  readonly session: string;
  readonly revision: string;
  readonly intent: string;
  readonly epoch: string;
}

export interface BrowserRetainedNavigationCoordinateOutcome {
  readonly disposition: string;
  readonly detail: string;
  readonly libraryPairing: string | null;
  readonly typeCorrespondence: string | null;
  readonly memberCorrespondence: string | null;
}

export interface BrowserRetainedNavigationDiagnostic {
  readonly kind: string;
  readonly library: string;
  readonly message: string;
}

export interface BrowserRetainedNavigationFacet {
  readonly id: string;
  readonly kind: string;
  readonly title: string;
  readonly summary: string;
  readonly order: number;
  readonly role: string | null;
}

export interface BrowserRetainedNavigationLens {
  readonly id: string;
  readonly subject: BrowserRetainedNavigationSubject;
  readonly facet: string;
}

export interface BrowserRetainedNavigationLensDescriptor {
  readonly facet: BrowserRetainedNavigationFacet;
  readonly state: string;
  readonly isCurrent: boolean;
  readonly target: BrowserRetainedNavigationLens | null;
  readonly unavailability: string | null;
  readonly message: string | null;
  readonly action: BrowserRetainedNavigationAction | null;
}

export interface BrowserRetainedNavigationLensOutcome {
  readonly kind: string;
  readonly basis: string;
  readonly subject: BrowserRetainedNavigationSubject;
  readonly effectiveLens: BrowserRetainedNavigationLens | null;
  readonly request: BrowserRetainedNavigationLens | null;
  readonly preferredRole: string | null;
  readonly policyFailure: string | null;
  readonly resolution: BrowserRetainedNavigationResolution | null;
  readonly suspension: BrowserRetainedNavigationRealization | null;
}

export interface BrowserRetainedNavigationLibraryDescriptor {
  readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
  readonly assetId: string | null;
  readonly isAggregate: boolean;
  readonly isPrimary: boolean;
}

export interface BrowserRetainedNavigationMemberDescriptor {
  readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
  readonly library: string;
  readonly containingType: string;
  readonly declaringType: string;
  readonly accessibility: string | null;
  readonly memberKind: string;
  readonly signature: string | null;
  readonly descendantLenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
}

export interface BrowserRetainedNavigationOutcome {
  readonly kind: string;
  readonly rejection: string | null;
  readonly failureSource: string | null;
  readonly message: string | null;
  readonly request: BrowserRetainedNavigationRequest | null;
  readonly resolution: BrowserRetainedNavigationResolution | null;
  readonly scope: BrowserRetainedNavigationScopeOutcome | null;
  readonly diagnostics: ReadonlyArray<BrowserRetainedNavigationDiagnostic>;
  readonly coordinateRetention: BrowserRetainedNavigationCoordinateOutcome | null;
}

export interface BrowserRetainedNavigationPackageDescriptor {
  readonly order: number;
  readonly subject: BrowserRetainedNavigationSubject;
  readonly packageId: string;
  readonly version: string;
  readonly framework: string | null;
  readonly runtimeIdentifier: string | null;
  readonly realization: string;
  readonly realizationFailure: string | null;
  readonly state: string;
  readonly isCurrent: boolean;
  readonly action: BrowserRetainedNavigationAction | null;
}

export interface BrowserRetainedNavigationRealization {
  readonly kind: string;
  readonly failure: string | null;
}

export interface BrowserRetainedNavigationRequest {
  readonly source: BrowserRetainedNavigationSubject;
  readonly destination: BrowserRetainedNavigationSubject;
  readonly lens: BrowserRetainedNavigationLens | null;
}

export interface BrowserRetainedNavigationResolution {
  readonly kind: string;
  readonly descriptor: BrowserRetainedNavigationFacet | null;
  readonly unavailability: string | null;
  readonly message: string | null;
}

export interface BrowserRetainedNavigationResult {
  readonly operation: string;
  readonly request: string;
  readonly snapshot: BrowserRetainedNavigationSnapshot;
  readonly outcome: BrowserRetainedNavigationOutcome;
  readonly synchronization: string;
  readonly authority: BrowserRetainedNavigationAuthority | null;
}

export interface BrowserRetainedNavigationScopeOutcome {
  readonly kind: string;
  readonly operation: string;
  readonly rejection: string | null;
  readonly failure: string | null;
}

export interface BrowserRetainedNavigationScopeStatus {
  readonly kind: string;
  readonly runtimeFailure: string | null;
}

export interface BrowserRetainedNavigationSnapshot {
  readonly generation: string;
  readonly scope: BrowserRetainedNavigationScopeStatus;
  readonly workspace: BrowserRetainedNavigationSubject;
  readonly activePackage: string | null;
  readonly activeSubject: BrowserRetainedNavigationSubject;
  readonly typeInventoryLibraryContext: BrowserRetainedNavigationSubject | null;
  readonly packages: ReadonlyArray<BrowserRetainedNavigationPackageDescriptor>;
  readonly hierarchy: ReadonlyArray<BrowserRetainedNavigationSubjectDescriptor>;
  readonly libraries: ReadonlyArray<BrowserRetainedNavigationLibraryDescriptor>;
  readonly types: ReadonlyArray<BrowserRetainedNavigationTypeDescriptor>;
  readonly members: ReadonlyArray<BrowserRetainedNavigationMemberDescriptor>;
  readonly lenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
  readonly lensOutcome: BrowserRetainedNavigationLensOutcome;
  readonly diagnostics: ReadonlyArray<BrowserRetainedNavigationDiagnostic>;
}

export interface BrowserRetainedNavigationSubject {
  readonly id: string;
  readonly kind: string;
  readonly label: string;
  readonly summary: string | null;
  readonly parent: string | null;
}

export interface BrowserRetainedNavigationSubjectDescriptor {
  readonly kind: string;
  readonly label: string;
  readonly subject: BrowserRetainedNavigationSubject | null;
  readonly state: string;
  readonly isActive: boolean;
  readonly isRetained: boolean;
  readonly evidence: ReadonlyArray<BrowserRetainedNavigationDiagnostic>;
  readonly action: BrowserRetainedNavigationAction | null;
}

export interface BrowserRetainedNavigationTypeDescriptor {
  readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
  readonly library: string;
  readonly accessibility: string | null;
  readonly typeKind: string;
  readonly descendantLenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
}

export interface BrowserRetainedWorkspaceActivationFailure {
  readonly kind: string;
  readonly message: string;
}

export interface BrowserRetainedWorkspaceActivationResult {
  readonly status: string;
  readonly posting: BrowserRetainedWorkspacePosting | null;
  readonly failure: BrowserRetainedWorkspaceActivationFailure | null;
}

export interface BrowserRetainedWorkspaceCleanup {
  readonly message: string;
}

export interface BrowserRetainedWorkspaceConsumerCompletionResult {
  readonly status: string;
  readonly succeeded: boolean | null;
  readonly failure: string | null;
  readonly message: string | null;
}

export interface BrowserRetainedWorkspaceDeactivationResult {
  readonly status: string;
  readonly completionReceipt: string | null;
  readonly settlement: BrowserRetainedWorkspaceSettlement | null;
  readonly message: string | null;
}

export interface BrowserRetainedWorkspaceDefinitionState {
  readonly tabs: ReadonlyArray<BrowserWorkspaceShareTab>;
  readonly contexts: ReadonlyArray<BrowserWorkspaceShareContext>;
  readonly registrations: ReadonlyArray<BrowserRetainedWorkspaceRegistration>;
  readonly activeTabId: string | null;
  readonly selectedContextId: string | null;
}

export interface BrowserRetainedWorkspaceEcosystem {
  readonly id: string;
  readonly namespaceRoots: ReadonlyArray<string>;
  readonly corePackages: ReadonlyArray<string>;
  readonly populations: ReadonlyArray<BrowserRetainedWorkspaceEcosystemPopulation>;
}

export interface BrowserRetainedWorkspaceEcosystemPopulation {
  readonly kind: string;
  readonly exactLibrary: BrowserRetainedWorkspaceExactLibrary | null;
  readonly platformFamily: string | null;
  readonly packagePrefix: string | null;
}

export interface BrowserRetainedWorkspaceExactLibrary {
  readonly kind: string;
  readonly library: BrowserRetainedWorkspaceLibraryIdentity;
  readonly packageId: string | null;
  readonly packageVersion: string | null;
  readonly platformFamily: string | null;
}

export interface BrowserRetainedWorkspaceLibraryIdentity {
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

export interface BrowserRetainedWorkspacePackage {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly consumerPackageSubjectId: string;
  readonly surface: BrowserPackageSurface;
  readonly typePage: BrowserRetainedWorkspaceTypePage;
}

export interface BrowserRetainedWorkspacePackageAdmissionResult {
  readonly status: string;
  readonly package: BrowserRetainedWorkspacePackage | null;
  readonly message: string | null;
}

export interface BrowserRetainedWorkspacePackageInventory {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly consumerPackageSubjectId: string;
  readonly summary: BrowserRetainedWorkspaceSurfaceSummary;
}

export interface BrowserRetainedWorkspacePackageSourceCredential {
  readonly username: string;
  readonly pat: string;
}

export interface BrowserRetainedWorkspacePlatform {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly family: string;
  readonly runtimeIdentifier: string | null;
  readonly surface: BrowserPackageSurface;
  readonly typePage: BrowserRetainedWorkspaceTypePage;
}

export interface BrowserRetainedWorkspacePlatformAdmissionResult {
  readonly status: string;
  readonly platform: BrowserRetainedWorkspacePlatform | null;
  readonly message: string | null;
}

export interface BrowserRetainedWorkspacePlatformInventory {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly family: string;
  readonly runtimeIdentifier: string | null;
  readonly summary: BrowserRetainedWorkspaceSurfaceSummary;
}

export interface BrowserRetainedWorkspacePosting {
  readonly retainedDefinitionId: string;
  readonly label: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
  readonly realizationId: string;
  readonly publicationOrdinal: number;
  readonly definition: BrowserRetainedWorkspaceDefinitionState;
  readonly navigation: BrowserRetainedNavigationResult;
  readonly packages: ReadonlyArray<BrowserRetainedWorkspacePackageInventory>;
  readonly platforms: ReadonlyArray<BrowserRetainedWorkspacePlatformInventory>;
  readonly predecessor: BrowserRetainedWorkspacePredecessor | null;
  readonly cleanup: BrowserRetainedWorkspaceCleanup | null;
}

export interface BrowserRetainedWorkspacePredecessor {
  readonly settlementId: string;
  readonly reason: string;
}

export interface BrowserRetainedWorkspacePreparationResult {
  readonly status: string;
  readonly receipt: string | null;
  readonly preparation: BrowserRetainedWorkspacePreparedPosting | null;
  readonly posting: BrowserRetainedWorkspacePosting | null;
  readonly failure: BrowserRetainedWorkspaceActivationFailure | null;
}

export interface BrowserRetainedWorkspacePreparedPosting {
  readonly retainedDefinitionId: string;
  readonly label: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
  readonly definition: BrowserRetainedWorkspaceDefinitionState;
  readonly navigation: BrowserRetainedNavigationResult;
  readonly packages: ReadonlyArray<BrowserRetainedWorkspacePackageInventory>;
  readonly platforms: ReadonlyArray<BrowserRetainedWorkspacePlatformInventory>;
}

export interface BrowserRetainedWorkspaceRegistration {
  readonly kind: string;
  readonly exactLibrary: BrowserRetainedWorkspaceExactLibrary | null;
  readonly packagePrefix: string | null;
  readonly ecosystem: BrowserRetainedWorkspaceEcosystem | null;
}

export interface BrowserRetainedWorkspaceSettlement {
  readonly succeeded: boolean;
  readonly reason: string;
  readonly failure: string | null;
}

export interface BrowserRetainedWorkspaceSettlementResult {
  readonly status: string;
  readonly settlement: BrowserRetainedWorkspaceSettlement | null;
}

export interface BrowserRetainedWorkspaceSurfaceSummary {
  readonly selectedCompileFramework: string | null;
  readonly libraryCount: number;
  readonly typeCount: number;
  readonly memberCount: number;
  readonly documentCount: number;
  readonly hasInspectionNotices: boolean;
}

export interface BrowserRetainedWorkspaceTypePage {
  readonly offset: number;
  readonly totalTypes: number;
  readonly nextOffset: number | null;
}

export interface BrowserTypeSurface {
  readonly id: string;
  readonly definitionId: string;
  readonly queryId: string;
  readonly metadataId: string;
  readonly name: string;
  readonly displayName: string;
  readonly namespace: string;
  readonly kind: string;
  readonly accessibility: string;
  readonly accessibilityId: string;
  readonly assembly: string;
  readonly assemblyId: string;
  readonly assemblyName: string;
  readonly members: number;
  readonly signature: string;
  readonly api: ReadonlyArray<BrowserMemberSurface>;
  readonly platformPack: string | null;
}

export interface BrowserVocabularyDocument {
  readonly schema_version: number;
  readonly sections: ReadonlyArray<BrowserVocabularySection>;
}

export interface BrowserVocabularyField {
  readonly id: string;
  readonly label: string;
  readonly summary: string;
  readonly type: string;
  readonly operators: ReadonlyArray<string>;
}

export interface BrowserVocabularySection {
  readonly id: string;
  readonly name: string;
  readonly summary: string;
  readonly accepted_by: ReadonlyArray<string>;
  readonly fields: ReadonlyArray<BrowserVocabularyField>;
  readonly values: ReadonlyArray<unknown>;
}

export interface BrowserWorkspacePackageSourceRequirement {
  readonly endpoint: string;
  readonly authentication: BrowserWorkspacePackageSourceAuthentication;
}

export interface BrowserWorkspacePackageSourceRequirementsResult {
  readonly succeeded: boolean;
  readonly sources: ReadonlyArray<BrowserWorkspacePackageSourceRequirement>;
  readonly failure: BrowserWorkspaceShareFailure | null;
}

export interface BrowserWorkspaceShareContext {
  readonly id: string;
  readonly tabIds: ReadonlyArray<string>;
}

export interface BrowserWorkspaceShareDecodeResult {
  readonly succeeded: boolean;
  readonly state: BrowserWorkspaceShareState | null;
  readonly failure: BrowserWorkspaceShareFailure | null;
}

export interface BrowserWorkspaceShareEncodeResult {
  readonly succeeded: boolean;
  readonly packet: string | null;
  readonly failure: BrowserWorkspaceShareFailure | null;
}

export interface BrowserWorkspaceShareFailure {
  readonly kind: string;
  readonly path: string;
  readonly message: string;
}

export interface BrowserWorkspaceShareState {
  readonly tabs: ReadonlyArray<BrowserWorkspaceShareTab>;
  readonly contexts: ReadonlyArray<BrowserWorkspaceShareContext>;
  readonly activeTabId: string;
  readonly selectedContextId: string;
  readonly view: BrowserWorkspaceShareView;
}

export interface BrowserWorkspaceShareTab {
  readonly id: string;
  readonly kind: string;
  readonly source: string;
  readonly version: string | null;
  readonly framework: string | null;
  readonly runtimeIdentifier: string | null;
}

export interface BrowserWorkspaceShareView {
  readonly lens: string | null;
  readonly type: string | null;
  readonly memberAnchor: string | null;
  readonly memberSignature: string | null;
  readonly section: string | null;
  readonly libraries: ReadonlyArray<string>;
}

type $ManagedExports = {
  readonly "DotnetInspect": {
    readonly "Web": {
      readonly "Interop": {
        readonly "Catalog": {
          readonly "CatalogExports": {
            readonly "AbandonRetainedWorkspaceNavigation.1618630472": (realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string) => string;
            readonly "AcknowledgeRetainedWorkspaceNavigation.1618630472": (realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string) => string;
            readonly "ActivateRetainedWorkspaceDefinition.1579276339": (retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string) => Promise<string>;
            readonly "ActivateRetainedWorkspaceDefinitionWithCredentials.1330709314": (retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string, packageSourceCredentialsJson: string) => Promise<string>;
            readonly "AdmitRetainedWorkspacePackage.2036994461": (retainedDefinitionId: string, realizationId: string, navigationId: string, typeOffset: number) => Promise<string>;
            readonly "AdmitRetainedWorkspacePlatform.2036994461": (retainedDefinitionId: string, realizationId: string, navigationId: string, typeOffset: number) => Promise<string>;
            readonly "CancelRetainedWorkspaceActivation.976702342": (receipt: string) => Promise<string>;
            readonly "CanonicalizeWorkspaceSharePacket.304094707": (encoded: string) => string;
            readonly "CaptureCompleteWorkspaceShareState.304094707": (stateJson: string) => string;
            readonly "CommitRetainedWorkspaceActivation.976702342": (receipt: string) => Promise<string>;
            readonly "CompleteRetainedWorkspaceActivation.377497262": (receipt: string, succeeded: boolean, failure: string | null) => string;
            readonly "CompleteRetainedWorkspaceDeactivation.377497262": (receipt: string, succeeded: boolean, failure: string | null) => string;
            readonly "DeactivateRetainedWorkspaceDefinition.976702342": (retainedDefinitionId: string) => Promise<string>;
            readonly "DecodeWorkspaceShareState.304094707": (encoded: string) => string;
            readonly "DescribeWorkspacePackageSources.304094707": (canonicalPacket: string) => string;
            readonly "EncodeWorkspaceShareState.304094707": (stateJson: string) => string;
            readonly "ListHomeDemos.1310674786": () => string;
            readonly "ListVocabulary.1310674786": () => string;
            readonly "ObserveRetainedWorkspaceSettlement.976702342": (settlementId: string) => Promise<string>;
            readonly "PrepareRetainedWorkspaceDefinition.1579276339": (retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string) => Promise<string>;
            readonly "PrepareRetainedWorkspaceDefinitionWithCredentials.1330709314": (retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string, packageSourceCredentialsJson: string) => Promise<string>;
            readonly "RecordRetainedWorkspaceNavigationPosting.1618630472": (realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string) => string;
            readonly "ResolveHomeDemo.304094707": (scenarioId: string) => string;
            readonly "RunHomeDemo.976702342": (scenarioId: string) => Promise<string>;
            readonly "ValidateRetainedWorkspaceNavigationAuthority.1044747233": (realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string) => boolean;
          };
        };
      };
    };
  };
};

export interface JsExportRuntime {
  readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
  readonly runMain: (
    mainAssemblyName?: string,
    args?: string[],
  ) => Promise<number>;
}

const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime: JsExportRuntime | undefined;
let $managedExports: $ManagedExports | undefined;
let $initialization: Promise<void> | undefined;
let $initializationFailure: { readonly error: unknown } | undefined;

function $ownDataProperty(value: unknown, key: string): unknown {
  if (value === null || (typeof value !== "object" && typeof value !== "function")) {
    throw new Error(`Managed export path '${key}' has a non-object parent.`);
  }
  const descriptor = Object.getOwnPropertyDescriptor(value, key);
  if (descriptor === undefined || !("value" in descriptor)) {
    throw new Error(`Managed export path '${key}' is not an own data property.`);
  }
  return descriptor.value;
}

function $requireRuntime(): JsExportRuntime {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($runtime === undefined) {
    throw $notInitializedError;
  }
  return $runtime;
}

function $requireManagedExports(): $ManagedExports {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($managedExports === undefined) {
    throw $notInitializedError;
  }
  return $managedExports;
}

function $validateManagedExports(exports: unknown): asserts exports is $ManagedExports {
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "AbandonRetainedWorkspaceNavigation.1618630472");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AbandonRetainedWorkspaceNavigation.1618630472\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "AcknowledgeRetainedWorkspaceNavigation.1618630472");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AcknowledgeRetainedWorkspaceNavigation.1618630472\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ActivateRetainedWorkspaceDefinition.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ActivateRetainedWorkspaceDefinitionWithCredentials.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ActivateRetainedWorkspaceDefinitionWithCredentials.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "AdmitRetainedWorkspacePackage.2036994461");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AdmitRetainedWorkspacePackage.2036994461\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "AdmitRetainedWorkspacePlatform.2036994461");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AdmitRetainedWorkspacePlatform.2036994461\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CancelRetainedWorkspaceActivation.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CancelRetainedWorkspaceActivation.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CanonicalizeWorkspaceSharePacket.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CanonicalizeWorkspaceSharePacket.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CaptureCompleteWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CaptureCompleteWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CommitRetainedWorkspaceActivation.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CommitRetainedWorkspaceActivation.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CompleteRetainedWorkspaceActivation.377497262");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CompleteRetainedWorkspaceActivation.377497262\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "CompleteRetainedWorkspaceDeactivation.377497262");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CompleteRetainedWorkspaceDeactivation.377497262\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "DeactivateRetainedWorkspaceDefinition.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DeactivateRetainedWorkspaceDefinition.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "DescribeWorkspacePackageSources.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DescribeWorkspacePackageSources.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ListHomeDemos.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListHomeDemos.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ListVocabulary.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListVocabulary.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ObserveRetainedWorkspaceSettlement.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ObserveRetainedWorkspaceSettlement.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "PrepareRetainedWorkspaceDefinition.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.PrepareRetainedWorkspaceDefinition.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "PrepareRetainedWorkspaceDefinitionWithCredentials.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.PrepareRetainedWorkspaceDefinitionWithCredentials.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "RecordRetainedWorkspaceNavigationPosting.1618630472");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.RecordRetainedWorkspaceNavigationPosting.1618630472\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ResolveHomeDemo.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "RunHomeDemo.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemo.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Catalog");
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ValidateRetainedWorkspaceNavigationAuthority.1044747233");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ValidateRetainedWorkspaceNavigationAuthority.1044747233\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Catalog");
  $validateManagedExports(exports);
  $runtime = runtime;
  $managedExports = exports;
}

export function createRuntime(): Promise<JsExportRuntime> {
  return dotnet.create();
}

export function initializeRuntime(
  runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
): Promise<void> {
  if ($initialization === undefined) {
    $initialization = Promise.resolve()
      .then(() => runtime === undefined ? createRuntime() : runtime)
      .then($initializeRuntimeCore)
      .catch((error: unknown) => {
        $initializationFailure = { error };
        throw error;
      });
  }
  return $initialization;
}

export function runEntryPoint(
  mainAssemblyName?: string,
  args?: string[],
): Promise<number> {
  return $requireRuntime().runMain(mainAssemblyName, args);
}

function $serializeJsonInput(
  value: unknown,
  operation: string,
  parameter: string,
): string {
  const json = JSON.stringify(value);
  if (json === undefined) {
    throw new TypeError(
      `${operation} parameter '${parameter}' could not be serialized as JSON.`,
    );
  }
  return json;
}

export function abandonRetainedWorkspaceNavigation(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string {
  return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AbandonRetainedWorkspaceNavigation.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}

export function acknowledgeRetainedWorkspaceNavigation(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string {
  return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AcknowledgeRetainedWorkspaceNavigation.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}

export async function activateRetainedWorkspaceDefinition(retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string): Promise<BrowserRetainedWorkspaceActivationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ActivateRetainedWorkspaceDefinition.1579276339"](retainedDefinitionId, label, canonicalLocation, canonicalPacket);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceActivationResult;
}

export async function activateRetainedWorkspaceDefinitionWithCredentials(retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string, packageSourceCredentialsJson: Readonly<Record<string, BrowserRetainedWorkspacePackageSourceCredential>>): Promise<BrowserRetainedWorkspaceActivationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ActivateRetainedWorkspaceDefinitionWithCredentials.1330709314"](retainedDefinitionId, label, canonicalLocation, canonicalPacket, $serializeJsonInput(packageSourceCredentialsJson, "DotnetInspect.Web.Interop.Catalog.CatalogExports.ActivateRetainedWorkspaceDefinitionWithCredentials.1330709314", "packageSourceCredentialsJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceActivationResult;
}

export async function admitRetainedWorkspacePackage(retainedDefinitionId: string, realizationId: string, navigationId: string, typeOffset: number): Promise<BrowserRetainedWorkspacePackageAdmissionResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AdmitRetainedWorkspacePackage.2036994461"](retainedDefinitionId, realizationId, navigationId, typeOffset);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspacePackageAdmissionResult;
}

export async function admitRetainedWorkspacePlatform(retainedDefinitionId: string, realizationId: string, navigationId: string, typeOffset: number): Promise<BrowserRetainedWorkspacePlatformAdmissionResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AdmitRetainedWorkspacePlatform.2036994461"](retainedDefinitionId, realizationId, navigationId, typeOffset);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspacePlatformAdmissionResult;
}

export async function cancelRetainedWorkspaceActivation(receipt: string): Promise<BrowserRetainedWorkspaceActivationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CancelRetainedWorkspaceActivation.976702342"](receipt);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceActivationResult;
}

export function canonicalizeWorkspaceSharePacket(encoded: string): BrowserWorkspaceShareEncodeResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CanonicalizeWorkspaceSharePacket.304094707"](encoded);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareEncodeResult;
}

export function captureCompleteWorkspaceShareState(stateJson: BrowserWorkspaceShareState): BrowserWorkspaceShareEncodeResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CaptureCompleteWorkspaceShareState.304094707"]($serializeJsonInput(stateJson, "DotnetInspect.Web.Interop.Catalog.CatalogExports.CaptureCompleteWorkspaceShareState.304094707", "stateJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareEncodeResult;
}

export async function commitRetainedWorkspaceActivation(receipt: string): Promise<BrowserRetainedWorkspaceActivationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CommitRetainedWorkspaceActivation.976702342"](receipt);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceActivationResult;
}

export function completeRetainedWorkspaceActivation(receipt: string, succeeded: boolean, failure: string | null): BrowserRetainedWorkspaceConsumerCompletionResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CompleteRetainedWorkspaceActivation.377497262"](receipt, succeeded, failure);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceConsumerCompletionResult;
}

export function completeRetainedWorkspaceDeactivation(receipt: string, succeeded: boolean, failure: string | null): BrowserRetainedWorkspaceConsumerCompletionResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CompleteRetainedWorkspaceDeactivation.377497262"](receipt, succeeded, failure);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceConsumerCompletionResult;
}

export async function deactivateRetainedWorkspaceDefinition(retainedDefinitionId: string): Promise<BrowserRetainedWorkspaceDeactivationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DeactivateRetainedWorkspaceDefinition.976702342"](retainedDefinitionId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceDeactivationResult;
}

export function decodeWorkspaceShareState(encoded: string): BrowserWorkspaceShareDecodeResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DecodeWorkspaceShareState.304094707"](encoded);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareDecodeResult;
}

export function describeWorkspacePackageSources(canonicalPacket: string): BrowserWorkspacePackageSourceRequirementsResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DescribeWorkspacePackageSources.304094707"](canonicalPacket);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageSourceRequirementsResult;
}

export function encodeWorkspaceShareState(stateJson: BrowserWorkspaceShareState): BrowserWorkspaceShareEncodeResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["EncodeWorkspaceShareState.304094707"]($serializeJsonInput(stateJson, "DotnetInspect.Web.Interop.Catalog.CatalogExports.EncodeWorkspaceShareState.304094707", "stateJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareEncodeResult;
}

export function listHomeDemos(): BrowserHomeDemoCatalog {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListHomeDemos.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoCatalog;
}

export function listVocabulary(): BrowserVocabularyDocument {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListVocabulary.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserVocabularyDocument;
}

export async function observeRetainedWorkspaceSettlement(settlementId: string): Promise<BrowserRetainedWorkspaceSettlementResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ObserveRetainedWorkspaceSettlement.976702342"](settlementId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspaceSettlementResult;
}

export async function prepareRetainedWorkspaceDefinition(retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string): Promise<BrowserRetainedWorkspacePreparationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["PrepareRetainedWorkspaceDefinition.1579276339"](retainedDefinitionId, label, canonicalLocation, canonicalPacket);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspacePreparationResult;
}

export async function prepareRetainedWorkspaceDefinitionWithCredentials(retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string, packageSourceCredentialsJson: Readonly<Record<string, BrowserRetainedWorkspacePackageSourceCredential>>): Promise<BrowserRetainedWorkspacePreparationResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["PrepareRetainedWorkspaceDefinitionWithCredentials.1330709314"](retainedDefinitionId, label, canonicalLocation, canonicalPacket, $serializeJsonInput(packageSourceCredentialsJson, "DotnetInspect.Web.Interop.Catalog.CatalogExports.PrepareRetainedWorkspaceDefinitionWithCredentials.1330709314", "packageSourceCredentialsJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserRetainedWorkspacePreparationResult;
}

export function recordRetainedWorkspaceNavigationPosting(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string {
  return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["RecordRetainedWorkspaceNavigationPosting.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}

export function resolveHomeDemo(scenarioId: string): BrowserHomeDemoResolveResult {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ResolveHomeDemo.304094707"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoResolveResult;
}

export async function runHomeDemo(scenarioId: string): Promise<BrowserHomeDemoRunResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["RunHomeDemo.976702342"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoRunResult;
}

export function validateRetainedWorkspaceNavigationAuthority(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): boolean {
  return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ValidateRetainedWorkspaceNavigationAuthority.1044747233"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}

