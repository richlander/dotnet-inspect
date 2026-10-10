import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

test("Overview declares Enablements with API only while that companion is needed", async () => {
  const load = sourceText(functionDeclaration("loadLibraryApi"));
  for (const [hasEnablements, hasRequest, expected] of [
    [false, true, true],
    [true, true, false],
    [false, false, false],
  ]) {
    const calls: unknown[][] = [];
    const context = {
      state: {
        libraryApiInspections: new Map(),
        libraryApiLoads: new Set(),
        libraryApiErrors: new Map(),
        libraryEnablements: new Map(hasEnablements ? [["library", {}]] : []),
        package: null,
      },
      libraryApiSignature: () => "library",
      libraryEnablementsRequest: () => hasRequest ? { plan: { enablements: true } } : null,
      inspectLibraryApi: (...args: unknown[]) => {
        calls.push(args);
        return Promise.resolve({ content: {} });
      },
    };
    await runInNewContext(stripTypeScriptTypes(`(async () => {
      ${load}
      await loadLibraryApi({ id: "Newtonsoft.Json", version: "13.0.4", activeFramework: "net6.0" },
        { id: "compile:lib/net6.0/Newtonsoft.Json.dll", name: "Newtonsoft.Json" });
    })()`), context);
    assert.deepEqual(calls, [["Newtonsoft.Json", "13.0.4", "net6.0",
      "compile:lib/net6.0/Newtonsoft.Json.dll", expected]]);
    assert.equal(context.state.libraryApiLoads.size, 0);
    assert.equal(context.state.libraryApiInspections.has("library"), true);
  }
});
