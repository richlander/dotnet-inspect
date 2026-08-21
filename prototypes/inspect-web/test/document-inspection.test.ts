import assert from "node:assert/strict";
import test from "node:test";

import {
  createDocumentInspectionCoordinator,
  type DocumentInspectionDependencies,
  type DocumentInspectionState,
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
    docViewerOpen: false,
    docViewer: null,
    docViewerLoading: false,
    docViewerError: "",
    docViewerHtml: "",
    docViewerMeta: null,
    docViewerSeq: 0,
    ...overrides,
  };
}

function inspectionDependencies(
  state: DocumentInspectionState,
  overrides: Partial<Omit<DocumentInspectionDependencies, "state">> = {},
): DocumentInspectionDependencies {
  return {
    state,
    queryDocument: async () => content("# Read me"),
    renderMarkdown: async text => `<p>${String(text)}</p>`,
    renderMarkdownInline: async text => `<span>${String(text)}</span>`,
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

  assert.equal(state.docViewerOpen, true);
  assert.equal(state.docViewer, document);
  assert.equal(state.docViewerHtml, "<h1>Read me</h1>");
  assert.equal(state.docViewerMeta, null);
  assert.equal(state.docViewerLoading, false);
  assert.equal(state.docViewerError, "");
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

  assert.deepEqual(state.docViewerMeta, {
    name: "Package guide",
    version: "2.0",
    descriptionHtml: "<p>First line second line</p>",
  });
  assert.equal(state.docViewerHtml, "<h1>Body</h1>");
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

  assert.deepEqual(state.docViewerMeta, {
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

  assert.equal(state.docViewerMeta, null);
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
  assert.equal(state.docViewerOpen, false);
  assert.equal(state.docViewer, null);
  assert.equal(state.docViewerHtml, "");
});

test("a newer document remains published after an older request completes", async () => {
  const first = deferred<BrowserPackageDocumentContent>();
  const secondDocument: BrowserPackageDocument = {
    ...document,
    name: "GUIDE.md",
    path: "docs/GUIDE.md",
  };
  const state = inspectionState();
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocument: async request =>
        request.document === document
          ? first.promise
          : content("current"),
      renderMarkdown: async text => `<p>${String(text)}</p>`,
    }));

  const firstOpen = coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document,
  });
  await coordinator.open({
    packageId: "Example.Package",
    version: "1.2.3",
    document: secondDocument,
  });
  first.resolve(content("stale"));
  await firstOpen;

  assert.equal(state.docViewer, secondDocument);
  assert.equal(state.docViewerHtml, "<p>current</p>");
  assert.equal(state.docViewerLoading, false);
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
  assert.equal(state.docViewerHtml, "");
  assert.equal(state.docViewerMeta, null);
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

  assert.equal(state.docViewerHtml, "");
  assert.equal(state.docViewerMeta, null);
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

  assert.equal(state.docViewerError, "");
  assert.equal(state.docViewerLoading, false);
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

  assert.equal(state.docViewerError, "Markdown renderer unavailable");
  assert.equal(state.docViewerLoading, false);
  assert.equal(renders, 2);
});

test("closing resets every document surface and invalidates its sequence", () => {
  let renders = 0;
  const state = inspectionState({
    docViewerOpen: true,
    docViewer: document,
    docViewerLoading: true,
    docViewerError: "old error",
    docViewerHtml: "<p>old</p>",
    docViewerMeta: {
      name: "Old",
      version: "1.0",
      descriptionHtml: "<p>old</p>",
    },
    docViewerSeq: 4,
  });
  const coordinator = createDocumentInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  coordinator.close();

  assert.equal(state.docViewerSeq, 5);
  assert.equal(state.docViewerOpen, false);
  assert.equal(state.docViewer, null);
  assert.equal(state.docViewerLoading, false);
  assert.equal(state.docViewerError, "");
  assert.equal(state.docViewerHtml, "");
  assert.equal(state.docViewerMeta, null);
  assert.equal(renders, 1);
});
