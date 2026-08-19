import assert from "node:assert/strict";
import test from "node:test";
import {
  renderSettingsView,
  renderTastePopover,
  styleCatalogGroupsHtml,
} from "../src/settings-panel.ts";

function escapeHtml(value) {
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
  assert.match(html, /&lt;script&gt;/);
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
