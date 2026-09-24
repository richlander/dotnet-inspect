import assert from "node:assert/strict";
import test from "node:test";

import { defaultTypeExplorerIntent } from "../src/type-explorer-route.ts";
import {
  bindTypeExplorerView,
  captureTypeExplorerViewportAnchor,
  canceledTypeExplorerView,
  renderTypeExplorerView,
  restoreTypeExplorerMemberSelection,
  restoreTypeExplorerViewportAnchor,
  type TypeExplorerInspection,
  type TypeExplorerViewActions,
} from "../src/type-explorer-view.ts";
import { fakeDom } from "./fake-dom.ts";

const identity = {
  stableSelector: "M:ConvertName(System.String)",
  canonicalSignature:
    "System.String System.Text.Json.JsonNamingPolicy::ConvertName(System.String)",
  fingerprint: "0123456789",
  typeFullName: "System.Text.Json.JsonNamingPolicy",
  memberName: "ConvertName",
};

const inspection: TypeExplorerInspection = {
  outcome: "Available",
  reason: null,
  bodyProjectionsAttempted: 1,
  failedBodyIds: [],
  document: {
    assemblyName: "System.Text.Json.dll",
    pdbSupplied: false,
    symbolSource: "decompiled",
    renderingPolicy: "whole-type",
    documentationCapability: "Available",
    contractRelationshipCapability: "Available",
    projectionFailure: null,
    projection: {
      revision: "a".repeat(64),
      text: "public abstract string ConvertName(string name);",
      diagnostics: [],
      declarations: [{
        declarationId: 7,
        identity,
        declarationToken: 100663297,
        kind: "Method",
        accessibility: "Public",
        placement: "Instance",
        origin: "NonGenerated",
        supportsSelectedBody: false,
        range: {
          start: 0,
          length: 48,
        },
        bodies: [],
      }],
    },
  },
  diagnostics: [],
};

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

test("Type Explorer renders owner-issued declarations and selection", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedDeclarationId: 7,
      documentRevision: "a".repeat(64),
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer: JsonNamingPolicy/u);
  assert.match(html, /data-type-explorer-declaration="7"/u);
  assert.match(html, /aria-current="true"/u);
  assert.match(html, /type-explorer-source-declaration selected/u);
  assert.match(
    html,
    /type-explorer-source-declaration selected" role="button" tabindex="0" aria-current="true"/u);
  assert.match(html, /ConvertName/u);
  assert.match(html, /id="type-explorer-outline-toggle"/u);
  assert.match(html, /aria-expanded="false"/u);
});

test("Type Explorer visibly disambiguates colliding stable selectors", () => {
  const first = "public void Overload(Parameter930885 value);";
  const second = "public void Overload(Parameter1349381 value);";
  const text = `${first}\n${second}`;
  const stableSelector = "Overload~2e56935e85";
  const overloadedInspection: TypeExplorerInspection = {
    ...inspection,
    document: {
      ...inspection.document!,
      projection: {
        ...inspection.document!.projection!,
        text,
        declarations: [
          {
            ...inspection.document!.projection!.declarations[0]!,
            identity: {
              ...identity,
              stableSelector,
              canonicalSignature:
                "M:Samples.Collision.Overload(Samples.Parameter930885)",
              fingerprint: "2e56935e85",
              typeFullName: "Samples.Collision",
              memberName: "Overload",
            },
            range: { start: 0, length: first.length },
          },
          {
            ...inspection.document!.projection!.declarations[0]!,
            declarationId: 8,
            identity: {
              ...identity,
              stableSelector,
              canonicalSignature:
                "M:Samples.Collision.Overload(Samples.Parameter1349381)",
              fingerprint: "2e56935e85",
              typeFullName: "Samples.Collision",
              memberName: "Overload",
            },
            range: { start: first.length + 1, length: second.length },
          },
        ],
      },
    },
  };
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedDeclarationId: 8,
      documentRevision: "a".repeat(64),
    },
    state: {
      status: "ready",
      inspection: overloadedInspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(
    html,
    /<span class="type-explorer-outline-signature">M:Samples\.Collision\.Overload\(Samples\.Parameter930885\)<\/span>/u);
  assert.match(
    html,
    /<span class="type-explorer-outline-signature">M:Samples\.Collision\.Overload\(Samples\.Parameter1349381\)<\/span>/u);
  assert.doesNotMatch(html, /Overload~2e56935e85/u);
  assert.match(
    html,
    /data-type-explorer-declaration="8"[^>]*aria-current="true"/u);
});

test("Type Explorer disables Selected body without owner-issued support", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedDeclarationId: 7,
      documentRevision: "a".repeat(64),
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(
    html,
    /value="SelectedBody" disabled/u);
});

  test("Type Explorer reveals Inspect only for the selected exact body", () => {
    const text = "public string ConvertName(string name) { return name; }";
    const bodyStart = text.indexOf("{");
    const bodyInspection: TypeExplorerInspection = {
      ...inspection,
      document: {
        ...inspection.document!,
        projection: {
          ...inspection.document!.projection!,
          text,
          declarations: [{
            ...inspection.document!.projection!.declarations[0]!,
            supportsSelectedBody: true,
            range: { start: 0, length: text.length },
            bodies: [{
              bodyId: 11,
              role: "Method",
              range: {
                start: bodyStart,
                length: text.length - bodyStart,
              },
              destination: {
                moduleVersionId: "11111111-1111-1111-1111-111111111111",
                member: identity,
                metadataToken: 0x06000001,
              },
            }],
          }],
        },
      },
    };
    const selected = renderTypeExplorerView({
      typeDisplay: "JsonNamingPolicy",
      packageDisplay: "System.Text.Json",
      intent: {
        ...defaultTypeExplorerIntent(),
        selectedDeclarationId: 7,
      },
      state: { status: "ready", inspection: bodyInspection },
      escapeHtml,
      highlightCSharp: escapeHtml,
    });
    const unselected = renderTypeExplorerView({
      typeDisplay: "JsonNamingPolicy",
      packageDisplay: "System.Text.Json",
      intent: defaultTypeExplorerIntent(),
      state: { status: "ready", inspection: bodyInspection },
      escapeHtml,
      highlightCSharp: escapeHtml,
    });

    assert.match(selected, />Inspect<\/button>/u);
    assert.match(selected, /aria-label="Inspect method body"/u);
    assert.match(selected, /data-type-explorer-inspect-body="11"/u);
    assert.doesNotMatch(unselected, /data-type-explorer-inspect-body/u);
  });

  test("Type Explorer distinguishes accessor Inspect actions without guessing", () => {
    const text = "public int Value { get { return 1; } set { } }";
    const getterStart = text.indexOf("{", text.indexOf("get"));
    const getterEnd = text.indexOf("}", getterStart) + 1;
    const setterStart = text.indexOf("{", text.indexOf("set"));
    const accessorInspection: TypeExplorerInspection = {
      ...inspection,
      document: {
        ...inspection.document!,
        projection: {
          ...inspection.document!.projection!,
          text,
          declarations: [{
            ...inspection.document!.projection!.declarations[0]!,
            identity: {
              ...identity,
              memberName: "Value",
            },
            range: { start: 0, length: text.length },
            bodies: [
              {
                bodyId: 21,
                role: "Getter",
                range: {
                  start: getterStart,
                  length: getterEnd - getterStart,
                },
                destination: {
                  moduleVersionId: "11111111-1111-1111-1111-111111111111",
                  member: {
                    ...identity,
                    stableSelector: "M:get_Value",
                    canonicalSignature: "System.Int32 Example::get_Value()",
                    memberName: "get_Value",
                  },
                  metadataToken: 0x06000002,
                },
              },
              {
                bodyId: 22,
                role: "Setter",
                range: {
                  start: setterStart,
                  length: text.length - setterStart - 2,
                },
                destination: {
                  moduleVersionId: "11111111-1111-1111-1111-111111111111",
                  member: {
                    ...identity,
                    stableSelector: "M:set_Value(System.Int32)",
                    canonicalSignature:
                      "System.Void Example::set_Value(System.Int32)",
                    memberName: "set_Value",
                  },
                  metadataToken: 0x06000003,
                },
              },
            ],
          }],
        },
      },
    };
    const html = renderTypeExplorerView({
      typeDisplay: "Example",
      packageDisplay: "Example",
      intent: {
        ...defaultTypeExplorerIntent(),
        selectedDeclarationId: 7,
      },
      state: { status: "ready", inspection: accessorInspection },
      escapeHtml,
      highlightCSharp: escapeHtml,
    });

    assert.equal(html.match(/>Inspect<\/button>/gu)?.length, 2);
    assert.match(html, /aria-label="Inspect getter body"/u);
    assert.match(html, /aria-label="Inspect setter body"/u);
  });

  test("Type Explorer keeps body acquisition progress and failure local", () => {
    const body = {
      bodyId: 11,
      role: "Method" as const,
      range: { start: 0, length: 48 },
      destination: {
        moduleVersionId: "11111111-1111-1111-1111-111111111111",
        member: identity,
        metadataToken: 0x06000001,
      },
    };
    const bodyInspection: TypeExplorerInspection = {
      ...inspection,
      document: {
        ...inspection.document!,
        projection: {
          ...inspection.document!.projection!,
          declarations: [{
            ...inspection.document!.projection!.declarations[0]!,
            bodies: [body],
          }],
        },
      },
    };
    const options = {
      typeDisplay: "JsonNamingPolicy",
      packageDisplay: "System.Text.Json",
      intent: {
        ...defaultTypeExplorerIntent(),
        selectedDeclarationId: 7,
      },
      state: { status: "ready" as const, inspection: bodyInspection },
      escapeHtml,
      highlightCSharp: escapeHtml,
    };
    const loading = renderTypeExplorerView({
      ...options,
      bodyInspection: { status: "loading", bodyId: 11 },
    });
    const failed = renderTypeExplorerView({
      ...options,
      bodyInspection: {
        status: "failed",
        bodyId: 11,
        error: "<body unavailable>",
      },
    });

    assert.match(loading, /aria-busy="true"/u);
    assert.match(loading, /Inspecting…/u);
    assert.match(failed, /role="alert">&lt;body unavailable&gt;/u);
    assert.doesNotMatch(failed, /<body unavailable>/u);
  });

test("Type Explorer keeps Selected body disabled during projection failure", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedDeclarationId: 7,
      documentRevision: "a".repeat(64),
    },
    state: {
      status: "ready",
      inspection: {
        ...inspection,
        document: {
          ...inspection.document!,
          projection: null,
          projectionFailure: {
            kind: "SelectedMemberHidden",
            message:
              "The selected member is excluded by the structural filters.",
          },
        },
      },
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /SelectedMemberHidden/u);
  assert.match(html, /value="SelectedBody" disabled/u);
});

test("Type Explorer keeps failures visible and inert", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json",
    intent: defaultTypeExplorerIntent(),
    state: {
      status: "failed",
      error: "<projection failed>",
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer failed/u);
  assert.match(html, /&lt;projection failed&gt;/u);
  assert.doesNotMatch(html, /<projection failed>/u);
  assert.match(html, /id="type-explorer-retry"/u);
});

test("Type Explorer presents current Worker cancellation as retryable failure", () => {
  assert.deepEqual(
    canceledTypeExplorerView("worker-restarted"),
    {
      status: "failed",
      error:
        "The inspection engine restarted while building Type Explorer. Retry to rebuild the Type document.",
    });
});

test("Type Explorer reveals both panes and restores the initiating member focus", () => {
  class FakeTarget {
    scrollCount = 0;
    focusCount = 0;

    scrollIntoView(options?: ScrollIntoViewOptions) {
      assert.equal(options?.block, "nearest");
      this.scrollCount++;
    }

    focus(options?: FocusOptions) {
      assert.equal(options?.preventScroll, true);
      this.focusCount++;
    }
  }

  const outline = new FakeTarget();
  const source = new FakeTarget();
  const root = fakeDom.parentNode({
    querySelector(selector: string) {
      if (selector.startsWith(".type-explorer-outline")) return outline;
      if (selector.startsWith(".type-explorer-source")) return source;
      return null;
    },
  });
  const projection = inspection.document?.projection;
  assert.ok(projection);

  assert.equal(
    restoreTypeExplorerMemberSelection(
      root,
      projection,
      identity,
      "outline"),
    true);
  assert.equal(outline.scrollCount, 1);
  assert.equal(source.scrollCount, 1);
  assert.equal(outline.focusCount, 1);
  assert.equal(source.focusCount, 0);
});

test("Type Explorer restores the selected member's visual viewport anchor", () => {
  class FakeElement {
    scrollTop = 0;
    top: number;

    constructor(top: number) {
      this.top = top;
    }

    getBoundingClientRect() {
      return { top: this.top };
    }
  }

  const outline = new FakeElement(20);
  const source = new FakeElement(40);
  const outlineTarget = new FakeElement(95);
  const sourceTarget = new FakeElement(180);
  const root = fakeDom.parentNode({
    querySelector(selector: string) {
      if (selector === ".type-explorer-outline") return outline;
      if (selector === ".type-explorer-source pre") return source;
      if (selector.startsWith(".type-explorer-outline "))
        return outlineTarget;
      if (selector.startsWith(".type-explorer-source "))
        return sourceTarget;
      return null;
    },
  });
  const projection = inspection.document?.projection;
  assert.ok(projection);
  const anchor = captureTypeExplorerViewportAnchor(
    root,
    projection,
    7);
  assert.ok(anchor);
  assert.equal(anchor.outlineOffsetTop, 75);
  assert.equal(anchor.sourceOffsetTop, 140);

  outline.scrollTop = 300;
  source.scrollTop = 500;
  outlineTarget.top = 45;
  sourceTarget.top = 260;
  assert.equal(
    restoreTypeExplorerViewportAnchor(root, projection, anchor),
    true);
  assert.equal(outline.scrollTop, 250);
  assert.equal(source.scrollTop, 580);
});

test("Type Explorer reports the pane that initiated member selection", () => {
  class FakeTarget {
    readonly dataset = { typeExplorerDeclaration: "7" };
    private readonly pane: "outline" | "source";
    private click: (() => void) | null = null;

    constructor(pane: "outline" | "source") {
      this.pane = pane;
    }

    addEventListener(name: string, listener: () => void) {
      if (name === "click") this.click = listener;
    }

    closest() {
      return this.pane === "outline" ? {} : null;
    }

    activate() {
      this.click?.();
    }
  }

  const outline = new FakeTarget("outline");
  const source = new FakeTarget("source");
  const root = fakeDom.parentNode({
    querySelector() {
      return null;
    },
    querySelectorAll(selector: string) {
      return selector === "[data-type-explorer-declaration]"
        ? [outline, source]
        : [];
    },
  });
  const selected: string[] = [];
  const actions: TypeExplorerViewActions = {
    close() {},
    retry() {},
    toggleOutline() {},
    selectBodyMode() {},
    selectPlacement() {},
    selectAccessibility() {},
    setIncludeGenerated() {},
    setIncludeDocumentation() {},
    setIncludeAttributes() {},
    inspectBody() {},
    selectMember(_declaration, pane) {
      selected.push(pane);
    },
  };

  bindTypeExplorerView(root, { status: "ready", inspection }, actions);
  outline.activate();
  source.activate();

  assert.deepEqual(selected, ["outline", "source"]);
});

test("Type Explorer binds Inspect to the exact projected body", () => {
  class FakeInspectTarget {
    readonly dataset = {
      typeExplorerInspectBody: "11",
    };
    private click:
      ((event: { stopPropagation(): void }) => void) | null = null;

    addEventListener(
      name: string,
      listener: (event: { stopPropagation(): void }) => void,
    ) {
      if (name === "click") this.click = listener;
    }

    activate() {
      this.click?.({ stopPropagation() {} });
    }
  }

  const target = new FakeInspectTarget();
  const body = {
    bodyId: 11,
    role: "Method" as const,
    range: { start: 0, length: 48 },
    destination: {
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      member: identity,
      metadataToken: 0x06000001,
    },
  };
  const declaration = {
    ...inspection.document!.projection!.declarations[0]!,
    bodies: [body],
  };
  const exactInspection: TypeExplorerInspection = {
    ...inspection,
    document: {
      ...inspection.document!,
      projection: {
        ...inspection.document!.projection!,
        declarations: [declaration],
      },
    },
  };
  const root = fakeDom.parentNode({
    querySelector() {
      return null;
    },
    querySelectorAll(selector: string) {
      return selector === "[data-type-explorer-inspect-body]"
        ? [target]
        : [];
    },
  });
  const inspected: number[] = [];
  const actions: TypeExplorerViewActions = {
    close() {},
    retry() {},
    toggleOutline() {},
    selectBodyMode() {},
    selectPlacement() {},
    selectAccessibility() {},
    setIncludeGenerated() {},
    setIncludeDocumentation() {},
    setIncludeAttributes() {},
    selectMember() {},
    inspectBody(selectedDeclaration, selectedBody) {
      assert.equal(selectedDeclaration, declaration);
      inspected.push(selectedBody.bodyId);
    },
  };

  bindTypeExplorerView(
    root,
    { status: "ready", inspection: exactInspection },
    actions);
  target.activate();

  assert.deepEqual(inspected, [11]);
});
