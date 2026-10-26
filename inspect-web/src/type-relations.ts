import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "./facades/inspect-web-metadata.d.ts";

interface CanonicalSubject {
  id: number;
  subject: {
    member: {
      member: {
        declaring_type: { namespace: string; name: string };
        name: string;
      };
    };
  };
}

interface CanonicalRelation {
  id: number;
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
  root.querySelector<HTMLButtonElement>(
    "[data-inspect-type-relations]",
  )?.addEventListener("click", inspect);
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
      || typeof candidate.subject.member.member.declaring_type.namespace
        !== "string"
      || typeof candidate.subject.member.member.declaring_type.name
        !== "string"
      || typeof candidate.subject.member.member.name !== "string") {
      return null;
    }
    subjects.push({
      id: candidate.id,
      subject: {
        member: {
          member: {
            declaring_type: {
              namespace:
                candidate.subject.member.member.declaring_type.namespace,
              name: candidate.subject.member.member.declaring_type.name,
            },
            name: candidate.subject.member.member.name,
          },
        },
      },
    });
  }
  const relations: CanonicalRelation[] = [];
  for (const candidate of value.relations) {
    if (!isRecord(candidate)
      || typeof candidate.id !== "number"
      || typeof candidate.target_subject_id !== "number") return null;
    relations.push({
      id: candidate.id,
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
  escapeHtml: (value: string) => string;
}

export function renderTypeRelations(
  options: RenderTypeRelationsOptions,
): string {
  const { signature, state, escapeHtml } = options;
  const fresh = state.signature === signature;
  if (!fresh) {
    return section(
      `<button type="button" data-inspect-type-relations>`
        + "Inspect outgoing calls</button>",
    );
  }
  if (state.loading) {
    return section(
      '<button type="button" disabled><span class="loader"></span> '
        + "Inspecting outgoing calls…</button>",
    );
  }
  if (state.error) {
    return failure(state.error, escapeHtml);
  }
  const result = state.result;
  if (!result) {
    return section(
      `<button type="button" data-inspect-type-relations>`
        + "Inspect outgoing calls</button>",
    );
  }
  if (result.outcome !== "Available" || !result.inspection) {
    return failure(
      result.reason ?? `Inspection ${String(result.outcome).toLowerCase()}.`,
      escapeHtml,
    );
  }
  const document = canonicalDocument(result.inspection.content);
  if (!document) {
    return failure(
      "The relation document did not match its canonical wire shape.",
      escapeHtml,
    );
  }

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
  const visible = document.relations.slice(0, 50);
  const rows = visible.map(relation => {
    const member = subjects.get(relation.target_subject_id)
      ?.subject.member.member;
    const type = member?.declaring_type;
    const target = member && type
      ? `${type.namespace ? `${type.namespace}.` : ""}${type.name}.${member.name}`
      : `Subject ${relation.target_subject_id}`;
    const relationColors =
      colorsBySubject.get(relation.target_subject_id) ?? [];
    return `<li><code>${escapeHtml(target)}</code>`
      + `<span>${occurrenceCounts.get(relation.id) ?? 0} occurrence${
        occurrenceCounts.get(relation.id) === 1 ? "" : "s"
      }</span>`
      + (relationColors.length
        ? `<small>${relationColors
            .map(color => `<code>${escapeHtml(color)}</code>`)
            .join(" ")}</small>`
        : "")
      + "</li>";
  }).join("");
  const coverage = document.relationship_outcomes
    .map(outcome => outcome.coverage)
    .reduce(
      (total, value) => ({
        considered: total.considered + value.considered,
        examined: total.examined + value.examined,
        excluded: total.excluded + value.excluded,
        unavailable: total.unavailable + value.unavailable,
        limited: total.limited + value.limited,
      }),
      {
        considered: 0,
        examined: 0,
        excluded: 0,
        unavailable: 0,
        limited: 0,
      });
  const relationshipIsPartial = document.terminal.is_complete === false
    || coverage.unavailable > 0
    || coverage.limited > 0;
  const incompleteColors = document.color_outcomes.filter(
    outcome =>
      outcome.coverage.unavailable > 0
      || outcome.coverage.limited > 0);
  const colorWarning = incompleteColors.length
    ? `<div class="metadata-warning"><strong>⚠ Compatibility color evidence is partial</strong><ul>${
        incompleteColors.map(outcome =>
          `<li><code>${escapeHtml(outcome.family)}</code>: `
          + `${outcome.coverage.examined} of `
          + `${outcome.coverage.considered} examined`
          + (outcome.coverage.unavailable > 0
            ? `; ${outcome.coverage.unavailable} unavailable`
            : "")
          + (outcome.coverage.limited > 0
            ? `; ${outcome.coverage.limited} limited`
            : "")
          + "</li>").join("")
      }</ul></div>`
    : "";
  const count = document.terminal.value ?? document.relations.length;
  return section(`
    <div class="section-title">
      <h2>Outgoing calls</h2>
      <span>${count} logical · ${document.occurrences.length} physical</span>
    </div>
    ${relationshipIsPartial
      ? `<div class="metadata-warning"><strong>⚠ Call evidence is partial</strong>`
        + `<p>${coverage.examined} of ${coverage.considered} candidates examined; `
        + `${coverage.unavailable} unavailable; `
        + `${coverage.limited} limited.</p></div>`
      : ""}
    ${colorWarning}
    ${rows ? `<ol class="type-relations-list">${rows}</ol>` : "<p>No outgoing calls found.</p>"}
    ${document.relations.length > visible.length
      ? `<p>Showing ${visible.length} of ${document.relations.length} relations.</p>`
      : ""}
    <button type="button" class="tiny-button" data-inspect-type-relations>Refresh</button>`);
}

function section(content: string): string {
  return `<section class="document-section type-relations-section">
    <div class="section-title"><h2>Annotated Type Relations</h2><span>focused binary evidence</span></div>
    ${content}
  </section>`;
}

function failure(
  reason: string,
  escapeHtml: (value: string) => string,
): string {
  return section(`
    <div class="metadata-warning" role="alert">
      <strong>Outgoing calls unavailable</strong>
      <p>${escapeHtml(reason)}</p>
    </div>
    <button type="button" data-inspect-type-relations>Retry</button>`);
}
