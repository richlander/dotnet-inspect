import type { BrowserPackageIntegrations } from "./facades/inspect-web-analysis.d.ts";

export interface LibraryIntegrationsOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  loading: boolean;
  error: string;
  data: BrowserPackageIntegrations | null;
  escapeHtml: (value: unknown) => string;
}

export function renderLibraryIntegrationsSurface(options: LibraryIntegrationsOptions): string {
  const {
    libraryName, assemblyIdentity, assetPath, coordinate,
    requireLibrary, pickerHtml, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25C8;</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to scan its public surface for DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting integration signals.</p></section>`;
  } else if (loading) {
    status = "Scanning integrations\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Scanning integrations&hellip;</h2><p>Reading the public surface of ${escapeHtml(libraryName)} for ecosystem signals.</p></section>`;
  } else if (error) {
    status = "Scan failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25C8;</span><h2>Integration scan failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else if (!data) {
    status = "Loading\u2026";
    content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
  } else {
    const categories = data.categories;
    const partial = !data.isComplete || Boolean(data.inspectionError);
    status = `${categories.length.toLocaleString()} categor${categories.length === 1 ? "y" : "ies"} \u00b7 ${data.totalSignals.toLocaleString()} signal${data.totalSignals === 1 ? "" : "s"}${partial ? " \u00b7 partial" : ""}`;
    const warning = partial
      ? `<section class="document-section metadata-warning"><strong>&#x26A0; This library could not be scanned completely</strong>${data.inspectionError ? `<ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul>` : ""}</section>`
      : "";
    const blocks = categories.map((category, index) => {
      const signals = [...category.signals].sort((a, b) => {
        const rank = (shape: string) => /type/i.test(shape) ? 0 : 1;
        return rank(a.shape) - rank(b.shape) || a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name);
      });
      const typeCount = signals.filter(signal => /type/i.test(signal.shape)).length;
      const apiCount = signals.length - typeCount;
      const rows = signals.map(signal => {
        const isType = /type/i.test(signal.shape);
        const { short, qualifier } = splitSignalName(signal.name);
        return `<div class="signal-row" role="listitem" title="${escapeHtml(signal.name)} &middot; ${escapeHtml(signal.shape)} &middot; ${escapeHtml(signal.kind)}">
          <span class="signal-badge signal-${isType ? "type" : "api"}">${isType ? "T" : "&#402;"}</span>
          <span class="signal-body"><span class="signal-name">${escapeHtml(short)}</span>${qualifier ? `<span class="signal-ns">${escapeHtml(qualifier)}</span>` : ""}</span>
          <span class="signal-kind">${escapeHtml(signal.kind)}</span>
        </div>`;
      }).join("");
      return `<section class="integration-category" aria-labelledby="integration-category-${index}">
        <div class="section-title"><h2 id="integration-category-${index}">${escapeHtml(category.integration)}</h2><span>${typeCount} type${typeCount === 1 ? "" : "s"} &middot; ${apiCount} API${apiCount === 1 ? "" : "s"}</span></div>
        <div class="signal-list" role="list">${rows}</div>
      </section>`;
    }).join("");
    const empty = partial
      ? `<section class="document-section empty-document"><h2>Integration scan incomplete</h2><p>No integration signals are available from this incomplete scan.</p></section>`
      : `<section class="document-section empty-document"><span class="large-glyph">&#x25C7;</span><h2>No ecosystem integrations detected</h2><p>The public surface of ${escapeHtml(libraryName)} shows no known DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting signals.</p></section>`;
    content = `${warning}${categories.length ? blocks : empty}`;
  }
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-integrations-surface${pickerHtml ? " library-integrations-with-controls" : ""}" aria-labelledby="library-integrations-title">
    <header class="api-surface-head">
      <h1 id="library-integrations-title">Integrations</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    ${pickerHtml ? `<section class="library-integrations-controls" aria-label="Integration scan library">${pickerHtml}</section>` : ""}
    <div class="library-integrations-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}

// Split before parameter/generic lists so their dots cannot become the name boundary.
function splitSignalName(fullName: string) {
  const paren = fullName.indexOf("(");
  const angle = fullName.indexOf("<");
  const bounds = [paren, angle].filter(i => i >= 0);
  const cut = bounds.length ? Math.min(...bounds) : -1;
  const head = cut < 0 ? fullName : fullName.slice(0, cut);
  const suffix = cut < 0 ? "" : fullName.slice(cut);
  const dot = head.lastIndexOf(".");
  return {
    short: (dot < 0 ? head : head.slice(dot + 1)) + suffix,
    qualifier: dot < 0 ? "" : head.slice(0, dot),
  };
}
