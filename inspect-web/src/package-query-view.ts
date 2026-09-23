import type {
  PackageQueryState,
  QueryPreset,
  QueryRequest,
  QueryResultRow,
  QuerySourceSelection,
  QueryTermDescriptor,
  TerminalQueryCompletion,
} from "./package-query.ts";
import {
  createQueryRequest,
  isLibraryLiteralQuery,
} from "./package-query.ts";
import {
  resolvePackageQueryRowWindow,
  type PackageQueryViewportSnapshot,
} from "./package-query-window.ts";
import {
  bindPackageQueryEditor,
  capturePackageQueryEditor,
  restorePackageQueryEditor,
  type PackageQueryEditorSnapshot,
} from "./package-query-editor-lifecycle.ts";
import { renderBrand } from "./brand.ts";
import { focusRenderedElement } from "./scope-bar.ts";

const PACKAGE_QUERY_PRESSURE_DISTANCE_PX = 600;

export interface PackageQueryBindingActions {
  onBack: () => void;
  onCancel: () => void;
  onPresetToggle: (presetId: string, prefix: string) => void;
  onLibraryTargetInput: (targetFramework: string) => void;
  onTermAdd?: (termKey: string) => void;
  onTermApply?: (
    index: number | null,
    operator: string,
    value: string,
    prefix: string,
  ) => void;
  onTermEdit?: (
    index: number | null,
    operator: string,
    value: string,
  ) => void;
  onTermDraftCancel?: () => void;
  onTermRemove?: (index: number, prefix: string) => void;
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
  onEditorCompositionEnd?: () => void;
}

export type PackageQueryFocusSnapshot =
  | {
      kind: "prefix" | "library-tfm";
      editor: PackageQueryEditorSnapshot;
    }
  | {
      kind: "library-literal";
      editor: PackageQueryEditorSnapshot;
    }
  | { kind: "product" }
  | { kind: "back" }
  | { kind: "run" }
  | { kind: "results" }
  | { kind: "prerelease" }
  | { kind: "preset"; presetId: string }
  | { kind: "term-add"; termKey: string }
  | {
      kind: "term";
      index: number;
      control: "operator" | "value" | "apply" | "remove";
      editor: PackageQueryEditorSnapshot | null;
    }
  | {
      kind: "term-draft";
      control: "operator" | "value" | "apply" | "cancel";
      editor: PackageQueryEditorSnapshot | null;
    }
  | { kind: "row"; packageId: string; version: string }
  | { kind: "cancel"; index: number }
  | { kind: "fallback" };

function isFocusableQueryElement(
  element: Element | null,
): element is HTMLElement {
  return element !== null
    && "dataset" in element
    && "focus" in element
    && typeof element.focus === "function";
}

function revealLibraryLiteralControl(element: Element | null): void {
  const disclosure =
    element?.closest<HTMLDetailsElement>("details.query-library-literal");
  if (disclosure) disclosure.open = true;
}

function termControl(
  value: string | undefined,
): "operator" | "value" | "apply" | "remove" | null {
  switch (value) {
    case "operator":
    case "value":
    case "apply":
    case "remove":
      return value;
    default:
      return null;
  }
}

function termDraftControl(
  value: string | undefined,
): "operator" | "value" | "apply" | "cancel" | null {
  switch (value) {
    case "operator":
    case "value":
    case "apply":
    case "cancel":
      return value;
    default:
      return null;
  }
}

export function capturePackageQueryFocus(
  root: Document,
): PackageQueryFocusSnapshot | null {
  const active = root.activeElement;
  if (!isFocusableQueryElement(active)) return null;
  if (active === root.body) return null;
  if (active.id === "package-query-library-literal") {
    const editor = capturePackageQueryEditor(active);
    if (!editor) return { kind: "fallback" };
    return {
      kind: "library-literal",
      editor,
    };
  }
  if (active.id === "package-query-prefix"
    || active.id === "package-query-library-tfm") {
    const editor = capturePackageQueryEditor(active);
    if (!editor) return { kind: "fallback" };
    return {
      kind: active.id === "package-query-prefix"
        ? "prefix"
        : "library-tfm",
      editor,
    };
  }
  if (active.id === "package-query-product") return { kind: "product" };
  if (active.id === "package-query-back") return { kind: "back" };
  if (active.id === "package-query-run") return { kind: "run" };
  if (active.id === "package-query-results") return { kind: "results" };
  if (active.id === "package-query-prerelease") return { kind: "prerelease" };
  if (active.dataset.queryPreset) {
    return { kind: "preset", presetId: active.dataset.queryPreset };
  }
  if (active.dataset.queryTermAdd) {
    return { kind: "term-add", termKey: active.dataset.queryTermAdd };
  }
  const activeTermControl = termControl(active.dataset.queryTermControl);
  if (activeTermControl) {
    const index = Number(active.dataset.queryTermIndex);
    if (Number.isInteger(index) && index >= 0) {
      return {
        kind: "term",
        index,
        control: activeTermControl,
        editor: activeTermControl === "value"
          ? capturePackageQueryEditor(active)
          : null,
      };
    }
  }
  const activeDraftControl =
    termDraftControl(active.dataset.queryTermDraftControl);
  if (activeDraftControl) {
    return {
      kind: "term-draft",
      control: activeDraftControl,
      editor: activeDraftControl === "value"
        ? capturePackageQueryEditor(active)
        : null,
    };
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
    case "library-literal":
      target = root.querySelector("#package-query-library-literal");
      break;
    case "library-tfm":
      target = root.querySelector("#package-query-library-tfm");
      break;
    case "preset":
      target = [...root.querySelectorAll<HTMLElement>("[data-query-preset]")]
        .find(element => element.dataset.queryPreset === snapshot.presetId)
        ?? null;
      break;
    case "term-add":
      target = [...root.querySelectorAll<HTMLElement>("[data-query-term-add]")]
        .find(element => element.dataset.queryTermAdd === snapshot.termKey)
        ?? null;
      break;
    case "term":
      target = [
        ...root.querySelectorAll<HTMLElement>("[data-query-term-control]"),
      ].find(element =>
        element.dataset.queryTermIndex === String(snapshot.index)
        && element.dataset.queryTermControl === snapshot.control) ?? null;
      break;
    case "term-draft":
      target = [
        ...root.querySelectorAll<HTMLElement>(
          "[data-query-term-draft-control]"),
      ].find(element =>
        element.dataset.queryTermDraftControl === snapshot.control) ?? null;
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
  if (snapshot.kind === "library-literal"
    || snapshot.kind === "library-tfm") {
    revealLibraryLiteralControl(target);
  }
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
  if (!usedFallback) {
    const editor = snapshot.kind === "prefix"
        || snapshot.kind === "library-literal"
        || snapshot.kind === "library-tfm"
      ? snapshot.editor
      : snapshot.kind === "term" || snapshot.kind === "term-draft"
        ? snapshot.editor
        : null;
    restorePackageQueryEditor(target, editor);
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
  const prefix = prefixInput();
  if (prefix) {
    bindPackageQueryEditor(
      prefix,
      () => actions.onPrefixInput(prefix.value),
      actions.onEditorCompositionEnd);
  }
  root.querySelectorAll<HTMLElement>("[data-query-preset]").forEach(button =>
    button.addEventListener("click", () => actions.onPresetToggle(
      button.dataset.queryPreset ?? "",
      prefixInput()?.value ?? "")));
  bindPackageQueryTerms(root, actions, prefixInput);
  const prerelease = root.querySelector<HTMLInputElement>(
    "#package-query-prerelease");
  prerelease?.addEventListener("change", () => actions.onSourceChange({
    includePrerelease: prerelease.checked,
  }, prefixInput()?.value ?? ""));
  const targetFramework = root.querySelector<HTMLInputElement>(
    "#package-query-library-tfm");
  if (targetFramework) {
    bindPackageQueryEditor(
      targetFramework,
      () => actions.onLibraryTargetInput(targetFramework.value),
      actions.onEditorCompositionEnd);
  }
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

function bindPackageQueryTerms(
  root: ParentNode,
  actions: PackageQueryBindingActions,
  prefixInput: () => HTMLInputElement | null,
): void {
  root.querySelectorAll<HTMLElement>("[data-query-term-add]").forEach(button =>
    button.addEventListener("click", () =>
      actions.onTermAdd?.(button.dataset.queryTermAdd ?? "")));
  root.querySelectorAll<HTMLFormElement>("[data-query-term-form]")
    .forEach(form => {
      const termValue = form.querySelector<
        HTMLInputElement | HTMLTextAreaElement>(
        "[data-query-term-value]");
      const termOperator =
        form.querySelector<HTMLInputElement | HTMLSelectElement>(
          "[data-query-term-operator]");
      const indexText = form.dataset.queryTermForm;
      const index = indexText === "draft" ? null : Number(indexText);
      if (index !== null && (!Number.isInteger(index) || index < 0)) {
        throw new Error("Package-query term index is invalid.");
      }
      const retainEdit = () => {
        if (!termValue || !termOperator) return;
        actions.onTermEdit?.(
          index,
          termOperator.value,
          termValue instanceof HTMLTextAreaElement
            ? decodeLibraryLiteralEditorValue(termValue.value)
            : termValue.value);
      };
      if (termValue) {
        const updateValue = () => {
          termValue.setCustomValidity("");
          retainEdit();
        };
        bindPackageQueryEditor(
          termValue,
          updateValue,
          actions.onEditorCompositionEnd);
        termValue.addEventListener("change", updateValue);
      }
      termOperator?.addEventListener("change", retainEdit);
      form.addEventListener("submit", event => {
        event.preventDefault();
        if (!termValue || !termOperator) {
          throw new Error("Package-query term controls are incomplete.");
        }
        termValue.setCustomValidity("");
        const value = termValue instanceof HTMLTextAreaElement
          ? decodeLibraryLiteralEditorValue(termValue.value)
          : termValue.value;
        if (value.length === 0
          || (!(termValue instanceof HTMLTextAreaElement)
            && value.trim().length === 0)) {
          termValue.setCustomValidity("Enter a term value.");
          termValue.reportValidity();
          return;
        }
        actions.onTermApply?.(
          index,
          termOperator.value,
          value,
          prefixInput()?.value ?? "");
      });
    });
  root.querySelectorAll<HTMLElement>("[data-query-term-remove]")
    .forEach(button => button.addEventListener("click", () => {
      const index = Number(button.dataset.queryTermRemove);
      if (!Number.isInteger(index) || index < 0) {
        throw new Error("Package-query term index is invalid.");
      }
      actions.onTermRemove?.(index, prefixInput()?.value ?? "");
    }));
  root.querySelector("[data-query-term-draft-cancel]")
    ?.addEventListener("click", () => actions.onTermDraftCancel?.());
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
  const answers = row.answers
    .map(item => `<li class="query-answer">${escapeHtml(item.value)}</li>`)
    .join("");
  const evidence = row.evidence
    .filter(item => item.scope === "package")
    .map(item => {
      const summary = item.summary;
      const text = escapeHtml(formatEvidence(item));
      if ((item.id !== "selected-assembly"
          && item.id !== "implementation-libraries")
        || summary === null)
        return `<li>${text}</li>`;
      const preview = summary.preview
        .map(value => `<li>${escapeHtml(value)}</li>`)
        .join("");
      return `<li>
        ${text}
        <p>Showing ${summary.preview.length.toLocaleString()} of ${summary.count.toLocaleString()} occurrences.</p>
        ${preview ? `<ul>${preview}</ul>` : ""}
      </li>`;
    })
    .join("");
  const rootRequest = row.rootRequest === undefined
    ? ""
    : ` data-query-root-request="${escapeHtml(row.rootRequest)}"`;
  const openAction =
    `<button type="button" data-query-row-open="${escapeHtml(row.packageId)}" data-query-row-version="${escapeHtml(row.version)}"${rootRequest}>Open in workspace</button>`;
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
      ${answers ? `<ul class="query-answers" aria-label="Answers">${answers}</ul>` : ""}
      ${evidence ? `<ul class="query-evidence">${evidence}</ul>` : ""}
      <div class="query-row-meta">
        <span>${row.totalDownloads === null
          ? "Lifetime downloads unavailable"
          : `${row.totalDownloads.toLocaleString()} lifetime downloads`}</span>
        ${row.producer
          ? `<span>${escapeHtml(row.producer)}</span>`
          : ""}
        ${openAction}
      </div>
    </article>`;
}

function renderQueryContext(
  rows: readonly QueryResultRow[],
  escapeHtml: (value: unknown) => string,
): string {
  const evidence = rows[0]?.evidence
    .filter(item => item.scope === "query")
    .map(item => `<li>${escapeHtml(formatEvidence(item))}</li>`)
    .join("") ?? "";
  return evidence
    ? `<section class="query-context" aria-label="Query context"><h2>Query context</h2><ul class="query-evidence">${evidence}</ul></section>`
    : "";
}

function formatEvidence(
  evidence: QueryResultRow["evidence"][number],
): string {
  const property = (name: string): string | null =>
    evidence.properties.find(item => item.name === name)?.value ?? null;
  switch (evidence.id) {
    case "package.query.scope.prefix":
      return `Prefix: ${property("prefix") ?? ""}`;
    case "package.query.scope.exact-package":
      return `Package: ${property("package") ?? ""}`;
    case "dependencies":
      return formatEvidenceSummary(
        evidence.summary,
        "dependency",
        "dependencies");
    case "dependency-target": {
      const target = property("target");
      if (target !== null)
        return `Dependency target: ${target}`;
      const requested = property("requested-target") ?? "";
      const status = property("selection-status") ?? "";
      const selected = property("selected-group");
      return selected === null
        ? `Dependency target: ${requested} (${status})`
        : `Dependency target: ${requested} -> ${selected} (${status})`;
    }
    case "depends":
      return formatEvidenceSummary(
        evidence.summary,
        "dependency declaration",
        "dependency declarations");
    case "downloads":
      return `Downloads: ${evidence.number?.toLocaleString() ?? ""}`;
    case "license":
      return `Nuspec license ${property("declaration-kind") ?? ""}: `
        + (property("declaration-value") ?? "");
    case "readme":
      return `README: ${property("path") ?? ""}`;
    case "tool":
      return `Package type: ${property("package-type") ?? ""}`;
    case "tool-format":
      return `.NET tool settings version: ${property("settings-version") ?? ""}`;
    case "skill":
      return formatEvidenceSummary(
        evidence.summary,
        "skill document",
        "skill documents");
    case "selected-assembly":
      return `${property("path") ?? ""}: `
        + `${property("literal-use-count") ?? "0"} literal uses; `
        + `${property("unevaluated-sibling-count") ?? "0"} sibling assemblies not evaluated.`;
    case "implementation-libraries":
      return `${property("evaluated-library-count") ?? "0"} implementation libraries evaluated; `
        + `${property("matched-library-count") ?? "0"} matched.`;
    case "literal-use":
      return `Method ${property("method-token") ?? ""}, `
        + `${property("il-offset") ?? ""}: ${property("excerpt") ?? ""}`;
    default: {
      const rawValue = property("value");
      if (rawValue !== null && evidence.properties.length === 1)
        return rawValue;
      const values = evidence.properties
        .map(item => `${item.name}: ${item.value}`)
        .join(", ");
      return values || evidence.id;
    }
  }
}

function formatEvidenceSummary(
  summary: QueryResultRow["evidence"][number]["summary"],
  singular: string,
  plural: string,
): string {
  if (summary === null)
    return "";
  const heading = `${summary.count} ${summary.count === 1 ? singular : plural}`;
  if (summary.preview.length === 0)
    return heading;
  const remaining = summary.count - summary.preview.length;
  return remaining > 0
    ? `${heading}: ${summary.preview.join(", ")} (+${remaining} more)`
    : `${heading}: ${summary.preview.join(", ")}`;
}

function renderPreset(
  preset: QueryPreset,
  activeKeys: ReadonlySet<string>,
  escapeHtml: (value: unknown) => string,
): string {
  const active = activeKeys.has(preset.id);
  return `
    <button
      type="button"
      class="query-preset ${active ? "active" : ""}"
      data-query-preset="${escapeHtml(preset.id)}"
      aria-pressed="${active}"
      title="${escapeHtml(preset.summary ?? preset.label)}">
      ${escapeHtml(preset.label)}
    </button>`;
}

function renderPresets(
  presets: readonly QueryPreset[],
  activeKeys: ReadonlySet<string>,
  escapeHtml: (value: unknown) => string,
): string {
  const renderedGroups = new Set<string>();
  return presets.map(preset => {
    if (!preset.displayGroupId) {
      return renderPreset(preset, activeKeys, escapeHtml);
    }
    if (renderedGroups.has(preset.displayGroupId)) return "";
    renderedGroups.add(preset.displayGroupId);
    const groupPresets = presets.filter(candidate =>
      candidate.displayGroupId === preset.displayGroupId);
    return `
      <div
        class="query-preset-group"
        role="group"
        aria-label="${escapeHtml(
          preset.displayGroupLabel ?? preset.label)}">
        ${groupPresets
          .map(groupPreset => renderPreset(
            groupPreset,
            activeKeys,
            escapeHtml))
          .join("")}
      </div>`;
  }).join("");
}

function renderTermOperator(
  descriptor: QueryTermDescriptor,
  selectedOperator: string,
  index: number | null,
  escapeHtml: (value: unknown) => string,
): string {
  const controlAttributes = index === null
    ? 'data-query-term-draft-control="operator"'
    : `data-query-term-index="${index}" data-query-term-control="operator"`;
  if (descriptor.operators.length <= 1) {
    return `<input type="hidden" data-query-term-operator value="${escapeHtml(selectedOperator)}" />`;
  }
  return `
    <label class="query-term-operator">
      <span>Operator</span>
      <select data-query-term-operator ${controlAttributes}>
        ${descriptor.operators.map(operator => `
          <option value="${escapeHtml(operator)}"${operator === selectedOperator ? " selected" : ""}>${escapeHtml(operator)}</option>`)
          .join("")}
      </select>
    </label>`;
}

function renderTermEditor(
  descriptor: QueryTermDescriptor,
  operator: string,
  value: string,
  index: number | null,
  escapeHtml: (value: unknown) => string,
): string {
  const draft = index === null;
  const identity = draft ? "draft" : String(index);
  const valueAttributes = draft
    ? 'data-query-term-draft-value data-query-term-draft-control="value"'
    : `data-query-term-index="${index}" data-query-term-control="value"`;
  const applyAttributes = draft
    ? 'data-query-term-draft-control="apply"'
    : `data-query-term-index="${index}" data-query-term-control="apply"`;
  const editorValue = descriptor.multiline
    ? encodeLibraryLiteralEditorValue(value)
    : value;
  const valueControl = descriptor.multiline
    ? `<textarea
          id="package-query-term-${identity}"
          data-query-term-value
          ${valueAttributes}
          rows="3"
          required
          placeholder="${escapeHtml(descriptor.example)}"
          title="${escapeHtml(descriptor.summary)}"
          autocomplete="off"
          spellcheck="false">${escapeHtml(editorValue)}</textarea>`
    : `<input
          id="package-query-term-${identity}"
          data-query-term-value
          ${valueAttributes}
          type="text"
          required
          value="${escapeHtml(editorValue)}"
          placeholder="${escapeHtml(descriptor.example)}"
          title="${escapeHtml(descriptor.summary)}"
          autocomplete="off"
          spellcheck="false" />`;
  return `
    <form
      class="query-term"
      data-query-term-form="${identity}"
      aria-label="${escapeHtml(descriptor.label)}">
      <label class="query-term-value" for="package-query-term-${identity}">
        <span>${escapeHtml(descriptor.label)}</span>
        ${valueControl}
        ${descriptor.multiline
          ? "<small>Line feeds remain line breaks. Use <code>\\r</code> for a carriage return and <code>\\\\</code> for a literal backslash.</small>"
          : ""}
      </label>
      ${renderTermOperator(descriptor, operator, index, escapeHtml)}
      <div class="query-term-actions">
        <button type="submit" ${applyAttributes}>Apply</button>
        ${draft
          ? `<button type="button" data-query-term-draft-cancel data-query-term-draft-control="cancel">Cancel</button>`
          : `<button type="button" data-query-term-remove="${index}" data-query-term-index="${index}" data-query-term-control="remove">Remove</button>`}
      </div>
    </form>`;
}

function renderTermControls(
  state: PackageQueryState,
  availableTerms: readonly QueryTermDescriptor[],
  escapeHtml: (value: unknown) => string,
): string {
  const applied = state.request?.terms ?? [];
  const draft = state.termDraft;
  const active = [
    ...applied.map((term, index) => {
      const edit = state.termEdits?.[index];
      return renderTermEditor(
        term.descriptor,
        edit?.operator ?? term.operator,
        edit?.value ?? term.value,
        index,
        escapeHtml);
    }),
    ...(draft
      ? [renderTermEditor(
          draft.descriptor,
          draft.operator,
          draft.value,
          null,
          escapeHtml)]
      : []),
  ].join("");
  const activeZone = active
    ? `
      <section class="query-active-terms" aria-labelledby="query-active-terms-heading">
        <h2 id="query-active-terms-heading">Active terms</h2>
        <div class="query-term-list">${active}</div>
      </section>`
    : "";
  const palette = availableTerms
    .filter(term => term.operators.length > 0)
    .map(term => `
      <button
        type="button"
        class="query-term-add"
        data-query-term-add="${escapeHtml(term.key)}"
        title="${escapeHtml(term.summary)}">
        Add ${escapeHtml(term.label)}
      </button>`)
    .join("");
  return `
    ${activeZone}
    <section class="query-available-terms" aria-labelledby="query-available-terms-heading">
      <h2 id="query-available-terms-heading">Available terms</h2>
      <p>Applied terms are combined; changes rerun nonblank package input.</p>
      <div class="query-term-palette">${palette}</div>
    </section>`;
}

function renderCompletionFooter(
  request: QueryRequest | null,
  outcome: PackageQueryState["outcome"],
  escapeHtml: (value: unknown) => string,
): string {
  const { completion } = outcome;
  const partialFailure = outcome.failures.length > 0;
  const libraryLiteralScope = completion.kind === "library-literal"
    ? renderLibraryLiteralCompletionScope(completion)
    : null;
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
        : completion.kind === "library-literal"
          ? `${completion.matchedPackageCount.toLocaleString()} matching package${completion.matchedPackageCount === 1 ? "" : "s"} · ${completion.occurrenceCount.toLocaleString()} occurrence${completion.occurrenceCount === 1 ? "" : "s"} · ${libraryLiteralScope}`
        : completion.kind === "failed"
          ? `failed: ${escapeHtml(completion.reason)}`
          : "cancelled";
  const cancelButton = completion.kind === "streaming"
    ? `<button type="button" data-query-cancel="1">Cancel</button>`
    : "";
  const resultLabel = `package${outcome.rows.length === 1 ? "" : "s"}`;
  return `
    <div class="query-footer">
      <span>${outcome.rows.length} ${resultLabel} · ${label}</span>
      ${cancelButton}
    </div>`;
}

function renderLibraryLiteralCompletionScope(
  completion: Extract<
    TerminalQueryCompletion,
    { kind: "library-literal" }
  >,
): string {
  let population: string;
  switch (completion.population) {
    case "ExactPackageComplete":
      population = "exact package population complete";
      break;
    case "PrefixExhausted":
      population = "prefix population exhausted";
      break;
    case "MatchLimitReached":
      population = "match limit reached";
      break;
    case "CandidateLimitReached":
      population = "candidate limit reached";
      break;
    case "SourcePageLimitReached":
      population = "source page limit reached";
      break;
    case "ClientPageLimitReached":
      population = "client page limit reached";
      break;
    case "SourceFailed":
      population = "source population failed";
      break;
    default: {
      const unreachable: never = completion.population;
      return unreachable;
    }
  }
  return completion.complete ? population : `${population}; operation incomplete`;
}

function renderLibraryTargetControl(
  request: QueryRequest,
  escapeHtml: (value: unknown) => string,
): string {
  if (!isLibraryLiteralQuery(request)) return "";
  return `
    <section class="query-library-literal">
      <h2>Library selection</h2>
      <label for="package-query-library-tfm">
        <span>Target framework</span>
        <input
          id="package-query-library-tfm"
          type="text"
          value="${escapeHtml(request.targetFramework)}"
          placeholder="net10.0"
          autocomplete="off"
          spellcheck="false" />
      </label>
      <p class="query-preset-disclosure">The Product planner records this exact TFM with the active library-literal term. Metadata-expensive queries inspect at most five candidates.</p>
    </section>`;
}

export function encodeLibraryLiteralEditorValue(value: string): string {
  return value.replaceAll("\\", "\\\\").replaceAll("\r", "\\r");
}

export function decodeLibraryLiteralEditorValue(value: string): string {
  let decoded = "";
  for (let index = 0; index < value.length; index++) {
    const current = value[index]!;
    if (current !== "\\" || index + 1 >= value.length) {
      decoded += current;
      continue;
    }
    const next = value[index + 1]!;
    if (next === "r") {
      decoded += "\r";
      index++;
    } else if (next === "\\") {
      decoded += "\\";
      index++;
    } else {
      decoded += current;
    }
  }
  return decoded;
}

function renderPackageOptions(request: QueryRequest): string {
  return `
    <div class="query-source-controls" role="group" aria-label="Package query options">
      <h2>Search options</h2>
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
          : progress.phase === "dependency-traversal"
            ? "Dependency traversal"
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
        <p>Enter a package ID or terminal-star prefix. Selected inspection facts remain configured.</p>
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
  if (completion.kind === "library-literal") {
    const scope = renderLibraryLiteralCompletionScope(completion);
    return `
      <section class="query-empty">
        <span class="large-glyph">◇</span>
        <h2>${completion.complete
          ? "No matching package libraries"
          : "No matching package libraries in the completed work"}</h2>
        <p>Scope: ${scope}. The selector-issued implementation libraries produced no package Result.${completion.complete ? "" : " This is not a confirmed empty result for the requested population."} Candidate outcomes remain listed above.</p>
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
      <p>Try a broader explicit prefix or select fewer inspection facts.</p>
    </section>`;
}

export interface RenderPackageQueryOptions {
  state: PackageQueryState;
  prefix?: string;
  viewport?: PackageQueryViewportSnapshot | null;
  availablePresets: readonly QueryPreset[];
  availableTerms?: readonly QueryTermDescriptor[];
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

function renderAssessments(
  state: PackageQueryState,
  escapeHtml: (value: unknown) => string,
): string {
  if (state.outcome.assessments.length === 0) return "";
  return `
    <section class="query-assessments">
      <strong>Selected implementation Library outcomes</strong>
      <p>These outcomes cover each package's selector-issued implementation Libraries for the requested target, not other package asset roles.</p>
      <ul>${state.outcome.assessments.map(assessment => `
        <li>
          <strong>${escapeHtml(assessment.packageId)}@${escapeHtml(assessment.version)} · ${assessment.disposition === "Matched"
            ? "Matched"
            : assessment.disposition === "NoMatch"
              ? "No match"
              : assessment.disposition === "NotApplicable"
                ? "Not applicable"
                : assessment.disposition === "Failure"
                  ? "Failed"
                  : "Not evaluated"}</strong>
          <span>${escapeHtml(assessment.message)}</span>
          ${assessment.libraries.length === 0
            ? assessment.assetPath
              ? `<span>Selected assembly: ${escapeHtml(assessment.assetPath)}</span>`
              : ""
            : `<ul>${assessment.libraries.map(library => `
              <li>
                <strong>${escapeHtml(library.path)} · ${library.disposition === "Matched"
                  ? `Matched (${library.occurrenceCount} occurrence${library.occurrenceCount === 1 ? "" : "s"})`
                  : library.disposition === "NoMatch"
                    ? "No match"
                    : "Failed"}</strong>
                ${library.failureStage
                  ? `<span>Stage: ${escapeHtml(library.failureStage)}</span>`
                  : ""}
                ${library.message
                  ? `<span>${escapeHtml(library.message)}</span>`
                  : ""}
              </li>`).join("")}</ul>`}
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
      ? `<section class="query-empty query-running"><span class="loader" aria-hidden="true"></span><h2>Acquiring package input</h2><p>Matches will appear as package candidates are evaluated.</p></section>${renderProgress(state.outcome, escapeHtml)}${assessments}${renderCompletionFooter(state.request, state.outcome, escapeHtml)}`
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
    availablePresets,
    availableTerms = [],
    navigationError = "",
    escapeHtml,
    viewport = null,
  } = options;
  const activeKeys = new Set(state.request?.presets.map(preset => preset.id) ?? []);
  const presets = renderPresets(availablePresets, activeKeys, escapeHtml);
  const failures = renderFailures(state, escapeHtml);
  const results = renderResults(state, escapeHtml, viewport);
  const request = state.request ?? createQueryRequest("");
  const terms = renderTermControls(state, availableTerms, escapeHtml);

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
          <p>Run a bounded query over an exact package ID or literal prefix. Manifests and package content are acquired only for selected inspection work.</p>
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
          <aside class="query-preset-rail" aria-label="Package query controls">
            ${renderPackageOptions(request)}
            ${renderLibraryTargetControl(request, escapeHtml)}
            ${terms}
            <h2>Inspection facts</h2>
            <p>Changes rerun the selected input; blank package input stays idle.</p>
            <div class="query-presets">${presets}</div>
            <p class="query-preset-disclosure">Content facts download up to 20 candidate package archives.</p>
            <p class="query-preset-disclosure">Transitive dependency facts inspect up to 5 package candidates.</p>
            <p class="query-preset-disclosure">Metadata-expensive facts inspect up to 5 package candidates.</p>
            <p class="query-preset-disclosure">Candidate bound K: ${request.requestedLimit.toLocaleString()}; exact IDs use one candidate. Maximum matches N: ${request.requestedMatchLimit.toLocaleString()}. The match limit does not change prefix capacity.</p>
            <p class="query-preset-disclosure">Match counts and lifetime downloads describe a bounded response, not global top-N.</p>
          </aside>
          <section id="package-query-results" class="query-results" aria-label="Package query results" tabindex="-1">
            ${results}
          </section>
        </div>
      </main>
    </div>`;
}
