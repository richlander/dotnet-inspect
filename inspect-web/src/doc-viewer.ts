import { assertNever } from "./data.ts";
import type { OpenDocumentViewerState } from "./document-inspection.ts";
import type { InspectedPackageDocument } from "./package-acquisition.ts";

type PackageDocumentSummary = Pick<
  InspectedPackageDocument,
  "kind" | "name" | "path" | "size"
>;

export interface RenderDocViewerOptions {
  state: OpenDocumentViewerState;
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

export function renderDocViewer(options: RenderDocViewerOptions): string {
  const { state, escapeHtml } = options;
  const { document } = state.request;
  let body: string;
  switch (state.status) {
    case "loading":
      body =
        `<div class="doc-viewer-status">Loading ${escapeHtml(document.name)}…</div>`;
      break;
    case "ready": {
      const metaCard = state.meta
        ? `<div class="doc-frontmatter">
        <div class="doc-fm-head"><strong>${escapeHtml(state.meta.name)}</strong>${state.meta.version ? `<span class="doc-fm-version">v${escapeHtml(state.meta.version)}</span>` : ""}</div>
        ${state.meta.descriptionHtml ? `<p class="doc-fm-desc">${state.meta.descriptionHtml}</p>` : ""}
      </div>`
        : "";
      body =
        `${metaCard}<article class="markdown-body">${state.html}</article>`;
      break;
    }
    case "failed":
      body = `<div class="doc-viewer-status error">${escapeHtml(
        state.error || "The document could not be loaded.",
      )}</div>`;
      break;
    default:
      return assertNever(state, "open document viewer state");
  }
  return `
    <div class="doc-viewer-backdrop" id="doc-viewer-backdrop">
      <div class="doc-viewer" role="dialog" aria-modal="true" aria-label="Package document">
        <div class="doc-viewer-head">
          <span class="doc-viewer-title">${escapeHtml(document.name)}<small>${escapeHtml(document.path)}</small></span>
          <button id="doc-viewer-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="doc-viewer-body">${body}</div>
      </div>
    </div>`;
}
