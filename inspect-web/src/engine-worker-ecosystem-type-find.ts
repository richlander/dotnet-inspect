import type {
  BrowserEcosystemTypeFindAssemblyIdentity,
  BrowserEcosystemTypeFindCancellation,
  BrowserEcosystemTypeFindFinding,
  BrowserEcosystemTypeFindInspection,
  BrowserEcosystemTypeFindItemFailure,
  BrowserEcosystemTypeFindMembership,
  BrowserEcosystemTypeFindPattern,
  BrowserEcosystemTypeFindProvenance,
  BrowserEcosystemTypeFindRequest,
  BrowserEcosystemTypeFindResult,
  BrowserEcosystemTypeFindSettlement,
  BrowserEcosystemTypeFindSource,
} from "./facades/inspect-web-catalog.d.ts";
import { mapEngineWorkerBoundaryErrors } from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  WorkerOperationCatalog,
  WorkerOperationContext,
} from "./worker-runtime-realm.ts";
import {
  isWorkerOperationCancelReason,
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";

const engineWorkerEcosystemTypeFindKind = "ecosystem-type-find";
const maximumTextCharacters = 64 * 1024;
const maximumDiagnosticCharacters = 64 * 1024;
const maximumEcosystems = 64;
const maximumFindings = 10_000;

export interface EcosystemTypeFindRequest
  extends BrowserEcosystemTypeFindRequest {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
  readonly resultGeneration: number;
}

export interface EcosystemTypeFindProgress {
  readonly phase: "Indexed" | "Bounded" | "Workspace" | "Prefix";
  readonly source: string;
}

export interface EcosystemTypeFindActivation {
  readonly status:
    | "Available"
    | "Unavailable"
    | "Stale"
    | "Ambiguous"
    | "Refused"
    | "Failed";
  readonly action: string | null;
  readonly reason: string | null;
}

export interface EcosystemTypeFindItem {
  readonly identity: string;
  readonly finding: BrowserEcosystemTypeFindFinding;
  readonly activation: EcosystemTypeFindActivation;
}

export type EcosystemTypeFindEvent =
  | {
    readonly kind: "Progress";
    readonly progress: EcosystemTypeFindProgress;
    readonly finding: null;
    readonly itemFailure: null;
  }
  | {
    readonly kind: "Finding";
    readonly progress: null;
    readonly finding: EcosystemTypeFindItem;
    readonly itemFailure: null;
  }
  | {
    readonly kind: "ItemFailure";
    readonly progress: null;
    readonly finding: null;
    readonly itemFailure: BrowserEcosystemTypeFindItemFailure;
  };

export interface EngineWorkerEcosystemTypeFindFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

export interface EngineWorkerEcosystemTypeFindFacade {
  findEcosystemTypes(
    operationId: string,
    retainedDefinitionId: string,
    realizationId: string,
    resultGeneration: number,
    requestJson: BrowserEcosystemTypeFindRequest,
    eventSink: unknown,
  ): Promise<BrowserEcosystemTypeFindResult>;
  cancelEcosystemTypeFind(
    operationId: string,
    reason: string,
  ): BrowserEcosystemTypeFindCancellation;
}

type EcosystemTypeFindSettlement = ManagedOperationSettlement<
  BrowserEcosystemTypeFindInspection,
  EngineWorkerEcosystemTypeFindFailure,
  string
>;

class PayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

function rejected<T = never>(
  error: unknown,
): BoundedPayloadDecodeResult<T> {
  if (error instanceof PayloadError) {
    return {
      kind: "rejected",
      reason: error.reason,
      message: error.message,
    };
  }
  return {
    kind: "rejected",
    reason: "invalid",
    message: error instanceof Error
      ? error.message
      : "Ecosystem Type Find payload validation failed.",
  };
}

function decoder<T>(
  project: (value: unknown) => T,
): BoundedPayloadDecoder<T> {
  return {
    decode(value) {
      try {
        return { kind: "decoded", value: project(value) };
      } catch (error: unknown) {
        return rejected(error);
      }
    },
  };
}

function dataRecord(
  value: unknown,
  label: string,
  names: readonly string[],
): object {
  if (typeof value !== "object"
    || value === null
    || Array.isArray(value)) {
    throw new PayloadError(`Expected ${label}.`);
  }
  const keys = Reflect.ownKeys(value);
  if (keys.length !== names.length
    || !names.every(name => {
      const property = Object.getOwnPropertyDescriptor(value, name);
      return property !== undefined && "value" in property;
    })) {
    throw new PayloadError(`Expected exact ${label} fields.`);
  }
  return value;
}

function ownData(candidate: object, name: string): unknown {
  const property = Object.getOwnPropertyDescriptor(candidate, name);
  return property !== undefined && "value" in property
    ? property.value
    : undefined;
}

function text(
  value: unknown,
  label: string,
  maximum = maximumDiagnosticCharacters,
): string {
  if (typeof value !== "string")
    throw new PayloadError(`Expected ${label} text.`);
  if (value.length > maximum) {
    throw new PayloadError(
      `${label} text exceeds ${maximum} characters.`,
      "oversized",
    );
  }
  return value;
}

function nullableText(value: unknown, label: string): string | null {
  return value === null ? null : text(value, label);
}

function positiveInt(value: unknown, label: string): number {
  if (typeof value !== "number"
    || !Number.isInteger(value)
    || value <= 0
    || value > 0x7fffffff) {
    throw new PayloadError(`${label} is invalid.`);
  }
  return value;
}

function nonnegativeInt(value: unknown, label: string): number {
  if (typeof value !== "number"
    || !Number.isInteger(value)
    || value < 0
    || value > 0x7fffffff) {
    throw new PayloadError(`${label} is invalid.`);
  }
  return value;
}

function nullablePositiveInt(
  value: unknown,
  label: string,
): number | null {
  return value === null ? null : positiveInt(value, label);
}

function finiteNumber(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value))
    throw new PayloadError(`${label} is invalid.`);
  return value;
}

function booleanValue(value: unknown, label: string): boolean {
  if (typeof value !== "boolean")
    throw new PayloadError(`${label} is invalid.`);
  return value;
}

function nullableBoolean(
  value: unknown,
  label: string,
): boolean | null {
  return value === null ? null : booleanValue(value, label);
}

function literal<T extends string>(
  value: unknown,
  label: string,
  allowed: readonly T[],
): T {
  if (typeof value === "string") {
    for (const candidate of allowed) {
      if (value === candidate) return candidate;
    }
  }
  throw new PayloadError(`${label} is invalid.`);
}

function arrayValue<T>(
  value: unknown,
  label: string,
  maximum: number,
  project: (item: unknown, index: number) => T,
): T[] {
  if (!Array.isArray(value))
    throw new PayloadError(`Expected ${label} array.`);
  if (value.length > maximum) {
    throw new PayloadError(
      `${label} exceeds ${maximum} items.`,
      "oversized",
    );
  }
  return value.map(project);
}

function textArray(
  value: unknown,
  label: string,
  maximum: number,
): string[] {
  return arrayValue(
    value,
    label,
    maximum,
    (item, index) => text(item, `${label} item ${index + 1}`),
  );
}

function uuid(value: unknown, label: string): string {
  const candidate = text(value, label);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu
    .test(candidate)) {
    throw new PayloadError(`${label} is invalid.`);
  }
  return candidate;
}

export const engineWorkerEcosystemTypeFindInput:
BoundedPayloadDecoder<EcosystemTypeFindRequest> = decoder(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find request",
    [
      "retainedDefinitionId",
      "realizationId",
      "resultGeneration",
      "text",
      "ecosystems",
      "prefixDemand",
      "rowStart",
      "rowEnd",
      "maximumPrefixPackages",
      "targetFramework",
    ],
  );
  const ecosystems = textArray(
    ownData(candidate, "ecosystems"),
    "Ecosystem Type Find ecosystems",
    maximumEcosystems,
  );
  if (ecosystems.length === 0
    || new Set(ecosystems).size !== ecosystems.length) {
    throw new PayloadError(
      "Ecosystem Type Find requires distinct selected Ecosystems.",
    );
  }
  const prefixDemand = textArray(
    ownData(candidate, "prefixDemand"),
    "Ecosystem Type Find prefix demand",
    maximumEcosystems,
  );
  const selected = new Set(ecosystems);
  if (new Set(prefixDemand).size !== prefixDemand.length
    || prefixDemand.some(ecosystem => !selected.has(ecosystem))) {
    throw new PayloadError(
      "Ecosystem Type Find prefix demand must be a selected subset.",
    );
  }
  const rowStart = positiveInt(
    ownData(candidate, "rowStart"),
    "Ecosystem Type Find row start",
  );
  const rowEnd = positiveInt(
    ownData(candidate, "rowEnd"),
    "Ecosystem Type Find row end",
  );
  if (rowEnd < rowStart)
    throw new PayloadError("Ecosystem Type Find row range is invalid.");
  if (rowEnd > maximumFindings) {
    throw new PayloadError(
      `Ecosystem Type Find row end exceeds ${maximumFindings}.`,
      "oversized",
    );
  }
  const maximumPrefixPackages = positiveInt(
    ownData(candidate, "maximumPrefixPackages"),
    "Ecosystem Type Find maximum prefix packages",
  );
  if (maximumPrefixPackages > 500) {
    throw new PayloadError(
      "Ecosystem Type Find maximum prefix packages exceeds 500.",
    );
  }
  return {
    retainedDefinitionId: text(
      ownData(candidate, "retainedDefinitionId"),
      "Ecosystem Type Find retained definition",
    ),
    realizationId: text(
      ownData(candidate, "realizationId"),
      "Ecosystem Type Find realization",
    ),
    resultGeneration: positiveInt(
      ownData(candidate, "resultGeneration"),
      "Ecosystem Type Find result generation",
    ),
    text: text(
      ownData(candidate, "text"),
      "Ecosystem Type Find query",
      maximumTextCharacters,
    ),
    ecosystems,
    prefixDemand,
    rowStart,
    rowEnd,
    maximumPrefixPackages,
    targetFramework: nullableText(
      ownData(candidate, "targetFramework"),
      "Ecosystem Type Find target framework",
    ),
  };
});

const assemblyIdentity = decoder<BrowserEcosystemTypeFindAssemblyIdentity>(
  value => {
    const candidate = dataRecord(
      value,
      "Ecosystem Type Find assembly identity",
      ["name", "version", "culture", "publicKeyToken"],
    );
    return {
      name: text(ownData(candidate, "name"), "assembly name"),
      version: text(ownData(candidate, "version"), "assembly version"),
      culture: nullableText(
        ownData(candidate, "culture"),
        "assembly culture",
      ),
      publicKeyToken: nullableText(
        ownData(candidate, "publicKeyToken"),
        "assembly public key token",
      ),
    };
  },
);

const provenance = decoder<BrowserEcosystemTypeFindProvenance>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find provenance",
    [
      "kind",
      "packageId",
      "packageVersion",
      "targetFramework",
      "runtimeIdentifier",
      "assetPath",
      "framework",
      "frameworkVersion",
      "project",
      "resolverSource",
      "contentReference",
      "digest",
      "declaredName",
    ],
  );
  return {
    kind: literal(
      ownData(candidate, "kind"),
      "Ecosystem Type Find provenance kind",
      ["Package", "Platform", "Project", "Local", "Designated", "Embedded"],
    ),
    packageId: nullableText(ownData(candidate, "packageId"), "package ID"),
    packageVersion: nullableText(
      ownData(candidate, "packageVersion"),
      "package version",
    ),
    targetFramework: nullableText(
      ownData(candidate, "targetFramework"),
      "provenance target framework",
    ),
    runtimeIdentifier: nullableText(
      ownData(candidate, "runtimeIdentifier"),
      "provenance runtime identifier",
    ),
    assetPath: nullableText(ownData(candidate, "assetPath"), "asset path"),
    framework: nullableText(ownData(candidate, "framework"), "framework"),
    frameworkVersion: nullableText(
      ownData(candidate, "frameworkVersion"),
      "framework version",
    ),
    project: nullableText(ownData(candidate, "project"), "project"),
    resolverSource: nullableText(
      ownData(candidate, "resolverSource"),
      "resolver source",
    ),
    contentReference: nullableText(
      ownData(candidate, "contentReference"),
      "content reference",
    ),
    digest: nullableText(ownData(candidate, "digest"), "content digest"),
    declaredName: nullableText(
      ownData(candidate, "declaredName"),
      "declared name",
    ),
  };
});

const membership = decoder<BrowserEcosystemTypeFindMembership>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find membership",
    [
      "ecosystem",
      "basis",
      "platformFamily",
      "packageId",
      "packageVersion",
      "packageFramework",
      "packageRuntimeIdentifier",
      "packagePrefix",
    ],
  );
  return {
    ecosystem: text(ownData(candidate, "ecosystem"), "membership Ecosystem"),
    basis: literal(
      ownData(candidate, "basis"),
      "membership basis",
      ["PlatformPopulation", "CorePackage", "PackagePrefix"],
    ),
    platformFamily: nullableText(
      ownData(candidate, "platformFamily"),
      "membership platform family",
    ),
    packageId: nullableText(
      ownData(candidate, "packageId"),
      "membership package ID",
    ),
    packageVersion: nullableText(
      ownData(candidate, "packageVersion"),
      "membership package version",
    ),
    packageFramework: nullableText(
      ownData(candidate, "packageFramework"),
      "membership package framework",
    ),
    packageRuntimeIdentifier: nullableText(
      ownData(candidate, "packageRuntimeIdentifier"),
      "membership package runtime identifier",
    ),
    packagePrefix: nullableText(
      ownData(candidate, "packagePrefix"),
      "membership package prefix",
    ),
  };
});

const pattern = decoder<BrowserEcosystemTypeFindPattern>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find pattern",
    [
      "ordinal",
      "text",
      "kind",
      "exactNamespaceFallback",
      "namespace",
      "namespaceMatch",
    ],
  );
  const kind = literal(
    ownData(candidate, "kind"),
    "pattern kind",
    ["Ordinary", "Glob", "Namespace"],
  );
  const exactNamespaceFallback = nullableText(
    ownData(candidate, "exactNamespaceFallback"),
    "exact namespace fallback",
  );
  const namespace = nullableText(
    ownData(candidate, "namespace"),
    "pattern namespace",
  );
  const namespaceMatch = ownData(candidate, "namespaceMatch") === null
    ? null
    : literal(
      ownData(candidate, "namespaceMatch"),
      "pattern namespace match",
      ["Exact", "Suffix", "ExactOrDescendant"],
    );
  if ((kind === "Ordinary"
      && (namespace !== null || namespaceMatch !== null))
    || (kind === "Glob"
      && (exactNamespaceFallback !== null
        || namespace !== null
        || namespaceMatch !== null))
    || (kind === "Namespace"
      && (exactNamespaceFallback !== null
        || namespace === null
        || namespaceMatch === null))) {
    throw new PayloadError("Ecosystem Type Find pattern union is malformed.");
  }
  return {
    ordinal: nonnegativeInt(
      ownData(candidate, "ordinal"),
      "pattern ordinal",
    ),
    text: text(ownData(candidate, "text"), "pattern"),
    kind,
    exactNamespaceFallback,
    namespace,
    namespaceMatch,
  };
});

const source = decoder<BrowserEcosystemTypeFindSource>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find source",
    [
      "kind",
      "label",
      "packageId",
      "packageVersion",
      "platformFamily",
      "library",
      "provenance",
      "requestedTargetFramework",
      "requestedRuntimeIdentifier",
      "contextOrder",
      "memberOrder",
    ],
  );
  const library = assemblyIdentity.decode(ownData(candidate, "library"));
  if (library.kind === "rejected") throw new PayloadError(library.message);
  const acquired = provenance.decode(ownData(candidate, "provenance"));
  if (acquired.kind === "rejected")
    throw new PayloadError(acquired.message);
  return {
    kind: literal(
      ownData(candidate, "kind"),
      "source kind",
      ["Package", "Platform", "Project", "Local"],
    ),
    label: text(ownData(candidate, "label"), "source label"),
    packageId: nullableText(
      ownData(candidate, "packageId"),
      "source package ID",
    ),
    packageVersion: nullableText(
      ownData(candidate, "packageVersion"),
      "source package version",
    ),
    platformFamily: nullableText(
      ownData(candidate, "platformFamily"),
      "source platform family",
    ),
    library: library.value,
    provenance: acquired.value,
    requestedTargetFramework: nullableText(
      ownData(candidate, "requestedTargetFramework"),
      "requested target framework",
    ),
    requestedRuntimeIdentifier: nullableText(
      ownData(candidate, "requestedRuntimeIdentifier"),
      "requested runtime identifier",
    ),
    contextOrder: nonnegativeInt(
      ownData(candidate, "contextOrder"),
      "source context order",
    ),
    memberOrder: nonnegativeInt(
      ownData(candidate, "memberOrder"),
      "source member order",
    ),
  };
});

const finding = decoder<BrowserEcosystemTypeFindFinding>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find finding",
    [
      "identity",
      "scope",
      "memberships",
      "pattern",
      "effectivePattern",
      "matchKind",
      "similarity",
      "name",
      "namespace",
      "fullName",
      "source",
      "moduleVersionId",
      "declarationOrder",
      "declarationKind",
      "definitionKind",
      "isEditorBrowsableNever",
      "isObsolete",
    ],
  );
  const projectedPattern = pattern.decode(ownData(candidate, "pattern"));
  if (projectedPattern.kind === "rejected")
    throw new PayloadError(projectedPattern.message);
  const projectedSource = source.decode(ownData(candidate, "source"));
  if (projectedSource.kind === "rejected")
    throw new PayloadError(projectedSource.message);
  const memberships = arrayValue(
    ownData(candidate, "memberships"),
    "Ecosystem Type Find memberships",
    maximumEcosystems,
    item => {
      const decoded = membership.decode(item);
      if (decoded.kind === "rejected")
        throw new PayloadError(decoded.message);
      return decoded.value;
    },
  );
  const scope = literal(
    ownData(candidate, "scope"),
    "finding scope",
    ["Workspace", "Ecosystem"],
  );
  if ((scope === "Workspace" && memberships.length !== 0)
    || (scope === "Ecosystem" && memberships.length === 0)) {
    throw new PayloadError(
      "Ecosystem Type Find finding scope and memberships disagree.",
    );
  }
  return {
    identity: text(ownData(candidate, "identity"), "finding identity"),
    scope,
    memberships,
    pattern: projectedPattern.value,
    effectivePattern: text(
      ownData(candidate, "effectivePattern"),
      "effective pattern",
    ),
    matchKind: literal(
      ownData(candidate, "matchKind"),
      "match kind",
      ["Exact", "Direct", "Glob", "Namespace", "Prefix", "Substring", "Partial"],
    ),
    similarity: finiteNumber(
      ownData(candidate, "similarity"),
      "match similarity",
    ),
    name: text(ownData(candidate, "name"), "Type name"),
    namespace: text(ownData(candidate, "namespace"), "Type namespace"),
    fullName: text(ownData(candidate, "fullName"), "Type full name"),
    source: projectedSource.value,
    moduleVersionId: uuid(
      ownData(candidate, "moduleVersionId"),
      "module version identity",
    ),
    declarationOrder: nonnegativeInt(
      ownData(candidate, "declarationOrder"),
      "declaration order",
    ),
    declarationKind: literal(
      ownData(candidate, "declarationKind"),
      "declaration kind",
      ["Definition", "Forwarder", "ModuleExport"],
    ),
    definitionKind: ownData(candidate, "definitionKind") === null
      ? null
      : literal(
        ownData(candidate, "definitionKind"),
        "definition kind",
        ["Class", "Interface", "ValueType", "Enum", "Delegate"],
      ),
    isEditorBrowsableNever: nullableBoolean(
      ownData(candidate, "isEditorBrowsableNever"),
      "EditorBrowsable state",
    ),
    isObsolete: nullableBoolean(
      ownData(candidate, "isObsolete"),
      "obsolete state",
    ),
  };
});

const itemFailure = decoder<BrowserEcosystemTypeFindItemFailure>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find item failure",
    ["phase", "source", "message"],
  );
  return {
    phase: text(ownData(candidate, "phase"), "failure phase"),
    source: text(ownData(candidate, "source"), "failure source"),
    message: text(ownData(candidate, "message"), "failure message"),
  };
});

const settlement = decoder<BrowserEcosystemTypeFindSettlement>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find settlement",
    [
      "rowStart",
      "rowEnd",
      "findings",
      "completion",
      "failures",
      "unsearched",
      "incomplete",
    ],
  );
  const rowStart = nullablePositiveInt(
    ownData(candidate, "rowStart"),
    "settlement row start",
  );
  const rowEnd = nullablePositiveInt(
    ownData(candidate, "rowEnd"),
    "settlement row end",
  );
  if ((rowStart === null) !== (rowEnd === null)
    || (rowStart !== null && rowEnd !== null && rowEnd < rowStart)) {
    throw new PayloadError(
      "Ecosystem Type Find settlement row range is invalid.",
    );
  }
  return {
    rowStart,
    rowEnd,
    findings: arrayValue(
      ownData(candidate, "findings"),
      "Ecosystem Type Find findings",
      maximumFindings,
      item => {
        const decoded = finding.decode(item);
        if (decoded.kind === "rejected")
          throw new PayloadError(decoded.message);
        return decoded.value;
      },
    ),
    completion: literal(
      ownData(candidate, "completion"),
      "settlement completion",
      [
        "Exhausted",
        "RowLimitReached",
        "CandidateLimitReached",
        "SourcePageLimitReached",
        "ClientPageLimitReached",
        "Partial",
        "Failed",
      ],
    ),
    failures: arrayValue(
      ownData(candidate, "failures"),
      "Ecosystem Type Find failures",
      maximumFindings,
      item => {
        const decoded = itemFailure.decode(item);
        if (decoded.kind === "rejected")
          throw new PayloadError(decoded.message);
        return decoded.value;
      },
    ),
    unsearched: textArray(
      ownData(candidate, "unsearched"),
      "Ecosystem Type Find unsearched sources",
      maximumFindings,
    ),
    incomplete: booleanValue(
      ownData(candidate, "incomplete"),
      "settlement incomplete state",
    ),
  };
});

const inspection = decoder<BrowserEcosystemTypeFindInspection>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find inspection",
    ["requestIdentity", "settlement"],
  );
  const completed = settlement.decode(ownData(candidate, "settlement"));
  if (completed.kind === "rejected")
    throw new PayloadError(completed.message);
  return {
    requestIdentity: uuid(
      ownData(candidate, "requestIdentity"),
      "request identity",
    ),
    settlement: completed.value,
  };
});

const event = decoder<EcosystemTypeFindEvent>(value => {
  const candidate = dataRecord(
    value,
    "Ecosystem Type Find event",
    ["kind", "progress", "finding", "itemFailure"],
  );
  const kind = literal(
    ownData(candidate, "kind"),
    "event kind",
    ["Progress", "Finding", "ItemFailure"],
  );
  if (kind === "Progress") {
    const progress = dataRecord(
      ownData(candidate, "progress"),
      "Ecosystem Type Find progress",
      ["phase", "source"],
    );
    if (ownData(candidate, "finding") !== null
      || ownData(candidate, "itemFailure") !== null) {
      throw new PayloadError("Ecosystem Type Find progress is malformed.");
    }
    return {
      kind,
      progress: {
        phase: literal(
          ownData(progress, "phase"),
          "progress phase",
          ["Indexed", "Bounded", "Workspace", "Prefix"],
        ),
        source: text(ownData(progress, "source"), "progress source"),
      },
      finding: null,
      itemFailure: null,
    };
  }
  if (kind === "Finding") {
    const item = dataRecord(
      ownData(candidate, "finding"),
      "Ecosystem Type Find item",
      ["identity", "finding", "activation"],
    );
    const projected = finding.decode(ownData(item, "finding"));
    if (projected.kind === "rejected")
      throw new PayloadError(projected.message);
    const activation = dataRecord(
      ownData(item, "activation"),
      "Ecosystem Type Find activation",
      ["status", "action", "reason"],
    );
    const identity = text(ownData(item, "identity"), "item identity");
    if (identity !== projected.value.identity)
      throw new PayloadError("Ecosystem Type Find item identity changed.");
    if (ownData(candidate, "progress") !== null
      || ownData(candidate, "itemFailure") !== null) {
      throw new PayloadError("Ecosystem Type Find finding is malformed.");
    }
    return {
      kind,
      progress: null,
      finding: {
        identity,
        finding: projected.value,
        activation: {
          status: literal(
            ownData(activation, "status"),
            "activation status",
            [
              "Available",
              "Unavailable",
              "Stale",
              "Ambiguous",
              "Refused",
              "Failed",
            ],
          ),
          action: nullableText(
            ownData(activation, "action"),
            "activation action",
          ),
          reason: nullableText(
            ownData(activation, "reason"),
            "activation reason",
          ),
        },
      },
      itemFailure: null,
    };
  }
  const failure = itemFailure.decode(ownData(candidate, "itemFailure"));
  if (failure.kind === "rejected")
    throw new PayloadError(failure.message);
  if (ownData(candidate, "progress") !== null
    || ownData(candidate, "finding") !== null) {
    throw new PayloadError("Ecosystem Type Find item failure is malformed.");
  }
  return {
    kind,
    progress: null,
    finding: null,
    itemFailure: failure.value,
  };
});

const terminalFailure = decoder<EngineWorkerEcosystemTypeFindFailure>(
  value => {
    const candidate = dataRecord(
      value,
      "Ecosystem Type Find failure",
      ["failureKind", "error", "diagnostic"],
    );
    return {
      failureKind: literal(
        ownData(candidate, "failureKind"),
        "failure kind",
        ["Expected", "Unexpected"],
      ),
      error: text(ownData(candidate, "error"), "failure error"),
      diagnostic: text(
        ownData(candidate, "diagnostic"),
        "failure diagnostic",
      ),
    };
  },
);

const diagnostic = decoder<string>(
  value => text(value, "Ecosystem Type Find diagnostic"),
);

const noProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return {
      kind: "rejected",
      reason: "invalid",
      message: "Ecosystem Type Find uses ordered durable events.",
    };
  },
};

const boundaryErrors = mapEngineWorkerBoundaryErrors(message => ({
  failureKind: "Unexpected" as const,
  error: message,
  diagnostic: message,
}));

function invalidResultFailure(error: unknown): EcosystemTypeFindSettlement {
  const detail = error instanceof Error
    ? error.message.slice(0, maximumDiagnosticCharacters)
    : "Ecosystem Type Find result validation failed.";
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Ecosystem Type Find returned invalid Worker boundary data.",
      diagnostic: detail,
    },
    diagnostic: detail,
  };
}

export function mapEngineWorkerEcosystemTypeFindResult(
  result: BrowserEcosystemTypeFindResult,
): EcosystemTypeFindSettlement {
  try {
    const candidate = dataRecord(
      result,
      "Ecosystem Type Find result",
      ["kind", "inspection", "failureKind", "error", "diagnostic", "reason"],
    );
    const kind = literal(
      ownData(candidate, "kind"),
      "result kind",
      ["Succeeded", "Failed", "Canceled"],
    );
    if (kind === "Succeeded") {
      if (ownData(candidate, "failureKind") !== null
        || ownData(candidate, "error") !== null
        || ownData(candidate, "diagnostic") !== null
        || ownData(candidate, "reason") !== null) {
        throw new PayloadError("Ecosystem Type Find success is malformed.");
      }
      const value = inspection.decode(ownData(candidate, "inspection"));
      if (value.kind === "rejected") throw new PayloadError(value.message);
      return { kind: "succeeded", value: value.value };
    }
    if (kind === "Failed") {
      if (ownData(candidate, "inspection") !== null
        || ownData(candidate, "reason") !== null) {
        throw new PayloadError("Ecosystem Type Find failure is malformed.");
      }
      const failureKind = literal(
        ownData(candidate, "failureKind"),
        "result failure kind",
        ["Expected", "Unexpected"],
      );
      const error = text(ownData(candidate, "error"), "result error");
      const detail = text(
        ownData(candidate, "diagnostic"),
        "result diagnostic",
      );
      return {
        kind: "failed",
        failureKind: failureKind === "Expected"
          ? "expected"
          : "unexpected",
        error: { failureKind, error, diagnostic: detail },
        diagnostic: detail,
      };
    }
    if (ownData(candidate, "inspection") !== null
      || ownData(candidate, "failureKind") !== null
      || ownData(candidate, "error") !== null
      || ownData(candidate, "diagnostic") !== null) {
      throw new PayloadError("Ecosystem Type Find cancellation is malformed.");
    }
    const reason = text(
      ownData(candidate, "reason"),
      "cancellation reason",
    );
    if (!isWorkerOperationCancelReason(reason))
      throw new PayloadError("Ecosystem Type Find cancellation is invalid.");
    return { kind: "canceled", reason };
  } catch (error: unknown) {
    return invalidResultFailure(error);
  }
}

export function engineWorkerEcosystemTypeFindCancellationIsRunning(
  result: BrowserEcosystemTypeFindCancellation,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  const candidate = dataRecord(
    result,
    "Ecosystem Type Find cancellation",
    ["status", "reason"],
  );
  const status = literal(
    ownData(candidate, "status"),
    "cancellation status",
    ["Requested", "AlreadyRequested", "NotActive"],
  );
  if (status === "NotActive") {
    if (ownData(candidate, "reason") !== null) {
      throw new PayloadError(
        "Inactive Ecosystem Type Find cancellation has a reason.",
      );
    }
    return false;
  }
  const reason = text(
    ownData(candidate, "reason"),
    "cancellation reason",
  );
  if (!isWorkerOperationCancelReason(reason))
    throw new PayloadError("Ecosystem Type Find cancellation is invalid.");
  if (status === "Requested" && reason !== requestedReason) {
    throw new PayloadError(
      "Ecosystem Type Find cancellation changed the requested reason.",
    );
  }
  return true;
}

export function createEngineWorkerEcosystemTypeFindHostRegistration():
WorkerRuntimeOperationRegistration<
  EcosystemTypeFindRequest,
  BrowserEcosystemTypeFindInspection,
  EngineWorkerEcosystemTypeFindFailure,
  string,
  never,
  WorkerRuntimePreparationError,
  EcosystemTypeFindEvent
> {
  return {
    kind: engineWorkerEcosystemTypeFindKind,
    allowance: { kind: "unbounded" },
    encodeInput: engineWorkerEcosystemTypeFindInput.decode,
    value: inspection,
    error: terminalFailure,
    diagnostic,
    progress: noProgress,
    durable: event,
    mapPreparationError: error => error,
    boundaryErrors,
  };
}

function createEventSink(
  context: WorkerOperationContext,
): Record<string, unknown> {
  const eventSink: Record<string, unknown> = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      if (typeof value !== "string") {
        throw new Error(
          "Ecosystem Type Find callback payload was not JSON text.",
        );
      }
      const decoded = event.decode(JSON.parse(value));
      if (decoded.kind === "rejected") throw new Error(decoded.message);
      if (!context.reportEvents([{
        kind: "durable",
        payload: decoded.value,
      }])) {
        throw new Error(
          "Ecosystem Type Find Worker event publication is closed.",
        );
      }
    },
  });
  return eventSink;
}

export function registerEngineWorkerEcosystemTypeFindOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerEcosystemTypeFindFacade,
): void {
  operations.register({
    kind: engineWorkerEcosystemTypeFindKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerEcosystemTypeFindInput,
    rejectInvalidPayload: failure => ({
      error: {
        failureKind: "Unexpected",
        error: failure.message,
        diagnostic: failure.message,
      },
      diagnostic: failure.message,
    }),
    invoke: async (input, context) =>
      mapEngineWorkerEcosystemTypeFindResult(
        await facade().findEcosystemTypes(
          context.operation.operationId,
          input.retainedDefinitionId,
          input.realizationId,
          input.resultGeneration,
          {
            text: input.text,
            ecosystems: input.ecosystems,
            prefixDemand: input.prefixDemand,
            rowStart: input.rowStart,
            rowEnd: input.rowEnd,
            maximumPrefixPackages: input.maximumPrefixPackages,
            targetFramework: input.targetFramework,
          },
          createEventSink(context),
        ),
      ),
    cancel: (operation, reason) =>
      engineWorkerEcosystemTypeFindCancellationIsRunning(
        facade().cancelEcosystemTypeFind(operation.operationId, reason),
        reason,
      ),
  });
}
