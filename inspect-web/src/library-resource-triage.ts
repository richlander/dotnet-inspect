import type { BrowserResourceTriage, BrowserResourceTriageCandidate } from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector, type AnalysisInspectorContext } from "./analysis-inspector.ts";
import { triageMemberLabel } from "./triage-code.ts";

interface ResourceTriageOptions extends AnalysisInspectorContext {
  libraryName: string;
  requireLibrary: boolean;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserResourceTriage | null;
}

const actionabilityLabels: Record<string, string> = {
  UntrustedActionable: "External-input boundary",
  TrustedLowActionability: "In-memory transform",
  Unknown: "Actionability uncertain",
};

const boundaryLabels: Record<string, string> = {
  ExternalInput: "External-input boundary",
  InMemoryTransform: "In-memory transform",
  Unknown: "Unclassified operation",
};

function il(offset: number): string {
  return `IL_${offset.toString(16).padStart(4, "0").toUpperCase()}`;
}

export function renderLibraryResourceTriageSurface(options: ResourceTriageOptions): string {
  const { escapeHtml: escape, fresh, loading, error } = options;
  const data = fresh ? options.data : null;
  let status: string;
  let content: string;
  if (options.requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to inspect ArrayPool exception cleanup.</p></section>`;
  } else if (fresh && error) {
    status = "Resource triage failed";
    content = `<section class="document-section empty-document"><h2>Resource triage failed</h2><p>${escape(error)}</p></section>`;
  } else if (!data || loading) {
    status = "Analyzing resource cleanup\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing resource cleanup&hellip;</h2><p>Inspecting ArrayPool acquisition and exception cleanup across this library's method bodies.</p></section>`;
  } else if (data.outcome !== "available" && data.outcome !== "incomplete") {
    status = data.outcome === "failed" || data.outcome === "rejected" ? "Resource triage failed" : "Resource triage unavailable";
    content = `<section class="document-section empty-document"><h2>${status}</h2><p>${escape(data.inspectionError || "No resource lifecycle evidence is available.")}</p></section>`;
  } else {
    const partial = data.outcome === "incomplete";
    status = `${data.candidates.length.toLocaleString()} candidate${data.candidates.length === 1 ? "" : "s"}${partial ? " \u00b7 incomplete" : ""}`;
    const warning = partial
      ? `<section class="document-section metadata-warning"><strong>This library could not be analyzed completely</strong></section>` : "";
    const rows = data.candidates.map(candidate => {
      const display = candidate.bodyMemberName
        ? triageMemberLabel(candidate.bodyTypeId ?? candidate.typeId ?? "", candidate.bodyMemberName)
        : candidate.method;
      const navigation = candidate.typeId && candidate.stableSelector
        ? `button type="button" data-resource-token="${candidate.methodToken}" data-perf-selector="${escape(candidate.stableSelector)}" data-perf-assembly="${escape(candidate.assembly)}" data-perf-type="${escape(candidate.typeId)}"`
        : "div";
      const tag = navigation.startsWith("button") ? "button" : "div";
      return `<article class="triage-item resource-triage-candidate"><${navigation} class="perf-row" title="${escape(display)}${tag === "button" ? " — open member Resource Triage" : ""}">
        <span class="perf-count" aria-hidden="true">△</span>
        <span class="perf-member"><span class="perf-name">${escape(display)}</span><span class="perf-shapes"><span class="perf-shape">Pool churn on exception</span><span class="perf-shape">${escape(actionabilityLabels[candidate.actionability] ?? candidate.actionability)}</span></span></span>
        <span class="perf-meta"><span class="perf-confidence">${escape(candidate.confidence.toLowerCase())}</span></span>
      </${tag}></article>`;
    }).join("");
    const empty = data.candidates.length ? "" : `<section class="document-section empty-document"><h2>${partial ? "No candidates in the available evidence" : "No ArrayPool exception-cleanup candidates found"}</h2><p>${partial ? "The census is incomplete; absence cannot be established." : "No candidates were found within the supported ArrayPool analysis."}</p></section>`;
    content = warning + `<div class="perf-list">${rows}</div>` + empty;
  }
  return renderAnalysisInspector(options, "resource-triage", status, content);
}

/** Detailed evidence belongs to the selected member, rather than the library list. */
export function renderMemberResourceTriage(
  candidates: readonly BrowserResourceTriageCandidate[],
  escape: (value: unknown) => string,
): string {
  if (!candidates.length) return "";
  return `<section class="document-section member-resource-triage">${candidates.map(candidate => `
    <article>
      <p>${escape(candidate.resource)} · ${escape(actionabilityLabels[candidate.actionability] ?? candidate.actionability)} · ${escape(candidate.confidence.toLowerCase())} confidence</p>
      <p>Acquire <code>${il(candidate.acquireOffset)}</code></p>
      <ul class="triage-boundaries">${candidate.boundaries.map(boundary => `<li><code>${il(boundary.ilOffset)}</code> ${escape(boundary.operation)} <span>${escape(boundaryLabels[boundary.kind] ?? boundary.kind)}</span></li>`).join("")}</ul>
      <p>An exception may bypass Return. Return the pooled array from finally or catch-all cleanup.</p>
    </article>`).join("")}</section>`;
}
