import type {
  BrowserAnalysisInspectionDiagnostic,
  BrowserAnalysisInspectionShare,
  BrowserImplementationProfile,
  BrowserImplementationProfileAnalysisDiagnostic,
  BrowserImplementationProfileApiSurfaceFailure,
  BrowserImplementationProfileContent,
  BrowserImplementationProfileCoverage,
  BrowserImplementationProfileFailure,
  BrowserImplementationProfileMethod,
  BrowserImplementationProfileRelationship,
  BrowserImplementationProfiles,
  BrowserImplementationProfileSubject,
  BrowserImplementationProfileUnavailableBody,
} from "./facades/inspect-web-analysis.d.ts";
import type {
  OperationAuthorityPage,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

export interface PackageImplementationProfileRequest {
  readonly kind: "package";
  readonly workspaceGeneration: string;
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly assemblyName: string;
  readonly typeDefinitionId: string;
  readonly stableSelectors: ReadonlyArray<string>;
}

export interface PlatformImplementationProfileRequest {
  readonly kind: "platform";
  readonly workspaceGeneration: string;
  readonly targetFramework: string;
  readonly platformVersion: string;
  readonly pack: string;
  readonly assemblyFileName: string;
  readonly typeDefinitionId: string;
  readonly stableSelectors: ReadonlyArray<string>;
}

export type ImplementationProfileFamilyRequest =
  | PackageImplementationProfileRequest
  | PlatformImplementationProfileRequest;

interface ImplementationProfileFamilyMember {
  readonly typeDefinitionId: string;
  readonly stableSelector: string;
  readonly display: string;
  readonly bodyTokens: ReadonlyArray<number>;
  readonly selected: boolean;
}

export interface ImplementationProfileFamilySelection {
  readonly typeDefinitionId: string;
  readonly display: string;
  readonly members: ReadonlyArray<ImplementationProfileFamilyMember>;
  readonly isCurrent: () => boolean;
}

interface ImplementationProfileRawMetric {
  readonly label: string;
  readonly value: string;
}

interface ImplementationProfilePhysicalRow {
  readonly profile: BrowserImplementationProfile;
  readonly logicalMethod: BrowserImplementationProfileMethod;
  readonly evidenceMethod: BrowserImplementationProfileMethod;
  readonly generatedEvidence: boolean;
  readonly relativeInstructionPercent: number | null;
  readonly relativeInstructionText: string;
  readonly evidenceCues: ReadonlyArray<string>;
  readonly rawMetrics: ReadonlyArray<ImplementationProfileRawMetric>;
  readonly relationships: ReadonlyArray<BrowserImplementationProfileRelationship>;
}

interface ImplementationProfileOverloadRow {
  readonly member: ImplementationProfileFamilyMember;
  readonly physicalRows: ReadonlyArray<ImplementationProfilePhysicalRow>;
  readonly unavailableBodies: ReadonlyArray<BrowserImplementationProfileUnavailableBody>;
  readonly largestInstructionCount: number | null;
}

interface ImplementationProfileFamilyProjection {
  readonly display: string;
  readonly rows: ReadonlyArray<ImplementationProfileOverloadRow>;
  readonly physicalRowCount: number;
  readonly relativeBarsVisible: boolean;
  readonly maximumInstructionCount: number;
  readonly relationships: ReadonlyArray<BrowserImplementationProfileRelationship>;
  readonly subject: BrowserImplementationProfileSubject;
  readonly share: BrowserAnalysisInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserAnalysisInspectionDiagnostic>;
  readonly coverage: BrowserImplementationProfileCoverage;
  readonly analysisDiagnostics:
    ReadonlyArray<BrowserImplementationProfileAnalysisDiagnostic>;
  readonly apiSurfaceInspectionFailures:
    ReadonlyArray<BrowserImplementationProfileApiSurfaceFailure>;
  readonly generatedFrameworkTypes: ReadonlyArray<string>;
}

interface ImplementationProfileOwnerProjectionBase {
  readonly selection: ImplementationProfileFamilySelection;
  readonly inspection: BrowserImplementationProfiles;
}

interface ImplementationProfileProjectedOutcomeBase
  extends ImplementationProfileOwnerProjectionBase {
  readonly family: ImplementationProfileFamilyProjection;
}

export type ImplementationProfileProjectionOutcome =
  | ({
      readonly status: "available";
    } & ImplementationProfileProjectedOutcomeBase)
  | ({
      readonly status: "incomplete";
    } & ImplementationProfileProjectedOutcomeBase)
  | ({
      readonly status: "empty";
    } & ImplementationProfileProjectedOutcomeBase)
  | ({
      readonly status: "rejected";
      readonly failure: BrowserImplementationProfileFailure;
    } & ImplementationProfileOwnerProjectionBase)
  | ({
      readonly status: "failed";
      readonly failure: BrowserImplementationProfileFailure;
    } & ImplementationProfileOwnerProjectionBase)
  | ({
      readonly status: "unavailable";
      readonly failure: BrowserImplementationProfileFailure;
    } & ImplementationProfileOwnerProjectionBase);

type ImplementationProfileTerminalState =
  ImplementationProfileProjectionOutcome & {
    readonly request: ImplementationProfileFamilyRequest;
  };

export type ImplementationProfileState =
  | { readonly status: "idle" }
  | {
      readonly status: "loading";
      readonly request: ImplementationProfileFamilyRequest;
      readonly selection: ImplementationProfileFamilySelection;
    }
  | ImplementationProfileTerminalState
  | {
      readonly status: "producer-failed";
      readonly request: ImplementationProfileFamilyRequest;
      readonly selection: ImplementationProfileFamilySelection;
      readonly error: unknown;
      readonly message: string;
    };

export interface ImplementationProfileStateHost {
  implementationProfiles: ImplementationProfileState;
}

type ImplementationProfileCacheStatus =
  | "missing"
  | "in-flight"
  | "settled"
  | "producer-failed";

export interface ImplementationProfileResultCache {
  load(
    request: ImplementationProfileFamilyRequest,
    producer: (
      request: ImplementationProfileFamilyRequest,
    ) => Promise<BrowserImplementationProfiles>,
  ): Promise<BrowserImplementationProfiles>;
  retry(request: ImplementationProfileFamilyRequest): boolean;
  status(request: ImplementationProfileFamilyRequest):
    ImplementationProfileCacheStatus;
}

type CacheEntry =
  | {
      readonly kind: "in-flight";
      readonly promise: Promise<BrowserImplementationProfiles>;
    }
  | {
      readonly kind: "settled";
      readonly result: BrowserImplementationProfiles;
    }
  | {
      readonly kind: "producer-failed";
      readonly error: unknown;
    };

function producerError(error: unknown): Error {
  return error instanceof Error
    ? error
    : new Error(String(error), { cause: error });
}

export interface ImplementationProfileCoordinatorDependencies {
  readonly state: ImplementationProfileStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  readonly cache?: ImplementationProfileResultCache;
  query(
    request: ImplementationProfileFamilyRequest,
  ): Promise<BrowserImplementationProfiles>;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface ImplementationProfileCoordinator {
  activate(
    request: ImplementationProfileFamilyRequest,
    selection: ImplementationProfileFamilySelection,
  ): Promise<void>;
  retry(
    request: ImplementationProfileFamilyRequest,
    selection: ImplementationProfileFamilySelection,
  ): Promise<void>;
  deactivate(): boolean;
}

export interface ImplementationProfileBindingActions {
  readonly onRetry: () => void;
}

export function bindImplementationProfileState(
  root: ParentNode,
  actions: ImplementationProfileBindingActions,
): void {
  root.querySelector("[data-implementation-profile-retry]")
    ?.addEventListener("click", actions.onRetry);
}

interface ImplementationProfileOperationInput {
  readonly request: ImplementationProfileFamilyRequest;
  readonly selection: ImplementationProfileFamilySelection;
}

function familyMemberKey(
  typeDefinitionId: string,
  stableSelector: string,
): string {
  return JSON.stringify([typeDefinitionId, stableSelector]);
}

export function implementationProfileCacheKey(
  request: ImplementationProfileFamilyRequest,
): string {
  if (request.stableSelectors.length < 2)
    throw new Error(
      "An implementation-profile family requires at least two selectors.",
    );
  if (request.stableSelectors.some(selector => selector.trim().length === 0))
    throw new Error("Implementation-profile selectors cannot be blank.");
  const selectors = [...request.stableSelectors].sort();
  if (new Set(selectors).size !== selectors.length)
    throw new Error("Implementation-profile selectors must be distinct.");
  return request.kind === "package"
    ? JSON.stringify([
        "implementation-profiles",
        request.workspaceGeneration,
        "package",
        request.packageId,
        request.version,
        request.targetFramework,
        request.assemblyName,
        request.typeDefinitionId,
        selectors,
      ])
    : JSON.stringify([
        "implementation-profiles",
        request.workspaceGeneration,
        "platform",
        request.targetFramework,
        request.platformVersion,
        request.pack,
        request.assemblyFileName,
        request.typeDefinitionId,
        selectors,
      ]);
}

export function createImplementationProfileResultCache():
ImplementationProfileResultCache {
  const entries = new Map<string, CacheEntry>();

  return {
    load(request, producer) {
      const key = implementationProfileCacheKey(request);
      const existing = entries.get(key);
      if (existing?.kind === "in-flight") return existing.promise;
      if (existing?.kind === "settled")
        return Promise.resolve(existing.result);
      if (existing?.kind === "producer-failed")
        return Promise.reject(producerError(existing.error));

      let produced: Promise<BrowserImplementationProfiles>;
      try {
        produced = producer(request);
      } catch (error: unknown) {
        entries.set(key, { kind: "producer-failed", error });
        return Promise.reject(producerError(error));
      }

      let entry: Extract<CacheEntry, { readonly kind: "in-flight" }>;
      const promise = produced.then(
        result => {
          validateInspection(result);
          validateInspectionForRequest(result, request);
          if (entries.get(key) === entry)
            entries.set(key, { kind: "settled", result });
          return result;
        },
        (error: unknown) => {
          if (entries.get(key) === entry)
            entries.set(key, { kind: "producer-failed", error });
          throw error;
        },
      ).catch((error: unknown) => {
        if (entries.get(key) === entry)
          entries.set(key, { kind: "producer-failed", error });
        throw error;
      });
      entry = { kind: "in-flight", promise };
      entries.set(key, entry);
      return promise;
    },

    retry(request) {
      const key = implementationProfileCacheKey(request);
      if (entries.get(key)?.kind !== "producer-failed") return false;
      entries.delete(key);
      return true;
    },

    status(request) {
      return entries.get(implementationProfileCacheKey(request))?.kind
        ?? "missing";
    },
  };
}

function validateInspection(
  inspection: BrowserImplementationProfiles,
): void {
  if (inspection.schemaVersion !== 2)
    throw new Error("Unsupported implementation-profile schema version.");
  if (inspection.compileLibrary === null
    || typeof inspection.compileLibrary !== "object") {
    throw new Error("Implementation-profile result has no Library availability.");
  }
  switch (inspection.outcome) {
    case "available":
      if (inspection.subject === null
        || inspection.content === null
        || inspection.failure !== null
        || inspection.share === null) {
        throw new Error("Available implementation profiles are incomplete.");
      }
      return;
    case "rejected":
    case "failed":
      if (inspection.subject === null
        || inspection.content !== null
        || inspection.failure === null
        || inspection.share === null) {
        throw new Error(
          `Implementation-profile ${inspection.outcome} result is incomplete.`,
        );
      }
      return;
    case "unavailable":
      if (inspection.subject !== null
        || inspection.content !== null
        || inspection.failure === null
        || inspection.share !== null) {
        throw new Error("Unavailable implementation profiles are inconsistent.");
      }
      return;
    default:
      throw new Error(
        `Unknown implementation-profile outcome: ${inspection.outcome}.`,
      );
  }
}

function validateInspectionForRequest(
  inspection: BrowserImplementationProfiles,
  request: ImplementationProfileFamilyRequest,
): void {
  if (inspection.outcome !== "available") return;
  const content = inspection.content;
  if (content === null)
    throw new Error("Available implementation profiles are incomplete.");

  const expectedMembers = new Set(request.stableSelectors.map(selector =>
    familyMemberKey(request.typeDefinitionId, selector)));
  const issuedMembers = new Set<string>();
  for (const member of content.members) {
    const key = familyMemberKey(
      member.typeDefinitionId,
      member.stableSelector,
    );
    if (issuedMembers.has(key)) {
      throw new Error(
        "The implementation-profile result has a duplicate family member.",
      );
    }
    issuedMembers.add(key);
  }
  if (issuedMembers.size !== expectedMembers.size
    || [...expectedMembers].some(key => !issuedMembers.has(key))) {
    throw new Error(
      "The implementation-profile result does not match the requested family.",
    );
  }

  const methods = methodMap(content);
  const requireMethod = (key: string) => {
    if (!methods.has(key))
      throw new Error(`Unknown implementation-profile method '${key}'.`);
  };
  for (const profile of content.profiles) {
    requireMethod(profile.methodKey);
    requireMethod(profile.evidenceMethodKey);
    if (profile.publicMembers.length === 0) {
      throw new Error(
        "The implementation-profile result contains an unattributed profile.",
      );
    }
    for (const member of profile.publicMembers) {
      if (!expectedMembers.has(
        familyMemberKey(member.typeDefinitionId, member.stableSelector))) {
        throw new Error(
          "The implementation-profile result contains a profile outside the requested family.",
        );
      }
    }
  }
  for (const relationship of content.overloadRelationships) {
    requireMethod(relationship.callerKey);
    requireMethod(relationship.calleeKey);
    requireMethod(relationship.evidenceMethodKey);
  }
  for (const key of [
    ...content.coverage.declaredMethodKeys,
    ...content.coverage.managedMethodBodyKeys,
    ...content.coverage.profiledEvidenceBodyKeys,
  ]) {
    requireMethod(key);
  }
  for (const body of content.coverage.unavailableBodies) {
    if (body.evidenceMethodKey !== null)
      requireMethod(body.evidenceMethodKey);
  }
}

function methodMap(
  content: BrowserImplementationProfileContent,
): ReadonlyMap<string, BrowserImplementationProfileMethod> {
  const methods = new Map<string, BrowserImplementationProfileMethod>();
  for (const method of content.methods) {
    if (methods.has(method.key))
      throw new Error(`Duplicate implementation-profile method '${method.key}'.`);
    methods.set(method.key, method);
  }
  return methods;
}

function exceptionRegionCount(
  profile: BrowserImplementationProfile,
): number {
  return profile.catchCount
    + profile.filterCount
    + profile.finallyCount
    + profile.faultCount;
}

function evidenceCues(
  profile: BrowserImplementationProfile,
  generatedEvidence: boolean,
): string[] {
  const cues: string[] = [];
  if (generatedEvidence) cues.push("Generated physical body");
  else cues.push("Physical body");
  if (profile.branchCount > 0)
    cues.push(`Branches: ${profile.branchCount}`);
  if (profile.loopCount > 0)
    cues.push(`Loops: ${profile.loopCount}`);
  const exceptionRegions = exceptionRegionCount(profile);
  if (exceptionRegions > 0)
    cues.push(`Exception regions: ${exceptionRegions}`);
  if (profile.async) cues.push("Async evidence");
  if (profile.unsafe) cues.push("Unsafe evidence");
  if (profile.reflectionCallCount > 0)
    cues.push(`Reflection calls: ${profile.reflectionCallCount}`);
  if (!profile.isComplete) cues.push("Incomplete measurements");
  return cues;
}

function rawMetrics(
  profile: BrowserImplementationProfile,
): ImplementationProfileRawMetric[] {
  return [
    { label: "IL bytes", value: String(profile.ilBytes) },
    { label: "Instruction count", value: String(profile.instructionCount) },
    {
      label: "Distinct opcode count",
      value: String(profile.distinctOpcodeCount),
    },
    { label: "Basic blocks", value: String(profile.basicBlockCount) },
    { label: "Branches", value: String(profile.branchCount) },
    {
      label: "Conditional branches",
      value: String(profile.conditionalBranchCount),
    },
    { label: "Switches", value: String(profile.switchCount) },
    { label: "Switch targets", value: String(profile.switchTargetCount) },
    {
      label: "Normal-flow cyclomatic complexity",
      value: String(profile.normalFlowCyclomaticComplexity),
    },
    { label: "Loops", value: String(profile.loopCount) },
    { label: "Catch regions", value: String(profile.catchCount) },
    { label: "Filter regions", value: String(profile.filterCount) },
    { label: "Finally regions", value: String(profile.finallyCount) },
    { label: "Fault regions", value: String(profile.faultCount) },
    { label: "Locals", value: String(profile.localCount) },
    { label: "Direct calls", value: String(profile.directCallCount) },
    {
      label: "Distinct callees",
      value: String(profile.distinctCalleeCount),
    },
    { label: "Allocations", value: String(profile.allocationCount) },
    { label: "Throws", value: String(profile.throwCount) },
    { label: "Async evidence", value: profile.async ? "Yes" : "No" },
    { label: "Unsafe evidence", value: profile.unsafe ? "Yes" : "No" },
    {
      label: "Reflection calls",
      value: String(profile.reflectionCallCount),
    },
    {
      label: "Incoming sibling-overload callers",
      value: String(profile.incomingOverloadCallerCount),
    },
    {
      label: "Outgoing sibling-overload targets",
      value: String(profile.outgoingOverloadTargetCount),
    },
    {
      label: "Measurements complete",
      value: profile.isComplete ? "Yes" : "No",
    },
    {
      label: "Incomplete reasons",
      value: profile.incompleteReasons.length === 0
        ? "None"
        : profile.incompleteReasons.join("; "),
    },
  ];
}

function familyIsIncomplete(
  inspection: BrowserImplementationProfiles,
  profiles: ReadonlyArray<BrowserImplementationProfile>,
): boolean {
  const content = inspection.content;
  if (content === null) return false;
  return profiles.some(profile => !profile.isComplete)
    || !content.coverage.wasRequested
    || content.coverage.unavailableBodies.length > 0
    || content.coverage.diagnostics.length > 0
    || content.analysisDiagnostics.length > 0
    || content.apiSurfaceInspectionFailures.length > 0
    || inspection.diagnostics.length > 0;
}

export function projectImplementationProfileFamily(
  inspection: BrowserImplementationProfiles,
  selection: ImplementationProfileFamilySelection,
): ImplementationProfileProjectionOutcome {
  validateInspection(inspection);
  const base: ImplementationProfileOwnerProjectionBase = {
    selection,
    inspection,
  };
  if (inspection.outcome !== "available") {
    const failure = inspection.failure;
    if (failure === null)
      throw new Error("Implementation-profile failure details are unavailable.");
    if (inspection.outcome === "rejected")
      return { ...base, status: "rejected", failure };
    if (inspection.outcome === "failed")
      return { ...base, status: "failed", failure };
    return { ...base, status: "unavailable", failure };
  }

  const content = inspection.content;
  const subject = inspection.subject;
  const share = inspection.share;
  if (content === null || subject === null || share === null)
    throw new Error("Available implementation profiles are incomplete.");
  const projection = projectAvailableFamily(
    content,
    subject,
    share,
    inspection.diagnostics,
    selection,
  );
  const projectedBase = { ...base, family: projection };
  if (projection.physicalRowCount === 0) {
    return familyIsIncomplete(inspection, [])
      ? { ...projectedBase, status: "incomplete" }
      : { ...projectedBase, status: "empty" };
  }
  const profiles = projection.rows.flatMap(row =>
    row.physicalRows.map(physical => physical.profile));
  return familyIsIncomplete(inspection, profiles)
    ? { ...projectedBase, status: "incomplete" }
    : { ...projectedBase, status: "available" };
}

function projectForRequest(
  inspection: BrowserImplementationProfiles,
  input: ImplementationProfileOperationInput,
): ImplementationProfileTerminalState {
  const projected = projectImplementationProfileFamily(
    inspection,
    input.selection,
  );
  return {
    ...projected,
    request: input.request,
  };
}

function projectAvailableFamily(
  content: BrowserImplementationProfileContent,
  subject: BrowserImplementationProfileSubject,
  share: BrowserAnalysisInspectionShare,
  diagnostics: ReadonlyArray<BrowserAnalysisInspectionDiagnostic>,
  selection: ImplementationProfileFamilySelection,
): ImplementationProfileFamilyProjection {
  const issuedMembers = new Map<
    string,
    BrowserImplementationProfileContent["members"][number]
  >();
  for (const member of content.members) {
    const key = familyMemberKey(
      member.typeDefinitionId,
      member.stableSelector,
    );
    if (issuedMembers.has(key))
      throw new Error(
        `Duplicate issued implementation-profile selector '${member.stableSelector}'.`,
      );
    issuedMembers.set(key, member);
  }
  const seenMembers = new Set<string>();
  const familyMembers = selection.members.map(member => {
    if (member.typeDefinitionId !== selection.typeDefinitionId) {
      throw new Error(
        "Implementation-profile family members must share one Type definition ID.",
      );
    }
    const key = familyMemberKey(
      member.typeDefinitionId,
      member.stableSelector,
    );
    if (seenMembers.has(key)) {
      throw new Error(
        `Duplicate implementation-profile selector '${member.stableSelector}'.`,
      );
    }
    seenMembers.add(key);
    const issued = issuedMembers.get(key);
    if (issued === undefined)
      throw new Error(
        `Implementation-profile result omitted selector '${member.stableSelector}'.`,
      );
    return {
      ...member,
      bodyTokens: issued.bodyTokens,
    };
  });
  if (issuedMembers.size !== familyMembers.length)
    throw new Error(
      "Implementation-profile result does not match the requested family.",
    );
  for (const profile of content.profiles) {
    if (profile.publicMembers.length === 0) {
      throw new Error(
        "The implementation-profile result contains an unattributed profile.",
      );
    }
    for (const publicMember of profile.publicMembers) {
      const key = familyMemberKey(
        publicMember.typeDefinitionId,
        publicMember.stableSelector,
      );
      if (!seenMembers.has(key)) {
        throw new Error(
          "The implementation-profile result contains a profile outside the requested family.",
        );
      }
    }
  }

  const methods = methodMap(content);
  const attributed = familyMembers.map((member, rosterIndex) => {
    const physical = content.profiles.flatMap((profile, profileIndex) =>
      profile.publicMembers.some(publicMember =>
        publicMember.typeDefinitionId === member.typeDefinitionId
        && publicMember.stableSelector === member.stableSelector)
        ? [{ profile, profileIndex }]
        : []);
    physical.sort((left, right) =>
      right.profile.instructionCount - left.profile.instructionCount
      || left.profileIndex - right.profileIndex);
    const bodyTokens = new Set(member.bodyTokens);
    const unavailableBodies = content.coverage.unavailableBodies.filter(body =>
      bodyTokens.has(body.methodToken));
    return { member, rosterIndex, physical, unavailableBodies };
  });
  const allProfiles = attributed.flatMap(item =>
    item.physical.map(physical => physical.profile));
  const physicalRowCount = allProfiles.length;
  const maximumInstructionCount = allProfiles.reduce(
    (maximum, profile) => Math.max(maximum, profile.instructionCount),
    0,
  );
  const uniformlyTiny = allProfiles.every(profile =>
    profile.instructionCount <= 8
    && profile.branchCount === 0
    && profile.loopCount === 0
    && exceptionRegionCount(profile) === 0
    && !profile.unsafe
    && profile.reflectionCallCount === 0);
  const relativeBarsVisible = physicalRowCount >= 2
    && !uniformlyTiny
    && maximumInstructionCount > 0;
  const relationships = content.overloadRelationships;

  const rows = attributed.map(item => {
    const physicalRows = item.physical.map(({ profile }) => {
      const logicalMethod = methods.get(profile.methodKey);
      const evidenceMethod = methods.get(profile.evidenceMethodKey);
      if (logicalMethod === undefined)
        throw new Error(
          `Logical method '${profile.methodKey}' is unavailable.`,
        );
      if (evidenceMethod === undefined)
        throw new Error(
          `Evidence method '${profile.evidenceMethodKey}' is unavailable.`,
        );
      const generatedEvidence =
        profile.methodKey !== profile.evidenceMethodKey;
      const relativeInstructionPercent = relativeBarsVisible
        ? Math.round(
            (profile.instructionCount / maximumInstructionCount) * 100,
          )
        : null;
      return {
        profile,
        logicalMethod,
        evidenceMethod,
        generatedEvidence,
        relativeInstructionPercent,
        relativeInstructionText: relativeInstructionPercent === null
          ? `${profile.instructionCount} instructions`
          : `${profile.instructionCount} instructions; ${relativeInstructionPercent}% of the largest physical body in this overload family`,
        evidenceCues: evidenceCues(profile, generatedEvidence),
        rawMetrics: rawMetrics(profile),
        relationships: relationships.filter(relationship =>
          relationship.evidenceMethodKey === profile.evidenceMethodKey),
      };
    });
    return {
      member: item.member,
      physicalRows,
      unavailableBodies: item.unavailableBodies,
      largestInstructionCount: physicalRows[0]?.profile.instructionCount
        ?? null,
      rosterIndex: item.rosterIndex,
    };
  });
  rows.sort((left, right) => {
    if (left.largestInstructionCount === null
      && right.largestInstructionCount !== null) return 1;
    if (left.largestInstructionCount !== null
      && right.largestInstructionCount === null) return -1;
    return (right.largestInstructionCount ?? 0)
      - (left.largestInstructionCount ?? 0)
      || left.rosterIndex - right.rosterIndex;
  });

  return {
    display: selection.display,
    rows: rows.map(({ rosterIndex: _rosterIndex, ...row }) => row),
    physicalRowCount,
    relativeBarsVisible,
    maximumInstructionCount,
    relationships,
    subject,
    share,
    diagnostics,
    coverage: content.coverage,
    analysisDiagnostics: content.analysisDiagnostics,
    apiSurfaceInspectionFailures: content.apiSurfaceInspectionFailures,
    generatedFrameworkTypes: content.generatedFrameworkTypes,
  };
}

export function createImplementationProfileCoordinator(
  dependencies: ImplementationProfileCoordinatorDependencies,
): ImplementationProfileCoordinator {
  type FeatureEvent =
    OperationFeatureEvent<ImplementationProfileTerminalState, unknown, never>;
  type Session = OperationSession<
    ImplementationProfileOperationInput,
    ImplementationProfileTerminalState,
    unknown,
    never,
    never
  >;

  const cache = dependencies.cache ?? createImplementationProfileResultCache();
  const inputs = new Map<OperationId, ImplementationProfileOperationInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Implementation-profile operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const input = inputFor(event.operation.id);
        if (!input.selection.isCurrent()) break;
        dependencies.state.implementationProfiles = {
          status: "loading",
          request: input.request,
          selection: input.selection,
        };
        dependencies.render();
        break;
      }
      case "terminal": {
        const input = inputFor(event.operationId);
        if (!input.selection.isCurrent()) break;
        dependencies.state.implementationProfiles =
          event.outcome.kind === "succeeded"
            ? event.outcome.value
            : {
                status: "producer-failed",
                request: input.request,
                selection: input.selection,
                error: event.outcome.error,
                message: dependencies.describeError(event.outcome.error),
              };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
      case "progress":
        break;
    }
    return undefined;
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic =>
        dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    ImplementationProfileOperationInput,
    ImplementationProfileTerminalState,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      let quiesced = false;
      const quiesce = (): undefined => {
        if (quiesced) return undefined;
        quiesced = true;
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const finish = (inspection: BrowserImplementationProfiles): undefined => {
        if (!input.selection.isCurrent()) {
          sink.reportTerminal({ kind: "canceled", reason: "superseded" });
          return quiesce();
        }
        try {
          sink.reportTerminal({
            kind: "succeeded",
            value: projectForRequest(inspection, input),
          });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      const fail = (error: unknown): undefined => {
        if (!input.selection.isCurrent()) {
          sink.reportTerminal({ kind: "canceled", reason: "superseded" });
          return quiesce();
        }
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: () => undefined,
          activate: () => {
            let result: Promise<BrowserImplementationProfiles>;
            try {
              result = cache.load(
                input.request,
                request => dependencies.query(request),
              );
            } catch (error: unknown) {
              return fail(error);
            }
            void result.then(finish, fail);
            return undefined;
          },
          abandon: () => {
            inputs.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const start = (
    request: ImplementationProfileFamilyRequest,
    selection: ImplementationProfileFamilySelection,
  ): Promise<void> => {
    const input = { request, selection };
    const result = session.start(input, adapter);
    if (result.kind === "rejected") {
      dependencies.state.implementationProfiles = {
        status: "producer-failed",
        request,
        selection,
        error: result.reason,
        message:
          `Implementation profiles could not start: ${result.reason.kind}.`,
      };
      dependencies.render();
      return Promise.resolve();
    }
    return result.handle.quiesced;
  };

  return {
    activate: start,
    retry(request, selection) {
      cache.retry(request);
      return start(request, selection);
    },
    deactivate() {
      const cancellation = session.cancelCurrent("superseded");
      if (cancellation.kind === "rejected") {
        dependencies.reportOperationDiagnostic({
          kind: "producer-contract",
          operationId: null,
          error: new Error(
            "Implementation-profile deactivation was attempted during feature publication.",
          ),
        });
        return false;
      }
      const changed = dependencies.state.implementationProfiles.status
        !== "idle";
      dependencies.state.implementationProfiles = { status: "idle" };
      if (changed) dependencies.render();
      return cancellation.kind === "applied" || changed;
    },
  };
}

function renderDiagnostics(
  diagnostics: ReadonlyArray<BrowserAnalysisInspectionDiagnostic>,
  escapeHtml: (value: unknown) => string,
): string {
  if (diagnostics.length === 0) return "";
  return `<ul class="implementation-profile-diagnostics">${
    diagnostics.map(diagnostic =>
      `<li><strong>${escapeHtml(diagnostic.code)}</strong>: ${escapeHtml(diagnostic.summary)}${
        diagnostic.correspondence === null
          ? ""
          : ` <span>${escapeHtml(diagnostic.correspondence)}</span>`
      }</li>`).join("")
  }</ul>`;
}

function renderAnalysisDiagnostics(
  title: string,
  diagnostics: ReadonlyArray<BrowserImplementationProfileAnalysisDiagnostic>,
  escapeHtml: (value: unknown) => string,
): string {
  if (diagnostics.length === 0) return "";
  return `<section><h3>${escapeHtml(title)}</h3><ul>${
    diagnostics.map(diagnostic =>
      `<li>Method token ${diagnostic.methodToken}: ${escapeHtml(diagnostic.method)} - ${escapeHtml(diagnostic.message)}</li>`)
      .join("")
  }</ul></section>`;
}

function renderApiSurfaceFailures(
  failures: ReadonlyArray<BrowserImplementationProfileApiSurfaceFailure>,
  escapeHtml: (value: unknown) => string,
): string {
  if (failures.length === 0) return "";
  return `<section><h3>API-surface inspection failures</h3><ul>${
    failures.map(failure =>
      `<li>${escapeHtml(failure.operation)} for token ${failure.subjectToken}: ${escapeHtml(failure.kind)} - ${escapeHtml(failure.detail)}</li>`)
      .join("")
  }</ul></section>`;
}

function renderFailure(
  title: string,
  state: Extract<
    ImplementationProfileProjectionOutcome,
    { readonly status: "rejected" | "failed" | "unavailable" }
  >,
  escapeHtml: (value: unknown) => string,
): string {
  return `<section class="implementation-profile-state implementation-profile-${state.status}" aria-labelledby="implementation-profile-state-title">
    <h2 id="implementation-profile-state-title" tabindex="-1">${escapeHtml(title)}</h2>
    <p><strong>${escapeHtml(state.failure.kind)}</strong>: ${escapeHtml(state.failure.detail)}</p>
    ${state.failure.metadataRootReason === null
      ? ""
      : `<p>Metadata root: ${escapeHtml(state.failure.metadataRootReason)}</p>`}
    ${state.inspection.compileLibrary.message === null
      ? ""
      : `<p>${escapeHtml(state.inspection.compileLibrary.message)}</p>`}
    ${renderDiagnostics(state.inspection.diagnostics, escapeHtml)}
  </section>`;
}

function renderPhysicalRow(
  row: ImplementationProfilePhysicalRow,
  index: number,
  escapeHtml: (value: unknown) => string,
): string {
  const bar = row.relativeInstructionPercent === null
    ? ""
    : `<div class="implementation-profile-bar" role="img" aria-label="${escapeHtml(row.relativeInstructionText)}">
        <span class="implementation-profile-bar-fill" style="width: ${row.relativeInstructionPercent}%"></span>
        <span>${escapeHtml(row.relativeInstructionText)}</span>
      </div>`;
  const methodIdentity = row.generatedEvidence
    ? `<p><span>Logical overload:</span> <code>${escapeHtml(row.logicalMethod.display)}</code><br><span>Physical evidence:</span> <code>${escapeHtml(row.evidenceMethod.display)}</code></p>`
    : `<p><span>Physical evidence:</span> <code>${escapeHtml(row.evidenceMethod.display)}</code></p>`;
  const cues = `<ul class="implementation-profile-cues" aria-label="Physical evidence cues">${
    row.evidenceCues.map(cue => `<li>${escapeHtml(cue)}</li>`).join("")
  }</ul>`;
  const metrics = row.rawMetrics.map(metric =>
    `<div><dt>${escapeHtml(metric.label)}</dt><dd>${escapeHtml(metric.value)}</dd></div>`)
    .join("");
  const relationships = row.relationships.length === 0
    ? "<p>No sibling-overload relationships were issued for this physical body.</p>"
    : `<ol>${row.relationships.map(relationship =>
        `<li><code>${escapeHtml(relationship.callerKey)}</code> to <code>${escapeHtml(relationship.calleeKey)}</code>; ${escapeHtml(relationship.kind)} at IL offset ${relationship.ilOffset}</li>`)
      .join("")}</ol>`;
  return `<article class="implementation-profile-physical-row" aria-labelledby="implementation-profile-physical-${index}">
    <h4 id="implementation-profile-physical-${index}">${escapeHtml(row.generatedEvidence ? "Generated physical body" : "Physical body")}</h4>
    ${methodIdentity}
    <p class="implementation-profile-instruction-cue">${escapeHtml(row.relativeInstructionText)}</p>
    ${bar}
    ${cues}
    <details>
      <summary>Raw implementation metrics</summary>
      <dl>${metrics}</dl>
    </details>
    <details>
      <summary>Exact sibling-overload relationships (${row.relationships.length})</summary>
      ${relationships}
    </details>
  </article>`;
}

function renderFamily(
  state: Extract<
    ImplementationProfileProjectionOutcome,
    { readonly status: "available" | "incomplete" | "empty" }
  >,
  escapeHtml: (value: unknown) => string,
): string {
  const family = state.family;
  let physicalIndex = 0;
  const rows = family.rows.map((row, rowIndex) => {
    const physical = row.physicalRows.map(item =>
      renderPhysicalRow(item, physicalIndex++, escapeHtml)).join("");
    const unavailable = row.unavailableBodies.map(body =>
      `<li>Method token ${body.methodToken}: ${escapeHtml(body.reason)}${
        body.diagnostic === null
          ? ""
          : ` - ${escapeHtml(body.diagnostic.message)}`
      }</li>`).join("");
    const absence = physical.length > 0
      ? ""
      : `<p class="implementation-profile-no-body">${
          unavailable.length > 0
            ? "Physical evidence is unavailable."
            : "No physical profile was attributed to this overload."
        }</p>${unavailable.length > 0 ? `<ul>${unavailable}</ul>` : ""}`;
    return `<section class="implementation-profile-overload${row.member.selected ? " is-selected" : ""}" aria-labelledby="implementation-profile-overload-${rowIndex}">
      <h3 id="implementation-profile-overload-${rowIndex}">${escapeHtml(row.member.display)}${row.member.selected ? " - selected overload" : ""}</h3>
      ${physical}${absence}
    </section>`;
  }).join("");
  const emptyNotice = state.status === "empty"
    ? `<section class="implementation-profile-empty-note" role="note">
        <h2>No physical implementation profiles</h2>
        <p>This complete inspection attributed no physical profile to the overload family. Each overload remains listed below.</p>
      </section>`
    : "";
  const qualification = state.status === "incomplete"
    ? `<section class="implementation-profile-incomplete" role="note">
        <h2>Implementation profiles are incomplete</h2>
        <p>Available physical evidence is shown, but coverage or diagnostics qualify this family.</p>
      </section>`
    : "";
  const barNote = family.relativeBarsVisible
    ? `<p>Instruction bars are relative only to the largest physical body in this overload family. Raw counts remain authoritative.</p>`
    : `<p>Relative instruction bars are omitted because this family has fewer than two physical rows or only uniformly tiny bodies without structural evidence. Raw counts remain available.</p>`;
  const diagnosticDetails = state.status === "incomplete"
    ? `<details class="implementation-profile-qualification">
        <summary>Coverage and diagnostic details</summary>
      <p>Family body analysis requested: ${family.coverage.wasRequested ? "Yes" : "No"}</p>
        ${renderAnalysisDiagnostics(
          "Coverage diagnostics",
          family.coverage.diagnostics,
          escapeHtml,
        )}
        ${renderAnalysisDiagnostics(
          "Analysis diagnostics",
          family.analysisDiagnostics,
          escapeHtml,
        )}
        ${renderApiSurfaceFailures(
          family.apiSurfaceInspectionFailures,
          escapeHtml,
        )}
        ${renderDiagnostics(family.diagnostics, escapeHtml)}
      </details>`
    : renderDiagnostics(family.diagnostics, escapeHtml);
  return `<section class="implementation-profile-state implementation-profile-${state.status}" aria-labelledby="implementation-profile-state-title">
    <h2 id="implementation-profile-state-title" tabindex="-1">${escapeHtml(family.display)}</h2>
    ${emptyNotice}
    ${qualification}
    ${barNote}
    ${rows}
    ${diagnosticDetails}
  </section>`;
}

export function renderImplementationProfileState(
  state: ImplementationProfileState | ImplementationProfileProjectionOutcome,
  escapeHtml: (value: unknown) => string,
): string {
  switch (state.status) {
    case "idle":
      return "";
    case "loading":
      return `<section class="implementation-profile-state implementation-profile-loading" role="status" aria-live="polite"><h2 id="implementation-profile-state-title" tabindex="-1">Loading implementation profiles</h2><p>Inspecting the overload family ${escapeHtml(state.selection.display)}.</p></section>`;
    case "available":
    case "incomplete":
    case "empty":
      return renderFamily(state, escapeHtml);
    case "rejected":
      return renderFailure("Implementation profile request rejected", state, escapeHtml);
    case "failed":
      return renderFailure("Implementation profile inspection failed", state, escapeHtml);
    case "unavailable":
      return renderFailure("Implementation profiles unavailable", state, escapeHtml);
    case "producer-failed":
      return `<section class="implementation-profile-state implementation-profile-producer-failed" aria-labelledby="implementation-profile-state-title">
        <h2 id="implementation-profile-state-title" tabindex="-1">Implementation profile producer failed</h2>
        <p>${escapeHtml(state.message)}</p>
        <button id="implementation-profile-retry" type="button" data-implementation-profile-retry>Retry implementation profiles</button>
      </section>`;
  }
  throw new Error("Unknown implementation-profile state.");
}
