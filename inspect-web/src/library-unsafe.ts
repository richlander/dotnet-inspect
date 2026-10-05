import type {
  BrowserPackageUnsafeFindings,
} from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector } from "./analysis-inspector.ts";

export interface LibraryUnsafeOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserPackageUnsafeFindings | null;
  escapeHtml: (value: unknown) => string;
}

function shortTypeName(fullName: string): string {
  const generic = fullName.indexOf("<");
  const head = generic < 0 ? fullName : fullName.slice(0, generic);
  const tail = generic < 0 ? "" : fullName.slice(generic);
  const dot = head.lastIndexOf(".");
  return (dot < 0 ? head : head.slice(dot + 1)) + tail;
}

export function renderLibraryUnsafeSurface(options: LibraryUnsafeOptions): string {
  const {
    libraryName, requireLibrary, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to list unsafe findings from its compiled declarations and method bodies.</p></section>`;
  } else if (loading && fresh) {
    status = "Analyzing unsafe findings\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing unsafe findings&hellip;</h2><p>Inspecting compiled declarations and IL without grading or auditing the findings.</p></section>`;
  } else if (fresh && error) {
    status = "Analysis failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Analysis failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else {
      const findings = resolved.findings ?? [];
      const memberCount = new Set(
        findings.map(finding =>
          `${finding.assembly}\u0000${finding.typeId}\u0000${finding.stableSelector}`),
      ).size;
      const partial = Boolean(resolved.inspectionError);
      const nonPublicStatus = resolved.nonPublicFindings > 0
        ? ` \u00b7 ${resolved.nonPublicFindings.toLocaleString()} non-public`
        : "";
      status = `${findings.length.toLocaleString()} public finding${findings.length === 1 ? "" : "s"} \u00b7 ${memberCount.toLocaleString()} member${memberCount === 1 ? "" : "s"}${nonPublicStatus}${partial ? " \u00b7 partial" : ""}`;
      const warning = partial
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; This library could not be analyzed completely</strong><ul><li><code>${escapeHtml(resolved.inspectionError)}</code></li></ul></section>`
        : "";
      const note = findings.length
        ? `<p class="library-analysis-note">Ungraded findings from compiled declarations and IL. These are neither audit results nor recommendations. Select a finding to inspect that member's safety facts.</p>`
        : "";
      const rows = findings.map(finding => {
        const display =
          `${shortTypeName(finding.typeId)}.${finding.memberName}`;
        const offset = finding.offset == null
          ? '<span class="unsafe-declaration">declaration</span>'
          : `<code>${escapeHtml(finding.offset)}</code>`;
        return `<button class="unsafe-row" data-unsafe-selector="${escapeHtml(finding.stableSelector)}" data-unsafe-assembly="${escapeHtml(finding.assembly)}" data-unsafe-type="${escapeHtml(finding.typeId)}" title="${escapeHtml(finding.typeId)}.${escapeHtml(finding.memberName)} &mdash; open member">
          <span class="unsafe-location">${offset}</span>
          <span class="unsafe-main"><span class="unsafe-name">${escapeHtml(display)}</span><code class="unsafe-operation">${escapeHtml(finding.operation)}</code><span class="unsafe-evidence">${escapeHtml(finding.evidence)}</span></span>
          <span class="unsafe-kind">${escapeHtml(finding.kind)}</span>
        </button>`;
      }).join("");
      const nonPublicNote = resolved.nonPublicFindings > 0
        ? ` ${resolved.nonPublicFindings.toLocaleString()} finding${resolved.nonPublicFindings === 1 ? " is" : "s are"} on non-public members.`
        : "";
      const empty = partial
        ? `<section class="document-section empty-document"><h2>Analysis incomplete</h2><p>No public-member findings are available from this incomplete analysis.</p></section>`
        : `<section class="document-section empty-document"><span class="large-glyph">&#x25C7;</span><h2>No public unsafe findings</h2><p>Compiled declaration and IL analysis produced no unsafe findings for navigable public members of ${escapeHtml(libraryName)}.${nonPublicNote}</p></section>`;
      content = `${warning}${note}${findings.length ? `<div class="unsafe-list">${rows}</div>` : empty}`;
    }
  }
  return renderAnalysisInspector(options, "unsafe", status, content);
}
