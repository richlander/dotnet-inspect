import type {
  PackageQueryState,
  QueryAssemblyPatternDescriptor,
  QueryFacetTerm,
  QueryRequest,
  QueryResultRow,
  QuerySourceSelection,
} from "./package-query.ts";
import {
  createAssemblyQueryRequest,
  createQueryRequest,
} from "./package-query.ts";
import {
  resolvePackageQueryRowWindow,
  type PackageQueryViewportSnapshot,
} from "./package-query-window.ts";
import { renderBrand } from "./brand.ts";
import { focusRenderedElement } from "./scope-bar.ts";

const PACKAGE_QUERY_PRESSURE_DISTANCE_PX = 600;
const DEFAULT_ASSEMBLY_QUERY_TARGET_FRAMEWORK = "net10.0";
const MAX_ASSEMBLY_QUERY_PACKAGES = 5;

export interface PackageQueryBindingActions {
  onBack: () => void;
  onCancel: () => void;
  onAssemblyRun?: (request: QueryRequest) => void;
  onFacetToggle: (facetKey: string, prefix: string) => void;
  onPrefixInput: (prefix: string) => void;
  onResultPressure: () => void;
  onResultViewportChange: () => void;
  onRowOpen: (
    packageId: string,
    version: string,
    rootRequest?: string,
  ) => void;
  onRun: (prefix: string) => void;
  onSourceChange: (
    selection: Partial<QuerySourceSelection>,
    searchText: string,
  ) => void;
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
  | { kind: "results" }
  | { kind: "prerelease" }
  | {
      kind: "assembly";
      control: "pattern" | "packages" | "operand" | "tfm" | "run";
    }
  | { kind: "facet"; facetKey: string }
  | { kind: "row"; packageId: string; version: string }
  | { kind: "cancel"; index: number }
  | { kind: "fallback" };

interface SelectableQueryElement extends HTMLElement {
  setSelectionRange(start: number, end: number): void;
}

interface AssemblyQueryControl extends HTMLElement {
  value: string;
  setCustomValidity(message: string): void;
  reportValidity(): boolean;
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
  if (active.id === "package-query-results") return { kind: "results" };
  if (active.id === "package-query-prerelease") return { kind: "prerelease" };
  const assemblyControl = assemblyControlName(active.id);
  if (assemblyControl) {
    return { kind: "assembly", control: assemblyControl };
  }
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
    case "results":
      target = root.querySelector("#package-query-results");
      break;
    case "prerelease":
      target = root.querySelector(`#package-query-${snapshot.kind}`);
      break;
    case "assembly":
      target = root.querySelector(
        `#package-query-assembly-${snapshot.control}`);
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
  if (!isFocusableQueryElement(target)
    || !focusRenderedElement(target, { preventScroll: true })) {
    target = root.querySelector(
      snapshot.kind === "row"
        ? "#package-query-results"
        : "#package-query-prefix");
    usedFallback = true;
  }
  if (!isFocusableQueryElement(target)) return "none";
  if (usedFallback
    && !focusRenderedElement(target, { preventScroll: true })) return "none";
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
  const prerelease = root.querySelector<HTMLInputElement>(
    "#package-query-prerelease");
  prerelease?.addEventListener("change", () => actions.onSourceChange({
    includePrerelease: prerelease.checked,
  }, prefixInput()?.value ?? ""));
  bindAssemblyQueryControls(root, actions);
  bindPackageQueryStreamControls(root, actions);
  const queryMain = root.querySelector<HTMLElement>(".query-main");
  const reportResultPressure = () => {
    if (queryMain && packageQueryNeedsMoreMatches(queryMain)) {
      actions.onResultPressure();
    }
  };
  const handleResultScroll = () => {
    reportResultPressure();
    actions.onResultViewportChange();
  };
  queryMain?.addEventListener("scroll", handleResultScroll);
  reportResultPressure();
  return {
    disconnect() {
      queryMain?.removeEventListener("scroll", handleResultScroll);
    },
  };
}

function assemblyControlName(
  id: string,
): "pattern" | "packages" | "operand" | "tfm" | "run" | null {
  const prefix = "package-query-assembly-";
  if (!id.startsWith(prefix)) return null;
  const control = id.slice(prefix.length);
  switch (control) {
    case "pattern":
    case "packages":
    case "operand":
    case "tfm":
    case "run":
      return control;
    default:
      return null;
  }
}

function bindAssemblyQueryControls(
  root: ParentNode,
  actions: PackageQueryBindingActions,
): void {
  const form = root.querySelector<HTMLFormElement>(
    "#package-query-assembly-form");
  if (!form) return;
  const editableControls = [
    root.querySelector<HTMLSelectElement>(
      "#package-query-assembly-pattern"),
    root.querySelector<HTMLTextAreaElement>(
      "#package-query-assembly-packages"),
    root.querySelector<HTMLInputElement>(
      "#package-query-assembly-operand"),
    root.querySelector<HTMLInputElement>(
      "#package-query-assembly-tfm"),
  ];
  for (const control of editableControls) {
    if (!control) continue;
    const clearError = () => control.setCustomValidity("");
    control.addEventListener("input", clearError);
    control.addEventListener("change", clearError);
  }
  form.addEventListener("submit", event => {
    event.preventDefault();
    const pattern = root.querySelector<HTMLSelectElement>(
      "#package-query-assembly-pattern");
    const packages = root.querySelector<HTMLTextAreaElement>(
      "#package-query-assembly-packages");
    const operand = root.querySelector<HTMLInputElement>(
      "#package-query-assembly-operand");
    const targetFramework = root.querySelector<HTMLInputElement>(
      "#package-query-assembly-tfm");
    if (!pattern || !packages || !operand || !targetFramework) {
      throw new Error("Assembly query controls are incomplete.");
    }
    clearCustomValidity([
      pattern,
      packages,
      operand,
      targetFramework,
    ]);
    const selectedPattern = pattern.selectedOptions.item(0);
    const maximumPackages = Number(
      selectedPattern?.dataset.maximumPackages ?? 0);
    const maximumOperandLength = Number(
      selectedPattern?.dataset.maximumOperandLength ?? 0);
    if (!pattern.value || maximumPackages <= 0 || maximumOperandLength <= 0) {
      reportControlError(
        pattern,
        "Select an available assembly pattern.");
      return;
    }
    const coordinates = parseExactPackageCoordinates(
      packages.value,
      maximumPackages);
    if (typeof coordinates === "string") {
      reportControlError(packages, coordinates);
      return;
    }
    if (operand.value.length === 0) {
      reportControlError(operand, "Enter a literal operand.");
      return;
    }
    if (operand.value.length > maximumOperandLength) {
      reportControlError(
        operand,
        `The literal operand must be at most ${maximumOperandLength.toLocaleString()} characters.`);
      return;
    }
    const framework = targetFramework.value.trim();
    if (!framework) {
      reportControlError(
        targetFramework,
        "Enter a target framework.");
      return;
    }
    if (!actions.onAssemblyRun) {
      reportControlError(
        pattern,
        "Assembly-pattern package queries are unavailable.");
      return;
    }
    actions.onAssemblyRun(createAssemblyQueryRequest(
      pattern.value,
      operand.value,
      coordinates,
      framework));
  });
}

function clearCustomValidity(
  controls: readonly AssemblyQueryControl[],
): void {
  for (const control of controls) control.setCustomValidity("");
}

function reportControlError(
  control: AssemblyQueryControl,
  message: string,
): void {
  control.setCustomValidity(message);
  control.reportValidity();
}

export function parseExactPackageCoordinates(
  value: string,
  maximumPackages: number,
): readonly string[] | string {
  const coordinates = value
    .split(/\r?\n/)
    .map(coordinate => coordinate.trim())
    .filter(Boolean);
  if (coordinates.length === 0) {
    return "Enter at least one exact package as ID@VERSION.";
  }
  if (coordinates.length > maximumPackages) {
    return `Enter no more than ${maximumPackages.toLocaleString()} packages.`;
  }
  if (coordinates.some(coordinate => !/^[^@\s]+@[^@\s]+$/.test(coordinate))) {
    return "Enter one exact ID@VERSION package per line.";
  }
  return coordinates;
}

function bindPackageQueryStreamControls(
  root: ParentNode,
  actions: PackageQueryBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-query-row-open]").forEach(button =>
    button.addEventListener("click", () => actions.onRowOpen(
      button.dataset.queryRowOpen ?? "",
      button.dataset.queryRowVersion ?? "",
      button.dataset.queryRootRequest)));
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
  index: number,
  rowCount: number,
  escapeHtml: (value: unknown) => string,
): string {
  const evidence = row.evidence
    .filter(item => item.scope === "package")
    .map(item => `<li>${escapeHtml(item.text)}</li>`)
    .join("");
  const rootRequest = row.tier === "assembly" ? row.rootRequest : undefined;
  const openAction = row.tier === "assembly" && !rootRequest?.trim()
    ? `<span>Workspace opening request unavailable</span>`
    : `<button type="button" data-query-row-open="${escapeHtml(row.packageId)}" data-query-row-version="${escapeHtml(row.version)}"${rootRequest
        ? ` data-query-root-request="${escapeHtml(rootRequest)}"`
        : ""}>Open in workspace</button>`;
  return `
    <article class="query-row"
      role="listitem"
      aria-posinset="${index + 1}"
      aria-setsize="${rowCount}"
      data-query-row-index="${index}">
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
      ${evidence ? `<ul class="query-evidence">${evidence}</ul>` : ""}
      <div class="query-row-meta">
        <span>${row.tier === "assembly"
          ? "Selector-issued primary implementation assembly"
          : row.totalDownloads === null
            ? "Lifetime downloads unavailable"
            : `${row.totalDownloads.toLocaleString()} lifetime downloads`}</span>
        ${row.producer
          ? `<span>${escapeHtml(row.producer)}</span>`
          : ""}
        ${openAction}
      </div>
    </article>`;
}

function renderAssemblyControls(
  patterns: readonly QueryAssemblyPatternDescriptor[],
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  if (patterns.length === 0) return "";
  const active = state.request?.assemblyPattern;
  const selected = patterns.find(pattern => pattern.id === active?.patternId)
    ?? patterns[0]!;
  const maximumPackages = Math.min(
    selected.maximumPackages,
    MAX_ASSEMBLY_QUERY_PACKAGES);
  const unavailablePattern = active
    && !patterns.some(pattern => pattern.id === active.patternId)
    ? `<option value="${escapeHtml(active.patternId)}" selected disabled>Unavailable pattern</option>`
    : "";
  const options = patterns.map(pattern => `
    <option
      value="${escapeHtml(pattern.id)}"
      data-maximum-operand-length="${pattern.maximumOperandLength}"
      data-maximum-packages="${Math.min(
        pattern.maximumPackages,
        MAX_ASSEMBLY_QUERY_PACKAGES)}"${pattern.id === selected.id && !unavailablePattern ? " selected" : ""}>
      ${escapeHtml(pattern.label)}
    </option>`).join("");
  return `
    <details class="query-source-controls query-assembly-controls"${active ? " open" : ""}>
      <summary>Assembly patterns</summary>
      <p>Explicit bounded analysis of the selector-issued primary implementation assembly. It does not search every assembly in a package.</p>
      <form id="package-query-assembly-form">
        <label for="package-query-assembly-pattern">Pattern
          <select id="package-query-assembly-pattern" aria-describedby="package-query-assembly-summary">
            ${unavailablePattern}${options}
          </select>
        </label>
        <p id="package-query-assembly-summary">${escapeHtml(selected.summary)}</p>
        <label for="package-query-assembly-packages">Exact packages
          <textarea id="package-query-assembly-packages" rows="${maximumPackages}" required aria-describedby="package-query-assembly-packages-description" placeholder="Package.Id@1.2.3">${escapeHtml(active?.packageCoordinates.join("\n") ?? "")}</textarea>
        </label>
        <p id="package-query-assembly-packages-description">One exact ID@VERSION per line, up to ${maximumPackages.toLocaleString()}.</p>
        <label for="package-query-assembly-operand">Literal operand
          <input id="package-query-assembly-operand" type="text" required maxlength="${selected.maximumOperandLength}" value="${escapeHtml(active?.operand ?? "")}" autocomplete="off" spellcheck="false" />
        </label>
        <label for="package-query-assembly-tfm">Target framework
          <input id="package-query-assembly-tfm" type="text" required value="${escapeHtml(active?.targetFramework ?? DEFAULT_ASSEMBLY_QUERY_TARGET_FRAMEWORK)}" autocomplete="off" spellcheck="false" />
        </label>
        <button id="package-query-assembly-run" type="submit">Run assembly query</button>
      </form>
    </details>`;
}

function renderQueryContext(
  rows: readonly QueryResultRow[],
  escapeHtml: (value: unknown) => string,
): string {
  const evidence = rows[0]?.evidence
    .filter(item => item.scope === "query")
    .map(item => `<li>${escapeHtml(item.text)}</li>`)
    .join("") ?? "";
  return evidence
    ? `<section class="query-context" aria-label="Query context"><h2>Query context</h2><ul class="query-evidence">${evidence}</ul></section>`
    : "";
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
  request: QueryRequest | null,
  outcome: PackageQueryState["outcome"],
  escapeHtml: (value: unknown) => string,
): string {
  const { completion } = outcome;
  const partialFailure = outcome.failures.length > 0;
  const label = completion.kind === "streaming"
    ? "streaming…"
    : completion.kind === "idle"
      ? "idle"
    : completion.kind === "bounded"
      ? `bounded: ${escapeHtml(completion.reason)}`
      : completion.kind === "exhausted"
        ? partialFailure
          ? "all matches from the source work that succeeded"
          : "all matches"
        : completion.kind === "exact"
          ? "exact package selection complete"
        : completion.kind === "failed"
          ? `failed: ${escapeHtml(completion.reason)}`
          : "cancelled";
  const cancelButton = completion.kind === "streaming"
    ? `<button type="button" data-query-cancel="1">Cancel</button>`
    : "";
  const resultLabel = request?.assemblyPattern
    ? `assembly match${outcome.rows.length === 1 ? "" : "es"}`
    : `package${outcome.rows.length === 1 ? "" : "s"}`;
  return `
    <div class="query-footer">
      <span>${outcome.rows.length} ${resultLabel} · ${label}</span>
      ${cancelButton}
    </div>`;
}

function renderPackageOptions(request: QueryRequest): string {
  return `
    <div class="query-source-controls" role="group" aria-label="Package query options">
      <h2>Package options</h2>
      <label for="package-query-prerelease">
        <input id="package-query-prerelease" type="checkbox"${request.includePrerelease ? " checked" : ""} />
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
      ? "Source acquisition"
      : progress.phase === "manifest"
        ? "Manifests"
        : progress.phase === "package-content"
          ? "Package content"
          : "Selected assemblies";
    const detail = progress.phase === "search"
      ? progress.completed === progress.limit ? "ready" : "running"
      : progress.phase === "assembly"
        ? `${progress.completed.toLocaleString()} of ${progress.limit.toLocaleString()}`
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
  if (state.request?.assemblyPattern) {
    if (completion.kind === "cancelled") {
      return `
        <section class="query-empty">
          <span class="large-glyph">◇</span>
          <h2>Assembly query cancelled</h2>
          <p>This was stopped before every selected package was assessed.</p>
        </section>`;
    }
    if (completion.kind === "failed") {
      return `
        <section class="query-empty">
          <span class="large-glyph">◇</span>
          <h2>Assembly query failed</h2>
          <p>${escapeHtml(completion.reason)} This is not a semantic no-match result.</p>
        </section>`;
    }
    if (completion.kind !== "streaming") {
      const completionScope = completion.kind === "bounded"
        ? ` Scope: ${escapeHtml(completion.reason)}.`
        : "";
      return `
        <section class="query-empty">
          <span class="large-glyph">◇</span>
          <h2>No selected assembly matches</h2>
          <p>Each outcome applies only to the selector-issued primary implementation assembly. It is not a package-wide absence claim.${completionScope}</p>
        </section>`;
    }
  }
  if (!state.request) {
    return `
      <section class="query-empty">
        <span class="large-glyph">⌕</span>
        <h2>Select package input</h2>
        <p>Enter an exact package ID or add one terminal <code>*</code> for a literal prefix.</p>
      </section>`;
  }
  if (completion.kind === "idle") {
    return `
      <section class="query-empty">
        <span class="large-glyph">⌕</span>
        <h2>Ready to query</h2>
        <p>Enter a package ID or terminal-star prefix. Selected inspection facets remain configured.</p>
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
  if (completion.kind === "exact") {
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>${state.outcome.failures.length ? "Exact package inspection incomplete" : "No package selected"}</h2>
        <p>${state.outcome.failures.length
          ? "Some required inspection work failed, so this is not a confirmed empty result."
          : "The exact package lookup completed without a matching result."} No fallback search was used.</p>
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
      <p>Try a broader explicit prefix or select fewer inspection facets.</p>
    </section>`;
}

export interface RenderPackageQueryOptions {
  state: PackageQueryState;
  prefix?: string;
  viewport?: PackageQueryViewportSnapshot | null;
  availableFacets: readonly QueryFacetTerm[];
  availableAssemblyPatterns?: readonly QueryAssemblyPatternDescriptor[];
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
        <strong>${state.request?.assemblyPattern
          ? "Some selected-assembly work failed"
          : "Some package source work failed"}</strong>
        <ul>${state.outcome.failures
          .map(failure => `<li>${escapeHtml(failure)}</li>`)
          .join("")}</ul>
      </section>`
    : "";
}

function renderAssessments(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  if (state.outcome.assessments.length === 0) return "";
  return `
    <section class="query-assessments">
      <strong>Selected assembly outcomes</strong>
      <p>These outcomes cover only each package's selector-issued primary implementation assembly, not the whole package.</p>
      <ul>${state.outcome.assessments.map(assessment => `
        <li>
          <strong>${escapeHtml(assessment.packageId)}@${escapeHtml(assessment.version)} · ${assessment.disposition === "NoMatch" ? "No match" : "Not applicable"}</strong>
          <span>${escapeHtml(assessment.message)}</span>
          ${assessment.assetPath
            ? `<span>Selected assembly: ${escapeHtml(assessment.assetPath)}</span>`
            : ""}
        </li>`).join("")}</ul>
    </section>`;
}

function renderResults(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
  viewport: PackageQueryViewportSnapshot | null = null,
): string {
  const rowCount = state.outcome.rows.length;
  const window = resolvePackageQueryRowWindow(rowCount, viewport);
  const rows = state.outcome.rows
    .slice(window.start, window.end)
    .map((row, offset) =>
      renderRow(row, window.start + offset, rowCount, escapeHtml))
    .join("");
  const assessments = renderAssessments(state, escapeHtml);
  const renderedRows = rows
    ? `<div
        id="package-query-row-window"
        class="query-row-window"
        data-query-row-extent="${window.rowExtent.toFixed(2)}">
        <div
          class="query-row-spacer"
          aria-hidden="true"
          style="height:${window.beforeHeight.toFixed(2)}px"></div>
        <div class="query-list"
          role="list"
          aria-label="Packages ${window.start + 1} through ${window.end} of ${rowCount}">
          ${rows}
        </div>
        <div
          class="query-row-spacer"
          aria-hidden="true"
          style="height:${window.afterHeight.toFixed(2)}px"></div>
      </div>`
    : "";
  return rows
    ? `${renderProgress(state.outcome, escapeHtml)}${assessments}${renderQueryContext(state.outcome.rows, escapeHtml)}${renderedRows}${renderCompletionFooter(state.request, state.outcome, escapeHtml)}`
    : state.outcome.completion.kind === "streaming" && state.request
      ? `<section class="query-empty query-running"><span class="loader" aria-hidden="true"></span><h2>${state.request.assemblyPattern ? "Evaluating selected assemblies" : "Acquiring package input"}</h2><p>${state.request.assemblyPattern ? "Matches, semantic non-matches, and non-applicable selections will appear as the explicit packages are evaluated." : "Matches will appear as package candidates are evaluated."}</p></section>${renderProgress(state.outcome, escapeHtml)}${assessments}${renderCompletionFooter(state.request, state.outcome, escapeHtml)}`
      : `${assessments}${renderEmptyState(state, escapeHtml)}`;
}

export function patchPackageQueryStream(
  root: ParentNode,
  options: Pick<
    RenderPackageQueryOptions,
    "state" | "escapeHtml" | "viewport"
  >,
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
  results.innerHTML = renderResults(
    options.state,
    options.escapeHtml,
    options.viewport);
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
    availableAssemblyPatterns = [],
    navigationError = "",
    escapeHtml,
    viewport = null,
  } = options;
  const activeKeys = new Set(state.request?.facets.map(facet => facet.key) ?? []);
  const facets = renderFacets(availableFacets, activeKeys, escapeHtml);
  const failures = renderFailures(state, escapeHtml);
  const results = renderResults(state, escapeHtml, viewport);
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
          <p class="query-kicker">Exact package + literal prefix · nuget.org</p>
          <h1 id="package-query-heading" tabindex="-1">Package query</h1>
          <p>Run a bounded query over an exact package ID or literal prefix. Manifests and package content are acquired only with inspection facets.</p>
        </div>
        <form id="package-query-form" class="query-bar" role="search">
          <label for="package-query-prefix">Package ID or prefix</label>
          <input id="package-query-prefix" name="search" value="${escapeHtml(prefix)}" autocomplete="off" spellcheck="false" placeholder="Newtonsoft.Json or Newtonsoft.*" />
          <span>
            <button id="package-query-run" type="submit">Run query</button>
          </span>
          <span id="package-query-cancel-region">${renderStreamingCancel(state)}</span>
        </form>
        ${navigationError
          ? `<div class="query-navigation-error">${escapeHtml(navigationError)}</div>`
          : ""}
        <div id="package-query-failure-region">${failures}</div>
        <div class="query-layout">
          <aside class="query-facet-rail" aria-label="Package query controls">
            ${renderPackageOptions(request)}
            ${renderAssemblyControls(
              availableAssemblyPatterns,
              state,
              escapeHtml)}
            <h2>Inspection facets</h2>
            <p>Changes rerun the selected input; blank package input stays idle.</p>
            <div class="query-facets">${facets}</div>
            <p class="query-facet-disclosure">Content facets download up to 20 candidate package archives.</p>
            <p class="query-facet-disclosure">Candidate bound K: ${request.requestedLimit.toLocaleString()}; exact IDs use one candidate. Maximum matches N: ${request.requestedMatchLimit.toLocaleString()}. The match limit does not change prefix capacity.</p>
            <p class="query-facet-disclosure">Match counts and lifetime downloads describe a bounded response, not global top-N.</p>
          </aside>
          <section id="package-query-results" class="query-results" aria-label="Package query results" tabindex="-1">
            ${results}
          </section>
        </div>
      </main>
    </div>`;
}
