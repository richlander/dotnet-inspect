import type { BrowserLibraryMetrics } from "./facades/inspect-web-analysis.d.ts";

export interface LibraryMetricsOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserLibraryMetrics | null;
  escapeHtml: (value: unknown) => string;
}

function metricLabel(metric: string): string {
  return metric
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, character => character.toUpperCase());
}

export function renderLibraryMetricsSurface(
  options: LibraryMetricsOptions,
): string {
  const {
    libraryName, assemblyIdentity, assetPath, coordinate,
    requireLibrary, pickerHtml, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to measure</h2><p>Choose a .NET platform library above to summarize compiled implementation metrics.</p></section>`;
  } else if (loading && fresh) {
    status = "Measuring library\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Measuring library&hellip;</h2><p>Computing Research-owned structural distributions across this library's method bodies.</p></section>`;
  } else if (fresh && error) {
    status = "Metrics failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Metrics failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else if (resolved.outcome !== "available") {
      status = resolved.outcome === "failed" ? "Metrics failed" : "Metrics unavailable";
      content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>${escapeHtml(status)}</h2><p>${escapeHtml(resolved.failure || "The Research document could not be produced.")}</p></section>`;
    } else {
      const population = resolved.population;
      const distributions = resolved.distributions ?? [];
      status = `${(population?.completeProfileCount ?? 0).toLocaleString()} complete bodies`;
      const rows = distributions.map(distribution => `<tr>
        <th scope="row">${escapeHtml(metricLabel(distribution.metric))}</th>
        <td>${distribution.minimum ?? "\u2014"}</td>
        <td>${distribution.p50 ?? "\u2014"}</td>
        <td>${distribution.p90 ?? "\u2014"}</td>
        <td>${distribution.maximum ?? "\u2014"}</td>
      </tr>`).join("");
      const incomplete = population && population.incompleteProfileCount > 0
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; Some method bodies are incomplete</strong><p>${population.incompleteProfileCount.toLocaleString()} of ${population.profiledPhysicalEvidenceBodyCount.toLocaleString()} profiled bodies have incomplete metrics.</p></section>`
        : "";
      content = `${incomplete}
        <section class="document-section">
          <p>Compiled IL metrics for <strong>${escapeHtml(libraryName)}</strong>. These are structural implementation measures, not authored-source complexity.</p>
          <table class="metrics-table"><thead><tr><th>Metric</th><th>Min</th><th>P50</th><th>P90</th><th>Max</th></tr></thead><tbody>${rows}</tbody></table>
        </section>`;
    }
  }
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-analysis-surface${pickerHtml ? " library-analysis-with-controls" : ""}" aria-labelledby="library-metrics-title">
    <header class="api-surface-head">
      <h1 id="library-metrics-title">Library Metrics</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    ${pickerHtml ? `<section class="library-analysis-controls" aria-label="Metrics library">${pickerHtml}</section>` : ""}
    <div class="library-analysis-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}
