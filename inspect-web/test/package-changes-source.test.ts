import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageChangesDataSource,
  packageChangesEcosystems,
  type BrowserPackageChangesEngine,
} from "../src/package-changes-source.ts";
import { createPackageChangesRequest } from "../src/package-changes.ts";
import { changeRow, failure, inspection, progress } from "./package-changes-fixture.ts";

function publish(sink: unknown, event: object): void {
  if (typeof sink !== "object" || sink === null) {
    throw new TypeError("Expected an event sink object.");
  }
  Reflect.set(sink, "event", JSON.stringify(event));
}

test("packageChangesEcosystems preserves product order and rejects reconstructed catalogs", () => {
  const descriptors = packageChangesEcosystems({
    version: 1,
    ecosystems: [
      {
        id: "ecosystem.first",
        title: "First",
        summary: "First ecosystem",
        order: 10,
        prefixes: ["First."],
      },
      {
        id: "ecosystem.second",
        title: "Second",
        summary: "Second ecosystem",
        order: 20,
        prefixes: ["Second.", "SecondToo."],
      },
    ],
  });
  assert.deepEqual(
    descriptors.map(descriptor => [descriptor.id, descriptor.prefixes]),
    [
      ["ecosystem.first", ["First."]],
      ["ecosystem.second", ["Second.", "SecondToo."]],
    ]);
  assert.throws(() => packageChangesEcosystems({
    version: 2,
    ecosystems: descriptors,
  }), /version/);
  assert.throws(() => packageChangesEcosystems({
    version: 1,
    ecosystems: [],
  }), /at least one/);
  assert.throws(() => packageChangesEcosystems({
    version: 1,
    ecosystems: [descriptors[1]!, descriptors[0]!],
  }), /product order/);
  assert.throws(() => packageChangesEcosystems({
    version: 1,
    ecosystems: [descriptors[0]!, descriptors[0]!],
  }), /repeats/);
  for (const prefixes of [
    [],
    [" "],
    ["First.", "First."],
    Array.from({ length: 17 }, (_, index) => `First${index}.`),
  ]) {
    assert.throws(() => packageChangesEcosystems({
      version: 1,
      ecosystems: [{ ...descriptors[0]!, prefixes }],
    }), /invalid descriptor/);
  }
});

test("source drains nonterminal events in callback order before terminal success", async () => {
  const row = changeRow("Example.Package");
  let receivedRequest:
    ReturnType<typeof createPackageChangesRequest> | null = null;
  const engine: BrowserPackageChangesEngine = {
    cancel() {},
    async run(_operationId, request, sink) {
      receivedRequest = request;
      publish(sink, {
        kind: "Progress", progress, row: null, failure: null,
      });
      publish(sink, {
        kind: "Row", progress: null, row, failure: null,
      });
      publish(sink, {
        kind: "Failure", progress: null, row: null, failure,
      });
      return {
        version: 1,
        kind: "Succeeded",
        inspection: inspection([row], [failure]),
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
  };
  const source = createBrowserPackageChangesDataSource(engine, {
    createOperationId: () => "changes-1",
  });
  const observed: string[] = [];
  const request = createPackageChangesRequest("ecosystem.example");
  const result = await source.run(
    request,
    item => observed.push(`progress:${item.phase}`),
    item => observed.push(`row:${item.catalogActivity.packageId}`),
    item => observed.push(`failure:${item.provider}`),
    new AbortController().signal);

  assert.deepEqual(observed, [
    "progress:Catalog",
    "row:Example.Package",
    "failure:Advisory",
  ]);
  assert.deepEqual(receivedRequest, request);
  assert.equal(result.kind, "succeeded");
});

test("malformed callback events fail the observer and cancel managed work", async () => {
  const cancellations: string[] = [];
  const engine: BrowserPackageChangesEngine = {
    cancel(_operationId, reason) { cancellations.push(reason); },
    async run(_operationId, _json, sink) {
      publish(sink, {
        kind: "Row", progress: null, row: null, failure: null,
      });
      await Promise.resolve();
      return {
        version: 1,
        kind: "Succeeded",
        inspection: inspection([]),
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
  };
  const source = createBrowserPackageChangesDataSource(engine, {
    createOperationId: () => "changes-malformed",
  });

  await assert.rejects(
    source.run(
      createPackageChangesRequest("ecosystem.example"),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /does not match its kind/);
  assert.deepEqual(cancellations, ["feature-observer-failed"]);
});

test("abort requests managed cancellation and reports physical cancellation", async () => {
  const controller = new AbortController();
  const cancellations: string[] = [];
  const engine: BrowserPackageChangesEngine = {
    cancel(_operationId, reason) { cancellations.push(reason); },
    async run() {
      await new Promise<void>(resolve =>
        controller.signal.addEventListener("abort", () => resolve(), {
          once: true,
        }));
      return {
        version: 1,
        kind: "Canceled",
        inspection: null,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: "disposed",
      };
    },
  };
  const source = createBrowserPackageChangesDataSource(engine, {
    createOperationId: () => "changes-cancel",
  });
  const pending = source.run(
    createPackageChangesRequest("ecosystem.example"),
    () => {},
    () => {},
    () => {},
    controller.signal);
  controller.abort("disposed");

  assert.deepEqual(await pending, { kind: "canceled", reason: "disposed" });
  assert.deepEqual(cancellations, ["disposed"]);
});

test("a successful physical settlement wins a cancellation race", async () => {
  const controller = new AbortController();
  const terminal = inspection([changeRow("Terminal.Package")]);
  const engine: BrowserPackageChangesEngine = {
    cancel() {},
    async run() {
      controller.abort("user");
      return {
        version: 1,
        kind: "Succeeded",
        inspection: terminal,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
  };
  const source = createBrowserPackageChangesDataSource(engine, {
    createOperationId: () => "changes-success-race",
  });

  const result = await source.run(
    createPackageChangesRequest("ecosystem.example"),
    () => {},
    () => {},
    () => {},
    controller.signal);

  assert.deepEqual(result, { kind: "succeeded", inspection: terminal });
});
