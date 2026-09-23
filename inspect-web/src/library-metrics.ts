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

function metricValue(value: number | null): string {
  return value === null ? "\u2014" : value.toLocaleString();
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
        <td>${metricValue(distribution.completeBodyCount)}</td>
        <td>${metricValue(distribution.minimum)}</td>
        <td>${metricValue(distribution.p50)}</td>
        <td>${metricValue(distribution.p90)}</td>
        <td>${metricValue(distribution.p95)}</td>
        <td>${metricValue(distribution.p99)}</td>
        <td>${metricValue(distribution.maximum)}</td>
      </tr>`).join("");
      const coverageGap = population
        && population.profiledPhysicalEvidenceBodyCount
          < population.physicalEvidenceBodyCount;
      const incomplete = population && (
        coverageGap || population.incompleteProfileCount > 0)
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; Metrics are qualified</strong><p>${
          coverageGap
            ? `${population.profiledPhysicalEvidenceBodyCount.toLocaleString()} of ${population.physicalEvidenceBodyCount.toLocaleString()} physical bodies were profiled.`
            : ""
        }${
          population.incompleteProfileCount > 0
            ? ` ${population.incompleteProfileCount.toLocaleString()} profiled bodies have incomplete metrics.`
            : ""
        }</p>${
          resolved.diagnostics.length > 0
            ? `<ul>${resolved.diagnostics.map(diagnostic =>
              `<li>${escapeHtml(diagnostic)}</li>`).join("")}</ul>`
            : ""
        }</section>`
        : "";
      const populationRows = population
        ? `<section class="document-section">
            <div class="section-title"><h2>Population</h2><span>${population.completeProfileCount.toLocaleString()} complete profiles</span></div>
            <dl class="fact-rows library-metrics-population">
              <div><dt>Physical evidence bodies</dt><dd><code>${population.physicalEvidenceBodyCount.toLocaleString()}</code></dd></div>
              <div><dt>Profiled bodies</dt><dd><code>${population.profiledPhysicalEvidenceBodyCount.toLocaleString()}</code></dd></div>
              <div><dt>Complete profiles</dt><dd><code>${population.completeProfileCount.toLocaleString()}</code></dd></div>
              <div><dt>Incomplete profiles</dt><dd><code>${population.incompleteProfileCount.toLocaleString()}</code></dd></div>
              <div><dt>Logical owners</dt><dd><code>${population.logicalOwnerCount.toLocaleString()}</code></dd></div>
            </dl>
          </section>`
        : "";
      const asyncDisposition = resolved.asyncStateMachinePresence
        ? `<section class="document-section">
            <div class="section-title"><h2>${escapeHtml(metricLabel(resolved.asyncStateMachinePresence.name))}</h2><span>${resolved.asyncStateMachinePresence.completeBodyCount.toLocaleString()} complete bodies</span></div>
            <dl class="fact-rows library-metrics-population">
              <div><dt>Present</dt><dd><code>${resolved.asyncStateMachinePresence.presentCount.toLocaleString()}</code></dd></div>
              <div><dt>Absent</dt><dd><code>${resolved.asyncStateMachinePresence.absentCount.toLocaleString()}</code></dd></div>
            </dl>
          </section>`
        : "";
      content = `${incomplete}
        <section class="document-section library-metrics-intro">
          <p>Compiled IL metrics for <strong>${escapeHtml(libraryName)}</strong>. These are structural implementation measures, not authored-source complexity.</p>
        </section>
        ${populationRows}
        <section class="document-section">
          <div class="section-title"><h2>Distributions</h2><span>${distributions.length.toLocaleString()} measure${distributions.length === 1 ? "" : "s"}</span></div>
          ${distributions.length
            ? `<div class="library-metrics-table-wrap" role="region" aria-label="Metric distributions" tabindex="0">
                <table class="library-metrics-table">
                  <thead><tr><th scope="col">Metric</th><th scope="col">Complete</th><th scope="col">Min</th><th scope="col">P50</th><th scope="col">P90</th><th scope="col">P95</th><th scope="col">P99</th><th scope="col">Max</th></tr></thead>
                  <tbody>${rows}</tbody>
                </table>
              </div>`
            : `<div class="empty-list">No complete metric distributions were produced.</div>`}
        </section>
        ${asyncDisposition}`;
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
