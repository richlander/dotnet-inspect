import { assertNever } from "./data.ts";
import type {
  BrowserPackageDocumentContent,
} from "./facades/inspect-web-package.d.ts";
import type { InspectedPackageDocument } from "./package-acquisition.ts";

export interface DocumentInspectionState {
  docViewer: DocumentViewerState;
}

export interface PackageDocumentRequest {
  packageId: string;
  version: string;
  document: InspectedPackageDocument;
}

export interface DocumentViewerMeta {
  name: string;
  version: string;
  descriptionHtml: string;
}

interface DocumentViewerTarget {
  readonly request: PackageDocumentRequest;
}

export type DocumentViewerState =
  | { readonly status: "closed" }
  | ({ readonly status: "loading" } & DocumentViewerTarget)
  | ({
      readonly status: "ready";
      readonly html: string;
      readonly meta: DocumentViewerMeta | null;
    } & DocumentViewerTarget)
  | ({
      readonly status: "failed";
      readonly error: string;
    } & DocumentViewerTarget);

export type OpenDocumentViewerState =
  Exclude<DocumentViewerState, { readonly status: "closed" }>;

export function documentViewerIsOpen(
  state: DocumentViewerState,
): state is OpenDocumentViewerState {
  switch (state.status) {
    case "closed":
      return false;
    case "loading":
    case "ready":
    case "failed":
      return true;
    default:
      return assertNever(state, "document viewer state");
  }
}

export function normalizeDocumentViewerSnapshot(
  state: DocumentViewerState,
): DocumentViewerState {
  switch (state.status) {
    case "loading":
      return {
        status: "failed",
        request: state.request,
        error: "",
      };
    case "closed":
    case "ready":
    case "failed":
      return state;
    default:
      return assertNever(state, "document viewer snapshot state");
  }
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
  const frontmatter = match[1];
  const matchedText = match[0];
  if (frontmatter === undefined || matchedText === undefined)
    return { meta: null, body: source };
  const meta: DocumentFrontmatter = {};
  const lines = frontmatter.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (line === undefined) continue;
    const kv = /^([A-Za-z0-9_-]+):\s?(.*)$/.exec(line);
    if (!kv) continue;
    const key = kv[1];
    const rawValue = kv[2];
    if (key === undefined || rawValue === undefined) continue;
    let value = rawValue;
    if (value === ">" || value === ">-" || value === "|" || value === "|-") {
      const folded = value.startsWith(">");
      const buffer = [];
      while (i + 1 < lines.length) {
        const continuation = lines[i + 1];
        if (continuation === undefined
          || (!/^\s+\S/.test(continuation) && continuation.trim() !== "")) {
          break;
        }
        i++;
        buffer.push(continuation.trim());
      }
      value = buffer.join(folded ? " " : "\n").trim();
    }
    meta[key] = value.trim();
  }
  return { meta, body: source.slice(matchedText.length) };
}

export function createDocumentInspectionCoordinator(
  dependencies: DocumentInspectionDependencies,
) {
  const { state } = dependencies;
  const clear = () => {
    state.docViewer = { status: "closed" };
  };

  return {
    async open(request: PackageDocumentRequest) {
      const pending = {
        status: "loading",
        request,
      } as const;
      state.docViewer = pending;
      dependencies.render();
      try {
        const content = await dependencies.queryDocument(request);
        if (state.docViewer !== pending) return;
        if (typeof content.text !== "string")
          throw new TypeError("The document content did not contain text.");
        const { meta, body } = splitFrontmatter(content.text);
        const html = await dependencies.renderMarkdown(body);
        if (state.docViewer !== pending) return;
        const descriptionHtml = meta?.description
          ? await dependencies.renderMarkdownInline(meta.description)
          : "";
        if (state.docViewer !== pending) return;
        const projectedMeta = meta && (meta.name || meta.description)
          ? {
              name: meta.name || request.document.name,
              version: meta.version || "",
              descriptionHtml,
            }
          : null;
        state.docViewer = {
          status: "ready",
          request,
          html,
          meta: projectedMeta,
        };
      } catch (error) {
        if (state.docViewer !== pending) return;
        state.docViewer = {
          status: "failed",
          request,
          error: dependencies.describeError(error),
        };
      }
      dependencies.render();
    },

    clear,

    close() {
      clear();
      dependencies.render();
    },
  };
}
