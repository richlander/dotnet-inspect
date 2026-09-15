import assert from "node:assert/strict";
import test from "node:test";
import { engineWorkerDiagnostic } from "../src/engine-worker-contract.ts";
import {
  diagnosticDetail,
  retainDiagnosticDetail,
} from "../src/failure-detail.ts";

test("worker diagnostics retain the originating Wasm stack", () => {
  const failure = new WebAssembly.RuntimeError("memory access out of bounds");
  failure.stack = [
    "RuntimeError: memory access out of bounds",
    "    at wasm://wasm/0123456a:wasm-function[18442]:0x4f22bc",
  ].join("\n");

  assert.equal(engineWorkerDiagnostic(failure), failure.stack);
});

test("later cleanup failures do not replace the originating diagnostic", () => {
  const runtimeFailure = new WebAssembly.RuntimeError(
    "memory access out of bounds",
  );
  runtimeFailure.stack = [
    "RuntimeError: memory access out of bounds",
    "    at wasm://wasm/0123456a:wasm-function[18442]:0x4f22bc",
  ].join("\n");
  const cleanupFailure = new Error("Assert failed: The runtime is not running.");
  cleanupFailure.stack = [
    "Error: Assert failed: The runtime is not running.",
    "    at engine-worker-client.js:1:74861",
  ].join("\n");

  const retained = retainDiagnosticDetail(
    diagnosticDetail(runtimeFailure),
    cleanupFailure,
  );
  assert.match(retained, /^RuntimeError: memory access out of bounds/u);
  assert.match(retained, /wasm-function\[18442\]/u);
  assert.match(retained, /Subsequent failure:/u);
  assert.match(retained, /runtime is not running/u);
});
