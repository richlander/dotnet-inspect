import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

test("Library Overview uses the shared Name request for Package and Platform Libraries", async () => {
  const load = sourceText(functionDeclaration("loadLibraryApi"));
  const lower = sourceText(functionDeclaration("libraryInspectionRequest"));
  for (const [pkg, library, selector] of [
    [
      { id: "Newtonsoft.Json", version: "13.0.4", activeFramework: "net6.0" },
      { id: "compile:lib/net6.0/Newtonsoft.Json.dll", name: "Newtonsoft.Json" },
      { kind: "Package", package: {
        packageId: "Newtonsoft.Json", version: "13.0.4", targetFramework: "net6.0",
        assemblyId: "compile:lib/net6.0/Newtonsoft.Json.dll",
      }, platform: null },
    ],
    [
      { id: "microsoft.netcore.app", version: "11.0.0-rc.1.26425.128",
        activeFramework: "net11.0", isRuntimePack: true },
      { id: '["netcore.app","System.Text.Json.dll"]',
        name: "System.Text.Json", platformPack: "netcore.app" },
      { kind: "Platform", package: null, platform: {
        targetFramework: "net11.0", platformVersion: "11.0.0-rc.1.26425.128",
        assemblyFileName: "System.Text.Json.dll", pack: "netcore.app",
      } },
    ],
  ]) {
    const calls: unknown[] = [];
    const context = {
      state: {
        rootKind: "package",
        libraryApiInspections: new Map(),
        libraryApiLoads: new Set(),
        libraryApiErrors: new Map(),
        package: null,
      },
      libraryApiSignature: () => "library",
      inspectLibraryDocument: (request: unknown) => {
        calls.push(JSON.parse(JSON.stringify(request)));
        return Promise.resolve({ outcome: "Available" });
      },
    };
    await runInNewContext(stripTypeScriptTypes(`(async () => {
      ${lower}
      ${load}
      await loadLibraryApi(${JSON.stringify(pkg)}, ${JSON.stringify(library)});
    })()`), context);
    assert.deepEqual(calls, [{
      library: selector,
      plan: { enablements: false, typeNames: true },
    }]);
    assert.equal(context.state.libraryApiLoads.size, 0);
    assert.equal(context.state.libraryApiInspections.has("library"), true);
  }
});

test("Library Name loading includes Platform but leaves uploaded Libraries on their existing route", () => {
  const load = sourceText(functionDeclaration("maybeAutoLoadLibraryApi"));
  for (const rootKind of ["package", "platform", "library"]) {
    let calls = 0;
    const context = {
      state: {
        rootKind,
        atLibraryRoot: true,
        libraryLens: "overview",
        package: { isRuntimePack: rootKind === "platform" },
        libraryApiInspections: new Map(),
        libraryApiLoads: new Set(),
        libraryApiErrors: new Map(),
      },
      selectedLibrary: () => ({ id: "library", name: "System.Text.Json" }),
      libraryApiSignature: () => "library",
      loadLibraryApi: () => { calls++; },
      observeAsync: () => undefined,
    };
    runInNewContext(stripTypeScriptTypes(`${load}; maybeAutoLoadLibraryApi();`), context);
    assert.equal(calls, rootKind === "library" ? 0 : 1);
  }
});
