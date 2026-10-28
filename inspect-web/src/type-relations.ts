import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "./facades/inspect-web-metadata.d.ts";

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
  isVisible(): boolean;
}

export interface TypeRelationsState {
  signature: string;
  loading: boolean;
  result: BrowserAnnotatedTypeRelationsInspection | null;
  error: string;
  generation: number;
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

export function bindTypeRelations(
  root: ParentNode,
  inspect: () => void,
): void {
  root.querySelectorAll<HTMLButtonElement>(
    "[data-inspect-type-relations]",
  ).forEach(button => button.addEventListener("click", inspect));
  root.querySelector<HTMLButtonElement>(
    "[data-type-call-graph]",
  )?.addEventListener("click", () => {
    const heading = root.querySelector<HTMLElement>("#type-call-graph-title");
    heading?.scrollIntoView({ block: "start" });
    heading?.focus({ preventScroll: true });
  });
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
      dependencies.render();
      const ownsRequest = () =>
        state.generation === generation
        && state.signature === request.signature;
      try {
        const result = await dependencies.query(request);
        if (ownsRequest() && request.isVisible()) state.result = result;
      } catch (error) {
        if (ownsRequest() && request.isVisible()) {
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
      || typeof candidate.relation_id !== "number") return null;
    occurrences.push({ relation_id: candidate.relation_id });
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
  occurrenceCounts: Map<number, number>;
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
  const occurrenceCounts = new Map<number, number>();
  for (const occurrence of document.occurrences) {
    occurrenceCounts.set(
      occurrence.relation_id,
      (occurrenceCounts.get(occurrence.relation_id) ?? 0) + 1);
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
    const target = subjects.get(relation.target_subject_id);
    if (!target) return null;
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
    occurrenceCounts,
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

function memberName(subject: CanonicalSubject): string {
  const member = subject.subject.member.member;
  const parameters = member.parameter_types?.map(parameter =>
    parameter.name).join(", ");
  return parameters === undefined
    ? member.name
    : `${member.name}(${parameters})`;
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
  if (!fresh) {
    return "<button type=\"button\" data-inspect-type-relations>Inspect call graph</button>";
  }
  if (state.loading) {
    return '<button type="button" disabled><span class="loader"></span> Inspecting calls…</button>';
  }
  const projection = currentProjection(options);
  if (!projection)
    return "<button type=\"button\" data-inspect-type-relations>Retry call graph</button>";
  const count = projection.edges.length;
  return `<button type="button" data-type-call-graph>${
    count.toLocaleString()
  }${projection.relationshipIsPartial ? "+" : ""} called type${
    count === 1 ? "" : "s"
  }</button>`;
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
        <p>Building the Type call graph from focused binary evidence…</p>
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
    document, subjects, edges, occurrenceCounts, colorsBySubject,
    relationshipIsPartial, colorIsPartial,
  } = projection;
  const edgeHtml = edges.map(edge => {
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
        const sites = occurrenceCounts.get(relation.id) ?? 0;
        const colors = colorsBySubject.get(relation.target_subject_id) ?? [];
        return `<li>
          <code>${escapeHtml(memberName(target))}</code>
          <span>${sites} site${sites === 1 ? "" : "s"}</span>
          ${colors.length
            ? `<small>${colors.map(color =>
                `<abbr title="${escapeHtml(color)}">${escapeHtml(colorLabel(color))}</abbr>`)
                .join("")}</small>`
            : ""}
        </li>`;
      }).join("");
      return `<div class="type-call-member-group">
        <h4>${escapeHtml(source ? memberName(source) : `Member ${sourceId}`)}</h4>
        <ul>${rows}</ul>
      </div>`;
    }).join("");
    const colorCount = new Set(edge.relations.flatMap(relation =>
      colorsBySubject.get(relation.target_subject_id) ?? [])).size;
    return `<details class="type-call-edge">
      <summary>
        <span class="type-call-edge-line"><strong>${edge.relations.length}</strong> member edge${edge.relations.length === 1 ? "" : "s"}</span>
        <span class="type-call-target-node">
          <strong>${escapeHtml(typeName(edge.targetType))}</strong>
          <small>${colorCount
            ? `${colorCount} compatibility signal${colorCount === 1 ? "" : "s"}`
            : "called Type"}</small>
        </span>
      </summary>
      <div class="type-call-edge-detail">
        <h3>${escapeHtml(focusTypeName)} → ${escapeHtml(typeName(edge.targetType))}</h3>
        ${members}
      </div>
    </details>`;
  }).join("");
  return section(`
    <div class="section-title">
      <h2 id="type-call-graph-title" tabindex="-1">Type call graph</h2>
      <span>${edges.length} called type${edges.length === 1 ? "" : "s"} · ${document.relations.length} member edge${document.relations.length === 1 ? "" : "s"}</span>
    </div>
    ${relationshipIsPartial
      ? `<div class="metadata-warning"><strong>Partial call evidence</strong><p>Counts are observed lower bounds; unresolved calls may add Type or member edges.</p></div>`
      : ""}
    ${colorIsPartial
      ? `<div class="metadata-warning"><strong>Partial compatibility evidence</strong><p>Compatibility signals are incomplete; an unmarked member may still carry a selected compatibility attribute.</p></div>`
      : ""}
    ${edgeHtml
      ? `<div class="type-call-graph" aria-labelledby="type-call-graph-title">
          <div class="type-call-source-node">
            <strong>${escapeHtml(focusTypeName)}</strong>
            <small>selected Type</small>
          </div>
          <div class="type-call-edges">${edgeHtml}</div>
        </div>`
      : relationshipIsPartial
        ? "<p>No called Types were resolved from the available evidence.</p>"
        : "<p>No outgoing Type edges found.</p>"}
    <button type="button" class="tiny-button" data-inspect-type-relations>Refresh</button>`);
}

function section(content: string): string {
  return `<section class="document-section type-relations-section" data-type-call-graph-surface>
    ${content}
  </section>`;
}

function failure(
  reason: string,
  escapeHtml: (value: string) => string,
): string {
  return section(`
    <div class="metadata-warning" role="alert">
      <strong>Type call graph unavailable</strong>
      <p>${escapeHtml(reason)}</p>
    </div>
    <button type="button" data-inspect-type-relations>Retry call graph</button>`);
}
