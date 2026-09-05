import type {
  PackageQueryState,
  QueryFacetTerm,
  QueryResultRow,
  QuerySourceCatalog,
  QuerySourceSelection,
} from "./package-query.ts";
import { createQueryRequest } from "./package-query.ts";
import { renderBrand } from "./brand.ts";
import { focusRenderedElement } from "./scope-bar.ts";

const PACKAGE_QUERY_PRESSURE_DISTANCE_PX = 600;

export interface PackageQueryBindingActions {
  onBack: () => void;
  onCancel: () => void;
  onFacetToggle: (facetKey: string, prefix: string) => void;
  onPrefixInput: (prefix: string) => void;
  onResultPressure: () => void;
  onRowOpen: (packageId: string, version: string) => void;
  onRun: (prefix: string) => void;
  onSourceChange: (selection: QuerySourceSelection, searchText: string) => void;
}

export type PackageQueryFocusSnapshot =
  | {
      kind: "prefix";
      selectionStart: number | null;
      selectionEnd: number | null;
    }
  | { kind: "product" }
  | { kind: "back" }
  | { kind: "run" }
  | { kind: "type" | "order" | "prerelease" }
  | { kind: "facet"; facetKey: string }
  | { kind: "row"; packageId: string; version: string }
  | { kind: "cancel"; index: number }
  | { kind: "fallback" };

interface SelectableQueryElement extends HTMLElement {
  setSelectionRange(start: number, end: number): void;
}

function isFocusableQueryElement(
  element: Element | null,
): element is HTMLElement {
  return element !== null
    && "dataset" in element
    && "focus" in element
    && typeof element.focus === "function";
}

function supportsSelectionRange(
  element: HTMLElement,
): element is SelectableQueryElement {
  return "setSelectionRange" in element
    && typeof element.setSelectionRange === "function";
}

export function capturePackageQueryFocus(
  root: Document,
): PackageQueryFocusSnapshot | null {
  const active = root.activeElement;
  if (!isFocusableQueryElement(active)) return null;
  if (active === root.body) return null;
  if (active.id === "package-query-prefix") {
    return {
      kind: "prefix",
      selectionStart: "selectionStart" in active
        && typeof active.selectionStart === "number"
        ? active.selectionStart
        : null,
      selectionEnd: "selectionEnd" in active
        && typeof active.selectionEnd === "number"
        ? active.selectionEnd
        : null,
    };
  }
  if (active.id === "package-query-product") return { kind: "product" };
  if (active.id === "package-query-back") return { kind: "back" };
  if (active.id === "package-query-run") return { kind: "run" };
  if (active.id === "package-query-type") return { kind: "type" };
  if (active.id === "package-query-order") return { kind: "order" };
  if (active.id === "package-query-prerelease") return { kind: "prerelease" };
  if (active.dataset.queryFacet) {
    return { kind: "facet", facetKey: active.dataset.queryFacet };
  }
  if (active.dataset.queryRowOpen && active.dataset.queryRowVersion) {
    return {
      kind: "row",
      packageId: active.dataset.queryRowOpen,
      version: active.dataset.queryRowVersion,
    };
  }
  const cancelButtons = [
    ...root.querySelectorAll<HTMLElement>("[data-query-cancel]"),
  ];
  const cancelIndex = cancelButtons.findIndex(element => element === active);
  return cancelIndex >= 0
    ? { kind: "cancel", index: cancelIndex }
    : { kind: "fallback" };
}

export function restorePackageQueryFocus(
  root: ParentNode,
  snapshot: PackageQueryFocusSnapshot | null,
): "none" | "restored" | "fallback" {
  if (!snapshot) return "none";
  let target: Element | null;
  switch (snapshot.kind) {
    case "prefix":
      target = root.querySelector("#package-query-prefix");
      break;
    case "product":
      target = root.querySelector("#package-query-product");
      break;
    case "back":
      target = root.querySelector("#package-query-back");
      break;
    case "run":
      target = root.querySelector("#package-query-run");
      break;
    case "type":
    case "order":
    case "prerelease":
      target = root.querySelector(`#package-query-${snapshot.kind}`);
      break;
    case "facet":
      target = [...root.querySelectorAll<HTMLElement>("[data-query-facet]")]
        .find(element => element.dataset.queryFacet === snapshot.facetKey)
        ?? null;
      break;
    case "row":
      target = [...root.querySelectorAll<HTMLElement>("[data-query-row-open]")]
        .find(element =>
          element.dataset.queryRowOpen === snapshot.packageId
          && element.dataset.queryRowVersion === snapshot.version)
        ?? null;
      break;
    case "cancel":
      target = [
        ...root.querySelectorAll<HTMLElement>("[data-query-cancel]"),
      ][snapshot.index] ?? null;
      break;
    case "fallback":
      target = null;
      break;
  }
  let usedFallback = false;
  if (!isFocusableQueryElement(target) || !focusRenderedElement(target)) {
    target = root.querySelector("#package-query-prefix");
    usedFallback = true;
  }
  if (!isFocusableQueryElement(target)) return "none";
  if (usedFallback && !focusRenderedElement(target)) return "none";
  if (snapshot.kind === "prefix"
    && supportsSelectionRange(target)
    && snapshot.selectionStart !== null
    && snapshot.selectionEnd !== null) {
    target.setSelectionRange(snapshot.selectionStart, snapshot.selectionEnd);
  }
  return usedFallback ? "fallback" : "restored";
}

export function capturePackageQueryScroll(root: ParentNode): number | null {
  return root.querySelector<HTMLElement>(".query-main")?.scrollTop ?? null;
}

export function restorePackageQueryScroll(
  root: ParentNode,
  scrollTop: number | null,
): void {
  if (scrollTop === null) return;
  const main = root.querySelector<HTMLElement>(".query-main");
  if (main) main.scrollTop = scrollTop;
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
  root.querySelectorAll<HTMLElement>("[data-query-facet]").forEach(button =>
    button.addEventListener("click", () => actions.onFacetToggle(
      button.dataset.queryFacet ?? "",
      prefixInput()?.value ?? "")));
  const packageType = root.querySelector<HTMLSelectElement>(
    "#package-query-type");
  const sourceOrder = root.querySelector<HTMLSelectElement>(
    "#package-query-order");
  const prerelease = root.querySelector<HTMLInputElement>(
    "#package-query-prerelease");
  if (packageType && sourceOrder && prerelease) {
    const sourceChanged = () => actions.onSourceChange({
      packageType: packageType.value || null,
      sourceOrderId: sourceOrder.value || null,
      includePrerelease: prerelease.checked,
    }, prefixInput()?.value ?? "");
    packageType.addEventListener("change", sourceChanged);
    sourceOrder.addEventListener("change", sourceChanged);
    prerelease.addEventListener("change", sourceChanged);
  }
  bindPackageQueryStreamControls(root, actions);
  const queryMain = root.querySelector<HTMLElement>(".query-main");
  const reportResultPressure = () => {
    if (queryMain && packageQueryNeedsMoreMatches(queryMain)) {
      actions.onResultPressure();
    }
  };
  queryMain?.addEventListener("scroll", reportResultPressure);
  reportResultPressure();
  return {
    disconnect() {
      queryMain?.removeEventListener("scroll", reportResultPressure);
    },
  };
}

function bindPackageQueryStreamControls(
  root: ParentNode,
  actions: PackageQueryBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-query-row-open]").forEach(button =>
    button.addEventListener("click", () => actions.onRowOpen(
      button.dataset.queryRowOpen ?? "",
      button.dataset.queryRowVersion ?? "")));
  root.querySelectorAll<HTMLElement>("[data-query-cancel]").forEach(button =>
    button.addEventListener("click", actions.onCancel));
}

export function packageQueryNeedsMoreMatches(
  main: Pick<HTMLElement, "clientHeight" | "scrollHeight" | "scrollTop">,
): boolean {
  return main.scrollHeight - main.scrollTop - main.clientHeight
    <= PACKAGE_QUERY_PRESSURE_DISTANCE_PX;
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
        <span class="query-tier query-tier-${escapeHtml(row.tier)}">${escapeHtml(row.tier)}</span>
      </div>
      ${row.description?.trim()
        ? `<p class="query-row-description">${escapeHtml(row.description)}</p>`
        : ""}
      <ul class="query-evidence">${evidence}</ul>
      <div class="query-row-meta">
        <span>${row.totalDownloads === null
          ? "Lifetime downloads unavailable"
          : `${row.totalDownloads.toLocaleString()} lifetime downloads`}</span>
        ${row.producer
          ? `<span>${escapeHtml(row.producer)}</span>`
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

function renderFacets(
  facets: readonly QueryFacetTerm[],
  activeKeys: ReadonlySet<string>,
  escapeHtml: (value: unknown) => string,
): string {
  const renderedGroups = new Set<string>();
  return facets.map(facet => {
    if (!facet.displayGroupId) {
      return renderFacet(facet, activeKeys, escapeHtml);
    }
    if (renderedGroups.has(facet.displayGroupId)) return "";
    renderedGroups.add(facet.displayGroupId);
    const groupFacets = facets.filter(candidate =>
      candidate.displayGroupId === facet.displayGroupId);
    return `
      <div
        class="query-facet-group"
        role="group"
        aria-label="${escapeHtml(
          facet.displayGroupLabel ?? facet.label)}">
        ${groupFacets
          .map(groupFacet => renderFacet(
            groupFacet,
            activeKeys,
            escapeHtml))
          .join("")}
      </div>`;
  }).join("");
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
    <div class="query-footer">
      <span>${outcome.rows.length} package${outcome.rows.length === 1 ? "" : "s"} · ${label}</span>
      ${cancelButton}
    </div>`;
}

function renderSourceControls(
  catalog: QuerySourceCatalog | null,
  selection: QuerySourceSelection,
  escapeHtml: (value: unknown) => string,
): string {
  if (!catalog) return "";
  const packageTypes = catalog.packageType.suggestions.map(suggestion => `
    <option value="${escapeHtml(suggestion.value)}"${selection.packageType === suggestion.value ? " selected" : ""}>${escapeHtml(suggestion.label)}</option>`).join("");
  const customType = selection.packageType !== null
    && !catalog.packageType.suggestions.some(
      suggestion => suggestion.value === selection.packageType)
    ? `<option value="${escapeHtml(selection.packageType)}" selected>${escapeHtml(selection.packageType)}</option>`
    : "";
  const orders = catalog.orders.map(order => `
    <option value="${escapeHtml(order.id)}" title="${escapeHtml(order.summary)}"${selection.sourceOrderId === order.id ? " selected" : ""}>${escapeHtml(order.label)}</option>`).join("");
  const orderSummary = selection.sourceOrderId === null
    ? "Automatic uses the source default for this search."
    : catalog.orders.find(order => order.id === selection.sourceOrderId)?.summary
      ?? `Unavailable source order: ${selection.sourceOrderId}`;
  const unavailableOrder = selection.sourceOrderId !== null
    && !catalog.orders.some(order => order.id === selection.sourceOrderId)
    ? `<option value="${escapeHtml(selection.sourceOrderId)}" selected>${escapeHtml(selection.sourceOrderId)} (unavailable)</option>`
    : "";
  return `
    <div class="query-source-controls" role="group" aria-label="Gallery filters">
      <h2>Gallery filters</h2>
      <label for="package-query-type">${escapeHtml(catalog.packageType.label)}
        <select id="package-query-type" aria-describedby="package-query-type-description">
          <option value=""${selection.packageType === null ? " selected" : ""}>All package types</option>
          ${packageTypes}${customType}
        </select>
      </label>
      <p id="package-query-type-description">${escapeHtml(catalog.packageType.summary)}</p>
      <label for="package-query-order">Source order
        <select id="package-query-order" aria-describedby="package-query-order-description">
          <option value=""${selection.sourceOrderId === null ? " selected" : ""}>Automatic</option>
          ${orders}${unavailableOrder}
        </select>
      </label>
      <p id="package-query-order-description">${escapeHtml(orderSummary)}</p>
      <label for="package-query-prerelease">
        <input id="package-query-prerelease" type="checkbox"${selection.includePrerelease ? " checked" : ""} />
        Include prerelease
      </label>
    </div>`;
}

function renderStreamingCancel(
  state: PackageQueryState,
): string {
  return state.outcome.completion.kind === "streaming" && state.request
    ? `<button type="button" class="query-bar-cancel" data-query-cancel="1">Cancel</button>`
    : "";
}

function renderProgress(
  outcome: PackageQueryState["outcome"],
  escapeHtml: (value: unknown) => string,
): string {
  if (outcome.completion.kind !== "streaming" || outcome.progress.length === 0)
    return "";

  const checkpoints = outcome.progress.map(progress => {
    const label = progress.phase === "search"
      ? "Source search"
      : progress.phase === "manifest"
        ? "Manifests"
        : "Package content";
    const detail = progress.phase === "search"
      ? progress.completed === progress.limit ? "ready" : "running"
      : `${progress.completed.toLocaleString()} of up to ${progress.limit.toLocaleString()}`;
    return `
      <div class="query-progress-item">
        <div><span>${escapeHtml(label)}</span><strong>${escapeHtml(detail)}</strong></div>
        <progress value="${progress.completed}" max="${progress.limit}"></progress>
      </div>`;
  }).join("");
  return `
    <div class="query-progress" aria-label="Query progress">
      ${checkpoints}
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
        <p>Leave search blank to browse, or enter Gallery search text. Narrow the source with Gallery filters; add inspection facets only when needed.</p>
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
        <p>Scope: ${escapeHtml(completion.reason)}. This is not the whole source.${state.outcome.failures.length ? " Some source work also failed, so this is not a confirmed empty result within that bound." : ""}</p>
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
      <p>Try a broader search, browse without search text, or select fewer inspection facets.</p>
    </section>`;
}

export interface RenderPackageQueryOptions {
  state: PackageQueryState;
  prefix?: string;
  availableFacets: readonly QueryFacetTerm[];
  sourceCatalog?: QuerySourceCatalog | null;
  navigationError?: string;
  escapeHtml: (value: unknown) => string;
}

function renderFailures(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  return state.outcome.failures.length
    ? `
      <section class="query-failures">
        <strong>Some package source work failed</strong>
        <ul>${state.outcome.failures
          .map(failure => `<li>${escapeHtml(failure)}</li>`)
          .join("")}</ul>
      </section>`
    : "";
}

function renderResults(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  const rows = state.outcome.rows
    .map(row => renderRow(row, escapeHtml))
    .join("");
  return rows
    ? `${renderProgress(state.outcome, escapeHtml)}<div class="query-list">${rows}</div>${renderCompletionFooter(state.outcome, escapeHtml)}`
    : state.outcome.completion.kind === "streaming" && state.request
      ? `<section class="query-empty query-running"><span class="loader" aria-hidden="true"></span><h2>Searching nuget.org</h2><p>Matches will appear as package candidates are evaluated.</p></section>${renderProgress(state.outcome, escapeHtml)}${renderCompletionFooter(state.outcome, escapeHtml)}`
      : renderEmptyState(state, escapeHtml);
}

export function patchPackageQueryStream(
  root: ParentNode,
  options: Pick<RenderPackageQueryOptions, "state" | "escapeHtml">,
  actions: PackageQueryBindingActions,
): boolean {
  const failures = root.querySelector<HTMLElement>(
    "#package-query-failure-region");
  const cancel = root.querySelector<HTMLElement>(
    "#package-query-cancel-region");
  const results = root.querySelector<HTMLElement>(
    "#package-query-results");
  if (!failures || !cancel || !results) return false;

  failures.innerHTML = renderFailures(options.state, options.escapeHtml);
  cancel.innerHTML = renderStreamingCancel(options.state);
  results.innerHTML = renderResults(options.state, options.escapeHtml);
  bindPackageQueryStreamControls(root, actions);

  const queryMain = root.querySelector<HTMLElement>(".query-main");
  if (queryMain && packageQueryNeedsMoreMatches(queryMain)) {
    actions.onResultPressure();
  }
  return true;
}

export function renderPackageQueryView(
  options: RenderPackageQueryOptions,
): string {
  const {
    state,
    prefix = state.request?.scopeQuery ?? "",
    availableFacets,
    sourceCatalog = null,
    navigationError = "",
    escapeHtml,
  } = options;
  const activeKeys = new Set(state.request?.facets.map(facet => facet.key) ?? []);
  const facets = renderFacets(availableFacets, activeKeys, escapeHtml);
  const failures = renderFailures(state, escapeHtml);
  const results = renderResults(state, escapeHtml);
  const request = state.request ?? createQueryRequest("");

  return `
    <div class="query-page">
      <header class="query-page-bar">
        ${renderBrand({ id: "package-query-product" })}
        <div class="query-page-navigation">
          <button id="package-query-back" type="button">Back</button>
        </div>
      </header>
      <main class="query-main">
        <div class="query-heading">
          <p class="query-kicker">Gallery discovery + optional inspection · nuget.org</p>
          <h1 id="package-query-heading" tabindex="-1">Package query</h1>
          <p>Browse or search using basic search metadata. Manifests and package content are acquired only with explicit inspection facets.</p>
        </div>
        <form id="package-query-form" class="query-bar" role="search">
          <label for="package-query-prefix">Search (optional)</label>
          <input id="package-query-prefix" name="search" value="${escapeHtml(prefix)}" autocomplete="off" spellcheck="false" placeholder="Search packages or leave blank to browse" maxlength="100" />
          <button id="package-query-run" type="submit">Run query</button>
          <span id="package-query-cancel-region">${renderStreamingCancel(state)}</span>
        </form>
        ${navigationError
          ? `<div class="query-navigation-error">${escapeHtml(navigationError)}</div>`
          : ""}
        <div id="package-query-failure-region">${failures}</div>
        <div class="query-layout">
          <aside class="query-facet-rail" aria-label="Package query controls">
            ${renderSourceControls(sourceCatalog, request, escapeHtml)}
            <h2>Inspection facets</h2>
            <p>Every change starts a fresh request.</p>
            <div class="query-facets">${facets}</div>
            <p class="query-facet-disclosure">Content facets download up to 20 candidate package archives.</p>
            <p class="query-facet-disclosure">Source capacity K: ${request.requestedLimit.toLocaleString()} candidates. Maximum matches N: ${request.requestedMatchLimit.toLocaleString()}. The match limit does not change source capacity.</p>
            <p class="query-facet-disclosure">Match counts and lifetime downloads describe a bounded response, not global top-N.</p>
          </aside>
          <section id="package-query-results" class="query-results" aria-label="Package query results">
            ${results}
          </section>
        </div>
      </main>
    </div>`;
}
