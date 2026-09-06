import type {
  BrowserSourceComparisonEndpoint,
  BrowserSourceComparisonLine,
} from "./facades/inspect-web-source.d.ts";
import type { EscapeHtml } from "./csharp-highlighting.ts";
import { isExactSourceComparisonVersion, type SourceDiffState } from "./source-comparison.ts";
import { trapModalTab } from "./shell-controls.ts";

export const SOURCE_DIFF_ACTION_SELECTOR = "#compare-authored-source";
export const SOURCE_DIFF_VERSION_SELECTOR = "#source-diff-after-version";

export interface SourceComparisonAvailability {
  readonly available: boolean;
  readonly reason: string;
}

export type SourceDiffAction =
  | { readonly kind: "open" | "close" | "compare" }
  | { readonly kind: "version"; readonly value: string; readonly caret: number };

export function renderSourceComparisonAction(
  availability: SourceComparisonAvailability,
  escapeHtml: EscapeHtml,
): string {
  return `<button id="compare-authored-source" type="button"
      data-source-diff-action="open"${availability.available
        ? ""
        : ' aria-describedby="compare-authored-source-reason" disabled'}
      title="Compare this authored declaration with the same member in another package version">Compare authored source</button>
    ${availability.available ? "" : `<span id="compare-authored-source-reason" class="working-surface-note">${escapeHtml(availability.reason)}</span>`}`;
}

function field(label: string, value: string | null, escapeHtml: EscapeHtml): string {
  return value === null || value === "" ? "" :
    `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd></div>`;
}

function provenanceLink(label: string, value: string | null, escapeHtml: EscapeHtml): string {
  if (!value) return "";
  let href: string | null = null;
  try {
    const url = new URL(value);
    if (url.protocol === "https:" || url.protocol === "http:") href = url.href;
  } catch {
    // Keep non-URL provenance visible without turning it into navigation.
  }
  return `<div><dt>${escapeHtml(label)}</dt><dd>${href
    ? `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">${escapeHtml(value)}</a>`
    : escapeHtml(value)}</dd></div>`;
}

function endpoint(
  value: BrowserSourceComparisonEndpoint,
  side: "before" | "after",
  expandText: boolean,
  escapeHtml: EscapeHtml,
): string {
  const label = side === "before" ? "Before" : "After";
  return `<section data-source-diff-side="${side}" aria-label="${label} Source">
      <h4>${label}</h4>
      <p class="method-body-label">${escapeHtml(value.packageId)} <strong>${escapeHtml(value.version)}</strong></p>
      <p class="method-body-status" data-source-diff-endpoint-state="${escapeHtml(value.state)}">${escapeHtml(value.state)}</p>
      ${value.detail ? `<p class="method-body-empty">${escapeHtml(value.detail)}</p>` : ""}
      <details><summary>${label} provenance and identity</summary>
      <dl class="method-body-identity">
        ${field("Framework", value.framework, escapeHtml)}
        ${field("Assembly", value.assembly, escapeHtml)}
        ${field("Asset", value.assetPath, escapeHtml)}
        ${field("Identity", value.assemblyIdentity, escapeHtml)}
        ${field("MVID", value.moduleVersionId, escapeHtml)}
        ${field("Member", value.memberIdentity, escapeHtml)}
        ${field("MethodDef", value.metadataToken == null ? null
          : `0x${value.metadataToken.toString(16).padStart(8, "0")}`, escapeHtml)}
        ${provenanceLink("Source", value.sourceUrl, escapeHtml)}
        ${provenanceLink("Repository", value.repositoryUrl, escapeHtml)}
        ${field("Revision", value.revision, escapeHtml)}
      </dl>
      </details>
      ${value.text !== null
        ? `<details class="source-diff-declaration"${expandText ? " open" : ""}>
            <summary>${label} authored declaration</summary>
            <pre><code class="language-csharp">${escapeHtml(value.text)}</code></pre>
          </details>`
        : `<p class="method-body-empty">No authored declaration is available for ${label}. This is not an empty declaration or decompiled fallback.</p>`}
    </section>`;
}

function lineSide(
  side: "before" | "after",
  coordinate: number | null,
  text: string | null,
  escapeHtml: EscapeHtml,
): string {
  const label = side === "before" ? "Before" : "After";
  return `<div class="method-body-value" data-source-diff-line-side="${side}">
      <span class="method-body-value-label">${label}${coordinate === null ? " · absent" : ` · line ${coordinate}`}</span>
      ${text === null ? "" : `<pre><code class="language-csharp">${escapeHtml(text)}</code></pre>`}
    </div>`;
}

function nativeLine(line: BrowserSourceComparisonLine, escapeHtml: EscapeHtml): string {
  return `<li class="method-body-row source-diff-line"
      data-source-diff-kind="${escapeHtml(line.kind)}"
      data-source-diff-difference="${escapeHtml(line.difference)}">
      <p class="method-body-row-head">
        <span class="method-body-row-kind">${escapeHtml(line.kind)}</span>
        ${line.difference === "None" ? "" : `<span class="source-diff-movement">${escapeHtml(line.difference)}</span>`}
      </p>
      <div class="method-body-row-values">
        ${lineSide("before", line.beforeLine, line.beforeText, escapeHtml)}
        ${lineSide("after", line.afterLine, line.afterText, escapeHtml)}
      </div>
    </li>`;
}

function result(state: SourceDiffState, escapeHtml: EscapeHtml): string {
  const request = state.submittedRequest;
  const submitted = request
    ? `<p class="method-body-scope" data-source-diff-submitted>${escapeHtml(request.packageId)} · Before ${escapeHtml(request.beforeVersion)} → After ${escapeHtml(request.afterVersion)} · ${escapeHtml(request.typeIdentity)}.${escapeHtml(request.memberName)}</p>`
    : "";
  if (state.loading)
    return `${submitted}<p class="method-body-progress" role="status"><span class="loader"></span>Acquiring and comparing the submitted authored declarations…</p>`;
  if (state.error)
    return `${submitted}<p class="method-body-failure" role="alert">${escapeHtml(state.error)}</p>`;
  const comparison = state.comparison;
  if (!comparison)
    return `<p class="method-body-empty">Choose an exact After version, then select Compare. Source is acquired only when you compare.</p>`;
  const verdict = comparison.status === "Compared"
    ? comparison.isExact ? "Exact authored source" : "Changed authored source"
    : comparison.status === "Unavailable" ? "Source comparison unavailable"
      : "Source comparison failed";
  return `${submitted}
    <header class="method-body-result-head">
      <h3 data-source-diff-verdict>${escapeHtml(verdict)}</h3>
      <p class="method-body-status">
        <span data-source-diff-status>${escapeHtml(comparison.status)}</span>
        <span data-source-diff-exact="${comparison.isExact}">${comparison.status !== "Compared" ? "Not compared" : comparison.isExact ? "Exact" : "Not exact"}</span>
      </p>
    </header>
    ${comparison.failure ? `<p class="method-body-failure" role="alert">${escapeHtml(comparison.failure)}</p>` : ""}
    <div class="method-body-pair source-diff-endpoints">
      ${endpoint(comparison.before, "before", comparison.status !== "Compared" || comparison.lines.length === 0, escapeHtml)}
      ${endpoint(comparison.after, "after", comparison.status !== "Compared" || comparison.lines.length === 0, escapeHtml)}
    </div>
    ${comparison.status === "Compared"
      ? `<section aria-label="Native Source line relations">
          <p class="method-body-empty">Native line relations · one-based, declaration-relative coordinates. Movement is independent of content changes.</p>
          <ol class="method-body-rows source-diff-lines">${comparison.lines.map(line => nativeLine(line, escapeHtml)).join("")}</ol>
        </section>`
      : ""}`;
}

export function renderSourceDiffModal(options: {
  readonly state: SourceDiffState;
  readonly escapeHtml: EscapeHtml;
}): string {
  const { state, escapeHtml } = options;
  if (!state.open) return "";
  const context = state.context;
  const body = state.unavailableReason
    ? `<section class="method-body-unavailable" role="alert">
        <h3>Comparison is unavailable here</h3><p>${escapeHtml(state.unavailableReason)}</p>
      </section>`
    : `<div class="method-body-pair">
        <section aria-label="Before selection">
          <h3>Before</h3>
          <p class="method-body-label">${escapeHtml(context?.packageId ?? "")} <strong>${escapeHtml(context?.version ?? "")}</strong></p>
          <p class="method-body-label"><code>${escapeHtml(context?.label ?? "")}</code></p>
          <dl class="method-body-identity">
            ${field("Type", context?.typeIdentity ?? null, escapeHtml)}
            ${field("Assembly", context?.assembly ?? null, escapeHtml)}
            ${field("Framework", context?.framework ?? null, escapeHtml)}
          </dl>
        </section>
        <section aria-label="After selection">
          <h3>After</h3>
          <p class="method-body-label">${escapeHtml(context?.packageId ?? "")} · same assembly and logical member</p>
          <label class="method-body-field" for="source-diff-after-version">
            <span>After version (exact)</span>
            <input id="source-diff-after-version" type="text" data-source-diff-version
              autocomplete="off" spellcheck="false" aria-describedby="source-diff-version-help"
              placeholder="For example, 10.0.1" value="${escapeHtml(state.afterVersion)}" />
          </label>
          <p id="source-diff-version-help" class="method-body-empty">Enter an exact package version. The same version is valid; ranges and floating versions are not.</p>
        </section>
      </div>
      <div class="method-body-actions">
        <button id="source-diff-compare" type="button" data-source-diff-action="compare"${context && isExactSourceComparisonVersion(state.afterVersion) && !state.loading ? "" : " disabled"}>Compare</button>
      </div>
      <section class="method-body-result" aria-live="polite">${result(state, escapeHtml)}</section>`;
  return `<div id="source-diff-backdrop" class="method-body-modal-backdrop">
      <section id="source-diff-modal" class="method-body-modal source-diff-modal"
        role="dialog" aria-modal="true" aria-labelledby="source-diff-title">
        <header class="method-body-modal-head">
          <div><p class="section-eyebrow">Compare authored source</p>
            <h2 id="source-diff-title" tabindex="-1">Source Diff</h2></div>
          <button id="source-diff-close" type="button" data-source-diff-action="close">Close</button>
        </header>
        <div class="method-body-modal-body">${body}</div>
      </section>
    </div>`;
}

export function bindSourceDiff(
  root: ParentNode,
  actions: { readonly onAction: (action: SourceDiffAction) => void },
): void {
  root.querySelectorAll<HTMLElement>("[data-source-diff-action]").forEach(element => {
    element.addEventListener("click", () => {
      const kind = element.dataset.sourceDiffAction;
      if (kind === "open" || kind === "close" || kind === "compare")
        actions.onAction({ kind });
    });
  });
  const input = root.querySelector<HTMLInputElement>("[data-source-diff-version]");
  input?.addEventListener("input", () => actions.onAction({
    kind: "version", value: input.value, caret: input.selectionStart ?? input.value.length,
  }));
  const backdrop = root.querySelector<HTMLElement>("#source-diff-backdrop");
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop) actions.onAction({ kind: "close" });
  });
  const modal = root.querySelector<HTMLElement>("#source-diff-modal");
  modal?.addEventListener("keydown", event => {
    if (event.key === "Tab") trapModalTab(modal, event);
  });
}
