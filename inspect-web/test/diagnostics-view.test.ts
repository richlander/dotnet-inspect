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
  buildIdentity: {
    version: "1.2.3",
    commit: "0123456789abcdef0123456789abcdef01234567",
    builtAtUtc: "2026-01-23T15:41:12Z",
    commitUrl: "https://github.com/richlander/dotnet-inspect/commit/0123456789abcdef0123456789abcdef01234567",
  },
  packageCache: {
    kind: "ready",
    stats: {
      packages: 7,
      resident: 5,
      workspaces: 2,
      residentBytes: 12_582_912,
    },
  },
  capturedAtUtc: "2026-01-23T15:45:00Z",
};

test("Diagnostics renders runtime, build, and package-cache evidence in order", () => {
  const html = diagnosticsViewHtml(ready, escapeHtml);

  assert.ok(html.indexOf(">Runtime startup<") < html.indexOf(">Build<"));
  assert.ok(html.indexOf(">Build<") < html.indexOf(">Package cache<"));
  assert.match(html, /Download/);
  assert.match(html, /1\.01 s/);
  assert.match(html, /Framework assets/);
  assert.match(html, />42</);
  assert.match(html, /0123456789abcdef0123456789abcdef01234567/);
  assert.match(html, /Browser \/ WebAssembly/);
  assert.match(html, /Resident payloads/);
  assert.match(html, /12 MB/);
  assert.match(html, /Startup phase legend/);
  assert.match(html, /Browser Performance API/);
});

test("Diagnostics distinguishes valid zero bytes from unavailable values", () => {
  const html = diagnosticsViewHtml({
    ...ready,
    runtime: {
      kind: "ready",
      message: "Ready.",
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
        workspaces: 0,
        residentBytes: 0,
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
    buildIdentity: null,
    packageCache: {
      kind: "loading",
    },
    capturedAtUtc: "not-a-date",
  }, escapeHtml);

  assert.match(html, /Starting &lt;runtime&gt;\./);
  assert.match(html, /Build identity/);
  assert.match(html, />Unavailable</);
  assert.match(html, /Reading aggregate package-cache statistics\./);
  assert.doesNotMatch(html, />0</);
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

test("Diagnostics binds product Home and Back as distinct route actions", () => {
  const listeners = new Map<string, EventListener>();
  const root = {
    querySelector(selector: string) {
      return selector === "#diagnostics-product" || selector === "#diagnostics-back"
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
    onHome: () => calls.push("home"),
  });
  const event = fakeDom.event({ preventDefault() {} });
  listeners.get("#diagnostics-product")?.(event);
  listeners.get("#diagnostics-back")?.(event);

  assert.deepEqual(calls, ["home", "back"]);
});
