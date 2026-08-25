// Render + bind shape for the package query experience (docs/design/package-query-experience.md).
// Follows the render-function-returns-HTML-string and data-attribute binding
// conventions used across the other full-bleed lenses (package-opportunities.ts,
// package-view.ts).

import type {
  PackageQueryState,
  QueryFacetTerm,
  QueryResultRow,
} from "./package-query.ts";

export interface PackageQueryBindingActions {
  onRowOpen: (packageId: string, version: string) => void;
  onRowSelectToggle: (packageId: string) => void;
  onFacetToggle: (facetKey: string) => void;
  onDeepen: () => void;
  onCancel: () => void;
}

export function bindPackageQueryView(
  root: ParentNode,
  actions: PackageQueryBindingActions,
) {
  root.querySelectorAll<HTMLElement>("[data-query-row-open]").forEach(button =>
    button.addEventListener("click", () => actions.onRowOpen(
      button.dataset.queryRowOpen ?? "",
      button.dataset.queryRowVersion ?? "")));
  root.querySelectorAll<HTMLElement>("[data-query-row-select]").forEach(box =>
    box.addEventListener("change", () => actions.onRowSelectToggle(
      box.dataset.queryRowSelect ?? "")));
  root.querySelectorAll<HTMLElement>("[data-query-facet]").forEach(button =>
    button.addEventListener("click", () => actions.onFacetToggle(
      button.dataset.queryFacet ?? "")));
  root.querySelectorAll<HTMLElement>("[data-query-deepen]").forEach(button =>
    button.addEventListener("click", () => actions.onDeepen()));
  root.querySelectorAll<HTMLElement>("[data-query-cancel]").forEach(button =>
    button.addEventListener("click", () => actions.onCancel()));
}

function renderRow(
  row: QueryResultRow,
  selected: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  const evidence = row.evidence
    .map(item => `<span class="opp-pattern">${escapeHtml(item)}</span>`)
    .join("");
  return `
    <div class="query-row">
      <input type="checkbox" data-query-row-select="${escapeHtml(row.packageId)}" ${selected ? "checked" : ""} />
      <div class="opp-body">
        <div class="opp-head">
          <button class="opp-type-chip" data-query-row-open="${escapeHtml(row.packageId)}" data-query-row-version="${escapeHtml(row.version)}" title="Open ${escapeHtml(row.packageId)} in the workspace">
            <span class="opp-type-name">${escapeHtml(row.packageId)}</span><span class="opp-type-ns">${escapeHtml(row.version)}</span>
          </button>
          <span class="query-tier query-tier-${escapeHtml(row.tier)}">${escapeHtml(row.tier)}</span>
        </div>
        <div class="opp-lookfor">${evidence}</div>
      </div>
    </div>`;
}

function renderFacetGroup(
  label: string,
  facets: readonly QueryFacetTerm[],
  activeKeys: ReadonlySet<string>,
  escapeHtml: (value: unknown) => string,
): string {
  const items = facets.map(facet => {
    const active = activeKeys.has(facet.key) ? "active" : "";
    const tierBadge = facet.tier === "promoted" ? `<small class="query-tier-badge">deepen</small>` : "";
    return `<button class="type-chip ${active}" data-query-facet="${escapeHtml(facet.key)}">${escapeHtml(facet.label)}${tierBadge}</button>`;
  }).join("");
  return `<div class="query-facet-group"><h3>${escapeHtml(label)}</h3><div class="type-chip-list">${items}</div></div>`;
}

function renderCompletionFooter(
  outcome: PackageQueryState["outcome"],
  escapeHtml: (value: unknown) => string,
): string {
  const { completion } = outcome;
  const label = completion.kind === "streaming"
    ? "streaming…"
    : completion.kind === "bounded"
      ? `bounded: ${escapeHtml(completion.reason)}`
      : completion.kind === "exhausted"
        ? "all matches"
        : "cancelled";
  const cancelButton = completion.kind === "streaming"
    ? `<button data-query-cancel="1">Cancel</button>`
    : "";
  return `<div class="query-footer"><span>${outcome.rows.length} package${outcome.rows.length === 1 ? "" : "s"} · ${label}</span>${cancelButton}</div>`;
}

export interface RenderPackageQueryOptions {
  state: PackageQueryState;
  availableFacets: readonly QueryFacetTerm[];
  escapeHtml: (value: unknown) => string;
}

export function renderPackageQueryView(options: RenderPackageQueryOptions): string {
  const { state, availableFacets, escapeHtml } = options;

  if (!state.request) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌕</span><h2>Query nuget.org</h2><p>Choose a scope and narrow with facets — a nuspec-only funnel over the ecosystem, no package download required.</p></section>`;
  }

  const failures = state.outcome.failures.length
    ? `<section class="document-section metadata-warning"><strong>⚠ Some sources failed</strong><ul>${state.outcome.failures.map(f => `<li><code>${escapeHtml(f)}</code></li>`).join("")}</ul></section>`
    : "";

  if (!state.outcome.rows.length && state.outcome.completion.kind !== "streaming") {
    // A failed source means "no rows" is not the same claim as "no matches" —
    // some of the space was never searched, so the empty state must say so
    // rather than implying a clean, confident zero (see the honesty rule in
    // package-query.ts's QueryOutcome doc comment).
    const emptyState = state.outcome.failures.length
      ? `<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No matches found — with failures</h2><p>Some sources failed above, so this is not a confirmed empty result. Retry the failed sources or broaden the facets.</p></section>`
      : `<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No matches</h2><p>Try a broader facet.</p></section>`;
    return `${failures}${emptyState}`;
  }

  const activeKeys = new Set(state.request.facets.map(f => f.key));
  const nuspecFacets = availableFacets.filter(f => f.tier === "nuspec");
  const promotedFacets = availableFacets.filter(f => f.tier === "promoted");
  const deepenEnabled = state.selected.size > 0;

  const rail = `
    <aside class="query-facet-rail">
      ${renderFacetGroup("Instant (nuspec)", nuspecFacets, activeKeys, escapeHtml)}
      ${renderFacetGroup("Deepen (opens IL)", promotedFacets, activeKeys, escapeHtml)}
      <button data-query-deepen="1" ${deepenEnabled ? "" : "disabled"}>Deepen ${state.selected.size} selected →</button>
    </aside>`;

  const rows = state.outcome.rows
    .map(row => renderRow(row, state.selected.has(row.packageId), escapeHtml))
    .join("");

  return `
    ${failures}
    <div class="query-layout">
      ${rail}
      <div class="query-results">
        <div class="query-list">${rows}</div>
        ${renderCompletionFooter(state.outcome, escapeHtml)}
      </div>
    </div>`;
}
