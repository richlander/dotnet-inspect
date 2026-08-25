import assert from "node:assert/strict";
import test from "node:test";
import { bindLibraryControls } from "../src/library-controls.ts";
import type {
  LibraryControlBindingActions,
  PlatformLibraryLens,
} from "../src/library-controls.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  value = "";
  selectedOptions: FakeElement[] = [];
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
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({ target: this }));
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
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): LibraryControlBindingActions {
  return {
    onAccessibilityChipSelect: value =>
      calls.push(`accessibility:${value}`),
    onLibraryChipSelect: value => calls.push(`library-chip:${value}`),
    onLibraryJump: value => calls.push(`library-jump:${value}`),
    onPlatformLibrarySelect: (name, pack) =>
      calls.push(`platform:${name}:${pack}`),
    onPlatformLensLibrarySelect: (
      lens: PlatformLibraryLens,
      name,
      pack,
    ) => calls.push(`platform-lens:${lens}:${name}:${pack}`),
  };
}

test("library controls decode every rendered selector without eager work", () => {
  const root = new FakeRoot();
  const libraryChip = new FakeElement({ libraryChip: "System.Text.Json" });
  const defaultLibraryChip = new FakeElement();
  const accessChip = new FakeElement({ accessChip: "public" });
  const allAccessChip = new FakeElement();
  root.addAll(
    "[data-library-chip]",
    libraryChip,
    defaultLibraryChip);
  root.addAll("[data-access-chip]", accessChip, allAccessChip);

  const libraryJump = root.add("#library-jump", new FakeElement());
  libraryJump.value = "System.Collections";

  const platform = new FakeElement();
  platform.value = "System.Private.CoreLib";
  platform.selectedOptions = [new FakeElement({ pack: "netcore.app" })];
  // A platform option carrying no pack payload. This used to be rewritten to the specific
  // pack "netcore.app", showing a different pack's library with nothing reporting it.
  const packlessPlatform = new FakeElement();
  packlessPlatform.value = "System.Runtime";
  packlessPlatform.selectedOptions = [new FakeElement()];
  const emptyPlatform = new FakeElement();
  const invalidPlatform = new FakeElement();
  invalidPlatform.value = "System.Bogus";
  invalidPlatform.selectedOptions = [new FakeElement({ pack: "bogus-pack" })];
  root.addAll(
    "[data-platform-library-select]",
    platform,
    packlessPlatform,
    invalidPlatform,
    emptyPlatform);

  const integrations = new FakeElement();
  integrations.value = "System.Net.Http";
  integrations.selectedOptions = [new FakeElement({ pack: "netcore.app" })];
  const opportunities = new FakeElement();
  opportunities.value = "System.Text.Json";
  opportunities.selectedOptions = [new FakeElement()];
  const analysis = new FakeElement();
  analysis.value = "System.Linq";
  const emptyAnalysis = new FakeElement();
  const metadata = new FakeElement();
  metadata.value = "System.Console";
  metadata.selectedOptions = [new FakeElement({ pack: "windowsdesktop.app" })];
  root.addAll("[data-platform-integrations-library]", integrations);
  root.addAll("[data-platform-opportunities-library]", opportunities);
  root.addAll("[data-platform-analysis-library]", analysis, emptyAnalysis);
  root.addAll("[data-platform-metadata-library]", metadata);
  const calls: string[] = [];

  bindLibraryControls(
    fakeDom.parentNode(root),
    recordingActions(calls));

  assert.deepEqual(calls, []);
  libraryChip.dispatch("click");
  defaultLibraryChip.dispatch("click");
  accessChip.dispatch("click");
  allAccessChip.dispatch("click");
  libraryJump.dispatch("change");
  libraryJump.value = "";
  libraryJump.dispatch("change");
  platform.dispatch("change");
  packlessPlatform.dispatch("change");
  invalidPlatform.dispatch("change");
  emptyPlatform.dispatch("change");
  integrations.dispatch("change");
  opportunities.dispatch("change");
  analysis.dispatch("change");
  emptyAnalysis.dispatch("change");
  metadata.dispatch("change");

  assert.deepEqual(calls, [
    "library-chip:System.Text.Json",
    "library-chip:",
    "accessibility:public",
    "accessibility:",
    "library-jump:System.Collections",
    "library-jump:",
    "platform:System.Private.CoreLib:netcore.app",
    // No "platform:System.Runtime:..." entry: a missing pack payload makes no selection
    // rather than substituting one.
    "platform-lens:integrations:System.Net.Http:netcore.app",
    "platform-lens:opportunities:System.Text.Json:undefined",
    "platform-lens:analysis:System.Linq:undefined",
  ]);
});

test("library control binding tolerates an inactive surface", () => {
  assert.doesNotThrow(() => bindLibraryControls(
    fakeDom.parentNode(new FakeRoot()),
    recordingActions([])));
});
