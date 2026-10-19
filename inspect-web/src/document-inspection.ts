import { assertNever } from "./data.ts";
import type {
  BrowserPackageDocumentContent,
} from "./facades/inspect-web-package.d.ts";
import type { ContainedDocumentFragment } from "./contained-document.ts";
import type { InspectedPackageDocument } from "./package-acquisition.ts";

export interface DocumentInspectionState {
  docViewer: DocumentViewerState;
}

export interface PackageDocumentRequest {
  packageId: string;
  version: string;
  document: InspectedPackageDocument;
}

interface DocumentViewerTarget {
  readonly request: PackageDocumentRequest;
}

export type DocumentViewerState =
  | { readonly status: "closed" }
  | ({ readonly status: "loading" } & DocumentViewerTarget)
  | ({
      readonly status: "ready";
      readonly fragment: ContainedDocumentFragment;
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
  renderDocument: (
    input: PackageDocumentRenderInput,
    generation: string,
    currentGeneration: () => string | null,
  ) => Promise<ContainedDocumentFragment>;
  describeError: (error: unknown) => string;
  render: () => void;
}

export interface PackageDocumentFrontmatter {
  readonly name: string;
  readonly version: string | null;
  readonly description: string | null;
}

export interface PackageDocumentRenderInput {
  readonly request: PackageDocumentRequest;
  readonly body: string;
  readonly frontmatter: PackageDocumentFrontmatter | null;
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
  let nextGeneration = 0;
  const clear = () => {
    state.docViewer = { status: "closed" };
  };

  return {
    async open(request: PackageDocumentRequest) {
      const generation = `package-document-${++nextGeneration}`;
      const pending = {
        status: "loading",
        request,
        generation,
      } as const;
      state.docViewer = pending;
      dependencies.render();
      try {
        const content = await dependencies.queryDocument(request);
        if (state.docViewer !== pending) return;
        if (typeof content.text !== "string")
          throw new TypeError("The document content did not contain text.");
        const { meta, body } = documentMarkdown(request, content.text);
        const frontmatter = meta && (meta.name || meta.description)
          ? {
            name: meta.name || request.document.name,
            version: meta.version || null,
            description: meta.description || null,
          }
          : null;
        const fragment = await dependencies.renderDocument(
          { request, body, frontmatter },
          generation,
          () => state.docViewer === pending ? generation : null,
        );
        if (state.docViewer !== pending) return;
        state.docViewer = {
          status: "ready",
          request,
          fragment,
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
  renderDocument: DocumentInspectionDependencies["renderDocument"];
  firstHeading: (fragment: ContainedDocumentFragment) => string | null;
  describeError: DocumentInspectionDependencies["describeError"];
  render: () => void;
}) {
  const packages = new WeakMap<object, Map<string, PackageDocumentTitle>>();
  let nextGeneration = 0;
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
          const generation = `package-document-title-${++nextGeneration}`;
          const fragment = await dependencies.renderDocument(
            {
              request,
              body: documentMarkdown(request, content.text).body,
              frontmatter: null,
            },
            generation,
            () => generation,
          );
          entries.set(path, {
            status: "ready",
            title: dependencies.firstHeading(fragment),
          });
        } catch (error) {
          entries.set(path, { status: "failed", error: dependencies.describeError(error) });
        }
        dependencies.render();
      }));
    },
  };
}
