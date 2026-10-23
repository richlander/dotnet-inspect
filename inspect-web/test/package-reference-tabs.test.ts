import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "../src/workbench-keybindings.ts";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";
import { fakeDom } from "./fake-dom.ts";

test("Package References tab navigation uses modal-aware keybinding priority", () => {
  const keybindings = new KeybindingRegistry();
  let focused = 0;
  let modal = false;
  const tabs = ["dependencies", "hierarchy"].map((lens, index) =>
    Object.assign(new EventTarget(), {
      dataset: { packageReferencesTab: lens },
      tabIndex: index === 0 ? 0 : -1,
      focus: () => { focused = index; },
    }));
  runInNewContext(stripTypeScriptTypes(
    `${sourceText(functionDeclaration("bindPackageReferencesTabs"))}
`
      + "bindPackageReferencesTabs();"), {
    document: { querySelectorAll: () => tabs },
    keybindings,
    WORKBENCH_KEYBINDING_PRIORITY,
    workspaceKeyboardContextIsActive: () => !modal,
  });
  const press = (key: string) => keybindings.dispatch(fakeDom.keyboardEvent({
    key,
    target: tabs[focused],
    composedPath: () => [tabs[focused]!],
    altKey: false, ctrlKey: false, metaKey: false, shiftKey: false,
    preventDefault: () => {},
  }));
  for (const [key, expected] of [
    ["ArrowRight", 1], ["ArrowRight", 0], ["ArrowLeft", 1],
    ["Home", 0], ["End", 1],
  ] as const) {
    assert.equal(press(key).bindingId, "package-references-tab.navigate");
    assert.equal(focused, expected);
    assert.deepEqual(tabs.map(tab => tab.tabIndex),
      focused === 0 ? [0, -1] : [-1, 0]);
  }
  keybindings.register({
    id: "modal.navigation",
    key: "ArrowLeft",
    priority: WORKBENCH_KEYBINDING_PRIORITY.settings,
    when: () => modal,
    run: () => true,
  });
  modal = true;
  assert.equal(press("ArrowLeft").bindingId, "modal.navigation");
  assert.equal(focused, 1);
});
