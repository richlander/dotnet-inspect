import type { BrowserResourceTriage } from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector, type AnalysisInspectorContext } from "./analysis-inspector.ts";

import { renderTriageCode, triageMemberLabel } from "./triage-code.ts";

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
    const note = `<p class="library-analysis-note">ArrayPool exception-cleanup candidates: an exception may bypass Return and cause pool churn. Static evidence has medium confidence. Actionability uncertain means the operation is not classified; inspect the code to judge its impact. Return the pooled array from finally or catch-all cleanup.</p>`;
    const rows = data.candidates.map(candidate => {
      const display = triageMemberLabel(candidate.bodyTypeId ?? candidate.typeId ?? "", candidate.bodyMemberName ?? candidate.method);
      const link = candidate.typeId && candidate.stableSelector
        ? `<button type="button" class="resource-triage-member" data-perf-selector="${escape(candidate.stableSelector)}" data-perf-assembly="${escape(candidate.assembly)}" data-perf-type="${escape(candidate.typeId)}">${escape(display)}</button>`
        : `<strong title="${escape(candidate.method)}">${escape(display)}</strong>`;
      const boundaries = candidate.boundaries.map(boundary =>
        `<li><code>${il(boundary.ilOffset)}</code> ${escape(boundary.operation)} <span>${escape(boundaryLabels[boundary.kind] ?? boundary.kind)}</span></li>`).join("");
      const code = candidate.bodyTypeId && candidate.bodyMemberName
        ? renderTriageCode({ assembly: candidate.assembly, typeId: candidate.bodyTypeId,
            memberName: candidate.bodyMemberName, selector: candidate.stableSelector ?? "triage",
            methodToken: candidate.methodToken }, escape) : "";
      return `<article class="triage-item resource-triage-candidate">
        <div class="perf-row"><span class="perf-count" aria-hidden="true">△</span><span class="perf-member"><span class="perf-name">${link}</span><span class="perf-shapes">${escape(candidate.resource)} · ${escape(actionabilityLabels[candidate.actionability] ?? candidate.actionability)} · Acquire <code>${il(candidate.acquireOffset)}</code></span></span><span class="perf-meta"><span class="perf-confidence">${escape(candidate.confidence.toLowerCase())}</span></span></div>
        <ul class="triage-boundaries">${boundaries}</ul>
        ${code}
      </article>`;
    }).join("");
    const empty = data.candidates.length ? "" : `<section class="document-section empty-document"><h2>${partial ? "No candidates in the available evidence" : "No ArrayPool exception-cleanup candidates found"}</h2><p>${partial ? "The census is incomplete; absence cannot be established." : "No candidates were found within the supported ArrayPool analysis."}</p></section>`;
    content = warning + note + rows + empty;
  }
  return renderAnalysisInspector(options, "resource-triage", status, content);
}
