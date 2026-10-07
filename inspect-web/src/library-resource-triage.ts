import type { BrowserResourceTriage } from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector, type AnalysisInspectorContext } from "./analysis-inspector.ts";

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
  Unknown: "Unclassified boundary",
};

const boundaryLabels: Record<string, string> = {
  ExternalInput: "External-input boundary",
  InMemoryTransform: "In-memory transform",
  Unknown: "Unclassified boundary",
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
    const warnings = [
      ...data.limitations.map(item => `${item.kind}${item.method ? ` (${item.method})` : ""}: ${item.detail}`),
      ...(data.inspectionError ? [data.inspectionError] : []),
      ...data.diagnostics.map(item => item.summary),
    ];
    const warning = partial || warnings.length
      ? `<section class="document-section metadata-warning"><strong>${partial ? "This library could not be analyzed completely" : "Analysis diagnostics"}</strong><ul>${warnings.map(item => `<li>${escape(item)}</li>`).join("")}</ul></section>` : "";
    const note = `<p class="library-analysis-note">ArrayPool exception-cleanup candidates: an exception may bypass Return and cause pool churn. Static evidence has medium confidence. Return the pooled array from finally or catch-all cleanup.</p>`;
    const rows = data.candidates.map(candidate => {
      const link = candidate.typeId && candidate.stableSelector
        ? `<button type="button" class="resource-triage-member" data-perf-selector="${escape(candidate.stableSelector)}" data-perf-assembly="${escape(candidate.assembly)}" data-perf-type="${escape(candidate.typeId)}">${escape(candidate.method)}</button>`
        : `<strong>${escape(candidate.method)}</strong><span class="resource-triage-unlinked">Member navigation unavailable; IL evidence retained.</span>`;
      const boundaries = candidate.boundaries.map(boundary =>
        `<li><code>${il(boundary.ilOffset)}</code> ${escape(boundary.operation)} <span>${escape(boundaryLabels[boundary.kind] ?? boundary.kind)}</span></li>`).join("");
      return `<article class="document-section resource-triage-candidate">
        ${link}
        <p>${escape(candidate.resource)} &middot; ${escape(actionabilityLabels[candidate.actionability] ?? candidate.actionability)} &middot; ${escape(candidate.confidence.toLowerCase())} confidence</p>
        <p>Acquire: <code>${il(candidate.acquireOffset)}</code> &middot; Method: <code>0x${candidate.methodToken.toString(16).toUpperCase()}</code> &middot; Candidate: <code>${escape(candidate.candidateId)}</code></p>
        <p>Module: <code>${escape(candidate.moduleVersionId)}</code> &middot; Finding: <code>${escape(candidate.findingId)}</code></p>
        <ul>${boundaries}</ul>
      </article>`;
    }).join("");
    const empty = data.candidates.length ? "" : `<section class="document-section empty-document"><h2>${partial ? "No candidates in the available evidence" : "No ArrayPool exception-cleanup candidates found"}</h2><p>${partial ? "The census is incomplete; absence cannot be established." : "No candidates were found within the supported ArrayPool analysis."}</p></section>`;
    content = warning + note + rows + empty;
  }
  return renderAnalysisInspector(options, "resource-triage", status, content);
}
