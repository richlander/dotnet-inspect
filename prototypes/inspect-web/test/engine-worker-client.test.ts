import assert from "node:assert/strict";
import test from "node:test";

import {
  registerEngineWorkerCloneCandidateOperation,
} from "../src/engine-worker-analysis.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  WorkerOperationCatalog,
  WorkerRuntimeRealm,
} from "../src/worker-runtime-realm.ts";
import type {
  BrowserCloneCandidateRequest,
} from "../src/facades/inspect-web-analysis.d.ts";

class LifecycleDocument extends EventTarget {
  hidden = false;
}

test("a stale startup waiter cannot supersede a newer Clone request", async () => {
  const originalDocument = Object.getOwnPropertyDescriptor(globalThis, "document");
  const originalWindow = Object.getOwnPropertyDescriptor(globalThis, "window");
  const originalWorker = Object.getOwnPropertyDescriptor(globalThis, "Worker");
  const originalSetTimeout = globalThis.setTimeout;
  const invoked: string[] = [];
  let ready: (() => void) | null = null;
  let releaseStartupPoll: (() => void) | null = null;
  let disposeClient: (() => void) | undefined;
  const olderRequest = {
    schemaVersion: 1,
    packages: [{
      packageId: "Example.Package",
      version: "1.2.3",
      targetFramework: "net11.0",
    }],
    selectedPackageIndex: 0,
    assembly: "Example.Package.dll",
    seed: {
      kind: "Library",
      typeDefinitionId: null,
      member: null,
      body: null,
    },
    breadth: "Everything",
    discovery: "SimilarNames",
  } satisfies BrowserCloneCandidateRequest;
  const newerRequest = {
    ...olderRequest,
    discovery: "All",
  } satisfies BrowserCloneCandidateRequest;
  const requests = new Map<string, BrowserCloneCandidateRequest>([
    [JSON.stringify(olderRequest), olderRequest],
    [JSON.stringify(newerRequest), newerRequest],
  ]);
  const releaseHeldStartupPoll = () => {
    if (releaseStartupPoll === null) {
      throw new Error("The startup waiter did not enter its polling delay.");
    }
    releaseStartupPoll();
  };

  class FakeEngineWorker extends EventTarget {
    readonly #realm: WorkerRuntimeRealm<string, string>;
    #heartbeat: ReturnType<typeof setInterval> | undefined;
    #terminated = false;

    constructor() {
      super();
      const operations = new WorkerOperationCatalog();
      registerEngineWorkerCloneCandidateOperation(operations, () => ({
        async queryCloneCandidates(requestJson: string) {
          const request = requests.get(requestJson);
          assert.ok(request);
          const discovery = String(request.discovery);
          invoked.push(discovery);
          await new Promise(resolve => setTimeout(resolve, 20));
          return {
            schemaVersion: 1,
            request,
            kind: "Rejected" as const,
            document: null,
            seedLibrary: null,
            openFailureKind: "InvalidImage" as const,
            failure: null,
            presentationRejectionKind: null,
            subject: null,
            detail: discovery,
            metadataRootReason: null,
          };
        },
      }));
      this.#realm = new WorkerRuntimeRealm({
        bootstrap: {
          decoder: engineWorkerText,
          bootstrap: async () => {
            await new Promise(resolve => setTimeout(resolve, 20));
          },
        },
        diagnostic: engineWorkerDiagnostic,
        unknownOperationRejection: kind => ({
          error: `Unknown Worker operation: ${kind}`,
          diagnostic: `Unknown Worker operation: ${kind}`,
        }),
        operations,
        producerClasses: createEngineWorkerProducerClasses(),
        post: message => {
          if (this.#terminated) return;
          setTimeout(() => {
            if (this.#terminated) return;
            this.dispatchEvent(new MessageEvent("message", { data: message }));
            if (message.kind === "ready") ready?.();
          }, 0);
          if (message.kind === "ready") {
            this.#heartbeat = setInterval(
              () => this.#realm.emitHeartbeat(),
              message.idleHeartbeatIntervalMilliseconds);
          }
        },
      });
    }

    postMessage(message: unknown): void {
      if (this.#terminated) return;
      setTimeout(() => {
        if (!this.#terminated) this.#realm.receive(message);
      }, 0);
    }

    terminate(): void {
      this.#terminated = true;
      clearInterval(this.#heartbeat);
    }
  }

  Object.defineProperty(globalThis, "document", {
    configurable: true,
    value: new LifecycleDocument(),
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: new EventTarget(),
  });
  Object.defineProperty(globalThis, "Worker", {
    configurable: true,
    value: FakeEngineWorker,
  });
  Object.defineProperty(globalThis, "setTimeout", {
    configurable: true,
    value: ((callback: (...args: unknown[]) => void,
      milliseconds?: number,
      ...args: unknown[]) => {
      if (milliseconds === 10 && releaseStartupPoll === null) {
        releaseStartupPoll = () => {
          originalSetTimeout(callback, 0, ...args);
        };
        return 0;
      }
      return originalSetTimeout(callback, milliseconds, ...args);
    }),
  });

  try {
    const { createEngineWorkerCloneCandidateClient } =
      await import("../src/engine-worker-client.ts");
    const client = createEngineWorkerCloneCandidateClient(
      "http://localhost/probe",
      {
        callbacks: {
          failure: () => undefined,
          diagnostic: () => undefined,
          realmReleased: () => undefined,
        },
        operationDiagnostic: () => undefined,
      });
    disposeClient = () => client.dispose();
    const older = client.query(JSON.stringify(olderRequest));
    const workerReady = new Promise<void>(resolve => {
      ready = resolve;
    });
    await workerReady;
    const newer = client.query(JSON.stringify(newerRequest));
    for (let attempt = 0;
      attempt < 20 && invoked.length === 0;
      attempt++) {
      await new Promise(resolve => originalSetTimeout(resolve, 1));
    }
    assert.deepEqual(invoked, ["All"]);
    releaseHeldStartupPoll();

    await assert.rejects(older, /superseded/);
    assert.equal((await newer).detail, "All");
    assert.deepEqual(invoked, ["All"]);
  } finally {
    disposeClient?.();
    restoreGlobal("document", originalDocument);
    restoreGlobal("window", originalWindow);
    restoreGlobal("Worker", originalWorker);
    Object.defineProperty(globalThis, "setTimeout", {
      configurable: true,
      value: originalSetTimeout,
      writable: true,
    });
  }
});

function restoreGlobal(
  name: "document" | "window" | "Worker",
  descriptor: PropertyDescriptor | undefined,
): void {
  if (descriptor) {
    Object.defineProperty(globalThis, name, descriptor);
  } else {
    Reflect.deleteProperty(globalThis, name);
  }
}
