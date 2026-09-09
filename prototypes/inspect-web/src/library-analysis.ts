import type { BrowserPackagePerformance } from "./facades/inspect-web-analysis.d.ts";

type LibraryAnalysisResult = Pick<
  BrowserPackagePerformance,
  "members" | "inspectionError" | "nonPublicOpportunities" | "totalOpportunities"
>;

export interface LibraryAnalysisOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: LibraryAnalysisResult | null;
  escapeHtml: (value: unknown) => string;
}

function shortTypeName(fullName: string): string {
  const generic = fullName.indexOf("<");
  const head = generic < 0 ? fullName : fullName.slice(0, generic);
  const tail = generic < 0 ? "" : fullName.slice(generic);
  const dot = head.lastIndexOf(".");
  return (dot < 0 ? head : head.slice(dot + 1)) + tail;
}

export function renderLibraryAnalysisSurface(options: LibraryAnalysisOptions): string {
  const {
    libraryName, assemblyIdentity, assetPath, coordinate,
    requireLibrary, pickerHtml, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to classify allocation and performance opportunities across its method bodies.</p></section>`;
  } else if (loading && fresh) {
    status = "Analyzing allocations\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing allocations&hellip;</h2><p>Classifying allocation and performance opportunities across this library's method bodies.</p></section>`;
  } else if (fresh && error) {
    status = "Analysis failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Analysis failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else {
      const members = resolved.members ?? [];
      const partial = Boolean(resolved.inspectionError);
      const nonPublicStatus = resolved.nonPublicOpportunities > 0
        ? ` \u00b7 ${resolved.nonPublicOpportunities.toLocaleString()} non-public`
        : "";
      status = `${members.length.toLocaleString()} public member${members.length === 1 ? "" : "s"} \u00b7 ${resolved.totalOpportunities.toLocaleString()} opportunit${resolved.totalOpportunities === 1 ? "y" : "ies"}${nonPublicStatus}${partial ? " \u00b7 partial" : ""}`;
      const warning = partial
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; This library could not be analyzed completely</strong><ul><li><code>${escapeHtml(resolved.inspectionError)}</code></li></ul></section>`
        : "";
      const note = members.length
        ? `<p class="library-analysis-note">Ranked by product triage policy. Static IL classification &mdash; confirm impact with a benchmark or profiler. Select a member to open its API details.</p>`
        : "";
      const rows = members.map(member => {
        const display = `${shortTypeName(member.typeId)}.${member.memberName}`;
        const shapes = member.shapes
          .map(shape => `<span class="perf-shape">${escapeHtml(shape)}</span>`)
          .join("");
        const loopBadge = member.inLoopCount > 0
          ? `<span class="perf-loop" title="${member.inLoopCount} in a loop">&#x21BB; ${member.inLoopCount}</span>`
          : "";
        return `<button class="perf-row" data-perf-selector="${escapeHtml(member.stableSelector)}" data-perf-assembly="${escapeHtml(member.assembly)}" data-perf-type="${escapeHtml(member.typeId)}" title="${escapeHtml(member.typeId)}.${escapeHtml(member.memberName)} &mdash; open member">
          <span class="perf-count">${member.opportunityCount}</span>
          <span class="perf-member"><span class="perf-name">${escapeHtml(display)}</span><span class="perf-shapes">${shapes}</span></span>
          <span class="perf-meta">${loopBadge}<span class="perf-confidence perf-${escapeHtml((member.confidence || "").toLowerCase())}">${escapeHtml(member.confidence || "\u2014")}</span></span>
        </button>`;
      }).join("");
      const nonPublicNote = resolved.nonPublicOpportunities > 0
        ? ` ${resolved.nonPublicOpportunities.toLocaleString()} opportunit${resolved.nonPublicOpportunities === 1 ? "y is" : "ies are"} in non-public members.`
        : "";
      const empty = partial
        ? `<section class="document-section empty-document"><h2>Analysis incomplete</h2><p>No public-member results are available from this incomplete analysis.</p></section>`
        : `<section class="document-section empty-document"><span class="large-glyph">&#x25C7;</span><h2>No public allocation hot spots</h2><p>${resolved.totalOpportunities.toLocaleString()} allocation/performance opportunit${resolved.totalOpportunities === 1 ? "y was" : "ies were"} classified, but none surface on a public member of ${escapeHtml(libraryName)}.${nonPublicNote}</p></section>`;
      content = `${warning}${note}${members.length ? `<div class="perf-list">${rows}</div>` : empty}`;
    }
  }
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-analysis-surface${pickerHtml ? " library-analysis-with-controls" : ""}" aria-labelledby="library-analysis-title">
    <header class="api-surface-head">
      <h1 id="library-analysis-title">Analysis</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    ${pickerHtml ? `<section class="library-analysis-controls" aria-label="Analysis library">${pickerHtml}</section>` : ""}
    <div class="library-analysis-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}
