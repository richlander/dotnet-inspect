import assert from "node:assert/strict";
import test from "node:test";
import {
  bindSettingsPanel,
  renderSettingsView,
  renderTastePopover,
  type SettingsPanelBindingActions,
  styleCatalogGroupsHtml,
} from "../src/settings-panel.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, event: Event = {} as Event) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): SettingsPanelBindingActions {
  return {
    onClose: () => calls.push("close"),
    onOpen: from => calls.push(`open:${from}`),
    onTasteClear: () => calls.push("clear"),
    onTasteOpenToggle: () => calls.push("taste-open"),
    onTasteToggle: taste => calls.push(`taste:${taste}`),
    onThemeSelect: theme => calls.push(`theme:${theme}`),
  };
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const styleTiers = [
  { id: "naming", title: "Naming", summary: "How identifiers are spelled." },
  { id: "layout", title: "Layout", summary: "Whitespace and braces.", byte_divergent: true },
];

const styleOptions = [
  { id: "readable-locals", tier: "naming", title: "Readable local names", summary: "Synthesize readable local names.", oracle_endorsed: true },
  { id: "expanded-braces", tier: "layout", title: "Expanded braces", summary: "Always use braces." },
];

test("settings bindings dispatch entry controls and contain taste clicks", () => {
  const root = new FakeRoot();
  const home = root.add("#home-settings", new FakeElement());
  const workbench = root.add("#open-settings", new FakeElement());
  const taste = root.add("#taste-btn", new FakeElement());
  const propagation = { stopped: false };
  const event = {
    stopPropagation: () => {
      propagation.stopped = true;
    },
  } as unknown as Event;
  const calls: string[] = [];

  bindSettingsPanel(
    root as unknown as ParentNode,
    recordingActions(calls));

  assert.deepEqual(calls, []);
  home.dispatch("click");
  assert.deepEqual(calls, ["open:home"]);
  workbench.dispatch("click");
  assert.deepEqual(calls, ["open:home", "open:workbench"]);
  taste.dispatch("click", event);
  assert.deepEqual(calls, ["open:home", "open:workbench", "taste-open"]);
  assert.equal(propagation.stopped, true);
});

test("settings bindings dispatch valid settings-page controls", () => {
  const root = new FakeRoot();
  const close = root.add("#settings-close", new FakeElement());
  const dark = new FakeElement({ theme: "dark" });
  const light = new FakeElement({ theme: "light" });
  const invalidTheme = new FakeElement({ theme: "system" });
  root.addAll(
    ".settings-seg[data-theme]",
    dark,
    light,
    invalidTheme);
  const taste = new FakeElement({ taste: "readable-locals" });
  const missingTaste = new FakeElement();
  root.addAll(".settings-taste [data-taste]", taste, missingTaste);
  const clear = root.add("#settings-taste-clear", new FakeElement());
  const calls: string[] = [];
  bindSettingsPanel(
    root as unknown as ParentNode,
    recordingActions(calls));

  close.dispatch("click");
  dark.dispatch("click");
  light.dispatch("click");
  invalidTheme.dispatch("click");
  taste.dispatch("change");
  missingTaste.dispatch("change");
  clear.dispatch("click");

  assert.deepEqual(calls, [
    "close",
    "theme:dark",
    "theme:light",
    "taste:readable-locals",
    "clear",
  ]);
});

test("taste popover bindings dispatch its optional controls", () => {
  const root = new FakeRoot();
  const taste = new FakeElement({ taste: "expanded-braces" });
  const missingTaste = new FakeElement();
  root.addAll("#taste-popover [data-taste]", taste, missingTaste);
  const clear = root.add("#taste-clear", new FakeElement());
  const calls: string[] = [];
  bindSettingsPanel(
    root as unknown as ParentNode,
    recordingActions(calls));

  taste.dispatch("change");
  missingTaste.dispatch("change");
  clear.dispatch("click");

  assert.deepEqual(calls, [
    "taste:expanded-braces",
    "taste:",
    "clear",
  ]);
});

test("settings binding tolerates controls from the inactive surface being absent", () => {
  const root = new FakeRoot();
  assert.doesNotThrow(() => bindSettingsPanel(
    root as unknown as ParentNode,
    recordingActions([])));
});

test("style catalog groups render tiers, byte-divergent badges, and checked state", () => {
  const html = styleCatalogGroupsHtml(
    { styleTiers, styleOptions, styleCatalogError: "", taste: ["readable-locals"] },
    escapeHtml);

  assert.match(html, /Naming/);
  assert.match(html, /Layout/);
  assert.match(html, /byte-divergent/);
  assert.match(html, /data-taste="readable-locals" checked/);
  assert.doesNotMatch(html, /data-taste="expanded-braces" checked/);
  assert.match(html, /oracle/);
});

test("style catalog groups escape untrusted tier and option text", () => {
  const html = styleCatalogGroupsHtml(
    {
      styleTiers: [{ id: "naming", title: '<script>alert(1)</script>', summary: "\"quoted\" & <b>bold</b>" }],
      styleOptions: [{ id: 'x"onmouseover=1', tier: "naming", title: "<img src=x>", summary: "<i>italic</i> & more" }],
      styleCatalogError: "",
      taste: [],
    },
    escapeHtml);

  assert.doesNotMatch(html, /<script>/);
  assert.doesNotMatch(html, /<img src=x>/);
  assert.doesNotMatch(html, /<b>bold<\/b>/);
  assert.doesNotMatch(html, /<i>italic<\/i>/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /&lt;b&gt;bold&lt;\/b&gt;/);
  assert.match(html, /&lt;i&gt;italic&lt;\/i&gt;/);
  assert.match(html, /&amp;/);
});

test("style catalog groups hide a tier with no options", () => {
  const html = styleCatalogGroupsHtml(
    {
      styleTiers: [
        ...styleTiers,
        { id: "empty-tier", title: "Empty Tier", summary: "Has no options." },
      ],
      styleOptions,
      styleCatalogError: "",
      taste: [],
    },
    escapeHtml);

  assert.match(html, /Naming/);
  assert.match(html, /Layout/);
  assert.doesNotMatch(html, /Empty Tier/);
});

test("style catalog reports an error when the catalog failed to load", () => {
  const html = styleCatalogGroupsHtml(
    { styleTiers: [], styleOptions: [], styleCatalogError: "network error", taste: [] },
    escapeHtml);

  assert.match(html, /Style catalog unavailable: network error/);
});

test("style catalog renders nothing when empty without an error", () => {
  const html = styleCatalogGroupsHtml(
    { styleTiers: [], styleOptions: [], styleCatalogError: "", taste: [] },
    escapeHtml);

  assert.equal(html, "");
});

test("taste popover shows a reset button once a style is active", () => {
  const html = renderTastePopover(
    { styleTiers, styleOptions, styleCatalogError: "", taste: ["readable-locals"] },
    escapeHtml);

  assert.match(html, /id="taste-popover"/);
  assert.match(html, /id="taste-clear"/);
  assert.doesNotMatch(html, /opcode-faithful/);
});

test("taste popover shows the default state when nothing is active", () => {
  const html = renderTastePopover(
    { styleTiers, styleOptions, styleCatalogError: "", taste: [] },
    escapeHtml);

  assert.doesNotMatch(html, /id="taste-clear"/);
  assert.match(html, /default · opcode-faithful/);
});

test("taste popover falls back to an empty-catalog message", () => {
  const html = renderTastePopover(
    { styleTiers: [], styleOptions: [], styleCatalogError: "", taste: [] },
    escapeHtml);

  assert.match(html, /Style catalog unavailable\.<\/div>/);
});

test("settings view marks the active theme segment", () => {
  const html = renderSettingsView({
    theme: "light",
    settingsReturn: "home",
    styleCatalog: { styleTiers, styleOptions, styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(html, /class="settings-seg active" data-theme="light" aria-pressed="true"/);
  assert.match(html, /class="settings-seg " data-theme="dark" aria-pressed="false"/);
});

test("settings view labels the close button by return destination", () => {
  const workbenchHtml = renderSettingsView({
    theme: "dark",
    settingsReturn: "workbench",
    styleCatalog: { styleTiers: [], styleOptions: [], styleCatalogError: "", taste: [] },
    escapeHtml,
  });
  const homeHtml = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleTiers: [], styleOptions: [], styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(workbenchHtml, /back to workbench ✕/);
  assert.match(homeHtml, /back to home ✕/);
});

test("settings view reports the active style count and a reset control", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleTiers, styleOptions, styleCatalogError: "", taste: ["readable-locals", "expanded-braces"] },
    escapeHtml,
  });

  assert.match(html, /2 on/);
  assert.match(html, /id="settings-taste-clear"/);
});

test("settings view shows the default badge and no reset control when taste is empty", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleTiers, styleOptions, styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(html, /settings-badge">default</);
  assert.doesNotMatch(html, /id="settings-taste-clear"/);
  assert.match(html, /Default · opcode-faithful/);
});

test("settings view surfaces a loading message while the catalog is still empty", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleTiers: [], styleOptions: [], styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(html, /Style catalog is still loading/);
});
