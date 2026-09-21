import type {
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffResult,
  BrowserLibraryApiDiffType,
} from "./facades/inspect-web-metadata.d.ts";
import type { EffectiveDiffTarget } from "./package-comparison-targets.ts";
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

function renderTypeRow(
  type: BrowserLibraryApiDiffType,
  escapeHtml: (value: unknown) => string,
): string {
  const before = type.before?.identifier ?? "";
  const after = type.after?.identifier ?? "";
  const beforeAttribute = escapeHtml(before).replaceAll("\r", "&#13;");
  const afterAttribute = escapeHtml(after).replaceAll("\r", "&#13;");
  const definition = type.typeDefinitionChanged === true
    ? '<span class="library-api-diff-definition">Type definition changed</span>'
    : "";
  return `<li class="library-api-diff-type" data-before-type-id="${beforeAttribute}" data-after-type-id="${afterAttribute}">
    <span class="library-api-diff-state library-api-diff-state-${String(type.state).toLowerCase()}">${escapeHtml(type.state)}</span>
    <span class="library-api-diff-type-copy">
      <strong>${escapeHtml(type.display)}</strong>
      <span>${escapeHtml(typeMetrics(type))}</span>
      ${definition}
    </span>
  </li>`;
}

function renderFrame(
  input: LibraryApiDiffOperationInput | null,
  status: string,
  content: string,
  escapeHtml: (value: unknown) => string,
): string {
  const target = input === null
    ? "Package Diff target"
    : `${input.targetVersion} → ${input.currentVersion}`;
  return `<section class="library-api-diff" aria-labelledby="library-api-diff-title">
    <header class="library-api-diff-head">
      <div>
        <p class="library-api-diff-kicker">Compare · Diff</p>
        <h1 id="library-api-diff-title">Library API diff</h1>
        <p class="library-api-diff-target">${escapeHtml(target)}</p>
      </div>
      <button type="button" class="library-api-diff-change-target" id="library-api-diff-change-target">Change target</button>
    </header>
    <p class="library-api-diff-status" role="status">${escapeHtml(status)}</p>
    ${content}
  </section>`;
}

export function renderLibraryApiDiff(
  state: LibraryApiDiffState,
  escapeHtml: (value: unknown) => string,
): string {
  if (state.status === "idle") {
    return renderFrame(
      null,
      "Choose a Gallery Package Library to compare.",
      "",
      escapeHtml,
    );
  }
  if (state.status === "target-loading"
    || state.status === "target-unavailable") {
    return renderFrame(
      null,
      state.message,
      state.status === "target-loading"
        ? '<div class="library-api-diff-loading" aria-hidden="true"></div>'
        : '<div class="library-api-diff-empty">Choose another Package Diff target to continue.</div>',
      escapeHtml,
    );
  }
  if (state.status === "loading") {
    return renderFrame(
      state.input,
      "Comparing complete public API surfaces...",
      '<div class="library-api-diff-loading" aria-hidden="true"></div>',
      escapeHtml,
    );
  }
  if (state.status === "failed") {
    return renderFrame(
      state.input,
      state.error,
      '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Retry comparison</button>',
      escapeHtml,
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
      const aggregate = value.aggregate;
      const metrics = [
        `${aggregate.changedTypeCount.toLocaleString()} changed ${
          aggregate.changedTypeCount === 1 ? "Type" : "Types"
        }`,
        compactCount(aggregate.changedMemberCount, "changed members"),
        compactCount(aggregate.breakingCount, "breaking"),
        compactCount(aggregate.additiveCount, "additive"),
        compactCount(
          aggregate.potentiallyBreakingCount,
          "potentially breaking",
        ),
      ].filter(Boolean);
      const content = value.types.length === 0
        ? `<div class="library-api-diff-empty">
            <strong>No public API changes</strong>
            <span>The selected Library is unchanged between these versions.</span>
          </div>`
        : `<div class="library-api-diff-metrics">${metrics.map(metric =>
            `<span>${escapeHtml(metric)}</span>`).join("")}</div>
          <ol class="library-api-diff-types">${value.types.map(type =>
            renderTypeRow(type, escapeHtml)).join("")}</ol>`;
      return renderFrame(
        input,
        value.types.length === 0
          ? "Comparison complete. No changed Types."
          : `Comparison complete. ${aggregate.changedTypeCount.toLocaleString()} changed Types.`,
        content + diagnosticHtml,
        escapeHtml,
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
      return renderFrame(
        input,
        `Comparison unavailable: ${String(unavailable.kind)}.`,
        (evidence
          || '<div class="library-api-diff-empty">One or both API surfaces are incomplete.</div>')
          + diagnosticHtml,
        escapeHtml,
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
      return renderFrame(
        input,
        `Comparison rejected: ${String(rejected.kind)}.`,
        `<div class="library-api-diff-empty">${escapeHtml(
          `The complete result could not be admitted.${bound}`,
        )}</div>${diagnosticHtml}`,
        escapeHtml,
      );
    }
    case "Failed":
      return renderFrame(
        input,
        result.error ?? "Library API Diff failed.",
        '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Retry comparison</button>',
        escapeHtml,
      );
    case "Canceled":
      return renderFrame(
        input,
        "Comparison canceled.",
        '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Run comparison</button>',
        escapeHtml,
      );
    default:
      throw new Error("Unknown Library API Diff result kind.");
  }
}

export function bindLibraryApiDiff(
  root: ParentNode,
  actions: {
    readonly changeTarget: () => void;
    readonly retry: () => void;
  },
): void {
  root.querySelector("#library-api-diff-change-target")
    ?.addEventListener("click", actions.changeTarget);
  root.querySelector("#library-api-diff-retry")
    ?.addEventListener("click", actions.retry);
}
