import type { DocViewerMeta } from "./doc-viewer.ts";
import type {
  BrowserPackageDocument,
  BrowserPackageDocumentContent,
} from "./inspect-web-engine.d.ts";

export interface DocumentInspectionState {
  docViewerOpen: boolean;
  docViewer: BrowserPackageDocument | null;
  docViewerLoading: boolean;
  docViewerError: string;
  docViewerHtml: string;
  docViewerMeta: DocViewerMeta | null;
  docViewerSeq: number;
}

export interface PackageDocumentRequest {
  packageId: string;
  version: string;
  document: BrowserPackageDocument;
}

export interface DocumentInspectionDependencies {
  state: DocumentInspectionState;
  queryDocument:
    (request: PackageDocumentRequest) => Promise<BrowserPackageDocumentContent>;
  renderMarkdown: (text: string) => Promise<string>;
  renderMarkdownInline: (text: string) => Promise<string>;
  describeError: (error: unknown) => string;
  render: () => void;
}

interface DocumentFrontmatter {
  name?: string;
  version?: string;
  description?: string;
  [key: string]: string | undefined;
}

// Skill files carry YAML frontmatter whose folded/literal descriptions need
// projecting separately before the remaining body is rendered as Markdown.
function splitFrontmatter(text: string) {
  const source = text;
  const match = /^\uFEFF?---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(source);
  if (!match) return { meta: null, body: source };
  const meta: DocumentFrontmatter = {};
  const lines = match[1].split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const kv = /^([A-Za-z0-9_-]+):\s?(.*)$/.exec(lines[i]);
    if (!kv) continue;
    let value = kv[2];
    if (value === ">" || value === ">-" || value === "|" || value === "|-") {
      const folded = value.startsWith(">");
      const buffer = [];
      while (i + 1 < lines.length
        && (/^\s+\S/.test(lines[i + 1]) || lines[i + 1].trim() === "")) {
        buffer.push(lines[++i].trim());
      }
      value = buffer.join(folded ? " " : "\n").trim();
    }
    meta[kv[1]] = value.trim();
  }
  return { meta, body: source.slice(match[0].length) };
}

export function createDocumentInspectionCoordinator(
  dependencies: DocumentInspectionDependencies,
) {
  const { state } = dependencies;

  return {
    async open(request: PackageDocumentRequest) {
      const sequence = ++state.docViewerSeq;
      state.docViewerOpen = true;
      state.docViewer = request.document;
      state.docViewerHtml = "";
      state.docViewerMeta = null;
      state.docViewerError = "";
      state.docViewerLoading = true;
      dependencies.render();
      try {
        const content = await dependencies.queryDocument(request);
        if (sequence !== state.docViewerSeq) return;
        if (typeof content.text !== "string")
          throw new TypeError("The document content did not contain text.");
        const { meta, body } = splitFrontmatter(content.text);
        const html = await dependencies.renderMarkdown(body);
        if (sequence !== state.docViewerSeq) return;
        const descriptionHtml = meta?.description
          ? await dependencies.renderMarkdownInline(meta.description)
          : "";
        if (sequence !== state.docViewerSeq) return;
        const projectedMeta = meta && (meta.name || meta.description)
          ? {
              name: meta.name || request.document.name,
              version: meta.version || "",
              descriptionHtml,
            }
          : null;
        state.docViewerHtml = html;
        state.docViewerMeta = projectedMeta;
      } catch (error) {
        if (sequence !== state.docViewerSeq) return;
        state.docViewerError = dependencies.describeError(error);
      } finally {
        if (sequence === state.docViewerSeq) {
          state.docViewerLoading = false;
          dependencies.render();
        }
      }
    },

    close() {
      state.docViewerSeq++;
      state.docViewerOpen = false;
      state.docViewer = null;
      state.docViewerHtml = "";
      state.docViewerMeta = null;
      state.docViewerError = "";
      state.docViewerLoading = false;
      dependencies.render();
    },
  };
}
