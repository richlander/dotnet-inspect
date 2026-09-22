import type {
  BrowserLibraryApiDiffChange,
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffMember,
  BrowserLibraryApiDiffMemberIdentity,
  BrowserLibraryApiDiffResult,
  BrowserLibraryApiDiffSucceeded,
  BrowserLibraryApiDiffType,
} from "./facades/inspect-web-metadata.d.ts";
import {
  renderCompareEmpty,
  renderCompareFrame,
  renderCompareLoading,
  renderCompareRetry,
} from "./compare-surface.ts";
import type {
  CompareMode,
  EffectiveDiffTarget,
} from "./package-comparison-targets.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

export interface LibraryApiDiffSelection {
  readonly packageModel: object;
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
  readonly target: EffectiveDiffTarget;
}

interface LibraryApiDiffOperationInput {
  readonly packageModel: object;
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
}

export type LibraryApiDiffState =
  | { readonly status: "idle" }
  | {
      readonly status: "target-loading";
      readonly selection: LibraryApiDiffSelection;
      readonly message: string;
    }
  | {
      readonly status: "target-unavailable";
      readonly selection: LibraryApiDiffSelection;
      readonly message: string;
    }
  | {
      readonly status: "loading";
      readonly input: LibraryApiDiffOperationInput;
    }
  | {
      readonly status: "ready";
      readonly input: LibraryApiDiffOperationInput;
      readonly result: BrowserLibraryApiDiffResult;
    }
  | {
      readonly status: "failed";
      readonly input: LibraryApiDiffOperationInput;
      readonly error: string;
    };

export interface LibraryApiDiffStateHost {
  libraryApiDiff: LibraryApiDiffState;
}

export interface LibraryApiDiffDependencies {
  readonly state: LibraryApiDiffStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  query(
    operationId: OperationId,
    requestJson: string,
  ): Promise<unknown>;
  cancel(
    operationId: OperationId,
    reason: OperationCancelReason,
  ): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface LibraryApiDiffCoordinator {
  reconcile(selection: LibraryApiDiffSelection | null): void;
  retry(selection: LibraryApiDiffSelection): void;
  cancelCurrentRequest(): boolean;
}

function sameSelection(
  left: LibraryApiDiffSelection,
  right: LibraryApiDiffSelection,
): boolean {
  return left.packageModel === right.packageModel
    && left.packageId === right.packageId
    && left.currentVersion === right.currentVersion
    && left.targetFramework === right.targetFramework
    && left.compileAssetId === right.compileAssetId
    && left.target.kind === right.target.kind
    && (left.target.kind !== "available"
      || (right.target.kind === "available"
        && left.target.version === right.target.version))
    && (left.target.kind === "available"
      || (right.target.kind !== "available"
        && left.target.message === right.target.message));
}

function sameInput(
  left: LibraryApiDiffOperationInput,
  right: LibraryApiDiffOperationInput,
): boolean {
  return left.packageModel === right.packageModel
    && left.packageId === right.packageId
    && left.currentVersion === right.currentVersion
    && left.targetVersion === right.targetVersion
    && left.targetFramework === right.targetFramework
    && left.compileAssetId === right.compileAssetId;
}

function stateMatchesSelection(
  state: LibraryApiDiffState,
  selection: LibraryApiDiffSelection,
): boolean {
  if (state.status === "target-loading"
    || state.status === "target-unavailable") {
    return sameSelection(state.selection, selection);
  }
  if (selection.target.kind !== "available"
    || state.status === "idle") {
    return false;
  }
  return sameInput(state.input, {
    packageModel: selection.packageModel,
    packageId: selection.packageId,
    currentVersion: selection.currentVersion,
    targetVersion: selection.target.version,
    targetFramework: selection.targetFramework,
    compileAssetId: selection.compileAssetId,
  });
}

function requestJson(input: LibraryApiDiffOperationInput): string {
  return JSON.stringify({
    schemaVersion: 1,
    packageId: input.packageId,
    currentVersion: input.currentVersion,
    targetVersion: input.targetVersion,
    targetFramework: input.targetFramework,
    compileAssetId: input.compileAssetId,
  });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function requireRecord(
  value: unknown,
  description: string,
): Record<string, unknown> {
  if (!isRecord(value))
    throw new Error(`${description} must be an object.`);
  return value;
}

function requireString(value: unknown, description: string): void {
  if (typeof value !== "string")
    throw new Error(`${description} must be a string.`);
}

function requireInteger(value: unknown, description: string): void {
  if (!Number.isSafeInteger(value) || Number(value) < 0)
    throw new Error(`${description} must be a non-negative integer.`);
}

function requireNullableString(value: unknown, description: string): void {
  if (value !== null) requireString(value, description);
}

function requireNullableInteger(value: unknown, description: string): void {
  if (value !== null) requireInteger(value, description);
}

function requireNullableEnum(
  value: unknown,
  description: string,
  values: readonly string[],
): void {
  if (value !== null) requireEnum(value, description, values);
}

function requireEnum(
  value: unknown,
  description: string,
  values: readonly string[],
): void {
  if (typeof value !== "string" || !values.includes(value))
    throw new Error(`${description} is unsupported.`);
}

function requireNull(
  record: Record<string, unknown>,
  ...properties: string[]
): void {
  for (const property of properties) {
    if (record[property] !== null)
      throw new Error(`Library API Diff ${property} must be null.`);
  }
}

function validateAssembly(value: unknown, description: string): void {
  const assembly = requireRecord(value, description);
  requireString(assembly.name, `${description}.name`);
  requireNullableString(assembly.version, `${description}.version`);
  requireNullableString(assembly.culture, `${description}.culture`);
  requireNullableString(
    assembly.publicKeyToken,
    `${description}.publicKeyToken`,
  );
}

function validateEndpointIssue(value: unknown, description: string): void {
  const issue = requireRecord(value, description);
  requireEnum(issue.kind, `${description}.kind`, [
    "Truncated",
    "Rejected",
    "Failed",
    "InspectionFailures",
    "DegradedSignatures",
    "UnexpectedAssemblyPopulation",
  ]);
  requireNullableString(issue.detail, `${description}.detail`);
  requireNullableInteger(issue.count, `${description}.count`);
  requireNullableEnum(
    issue.openFailureKind,
    `${description}.openFailureKind`,
    [
      "Unreadable",
      "InvalidImage",
      "ResourceBudget",
      "UnsupportedMetadataFormat",
    ],
  );
  requireNullableEnum(
    issue.metadataRootReason,
    `${description}.metadataRootReason`,
    [
      "UnmappableMetadataDirectory",
      "TruncatedFixedPrefix",
      "InvalidSignature",
      "InvalidVersionLength",
      "TruncatedVersionField",
      "MissingVersionTerminator",
    ],
  );
  if (issue.inspectionFailures !== null) {
    if (!Array.isArray(issue.inspectionFailures))
      throw new Error(`${description}.inspectionFailures must be an array.`);
    for (const [index, entry] of issue.inspectionFailures.entries()) {
      const failure = requireRecord(
        entry,
        `${description}.inspectionFailures[${index}]`,
      );
      requireString(failure.operation, `${description}.operation`);
      requireInteger(failure.subjectToken, `${description}.subjectToken`);
      requireEnum(failure.mechanism, `${description}.mechanism`, [
        "Metadata",
        "Relationship",
        "Signature",
        "TypeSpecification",
      ]);
      requireString(failure.kind, `${description}.failureKind`);
      requireString(failure.detail, `${description}.failureDetail`);
      if (failure.subjectAssembly !== null) {
        validateAssembly(
          failure.subjectAssembly,
          `${description}.subjectAssembly`,
        );
      }
      if (failure.dependencyAssembly !== null) {
        validateAssembly(
          failure.dependencyAssembly,
          `${description}.dependencyAssembly`,
        );
      }
    }
  }
  if (issue.truncation !== null) {
    const truncation = requireRecord(
      issue.truncation,
      `${description}.truncation`,
    );
    requireEnum(truncation.limit, `${description}.truncation.limit`, [
      "Participants",
      "Types",
      "Members",
      "InspectionFailures",
      "TypeForwarders",
      "MetadataRows",
      "RetainedTextCharacters",
    ]);
    for (const property of [
      "bound",
      "projectedParticipants",
      "omittedParticipants",
      "projectedTypes",
      "projectedMembers",
      "projectedInspectionFailures",
      "projectedTypeForwarders",
      "inspectedMetadataRows",
      "projectedRetainedTextCharacters",
    ]) {
      requireInteger(
        truncation[property],
        `${description}.truncation.${property}`,
      );
    }
  }
}

function validateEndpoint(value: unknown, description: string): void {
  const endpoint = requireRecord(value, description);
  requireString(endpoint.packageId, `${description}.packageId`);
  requireString(endpoint.version, `${description}.version`);
  requireString(endpoint.framework, `${description}.framework`);
  const asset = requireRecord(endpoint.asset, `${description}.asset`);
  requireString(asset.id, `${description}.asset.id`);
  requireString(asset.path, `${description}.asset.path`);
  requireString(asset.assemblyName, `${description}.asset.assemblyName`);
  validateAssembly(endpoint.assembly, `${description}.assembly`);
  requireEnum(endpoint.scope, `${description}.scope`, [
    "Public",
    "IncludeAll",
    "PublicWithNonPublicTypes",
  ]);
  if (typeof endpoint.isComplete !== "boolean")
    throw new Error(`${description}.isComplete must be a boolean.`);
  if (!Array.isArray(endpoint.issues))
    throw new Error(`${description}.issues must be an array.`);
  endpoint.issues.forEach((issue, index) =>
    validateEndpointIssue(issue, `${description}.issues[${index}]`));
}

function validateTypeIdentity(value: unknown, description: string): void {
  const identity = requireRecord(value, description);
  requireString(identity.identifier, `${description}.identifier`);
  requireString(identity.namespace, `${description}.namespace`);
  requireString(identity.display, `${description}.display`);
  if (!Array.isArray(identity.segments)
    || !identity.segments.every(segment => typeof segment === "string")) {
    throw new Error(`${description}.segments must be a string array.`);
  }
}

const changeKinds = [
  "TypeAdded",
  "TypeRemoved",
  "TypeKindChanged",
  "SealedAdded",
  "SealedRemoved",
  "AbstractAdded",
  "AbstractRemoved",
  "BaseTypeChanged",
  "InterfaceAdded",
  "InterfaceRemoved",
  "TypeParameterCountChanged",
  "TypeParameterVarianceChanged",
  "TypeParameterConstraintTightened",
  "TypeParameterConstraintLoosened",
  "MemberAdded",
  "MemberRemoved",
  "MemberSignatureChanged",
  "VirtualRemoved",
  "AbstractMemberAdded",
  "EnumValueChanged",
  "TypeAttributeAdded",
  "TypeAttributeRemoved",
  "MemberAttributeAdded",
  "MemberAttributeRemoved",
] as const;

function validateChanges(value: unknown, description: string): void {
  if (!Array.isArray(value))
    throw new Error(`${description} must be an array.`);
  for (const [index, entry] of value.entries()) {
    const change = requireRecord(entry, `${description}[${index}]`);
    requireEnum(change.kind, `${description}[${index}].kind`, changeKinds);
    requireEnum(
      change.classification,
      `${description}[${index}].classification`,
      ["Additive", "Breaking", "PotentiallyBreaking"],
    );
    requireEnum(change.category, `${description}[${index}].category`, [
      "Signature",
      "Attribute",
    ]);
    requireString(change.message, `${description}[${index}].message`);
    requireNullableString(
      change.oldValue,
      `${description}[${index}].oldValue`,
    );
    requireNullableString(
      change.newValue,
      `${description}[${index}].newValue`,
    );
  }
}

function validateMemberIdentity(value: unknown, description: string): void {
  const identity = requireRecord(value, description);
  for (const property of [
    "declaringTypeIdentifier",
    "stableSelector",
    "canonicalSignature",
    "fingerprint",
    "typeFullName",
    "memberName",
    "display",
  ]) {
    requireString(identity[property], `${description}.${property}`);
  }
}

function validateMembers(value: unknown, description: string): void {
  if (!Array.isArray(value))
    throw new Error(`${description} must be an array.`);
  for (const [index, entry] of value.entries()) {
    const member = requireRecord(entry, `${description}[${index}]`);
    requireString(
      member.documentIdentifier,
      `${description}[${index}].documentIdentifier`,
    );
    requireEnum(member.pairKind, `${description}[${index}].pairKind`, [
      "Changed",
      "Added",
      "Removed",
    ]);
    requireEnum(member.role, `${description}[${index}].role`, [
      "Before",
      "After",
      "Both",
    ]);
    if (member.before !== null)
      validateMemberIdentity(member.before, `${description}[${index}].before`);
    if (member.after !== null)
      validateMemberIdentity(member.after, `${description}[${index}].after`);
    if (member.before === null && member.after === null)
      throw new Error(`${description}[${index}] has no side.`);
    validateChanges(member.changes, `${description}[${index}].changes`);
    if (member.match !== null) {
      const match = requireRecord(member.match, `${description}[${index}].match`);
      requireString(match.tier, `${description}[${index}].match.tier`);
      if (match.tier === "")
        throw new Error(`${description}[${index}].match.tier must not be empty.`);
      if (!Number.isSafeInteger(match.confidence)
        || Number(match.confidence) <= 0
        || Number(match.confidence) >= 100) {
        throw new Error(
          `${description}[${index}].match.confidence must be between 1 and 99.`,
        );
      }
    }
  }
}

function validateSucceeded(value: unknown): void {
  const succeeded = requireRecord(value, "Library API Diff success");
  requireString(
    succeeded.libraryIdentifier,
    "Library API Diff success.libraryIdentifier",
  );
  requireString(
    succeeded.libraryDisplay,
    "Library API Diff success.libraryDisplay",
  );
  validateEndpoint(succeeded.target, "Library API Diff success.target");
  validateEndpoint(succeeded.current, "Library API Diff success.current");
  const aggregate = requireRecord(
    succeeded.aggregate,
    "Library API Diff success.aggregate",
  );
  for (const property of [
    "changedTypeCount",
    "addedTypeCount",
    "removedTypeCount",
    "changedMemberCount",
    "breakingCount",
    "additiveCount",
    "potentiallyBreakingCount",
  ]) {
    requireInteger(
      aggregate[property],
      `Library API Diff success.aggregate.${property}`,
    );
  }
  if (!Array.isArray(succeeded.types))
    throw new Error("Library API Diff success.types must be an array.");
  for (const [index, entry] of succeeded.types.entries()) {
    const type = requireRecord(
      entry,
      `Library API Diff success.types[${index}]`,
    );
    requireString(type.documentIdentifier, "Library API Diff Type identifier");
    requireString(type.display, "Library API Diff Type display");
    requireEnum(type.state, "Library API Diff Type state", [
      "Diff",
      "Addition",
      "Deletion",
    ]);
    if (type.typeDefinitionChanged !== null
      && typeof type.typeDefinitionChanged !== "boolean") {
      throw new Error(
        "Library API Diff Type definition state must be boolean or null.",
      );
    }
    for (const property of [
      "changedMemberCount",
      "breakingCount",
      "additiveCount",
      "potentiallyBreakingCount",
    ]) {
      requireInteger(type[property], `Library API Diff Type ${property}`);
    }
    if (type.before !== null)
      validateTypeIdentity(type.before, "Library API Diff before Type");
    if (type.after !== null)
      validateTypeIdentity(type.after, "Library API Diff after Type");
    validateMembers(
      type.members,
      `Library API Diff success.types[${index}].members`,
    );
    validateChanges(
      type.changes,
      `Library API Diff success.types[${index}].changes`,
    );
  }
}

function validateInspection(
  value: unknown,
  expectedOutcome: "available" | "unavailable" | "rejected",
): void {
  const inspection = requireRecord(value, "Library API Diff inspection");
  if (inspection.contentKind !== "outcome")
    throw new Error("Library API Diff inspection content kind is invalid.");
  if (inspection.resourcePath !== "library-api-diff") {
    throw new Error("Library API Diff inspection has the wrong resource path.");
  }
  const content = requireRecord(
    inspection.content,
    "Library API Diff inspection Content",
  );
  if (content.outcome !== expectedOutcome)
    throw new Error("Library API Diff inspection contradicts its result.");
  if (expectedOutcome === "available") {
    requireRecord(content.document, "Library API Diff Content document");
  } else {
    requireInteger(content.kind, "Library API Diff Content kind");
    requireRecord(content.before, "Library API Diff Content Before");
    requireRecord(content.after, "Library API Diff Content After");
  }
  const portableProjection = requireRecord(
    inspection.portableProjection,
    "Library API Diff portable projection",
  );
  requireEnum(
    portableProjection.kind,
    "Library API Diff portable projection kind",
    [
    "available",
    "nonProjectable",
    ],
  );
  if (portableProjection.kind === "available") {
    requireString(
      portableProjection.fullUrl,
      "Library API Diff portable projection URL",
    );
    requireString(
      portableProjection.packet,
      "Library API Diff portable projection packet",
    );
  } else {
    requireNull(portableProjection, "fullUrl", "packet");
    requireNullableString(
      portableProjection.location,
      "Library API Diff portable projection location",
    );
    requireEnum(
      portableProjection.reason,
      "Library API Diff portable projection reason",
      ["notSupported", "invalid", "incomplete", "unavailable", "failed"],
    );
    requireNullableString(
      portableProjection.explanation,
      "Library API Diff portable projection explanation",
    );
  }
  if (!Array.isArray(inspection.diagnostics))
    throw new Error("Library API Diff inspection diagnostics must be an array.");
  for (const item of inspection.diagnostics) {
    const diagnostic = requireRecord(item, "Library API Diff diagnostic");
    requireString(diagnostic.code, "Library API Diff diagnostic code");
    requireString(diagnostic.summary, "Library API Diff diagnostic summary");
    requireNullableString(
      diagnostic.correspondence,
      "Library API Diff diagnostic correspondence",
    );
    if (diagnostic.severity !== 0
      && diagnostic.severity !== 1
      && diagnostic.severity !== 2) {
      throw new Error("Library API Diff diagnostic severity is unsupported.");
    }
  }
}

function validateResult(
  result: unknown,
  input: LibraryApiDiffOperationInput,
): asserts result is BrowserLibraryApiDiffResult {
  const record = requireRecord(result, "Library API Diff result");
  if (record.schemaVersion !== 1)
    throw new Error("Unsupported Library API Diff result schema.");
  const request = requireRecord(
    record.request,
    "Library API Diff result request",
  );
  if (request.schemaVersion !== 1
    || request.packageId !== input.packageId
    || request.currentVersion !== input.currentVersion
    || request.targetVersion !== input.targetVersion
    || request.targetFramework !== input.targetFramework
    || request.compileAssetId !== input.compileAssetId) {
    throw new Error("Library API Diff result does not match its request.");
  }
  switch (record.kind) {
    case "Succeeded":
      validateInspection(record.inspection, "available");
      validateSucceeded(record.value);
      requireNull(
        record,
        "unavailable",
        "rejected",
        "failureKind",
        "error",
        "diagnostic",
        "reason",
      );
      return;
    case "Unavailable":
      validateInspection(record.inspection, "unavailable");
      {
        const unavailable = requireRecord(
          record.unavailable,
          "Library API Diff unavailable evidence",
        );
        requireEnum(unavailable.kind, "Library API Diff unavailable kind", [
          "TargetIncomplete",
          "CurrentIncomplete",
          "BothIncomplete",
        ]);
        validateEndpoint(
          unavailable.target,
          "Library API Diff unavailable target",
        );
        validateEndpoint(
          unavailable.current,
          "Library API Diff unavailable current",
        );
      }
      requireNull(
        record,
        "value",
        "rejected",
        "failureKind",
        "error",
        "diagnostic",
        "reason",
      );
      return;
    case "Rejected":
      {
        const rejected = requireRecord(
          record.rejected,
          "Library API Diff rejection",
        );
        requireEnum(rejected.kind, "Library API Diff rejection kind", [
          "LogicalLibraryMismatch",
          "FindingComparisonFailed",
          "CompatibilityInspectionFailed",
          "MissingExactTypeIdentity",
          "MissingMemberAnchor",
          "DuplicateExactTypeIdentity",
          "UnassociatedStructuredSubject",
          "ContradictoryOccupiedSideTopology",
          "ChangedTypeCountLimitExceeded",
          "TypeTextLimitExceeded",
          "CollectionEntryLimitExceeded",
          "SerializedResultLimitExceeded",
        ]);
        if (rejected.kind === "CollectionEntryLimitExceeded"
          || rejected.kind === "SerializedResultLimitExceeded") {
          requireNull(record, "inspection");
          requireNull(rejected, "target", "current");
        } else {
          validateInspection(
            record.inspection,
            rejected.kind === "ChangedTypeCountLimitExceeded"
              || rejected.kind === "TypeTextLimitExceeded"
              ? "available"
              : "rejected",
          );
        }
        if (rejected.target !== null)
          validateEndpoint(rejected.target, "Library API Diff rejected target");
        if (rejected.current !== null) {
          validateEndpoint(
            rejected.current,
            "Library API Diff rejected current",
          );
        }
        requireNullableInteger(
          rejected.bound,
          "Library API Diff rejection bound",
        );
        requireNullableInteger(
          rejected.observed,
          "Library API Diff rejection observation",
        );
      }
      requireNull(
        record,
        "value",
        "unavailable",
        "failureKind",
        "error",
        "diagnostic",
        "reason",
      );
      return;
    case "Failed":
      requireEnum(record.failureKind, "Library API Diff failure kind", [
        "Expected",
        "Unexpected",
      ]);
      requireString(record.error, "Library API Diff failure error");
      requireString(record.diagnostic, "Library API Diff failure diagnostic");
      requireNull(record, "value", "unavailable", "rejected", "reason", "inspection");
      return;
    case "Canceled":
      requireString(record.reason, "Library API Diff cancellation reason");
      requireNull(
        record,
        "value",
        "unavailable",
        "rejected",
        "failureKind",
        "error",
        "diagnostic",
        "inspection",
      );
      return;
    default:
      throw new Error("Unknown Library API Diff result kind.");
  }
}

export function createLibraryApiDiffCoordinator(
  dependencies: LibraryApiDiffDependencies,
): LibraryApiDiffCoordinator {
  type FeatureEvent =
    OperationFeatureEvent<BrowserLibraryApiDiffResult, unknown, never>;
  type Session = OperationSession<
    LibraryApiDiffOperationInput,
    BrowserLibraryApiDiffResult,
    unknown,
    never,
    never
  >;

  const inputs = new Map<OperationId, LibraryApiDiffOperationInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Library API Diff operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced":
        dependencies.state.libraryApiDiff = {
          status: "loading",
          input: inputFor(event.operation.id),
        };
        break;
      case "terminal": {
        const input = inputFor(event.operationId);
        dependencies.state.libraryApiDiff =
          event.outcome.kind === "succeeded"
            ? {
                status: "ready",
                input,
                result: event.outcome.value,
              }
            : {
                status: "failed",
                input,
                error: dependencies.describeError(event.outcome.error),
              };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
        dependencies.state.libraryApiDiff = { status: "idle" };
        break;
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
    LibraryApiDiffOperationInput,
    BrowserLibraryApiDiffResult,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      let cancellationRequested = false;
      const quiesce = (): undefined => {
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const boundaryFailure = (error: unknown): undefined => {
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      const finish = (result: unknown): undefined => {
        try {
          validateResult(result, input);
          sink.reportTerminal({ kind: "succeeded", value: result });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: reason => {
            if (!cancellationRequested) {
              cancellationRequested = true;
              dependencies.cancel(identity.id, reason);
            }
            return undefined;
          },
          activate: () => {
            let query: Promise<unknown>;
            try {
              query = dependencies.query(identity.id, requestJson(input));
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(finish, boundaryFailure);
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

  const cancelCurrentRequest = (): boolean => {
    const cancellation = session.cancelCurrent("superseded");
    if (cancellation.kind === "rejected") {
      dependencies.reportOperationDiagnostic({
        kind: "producer-contract",
        operationId: null,
        error: new Error(
          "Library API Diff cancellation was attempted during feature publication.",
        ),
      });
      return false;
    }
    dependencies.state.libraryApiDiff = { status: "idle" };
    return cancellation.kind === "applied";
  };

  const start = (selection: LibraryApiDiffSelection): void => {
    if (selection.target.kind !== "available")
      throw new Error("A Library API Diff operation requires a resolved target.");
    const started = session.start({
      packageModel: selection.packageModel,
      packageId: selection.packageId,
      currentVersion: selection.currentVersion,
      targetVersion: selection.target.version,
      targetFramework: selection.targetFramework,
      compileAssetId: selection.compileAssetId,
    }, adapter);
    if (started.kind === "rejected") {
      dependencies.state.libraryApiDiff = {
        status: "failed",
        input: {
          packageModel: selection.packageModel,
          packageId: selection.packageId,
          currentVersion: selection.currentVersion,
          targetVersion: selection.target.version,
          targetFramework: selection.targetFramework,
          compileAssetId: selection.compileAssetId,
        },
        error: `Library API Diff could not start: ${started.reason.kind}.`,
      };
    }
  };

  const reconcile = (selection: LibraryApiDiffSelection | null): void => {
    if (selection === null) {
      cancelCurrentRequest();
      return;
    }
    if (stateMatchesSelection(
      dependencies.state.libraryApiDiff,
      selection,
    )) {
      return;
    }
    if (selection.target.kind !== "available") {
      cancelCurrentRequest();
      dependencies.state.libraryApiDiff = {
        status: selection.target.kind === "loading"
          ? "target-loading"
          : "target-unavailable",
        selection,
        message: selection.target.message,
      };
      return;
    }
    start(selection);
  };

  return {
    reconcile,
    retry(selection) {
      cancelCurrentRequest();
      start(selection);
    },
    cancelCurrentRequest,
  };
}

function compactCount(value: number, label: string): string {
  return value === 0 ? "" : `${value.toLocaleString()} ${label}`;
}

function assemblyText(
  assembly: BrowserLibraryApiDiffEndpoint["assembly"] | null,
): string {
  if (assembly === null) return "";
  return assembly.version === null
    ? assembly.name
    : `${assembly.name}, ${assembly.version}`;
}

function endpointIssues(endpoint: BrowserLibraryApiDiffEndpoint): string[] {
  return endpoint.issues.flatMap(issue => {
    if (issue.kind === "InspectionFailures") {
      const details = issue.inspectionFailures ?? [];
      if (details.length === 0) {
        return [
          `${issue.count?.toLocaleString() ?? "Unknown"} Metadata inspection failures.`,
        ];
      }
      return details.map(failure => {
        const dependency = assemblyText(failure.dependencyAssembly);
        return `${failure.operation} at 0x${failure.subjectToken.toString(16)
          .toUpperCase().padStart(8, "0")} (${String(failure.mechanism)}/${
          failure.kind
        }): ${failure.detail}${dependency === ""
          ? ""
          : ` Dependency: ${dependency}.`}`;
      });
    }
    if (issue.kind === "Truncated" && issue.truncation !== null) {
      return [
        `Projection stopped at ${String(issue.truncation.limit)} bound ${
          issue.truncation.bound.toLocaleString()
        }; ${issue.truncation.projectedTypes.toLocaleString()} Types and ${
          issue.truncation.projectedMembers.toLocaleString()
        } members were retained.`,
      ];
    }
    if (issue.detail !== null)
      return [`${String(issue.kind)}: ${issue.detail}`];
    if (issue.count !== null)
      return [`${issue.count.toLocaleString()} ${String(issue.kind)}.`];
    return [String(issue.kind)];
  });
}

function endpointEvidence(
  endpoint: BrowserLibraryApiDiffEndpoint,
  label: string,
  escapeHtml: (value: unknown) => string,
): string {
  const issues = endpointIssues(endpoint);
  if (issues.length === 0) return "";
  return `<details class="library-api-diff-evidence">
    <summary>${escapeHtml(label)} endpoint evidence</summary>
    <ul>${issues.map(issue => `<li>${escapeHtml(issue)}</li>`).join("")}</ul>
  </details>`;
}

function typeMetrics(type: BrowserLibraryApiDiffType): string {
  return [
    compactCount(type.changedMemberCount, type.changedMemberCount === 1
      ? "member"
      : "members"),
    compactCount(type.breakingCount, "breaking"),
    compactCount(type.additiveCount, "additive"),
    compactCount(type.potentiallyBreakingCount, "potentially breaking"),
  ].filter(Boolean).join(" · ");
}

function attributeText(
  value: string,
  escapeHtml: (value: unknown) => string,
): string {
  return escapeHtml(value).replaceAll("\r", "&#13;");
}

// The exact Library, Type, or Member the Compare surface is projecting from the
// complete Library-root document. Type and Member never run their own partial
// comparison; they narrow the same root result.
export type LibraryApiDiffSubject =
  | { readonly kind: "library" }
  | { readonly kind: "type"; readonly typeIdentifier: string }
  | {
      readonly kind: "member";
      readonly typeIdentifier: string;
      readonly memberFingerprint: string;
    };

export interface LibraryApiDiffRenderOptions {
  readonly subject?: LibraryApiDiffSubject;
  readonly subjectLabel?: string;
  // Exact current-side identities the Browser has joined to loaded Navigation
  // subjects. A current-side row whose identity is absent stays visible but
  // inert: Compare never derives a subject from display text.
  readonly activatableTypes?: ReadonlySet<string>;
  readonly activatableMembers?: ReadonlySet<string>;
  readonly targetText?: string;
  readonly mode?: CompareMode;
}

function renderTypeRow(
  type: BrowserLibraryApiDiffType,
  escapeHtml: (value: unknown) => string,
  activatableTypes: ReadonlySet<string> | undefined,
): string {
  const before = type.before?.identifier ?? "";
  const after = type.after?.identifier ?? "";
  const beforeAttribute = attributeText(before, escapeHtml);
  const afterAttribute = attributeText(after, escapeHtml);
  const definition = type.typeDefinitionChanged === true
    ? '<span class="library-api-diff-definition">Type definition changed</span>'
    : "";
  const activatable =
    type.after !== null && activatableTypes?.has(after) === true;
  const inertReason = type.after === null
    ? "Removed in the current version; Before-side evidence only"
    : activatable
      ? ""
      : "Not joined to a loaded Type";
  const copy = `<span class="library-api-diff-type-copy">
      <strong>${escapeHtml(type.display)}</strong>
      <span>${escapeHtml(typeMetrics(type))}</span>
      ${definition}
      ${inertReason ? `<span class="library-api-diff-inert">${escapeHtml(inertReason)}</span>` : ""}
    </span>`;
  const state = `<span class="library-api-diff-state library-api-diff-state-${String(type.state).toLowerCase()}">${escapeHtml(type.state)}</span>`;
  return `<li class="library-api-diff-type${activatable ? "" : " library-api-diff-type-inert"}" data-before-type-id="${beforeAttribute}" data-after-type-id="${afterAttribute}">${
    activatable
      ? `<button type="button" class="library-api-diff-row" data-compare-type-id="${afterAttribute}" aria-label="Open ${escapeHtml(type.display)} Compare">${state}${copy}</button>`
      : `<div class="library-api-diff-row" aria-disabled="true">${state}${copy}</div>`
  }</li>`;
}

function classificationLabel(
  classification: BrowserLibraryApiDiffChange["classification"],
): string {
  switch (classification) {
    case "Breaking": return "Breaking";
    case "Additive": return "Additive";
    case "PotentiallyBreaking": return "Potentially breaking";
    default: return String(classification);
  }
}

// Producer kinds are PascalCase identifiers; spell them as quiet words.
function changeKindLabel(kind: BrowserLibraryApiDiffChange["kind"]): string {
  return String(kind).replaceAll(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
}

function classificationClass(
  classification: BrowserLibraryApiDiffChange["classification"],
): string {
  return `library-api-diff-change-${String(classification).toLowerCase()}`;
}

function changeChips(
  changes: readonly BrowserLibraryApiDiffChange[],
  escapeHtml: (value: unknown) => string,
): string {
  if (changes.length === 0) return "";
  return `<span class="library-api-diff-change-chips">${changes.map(change =>
    `<span class="library-api-diff-change-chip ${classificationClass(change.classification)}">${escapeHtml(
      `${classificationLabel(change.classification)} · ${changeKindLabel(change.kind)}`,
    )}</span>`).join("")}</span>`;
}

// One row per Metadata-issued compatibility change. The classification and
// message come from the producer; the Browser never re-derives either.
function renderChangeRows(
  changes: readonly BrowserLibraryApiDiffChange[],
  escapeHtml: (value: unknown) => string,
  label: string,
): string {
  if (changes.length === 0) return "";
  return `<ol class="library-api-diff-changes" aria-label="${escapeHtml(label)}">${changes.map(change => {
    // Identical old and new text (a modifier change the producer spells in
    // its message) adds nothing beside the message, so it stays out.
    const values = (change.oldValue !== null || change.newValue !== null)
      && change.oldValue !== change.newValue
      ? `<span class="library-api-diff-change-values"><code>${escapeHtml(change.oldValue ?? "—")}</code> → <code>${escapeHtml(change.newValue ?? "—")}</code></span>`
      : "";
    return `<li class="library-api-diff-change">
      <span class="library-api-diff-change-chip ${classificationClass(change.classification)}">${escapeHtml(classificationLabel(change.classification))}</span>
      <span class="library-api-diff-change-copy">
        <strong>${escapeHtml(changeKindLabel(change.kind))}</strong>
        <span>${escapeHtml(change.message)}</span>
        ${values}
        <span class="library-api-diff-change-category">${escapeHtml(String(change.category))}</span>
      </span>
    </li>`;
  }).join("")}</ol>`;
}

function memberStateLabel(member: BrowserLibraryApiDiffMember): string {
  switch (member.pairKind) {
    case "Changed": return "Changed";
    case "Added": return "Added";
    case "Removed": return "Removed";
    default: return String(member.pairKind);
  }
}

// A relation whose Before and After declaring Types differ is one Member that
// the producer placed under both Types. The exact declaring-Type identifiers
// are the join; display text is never used to decide this.
function counterpartType(
  member: BrowserLibraryApiDiffMember,
): { readonly identifier: string; readonly fullName: string } | null {
  if (member.before === null || member.after === null) return null;
  if (member.before.declaringTypeIdentifier
    === member.after.declaringTypeIdentifier) {
    return null;
  }
  const other = member.role === "Before" ? member.after : member.before;
  return {
    identifier: other.declaringTypeIdentifier,
    fullName: other.typeFullName,
  };
}

function matchText(member: BrowserLibraryApiDiffMember): string {
  return member.match === null
    ? ""
    : `Matched by ${member.match.tier} at ${member.match.confidence}% confidence`;
}

function renderMemberRow(
  member: BrowserLibraryApiDiffMember,
  escapeHtml: (value: unknown) => string,
  activatableMembers: ReadonlySet<string> | undefined,
  activatableTypes: ReadonlySet<string> | undefined,
): string {
  const display = member.after?.display ?? member.before?.display ?? "";
  const fingerprint = member.after?.fingerprint ?? "";
  const counterpart = counterpartType(member);
  // The Before-role placement of a moved Member has no current subject on this
  // Type; its current subject lives on the counterpart Type.
  const movedAway = counterpart !== null && member.role === "Before";
  const activatable = !movedAway
    && member.after !== null
    && activatableMembers?.has(fingerprint) === true;
  const inertReason = member.after === null
    ? "Removed in the current version; Before-side evidence only"
    : movedAway
      ? `Now declared on ${counterpart.fullName}`
      : activatable
        ? ""
        : "Not joined to a loaded Member";
  const movedFrom = counterpart !== null && member.role !== "Before"
    ? `<span class="library-api-diff-moved">Moved from ${escapeHtml(counterpart.fullName)}${
      member.match === null ? "" : ` · ${escapeHtml(matchText(member))}`}</span>`
    : "";
  const openCounterpart = movedAway
    && activatableTypes?.has(counterpart.identifier) === true
    ? `<button type="button" class="library-api-diff-counterpart" data-compare-type-id="${attributeText(counterpart.identifier, escapeHtml)}">Open ${escapeHtml(counterpart.fullName)}</button>`
    : "";
  const signatures = member.before !== null && member.after !== null
    && member.before.canonicalSignature !== member.after.canonicalSignature
    ? `<span class="library-api-diff-signature-change"><code>${escapeHtml(member.before.canonicalSignature)}</code> → <code>${escapeHtml(member.after.canonicalSignature)}</code></span>`
    : "";
  const copy = `<span class="library-api-diff-type-copy">
      <strong>${escapeHtml(display)}</strong>
      ${changeChips(member.changes, escapeHtml)}
      ${movedFrom}
      ${signatures}
      ${inertReason ? `<span class="library-api-diff-inert">${escapeHtml(inertReason)}</span>` : ""}
    </span>`;
  const state = `<span class="library-api-diff-state library-api-diff-state-${String(member.pairKind).toLowerCase()}">${escapeHtml(memberStateLabel(member))}</span>`;
  // The counterpart action is a sibling of the inert row, not a child of it,
  // so the row's disabled state never disables the one live control.
  return `<li class="library-api-diff-member${activatable ? "" : " library-api-diff-member-inert"}" data-member-fingerprint="${attributeText(fingerprint, escapeHtml)}" data-member-before-fingerprint="${attributeText(member.before?.fingerprint ?? "", escapeHtml)}">${
    activatable
      ? `<button type="button" class="library-api-diff-row" data-compare-member-fingerprint="${attributeText(fingerprint, escapeHtml)}" aria-label="Open ${escapeHtml(display)} Compare">${state}${copy}</button>`
      : `<div class="library-api-diff-row" aria-disabled="true">${state}${copy}</div>${openCounterpart}`
  }</li>`;
}

function memberIdentityEvidence(
  label: string,
  identity: BrowserLibraryApiDiffMemberIdentity | null,
  escapeHtml: (value: unknown) => string,
): string {
  if (identity === null) {
    return `<section class="library-api-diff-endpoint">
      <h2>${escapeHtml(label)}</h2>
      <p class="library-api-diff-absent">Not present on this side.</p>
    </section>`;
  }
  return `<section class="library-api-diff-endpoint">
    <h2>${escapeHtml(label)}</h2>
    <p><code>${escapeHtml(identity.display)}</code></p>
    <dl>
      <div><dt>Declaring type</dt><dd><code>${escapeHtml(identity.typeFullName)}</code></dd></div>
      <div><dt>Stable selector</dt><dd><code>${escapeHtml(identity.stableSelector)}</code></dd></div>
      <div><dt>Canonical signature</dt><dd><code>${escapeHtml(identity.canonicalSignature)}</code></dd></div>
      <div><dt>Digest</dt><dd><code>${escapeHtml(identity.fingerprint)}</code></dd></div>
    </dl>
  </section>`;
}

function findType(
  value: BrowserLibraryApiDiffSucceeded,
  typeIdentifier: string,
): BrowserLibraryApiDiffType | undefined {
  return value.types.find(type => type.after?.identifier === typeIdentifier)
    ?? value.types.find(type =>
      type.after === null && type.before?.identifier === typeIdentifier);
}

function findMember(
  type: BrowserLibraryApiDiffType,
  fingerprint: string,
): BrowserLibraryApiDiffMember | undefined {
  return type.members.find(member => member.after?.fingerprint === fingerprint)
    ?? type.members.find(member =>
      member.after === null && member.before?.fingerprint === fingerprint);
}

interface RenderedContent {
  readonly status: string;
  readonly content: string;
}

function renderLibrarySubject(
  value: BrowserLibraryApiDiffSucceeded,
  escapeHtml: (value: unknown) => string,
  options: LibraryApiDiffRenderOptions,
): RenderedContent {
  const aggregate = value.aggregate;
  const metrics = [
    `${aggregate.changedTypeCount.toLocaleString()} changed ${
      aggregate.changedTypeCount === 1 ? "Type" : "Types"
    }`,
    compactCount(aggregate.addedTypeCount, "added"),
    compactCount(aggregate.removedTypeCount, "removed"),
    compactCount(aggregate.changedMemberCount, "changed members"),
    compactCount(aggregate.breakingCount, "breaking"),
    compactCount(aggregate.additiveCount, "additive"),
    compactCount(aggregate.potentiallyBreakingCount, "potentially breaking"),
  ].filter(Boolean);
  if (value.types.length === 0) {
    return {
      status: "Comparison complete. No changed Types.",
      content: renderCompareEmpty(
        "No public API changes",
        "The selected Library is unchanged between these versions.",
        escapeHtml,
      ),
    };
  }
  return {
    status: `Comparison complete. ${aggregate.changedTypeCount.toLocaleString()} changed Types.`,
    content: `<div class="library-api-diff-metrics">${metrics.map(metric =>
      `<span>${escapeHtml(metric)}</span>`).join("")}</div>
      <ol class="library-api-diff-types" aria-label="Changed Types">${value.types.map(type =>
        renderTypeRow(type, escapeHtml, options.activatableTypes)).join("")}</ol>`,
  };
}

function renderTypeSubject(
  value: BrowserLibraryApiDiffSucceeded,
  typeIdentifier: string,
  escapeHtml: (value: unknown) => string,
  options: LibraryApiDiffRenderOptions,
): RenderedContent {
  const type = findType(value, typeIdentifier);
  if (type === undefined) {
    return {
      status: "Comparison complete. No changed Members.",
      content: renderCompareEmpty(
        "No public API changes",
        "This Type is unchanged between these versions.",
        escapeHtml,
      ),
    };
  }
  const metrics = [
    `${type.changedMemberCount.toLocaleString()} changed ${
      type.changedMemberCount === 1 ? "Member" : "Members"
    }`,
    compactCount(type.breakingCount, "breaking"),
    compactCount(type.additiveCount, "additive"),
    compactCount(type.potentiallyBreakingCount, "potentially breaking"),
    type.typeDefinitionChanged === true ? "Type definition changed" : "",
    type.state === "Addition"
      ? "Added Type"
      : type.state === "Deletion" ? "Removed Type" : "",
  ].filter(Boolean);
  // Type-level compatibility changes (kind, base type, interfaces, generic
  // parameters, attributes) precede the Member inventory.
  const typeChanges = renderChangeRows(
    type.changes,
    escapeHtml,
    "Type-level changes",
  );
  // A whole-Type immersive destination is owner-issued. None is issued today,
  // so the row is absent rather than advertised with a placeholder.
  if (type.members.length === 0) {
    return {
      status: `Comparison complete. ${metrics[0] ?? "No changed Members"}.`,
      content: `<div class="library-api-diff-metrics">${metrics.map(metric =>
        `<span>${escapeHtml(metric)}</span>`).join("")}</div>${typeChanges}${
        renderCompareEmpty(
          "No changed Members",
          type.typeDefinitionChanged === true
            ? "Only the Type definition changed between these versions."
            : "No Member-level changes were reported for this Type.",
          escapeHtml,
        )}`,
    };
  }
  return {
    status: `Comparison complete. ${type.members.length.toLocaleString()} changed Members.`,
    content: `<div class="library-api-diff-metrics">${metrics.map(metric =>
      `<span>${escapeHtml(metric)}</span>`).join("")}</div>${typeChanges}
      <ol class="library-api-diff-members" aria-label="Changed Members">${type.members.map(member =>
        renderMemberRow(
          member,
          escapeHtml,
          options.activatableMembers,
          options.activatableTypes,
        )).join("")}</ol>`,
  };
}

function renderMemberSubject(
  value: BrowserLibraryApiDiffSucceeded,
  typeIdentifier: string,
  memberFingerprint: string,
  escapeHtml: (value: unknown) => string,
): RenderedContent {
  const type = findType(value, typeIdentifier);
  const member = type === undefined
    ? undefined
    : findMember(type, memberFingerprint);
  if (type === undefined || member === undefined) {
    return {
      status: "Comparison complete. This Member is unchanged.",
      content: renderCompareEmpty(
        "No public API change",
        "This Member is unchanged between these versions.",
        escapeHtml,
      ),
    };
  }
  const classification = [
    `${memberStateLabel(member)} Member`,
    `Relation ${String(member.role)}`,
    ...member.changes.map(change =>
      `${classificationLabel(change.classification)} · ${changeKindLabel(change.kind)}`),
    type.typeDefinitionChanged === true ? "Type definition changed" : "",
  ].filter(Boolean);
  const counterpart = counterpartType(member);
  const correspondence = [
    matchText(member),
    counterpart !== null && member.before !== null && member.after !== null
      ? `Moved from ${member.before.typeFullName} to ${member.after.typeFullName}`
      : "",
  ].filter(Boolean);
  const correspondenceHtml = correspondence.length === 0
    ? ""
    : `<p class="library-api-diff-note library-api-diff-correspondence">${correspondence.map(escapeHtml).join(" · ")}</p>`;
  // A Member inside an added or removed Type carries no change of its own:
  // the classified change belongs to the Type entry.
  const carriedByType = member.changes.length === 0
    && (type.state === "Addition" || type.state === "Deletion");
  const changes = member.changes.length > 0
    ? renderChangeRows(member.changes, escapeHtml, "What changed")
    : `<p class="library-api-diff-note">${escapeHtml(carriedByType
      ? `No Member-level change is classified: the containing Type was ${type.state === "Addition" ? "added" : "removed"} as a whole.`
      : "No classified compatibility change is recorded for this Member.")}</p>`;
  // Explore opens only an owner-issued immersive destination. None is issued
  // for Member Diff today, so no Explore action is rendered.
  return {
    status: `Comparison complete. Member ${memberStateLabel(member).toLowerCase()}.`,
    content: `<div class="library-api-diff-metrics">${classification.map(metric =>
      `<span>${escapeHtml(metric)}</span>`).join("")}</div>
      ${correspondenceHtml}
      <section class="library-api-diff-change-section" aria-labelledby="library-api-diff-changes-title">
        <h2 id="library-api-diff-changes-title">What changed</h2>
        ${changes}
      </section>
      <div class="library-api-diff-member-detail">
        ${memberIdentityEvidence("Before", member.before, escapeHtml)}
        ${memberIdentityEvidence("After", member.after, escapeHtml)}
      </div>`,
  };
}

function renderSucceeded(
  value: BrowserLibraryApiDiffSucceeded,
  escapeHtml: (value: unknown) => string,
  options: LibraryApiDiffRenderOptions,
): RenderedContent {
  const subject = options.subject ?? { kind: "library" };
  switch (subject.kind) {
    case "library":
      return renderLibrarySubject(value, escapeHtml, options);
    case "type":
      return renderTypeSubject(
        value,
        subject.typeIdentifier,
        escapeHtml,
        options,
      );
    case "member":
      return renderMemberSubject(
        value,
        subject.typeIdentifier,
        subject.memberFingerprint,
        escapeHtml,
      );
    default: {
      const exhaustive: never = subject;
      throw new Error(`Unhandled Compare subject: ${String(exhaustive)}`);
    }
  }
}

function frame(
  input: LibraryApiDiffOperationInput | null,
  rendered: RenderedContent,
  escapeHtml: (value: unknown) => string,
  options: LibraryApiDiffRenderOptions,
): string {
  const target = options.targetText
    ?? (input === null
      ? "Package Diff target"
      : `${input.targetVersion} → ${input.currentVersion}`);
  return renderCompareFrame({
    subjectKind: options.subject?.kind ?? "library",
    subjectLabel: options.subjectLabel ?? "Library API diff",
    mode: options.mode ?? "diff",
    targetText: target,
    status: rendered.status,
    content: rendered.content,
    escapeHtml,
  });
}

export function renderLibraryApiDiff(
  state: LibraryApiDiffState,
  escapeHtml: (value: unknown) => string,
  options: LibraryApiDiffRenderOptions = {},
): string {
  if (state.status === "idle") {
    return frame(
      null,
      {
        status: "Choose a Gallery Package Library to compare.",
        content: "",
      },
      escapeHtml,
      options,
    );
  }
  if (state.status === "target-loading"
    || state.status === "target-unavailable") {
    return frame(
      null,
      {
        status: state.message,
        content: state.status === "target-loading"
          ? renderCompareLoading()
          : renderCompareEmpty(
              "No comparison target",
              "Choose another Package Diff target to continue.",
              escapeHtml,
            ),
      },
      escapeHtml,
      options,
    );
  }
  if (state.status === "loading") {
    return frame(
      state.input,
      {
        status: "Comparing complete public API surfaces...",
        content: renderCompareLoading(),
      },
      escapeHtml,
      options,
    );
  }
  if (state.status === "failed") {
    return frame(
      state.input,
      { status: state.error, content: renderCompareRetry() },
      escapeHtml,
      options,
    );
  }

  const { input, result } = state;
  const diagnostics = result.inspection?.diagnostics ?? [];
  const diagnosticHtml = diagnostics.length === 0 ? "" :
    `<details class="library-api-diff-evidence">
      <summary>Inspection diagnostics (${diagnostics.length})</summary>
      <ul>${diagnostics.map(diagnostic =>
        `<li>${escapeHtml(
          `${["Information", "Warning", "Error"][diagnostic.severity]} ${
            diagnostic.code}: ${diagnostic.summary}${
            diagnostic.correspondence === null
              ? ""
              : ` (${diagnostic.correspondence})`}`,
        )}</li>`).join("")}</ul>
    </details>`;
  switch (result.kind) {
    case "Succeeded": {
      const value = result.value;
      if (value === null)
        throw new Error("Library API Diff success has no value.");
      const rendered = renderSucceeded(value, escapeHtml, options);
      return frame(
        input,
        { status: rendered.status, content: rendered.content + diagnosticHtml },
        escapeHtml,
        options,
      );
    }
    case "Unavailable": {
      const unavailable = result.unavailable;
      if (unavailable === null)
        throw new Error("Library API Diff unavailable result has no evidence.");
      const evidence = [
        endpointEvidence(unavailable.target, "Target", escapeHtml),
        endpointEvidence(unavailable.current, "Current", escapeHtml),
      ].join("");
      return frame(
        input,
        {
          status: `Comparison unavailable: ${String(unavailable.kind)}.`,
          content: (evidence
            || renderCompareEmpty(
              "Comparison unavailable",
              "One or both API surfaces are incomplete.",
              escapeHtml,
            )) + diagnosticHtml,
        },
        escapeHtml,
        options,
      );
    }
    case "Rejected": {
      const rejected = result.rejected;
      if (rejected === null)
        throw new Error("Library API Diff rejection has no evidence.");
      const bound = rejected.bound === null
        ? ""
        : ` Bound ${rejected.bound.toLocaleString()}, observed ${
          rejected.observed?.toLocaleString() ?? "unknown"
        }.`;
      return frame(
        input,
        {
          status: `Comparison rejected: ${String(rejected.kind)}.`,
          content: renderCompareEmpty(
            "Comparison rejected",
            `The complete result could not be admitted.${bound}`,
            escapeHtml,
          ) + diagnosticHtml,
        },
        escapeHtml,
        options,
      );
    }
    case "Failed":
      return frame(
        input,
        {
          status: result.error ?? "Library API Diff failed.",
          content: renderCompareRetry(),
        },
        escapeHtml,
        options,
      );
    case "Canceled":
      return frame(
        input,
        {
          status: "Comparison canceled.",
          content: renderCompareRetry("Run comparison"),
        },
        escapeHtml,
        options,
      );
    default:
      throw new Error("Unknown Library API Diff result kind.");
  }
}

export interface LibraryApiDiffRowActions {
  // Both identities are owner-issued: the exact current-side Type identifier
  // and the exact current-side Member digest carried by the diff document.
  readonly activateType: (typeIdentifier: string) => void;
  readonly activateMember: (memberFingerprint: string) => void;
}

export function bindLibraryApiDiffRows(
  root: ParentNode,
  actions: LibraryApiDiffRowActions,
): void {
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-compare-type-id]",
  )) {
    button.addEventListener("click", () => {
      const identifier = button.dataset.compareTypeId;
      if (identifier) actions.activateType(identifier);
    });
  }
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-compare-member-fingerprint]",
  )) {
    button.addEventListener("click", () => {
      const fingerprint = button.dataset.compareMemberFingerprint;
      if (fingerprint) actions.activateMember(fingerprint);
    });
  }
}
