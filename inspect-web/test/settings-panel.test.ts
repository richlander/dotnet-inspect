import assert from "node:assert/strict";
import test from "node:test";
import {
  bindSettingsPanel,
  renderSettingsView,
  type SettingsPanelBindingActions,
  styleCatalogGroupsHtml,
} from "../src/settings-panel.ts";
import {
  reconcileStyleTaste,
  type ResolvedStyleCatalog,
} from "../src/style-vocabulary.ts";
import { fakeDom } from "./fake-dom.ts";

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

  dispatch(type: string, event: Event = fakeDom.event()) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();
  private readonly selectorQueries: string[] = [];

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    this.selectorQueries.push(`one:${selector}`);
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    this.selectorQueries.push(`all:${selector}`);
    return this.multiple.get(selector) ?? [];
  }

  assertSelectorQueries() {
    assert.deepEqual(
      [...this.selectorQueries].sort(),
      [
        "all:.settings-seg[data-theme]",
        "all:.settings-taste [data-taste]",
        "one:#home-settings",
        "one:#settings-backdrop",
        "one:#settings-close",
        "one:#settings-diagnostics-open",
        "one:#settings-dialog",
        "one:#settings-taste-clear",
      ].sort());
  }
}

function recordingActions(calls: string[]): SettingsPanelBindingActions {
  return {
    onClose: () => calls.push("close"),
    onOpenDiagnostics: () => calls.push("diagnostics"),
    onOpen: from => calls.push(`open:${from}`),
    onTasteClear: () => calls.push("clear"),
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

function term(
  _vocabulary: string,
  value: string,
  displayLabel: string,
  summary: string,
) {
  return {
    identity: { value },
    displayLabel,
    summary,
    mapEntries: [],
  };
}

const readableLocals = {
  term: term(
    "csharp.style-choices",
    "readable-locals",
    "Readable local names",
    "Synthesize readable local names."),
  conflictGroup: null,
  oracleEndorsed: true,
};
const expandedBraces = {
  term: term(
    "csharp.style-choices",
    "expanded-braces",
    "Expanded braces",
    "Always use braces."),
  conflictGroup: null,
  oracleEndorsed: false,
};
const styleCatalog: ResolvedStyleCatalog = {
  snapshotIdentity: `sha256:${"0".repeat(64)}`,
  tiers: [
    {
      term: term(
        "csharp.style-tiers",
        "naming",
        "Naming",
        "How identifiers are spelled."),
      byteDivergent: false,
      choices: [readableLocals],
    },
    {
      term: term(
        "csharp.style-tiers",
        "layout",
        "Layout",
        "Whitespace and braces."),
      byteDivergent: true,
      choices: [expandedBraces],
    },
  ],
  choices: [readableLocals, expandedBraces],
};

test("style taste reconciliation drops retired catalog choices", () => {
  assert.deepEqual(
    reconcileStyleTaste(
      ["readable-local-names", "expanded-braces"],
      styleCatalog),
    ["expanded-braces"]);
});

test("settings bindings dispatch the home entry control", () => {
  const root = new FakeRoot();
  const home = root.add("#home-settings", new FakeElement());
  const calls: string[] = [];

  bindSettingsPanel(
    fakeDom.parentNode(root),
    recordingActions(calls));
  root.assertSelectorQueries();

  assert.deepEqual(calls, []);
  home.dispatch("click");
  assert.deepEqual(calls, ["open:home"]);
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
  const diagnostics =
    root.add("#settings-diagnostics-open", new FakeElement());
  const calls: string[] = [];
  bindSettingsPanel(
    fakeDom.parentNode(root),
    recordingActions(calls));
  root.assertSelectorQueries();

  close.dispatch("click");
  dark.dispatch("click");
  light.dispatch("click");
  invalidTheme.dispatch("click");
  taste.dispatch("change");
  missingTaste.dispatch("change");
  clear.dispatch("click");
  diagnostics.dispatch("click");

  assert.deepEqual(calls, [
    "close",
    "theme:dark",
    "theme:light",
    "taste:readable-locals",
    "clear",
    "diagnostics",
  ]);
});

test("settings binding tolerates controls from the inactive surface being absent", () => {
  const root = new FakeRoot();
  assert.doesNotThrow(() => bindSettingsPanel(
    fakeDom.parentNode(root),
    recordingActions([])));
  root.assertSelectorQueries();
});

test("style catalog groups render tiers, byte-divergent badges, and checked state", () => {
  const html = styleCatalogGroupsHtml(
    { styleCatalog, styleCatalogError: "", taste: ["readable-locals"] },
    escapeHtml);

  assert.match(html, /Naming/);
  assert.match(html, /Layout/);
  assert.match(html, /byte-divergent/);
  assert.match(html, /data-taste="readable-locals" checked/);
  assert.doesNotMatch(html, /data-taste="expanded-braces" checked/);
  assert.match(html, /oracle/);
});

test("settings renders the routed Diagnostics entry", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "workbench",
    styleCatalog: {
      styleCatalog,
      styleCatalogError: "",
      taste: [],
    },
    escapeHtml,
  });

  assert.match(html, /<h2>Diagnostics<\/h2>/);
  assert.match(html, /id="settings-diagnostics-open"/);
  assert.match(html, /Open Diagnostics/);
});

test("style catalog groups escape untrusted tier and option text", () => {
  const html = styleCatalogGroupsHtml(
    {
      styleCatalog: {
        snapshotIdentity: `sha256:${"1".repeat(64)}`,
        tiers: [{
          term: term(
            "csharp.style-tiers",
            "naming",
            "<script>alert(1)</script>",
            "\"quoted\" & <b>bold</b>"),
          byteDivergent: false,
          choices: [{
            term: term(
              "csharp.style-choices",
              'x"onmouseover=1',
              "<img src=x>",
              "<i>italic</i> & more"),
            conflictGroup: null,
            oracleEndorsed: false,
          }],
        }],
        choices: [{
          term: term(
            "csharp.style-choices",
            'x"onmouseover=1',
            "<img src=x>",
            "<i>italic</i> & more"),
          conflictGroup: null,
          oracleEndorsed: false,
        }],
      },
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
      styleCatalog: {
        ...styleCatalog,
        tiers: [
          ...styleCatalog.tiers,
          {
            term: term(
              "csharp.style-tiers",
              "empty-tier",
              "Empty Tier",
              "Has no options."),
            byteDivergent: false,
            choices: [],
          },
        ],
      },
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
    { styleCatalog: null, styleCatalogError: "network error", taste: [] },
    escapeHtml);

  assert.match(html, /Style catalog unavailable: network error/);
});

test("style catalog renders nothing when empty without an error", () => {
  const html = styleCatalogGroupsHtml(
    { styleCatalog: null, styleCatalogError: "", taste: [] },
    escapeHtml);

  assert.equal(html, "");
});

test("settings view marks the active theme segment", () => {
  const html = renderSettingsView({
    theme: "light",
    settingsReturn: "home",
    styleCatalog: { styleCatalog, styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(html, /class="settings-seg active" data-theme="light" aria-pressed="true"/);
  assert.match(html, /class="settings-seg " data-theme="dark" aria-pressed="false"/);
});

test("settings view renders modal semantics and one close action", () => {
  const workbenchHtml = renderSettingsView({
    theme: "dark",
    settingsReturn: "workbench",
    styleCatalog: { styleCatalog: null, styleCatalogError: "", taste: [] },
    escapeHtml,
  });
  assert.match(workbenchHtml, /id="settings-dialog"/);
  assert.match(workbenchHtml, /role="dialog"/);
  assert.match(workbenchHtml, /aria-modal="true"/);
  assert.match(workbenchHtml, /aria-labelledby="settings-title"/);
  assert.match(workbenchHtml, /id="settings-title" tabindex="-1">Settings/);
  assert.match(
    workbenchHtml,
    /id="settings-decompiler-title" tabindex="-1">Decompiler style/);
  assert.match(workbenchHtml, /id="settings-close"[^>]*>Close/);
});

test("settings view reports the active style count and a reset control", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleCatalog, styleCatalogError: "", taste: ["readable-locals", "expanded-braces"] },
    escapeHtml,
  });

  assert.match(html, /2 on/);
  assert.match(html, /id="settings-taste-clear"/);
});

test("settings view shows the default badge and no reset control when taste is empty", () => {
  const html = renderSettingsView({
    theme: "dark",
    settingsReturn: "home",
    styleCatalog: { styleCatalog, styleCatalogError: "", taste: [] },
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
    styleCatalog: { styleCatalog: null, styleCatalogError: "", taste: [] },
    escapeHtml,
  });

  assert.match(html, /Style catalog is still loading/);
});
