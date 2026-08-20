export interface DocViewerDocument {
  name: string;
  path: string;
}

export interface DocViewerMeta {
  name: string;
  version: string;
  descriptionHtml: string;
}

export interface RenderDocViewerOptions {
  doc: DocViewerDocument | null;
  meta: DocViewerMeta | null;
  loading: boolean;
  error: string;
  html: string;
  escapeHtml: (value: unknown) => string;
}

export function renderDocViewer(options: RenderDocViewerOptions): string {
  const { doc, meta, loading, error, html, escapeHtml } = options;
  const title = doc ? `${doc.name}` : "Document";
  const subtitle = doc ? doc.path : "";
  const metaCard = meta
    ? `<div class="doc-frontmatter">
        <div class="doc-fm-head"><strong>${escapeHtml(meta.name)}</strong>${meta.version ? `<span class="doc-fm-version">v${escapeHtml(meta.version)}</span>` : ""}</div>
        ${meta.descriptionHtml ? `<p class="doc-fm-desc">${meta.descriptionHtml}</p>` : ""}
      </div>`
    : "";
  const body = loading
    ? `<div class="doc-viewer-status">Loading ${escapeHtml(title)}…</div>`
    : error
      ? `<div class="doc-viewer-status error">${escapeHtml(error)}</div>`
      : `${metaCard}<article class="markdown-body">${html}</article>`;
  return `
    <div class="doc-viewer-backdrop" id="doc-viewer-backdrop">
      <div class="doc-viewer" role="dialog" aria-modal="true" aria-label="Package document">
        <div class="doc-viewer-head">
          <span class="doc-viewer-title">${escapeHtml(title)}<small>${escapeHtml(subtitle)}</small></span>
          <button id="doc-viewer-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="doc-viewer-body">${body}</div>
      </div>
    </div>`;
}
