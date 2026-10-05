import type {
  BrowserPackageVulnerabilityFailure,
  BrowserPackageVulnerabilityResult,
} from "./facades/inspect-web-package.d.ts";

export interface PackageVulnerabilitiesOptions {
  packageId: string;
  packageVersion: string;
  loading: boolean;
  error: string;
  result: BrowserPackageVulnerabilityResult | null;
  escapeHtml: (value: unknown) => string;
}

function failureLabel(failure: BrowserPackageVulnerabilityFailure): string {
  switch (failure) {
    case "RequestLimitReached": return "The advisory request limit was reached.";
    case "ResponseByteLimitReached":
      return "An advisory response exceeded its byte limit.";
    case "AggregateResponseByteLimitReached":
      return "Advisory responses exceeded the aggregate byte limit.";
    case "DeadlineReached": return "The advisory lookup deadline was reached.";
    case "RateLimitOrForbidden":
      return "GitHub rejected or rate-limited the advisory lookup.";
    case "SourceUnavailable": return "The advisory source was unavailable.";
    case "InvalidData": return "The advisory source returned invalid data.";
    case "InvalidContinuation":
      return "The advisory source returned an invalid continuation.";
    default: return `Advisory lookup failed (${failure}).`;
  }
}

function observedDate(value: string): string {
  return value.slice(0, 10);
}

function availabilityLabel(
  availability: BrowserPackageVulnerabilityResult["availability"],
): string {
  switch (availability) {
    case "Partial": return "partial";
    case "Unavailable": return "unavailable";
    default: return "unknown";
  }
}

function severityClass(
  severity: BrowserPackageVulnerabilityResult["advisories"][number]["severity"],
): string {
  switch (severity) {
    case "Critical": return "critical";
    case "High": return "high";
    case "Medium": return "medium";
    case "Low": return "low";
    default: return "unknown";
  }
}

function resultStatus(result: BrowserPackageVulnerabilityResult): string {
  const count = result.advisories.length;
  const countLabel = `${count} reviewed ${
    count === 1 ? "advisory" : "advisories"
  }`;
  return result.availability === "Complete"
    ? countLabel
    : `${countLabel} · ${availabilityLabel(result.availability)} coverage`;
}

function renderResult(
  result: BrowserPackageVulnerabilityResult,
  escapeHtml: (value: unknown) => string,
): string {
  const failures = result.failures.length
    ? `<section class="package-vulnerability-notice" role="status">
        <strong>Coverage is incomplete</strong>
        <ul>${result.failures.map(failure =>
          `<li>${escapeHtml(failureLabel(failure))}</li>`).join("")}</ul>
      </section>`
    : "";
  if (result.advisories.length === 0) {
    const message = result.availability === "Complete"
      ? "No GitHub-reviewed NuGet advisories match this exact package version."
      : "No matching advisory was available from the incomplete lookup.";
    return `${failures}
      <section class="document-section empty-document package-vulnerability-empty">
        <span class="large-glyph">◇</span>
        <h2>No matching reviewed advisories</h2>
        <p>${escapeHtml(message)}</p>
      </section>`;
  }

  return `${failures}
    <section class="document-section package-vulnerability-list">
      <div class="section-title">
        <h2>Reviewed advisories</h2>
        <span>exact package version</span>
      </div>
      <div class="package-vulnerability-cards">
        ${result.advisories.map(advisory => {
          const identity = advisory.cveId
            ? `${advisory.ghsaId} · ${advisory.cveId}`
            : advisory.ghsaId;
          return `<article class="package-vulnerability-card">
            <div class="package-vulnerability-heading">
              <span class="package-vulnerability-severity severity-${severityClass(advisory.severity)}">${escapeHtml(advisory.severity)}</span>
              <h3>${escapeHtml(identity)}</h3>
            </div>
            <dl class="package-vulnerability-dates">
              <div><dt>Published</dt><dd>${escapeHtml(observedDate(advisory.publishedAt))}</dd></div>
              <div><dt>Updated</dt><dd>${escapeHtml(observedDate(advisory.updatedAt))}</dd></div>
            </dl>
            <a href="${escapeHtml(advisory.advisoryUrl)}" target="_blank" rel="noopener noreferrer">Open GitHub advisory</a>
          </article>`;
        }).join("")}
      </div>
    </section>`;
}

export function renderPackageVulnerabilities(
  options: PackageVulnerabilitiesOptions,
): string {
  const {
    packageId,
    packageVersion,
    loading,
    error,
    result,
    escapeHtml,
  } = options;
  const coordinate = `${packageId}@${packageVersion}`;
  const status = loading
    ? "checking"
    : error
      ? "query failed"
      : result
        ? resultStatus(result)
        : "loading";
  const content = loading
    ? `<section class="document-section package-vulnerabilities-state source-progress"><span class="loader"></span><h2>Checking reviewed advisories…</h2></section>`
    : error
      ? `<section class="document-section package-vulnerabilities-state empty-document"><span class="large-glyph">⌘</span><h2>Vulnerability query failed</h2><p>${escapeHtml(error)}</p></section>`
      : result
        ? renderResult(result, escapeHtml)
        : `<section class="document-section package-vulnerabilities-state source-progress"><span class="loader"></span><h2>Loading…</h2></section>`;

  return `<section class="package-vulnerabilities-surface" aria-labelledby="package-vulnerabilities-title">
    <header class="api-surface-head package-vulnerabilities-head">
      <h1 id="package-vulnerabilities-title">Vulnerabilities</h1>
      <p>${escapeHtml(status)}</p>
    </header>
    <div class="package-vulnerabilities-scroll">
      <section class="package-vulnerabilities-basis">
        <p>GitHub-reviewed NuGet advisories affecting the exact package version.</p>
      </section>
      ${content}
    </div>
    <footer class="api-surface-footer package-vulnerabilities-footer">
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
      <span>GitHub reviewed advisories</span>
    </footer>
  </section>`;
}
