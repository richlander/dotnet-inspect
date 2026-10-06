import type { BrowserPackageDependencies } from "./facades/inspect-web-package.d.ts";
import { assemblyReferenceGraphLegendHtml } from "./graph-legends.ts";

export interface LibraryReferencesOptions {
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  loading: boolean;
  error: string;
  data: BrowserPackageDependencies | null;
  escapeHtml: (value: unknown) => string;
}

export function renderLibraryReferencesSurface(options: LibraryReferencesOptions): string {
  const {
    loading,
    error,
    data,
    escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (loading) {
    status = "Reading references\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Reading references&hellip;</h2><p>Reading direct AssemblyRef rows.</p></section>`;
  } else if (error) {
    status = "Query failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x2318;</span><h2>Reference query failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else if (!data) {
    status = "Loading\u2026";
    content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
  } else if (data.assemblyReferences === null
    || typeof data.assemblyReferences === "string") {
    const message = data.assemblyReferences === null
      ? "The engine returned no assembly-reference result."
      : data.assemblyReferences || "No failure details were provided.";
    status = "Inspection failed";
    content = `<section class="document-section empty-document"><h2>Reference inspection failed</h2><p>${escapeHtml(message)}</p></section>`;
  } else {
    const references = data.assemblyReferences.references;
    status = `${references.length.toLocaleString()} direct reference${references.length === 1 ? "" : "s"}`;
    content = references.length
      ? `<section class="document-section reference-graph-section">
          <div class="section-title"><h2>Reference graph</h2><span>assembly above \u00b7 direct references below</span></div>
          <div id="library-reference-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph&hellip;</p></div>
          ${assemblyReferenceGraphLegendHtml()}
        </section>
        <section class="document-section reference-list-section">
          <div class="section-title"><h2>Assembly references</h2><span>${references.length.toLocaleString()} direct reference${references.length === 1 ? "" : "s"}</span></div>
          <ul class="dep-list" aria-label="Assembly references">${references.map(reference =>
            `<li><span class="dep-name">${escapeHtml(reference.name)}</span><code class="dep-version">${escapeHtml(`${reference.version} \u00b7 ${reference.culture || "neutral"} \u00b7 ${reference.publicKeyToken ? `pkt ${reference.publicKeyToken}` : "unsigned"}`)}</code></li>`).join("")}</ul>
        </section>`
      : `<section class="document-section empty-document"><h2>No direct references</h2><p>This assembly declares no direct AssemblyRef rows.</p></section>`;
  }
  return `<section class="library-references-surface" aria-labelledby="library-references-title">
    <header class="api-surface-head">
      <h1 id="library-references-title">References</h1>
      <p>${escapeHtml(status)}</p>
    </header>
    <div class="library-references-scroll">${content}</div>
  </section>`;
}
