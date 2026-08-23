import assert from "node:assert/strict";
import test from "node:test";

import {
  createDocumentInspectionCoordinator,
  docViewerOptions,
  type DocumentInspectionDependencies,
  type DocumentInspectionState,
  type DocumentViewerState,
} from "../src/document-inspection.ts";
import type {
  BrowserPackageDocument,
  BrowserPackageDocumentContent,
} from "../src/inspect-web-engine.d.ts";

const document: BrowserPackageDocument = {
  kind: "Markdown",
  name: "README.md",
  path: "docs/README.md",
  size: 128,
};

const guideDocument: BrowserPackageDocument = {
  ...document,
  name: "GUIDE.md",
  path: "docs/GUIDE.md",
};

function content(text: string): BrowserPackageDocumentContent {
  return {
    kind: "Markdown",
    name: document.name,
    path: document.path,
    text,
  };
}

function inspectionState(
  overrides: Partial<DocumentInspectionState> = {},
): DocumentInspectionState {
  return {
    docViewer: { status: "closed" },
    ...overrides,
  };
}

function loadingViewer(
  state: DocumentInspectionState,
  expectedDocument: BrowserPackageDocument,
): Extract<DocumentViewerState, { status: "loading" }> {
  const viewer = state.docViewer;
  assert.equal(viewer.status, "loading");
  if (viewer.status !== "loading") assert.fail("Expected a loading document");
  assert.equal(viewer.request.document, expectedDocument);
  return viewer;
}

function readyViewer(
  state: DocumentInspectionState,
  expectedDocument: BrowserPackageDocument,
  expectedHtml?: string,
): Extract<DocumentViewerState, { status: "ready" }> {
  const viewer = state.docViewer;
  assert.equal(viewer.status, "ready");
  if (viewer.status !== "ready") assert.fail("Expected a ready document");
  assert.equal(viewer.request.document, expectedDocument);
  if (expectedHtml !== undefined) assert.equal(viewer.html, expectedHtml);
  return viewer;
}

function failedViewer(
  state: DocumentInspectionState,
  expectedError: string,
): Extract<DocumentViewerState, { status: "failed" }> {
  const viewer = state.docViewer;
  assert.equal(viewer.status, "failed");
  if (viewer.status !== "failed") assert.fail("Expected a failed document");
  assert.equal(viewer.error, expectedError);
  return viewer;
}

function assertViewerClosed(state: DocumentInspectionState) {
  assert.equal(state.docViewer.status, "closed");
}

function inspectionDependencies(
  state: DocumentInspectionState,
  overrides: Partial<Omit<DocumentInspectionDependencies, "state">> = {},
): DocumentInspectionDependencies {
  return {
    state,
    queryDocument: async () => content("# Read me"),
    renderMarkdown: async text => `<p>${text}</p>`,
    renderMarkdownInline: async text => `<span>${text}</span>`,
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

test("document requests publish sanitized body HTML for exact coordinates", async () => {
  const events: string[] = [];
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request => {
        assert.deepEqual(
          [request.packageId, request.version, request.document],
          ["Example.Package", "1.2.3", document]);
        events.push("query");
        return content("# Read me");
      },
      renderMarkdown: async text => {
        assert.equal(text, "# Read me");
        events.push("markdown");
        return "<h1>Read me</h1>";
      },
      renderMarkdownInline: async () => {
        throw new Error("unexpected inline render");
      },
      render: () => events.push("render"),
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });

  assert.equal(readyViewer(state, document, "<h1>Read me</h1>").meta, null);
  assert.deepEqual(events, ["render", "query", "markdown", "render"]);
});

test("document frontmatter projects folded descriptions and version", async () => {
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () => content(
        "---\n"
        + "name: Package guide\n"
        + "version: 2.0\n"
        + "description: >-\n"
        + "  First line\n"
        + "  second line\n"
        + "---\n"
        + "# Body"),
      renderMarkdown: async text => {
        assert.equal(text, "# Body");
        return "<h1>Body</h1>";
      },
      renderMarkdownInline: async text => {
        assert.equal(text, "First line second line");
        return "<p>First line second line</p>";
      },
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });

  const viewer = readyViewer(state, document, "<h1>Body</h1>");
  assert.deepEqual(viewer.meta, {
    name: "Package guide",
    version: "2.0",
    descriptionHtml: "<p>First line second line</p>",
  });
});

test("frontmatter descriptions fall back to the document name", async () => {
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () => content(
        "---\n"
        + "version: 2.0\n"
        + "description: |-\n"
        + "  First line\n"
        + "  second line\n"
        + "---\n"
        + "Body"),
      renderMarkdownInline: async text => {
        assert.equal(text, "First line\nsecond line");
        return "<p>Description</p>";
      },
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });

  assert.deepEqual(readyViewer(state, document).meta, {
    name: "README.md",
    version: "2.0",
    descriptionHtml: "<p>Description</p>",
  });
});

test("frontmatter with only a version does not create a metadata card", async () => {
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () => content("---\nversion: 2.0\n---\nBody"),
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });

  assert.equal(readyViewer(state, document).meta, null);
});

test("closing during acquisition suppresses stale document publication", async () => {
  const query = deferred<BrowserPackageDocumentContent>();
  let markdownRenders = 0;
  let renders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () => query.promise,
      renderMarkdown: async () => {
        markdownRenders++;
        return "<p>stale</p>";
      },
      render: () => renders++,
    }));

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  coordinator.close();
  query.resolve(content("stale"));
  await open;

  assert.equal(markdownRenders, 0);
  assert.equal(renders, 2);
  assertViewerClosed(state);
});

test("a newer document remains published after an older request completes", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document
          ? first.promise
          : content("current"),
      renderMarkdown: async text => `<p>${text}</p>`,
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });
  first.resolve(content("stale"));
  await firstOpen;

  readyViewer(state, guideDocument, "<p>current</p>");
});

test("replacement during acquisition does not enter stale rendering", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const second = deferred<BrowserPackageDocumentContent>();
  let staleBodyRenders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document ? first.promise : second.promise,
      renderMarkdown: async text => {
        if (text === "stale") staleBodyRenders++;
        return `<p>${text}</p>`;
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });
  first.resolve(content("stale"));
  await firstOpen;

  assert.equal(staleBodyRenders, 0);
  loadingViewer(state, guideDocument);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, guideDocument, "<p>current</p>");
});

test("reopening the same document during acquisition rejects stale identity", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const second = deferred<BrowserPackageDocumentContent>();
  let queries = 0;
  let staleBodyRenders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: () => ++queries === 1 ? first.promise : second.promise,
      renderMarkdown: async text => {
        if (text === "stale") staleBodyRenders++;
        return `<p>${text}</p>`;
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  coordinator.close();
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  first.resolve(content("stale"));
  await firstOpen;

  assert.equal(staleBodyRenders, 0);
  loadingViewer(state, document);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, document, "<p>current</p>");
});

test("replacement during body rendering suppresses stale description work", async () => {
  const body = deferred<string>();
  const bodyEntered = deferred<void>();
  const second = deferred<BrowserPackageDocumentContent>();
  let inlineRenders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document
          ? content("---\ndescription: Summary\n---\nBody")
          : second.promise,
      renderMarkdown: async text => {
        if (text === "Body") {
          bodyEntered.resolve();
          return body.promise;
        }
        return `<p>${text}</p>`;
      },
      renderMarkdownInline: async () => {
        inlineRenders++;
        return "<p>stale description</p>";
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await bodyEntered.promise;
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });
  body.resolve("<p>stale body</p>");
  await firstOpen;

  assert.equal(inlineRenders, 0);
  loadingViewer(state, guideDocument);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, guideDocument, "<p>current</p>");
});

test("reopening the same document during body rendering suppresses stale work", async () => {
  const body = deferred<string>();
  const bodyEntered = deferred<void>();
  const second = deferred<BrowserPackageDocumentContent>();
  let queries = 0;
  let inlineRenders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: () => ++queries === 1
        ? Promise.resolve(content("---\ndescription: Summary\n---\nBody"))
        : second.promise,
      renderMarkdown: async text => {
        if (text === "Body") {
          bodyEntered.resolve();
          return body.promise;
        }
        return `<p>${text}</p>`;
      },
      renderMarkdownInline: async () => {
        inlineRenders++;
        return "<p>stale description</p>";
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await bodyEntered.promise;
  coordinator.close();
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  body.resolve("<p>stale body</p>");
  await firstOpen;

  assert.equal(inlineRenders, 0);
  loadingViewer(state, document);

  second.resolve(content("current"));
  await secondOpen;
});

test("replacement during description rendering suppresses stale publication", async () => {
  const description = deferred<string>();
  const descriptionEntered = deferred<void>();
  const second = deferred<BrowserPackageDocumentContent>();
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document
          ? content("---\ndescription: Summary\n---\nBody")
          : second.promise,
      renderMarkdown: async text => `<p>${text}</p>`,
      renderMarkdownInline: async () => {
        descriptionEntered.resolve();
        return description.promise;
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await descriptionEntered.promise;
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });
  description.resolve("<p>stale description</p>");
  await firstOpen;

  loadingViewer(state, guideDocument);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, guideDocument, "<p>current</p>");
});

test("reopening the same document during description rendering suppresses stale publication", async () => {
  const description = deferred<string>();
  const descriptionEntered = deferred<void>();
  const second = deferred<BrowserPackageDocumentContent>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: () => ++queries === 1
        ? Promise.resolve(content("---\ndescription: Summary\n---\nBody"))
        : second.promise,
      renderMarkdown: async text => `<p>${text}</p>`,
      renderMarkdownInline: async () => {
        descriptionEntered.resolve();
        return description.promise;
      },
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await descriptionEntered.promise;
  coordinator.close();
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  description.resolve("<p>stale description</p>");
  await firstOpen;

  loadingViewer(state, document);

  second.resolve(content("current"));
  await secondOpen;
});

test("rejected replaced documents cannot settle the current request", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const second = deferred<BrowserPackageDocumentContent>();
  let renders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document ? first.promise : second.promise,
      renderMarkdown: async text => `<p>${text}</p>`,
      render: () => renders++,
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });
  first.reject(new Error("stale failure"));
  await firstOpen;

  loadingViewer(state, guideDocument);
  assert.equal(renders, 2);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, guideDocument, "<p>current</p>");
  assert.equal(renders, 3);
});

test("reopening the same document suppresses a stale rejection", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const second = deferred<BrowserPackageDocumentContent>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: () => ++queries === 1 ? first.promise : second.promise,
      renderMarkdown: async text => `<p>${text}</p>`,
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  coordinator.close();
  const secondOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  first.reject(new Error("stale failure"));
  await firstOpen;

  loadingViewer(state, document);

  second.resolve(content("current"));
  await secondOpen;
  readyViewer(state, document, "<p>current</p>");
});

test("closing during body rendering suppresses description and publication", async () => {
  const body = deferred<string>();
  const bodyEntered = deferred<void>();
  let inlineRenders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () =>
        content("---\ndescription: Summary\n---\nBody"),
      renderMarkdown: async () => {
        bodyEntered.resolve();
        return body.promise;
      },
      renderMarkdownInline: async () => {
        inlineRenders++;
        return "<p>stale</p>";
      },
    }));

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await bodyEntered.promise;
  coordinator.close();
  body.resolve("<p>stale body</p>");
  await open;

  assert.equal(inlineRenders, 0);
  assertViewerClosed(state);
});

test("closing during description rendering suppresses all publication", async () => {
  const description = deferred<string>();
  const descriptionEntered = deferred<void>();
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () =>
        content("---\ndescription: Summary\n---\nBody"),
      renderMarkdown: async () => "<p>stale body</p>",
      renderMarkdownInline: async () => {
        descriptionEntered.resolve();
        return description.promise;
      },
    }));

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await descriptionEntered.promise;
  coordinator.close();
  description.resolve("<p>stale description</p>");
  await open;

  assertViewerClosed(state);
});

test("closing before a rejected request suppresses its stale failure", async () => {
  const query = deferred<BrowserPackageDocumentContent>();
  const queryEntered = deferred<void>();
  let renders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async () => {
        queryEntered.resolve();
        return query.promise;
      },
      render: () => renders++,
    }));

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await queryEntered.promise;
  coordinator.close();
  query.reject(new Error("stale failure"));
  await open;

  assertViewerClosed(state);
  assert.equal(renders, 2);
});

test("current document failures remain visible and settle loading", async () => {
  let renders = 0;
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      renderMarkdown: async () => {
        throw new Error("Markdown renderer unavailable");
      },
      render: () => renders++,
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });

  failedViewer(state, "Markdown renderer unavailable");
  assert.equal(renders, 2);
});

test("opening another document clears a prior visible failure", async () => {
  const replacement = deferred<BrowserPackageDocumentContent>();
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request => {
        if (request.document === document) {
          throw new Error("Document unavailable");
        }
        return replacement.promise;
      },
      renderMarkdown: async text => `<p>${text}</p>`,
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  failedViewer(state, "Document unavailable");

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });

  loadingViewer(state, guideDocument);

  replacement.resolve(content("current"));
  await open;
  readyViewer(state, guideDocument, "<p>current</p>");
});

test("opening another document clears prior published surfaces immediately", async () => {
  const replacement = deferred<BrowserPackageDocumentContent>();
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document
          ? content("---\nname: Old\n---\nold body")
          : replacement.promise,
      renderMarkdown: async text => `<p>${text}</p>`,
    }));

  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  assert.notEqual(readyViewer(state, document, "<p>old body</p>").meta, null);

  const open = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: guideDocument,
  });

  loadingViewer(state, guideDocument);

  replacement.resolve(content("current"));
  await open;
  readyViewer(state, guideDocument, "<p>current</p>");
});

test("closing replaces every document surface with a closed state", () => {
  let renders = 0;
  const state = inspectionState({
    docViewer: {
      status: "ready",
      request: {
        packageId: "Example.Package",
        version: "1.2.3",
        document,
      },
      html: "<p>old</p>",
      meta: {
        name: "Old",
        version: "1.0",
        descriptionHtml: "<p>old</p>",
      },
    },
  });
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  coordinator.close();

  assertViewerClosed(state);
  assert.equal(renders, 1);
});

// The union decides which fields *exist*; this projection decides which ones the renderer
// is actually told about, and the two are not the same property. Adversarial review made
// the point concretely by rewriting the old inline projection to pass `error: ""`: every
// one of the 497 tests still passed, and a document that had failed to load rendered as an
// empty `<article>` -- a failure wearing the shape of a successful, empty document.
//
// These cover the mapping itself, so dropping any field's route to the renderer is red.
const projectionDocument: BrowserPackageDocument = {
  kind: "doc",
  name: "README.md",
  path: "docs/README.md",
  size: 10,
};

const projectionRequest = {
  packageId: "Example.Package",
  version: "1.2.3",
  document: projectionDocument,
} as const;

test("a loading document projects as loading, with nothing else claimed", () => {
  assert.deepEqual(
    docViewerOptions({ status: "loading", request: projectionRequest }),
    { doc: projectionDocument, body: { status: "loading" } });
});

test("a ready document projects its html and metadata", () => {
  const meta = { name: "Example", version: "1.2.3", descriptionHtml: "<p>d</p>" };
  assert.deepEqual(
    docViewerOptions({
      status: "ready",
      request: projectionRequest,
      html: "<p>body</p>",
      meta,
    }),
    {
      doc: projectionDocument,
      body: { status: "ready", meta, html: "<p>body</p>" },
    });
});

test("a failed document projects its error and claims no content", () => {
  const options = docViewerOptions({
    status: "failed",
    request: projectionRequest,
    error: "the document could not be read",
  });
  // The projection carries the status through rather than flattening it, so "failed with
  // no content" is one value instead of a combination of four fields that could disagree.
  assert.deepEqual(options.body, {
    status: "failed",
    error: "the document could not be read",
  });
});
