import assert from "node:assert/strict";
import test from "node:test";
import {
  bindDiagnosticsView,
  diagnosticsViewHtml,
  type DiagnosticsViewModel,
} from "../src/diagnostics-view.ts";
import { fakeDom } from "./fake-dom.ts";

const escapeHtml = (value: string) => value
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;")
  .replaceAll("'", "&#039;");

const ready: DiagnosticsViewModel = {
  runtime: {
    kind: "ready",
    message: ".NET is running in this browser.",
    measurementsPending: false,
    diagnostics: {
      downloadMs: 1010,
      startupMs: 522,
      precomputeMs: 308,
      totalMs: 1840,
      transfer: 3_100_000,
      decoded: 9_800_000,
      assets: 42,
    },
  },
  build: {
    kind: "ready",
    identity: {
      version: "1.2.3",
      commit: "0123456789abcdef0123456789abcdef01234567",
      builtAtUtc: "2026-01-23T15:41:12Z",
      commitUrl: "https://github.com/richlander/dotnet-inspect/commit/0123456789abcdef0123456789abcdef01234567",
    },
  },
  packageCache: {
    kind: "ready",
    stats: {
      packages: 7,
      resident: 5,
      maxPackageEntries: 256,
      workspaces: 2,
      maxWorkspaces: 4,
      maxWorkspaceAssembliesPerRole: 256,
      residentBytes: 12_582_912,
      maxResidentBytes: 134_217_728,
      maxWorkspaceRetainedImageBytes: 67_108_864,
    },
  },
  capturedAtUtc: "2026-01-23T15:45:00Z",
};

test("Diagnostics renders runtime, build, and isolated-storage evidence in order", () => {
  const html = diagnosticsViewHtml(ready, escapeHtml);

  assert.match(
    html,
    /id="diagnostics-product"[\s\S]*aria-label="dotnet-inspect navigation"/);
  assert.doesNotMatch(html, /aria-label="dotnet-inspect workspace"/);
  assert.match(html, /aria-label="Back to previous page"/);
  assert.match(html, /&larr; Back/);
  assert.ok(html.indexOf(">Runtime startup<") < html.indexOf(">Build<"));
  assert.ok(html.indexOf(">Build<") < html.indexOf(">Isolated storage<"));
  assert.match(html, /Download/);
  assert.match(html, /1\.01 s/);
  assert.match(html, /Framework assets/);
  assert.match(html, />42</);
  assert.match(html, /0123456789abcdef0123456789abcdef01234567/);
  assert.match(html, /id="diagnostics-commit"/);
  assert.match(html, /Browser \/ WebAssembly/);
  assert.match(html, /Resident payloads[\s\S]*>5</);
  assert.match(html, /Package-entry budget[\s\S]*>256</);
  assert.match(html, /Resident bytes[\s\S]*12 MB of 128 MB/);
  assert.match(html, /Workspace slots[\s\S]*2 of 4/);
  assert.match(html, /Workspace assembly budget[\s\S]*256 per role/);
  assert.match(html, /Workspace image budget[\s\S]*64 MB each/);
  assert.match(html, /Startup phase legend/);
  assert.match(html, /Browser Performance API/);
});

test("Diagnostics distinguishes valid zero bytes from unavailable values", () => {
  const html = diagnosticsViewHtml({
    ...ready,
    runtime: {
      kind: "ready",
      message: "Ready.",
      measurementsPending: false,
      diagnostics: {
        downloadMs: 0,
        startupMs: 0,
        precomputeMs: 0,
        totalMs: 0,
        transfer: 0,
        decoded: 0,
        assets: 0,
      },
    },
    packageCache: {
      kind: "ready",
      stats: {
        packages: 0,
        resident: 0,
        maxPackageEntries: 256,
        workspaces: 0,
        maxWorkspaces: 4,
        maxWorkspaceAssembliesPerRole: 256,
        residentBytes: 0,
        maxResidentBytes: 134_217_728,
        maxWorkspaceRetainedImageBytes: 67_108_864,
      },
    },
  }, escapeHtml);

  assert.match(html, /0 B/);
  assert.match(html, />0</);
});

test("Diagnostics keeps loading and unavailable data visible without zeroes", () => {
  const html = diagnosticsViewHtml({
    runtime: {
      kind: "loading",
      message: "Starting <runtime>.",
    },
    build: {
      kind: "loading",
    },
    packageCache: {
      kind: "loading",
    },
    capturedAtUtc: "not-a-date",
  }, escapeHtml);

  assert.match(html, /Starting &lt;runtime&gt;\./);
  assert.match(html, /Reading product build identity\./);
  assert.match(html, /Captured Unavailable/);
  assert.match(html, /Reading aggregate package-cache statistics\./);
  assert.doesNotMatch(html, />0</);
});

test("Diagnostics reports runtime readiness while startup measurements finish", () => {
  const html = diagnosticsViewHtml({
    ...ready,
    runtime: {
      kind: "ready",
      message: "Ready.",
      diagnostics: null,
      measurementsPending: true,
    },
  }, escapeHtml);

  assert.match(html, /Browser inspection engine ready/);
  assert.match(html, /Finishing startup measurements\./);
  assert.doesNotMatch(
    html,
    /Startup measurements are unavailable for this session\./);
});

test("Diagnostics renders unavailable framework evidence and build failure", () => {
  const html = diagnosticsViewHtml({
    ...ready,
    runtime: {
      kind: "ready",
      message: "Ready.",
      measurementsPending: false,
      diagnostics: {
        downloadMs: null,
        startupMs: 522,
        precomputeMs: 308,
        totalMs: 830,
        transfer: null,
        decoded: null,
        assets: null,
      },
    },
    build: {
      kind: "failed",
      message: "Build <identity> unavailable.",
    },
  }, escapeHtml);

  assert.match(html, /Download[\s\S]*Unavailable/);
  assert.match(html, /Framework assets[\s\S]*Unavailable/);
  assert.match(html, /Transferred[\s\S]*Unavailable/);
  assert.match(html, /Decoded[\s\S]*Unavailable/);
  assert.match(html, /Build &lt;identity&gt; unavailable\./);
});

test("Diagnostics renders explicit runtime and cache failures with escaped detail", () => {
  const html = diagnosticsViewHtml({
    ...ready,
    runtime: {
      kind: "failed",
      message: "Browser inspection engine failed.",
      detail: "<script>alert(1)</script>",
    },
    packageCache: {
      kind: "failed",
      message: "Cache <offline>.",
    },
  }, escapeHtml);

  assert.match(html, /diagnostics-runtime-failed/);
  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.match(html, /Cache &lt;offline&gt;\./);
  assert.doesNotMatch(html, /<script>/);
});

test("Diagnostics binds Back as its route action", () => {
  const listeners = new Map<string, EventListener>();
  const root = {
    querySelector(selector: string) {
      return selector === "#diagnostics-back"
        ? {
            addEventListener(_type: string, listener: EventListener) {
              listeners.set(selector, listener);
            },
          }
        : null;
    },
  };
  const calls: string[] = [];

  bindDiagnosticsView(fakeDom.parentNode(root), {
    onBack: () => calls.push("back"),
  });
  const event = fakeDom.event({ preventDefault() {} });
  listeners.get("#diagnostics-back")?.(event);

  assert.deepEqual(calls, ["back"]);
});
