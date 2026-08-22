import assert from "node:assert/strict";
import test from "node:test";
import {
  bindHomeShell,
  bindLoadErrorShell,
  bindWorkbenchShell,
} from "../src/shell-controls.ts";
import { fakeDom } from "./fake-dom.ts";

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

  dispatch(type: string) {
    let prevented = false;
    const event = fakeDom.event({
      target: this,
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
    ["#theme-toggle", "toggle-theme"],
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
    onShare: () => calls.push("share"),
    onToggleTheme: () => calls.push("toggle-theme"),
  });

  assert.deepEqual(calls, []);
  for (const [selector, call] of controls) {
    root.querySelector(selector)?.dispatch("click");
    assert.equal(calls.at(-1), call);
  }
  assert.equal(calls.length, controls.size);
});

test("home shell accepts only known demos", () => {
  const root = new FakeRoot();
  const theme = new FakeElement();
  const dismiss = new FakeElement();
  const stj = new FakeElement({ homeDemo: "stj" });
  const runtime = new FakeElement({ homeDemo: "runtime" });
  const callgraph = new FakeElement({ homeDemo: "callgraph" });
  const unknown = new FakeElement({ homeDemo: "other" });
  const absent = new FakeElement();
  root.add("#home-theme", theme);
  root.add("#dismiss-notice", dismiss);
  root.addAll(
    "[data-home-demo]",
    stj,
    runtime,
    callgraph,
    unknown,
    absent,
  );
  const calls: string[] = [];

  bindHomeShell(fakeDom.parentNode(root), {
    onDemo: demo => calls.push(`demo:${demo}`),
    onDismissNotice: () => calls.push("dismiss"),
    onToggleTheme: () => calls.push("theme"),
  });

  assert.deepEqual(calls, []);
  theme.dispatch("click");
  dismiss.dispatch("click");
  stj.dispatch("click");
  runtime.dispatch("click");
  callgraph.dispatch("click");
  unknown.dispatch("click");
  absent.dispatch("click");
  assert.deepEqual(calls, [
    "theme",
    "dismiss",
    "demo:stj",
    "demo:runtime",
    "demo:callgraph",
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
    onOpenPackage: (id, version) => calls.push(`open:${id}@${version}`),
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
    "open:Example.Package@2.0.0",
    "open:Latest.Package@latest",
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
    onShare() {},
    onToggleTheme() {},
  }));
  assert.doesNotThrow(() => bindHomeShell(root, {
    onDemo() {},
    onDismissNotice() {},
    onToggleTheme() {},
  }));
  assert.doesNotThrow(() => bindLoadErrorShell(root, {
    onOpenPackage() {},
    onRetry() {},
  }));
});
