import assert from "node:assert/strict";
import test from "node:test";

import {
  bindCreditsPanel,
  isCreditsPath,
  renderCreditsPage,
} from "../src/credits-panel.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  private readonly listeners = new Map<string, EventListener[]>();
  textContent = "";

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({ target: this }));
    }
  }
}

test("credits route accepts only the two explicitly hosted paths", () => {
  assert.equal(isCreditsPath("/credits"), true);
  assert.equal(isCreditsPath("/credits/"), true);
  assert.equal(isCreditsPath("/Credits"), false);
  assert.equal(isCreditsPath("/credits//"), false);
  assert.equal(isCreditsPath("/"), false);
  assert.equal(isCreditsPath("/credits/more"), false);
});

test("credits name the core stack and supporting open-source libraries", () => {
  const html = renderCreditsPage("dark");

  for (const technology of [
    ".NET 11",
    "WebAssembly",
    "TypeScript 7",
    "NuGet",
    "System.Reflection.Metadata",
    "Vite",
    "Mermaid",
    "Prism.js",
    "Marked",
    "DOMPurify",
    "Azure Static Web Apps",
    "Azure Functions",
  ]) {
    assert.match(html, new RegExp(technology.replaceAll(".", "\\.")));
  }
  assert.match(html, /open source on GitHub/);
});

test("credits links open safely and theme control reflects the active theme", () => {
  const dark = renderCreditsPage("dark");
  const light = renderCreditsPage("light");

  assert.doesNotMatch(dark, /target="_blank"(?! rel="noopener noreferrer")/);
  assert.match(dark, /id="credits-theme"[^>]*>light<\/button>/);
  assert.match(light, /id="credits-theme"[^>]*>dark<\/button>/);
  assert.match(dark, /id="credits-close"/);
});

test("credits controls dispatch through typed bindings", () => {
  const close = new FakeElement();
  const theme = new FakeElement();
  const elements = new Map<string, FakeElement>([
    ["#credits-close", close],
    ["#credits-theme", theme],
  ]);
  const calls: string[] = [];

  bindCreditsPanel(fakeDom.parentNode({
    querySelector: (selector: string) => elements.get(selector) ?? null,
  }), {
    onClose: () => calls.push("close"),
    onToggleTheme: () => {
      calls.push("theme");
      return "light";
    },
  });

  close.dispatch("click");
  theme.dispatch("click");
  assert.deepEqual(calls, ["close", "theme"]);
  assert.equal(theme.textContent, "dark");
  assert.equal(elements.get("#credits-theme"), theme);
});
