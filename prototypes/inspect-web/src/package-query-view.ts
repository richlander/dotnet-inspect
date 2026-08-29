import type {
  PackageQueryState,
  QueryFacetTerm,
  QueryResultRow,
} from "./package-query.ts";

export interface PackageQueryBindingActions {
  onBack: () => void;
  onCancel: () => void;
  onFacetToggle: (facetKey: string, prefix: string) => void;
  onPrefixInput: (prefix: string) => void;
  onRowOpen: (
    packageId: string,
    version: string,
    invokingButton: HTMLElement,
  ) => void;
  onRun: (prefix: string) => void;
}

export function bindPackageQueryView(
  root: ParentNode,
  actions: PackageQueryBindingActions,
) {
  const prefixInput = () =>
    root.querySelector<HTMLInputElement>("#package-query-prefix");

  root.querySelector("#package-query-back")
    ?.addEventListener("click", actions.onBack);
  root.querySelector<HTMLFormElement>("#package-query-form")
    ?.addEventListener("submit", event => {
      event.preventDefault();
      actions.onRun(prefixInput()?.value ?? "");
    });
  prefixInput()?.addEventListener("input", event => {
    const input = event.currentTarget;
    if (input instanceof HTMLInputElement) actions.onPrefixInput(input.value);
  });
  root.querySelectorAll<HTMLElement>("[data-query-row-open]").forEach(button =>
    button.addEventListener("click", () => actions.onRowOpen(
      button.dataset.queryRowOpen ?? "",
      button.dataset.queryRowVersion ?? "",
      button)));
  root.querySelectorAll<HTMLElement>("[data-query-facet]").forEach(button =>
    button.addEventListener("click", () => actions.onFacetToggle(
      button.dataset.queryFacet ?? "",
      prefixInput()?.value ?? "")));
  root.querySelectorAll<HTMLElement>("[data-query-cancel]").forEach(button =>
    button.addEventListener("click", actions.onCancel));
}

function renderRow(
  row: QueryResultRow,
  escapeHtml: (value: unknown) => string,
): string {
  const evidence = row.evidence
    .map(item => `<li>${escapeHtml(item)}</li>`)
    .join("");
  return `
    <article class="query-row">
      <div class="query-row-head">
        <div>
          <h2>${escapeHtml(row.packageId)}</h2>
          <span class="query-row-version">${escapeHtml(row.version)}</span>
        </div>
        <span class="query-tier query-tier-nuspec">nuspec</span>
      </div>
      <ul class="query-evidence">${evidence}</ul>
      <div class="query-row-meta">
        <span>${row.totalDownloads.toLocaleString()} downloads</span>
        ${row.producer
          ? `<span title="${escapeHtml(row.producer)}">nuget.org</span>`
          : ""}
        <button type="button" data-query-row-open="${escapeHtml(row.packageId)}" data-query-row-version="${escapeHtml(row.version)}">Open in workspace</button>
      </div>
    </article>`;
}

function renderFacet(
  facet: QueryFacetTerm,
  activeKeys: ReadonlySet<string>,
  escapeHtml: (value: unknown) => string,
): string {
  const active = activeKeys.has(facet.key);
  return `
    <button
      type="button"
      class="query-facet ${active ? "active" : ""}"
      data-query-facet="${escapeHtml(facet.key)}"
      aria-pressed="${active}"
      title="${escapeHtml(facet.summary ?? facet.label)}">
      ${escapeHtml(facet.label)}
    </button>`;
}

function renderCompletionFooter(
  outcome: PackageQueryState["outcome"],
  escapeHtml: (value: unknown) => string,
): string {
  const { completion } = outcome;
  const partialFailure = outcome.failures.length > 0;
  const label = completion.kind === "streaming"
    ? "streaming…"
    : completion.kind === "bounded"
      ? `bounded: ${escapeHtml(completion.reason)}`
      : completion.kind === "exhausted"
        ? partialFailure
          ? "all matches from the source work that succeeded"
          : "all matches"
        : completion.kind === "failed"
          ? `failed: ${escapeHtml(completion.reason)}`
          : "cancelled";
  const cancelButton = completion.kind === "streaming"
    ? `<button type="button" data-query-cancel="1">Cancel</button>`
    : "";
  return `
    <div class="query-footer" role="status">
      <span>${outcome.rows.length} package${outcome.rows.length === 1 ? "" : "s"} · ${label}</span>
      ${cancelButton}
    </div>`;
}

function renderEmptyState(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  const completion = state.outcome.completion;
  if (!state.request) {
    return `
      <section class="query-empty">
        <span class="large-glyph">⌕</span>
        <h2>Query nuget.org</h2>
        <p>Enter a package ID prefix, then narrow the live result stream with nuspec facets. No package archive is downloaded.</p>
      </section>`;
  }
  if (completion.kind === "cancelled") {
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>Cancelled before any matches</h2>
        <p>This was stopped before it found anything, so it is not a confirmed empty result.</p>
      </section>`;
  }
  if (completion.kind === "failed") {
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>Query failed</h2>
        <p>${escapeHtml(completion.reason)} This is not a confirmed empty result.</p>
      </section>`;
  }
  if (completion.kind === "bounded") {
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>No matches within the bound</h2>
        <p>Searched ${escapeHtml(completion.reason)}, not the whole source.${state.outcome.failures.length ? " Some source work also failed, so this is not a confirmed empty result within that bound." : ""}</p>
      </section>`;
  }
  if (state.outcome.failures.length) {
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>No matches found with failures</h2>
        <p>Some source work failed, so this is not a confirmed empty result.</p>
      </section>`;
  }
  return `
    <section class="query-empty">
      <span class="large-glyph">◇</span>
      <h2>No matches</h2>
      <p>Try a broader prefix or fewer facets.</p>
    </section>`;
}

export interface RenderPackageQueryOptions {
  state: PackageQueryState;
  prefix?: string;
  availableFacets: readonly QueryFacetTerm[];
  navigationError?: string;
  escapeHtml: (value: unknown) => string;
}

export function renderPackageQueryView(
  options: RenderPackageQueryOptions,
): string {
  const {
    state,
    prefix = state.request?.scopeQuery ?? "",
    availableFacets,
    navigationError = "",
    escapeHtml,
  } = options;
  const activeKeys = new Set(state.request?.facets.map(facet => facet.key) ?? []);
  const facets = availableFacets
    .map(facet => renderFacet(facet, activeKeys, escapeHtml))
    .join("");
  const failures = state.outcome.failures.length
    ? `
      <section class="query-failures" role="alert">
        <strong>Some package source work failed</strong>
        <ul>${state.outcome.failures
          .map(failure => `<li>${escapeHtml(failure)}</li>`)
          .join("")}</ul>
      </section>`
    : "";
  const rows = state.outcome.rows
    .map(row => renderRow(row, escapeHtml))
    .join("");
  const results = rows
    ? `<div class="query-list">${rows}</div>${renderCompletionFooter(state.outcome, escapeHtml)}`
    : state.outcome.completion.kind === "streaming" && state.request
      ? `<section class="query-empty query-running" role="status"><span class="loader" aria-hidden="true"></span><h2>Searching nuget.org</h2><p>Matches will appear as their manifests are evaluated.</p></section>${renderCompletionFooter(state.outcome, escapeHtml)}`
      : renderEmptyState(state, escapeHtml);

  return `
    <div class="query-page">
      <header class="query-page-bar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        <button id="package-query-back" type="button">Back</button>
      </header>
      <main class="query-main">
        <div class="query-heading">
          <p class="query-kicker">nuspec-only · nuget.org</p>
          <h1 id="package-query-heading" tabindex="-1">Package query</h1>
          <p>Find packages by product-owned manifest and source facets without downloading package archives.</p>
        </div>
        <form id="package-query-form" class="query-bar" role="search">
          <label for="package-query-prefix">Package ID prefix</label>
          <input id="package-query-prefix" name="prefix" value="${escapeHtml(prefix)}" autocomplete="off" spellcheck="false" placeholder="Microsoft.Extensions." required maxlength="100" />
          <button type="submit">Run query</button>
          ${state.outcome.completion.kind === "streaming" && state.request
            ? `<button type="button" class="query-bar-cancel" data-query-cancel="1">Cancel</button>`
            : ""}
        </form>
        ${navigationError
          ? `<div class="query-navigation-error" role="alert">${escapeHtml(navigationError)}</div>`
          : ""}
        ${failures}
        <div class="query-layout">
          <aside class="query-facet-rail" aria-label="Package query facets">
            <h2>Facets</h2>
            <p>Every change starts a fresh request.</p>
            <div class="query-facets">${facets}</div>
          </aside>
          <section class="query-results" aria-label="Package query results">
            ${results}
          </section>
        </div>
      </main>
    </div>`;
}
