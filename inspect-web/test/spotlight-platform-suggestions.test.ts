import assert from "node:assert/strict";
import test from "node:test";
import { createPlatformSpotlightSuggestions } from "../src/spotlight-platform-suggestions.ts";

function storage() {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
  };
}
const runtime = { assembly: "System.Runtime", pack: "netcore.app" };

test("platform dismissal persists by pack and assembly and explicit reopening restores it", () => {
  const persisted = storage();
  const suggestions = createPlatformSpotlightSuggestions(persisted);
  suggestions.dismiss(runtime);
  const restored = createPlatformSpotlightSuggestions(persisted);
  assert.equal(restored.isDismissed({ ...runtime, assembly: "SYSTEM.RUNTIME" }), true);
  assert.equal(restored.isDismissed({ ...runtime, pack: "aspnetcore.app" }), false);
  restored.remember(runtime);
  assert.equal(createPlatformSpotlightSuggestions(persisted).isDismissed(runtime), false);
});

test("failed persistence leaves platform suggestion state unchanged", () => {
  const persisted = storage();
  let fail = true;
  const suggestions = createPlatformSpotlightSuggestions({
    getItem: persisted.getItem,
    setItem: (key, value) => {
      if (fail) throw new Error("storage unavailable");
      persisted.setItem(key, value);
    },
  });
  assert.throws(() => suggestions.dismiss(runtime), /storage unavailable/);
  assert.equal(suggestions.isDismissed(runtime), false);
  fail = false;
  suggestions.dismiss(runtime);
  fail = true;
  assert.throws(() => suggestions.remember(runtime), /storage unavailable/);
  assert.equal(suggestions.isDismissed(runtime), true);
});
