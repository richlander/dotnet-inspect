import assert from "node:assert/strict";
import test from "node:test";
import {
  bindHomeShell,
  bindLoadErrorShell,
  bindWorkbenchShell,
  workbenchShellHtml,
} from "../src/shell-controls.ts";
import { setProductHomeDemoCatalog } from "../src/product-home-demos.ts";
import { fakeDom } from "./fake-dom.ts";

setProductHomeDemoCatalog([
  { id: "stj-serializer", title: "System.Text.Json", summary: "Browse a real package API" },
  { id: "extensions-callgraph", title: "Cross-package call graph", summary: "Trace calls across three packages" },
  { id: "stj-serialize-callgraph", title: "Serialize call graph", summary: "Dense package-local STJ graph" },
  { id: "config-bind-callgraph", title: "Configuration Bind", summary: "Recursive binder call graph" },
  { id: "options-add-callgraph", title: "Options hub", summary: "Inbound fan-in at AddOptions" },
  { id: "di-tryadd-callgraph", title: "DI TryAdd hub", summary: "Keyed/scoped Try* fan-in" },
  { id: "http-addhttpclient-callgraph", title: "AddHttpClient", summary: "HttpClient factory registration" },
  { id: "stj-getdecimal-callgraph", title: "JsonElement.GetDecimal", summary: "STJ number parse path" },
]);

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  hidden = true;
  value = "";
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, values: Record<string, unknown> = {}) {
    let prevented = false;
    const event = fakeDom.event({
      target: this,
      ...values,
      preventDefault: () => prevented = true,
    });
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
    return prevented;
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

test("workbench shell binds every rendered control without eager work", () => {
  const root = new FakeRoot();
  const controls = new Map([
    ["#share", "share"],
    ["#dismiss-notice", "dismiss-notice"],
    ["#retry-notice", "retry-notice"],
    ["#dismiss-package-notice", "dismiss-package-notice"],
    ["#nav-back", "navigate-back"],
    ["#nav-forward", "navigate-forward"],
    ["#go-home", "go-home"],
    ["#open-search", "search"],
    ["#help", "help"],
  ]);
  for (const selector of controls.keys()) {
    root.add(selector, new FakeElement());
  }
  const calls: string[] = [];

  bindWorkbenchShell(fakeDom.parentNode(root), {
    onDismissNotice: () => calls.push("dismiss-notice"),
    onDismissPackageNotice: () => calls.push("dismiss-package-notice"),
    onGoHome: () => calls.push("go-home"),
    onHelp: () => calls.push("help"),
    onNavigateBack: () => calls.push("navigate-back"),
    onNavigateForward: () => calls.push("navigate-forward"),
    onRetryNotice: () => calls.push("retry-notice"),
    onSearch: () => calls.push("search"),
    onShare: () => calls.push("share"),
  });

  assert.deepEqual(calls, []);
  for (const [selector, call] of controls) {
    root.querySelector(selector)?.dispatch("click");
    assert.equal(calls.at(-1), call);
  }
  assert.equal(calls.length, controls.size);
});

test("workbench shell renders the top-level workbench actions", () => {
  const html = workbenchShellHtml();

  assert.match(html, /class="brand"[^>]*>[\s\S]*dotnet-inspect/);
  assert.match(html, /id="open-search"[\s\S]*>[\s\S]*Search[\s\S]*<kbd>Ctrl\/⌘ P<\/kbd>/);
  assert.match(html, /id="go-home"[^>]*>Home<\/button>/);
  assert.match(html, /id="open-settings"[^>]*>Settings<\/button>/);
  assert.doesNotMatch(html, /Package or Package@version|theme-toggle|id="share"|id="help"/);
});

test("home shell accepts only known demos", () => {
  const root = new FakeRoot();
  const theme = new FakeElement();
  const dismiss = new FakeElement();
  const credits = new FakeElement();
  const stj = new FakeElement({ homeDemo: "stj-serializer" });
  const callgraph = new FakeElement({ homeDemo: "extensions-callgraph" });
  const serializeGraph = new FakeElement({ homeDemo: "stj-serialize-callgraph" });
  const unknown = new FakeElement({ homeDemo: "other" });
  const absent = new FakeElement();
  root.add("#home-theme", theme);
  root.add("#dismiss-notice", dismiss);
  root.add("#home-credits", credits);
  root.addAll(
    "[data-home-demo]",
    stj,
    callgraph,
    serializeGraph,
    unknown,
    absent,
  );
  const calls: string[] = [];

  bindHomeShell(fakeDom.parentNode(root), {
    onDemo: demo => calls.push(`demo:${demo}`),
    onDismissNotice: () => calls.push("dismiss"),
    onOpenCredits: () => calls.push("credits"),
    onToggleTheme: () => calls.push("theme"),
  });

  assert.deepEqual(calls, []);
  theme.dispatch("click");
  dismiss.dispatch("click");
  assert.equal(credits.dispatch("click", { button: 0, metaKey: true }), false);
  assert.equal(credits.dispatch("click", { button: 1 }), false);
  assert.deepEqual(calls, ["theme", "dismiss"]);
  assert.equal(credits.dispatch("click"), true);
  stj.dispatch("click");
  callgraph.dispatch("click");
  serializeGraph.dispatch("click");
  unknown.dispatch("click");
  absent.dispatch("click");
  assert.deepEqual(calls, [
    "theme",
    "dismiss",
    "credits",
    "demo:stj-serializer",
    "demo:extensions-callgraph",
    "demo:stj-serialize-callgraph",
  ]);
});

test("load error shell parses replacement packages and owns local detail state", () => {
  const root = new FakeRoot();
  const retry = new FakeElement();
  const form = new FakeElement();
  const input = new FakeElement();
  const toggle = new FakeElement();
  const detail = new FakeElement();
  root.add("#retry-load", retry);
  root.add("#error-package-query", form);
  root.add("#error-package-input", input);
  root.add("#toggle-error-detail", toggle);
  root.add(".load-error-detail", detail);
  const calls: string[] = [];

  bindLoadErrorShell(fakeDom.parentNode(root), {
    onOpenPackage: query =>
      calls.push(`open:${query.packageId}@${query.version}:${query.explicitVersion}`),
    onRetry: () => calls.push("retry"),
  });

  assert.deepEqual(calls, []);
  retry.dispatch("click");
  input.value = " Example.Package@2.0.0 ";
  assert.equal(form.dispatch("submit"), true);
  input.value = "Latest.Package";
  assert.equal(form.dispatch("submit"), true);
  input.value = "Invalid.Package@";
  assert.equal(form.dispatch("submit"), true);
  input.value = " ";
  assert.equal(form.dispatch("submit"), true);
  toggle.dispatch("click");
  assert.equal(detail.hidden, false);
  toggle.dispatch("click");
  assert.equal(detail.hidden, true);
  assert.deepEqual(calls, [
    "retry",
    "open:Example.Package@2.0.0:true",
    "open:Latest.Package@latest:false",
  ]);
});

test("shell bindings tolerate inactive surfaces", () => {
  const root = fakeDom.parentNode(new FakeRoot());
  assert.doesNotThrow(() => bindWorkbenchShell(root, {
    onDismissNotice() {},
    onDismissPackageNotice() {},
    onGoHome() {},
    onHelp() {},
    onNavigateBack() {},
    onNavigateForward() {},
    onRetryNotice() {},
    onSearch() {},
    onShare() {},
  }));
  assert.doesNotThrow(() => bindHomeShell(root, {
    onDemo() {},
    onDismissNotice() {},
    onOpenCredits() {},
    onToggleTheme() {},
  }));
  assert.doesNotThrow(() => bindLoadErrorShell(root, {
    onOpenPackage() {},
    onRetry() {},
  }));
});
