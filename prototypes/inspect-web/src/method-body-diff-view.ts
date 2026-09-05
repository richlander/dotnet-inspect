import type {
  BrowserCSharpBodyRow,
  BrowserIlBodyRow,
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyDiagnostic,
  BrowserMethodBodyEndpoint,
  BrowserMethodBodyProducer,
  BrowserMethodBodySelection,
} from "./facades/inspect-web-source.d.ts";
import type { EscapeHtml } from "./csharp-highlighting.ts";
import {
  filterMethodBodyChoices,
  methodBodyChoices,
  methodBodyChoiceForKey,
  methodBodySelectionKey,
  type MethodBodyDiffState,
} from "./method-body-comparison.ts";
import { trapModalTab } from "./shell-controls.ts";

export const METHOD_BODY_DIFF_ACTION_SELECTOR = "#compare-method-bodies";
export const METHOD_BODY_DIFF_CHOOSER_SELECTOR = "#method-body-diff-after";
export const METHOD_BODY_DIFF_FILTER_SELECTOR = "#method-body-diff-filter";
export const METHOD_BODY_DIFF_TITLE_SELECTOR = "#method-body-diff-title";

export type MethodBodyDiffAction =
  | { readonly kind: "open" }
  | { readonly kind: "close" }
  | { readonly kind: "compare" }
  | { readonly kind: "select"; readonly key: string }
  | { readonly kind: "filter"; readonly value: string; readonly caret: number };

export interface MethodBodyDiffBindingActions {
  readonly onAction: (action: MethodBodyDiffAction) => void;
}

export interface MethodBodyComparisonAvailability {
  readonly available: boolean;
  readonly reason: string;
}

export interface MethodBodyDiffRenderOptions {
  readonly state: MethodBodyDiffState;
  readonly escapeHtml: EscapeHtml;
  readonly highlightCSharp: (value: string) => string;
}

function metadataToken(token: number | null | undefined): string {
  if (token === null || token === undefined) return "no token";
  return `0x${token.toString(16).padStart(8, "0")}`;
}

// The contextual action always states why it cannot run, so an unavailable implementation
// target is visible rather than a hidden or silent control.
export function renderMethodBodyComparisonAction(
  availability: MethodBodyComparisonAvailability,
  escapeHtml: EscapeHtml,
): string {
  const unavailable = !availability.available;
  return `
    <button id="compare-method-bodies" type="button"
      data-method-body-action="open"
      ${unavailable ? ' aria-describedby="compare-method-bodies-reason" disabled' : ""}
      title="Compare this method body with another method in the same implementation assembly">Compare method bodies</button>
    ${unavailable
      ? `<span id="compare-method-bodies-reason" class="working-surface-note">${escapeHtml(availability.reason)}</span>`
      : ""}`;
}

interface MethodBodyIdentityFields {
  readonly label: string;
  readonly typeIdentity: string;
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
}

function renderIdentity(
  identity: MethodBodyIdentityFields | null,
  escapeHtml: EscapeHtml,
  empty: string,
): string {
  if (!identity)
    return `<p class="method-body-empty">${escapeHtml(empty)}</p>`;
  return `
    <p class="method-body-label"><code>${escapeHtml(identity.label)}</code></p>
    <dl class="method-body-identity">
      <div><dt>Type</dt><dd><code>${escapeHtml(identity.typeIdentity)}</code></dd></div>
      <div><dt>Member</dt><dd><code>${escapeHtml(identity.memberName)}</code></dd></div>
      <div><dt>Body</dt><dd><code>${escapeHtml(identity.selectorKey)}</code></dd></div>
      <div><dt>MethodDef</dt><dd><code>${escapeHtml(metadataToken(identity.metadataToken))}</code></dd></div>
    </dl>`;
}

function renderSelectionIdentity(
  selection: BrowserMethodBodySelection | null,
  escapeHtml: EscapeHtml,
  empty: string,
): string {
  return renderIdentity(selection, escapeHtml, empty);
}

function renderAssemblyLine(
  request: BrowserMethodBodyComparisonRequest | null,
  targetsAssembly: string,
  targetsPackage: string,
  targetsVersion: string,
  targetsFramework: string,
  targetsModuleVersionId: string,
  escapeHtml: EscapeHtml,
): string {
  const assembly = request?.assembly ?? targetsAssembly;
  const packageId = request?.packageId ?? targetsPackage;
  const version = request?.version ?? targetsVersion;
  const framework = request?.framework ?? targetsFramework;
  const moduleVersionId = request?.moduleVersionId ?? targetsModuleVersionId;
  if (!assembly) return "";
  return `<p class="method-body-scope">
      <span>${escapeHtml(assembly)}</span>
      <span>${escapeHtml(packageId)} ${escapeHtml(version)} · ${escapeHtml(framework)}</span>
      <span>MVID ${escapeHtml(moduleVersionId)}</span>
    </p>`;
}

function renderChooser(
  state: MethodBodyDiffState,
  escapeHtml: EscapeHtml,
): string {
  const targets = state.targets;
  if (state.targetsLoading) {
    return `<p class="method-body-progress"><span class="loader"></span>Loading the implementation method inventory…</p>`;
  }
  if (state.targetsError) {
    return `<p class="method-body-failure" role="alert">${escapeHtml(state.targetsError)}</p>`;
  }
  if (!targets) {
    return `<p class="method-body-empty">No implementation method inventory is available.</p>`;
  }
  const choices = filterMethodBodyChoices(
    methodBodyChoices(targets),
    state.filter,
    state.candidateKey);
  const options = choices.map(choice => {
    const key = methodBodySelectionKey(choice);
    return `<option value="${escapeHtml(key)}"${key === state.candidateKey ? " selected" : ""}>${escapeHtml(choice.label)}</option>`;
  }).join("");
  return `
    <div class="method-body-chooser">
      <label class="method-body-field" for="method-body-diff-filter">
        <span>Filter</span>
        <input id="method-body-diff-filter" type="search"
          data-method-body-filter autocomplete="off" spellcheck="false"
          placeholder="Filter methods, types and bodies"
          value="${escapeHtml(state.filter)}" />
      </label>
      <label class="method-body-field" for="method-body-diff-after">
        <span>After method</span>
        <select id="method-body-diff-after" data-method-body-candidate>
          <option value=""${state.candidateKey ? "" : " selected"}>Choose a method…</option>
          ${options}
        </select>
      </label>
      <p class="method-body-choice-count">${choices.length} of ${methodBodyChoices(targets).length} methods shown</p>
    </div>`;
}

function endpointState(
  endpoint: BrowserMethodBodyEndpoint,
  side: "before" | "after",
  escapeHtml: EscapeHtml,
): string {
  const detail = [
    endpoint.targetState ? `target ${endpoint.targetState}` : "",
    endpoint.metadataToken === null || endpoint.metadataToken === undefined
      ? ""
      : metadataToken(endpoint.metadataToken),
    endpoint.moduleVersionId ? `MVID ${endpoint.moduleVersionId}` : "",
    endpoint.detail ?? "",
  ].filter(Boolean).join(" · ");
  return `<p class="method-body-endpoint" data-method-body-endpoint="${side}">
      <strong>${side === "before" ? "Before" : "After"}</strong>
      <span data-method-body-endpoint-state>${escapeHtml(endpoint.state)}</span>
      ${detail ? `<small>${escapeHtml(detail)}</small>` : ""}
    </p>`;
}

function renderDiagnostics(
  diagnostics: readonly BrowserMethodBodyDiagnostic[],
  escapeHtml: EscapeHtml,
  label: string,
): string {
  if (diagnostics.length === 0) return "";
  return `
    <section class="method-body-diagnostics" aria-label="${escapeHtml(label)}">
      <h4>${escapeHtml(label)}</h4>
      <ul>
        ${diagnostics.map(diagnostic => {
          const scope = [
            diagnostic.kind,
            diagnostic.side ?? "",
            diagnostic.mechanism ?? "",
            diagnostic.hunkId === null || diagnostic.hunkId === undefined
              ? ""
              : `hunk ${diagnostic.hunkId}`,
            diagnostic.subjectToken === null
              || diagnostic.subjectToken === undefined
              ? ""
              : metadataToken(diagnostic.subjectToken),
            diagnostic.path ?? "",
          ].filter(Boolean).join(" · ");
          return `<li>
            <span class="method-body-diagnostic-scope">${escapeHtml(scope)}</span>
            <span>${escapeHtml(diagnostic.message)}</span>
            ${diagnostic.detail ? `<small>${escapeHtml(diagnostic.detail)}</small>` : ""}
          </li>`;
        }).join("")}
      </ul>
    </section>`;
}

function renderCSharpValue(
  side: "old" | "new",
  value: string | null,
  operationKind: string | null,
  operationValue: string | null,
  escapeHtml: EscapeHtml,
  highlightCSharp: (value: string) => string,
): string {
  return `<div class="method-body-value" data-method-body-value="${side}">
      <span class="method-body-value-label">${side === "old" ? "Before" : "After"}</span>
      ${value === null
        ? `<span class="method-body-empty">no value</span>`
        : `<pre class="language-csharp"><code class="language-csharp">${highlightCSharp(value)}</code></pre>`}
      ${operationKind === null
        ? ""
        : `<small class="method-body-operation">${escapeHtml(operationKind)}: ${escapeHtml(operationValue ?? "")}</small>`}
    </div>`;
}

// Native row evidence is lowered to DOM exactly as the producer reported it: no second
// diff, no normalization, no reconstruction of a body from displayed text.
function renderCSharpRow(
  row: BrowserCSharpBodyRow,
  escapeHtml: EscapeHtml,
  highlightCSharp: (value: string) => string,
): string {
  const coordinates = [
    row.line === null || row.line === undefined ? "" : `line ${row.line}`,
    `hunk ${row.hunkId}`,
    row.sourceCoordinate ?? "",
    row.fidelity,
  ].filter(Boolean).join(" · ");
  return `
    <li class="method-body-row" data-method-body-row="csharp"
      data-method-body-kind="${escapeHtml(row.kind)}"
      data-method-body-hunk="${row.hunkId}"
      data-method-body-change="${escapeHtml(row.changeId)}"
      data-method-body-member-key="${escapeHtml(row.stableMemberKey)}">
      <p class="method-body-row-head">
        <span class="method-body-row-kind">${escapeHtml(row.kind)}</span>
        <span class="method-body-row-coordinates">${escapeHtml(coordinates)}</span>
        <span class="method-body-row-member"
          title="${escapeHtml(row.assemblyIdentity)}">${escapeHtml(row.member)}</span>
      </p>
      <pre class="language-csharp method-body-row-text"><code class="language-csharp">${highlightCSharp(row.text)}</code></pre>
      <div class="method-body-row-values">
        ${renderCSharpValue(
          "old",
          row.oldValue,
          row.oldOperation?.kind ?? null,
          row.oldOperation?.value ?? null,
          escapeHtml,
          highlightCSharp)}
        ${renderCSharpValue(
          "new",
          row.newValue,
          row.newOperation?.kind ?? null,
          row.newOperation?.value ?? null,
          escapeHtml,
          highlightCSharp)}
      </div>
      <p class="method-body-row-message">${escapeHtml(row.message)}</p>
    </li>`;
}

function renderIlRow(
  row: BrowserIlBodyRow,
  escapeHtml: EscapeHtml,
): string {
  const operand = row.operation.operand
    ? `${row.operation.operand.kind}: ${row.operation.operand.value}`
    : "no operand";
  return `
    <li class="method-body-row" data-method-body-row="il"
      data-method-body-kind="${escapeHtml(row.kind)}"
      data-method-body-hunk="${row.hunkId}">
      <p class="method-body-row-head">
        <span class="method-body-row-kind">${escapeHtml(row.kind)}</span>
        <span class="method-body-row-coordinates">IL_${row.operation.offset.toString(16).padStart(4, "0")} · hunk ${row.hunkId}</span>
        <span class="method-body-row-member">${escapeHtml(row.operation.opcodeFamily)}</span>
      </p>
      <p class="method-body-row-operand"><code>${escapeHtml(operand)}</code></p>
      <p class="method-body-row-message">${escapeHtml(row.message)}</p>
    </li>`;
}

function producerLabel(producer: string): string {
  switch (producer) {
    case "CSharp":
      return "C#";
    case "IlBody":
      return "IL";
    default:
      return producer;
  }
}

function exactness(isExact: boolean): string {
  return isExact
    ? "exact under this mechanism"
    : "not exact under this mechanism";
}

// Each native lane keeps its own outcome, verdict, endpoints and evidence; one failed lane
// never hides the other lane's usable evidence.
function renderProducer(
  producer: BrowserMethodBodyProducer,
  escapeHtml: EscapeHtml,
  highlightCSharp: (value: string) => string,
): string {
  const label = producerLabel(producer.producer);
  const cSharp = producer.cSharp;
  const il = producer.il;
  const cSharpEvidence = cSharp
    ? `<section class="method-body-evidence" data-method-body-evidence="csharp">
        <p class="method-body-evidence-head">
          <span data-method-body-exact="${cSharp.isExact}">${escapeHtml(exactness(cSharp.isExact))}</span>
          <span>${cSharp.rows.length} row${cSharp.rows.length === 1 ? "" : "s"}</span>
        </p>
        ${cSharp.rows.length === 0
          ? `<p class="method-body-empty">No aligned C# rows were reported.</p>`
          : `<ol class="method-body-rows">${cSharp.rows.map(row =>
              renderCSharpRow(row, escapeHtml, highlightCSharp)).join("")}</ol>`}
      </section>`
    : "";
  const ilEvidence = il
    ? `<section class="method-body-evidence" data-method-body-evidence="il">
        <p class="method-body-evidence-head">
          <span data-method-body-il-outcome>${escapeHtml(il.outcome)}</span>
          <span data-method-body-exact="${il.isExact}">${escapeHtml(exactness(il.isExact))}</span>
          <span>${il.isAvailable ? "IL evidence available" : "IL evidence unavailable"}</span>
          ${il.failure ? `<span class="method-body-failure">${escapeHtml(il.failure)}</span>` : ""}
        </p>
        ${il.rows.length === 0
          ? `<p class="method-body-empty">No IL instruction rows were reported.</p>`
          : `<details class="method-body-il-disclosure">
              <summary>${il.rows.length} IL instruction row${il.rows.length === 1 ? "" : "s"}</summary>
              <ol class="method-body-rows">${il.rows.map(row =>
                renderIlRow(row, escapeHtml)).join("")}</ol>
            </details>`}
      </section>`
    : "";
  const evidence = cSharpEvidence + ilEvidence;
  return `
    <section class="method-body-producer" data-method-body-producer="${escapeHtml(producer.producer)}"
      aria-label="${escapeHtml(label)} comparison">
      <header class="method-body-producer-head">
        <h3>${escapeHtml(label)}</h3>
        <p class="method-body-status">
          <span data-method-body-outcome>${escapeHtml(producer.outcome)}</span>
          <span data-method-body-verdict>${escapeHtml(producer.nativeVerdict)}</span>
        </p>
      </header>
      <div class="method-body-endpoints">
        ${endpointState(producer.before, "before", escapeHtml)}
        ${endpointState(producer.after, "after", escapeHtml)}
      </div>
      ${evidence || `<p class="method-body-empty">This mechanism returned no aligned body evidence.</p>`}
      ${renderDiagnostics(producer.diagnostics, escapeHtml, `${label} diagnostics`)}
    </section>`;
}

function orderedProducers(
  comparison: BrowserMethodBodyComparison,
): readonly BrowserMethodBodyProducer[] {
  // C# is the primary region; IL keeps its outcome visible next to it.
  return [...comparison.producers].sort((left, right) =>
    (left.producer === "CSharp" ? 0 : 1) - (right.producer === "CSharp" ? 0 : 1));
}

function renderResult(
  state: MethodBodyDiffState,
  escapeHtml: EscapeHtml,
  highlightCSharp: (value: string) => string,
): string {
  if (state.comparisonLoading) {
    return `<section class="method-body-result" aria-live="polite">
        <p class="method-body-progress"><span class="loader"></span>Comparing the submitted method bodies…</p>
      </section>`;
  }
  if (state.comparisonError) {
    return `<section class="method-body-result" aria-live="polite">
        <p class="method-body-failure" role="alert">${escapeHtml(state.comparisonError)}</p>
      </section>`;
  }
  const comparison = state.comparison;
  if (!comparison) {
    return `<section class="method-body-result" aria-live="polite">
        <p class="method-body-empty">Choose an After method, then select Compare.</p>
      </section>`;
  }
  const request = comparison.request;
  return `
    <section class="method-body-result" aria-live="polite">
      <header class="method-body-result-head">
        <h3>Comparison</h3>
        <p class="method-body-status">
          <span data-method-body-stage>${escapeHtml(comparison.stage)}</span>
          <span data-method-body-comparison-outcome>${escapeHtml(comparison.outcome)}</span>
        </p>
      </header>
      <div class="method-body-pair method-body-result-pair">
        <section data-method-body-side="before" aria-label="Compared Before method">
          <h4>Before</h4>
          ${renderSelectionIdentity(request.before, escapeHtml, "no Before method")}
        </section>
        <section data-method-body-side="after" aria-label="Compared After method">
          <h4>After</h4>
          ${renderSelectionIdentity(request.after, escapeHtml, "no After method")}
        </section>
      </div>
      ${orderedProducers(comparison).map(producer =>
        renderProducer(producer, escapeHtml, highlightCSharp)).join("")}
      ${renderDiagnostics(comparison.diagnostics, escapeHtml, "Comparison diagnostics")}
    </section>`;
}

export function renderMethodBodyDiffModal(
  options: MethodBodyDiffRenderOptions,
): string {
  const { state, escapeHtml, highlightCSharp } = options;
  if (!state.open) return "";
  const targets = state.targets;
  // Before is fixed by the launching selection: the owner-issued inventory identity once it
  // arrives, and the launching coordinates while it is still loading.
  const before: MethodBodyIdentityFields | null =
    targets?.before ?? state.context;
  const candidate = methodBodyChoiceForKey(targets, state.candidateKey);
  const body = state.unavailableReason
    ? `<section class="method-body-unavailable" role="alert">
        <h3>Comparison is unavailable here</h3>
        <p>${escapeHtml(state.unavailableReason)}</p>
      </section>`
    : `
      <div class="method-body-pair">
        <section data-method-body-side="before" aria-label="Before method">
          <h3>Before</h3>
          ${renderIdentity(
            before,
            escapeHtml,
            "The launching method is unavailable.")}
        </section>
        <section data-method-body-side="after" aria-label="After method">
          <h3>After</h3>
          ${renderChooser(state, escapeHtml)}
          ${renderSelectionIdentity(
            candidate,
            escapeHtml,
            "No After method is chosen yet.")}
        </section>
      </div>
      ${renderAssemblyLine(
        state.comparison?.request ?? state.submittedRequest,
        targets?.assembly ?? "",
        targets?.packageId ?? "",
        targets?.version ?? "",
        targets?.framework ?? "",
        targets?.moduleVersionId ?? "",
        escapeHtml)}
      <div class="method-body-actions">
        <button id="method-body-diff-compare" type="button"
          data-method-body-action="compare"${candidate && !state.comparisonLoading ? "" : " disabled"}>Compare</button>
        ${candidate
          ? ""
          : `<span class="method-body-empty">Choose an After method to enable Compare.</span>`}
      </div>
      ${renderResult(state, escapeHtml, highlightCSharp)}`;
  return `
    <div id="method-body-diff-backdrop" class="method-body-modal-backdrop">
      <section id="method-body-diff-modal" class="method-body-modal"
        role="dialog" aria-modal="true" aria-labelledby="method-body-diff-title">
        <header class="method-body-modal-head">
          <div>
            <p class="section-eyebrow">Compare method bodies</p>
            <h2 id="method-body-diff-title" tabindex="-1">Method Body Diff</h2>
          </div>
          <div class="method-body-modal-head-actions">
            <button id="method-body-diff-close" type="button"
              data-method-body-action="close">Close</button>
          </div>
        </header>
        <div class="method-body-modal-body">
          ${body}
        </div>
      </section>
    </div>`;
}

function isHtmlEventTarget(value: EventTarget | null): value is HTMLElement {
  return value !== null && "dataset" in value && "addEventListener" in value;
}

function htmlTarget(value: EventTarget | null): HTMLElement | null {
  return isHtmlEventTarget(value) ? value : null;
}

export function bindMethodBodyDiff(
  root: ParentNode,
  actions: MethodBodyDiffBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-method-body-action]")
    .forEach(element => {
      element.addEventListener("click", event => {
        const target = htmlTarget(event.currentTarget);
        const action = target?.dataset.methodBodyAction;
        if (action === "open") actions.onAction({ kind: "open" });
        else if (action === "close") actions.onAction({ kind: "close" });
        else if (action === "compare") actions.onAction({ kind: "compare" });
      });
    });

  const chooser =
    root.querySelector<HTMLSelectElement>("[data-method-body-candidate]");
  chooser?.addEventListener("change", () => {
    actions.onAction({ kind: "select", key: chooser.value });
  });

  const filter =
    root.querySelector<HTMLInputElement>("[data-method-body-filter]");
  filter?.addEventListener("input", () => {
    actions.onAction({
      kind: "filter",
      value: filter.value,
      caret: filter.selectionStart ?? filter.value.length,
    });
  });

  const backdrop =
    root.querySelector<HTMLElement>("#method-body-diff-backdrop");
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop) actions.onAction({ kind: "close" });
  });

  const modal = root.querySelector<HTMLElement>("#method-body-diff-modal");
  modal?.addEventListener("keydown", event => {
    if (event.key === "Tab") trapModalTab(modal, event);
  });
}
