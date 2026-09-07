import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { createSourceDiffState, type SourceDiffState } from "../src/source-comparison.ts";
import {
  bindSourceDiff, renderSourceComparisonAction, renderSourceDiffModal,
  type SourceDiffAction,
} from "../src/source-comparison-view.ts";
import { fakeDom } from "./fake-dom.ts";
import {
  sourceComparison, sourceContext, sourceEndpoint, sourceRequest,
} from "./source-comparison-fixture.ts";

const escapeHtml = (value: unknown): string => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");
const app = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");

function state(overrides: Partial<SourceDiffState> = {}): SourceDiffState {
  return {
    ...createSourceDiffState(), open: true, context: sourceContext,
    afterVersion: "2.0.0", ...overrides,
  };
}
function render(overrides: Partial<SourceDiffState> = {}): string {
  return renderSourceDiffModal({ state: state(overrides), escapeHtml });
}

test("contextual Source comparison action is visible with its unavailable reason", () => {
  const html = renderSourceComparisonAction({
    available: false, reason: "Select a method <first>.",
  }, escapeHtml);
  assert.match(html, /Compare authored source/);
  assert.match(html, /aria-describedby="compare-authored-source-reason" disabled/);
  assert.match(html, /Select a method &lt;first&gt;/);
  assert.doesNotMatch(renderSourceComparisonAction({
    available: true, reason: "",
  }, escapeHtml), / disabled/);
});

test("dialog is named, modal and explicit; same version enables Compare", () => {
  const html = render({ afterVersion: "" });
  assert.match(html, /role="dialog" aria-modal="true" aria-labelledby="source-diff-title"/);
  assert.match(html, /id="source-diff-title" tabindex="-1">Source Diff/);
  assert.match(html, /for="source-diff-after-version"/);
  assert.match(html, /id="source-diff-compare"[^>]* disabled/);
  assert.match(html, /Source is acquired only when you compare/);
  assert.doesNotMatch(render({ afterVersion: sourceContext.version }),
    /id="source-diff-compare"[^>]* disabled/);
  assert.match(render({ loading: true }), /id="source-diff-compare"[^>]* disabled/);
  assert.doesNotMatch(render({ open: false }), /Source Diff/);
});

test("unavailable dialog reports the reason without offering submission", () => {
  const html = render({ unavailableReason: "Accessors are not supported." });
  assert.match(html, /Comparison is unavailable here/);
  assert.match(html, /Accessors are not supported/);
  assert.doesNotMatch(html, /id="source-diff-compare"/);
});

test("native exactness is authoritative even when no relations are supplied", () => {
  const exact = render({
    comparison: sourceComparison({ isExact: true, lines: [] }),
    submittedRequest: sourceRequest,
  });
  assert.match(exact, /Exact authored source/);
  assert.match(exact, /data-source-diff-exact="true"/);
  assert.match(exact, /<details class="source-diff-declaration" open>/);
  const changed = render({
    comparison: sourceComparison({ isExact: false, lines: [] }),
    submittedRequest: sourceRequest,
  });
  assert.match(changed, /Changed authored source/);
  assert.match(changed, /data-source-diff-exact="false"/);
});

test("changed native declarations keep both text values and their one-based coordinates", () => {
  const html = render({ comparison: sourceComparison(), submittedRequest: sourceRequest });
  assert.match(html, /data-source-diff-kind="Changed"/);
  assert.match(html, /data-source-diff-difference="None"/);
  assert.match(html, /Before · line 1/);
  assert.match(html, /After · line 1/);
  assert.match(html, /1 \+ 2/);
  assert.match(html, /int Build\(\) =&gt; 3;/);
  assert.doesNotMatch(html, /line 0/);
});

test("movement survives Present and Changed relations alongside additions and removals", () => {
  const html = render({
    comparison: sourceComparison({ lines: [
      { kind: "Present", difference: "Moved", beforeLine: 1, beforeText: "// moved",
        afterLine: 4, afterText: "// moved" },
      { kind: "Changed", difference: "Moved", beforeLine: 2, beforeText: "// old",
        afterLine: 5, afterText: "// revised" },
      { kind: "Added", difference: "None", beforeLine: null, beforeText: null,
        afterLine: 1, afterText: "// added" },
      { kind: "Removed", difference: "None", beforeLine: 3, beforeText: "// removed",
        afterLine: null, afterText: null },
    ] }),
  });
  assert.match(html, /data-source-diff-kind="Present"\s+data-source-diff-difference="Moved"/);
  assert.match(html, /data-source-diff-kind="Changed"\s+data-source-diff-difference="Moved"/);
  assert.match(html, /Before · line 2/);
  assert.match(html, /After · line 5/);
  assert.match(html, /Before · absent/);
  assert.match(html, /After · absent/);
  assert.equal(html.split('class="source-diff-movement">Moved').length - 1, 2);
});

test("resolved endpoint identities and submitted labels never borrow the edited version", () => {
  const html = render({
    afterVersion: "99.0.0", submittedRequest: sourceRequest,
    comparison: sourceComparison(),
  });
  const submitted = html.slice(html.indexOf("data-source-diff-submitted"));
  assert.match(submitted, /Before 1\.2\.3 → After 2\.0\.0/);
  assert.match(submitted, /data-source-diff-side="before"[\s\S]*0x06000001/);
  assert.match(submitted, /data-source-diff-side="after"[\s\S]*0x06000019/);
  assert.doesNotMatch(submitted, /99\.0\.0/);
  assert.match(submitted, /before-mvid/);
  assert.match(submitted, /after-mvid/);
  assert.match(submitted, /before-revision/);
  assert.match(submitted, /after-revision/);
  assert.match(submitted, /lib\/net10\.0\/Example\.Package\.dll/);
  assert.match(submitted, /href="https:\/\/example\.org\/Widget\.cs"/);
});

test("an unavailable or failed endpoint does not erase the usable declaration or fabricate deletion", () => {
  for (const status of ["Unavailable", "Failed"]) {
    for (const side of ["before", "after"] as const) {
      const html = render({
        comparison: sourceComparison({
          status, lines: [], failure: status === "Failed" ? "Acquisition failed." : null,
          [side]: sourceEndpoint({
            state: status, text: null, detail: "No source for this version.",
          }),
        }),
      });
      assert.match(html, new RegExp(`data-source-diff-status>${status}`));
      assert.match(html, /No source for this version/);
      assert.match(html, /<details class="source-diff-declaration" open>/);
      assert.match(html, /not an empty declaration or decompiled fallback/);
      assert.doesNotMatch(html, /data-source-diff-kind="Removed"/);
      assert.doesNotMatch(html, /Exact authored source/);
      assert.match(html, /Not compared/);
      assert.doesNotMatch(html, /Not exact/);
      if (status === "Failed") assert.match(html, /Acquisition failed/);
    }
  }
});

test("Source and provenance use escaped text and only web links are navigable", () => {
  const hostile = '<img src=x onerror="alert(1)">';
  const html = render({
    comparison: sourceComparison({
      before: sourceEndpoint({
        text: hostile, detail: hostile,
        sourceUrl: "javascript:alert(1)", repositoryUrl: hostile, revision: hostile,
      }),
      lines: [{ kind: "Changed", difference: "None",
        beforeLine: 1, beforeText: hostile, afterLine: 1, afterText: hostile }],
    }),
  });
  assert.doesNotMatch(html, /<img/);
  assert.doesNotMatch(html, /href="javascript:/);
  assert.match(html, /&lt;img/);
  assert.match(html, /javascript:alert\(1\)/);
});

test("app action placement applies to all member surfaces and uses existing disposal and shell gates", () => {
  assert.match(app, /const methodBodyPageContext = activeScope === "member"/);
  assert.match(app, /methodBodyPageContext\s*\?\s*renderSourceComparisonAction/);
  assert.match(app, /renderSourceDiffModal\(\{\s*state: state\.sourceDiff/);
  for (const name of [
    "clearMemberContentCache", "captureCanonicalWorkspaceRestoreSnapshot",
    "dismissModalsForRoutedNavigation",
  ]) {
    const start = app.indexOf(`function ${name}(`);
    assert.match(app.slice(start, start + 380), /sourceComparison\.dispose\(\)/, name);
  }
  assert.match(app, /id: "source-diff\.dismiss",\s*key: "Escape"/);
  assert.match(app, /&& !state\.sourceDiff\.open/);
  assert.match(app, /state\.sourceDiff\.open \? " inert"/);
  const css = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
  assert.match(css, /\.method-body-pair,[\s\S]*?grid-template-columns: 1fr/);
});

test("launch availability rejects platform, property/accessor, missing overload and non-MethodDef contexts", () => {
  const availability = app.match(/function sourceComparisonAvailability\(\)[\s\S]*?\n\}/)?.[0];
  assert.ok(availability);
  const run = (kind: string, token: number | null, runtime = false, bodyToken: number | null = null) => {
    const result: { available?: boolean; reason?: string } = {};
    runInNewContext(stripTypeScriptTypes(`${availability}\nObject.assign(result, sourceComparisonAvailability());`), {
      result, state: { package: { isRuntimePack: runtime }, selectedBodyTarget:
        bodyToken === null ? null : { metadataToken: bodyToken } },
      scope: () => "member", selectedType: () => ({}),
      selectedMember: () => ({ overloads: [] }),
      selectedConcreteOverload: () => kind === "" ? null : { kind, metadataToken: token },
      graphOnlyImplementationBody: () => null,
      isMethodBodyToken: (value: number) =>
        (value & 0xff000000) === 0x06000000 && (value & 0x00ffffff) !== 0,
    });
    return result;
  };
  assert.equal(run("method", 0x06000001).available, true);
  assert.match(run("method", 0x06000001, true).reason ?? "", /runtime and platform/);
  assert.match(run("property", 0x17000001).reason ?? "", /accessors/);
  assert.match(run("", null).reason ?? "", /one method overload/);
  assert.match(run("method", 0x04000001).reason ?? "", /MethodDef/);
  assert.match(run("method", 0x06000001, false, 0x06000002).reason ?? "", /accessor or nested body/);
});

class FakeElement {
  readonly listeners = new Map<string, EventListener[]>();
  readonly dataset: Record<string, string>;
  readonly ownerDocument: { activeElement: FakeElement | null };
  hidden = false;
  value = "";
  selectionStart = 0;
  constructor(ownerDocument: { activeElement: FakeElement | null }, action = "") {
    this.ownerDocument = ownerDocument;
    this.dataset = { sourceDiffAction: action };
  }
  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, [...this.listeners.get(type) ?? [], listener]);
  }
  focus() { this.ownerDocument.activeElement = this; }
  getClientRects() { return [{}]; }
  emit(type: string, values: object = {}) {
    for (const listener of this.listeners.get(type) ?? [])
      listener(fakeDom.event({ target: this, ...values }));
  }
}

test("DOM bindings keep editing separate from Compare, dismiss only the backdrop, and trap Tab", () => {
  const owner = { activeElement: null as FakeElement | null };
  const open = new FakeElement(owner, "open");
  const close = new FakeElement(owner, "close");
  const compare = new FakeElement(owner, "compare");
  const input = new FakeElement(owner);
  const backdrop = new FakeElement(owner);
  const modal = Object.assign(new FakeElement(owner), {
    querySelectorAll: () => [close, input, compare],
    contains: (value: unknown) => [close, input, compare].some(element => element === value),
  });
  const elements = new Map([
    ["[data-source-diff-version]", input], ["#source-diff-backdrop", backdrop],
    ["#source-diff-modal", modal],
  ]);
  const root = fakeDom.parentNode({
    querySelectorAll: () => [open, close, compare],
    querySelector: (selector: string) => elements.get(selector),
  });
  const actions: SourceDiffAction[] = [];
  bindSourceDiff(root, { onAction: action => actions.push(action) });
  open.emit("click");
  input.value = "2.0.0";
  input.selectionStart = 3;
  input.emit("input");
  assert.deepEqual(actions, [{ kind: "open" }, { kind: "version", value: "2.0.0", caret: 3 }]);
  compare.emit("click");
  backdrop.emit("click", { target: modal });
  assert.equal(actions.at(-1)?.kind, "compare");
  backdrop.emit("click");
  assert.equal(actions.at(-1)?.kind, "close");

  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "HTMLElement");
  Object.defineProperty(globalThis, "HTMLElement", { configurable: true, value: FakeElement });
  try {
    let prevented = 0;
    compare.focus();
    modal.emit("keydown", { key: "Tab", shiftKey: false, preventDefault: () => prevented++ });
    assert.equal(owner.activeElement, close);
    close.focus();
    modal.emit("keydown", { key: "Tab", shiftKey: true, preventDefault: () => prevented++ });
    assert.equal(owner.activeElement, compare);
    assert.equal(prevented, 2);
  } finally {
    if (descriptor) Object.defineProperty(globalThis, "HTMLElement", descriptor);
    else Reflect.deleteProperty(globalThis, "HTMLElement");
  }
});

test("app focus hooks focus the version field, preserve the caret, and return focus on dismissal", () => {
  const restore = app.match(/function restoreSourceDiffFocus\([\s\S]*?\n\}/)?.[0];
  const close = app.match(/function closeSourceDiff\([\s\S]*?\n\}/)?.[0];
  assert.ok(restore);
  assert.ok(close);
  const focused: string[] = [];
  const ranges: number[][] = [];
  const input = {
    value: "2.0.0", focus: () => focused.push("input"),
    setSelectionRange: (start: number, end: number) => ranges.push([start, end]),
  };
  const launch = { focus: () => focused.push("launch") };
  const title = { focus: () => focused.push("title") };
  const closedState = { sourceDiff: { open: true } };
  runInNewContext(stripTypeScriptTypes(`
    let sourceDiffFocusIntent = true;
    let sourceDiffVersionCaret: number | null = null;
    ${restore}
    ${close}
    restoreSourceDiffFocus();
    sourceDiffVersionCaret = 3;
    restoreSourceDiffFocus();
    closeSourceDiff(true);
  `), {
    state: closedState, SOURCE_DIFF_VERSION_SELECTOR: "#source-diff-after-version",
    document: {
      querySelector: (selector: string) => selector === "#source-diff-after-version" ? input : launch,
      getElementById: () => title,
    },
    sourceComparison: {
      isOpen: () => closedState.sourceDiff.open,
      close: () => {
        closedState.sourceDiff.open = false;
        return { handled: true, returnFocusSelector: "#compare-authored-source" };
      },
    },
    render() {},
    requestAnimationFrame: (callback: () => void) => callback(),
  });
  assert.deepEqual(focused, ["input", "input", "launch"]);
  assert.deepEqual(ranges, [[3, 3]]);
  assert.equal(closedState.sourceDiff.open, false);
});
