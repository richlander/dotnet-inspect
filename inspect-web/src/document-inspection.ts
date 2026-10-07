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

// A nuspec is XML; render it through the Markdown path as a fenced xml block.
function documentMarkdown(request: PackageDocumentRequest, text: string) {
  if (request.document.kind !== "metadata") return splitFrontmatter(text);
  let longestRun = 0;
  for (const [run] of text.matchAll(/`+/g))
    if (run.length > longestRun) longestRun = run.length;
  const fence = "`".repeat(Math.max(3, longestRun + 1));
  return { meta: null, body: `${fence}xml\n${text}\n${fence}` };
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
        const { meta, body } = documentMarkdown(request, content.text);
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


export type PackageDocumentTitle =
  | { readonly status: "loading" }
  | { readonly status: "ready"; readonly title: string | null }
  | { readonly status: "failed"; readonly error: string };

export function createPackageDocumentTitles(dependencies: {
  queryDocument: DocumentInspectionDependencies["queryDocument"];
  renderMarkdown: DocumentInspectionDependencies["renderMarkdown"];
  firstHeading: (html: string) => string | null;
  describeError: DocumentInspectionDependencies["describeError"];
  render: () => void;
}) {
  const packages = new WeakMap<object, Map<string, PackageDocumentTitle>>();
  const get = (owner: object, path: string) => packages.get(owner)?.get(path);
  return {
    get,
    async load(owner: object, requests: readonly PackageDocumentRequest[]) {
      let titles = packages.get(owner);
      if (!titles) {
        titles = new Map();
        packages.set(owner, titles);
      }
      const entries = titles;
      await Promise.all(requests.map(async request => {
        const path = request.document.path;
        if (entries.has(path)) return;
        entries.set(path, { status: "loading" });
        try {
          const content = await dependencies.queryDocument(request);
          if (typeof content.text !== "string")
            throw new TypeError("The document content did not contain text.");
          const html = await dependencies.renderMarkdown(documentMarkdown(request, content.text).body);
          entries.set(path, { status: "ready", title: dependencies.firstHeading(html) });
        } catch (error) {
          entries.set(path, { status: "failed", error: dependencies.describeError(error) });
        }
        dependencies.render();
      }));
    },
  };
}
