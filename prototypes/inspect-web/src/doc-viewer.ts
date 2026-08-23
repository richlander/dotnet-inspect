import { assertNever } from "./data.ts";
import type { BrowserPackageDocument } from "./inspect-web-engine.d.ts";

type DocViewerDocument = Pick<BrowserPackageDocument, "name" | "path">;
type PackageDocumentSummary = Pick<
  BrowserPackageDocument,
  "kind" | "name" | "path" | "size"
>;

export interface DocViewerMeta {
  name: string;
  version: string;
  descriptionHtml: string;
}

// The body is a union, not five independent fields. Flattening the viewer's state into
// `loading`/`error`/`html` made the renderer re-derive which state it was in by asking
// whether a string was empty -- so a *failed* document whose message was empty and a
// *ready* document whose body was empty became the same value, and the failure rendered
// as a successful empty article. Round 2 review (Claude Opus 5) demonstrated it end to
// end. Discriminating on status makes that pair unrepresentable rather than merely
// unlikely.
export type DocViewerBody =
  | { status: "loading" }
  | { status: "ready"; meta: DocViewerMeta | null; html: string }
  | { status: "failed"; error: string };

export interface RenderDocViewerOptions {
  doc: DocViewerDocument | null;
  body: DocViewerBody;
  escapeHtml: (value: unknown) => string;
}

export interface DocViewerBindingActions {
  onClose: () => void;
  onOpenDocument: (path: string) => void;
}

export function bindDocViewer(
  root: ParentNode,
  actions: DocViewerBindingActions,
) {
  const backdrop =
    root.querySelector<HTMLElement>("#doc-viewer-backdrop");
  backdrop?.addEventListener("mousedown", event => {
    if (event.target === backdrop) actions.onClose();
  });
  root.querySelector("#doc-viewer-close")?.addEventListener(
    "click",
    actions.onClose);
  root.querySelectorAll<HTMLElement>("[data-doc-path]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onOpenDocument(button.dataset.docPath ?? "")));
}

export function renderPackageDocuments(
  documents: readonly PackageDocumentSummary[],
  escapeHtml: (value: unknown) => string,
): string {
  if (!documents.length) return "";
  const kindLabels = new Map([
    ["readme", "Readme"],
    ["package", "Package"],
    ["skill", "Skill"],
  ]);
  const kindGlyphs = new Map([
    ["readme", "▤"],
    ["package", "▤"],
    ["skill", "◆"],
  ]);
  const chips = documents
    .map(document => `
      <button class="doc-chip doc-${escapeHtml(document.kind)}" data-doc-path="${escapeHtml(document.path)}" title="${escapeHtml(document.path)} · ${document.size.toLocaleString()} bytes">
        <span class="doc-glyph">${kindGlyphs.get(document.kind) ?? "▤"}</span>
        <span class="doc-name">${escapeHtml(document.name)}</span>
        <span class="doc-kind">${escapeHtml(kindLabels.get(document.kind) ?? document.kind)}</span>
      </button>`)
    .join("");
  return `<section class="document-section">
      <div class="section-title"><h2>Documentation</h2><span>${documents.length} file${documents.length === 1 ? "" : "s"} — click to read</span></div>
      <div class="doc-chip-list">${chips}</div>
    </section>`;
}

function docViewerMetaCard(
  meta: DocViewerMeta | null,
  escapeHtml: (value: unknown) => string,
): string {
  if (!meta) return "";
  return `<div class="doc-frontmatter">
        <div class="doc-fm-head"><strong>${escapeHtml(meta.name)}</strong>${meta.version ? `<span class="doc-fm-version">v${escapeHtml(meta.version)}</span>` : ""}</div>
        ${meta.descriptionHtml ? `<p class="doc-fm-desc">${meta.descriptionHtml}</p>` : ""}
      </div>`;
}

export function renderDocViewer(options: RenderDocViewerOptions): string {
  const { doc, body: viewerBody, escapeHtml } = options;
  const title = doc ? doc.name : "Document";
  const subtitle = doc ? doc.path : "";
  let body: string;
  switch (viewerBody.status) {
    case "loading":
      body = `<div class="doc-viewer-status">Loading ${escapeHtml(title)}…</div>`;
      break;
    case "failed":
      // A failure with nothing to say is still a failure. An empty message must not be
      // able to render as an empty success, which is what re-deriving state from
      // truthiness allowed.
      body = `<div class="doc-viewer-status error">${
        escapeHtml(viewerBody.error || "The document could not be loaded.")}</div>`;
      break;
    case "ready":
      body = docViewerMetaCard(viewerBody.meta, escapeHtml)
        + `<article class="markdown-body">${viewerBody.html}</article>`;
      break;
    default:
      return assertNever(viewerBody, "DocViewerBody");
  }
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
