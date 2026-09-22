import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageChangesDataSource,
  packageChangesPackageSets,
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

test("packageChangesPackageSets preserves product order and rejects reconstructed catalogs", () => {
  const descriptors = packageChangesPackageSets({
    version: 1,
    packageSets: [
      { id: "package-set.first", title: "First", summary: "First set", order: 10 },
      { id: "package-set.second", title: "Second", summary: "Second set", order: 20 },
    ],
  });
  assert.deepEqual(
    descriptors.map(descriptor => descriptor.id),
    ["package-set.first", "package-set.second"]);
  assert.throws(() => packageChangesPackageSets({
    version: 2,
    packageSets: descriptors,
  }), /version/);
  assert.throws(() => packageChangesPackageSets({
    version: 1,
    packageSets: [descriptors[1]!, descriptors[0]!],
  }), /product order/);
  assert.throws(() => packageChangesPackageSets({
    version: 1,
    packageSets: [descriptors[0]!, descriptors[0]!],
  }), /repeats/);
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
  const request = createPackageChangesRequest("package-set.example");
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
      createPackageChangesRequest("package-set.example"),
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
    createPackageChangesRequest("package-set.example"),
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
    createPackageChangesRequest("package-set.example"),
    () => {},
    () => {},
    () => {},
    controller.signal);

  assert.deepEqual(result, { kind: "succeeded", inspection: terminal });
});
