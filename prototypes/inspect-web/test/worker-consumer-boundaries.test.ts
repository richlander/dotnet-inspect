import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import {
  buildWorkspaceStateUrlAsync,
  createAsyncWorkspaceLocationPersistence,
  type WorkspaceUrlState,
} from "../src/workspace-navigation.ts";
import type { BrowserWorkspaceShareEncodeResult } from "../src/facades/inspect-web-catalog.d.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

function workspace(): WorkspaceUrlState {
  return {
    package: "Example", subject: "workspace",
    tabs: [{ id: "t0", kind: "package", source: "Example", version: "1.0.0", framework: "net11.0", runtimeIdentifier: null }],
    contexts: [{ id: "g0", tabIds: ["t0"] }], activeTabId: "t0", selectedContextId: "g0",
    view: { lens: null, type: null, memberAnchor: null, memberSignature: null, section: null, libraries: [] },
  };
}

test("async share encoding captures the clicked coordinates before awaiting the Worker", async () => {
  const encoded = deferred<BrowserWorkspaceShareEncodeResult>();
  const state = workspace();
  const result = buildWorkspaceStateUrlAsync("https://inspect.example/", state, () => encoded.promise);
  state.package = "New";
  state.subject = null;
  encoded.resolve({ succeeded: true, packet: "original", failure: null });
  const url = await result;
  assert.equal(url.searchParams.get("package"), "Example");
  assert.equal(url.searchParams.get("w"), "original");
  assert.equal(url.hash, "#workspace");
});

test("asynchronous address-bar persistence cannot overwrite a later navigation", async () => {
  const encoded = deferred<BrowserWorkspaceShareEncodeResult>();
  let location = new URL("https://inspect.example/?package=Example");
  const writes: string[] = [];
  const persistence = createAsyncWorkspaceLocationPersistence({
    current: () => ({
      href: location.href, pathname: location.pathname, search: location.search, hash: location.hash,
    }),
    replace(url) { writes.push(url); location = new URL(url, location); },
    push(url) { writes.push(url); location = new URL(url, location); },
    encode: () => encoded.promise,
    decode: async () => ({ succeeded: false, state: null, failure: null }),
  });
  const sync = persistence.sync(workspace());
  persistence.push("/demos");
  encoded.resolve({ succeeded: true, packet: "stale", failure: null });
  await sync;
  assert.deepEqual(writes, ["/demos"]);
});

test("production Share requests clipboard permission before its Worker result resolves", async () => {
  const source = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
  const share = source.match(/async function share\(\) \{[\s\S]*?\n}/)?.[0];
  assert.ok(share);
  const url = deferred<URL>();
  const copied: string[] = [];
  const notices: string[] = [];
  let requested = false;
  class Item {
    readonly data: Record<string, Promise<Blob>>;
    constructor(data: Record<string, Promise<Blob>>) { this.data = data; }
  }
  const operation: unknown = runInNewContext(stripTypeScriptTypes(`${share}\nshare();`), {
    Blob, ClipboardItem: Item,
    buildStateUrl: () => url.promise,
    document: {},
    captureApplicationMenuFocusOwner: () => null,
    navigator: {
      clipboard: {
        async write(items: Item[]) {
          requested = true;
          const blob = await items[0]!.data["text/plain"];
          assert.ok(blob);
          copied.push(await blob.text());
        },
      },
    },
    showToast: (message: string) => { notices.push(message); },
    requestAnimationFrame: () => 0,
    render: () => assert.fail("Share unexpectedly failed."),
    state: {}, errorMessage: String,
  });
  assert.equal(requested, true);
  assert.deepEqual(notices, []);
  url.resolve(new URL("https://inspect.example/?w=clicked"));
  await operation;
  assert.deepEqual(copied, ["https://inspect.example/?w=clicked"]);
  assert.deepEqual(notices, ["selection link copied"]);
});
