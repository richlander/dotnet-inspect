import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
} from "./worker-runtime-protocol.ts";

const maximumEncodedResultBytes = 1_024 * 1_024;
const maximumEndpointBytes = 128 * 1_024;
const maximumEndpointLines = 1_024;
const maximumRelations = 2_048;
const maximumCoordinateOccurrences = 2_048;
const maximumChanges = 2_048;
const maximumInnerMappings = 1_024;
const maximumAnnotations = 512;
const maximumAnnotationTextBytes = 64 * 1_024;
const maximumAuxiliaryTextBytes = 16 * 1_024;

const resultKinds = ["Succeeded", "TooComplex", "Failed", "Canceled"] as const;
const capacityDimensions = [
  "RequestBytes",
  "RawBeforeBytes",
  "RawAfterBytes",
  "RawBeforeLines",
  "RawAfterLines",
  "Relations",
  "CoordinateOccurrences",
  "MappedChanges",
  "InnerMappings",
  "Annotations",
  "AnnotationTextBytes",
  "AuxiliaryTextBytes",
  "EncodedResultBytes",
] as const;
const endpointStates = [
  "Available",
  "Unavailable",
  "Failed",
  "NotFound",
  "Rejected",
] as const;
const comparisonStatuses = ["Compared", "Unavailable", "Failed"] as const;
const relationKinds = ["Addition", "Removal", "Correspondence"] as const;
const contentKinds = ["Unchanged", "Changed"] as const;
const placementKinds = ["Stable", "Moved"] as const;
const terminators = ["Unknown", "Present", "Absent"] as const;
const targetKinds = ["Change", "Line", "Span"] as const;
const sides = ["Before", "After", "Both"] as const;
const severities = ["Note", "Tip", "Important", "Warning", "Caution"] as const;
const failureKinds = ["Expected", "Unexpected"] as const;

type ResultKind = typeof resultKinds[number];
type CapacityDimension = typeof capacityDimensions[number];
type EndpointState = typeof endpointStates[number];
type ComparisonStatus = typeof comparisonStatuses[number];
type RelationKind = typeof relationKinds[number];
type ContentKind = typeof contentKinds[number];
type PlacementKind = typeof placementKinds[number];
type Terminator = typeof terminators[number];
type TargetKind = typeof targetKinds[number];
type Side = typeof sides[number];
type Severity = typeof severities[number];
type FailureKind = typeof failureKinds[number];

interface BrowserSourceComparisonRequest {
  readonly packageId: string;
  readonly beforeVersion: string;
  readonly afterVersion: string;
  readonly framework: string;
  readonly assembly: string;
  readonly typeIdentity: string;
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
}

export interface BrowserSourceComparisonEndpoint {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly assetPath: string;
  readonly moduleVersionId: string | null;
  readonly assemblyIdentity: string;
  readonly memberIdentity: string | null;
  readonly metadataToken: number | null;
  readonly state: EndpointState;
  readonly detail: string | null;
  readonly text: string | null;
  readonly browseUrl: string | null;
  readonly repositoryUrl: string | null;
  readonly revision: string | null;
}

interface BrowserSourceDiffCapacity {
  readonly dimension: CapacityDimension;
  readonly limit: number;
  readonly actual: number;
}

interface BrowserSourceDiffSequence {
  readonly label: string | null;
  readonly lines: ReadonlyArray<string>;
  readonly finalLineTerminator: Terminator;
}

interface BrowserSourceDiffRelation {
  readonly kind: RelationKind;
  readonly beforeCoordinates: ReadonlyArray<number>;
  readonly afterCoordinates: ReadonlyArray<number>;
  readonly content: ContentKind | null;
  readonly placement: PlacementKind | null;
}

interface BrowserSourceDiffStatistics {
  readonly added: number;
  readonly removed: number;
  readonly changedBefore: number;
  readonly changedAfter: number;
  readonly movedBefore: number;
  readonly movedAfter: number;
}

interface BrowserSourceDiffRange {
  readonly start: number;
  readonly count: number;
}

interface BrowserSourceDiffSpan {
  readonly line: number;
  readonly start: number;
  readonly count: number;
}

interface BrowserSourceDiffInnerMapping {
  readonly before: BrowserSourceDiffSpan;
  readonly after: BrowserSourceDiffSpan;
}

interface BrowserSourceDiffAnnotation {
  readonly text: string;
  readonly severity: Severity;
  readonly targetKind: TargetKind;
  readonly side: Side | null;
  readonly line: number | null;
  readonly span: BrowserSourceDiffSpan | null;
}

interface BrowserSourceDiffChange {
  readonly before: BrowserSourceDiffRange;
  readonly after: BrowserSourceDiffRange;
  readonly innerMappings: ReadonlyArray<BrowserSourceDiffInnerMapping>;
  readonly annotations: ReadonlyArray<BrowserSourceDiffAnnotation>;
}

interface BrowserSourceDiff {
  readonly version: 1;
  readonly before: BrowserSourceDiffSequence;
  readonly after: BrowserSourceDiffSequence;
  readonly relations: ReadonlyArray<BrowserSourceDiffRelation>;
  readonly statistics: BrowserSourceDiffStatistics;
  readonly changes: ReadonlyArray<BrowserSourceDiffChange>;
}

interface BrowserSourceComparison {
  readonly request: BrowserSourceComparisonRequest;
  readonly status: ComparisonStatus;
  readonly isExact: boolean;
  readonly before: BrowserSourceComparisonEndpoint;
  readonly after: BrowserSourceComparisonEndpoint;
  readonly diff: BrowserSourceDiff | null;
  readonly failure: string | null;
}

export interface BrowserSourceComparisonResult {
  readonly version: 1;
  readonly kind: ResultKind;
  readonly value: BrowserSourceComparison | null;
  readonly failureKind: FailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
  readonly capacity: BrowserSourceDiffCapacity | null;
}

interface DecodeBudget {
  coordinateOccurrences: number;
  innerMappings: number;
  annotations: number;
  annotationTextBytes: number;
}

class SourceDiffPayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

const utf8 = new TextEncoder();

function byteCount(value: string): number {
  return utf8.encode(value).byteLength;
}

function isUtf16Boundary(value: string, offset: number): boolean {
  if (offset < 0 || offset > value.length) return false;
  if (offset === 0 || offset === value.length) return true;
  const before = value.charCodeAt(offset - 1);
  const after = value.charCodeAt(offset);
  return !(before >= 0xD800 && before <= 0xDBFF
    && after >= 0xDC00 && after <= 0xDFFF);
}

function validateUtf16(value: string, path: string): void {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xD800 && code <= 0xDBFF) {
      const low = value.charCodeAt(index + 1);
      if (!(low >= 0xDC00 && low <= 0xDFFF)) {
        throw new SourceDiffPayloadError(
          `${path} contains malformed UTF-16.`);
      }
      index++;
    } else if (code >= 0xDC00 && code <= 0xDFFF) {
      throw new SourceDiffPayloadError(
        `${path} contains malformed UTF-16.`);
    }
  }
}

function reject(
  error: unknown,
): BoundedPayloadDecodeResult<never> {
  if (error instanceof SourceDiffPayloadError) {
    return {
      kind: "rejected",
      reason: error.reason,
      message: error.message,
      cause: error,
    };
  }
  return {
    kind: "rejected",
    reason: "invalid",
    message: error instanceof Error
      ? error.message
      : "Source diff payload validation failed.",
    cause: error,
  };
}

function record(
  value: unknown,
  fields: readonly string[],
  path: string,
): Record<string, unknown> {
  if (typeof value !== "object"
    || value === null
    || Array.isArray(value)
    || Object.getPrototypeOf(value) !== Object.prototype) {
    throw new SourceDiffPayloadError(`${path} must be an ordinary object.`);
  }
  const keys = Reflect.ownKeys(value);
  if (keys.length !== fields.length
    || !fields.every(field => keys.includes(field))) {
    throw new SourceDiffPayloadError(
      `${path} must contain exactly: ${fields.join(", ")}.`);
  }
  for (const field of fields) {
    const descriptor = Object.getOwnPropertyDescriptor(value, field);
    if (descriptor === undefined
      || !("value" in descriptor)
      || !descriptor.enumerable) {
      throw new SourceDiffPayloadError(
        `${path}.${field} must be an enumerable data property.`);
    }
  }
  // Exact plain-object validation establishes the indexable record shape.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as Record<string, unknown>;
}

function array(
  value: unknown,
  maximum: number,
  path: string,
): readonly unknown[] {
  if (!Array.isArray(value))
    throw new SourceDiffPayloadError(`${path} must be an array.`);
  if (Object.getPrototypeOf(value) !== Array.prototype) {
    throw new SourceDiffPayloadError(
      `${path} must use the ordinary Array prototype.`);
  }
  if (value.length > maximum) {
    throw new SourceDiffPayloadError(
      `${path} exceeds ${maximum} items.`,
      "oversized",
    );
  }
  const keys = Reflect.ownKeys(value);
  if (keys.length !== value.length + 1
    || !keys.every(key =>
      key === "length"
      || (typeof key === "string"
        && /^\d+$/.test(key)
        && Number(key) < value.length))) {
    throw new SourceDiffPayloadError(`${path} must be a dense array.`);
  }
  return value;
}

function text(
  value: unknown,
  path: string,
  maximumBytes = maximumEncodedResultBytes,
): string {
  if (typeof value !== "string")
    throw new SourceDiffPayloadError(`${path} must be text.`);
  validateUtf16(value, path);
  if (byteCount(value) > maximumBytes) {
    throw new SourceDiffPayloadError(
      `${path} exceeds ${maximumBytes} UTF-8 bytes.`,
      "oversized",
    );
  }
  return value;
}

function nullableText(
  value: unknown,
  path: string,
  maximumBytes = maximumEncodedResultBytes,
): string | null {
  return value === null ? null : text(value, path, maximumBytes);
}

function integer(value: unknown, path: string): number {
  if (typeof value !== "number"
    || !Number.isSafeInteger(value)
    || value < 0) {
    throw new SourceDiffPayloadError(
      `${path} must be a non-negative safe integer.`);
  }
  return value;
}

function nullableInteger(value: unknown, path: string): number | null {
  return value === null ? null : integer(value, path);
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== "boolean")
    throw new SourceDiffPayloadError(`${path} must be Boolean.`);
  return value;
}

function enumeration<const T extends readonly string[]>(
  value: unknown,
  values: T,
  path: string,
): T[number] {
  const member = typeof value === "string"
    ? values.find(candidate => candidate === value)
    : undefined;
  if (member === undefined) {
    throw new SourceDiffPayloadError(
      `${path} must be one of: ${values.join(", ")}.`);
  }
  return member;
}

function nullableEnumeration<const T extends readonly string[]>(
  value: unknown,
  values: T,
  path: string,
): T[number] | null {
  return value === null ? null : enumeration(value, values, path);
}

function countPhysicalLines(value: string): number {
  if (value.length === 0) return 0;
  let lines = 1;
  for (let index = 0; index < value.length; index++) {
    if (value[index] !== "\r" && value[index] !== "\n") continue;
    lines++;
    if (value[index] === "\r" && value[index + 1] === "\n") index++;
  }
  return lines;
}

function endpoint(
  value: unknown,
  path: string,
): BrowserSourceComparisonEndpoint {
  const item = record(value, [
    "packageId", "version", "framework", "assembly", "assetPath",
    "moduleVersionId", "assemblyIdentity", "memberIdentity", "metadataToken",
    "state", "detail", "text", "browseUrl", "repositoryUrl", "revision",
  ], path);
  const source = nullableText(item.text, `${path}.text`, maximumEndpointBytes);
  if (source !== null && countPhysicalLines(source) > maximumEndpointLines) {
    throw new SourceDiffPayloadError(
      `${path}.text exceeds ${maximumEndpointLines} physical lines.`,
      "oversized",
    );
  }
  return {
    packageId: text(item.packageId, `${path}.packageId`),
    version: text(item.version, `${path}.version`),
    framework: text(item.framework, `${path}.framework`),
    assembly: text(item.assembly, `${path}.assembly`),
    assetPath: text(item.assetPath, `${path}.assetPath`),
    moduleVersionId: nullableText(item.moduleVersionId,
      `${path}.moduleVersionId`),
    assemblyIdentity: text(item.assemblyIdentity, `${path}.assemblyIdentity`),
    memberIdentity: nullableText(item.memberIdentity,
      `${path}.memberIdentity`),
    metadataToken: nullableInteger(item.metadataToken,
      `${path}.metadataToken`),
    state: enumeration(item.state, endpointStates, `${path}.state`),
    detail: nullableText(item.detail, `${path}.detail`),
    text: source,
    browseUrl: nullableText(item.browseUrl, `${path}.browseUrl`),
    repositoryUrl: nullableText(item.repositoryUrl, `${path}.repositoryUrl`),
    revision: nullableText(item.revision, `${path}.revision`),
  };
}

function request(
  value: unknown,
  path: string,
): BrowserSourceComparisonRequest {
  const item = record(value, [
    "packageId", "beforeVersion", "afterVersion", "framework", "assembly",
    "typeIdentity", "memberName", "selectorKey", "metadataToken",
  ], path);
  return {
    packageId: text(item.packageId, `${path}.packageId`),
    beforeVersion: text(item.beforeVersion, `${path}.beforeVersion`),
    afterVersion: text(item.afterVersion, `${path}.afterVersion`),
    framework: text(item.framework, `${path}.framework`),
    assembly: text(item.assembly, `${path}.assembly`),
    typeIdentity: text(item.typeIdentity, `${path}.typeIdentity`),
    memberName: text(item.memberName, `${path}.memberName`),
    selectorKey: text(item.selectorKey, `${path}.selectorKey`),
    metadataToken: integer(item.metadataToken, `${path}.metadataToken`),
  };
}

function sequence(
  value: unknown,
  path: string,
): BrowserSourceDiffSequence {
  const item = record(
    value, ["label", "lines", "finalLineTerminator"], path);
  const lines = array(item.lines, maximumEndpointLines, `${path}.lines`)
    .map((line, index) => {
      const linePath = `${path}.lines[${index}]`;
      const decoded = text(line, linePath, maximumEndpointBytes);
      if (/[\r\n]/.test(decoded)) {
        throw new SourceDiffPayloadError(
          `${linePath} must be one logical line.`);
      }
      return decoded;
    });
  const label = nullableText(item.label, `${path}.label`);
  if (label !== null && /[\r\n]/.test(label)) {
    throw new SourceDiffPayloadError(
      `${path}.label must be one logical line.`);
  }
  const finalLineTerminator = enumeration(
    item.finalLineTerminator, terminators, `${path}.finalLineTerminator`);
  if (lines.length === 0 && finalLineTerminator !== "Unknown") {
    throw new SourceDiffPayloadError(
      `${path} cannot assert termination for an empty sequence.`);
  }
  const joined = lines.join("\n")
    + (finalLineTerminator === "Present" ? "\n" : "");
  if (byteCount(joined) > maximumEndpointBytes) {
    throw new SourceDiffPayloadError(
      `${path}.lines exceed ${maximumEndpointBytes} UTF-8 bytes.`,
      "oversized",
    );
  }
  return {
    label,
    lines,
    finalLineTerminator,
  };
}

function coordinates(
  value: unknown,
  lineCount: number,
  budget: DecodeBudget,
  path: string,
): readonly number[] {
  const values = array(value, maximumCoordinateOccurrences, path)
    .map((coordinate, index) => integer(coordinate, `${path}[${index}]`));
  budget.coordinateOccurrences += values.length;
  if (budget.coordinateOccurrences > maximumCoordinateOccurrences) {
    throw new SourceDiffPayloadError(
      `Source diff exceeds ${maximumCoordinateOccurrences} coordinate occurrences.`,
      "oversized",
    );
  }
  if (values.some(coordinate => coordinate >= lineCount)) {
    throw new SourceDiffPayloadError(
      `${path} contains an out-of-range line coordinate.`);
  }
  return values;
}

function relation(
  value: unknown,
  beforeLines: number,
  afterLines: number,
  budget: DecodeBudget,
  path: string,
): BrowserSourceDiffRelation {
  const item = record(value, [
    "kind", "beforeCoordinates", "afterCoordinates", "content", "placement",
  ], path);
  const kind = enumeration(item.kind, relationKinds, `${path}.kind`);
  const beforeCoordinates = coordinates(
    item.beforeCoordinates, beforeLines, budget, `${path}.beforeCoordinates`);
  const afterCoordinates = coordinates(
    item.afterCoordinates, afterLines, budget, `${path}.afterCoordinates`);
  const content = nullableEnumeration(
    item.content, contentKinds, `${path}.content`);
  const placement = nullableEnumeration(
    item.placement, placementKinds, `${path}.placement`);
  const valid = kind === "Addition"
    ? beforeCoordinates.length === 0
      && afterCoordinates.length === 1
      && content === null
      && placement === null
    : kind === "Removal"
      ? beforeCoordinates.length === 1
        && afterCoordinates.length === 0
        && content === null
        && placement === null
      : beforeCoordinates.length > 0
        && afterCoordinates.length > 0
        && content !== null
        && placement !== null;
  if (!valid) {
    throw new SourceDiffPayloadError(
      `${path} is inconsistent with relation kind ${kind}.`);
  }
  return { kind, beforeCoordinates, afterCoordinates, content, placement };
}

function statistics(
  value: unknown,
  path: string,
): BrowserSourceDiffStatistics {
  const item = record(value, [
    "added", "removed", "changedBefore", "changedAfter",
    "movedBefore", "movedAfter",
  ], path);
  return {
    added: integer(item.added, `${path}.added`),
    removed: integer(item.removed, `${path}.removed`),
    changedBefore: integer(item.changedBefore, `${path}.changedBefore`),
    changedAfter: integer(item.changedAfter, `${path}.changedAfter`),
    movedBefore: integer(item.movedBefore, `${path}.movedBefore`),
    movedAfter: integer(item.movedAfter, `${path}.movedAfter`),
  };
}

function range(
  value: unknown,
  lineCount: number,
  path: string,
): BrowserSourceDiffRange {
  const item = record(value, ["start", "count"], path);
  const start = integer(item.start, `${path}.start`);
  const count = integer(item.count, `${path}.count`);
  if (start > lineCount || count > lineCount - start) {
    throw new SourceDiffPayloadError(
      `${path} exceeds its sequence bounds.`);
  }
  return { start, count };
}

function span(
  value: unknown,
  lines: readonly string[],
  path: string,
): BrowserSourceDiffSpan {
  const item = record(value, ["line", "start", "count"], path);
  const line = integer(item.line, `${path}.line`);
  const start = integer(item.start, `${path}.start`);
  const count = integer(item.count, `${path}.count`);
  if (line >= lines.length
    || start > lines[line]!.length
    || count > lines[line]!.length - start
    || !isUtf16Boundary(lines[line]!, start)
    || !isUtf16Boundary(lines[line]!, start + count)) {
    throw new SourceDiffPayloadError(
      `${path} exceeds its sequence bounds.`);
  }
  return { line, start, count };
}

function mapping(
  value: unknown,
  beforeLines: readonly string[],
  afterLines: readonly string[],
  budget: DecodeBudget,
  path: string,
): BrowserSourceDiffInnerMapping {
  const item = record(value, ["before", "after"], path);
  budget.innerMappings++;
  if (budget.innerMappings > maximumInnerMappings) {
    throw new SourceDiffPayloadError(
      `Source diff exceeds ${maximumInnerMappings} inner mappings.`,
      "oversized",
    );
  }
  const result = {
    before: span(item.before, beforeLines, `${path}.before`),
    after: span(item.after, afterLines, `${path}.after`),
  };
  if (result.before.count === 0 && result.after.count === 0) {
    throw new SourceDiffPayloadError(
      `${path} cannot contain two empty spans.`);
  }
  return result;
}

function annotation(
  value: unknown,
  beforeLines: readonly string[],
  afterLines: readonly string[],
  budget: DecodeBudget,
  path: string,
): BrowserSourceDiffAnnotation {
  const item = record(value, [
    "text", "severity", "targetKind", "side", "line", "span",
  ], path);
  budget.annotations++;
  if (budget.annotations > maximumAnnotations) {
    throw new SourceDiffPayloadError(
      `Source diff exceeds ${maximumAnnotations} annotations.`,
      "oversized",
    );
  }
  const annotationText = text(item.text, `${path}.text`,
    maximumAnnotationTextBytes);
  if (annotationText.length === 0) {
    throw new SourceDiffPayloadError(
      `${path}.text cannot be empty.`);
  }
  budget.annotationTextBytes += byteCount(annotationText);
  if (budget.annotationTextBytes > maximumAnnotationTextBytes) {
    throw new SourceDiffPayloadError(
      `Source diff exceeds ${maximumAnnotationTextBytes} annotation text bytes.`,
      "oversized",
    );
  }
  const targetKind = enumeration(
    item.targetKind, targetKinds, `${path}.targetKind`);
  const side = nullableEnumeration(item.side, sides, `${path}.side`);
  const line = nullableInteger(item.line, `${path}.line`);
  const targetLines = side === "Before"
    ? beforeLines
    : side === "After"
      ? afterLines
      : null;
  const targetSpan = item.span === null
    ? null
    : targetLines === null
      ? (() => {
          throw new SourceDiffPayloadError(
            `${path}.span requires a single target side.`);
        })()
      : span(item.span, targetLines, `${path}.span`);
  const valid = targetKind === "Change"
    ? side === null && line === null && targetSpan === null
    : targetKind === "Line"
      ? targetLines !== null
        && line !== null
        && line < targetLines.length
        && targetSpan === null
      : targetLines !== null && line === null && targetSpan !== null;
  if (!valid) {
    throw new SourceDiffPayloadError(
      `${path} is inconsistent with annotation target ${targetKind}.`);
  }
  return {
    text: annotationText,
    severity: enumeration(item.severity, severities, `${path}.severity`),
    targetKind,
    side,
    line,
    span: targetSpan,
  };
}

function change(
  value: unknown,
  beforeLines: readonly string[],
  afterLines: readonly string[],
  budget: DecodeBudget,
  path: string,
): BrowserSourceDiffChange {
  const item = record(value, [
    "before", "after", "innerMappings", "annotations",
  ], path);
  const before = range(item.before, beforeLines.length, `${path}.before`);
  const after = range(item.after, afterLines.length, `${path}.after`);
  if (before.count === 0 && after.count === 0) {
    throw new SourceDiffPayloadError(
      `${path} cannot contain two empty ranges.`);
  }
  return {
    before,
    after,
    innerMappings: array(
      item.innerMappings, maximumInnerMappings, `${path}.innerMappings`)
      .map((candidate, index) => mapping(
        candidate,
        beforeLines,
        afterLines,
        budget,
        `${path}.innerMappings[${index}]`,
      )),
    annotations: array(
      item.annotations, maximumAnnotations, `${path}.annotations`)
      .map((candidate, index) => annotation(
        candidate,
        beforeLines,
        afterLines,
        budget,
        `${path}.annotations[${index}]`,
      )),
  };
}

function diff(value: unknown, path: string): BrowserSourceDiff {
  const item = record(value, [
    "version", "before", "after", "relations", "statistics", "changes",
  ], path);
  if (integer(item.version, `${path}.version`) !== 1)
    throw new SourceDiffPayloadError(`${path}.version must be 1.`);
  const before = sequence(item.before, `${path}.before`);
  const after = sequence(item.after, `${path}.after`);
  const budget: DecodeBudget = {
    coordinateOccurrences: 0,
    innerMappings: 0,
    annotations: 0,
    annotationTextBytes: 0,
  };
  return {
    version: 1,
    before,
    after,
    relations: array(item.relations, maximumRelations, `${path}.relations`)
      .map((candidate, index) => relation(
        candidate,
        before.lines.length,
        after.lines.length,
        budget,
        `${path}.relations[${index}]`,
      )),
    statistics: statistics(item.statistics, `${path}.statistics`),
    changes: array(item.changes, maximumChanges, `${path}.changes`)
      .map((candidate, index) => change(
        candidate,
        before.lines,
        after.lines,
        budget,
        `${path}.changes[${index}]`,
      )),
  };
}

function auxiliaryBytes(
  requestValue: BrowserSourceComparisonRequest,
  status: ComparisonStatus,
  before: BrowserSourceComparisonEndpoint,
  after: BrowserSourceComparisonEndpoint,
  sourceDiff: BrowserSourceDiff | null,
  failure: string | null,
): number {
  return [
    requestValue.packageId,
    requestValue.beforeVersion,
    requestValue.afterVersion,
    requestValue.framework,
    requestValue.assembly,
    requestValue.typeIdentity,
    requestValue.memberName,
    requestValue.selectorKey,
    status,
    failure,
    sourceDiff?.before.label ?? null,
    sourceDiff?.after.label ?? null,
    before.packageId,
    before.version,
    before.framework,
    before.assembly,
    before.assetPath,
    before.moduleVersionId,
    before.assemblyIdentity,
    before.memberIdentity,
    before.state,
    before.detail,
    before.browseUrl,
    before.repositoryUrl,
    before.revision,
    after.packageId,
    after.version,
    after.framework,
    after.assembly,
    after.assetPath,
    after.moduleVersionId,
    after.assemblyIdentity,
    after.memberIdentity,
    after.state,
    after.detail,
    after.browseUrl,
    after.repositoryUrl,
    after.revision,
  ].reduce((total, candidate) =>
    total + (candidate === null ? 0 : byteCount(candidate)), 0);
}

function comparison(
  value: unknown,
  path: string,
): BrowserSourceComparison {
  const item = record(value, [
    "request", "status", "isExact", "before", "after", "diff", "failure",
  ], path);
  const requestValue = request(item.request, `${path}.request`);
  const before = endpoint(item.before, `${path}.before`);
  const after = endpoint(item.after, `${path}.after`);
  const sourceDiff = item.diff === null ? null : diff(item.diff, `${path}.diff`);
  const failure = nullableText(item.failure, `${path}.failure`);
  const status = enumeration(item.status, comparisonStatuses, `${path}.status`);
  const isExact = boolean(item.isExact, `${path}.isExact`);
  if ((status === "Compared") !== (sourceDiff !== null)
    || (sourceDiff !== null && (before.text !== null || after.text !== null))
    || isExact && status !== "Compared") {
    throw new SourceDiffPayloadError(
      `${path} has inconsistent status, diff, endpoint text, or exactness.`);
  }
  return {
    request: requestValue,
    status,
    isExact,
    before,
    after,
    diff: sourceDiff,
    failure,
  };
}

function capacity(
  value: unknown,
  path: string,
): BrowserSourceDiffCapacity {
  const item = record(value, ["dimension", "limit", "actual"], path);
  const limit = integer(item.limit, `${path}.limit`);
  const actual = integer(item.actual, `${path}.actual`);
  if (actual <= limit) {
    throw new SourceDiffPayloadError(
      `${path}.actual must exceed its limit.`);
  }
  return {
    dimension: enumeration(
      item.dimension, capacityDimensions, `${path}.dimension`),
    limit,
    actual,
  };
}

function decodeResult(value: unknown): BrowserSourceComparisonResult {
  const item = record(value, [
    "version", "kind", "value", "failureKind", "error",
    "diagnostic", "reason", "capacity",
  ], "Source diff result");
  if (integer(item.version, "Source diff result.version") !== 1)
    throw new SourceDiffPayloadError("Source diff result.version must be 1.");
  const kind = enumeration(item.kind, resultKinds, "Source diff result.kind");
  const resultValue = item.value === null
    ? null
    : comparison(item.value, "Source diff result.value");
  const failureKind = nullableEnumeration(
    item.failureKind, failureKinds, "Source diff result.failureKind");
  const error = nullableText(item.error, "Source diff result.error");
  const diagnostic = nullableText(
    item.diagnostic, "Source diff result.diagnostic");
  const reason = nullableText(item.reason, "Source diff result.reason");
  const resultCapacity = item.capacity === null
    ? null
    : capacity(item.capacity, "Source diff result.capacity");
  const aggregateAuxiliaryBytes =
    (resultValue === null
      ? 0
      : auxiliaryBytes(
          resultValue.request,
          resultValue.status,
          resultValue.before,
          resultValue.after,
          resultValue.diff,
          resultValue.failure,
        ))
    + [error, diagnostic, reason].reduce((total, candidate) =>
      total + (candidate === null ? 0 : byteCount(candidate)), 0);
  if (aggregateAuxiliaryBytes > maximumAuxiliaryTextBytes) {
    throw new SourceDiffPayloadError(
      `Source diff result exceeds ${maximumAuxiliaryTextBytes} auxiliary text bytes.`,
      "oversized",
    );
  }
  const valid = kind === "Succeeded"
    ? resultValue !== null
      && failureKind === null
      && error === null
      && diagnostic === null
      && reason === null
      && resultCapacity === null
    : kind === "TooComplex"
      ? resultValue === null
        && failureKind === null
        && error === null
        && diagnostic === null
        && reason === null
        && resultCapacity !== null
      : kind === "Failed"
        ? resultValue === null
          && failureKind !== null
          && error !== null
          && reason === null
          && resultCapacity === null
        : resultValue === null
          && failureKind === null
          && error === null
          && diagnostic === null
          && reason !== null
          && resultCapacity === null;
  if (!valid) {
    throw new SourceDiffPayloadError(
      `Source diff result fields are inconsistent with kind ${kind}.`);
  }
  return {
    version: 1,
    kind,
    value: resultValue,
    failureKind,
    error,
    diagnostic,
    reason,
    capacity: resultCapacity,
  };
}

export const sourceDiffPayloadDecoder:
BoundedPayloadDecoder<BrowserSourceComparisonResult> = {
  decode(value) {
    try {
      if (typeof value !== "string") {
        throw new SourceDiffPayloadError(
          "Source diff result must be encoded JSON text.");
      }
      if (byteCount(value) > maximumEncodedResultBytes) {
        throw new SourceDiffPayloadError(
          `Source diff result exceeds ${maximumEncodedResultBytes} UTF-8 bytes.`,
          "oversized",
        );
      }
      return {
        kind: "decoded",
        value: decodeResult(JSON.parse(value) as unknown),
      };
    } catch (error: unknown) {
      return reject(error);
    }
  },
};
