import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "./facades/inspect-web-metadata.d.ts";
import type { CallGraphTarget } from "./data.ts";

interface CanonicalSubject {
  id: number;
  subject: {
    member: {
      member: {
        declaring_type: {
          assembly: string;
          kind: string;
          namespace: string;
          name: string;
          resolution: {
            origin: {
              kind: string;
              assembly?: {
                name: string;
                version?: string;
                culture?: string;
                public_key_token?: string;
              };
              module_name?: string;
            };
            type: {
              namespace: string;
              segments: string[];
            };
          };
        };
        name: string;
        parameter_types?: {
          namespace: string;
          name: string;
        }[];
      };
    };
  };
}

interface CanonicalRelation {
  id: number;
  source_subject_id: number;
  target_subject_id: number;
}

interface CanonicalOccurrence {
  relation_id: number;
  evidence: {
    kind: string;
    caller_method_token?: number;
    il_offset?: number;
    operand_token?: number;
    call_kind?: string;
  };
}

interface CanonicalAnnotation {
  descriptor: { id: string };
  target: { kind: string; id: number };
}

interface CanonicalFamilyOutcome {
  family: string;
  disposition: string;
  coverage: {
    considered: number;
    examined: number;
    excluded: number;
    unavailable: number;
    limited: number;
  };
}

interface CanonicalRelationsDocument {
  terminal: {
    kind: string;
    value: number | null;
    is_complete: boolean | null;
  };
  subjects: CanonicalSubject[];
  relations: CanonicalRelation[];
  occurrences: CanonicalOccurrence[];
  annotations: CanonicalAnnotation[];
  relationship_outcomes: CanonicalFamilyOutcome[];
  color_outcomes: CanonicalFamilyOutcome[];
}

export interface TypeRelationsRequest {
  signature: string;
  packageId: string;
  version: string;
  framework: string;
  runtimeIdentifier: string | null;
  assembly: string;
  typeDefinitionId: string;
  workspaceJson: string;
  isCurrent(): boolean;
  isVisible(): boolean;
}

export interface TypeRelationsState {
  signature: string;
  loading: boolean;
  result: BrowserAnnotatedTypeRelationsInspection | null;
  error: string;
  generation: number;
  selectedTargetTypeKey: string | null;
}

export interface TypeRelationsDependencies {
  state: TypeRelationsState;
  query(
    request: TypeRelationsRequest,
  ): Promise<BrowserAnnotatedTypeRelationsInspection>;
  describeError(error: unknown): string;
  render(): void;
}

export interface TypeRelationsCoordinator {
  inspect(request: TypeRelationsRequest): Promise<void>;
}

export interface TypeRelationsBindingActions {
  inspect(): void;
  selectRelationship(targetTypeKey: string): void;
  activateType(target: CallGraphTarget): void;
}

export function bindTypeRelations(
  root: ParentNode,
  actions: TypeRelationsBindingActions,
): void {
  root.querySelectorAll<HTMLButtonElement>(
    "[data-inspect-type-relations]",
  ).forEach(button => button.addEventListener(
    "click",
    () => actions.inspect(),
  ));
  const edges = root.querySelectorAll<SVGPathElement>(
    "[data-type-analysis-relationship]",
  );
  const empty = root.querySelector<HTMLElement>(
    "[data-type-analysis-detail-empty]",
  );
  const details = root.querySelectorAll<HTMLElement>(
    "[data-type-analysis-detail]",
  );
  const select = (edge: SVGPathElement) => {
    const key = edge.dataset.targetTypeKey;
    if (!key) return;
    for (const candidate of edges) {
      const selected = candidate === edge;
      candidate.setAttribute("aria-pressed", String(selected));
      candidate.nextElementSibling?.classList.toggle(
        "is-selected",
        selected,
      );
    }
    for (const detail of details) {
      detail.hidden = detail.dataset.typeAnalysisDetail !== key;
    }
    if (empty) empty.hidden = true;
    actions.selectRelationship(key);
  };
  for (const edge of edges) {
    edge.addEventListener("click", () => select(edge));
    edge.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      select(edge);
    });
  }
  for (const node of root.querySelectorAll<SVGGElement>(
    "[data-type-analysis-node]",
  )) {
    const activate = () => {
      const typeDefinitionId = node.dataset.typeQueryId;
      const assembly = node.dataset.typeAssembly;
      if (!typeDefinitionId || !assembly) return;
      const assemblyVersion = node.getAttribute(
        "data-type-assembly-version",
      );
      const assemblyCulture = node.getAttribute(
        "data-type-assembly-culture",
      );
      const assemblyPublicKeyToken = node.getAttribute(
        "data-type-assembly-public-key-token",
      );
      const target: CallGraphTarget = {
        typeDefinitionId,
        assembly,
        ...(assemblyVersion !== null
          ? { assemblyVersion }
          : {}),
        ...(assemblyCulture !== null
          ? { assemblyCulture }
          : {}),
        ...(assemblyPublicKeyToken !== null
          ? { assemblyPublicKeyToken }
          : {}),
      };
      actions.activateType(target);
    };
    node.addEventListener("click", activate);
    node.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      activate();
    });
  }
}

export function createTypeRelationsCoordinator(
  dependencies: TypeRelationsDependencies,
): TypeRelationsCoordinator {
  const { state } = dependencies;
  return {
    async inspect(request) {
      if (state.signature === request.signature && state.loading) return;
      const generation = ++state.generation;
      state.signature = request.signature;
      state.loading = true;
      state.result = null;
      state.error = "";
      state.selectedTargetTypeKey = null;
      dependencies.render();
      const ownsRequest = () =>
        state.generation === generation
        && state.signature === request.signature;
      try {
        const result = await dependencies.query(request);
        if (ownsRequest() && request.isCurrent()) state.result = result;
      } catch (error) {
        if (ownsRequest() && request.isCurrent()) {
          state.error = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.loading = false;
          if (request.isVisible()) dependencies.render();
        }
      }
    },
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function canonicalDocument(
  value: unknown,
): CanonicalRelationsDocument | null {
  if (!isRecord(value)
    || !isRecord(value.terminal)
    || !Array.isArray(value.subjects)
    || !Array.isArray(value.relations)
    || !Array.isArray(value.occurrences)
    || !Array.isArray(value.annotations)
    || !Array.isArray(value.relationship_outcomes)
    || !Array.isArray(value.color_outcomes)) {
    return null;
  }
  const terminal = value.terminal;
  if (typeof terminal.kind !== "string"
    || (terminal.value !== null && typeof terminal.value !== "number")
    || (terminal.is_complete !== null
      && typeof terminal.is_complete !== "boolean")) {
    return null;
  }
  const subjects: CanonicalSubject[] = [];
  for (const candidate of value.subjects) {
    if (!isRecord(candidate)
      || typeof candidate.id !== "number"
      || !isRecord(candidate.subject)
      || !isRecord(candidate.subject.member)
      || !isRecord(candidate.subject.member.member)
      || !isRecord(candidate.subject.member.member.declaring_type)
      || typeof candidate.subject.member.member.declaring_type.assembly
        !== "string"
      || typeof candidate.subject.member.member.declaring_type.kind
        !== "string"
      || typeof candidate.subject.member.member.declaring_type.namespace
        !== "string"
      || typeof candidate.subject.member.member.declaring_type.name
        !== "string"
      || !isRecord(
        candidate.subject.member.member.declaring_type.resolution)
      || !isRecord(
        candidate.subject.member.member.declaring_type.resolution.origin)
      || typeof candidate.subject.member.member.declaring_type.resolution
        .origin.kind !== "string"
      || !isRecord(
        candidate.subject.member.member.declaring_type.resolution.type)
      || typeof candidate.subject.member.member.declaring_type.resolution
        .type.namespace !== "string"
      || !Array.isArray(
        candidate.subject.member.member.declaring_type.resolution
          .type.segments)
      || typeof candidate.subject.member.member.name !== "string") {
      return null;
    }
    const typeSegments: string[] = [];
    for (const segment of candidate.subject.member.member.declaring_type
      .resolution.type.segments) {
      if (typeof segment !== "string") return null;
      typeSegments.push(segment);
    }
    const rawOriginAssembly =
      candidate.subject.member.member.declaring_type.resolution
        .origin.assembly;
    let originAssembly:
      {
        name: string;
        version?: string;
        culture?: string;
        public_key_token?: string;
      } | undefined;
    if (rawOriginAssembly !== undefined) {
      if (!isRecord(rawOriginAssembly)
        || typeof rawOriginAssembly.name !== "string") return null;
      const rawVersion = rawOriginAssembly.version;
      const rawCulture = rawOriginAssembly.culture;
      const rawPublicKeyToken = rawOriginAssembly.public_key_token;
      if ((rawVersion !== undefined && typeof rawVersion !== "string")
        || (rawCulture !== undefined && typeof rawCulture !== "string")
        || (rawPublicKeyToken !== undefined
          && typeof rawPublicKeyToken !== "string")) return null;
      originAssembly = {
        name: rawOriginAssembly.name,
        ...(rawVersion === undefined ? {} : { version: rawVersion }),
        ...(rawCulture === undefined ? {} : { culture: rawCulture }),
        ...(rawPublicKeyToken === undefined
          ? {}
          : { public_key_token: rawPublicKeyToken }),
      };
    }
    const rawModuleName =
      candidate.subject.member.member.declaring_type.resolution
        .origin.module_name;
    if (rawModuleName !== undefined && typeof rawModuleName !== "string")
      return null;
    const rawParameterTypes =
      candidate.subject.member.member.parameter_types;
    const parameterTypes: { namespace: string; name: string }[] = [];
    if (rawParameterTypes !== undefined) {
      if (!Array.isArray(rawParameterTypes)) return null;
      for (const parameter of rawParameterTypes) {
        if (!isRecord(parameter)
          || typeof parameter.namespace !== "string"
          || typeof parameter.name !== "string") return null;
        parameterTypes.push({
          namespace: parameter.namespace,
          name: parameter.name,
        });
      }
    }
    subjects.push({
      id: candidate.id,
      subject: {
        member: {
          member: {
            declaring_type: {
              assembly:
                candidate.subject.member.member.declaring_type.assembly,
              kind:
                candidate.subject.member.member.declaring_type.kind,
              namespace:
                candidate.subject.member.member.declaring_type.namespace,
              name: candidate.subject.member.member.declaring_type.name,
              resolution: {
                origin: {
                  kind: candidate.subject.member.member.declaring_type
                    .resolution.origin.kind,
                  ...(originAssembly === undefined
                    ? {}
                    : { assembly: originAssembly }),
                  ...(rawModuleName === undefined
                    ? {}
                    : { module_name: rawModuleName }),
                },
                type: {
                  namespace: candidate.subject.member.member.declaring_type
                    .resolution.type.namespace,
                  segments: [
                    ...typeSegments,
                  ],
                },
              },
            },
            name: candidate.subject.member.member.name,
            ...(rawParameterTypes === undefined
              ? {}
              : { parameter_types: parameterTypes }),
          },
        },
      },
    });
  }
  const relations: CanonicalRelation[] = [];
  for (const candidate of value.relations) {
    if (!isRecord(candidate)
      || typeof candidate.id !== "number"
      || typeof candidate.source_subject_id !== "number"
      || typeof candidate.target_subject_id !== "number") return null;
    relations.push({
      id: candidate.id,
      source_subject_id: candidate.source_subject_id,
      target_subject_id: candidate.target_subject_id,
    });
  }
  const occurrences: CanonicalOccurrence[] = [];
  for (const candidate of value.occurrences) {
    if (!isRecord(candidate)
      || typeof candidate.relation_id !== "number"
      || !isRecord(candidate.evidence)
      || typeof candidate.evidence.kind !== "string") return null;
    const rawCallerMethodToken = candidate.evidence.caller_method_token;
    const rawIlOffset = candidate.evidence.il_offset;
    const rawOperandToken = candidate.evidence.operand_token;
    const rawCallKind = candidate.evidence.call_kind;
    if ((rawCallerMethodToken !== undefined
        && typeof rawCallerMethodToken !== "number")
      || (rawIlOffset !== undefined && typeof rawIlOffset !== "number")
      || (rawOperandToken !== undefined && typeof rawOperandToken !== "number")
      || (rawCallKind !== undefined && typeof rawCallKind !== "string")) {
      return null;
    }
    occurrences.push({
      relation_id: candidate.relation_id,
      evidence: {
        kind: candidate.evidence.kind,
        ...(rawCallerMethodToken === undefined
          ? {}
          : { caller_method_token: rawCallerMethodToken }),
        ...(rawIlOffset === undefined ? {} : { il_offset: rawIlOffset }),
        ...(rawOperandToken === undefined
          ? {}
          : { operand_token: rawOperandToken }),
        ...(rawCallKind === undefined ? {} : { call_kind: rawCallKind }),
      },
    });
  }
  const annotations: CanonicalAnnotation[] = [];
  for (const candidate of value.annotations) {
    if (!isRecord(candidate)
      || !isRecord(candidate.descriptor)
      || typeof candidate.descriptor.id !== "string"
      || !isRecord(candidate.target)
      || typeof candidate.target.kind !== "string"
      || typeof candidate.target.id !== "number") return null;
    annotations.push({
      descriptor: { id: candidate.descriptor.id },
      target: {
        kind: candidate.target.kind,
        id: candidate.target.id,
      },
    });
  }
  const familyOutcomes = (
    values: unknown[],
  ): CanonicalFamilyOutcome[] | null => {
    const outcomes: CanonicalFamilyOutcome[] = [];
    for (const candidate of values) {
      if (!isRecord(candidate)
        || typeof candidate.family !== "string"
        || typeof candidate.disposition !== "string"
        || !isRecord(candidate.coverage)
        || typeof candidate.coverage.considered !== "number"
        || typeof candidate.coverage.examined !== "number"
        || typeof candidate.coverage.excluded !== "number"
        || typeof candidate.coverage.unavailable !== "number"
        || typeof candidate.coverage.limited !== "number") return null;
      outcomes.push({
        family: candidate.family,
        disposition: candidate.disposition,
        coverage: {
          considered: candidate.coverage.considered,
          examined: candidate.coverage.examined,
          excluded: candidate.coverage.excluded,
          unavailable: candidate.coverage.unavailable,
          limited: candidate.coverage.limited,
        },
      });
    }
    return outcomes;
  };
  const relationshipOutcomes =
    familyOutcomes(value.relationship_outcomes);
  const colorOutcomes = familyOutcomes(value.color_outcomes);
  if (!relationshipOutcomes || !colorOutcomes) return null;
  return {
    terminal: {
      kind: terminal.kind,
      value: terminal.value,
      is_complete: terminal.is_complete,
    },
    subjects,
    relations,
    occurrences,
    annotations,
    relationship_outcomes: relationshipOutcomes,
    color_outcomes: colorOutcomes,
  };
}

export interface RenderTypeRelationsOptions {
  signature: string;
  state: TypeRelationsState;
  focusTypeName: string;
  escapeHtml: (value: string) => string;
}

interface TypeEdge {
  key: string;
  targetType: CanonicalSubject["subject"]["member"]["member"]["declaring_type"];
  relations: CanonicalRelation[];
}

interface RelationsProjection {
  document: CanonicalRelationsDocument;
  subjects: Map<number, CanonicalSubject>;
  edges: TypeEdge[];
  occurrencesByRelation: Map<number, CanonicalOccurrence[]>;
  colorsBySubject: Map<number, string[]>;
  relationshipIsPartial: boolean;
  colorIsPartial: boolean;
}

function projectRelations(
  result: BrowserAnnotatedTypeRelationsInspection | null,
): RelationsProjection | null {
  if (result?.outcome !== "Available" || !result.inspection) return null;
  const document = canonicalDocument(result.inspection.content);
  if (!document) return null;
  const subjects = new Map(
    document.subjects.map(subject => [subject.id, subject]));
  const occurrencesByRelation = new Map<number, CanonicalOccurrence[]>();
  for (const occurrence of document.occurrences) {
    const relationOccurrences =
      occurrencesByRelation.get(occurrence.relation_id) ?? [];
    relationOccurrences.push(occurrence);
    occurrencesByRelation.set(occurrence.relation_id, relationOccurrences);
  }
  const colorsBySubject = new Map<number, string[]>();
  for (const annotation of document.annotations) {
    if (annotation.target.kind !== "Subject") continue;
    const existing = colorsBySubject.get(annotation.target.id) ?? [];
    existing.push(annotation.descriptor.id);
    colorsBySubject.set(annotation.target.id, existing);
  }
  const edgesByType = new Map<string, TypeEdge>();
  for (const relation of document.relations) {
    const source = subjects.get(relation.source_subject_id);
    const target = subjects.get(relation.target_subject_id);
    if (!source || !target) return null;
    const type = target.subject.member.member.declaring_type;
    const key = typeIdentityKey(type);
    const edge = edgesByType.get(key) ?? {
      key,
      targetType: type,
      relations: [],
    };
    edge.relations.push(relation);
    edgesByType.set(key, edge);
  }
  const hasIncompleteCoverage = (
    outcomes: readonly CanonicalFamilyOutcome[],
  ) => outcomes.some(outcome =>
    outcome.coverage.unavailable > 0 || outcome.coverage.limited > 0);
  return {
    document,
    subjects,
    edges: [...edgesByType.values()].sort((left, right) =>
      typeName(left.targetType).localeCompare(typeName(right.targetType))),
    occurrencesByRelation,
    colorsBySubject,
    relationshipIsPartial: document.terminal.is_complete === false
      || hasIncompleteCoverage(document.relationship_outcomes),
    colorIsPartial: hasIncompleteCoverage(document.color_outcomes),
  };
}

function typeName(
  type: CanonicalSubject["subject"]["member"]["member"]["declaring_type"],
): string {
  return `${type.namespace ? `${type.namespace}.` : ""}${type.name}`;
}

function typeIdentityKey(
  type: CanonicalSubject["subject"]["member"]["member"]["declaring_type"],
): string {
  const { origin } = type.resolution;
  return JSON.stringify([
    origin.kind,
    origin.assembly?.name ?? null,
    origin.assembly?.version ?? null,
    origin.assembly?.culture ?? null,
    origin.assembly?.public_key_token ?? null,
    origin.module_name ?? null,
    type.resolution.type.namespace,
    type.resolution.type.segments,
  ]);
}

function typeQueryId(
  type: CanonicalSubject["subject"]["member"]["member"]["declaring_type"],
): string {
  const { namespace, segments } = type.resolution.type;
  return `${namespace ? `${namespace}.` : ""}${segments.join("+")}`;
}

function typeNavigationAttributes(
  type: CanonicalSubject["subject"]["member"]["member"]["declaring_type"],
  escapeHtml: (value: string) => string,
): string {
  const assembly = type.resolution.origin.assembly;
  const attributes = [
    `data-type-query-id="${escapeHtml(typeQueryId(type))}"`,
    `data-type-assembly="${escapeHtml(assembly?.name ?? type.assembly)}"`,
  ];
  if (assembly?.version !== undefined) {
    attributes.push(
      `data-type-assembly-version="${escapeHtml(assembly.version)}"`,
    );
  }
  if (assembly?.culture !== undefined) {
    attributes.push(
      `data-type-assembly-culture="${escapeHtml(assembly.culture)}"`,
    );
  }
  if (assembly?.public_key_token !== undefined) {
    attributes.push(
      `data-type-assembly-public-key-token="${escapeHtml(assembly.public_key_token)}"`,
    );
  }
  return attributes.join(" ");
}

function shortTypeName(value: string): string {
  const separator = Math.max(value.lastIndexOf("."), value.lastIndexOf("+"));
  return separator < 0 ? value : value.slice(separator + 1);
}

function memberName(subject: CanonicalSubject): string {
  const member = subject.subject.member.member;
  const parameters = member.parameter_types?.map(parameter =>
    parameter.name).join(", ");
  return parameters === undefined
    ? member.name
    : `${member.name}(${parameters})`;
}

function hex(value: number, width: number): string {
  return `0x${value.toString(16).toUpperCase().padStart(width, "0")}`;
}

function renderCallSite(
  occurrence: CanonicalOccurrence,
  escapeHtml: (value: string) => string,
): string {
  const { evidence } = occurrence;
  const location = evidence.il_offset === undefined
    ? evidence.kind
    : `IL_${evidence.il_offset.toString(16).toUpperCase().padStart(4, "0")}`;
  const detail = [
    evidence.call_kind,
    evidence.operand_token === undefined
      ? null
      : `operand ${hex(evidence.operand_token, 8)}`,
    evidence.caller_method_token === undefined
      ? null
      : `method ${hex(evidence.caller_method_token, 8)}`,
  ].filter((part): part is string => part !== null);
  return `<li class="type-call-site">
    <code>${escapeHtml(location)}</code>
    ${detail.length === 0
      ? ""
      : `<span>${detail.map(escapeHtml).join(" · ")}</span>`}
  </li>`;
}

function colorLabel(color: string): string {
  return color === "member.requires-unreferenced-code"
    ? "RUC"
    : color === "member.requires-dynamic-code"
      ? "RDC"
      : color === "member.requires-assembly-files"
        ? "RAF"
        : color;
}

function currentProjection(
  options: RenderTypeRelationsOptions,
): RelationsProjection | null {
  const { signature, state } = options;
  return state.signature === signature
    ? projectRelations(state.result)
    : null;
}

export function renderTypeRelationsSummary(
  options: RenderTypeRelationsOptions,
): string {
  const { signature, state } = options;
  const fresh = state.signature === signature;
  if (!fresh || state.loading)
    return "<button type=\"button\" data-type-analysis>Analysis</button>";
  const projection = currentProjection(options);
  if (!projection)
    return "<button type=\"button\" data-type-analysis>Analysis</button>";
  const count = projection.edges.length;
  return `<button type="button" data-type-analysis>${
    count.toLocaleString()
  }${projection.relationshipIsPartial ? "+" : ""} called type${
    count === 1 ? "" : "s"
  }</button>`;
}

/** The Relationships mode's content, inside a Type Analysis inspector. */
export function renderTypeRelationshipsMode(
  options: RenderTypeRelationsOptions,
): string {
  return renderTypeRelations(options)
    || section(`
      <div class="source-progress">
        <span class="loader"></span>
        <p>Preparing focused binary relationship evidence…</p>
      </div>`);
}

export function renderTypeAnalysisPage(
  options: RenderTypeRelationsOptions,
): string {
  const content = renderTypeRelationshipsMode(options);
  return `
    <section class="metadata-surface type-analysis-surface" aria-labelledby="type-analysis-title">
      <header class="metadata-surface-head">
        <h1 id="type-analysis-title">Analysis</h1>
        <p>${options.escapeHtml(options.focusTypeName)}</p>
      </header>
      <div class="metadata-surface-scroll">${content}</div>
    </section>`;
}

export function renderTypeRelations(
  options: RenderTypeRelationsOptions,
): string {
  const {
    signature, state, focusTypeName, escapeHtml,
  } = options;
  if (state.signature !== signature || (!state.loading && !state.result
    && !state.error)) return "";
  if (state.loading) {
    return section(`
      <div class="source-progress">
        <span class="loader"></span>
        <p>Building Type relationships from focused binary evidence…</p>
      </div>`);
  }
  if (state.error) return failure(state.error, escapeHtml);
  const result = state.result;
  if (result?.outcome !== "Available" || !result.inspection) {
    return failure(
      result?.reason
        ?? `Inspection ${String(result?.outcome ?? "failed").toLowerCase()}.`,
      escapeHtml);
  }
  const projection = currentProjection(options);
  if (!projection) {
    return failure(
      "The relation document did not match its canonical wire shape.",
      escapeHtml);
  }
  const {
    document, subjects, edges, occurrencesByRelation, colorsBySubject,
    relationshipIsPartial, colorIsPartial,
  } = projection;
  const selectedKey = edges.some(edge =>
    edge.key === state.selectedTargetTypeKey)
    ? state.selectedTargetTypeKey
    : null;
  const firstRelation = edges[0]?.relations[0];
  const sourceType = firstRelation
    ? subjects.get(firstRelation.source_subject_id)
      ?.subject.member.member.declaring_type
    : null;
  if (edges.length > 0 && !sourceType) {
    return failure(
      "The relation document did not retain its focused source Type.",
      escapeHtml,
    );
  }
  const sourceNavigationAttributes = sourceType
    ? typeNavigationAttributes(sourceType, escapeHtml)
    : "";
  const graphHeight = Math.max(190, edges.length * 84 + 38);
  const sourceY = graphHeight / 2 - 25;
  const graphEdges = edges.map((edge, index) => {
    const targetY = 24 + index * 84;
    const middleY = targetY + 25;
    const selected = edge.key === selectedKey;
    const edgeLabel = `${edge.relations.length}${relationshipIsPartial ? "+" : ""}`;
    return `<g class="type-analysis-edge">
      <path class="type-analysis-edge-hit" d="M 292 ${sourceY + 25} C 410 ${sourceY + 25}, 430 ${middleY}, 548 ${middleY}"
        role="button" tabindex="0" aria-label="${escapeHtml(`${focusTypeName} calls ${typeName(edge.targetType)} through ${edge.relations.length} member relationship${edge.relations.length === 1 ? "" : "s"}`)}"
        aria-pressed="${selected}" data-type-analysis-relationship data-target-type-key="${escapeHtml(edge.key)}"></path>
      <path class="type-analysis-edge-line${selected ? " is-selected" : ""}" d="M 292 ${sourceY + 25} C 410 ${sourceY + 25}, 430 ${middleY}, 548 ${middleY}"></path>
      <text class="type-analysis-edge-label" x="420" y="${middleY - 7}" text-anchor="middle">${edgeLabel}</text>
    </g>`;
  }).join("");
  const targetNodes = edges.map((edge, index) => {
    const fullName = typeName(edge.targetType);
    const targetY = 24 + index * 84;
    return `<g class="type-analysis-node type-analysis-target-node" transform="translate(548 ${targetY})"
      role="button" tabindex="0" aria-label="${escapeHtml(`Open ${fullName}`)}"
      data-type-analysis-node ${typeNavigationAttributes(edge.targetType, escapeHtml)}>
      <title>${escapeHtml(fullName)}</title>
      <rect width="300" height="50" rx="7"></rect>
      <text x="16" y="22">${escapeHtml(shortTypeName(fullName))}</text>
      <text class="type-analysis-node-subtitle" x="16" y="38">${edge.relations.length} member relationship${edge.relations.length === 1 ? "" : "s"}</text>
    </g>`;
  }).join("");
  const detailHtml = edges.map(edge => {
    const sourceGroups = new Map<number, CanonicalRelation[]>();
    for (const relation of edge.relations) {
      const group = sourceGroups.get(relation.source_subject_id) ?? [];
      group.push(relation);
      sourceGroups.set(relation.source_subject_id, group);
    }
    const members = [...sourceGroups.entries()].map(([sourceId, relations]) => {
      const source = subjects.get(sourceId);
      const rows = relations.map(relation => {
        const target = subjects.get(relation.target_subject_id);
        if (!target) return "";
        const occurrences = occurrencesByRelation.get(relation.id) ?? [];
        const sites = occurrences.length;
        const colors = colorsBySubject.get(relation.target_subject_id) ?? [];
        return `<li class="type-analysis-member-pair">
          <div>
            <code>${escapeHtml(memberName(target))}</code>
            <span>${sites} site${sites === 1 ? "" : "s"}</span>
            ${colors.length
              ? `<small>${colors.map(color =>
                  `<abbr title="${escapeHtml(color)}">${escapeHtml(colorLabel(color))}</abbr>`)
                  .join("")}</small>`
              : ""}
          </div>
          <ol class="type-call-sites" aria-label="Physical call sites">
            ${occurrences.map(occurrence =>
              renderCallSite(occurrence, escapeHtml)).join("")}
          </ol>
        </li>`;
      }).join("");
      return `<div class="type-analysis-member-group">
        <h4>${escapeHtml(source ? memberName(source) : `Member ${sourceId}`)}</h4>
        <ul>${rows}</ul>
      </div>`;
    }).join("");
    const siteCount = edge.relations.reduce(
      (total, relation) =>
        total + (occurrencesByRelation.get(relation.id)?.length ?? 0),
      0,
    );
    const targetName = typeName(edge.targetType);
    return `<section class="type-analysis-relationship-detail"
      data-type-analysis-detail="${escapeHtml(edge.key)}"${edge.key === selectedKey ? "" : " hidden"}>
      <header>
        <h3>
          <button type="button" data-type-analysis-node ${sourceNavigationAttributes}>${escapeHtml(focusTypeName)}</button>
          <span aria-hidden="true">→</span>
          <button type="button" data-type-analysis-node ${typeNavigationAttributes(edge.targetType, escapeHtml)}>${escapeHtml(targetName)}</button>
        </h3>
        <span>${edge.relations.length} member relationship${edge.relations.length === 1 ? "" : "s"} · ${siteCount} physical site${siteCount === 1 ? "" : "s"}</span>
      </header>
      <div class="type-analysis-relationship-facts">
        ${members}
      </div>
    </section>`;
  }).join("");
  return section(`
    <div class="section-title">
      <h2 id="type-analysis-relationships-title" tabindex="-1">Relationships</h2>
      <span>${edges.length} called type${edges.length === 1 ? "" : "s"} · ${document.relations.length} member relationship${document.relations.length === 1 ? "" : "s"}</span>
    </div>
    ${relationshipIsPartial
      ? `<div class="metadata-warning"><strong>Partial call evidence</strong><p>Counts are observed lower bounds; unresolved calls may add Type or member edges.</p></div>`
      : ""}
    ${colorIsPartial
      ? `<div class="metadata-warning"><strong>Partial compatibility evidence</strong><p>Compatibility signals are incomplete; an unmarked member may still carry a selected compatibility attribute.</p></div>`
      : ""}
    ${edges.length
      ? `<div class="type-analysis-graph" aria-labelledby="type-analysis-relationships-title">
          <svg viewBox="0 0 880 ${graphHeight}" role="group" aria-label="Directed Type relationship graph">
            <defs>
              <marker id="type-analysis-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto">
                <path d="M 0 0 L 8 4 L 0 8 z"></path>
              </marker>
            </defs>
            ${graphEdges}
            <g class="type-analysis-node type-analysis-source-node" transform="translate(22 ${sourceY})"
              role="button" tabindex="0" aria-label="${escapeHtml(`Open ${focusTypeName}`)}"
              data-type-analysis-node ${sourceNavigationAttributes}>
              <title>${escapeHtml(focusTypeName)}</title>
              <rect width="270" height="50" rx="7"></rect>
              <text x="16" y="22">${escapeHtml(shortTypeName(focusTypeName))}</text>
              <text class="type-analysis-node-subtitle" x="16" y="38">selected Type</text>
            </g>
            ${targetNodes}
          </svg>
        </div>`
      : relationshipIsPartial
        ? "<p>No called Types were resolved from the available evidence.</p>"
        : "<p>No outgoing Type edges found.</p>"}
    ${edges.length
      ? `<div class="type-analysis-details" aria-live="polite">
          <p data-type-analysis-detail-empty${selectedKey ? " hidden" : ""}>Select a relationship in the graph to inspect its member and physical-site evidence.</p>
          ${detailHtml}
        </div>`
      : ""}
    <button type="button" class="tiny-button" data-inspect-type-relations>Refresh</button>`);
}

function section(content: string): string {
  return `<section class="document-section type-relations-section" data-type-analysis-relationships-surface>
    ${content}
  </section>`;
}

function failure(
  reason: string,
  escapeHtml: (value: string) => string,
): string {
  return section(`
    <div class="metadata-warning" role="alert">
      <strong>Type relationships unavailable</strong>
      <p>${escapeHtml(reason)}</p>
    </div>
    <button type="button" data-inspect-type-relations>Retry relationships</button>`);
}
