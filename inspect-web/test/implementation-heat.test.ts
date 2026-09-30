import assert from "node:assert/strict";
import test from "node:test";
import {
  createTypeHeatCoordinator,
  familyHeatCue,
  familyHeatFor,
  implementationHeatFamilyIsEligible,
  implementationHeatVisibleFamilyIsEligible,
  implementationHeatVisibleFamilyMatchesRequest,
  projectFamilyHeat,
  typeHeatCacheKey,
  type PackageTypeHeatRequest,
  type TypeHeatState,
  type TypeHeatStateHost,
} from "../src/implementation-heat.ts";
import type {
  BrowserImplementationHeatFamily,
  BrowserImplementationHeatMethod,
  BrowserTypeImplementationHeat,
} from "../src/facades/inspect-web-analysis.d.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

const typeId = "type:Example.Widget";

test("heat admits only coherent ordinary or single-declarer extension families", () => {
  const ordinary = {
    name: "Parse",
    kind: "method",
    overloads: [{ accessibility: "public" }, { accessibility: "public" }],
  };
  const extensions = {
    name: "Deserialize",
    kind: "extension-method",
    overloads: [
      {
        accessibility: "public",
        anchorTypeFullName: "System.Text.Json.JsonDocument",
        declaringTypeDefinitionId: "System.Text.Json.JsonSerializer",
      },
      {
        accessibility: "public",
        anchorTypeFullName: "System.Text.Json.JsonDocument",
        declaringTypeDefinitionId: "System.Text.Json.JsonSerializer",
      },
    ],
  };
  const privateExtensions = {
    name: "TryMakeArrayType",
    kind: "extension-method",
    overloads: [
      {
        accessibility: "private",
        anchorTypeFullName: "System.Type",
        declaringTypeDefinitionId:
          "System.Reflection.SignatureTypeExtensions",
      },
      {
        accessibility: "private",
        anchorTypeFullName: "System.Type",
        declaringTypeDefinitionId:
          "System.Reflection.SignatureTypeExtensions",
      },
    ],
  };
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [ordinary, extensions],
      ordinary),
    true);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [ordinary, extensions],
      extensions),
    true);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [extensions],
      { ...extensions, overloads: [extensions.overloads[0]!] }),
    false);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [extensions],
      {
        ...extensions,
        overloads: [
          extensions.overloads[0]!,
          {
            accessibility: "public",
            anchorTypeFullName: "System.Text.Json.JsonDocument",
            declaringTypeDefinitionId: "Example.OtherExtensions",
          },
        ],
      }),
    false);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [ordinary, { ...extensions, name: ordinary.name }],
      ordinary),
    false);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "public",
      [privateExtensions],
      privateExtensions),
    false);
  assert.equal(
    implementationHeatFamilyIsEligible(
      "internal",
      [ordinary],
      ordinary),
    false);
  assert.equal(
    implementationHeatVisibleFamilyIsEligible(
      [privateExtensions],
      privateExtensions),
    true);
  assert.equal(
    implementationHeatVisibleFamilyMatchesRequest(
      ordinary,
      {
        ...ordinary,
        overloads: ordinary.overloads.map(overload => ({
          ...overload,
          accessibility: "private",
        })),
      }),
    true);
  assert.equal(
    implementationHeatVisibleFamilyMatchesRequest(
      extensions,
      {
        ...privateExtensions,
        name: extensions.name,
        overloads: privateExtensions.overloads.map(overload => ({
          ...overload,
          declaringTypeDefinitionId:
            "System.Text.Json.JsonSerializer",
        })),
      }),
    true);
  assert.equal(
    implementationHeatVisibleFamilyMatchesRequest(
      extensions,
      {
        ...privateExtensions,
        name: extensions.name,
        overloads: privateExtensions.overloads.map(overload => ({
          ...overload,
          declaringTypeDefinitionId: "Example.OtherExtensions",
        })),
      }),
    false);
  assert.equal(
    implementationHeatVisibleFamilyMatchesRequest(
      extensions,
      ordinary),
    false);
});

function method(
  metadataToken: number,
  size: number | null,
  overrides: Partial<BrowserImplementationHeatMethod> = {},
): BrowserImplementationHeatMethod {
  return {
    metadataToken,
    isRosterMember: true,
    hasBody: size !== null,
    size,
    isTrivial: size !== null && size <= 8,
    isComplete: true,
    ...overrides,
  };
}

// Run(int) 80 calls Run(string) 5; Run(string) calls nothing.
function family(
  overrides: Partial<BrowserImplementationHeatFamily> = {},
): BrowserImplementationHeatFamily {
  return {
    member: "Run",
    roster: [
      { typeDefinitionId: typeId, stableSelector: "Run(int)", metadataToken: 1 },
      { typeDefinitionId: typeId, stableSelector: "Run(string)", metadataToken: 2 },
    ],
    methods: [method(1, 80), method(2, 5, { isTrivial: false })],
    relationships: [{ callerToken: 1, calleeToken: 2 }],
    unavailableBodies: [],
    analysisDiagnostics: [],
    ...overrides,
  };
}

function heat(
  families: ReadonlyArray<BrowserImplementationHeatFamily> = [family()],
): BrowserTypeImplementationHeat {
  return {
    schemaVersion: 1,
    outcome: "available",
    subject: {
      identity: { name: "Example", version: "1.0.0.0", culture: null, publicKeyToken: null },
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      provenance: {
        kind: "Package",
        packageId: "Example.Package",
        packageVersion: "1.0.0",
        framework: "net11.0",
        frameworkVersion: null,
        runtimeIdentifier: null,
        assetPath: "lib/net11.0/Example.dll",
        resolverSource: null,
        project: null,
        contentRef: null,
        digest: null,
        declaredName: null,
      },
    },
    content: {
      typeDefinitionId: typeId,
      families,
      analysisDiagnostics: [],
      apiSurfaceInspectionFailures: [],
    },
    failure: null,
    share: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: "type-implementation-heat/share",
      reason: "Fixture.",
    },
    diagnostics: [],
    compileLibrary: { status: "Selected", targetFramework: "net11.0", message: null },
  };
}

function request(typeDefinitionId = typeId): PackageTypeHeatRequest {
  return {
    kind: "package",
    workspaceGeneration: "generation",
    packageId: "Example.Package",
    version: "1.0.0",
    targetFramework: "net11.0",
    assemblyName: "Example",
    typeDefinitionId,
  };
}

function visible(
  ...members: ReadonlyArray<readonly [string, number]>
) {
  return members.map(([stableSelector, metadataToken]) => ({
    stableSelector,
    metadataToken,
  }));
}

test("heat tints overloads at or above half the family maximum", () => {
  const projected = projectFamilyHeat(family());
  assert.equal(projected.status, "shown");
  assert.equal(projected.maximum, 80);
  assert.deepEqual(
    projected.overloads.map(overload => overload.heatStrength),
    [1, null],
  );
  assert.equal(
    projected.overloads[0]?.description,
    "80 instructions; 100% of the largest body in this family",
  );
});

test("a hub is called by a same-name method and calls none", () => {
  const projected = projectFamilyHeat(family());
  assert.deepEqual(projected.overloads.map(overload => overload.hub), [false, true]);
  assert.match(
    projected.overloads[1]?.description ?? "",
    /hub called by 1 same-name method$/,
  );
});

test("a bodyless overload is never a hub", () => {
  const projected = projectFamilyHeat(family({
    methods: [method(1, 80), method(2, null, { hasBody: false })],
  }));
  assert.equal(projected.overloads[1]?.hub, false);
});

test("an unlisted implementation sets the maximum and silences listed forwarders", () => {
  const projected = projectFamilyHeat(family({
    methods: [
      method(1, 22),
      method(2, 19, { isTrivial: false }),
      method(3, 464, { isRosterMember: false, isTrivial: false }),
    ],
    relationships: [
      { callerToken: 1, calleeToken: 3 },
      { callerToken: 2, calleeToken: 3 },
    ],
  }));
  assert.equal(projected.status, "shown");
  assert.equal(projected.maximum, 464);
  assert.equal(projected.maximumIsUnlisted, true);
  assert.deepEqual(
    projected.overloads.map(overload => [overload.heatStrength, overload.hub]),
    [[null, false], [null, false]],
  );
  assert.match(
    projected.overloads[0]?.description ?? "",
    /5% of the largest body in this family, which is not a listed overload/,
  );
});

test("an unknown maximum removes heat but keeps complete hubs", () => {
  const unavailable = projectFamilyHeat(family({
    unavailableBodies: [{
      evidenceMethodKey: null,
      methodToken: 9,
      reason: "InvalidBody",
      diagnostic: null,
    }],
  }));
  assert.equal(unavailable.status, "unknown-maximum");
  assert.deepEqual(unavailable.overloads.map(overload => overload.heatStrength), [null, null]);
  assert.equal(unavailable.overloads[1]?.hub, true);

  const incomplete = projectFamilyHeat(family({
    methods: [method(1, 80, { isComplete: false }), method(2, 5, { isTrivial: false })],
  }));
  assert.equal(incomplete.status, "unknown-maximum");
});

test("heat is suppressed for one measured body or all-trivial families", () => {
  assert.equal(
    projectFamilyHeat(family({ methods: [method(1, 80), method(2, null, { hasBody: false })] }))
      .status,
    "suppressed",
  );
  assert.equal(
    projectFamilyHeat(family({ methods: [method(1, 8), method(2, 4)] })).status,
    "suppressed",
  );
  assert.equal(
    projectFamilyHeat(family({
      methods: [method(1, 8), method(2, 4, { isTrivial: false })],
    })).status,
    "shown",
  );
});

test("the record decides eligibility by exact roster", () => {
  const ready: TypeHeatState = {
    status: "ready",
    request: request(),
    isCurrent: () => true,
    families: new Map([["Run", projectFamilyHeat(family())]]),
  };
  assert.notEqual(
    familyHeatFor(
      ready,
      "Run",
      "public",
      visible(["Run(string)", 2], ["Run(int)", 1])),
    null);
  assert.equal(
    familyHeatFor(ready, "Run", "public", visible(["Run(int)", 1])),
    null);
  assert.equal(
    familyHeatFor(
      ready,
      "Run",
      "public",
      visible(["Run(int)", 1], ["Run(other)", 2])),
    null);
  assert.equal(
    familyHeatFor(
      ready,
      "Spin",
      "public",
      visible(["Spin(int)", 1], ["Spin(string)", 2])),
    null);
});

test("non-public rows join exact analyzed MethodDef tokens", () => {
  const projected = projectFamilyHeat(family({
    methods: [
      method(1, 80),
      method(2, 5, { isTrivial: false }),
      method(3, 70, { isRosterMember: false, isTrivial: false }),
      method(4, 12, { isRosterMember: false, isTrivial: false }),
    ],
    relationships: [
      { callerToken: 1, calleeToken: 2 },
      { callerToken: 3, calleeToken: 4 },
    ],
  }));
  const ready: TypeHeatState = {
    status: "ready",
    request: request(),
    isCurrent: () => true,
    families: new Map([["Run", projected]]),
  };

  const privateRows = familyHeatFor(
    ready,
    "Run",
    "private",
    visible(["Run(Guid)", 3], ["Run(DateTime)", 4]));
  assert.notEqual(privateRows, null);
  assert.equal(privateRows?.maximum, 80);
  assert.equal(privateRows?.maximumIsUnlisted, true);
  assert.deepEqual(
    privateRows?.overloads.map(overload =>
      [overload.metadataToken, overload.heatStrength, overload.hub]),
    [[3, Math.sqrt(70 / 80), false], [4, null, true]],
  );
  assert.match(
    privateRows?.overloads[0]?.description ?? "",
    /largest body in this family, which is not a listed overload/,
  );
  assert.equal(
    familyHeatFor(
      ready,
      "Run",
      "private",
      visible(["Run(Guid)", 3], ["Run(Missing)", 9])),
    null);
  assert.equal(
    familyHeatFor(
      ready,
      "Run",
      "private",
      visible(["Run(Guid)", 3], ["Run(int)", 1])),
    null);
});

test("parent-row status text follows the Type request", () => {
  const overloads = visible(["Run(int)", 1], ["Run(string)", 2]);
  assert.equal(
    familyHeatCue({ status: "idle" }, "Run", "public", overloads),
    null);
  assert.deepEqual(
    familyHeatCue(
      { status: "loading", request: request(), isCurrent: () => true },
      "Run",
      "public",
      overloads),
    { text: "measuring", tone: "progress" },
  );
  assert.deepEqual(
    familyHeatCue({
      status: "failed",
      request: request(),
      isCurrent: () => true,
      outcome: "failed",
      message: "boom",
    }, "Run", "public", overloads),
    { text: "heat unavailable", tone: "problem" },
  );
  const incomplete = projectFamilyHeat(family({
    methods: [method(1, 80, { isComplete: false }), method(2, 5)],
  }));
  assert.deepEqual(
    familyHeatCue({
      status: "ready",
      request: request(),
      isCurrent: () => true,
      families: new Map([["Run", incomplete]]),
    }, "Run", "public", overloads),
    { text: "heat incomplete", tone: "problem" },
  );
});

interface Deferred<T> {
  readonly promise: Promise<T>;
  resolve(value: T): void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>(resolve => { resolvePromise = resolve; });
  return { promise, resolve: value => resolvePromise?.(value) };
}

async function turn(): Promise<void> {
  await new Promise(resolve => setTimeout(resolve, 0));
}

function harness(currentType: () => string) {
  const state: TypeHeatStateHost = { typeHeat: { status: "idle" } };
  const queried: string[] = [];
  const results = new Map<string, Deferred<BrowserTypeImplementationHeat>>();
  let idle = deferred<void>();
  const coordinator = createTypeHeatCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: heatRequest => {
      queried.push(heatRequest.typeDefinitionId);
      const result = deferred<BrowserTypeImplementationHeat>();
      results.set(heatRequest.typeDefinitionId, result);
      return result.promise;
    },
    whenWorkerIdle: () => idle.promise,
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  const ask = (typeDefinitionId: string) =>
    coordinator.request(request(typeDefinitionId), () => currentType() === typeDefinitionId);
  return {
    state,
    queried,
    results,
    coordinator,
    ask,
    // An idle Worker stays idle until an ordinary request makes it busy.
    releaseIdle: () => idle.resolve(),
    busy: () => { idle = deferred<void>(); },
  };
}

test("heat waits for the ordinary Worker to go idle and shows measuring meanwhile", async () => {
  const h = harness(() => typeId);
  h.ask(typeId);
  await turn();
  assert.deepEqual(h.queried, []);
  assert.equal(h.state.typeHeat.status, "loading");

  h.releaseIdle();
  await turn();
  assert.deepEqual(h.queried, [typeId]);
  h.results.get(typeId)?.resolve(heat());
  await turn();
  assert.equal(h.state.typeHeat.status, "ready");
});

test("only the latest Type waits, and a departed queued Type is dropped", async () => {
  let current = "type:A";
  const h = harness(() => current);
  h.ask("type:A");
  await turn();
  current = "type:B";
  h.ask("type:B");
  current = "type:C";
  h.ask("type:C");
  h.releaseIdle();
  await turn();
  // A left before it was sent; only the latest requested Type is queried.
  assert.deepEqual(h.queried, ["type:C"]);
});

test("one heat run at a time; the next Type starts after the running one settles", async () => {
  let current = "type:A";
  const h = harness(() => current);
  h.ask("type:A");
  h.releaseIdle();
  await turn();
  assert.deepEqual(h.queried, ["type:A"]);

  current = "type:B";
  h.ask("type:B");
  await turn();
  assert.deepEqual(h.queried, ["type:A"]);

  h.results.get("type:A")?.resolve(heat());
  await turn();
  assert.deepEqual(h.queried, ["type:A", "type:B"]);
  // The departed Type's result settled its cache entry but did not publish.
  assert.notEqual(h.state.typeHeat.status, "ready");
});

test("returning to a Type reuses its settled result without a query", async () => {
  let current = typeId;
  const h = harness(() => current);
  h.ask(typeId);
  h.releaseIdle();
  await turn();
  h.results.get(typeId)?.resolve(heat());
  await turn();
  assert.equal(h.state.typeHeat.status, "ready");

  current = "type:Other";
  h.state.typeHeat = { status: "idle" };
  current = typeId;
  h.ask(typeId);
  await turn();
  assert.equal(h.state.typeHeat.status, "ready");
  assert.deepEqual(h.queried, [typeId]);
});

test("a producer failure stays failed until explicit retry", async () => {
  const h = harness(() => typeId);
  h.ask(typeId);
  h.releaseIdle();
  await turn();
  const failing = h.results.get(typeId);
  assert.ok(failing);
  // Reject by resolving an invalid result: the cache validates it.
  failing.resolve({ ...heat(), schemaVersion: 99 });
  await turn();
  assert.equal(h.state.typeHeat.status, "failed");

  h.ask(typeId);
  await turn();
  assert.equal(h.state.typeHeat.status, "failed");
  assert.deepEqual(h.queried, [typeId]);

  h.busy();
  h.coordinator.retry(request(), () => true);
  await turn();
  assert.deepEqual(h.queried, [typeId]);
  h.releaseIdle();
  await turn();
  assert.deepEqual(h.queried, [typeId, typeId]);
});

test("cache keys separate Types and coordinates", () => {
  assert.notEqual(typeHeatCacheKey(request("type:A")), typeHeatCacheKey(request("type:B")));
  assert.notEqual(
    typeHeatCacheKey(request()),
    typeHeatCacheKey({ ...request(), version: "2.0.0" }),
  );
});

test("settled failed Content keeps its outcome and diagnostics", async () => {
  const h = harness(() => typeId);
  h.ask(typeId);
  h.releaseIdle();
  await turn();
  h.results.get(typeId)?.resolve({
    ...heat(),
    outcome: "rejected",
    content: null,
    failure: {
      kind: "InvalidImage",
      detail: "The image is not a managed assembly.",
      metadataRootReason: null,
    },
    diagnostics: [{
      code: "implementation-profiles.participant-rejected",
      severity: 2,
      summary: "Participant rejected.",
      correspondence: null,
    }],
  });
  await turn();
  const failed = h.state.typeHeat;
  assert.equal(failed.status, "failed");
  if (failed.status !== "failed") return;
  assert.equal(failed.outcome, "rejected");
  assert.equal(failed.message, "The image is not a managed assembly.");
  assert.deepEqual(failed.diagnostics, [
    "implementation-profiles.participant-rejected: Participant rejected.",
  ]);
});

test("a result that settles after the reader leaves never strands measuring", async () => {
  let current = typeId;
  const h = harness(() => current);
  h.ask(typeId);
  h.releaseIdle();
  await turn();
  assert.deepEqual(h.queried, [typeId]);
  current = "type:B";
  h.results.get(typeId)?.resolve(heat());
  await turn();
  current = typeId;
  // The loading state belonged to the departed run; returning finds idle and
  // re-requests, which the settled cache entry serves without a query.
  assert.equal(h.state.typeHeat.status, "idle");
  h.ask(typeId);
  await turn();
  assert.equal(h.state.typeHeat.status, "ready");
  assert.deepEqual(h.queried, [typeId]);
});

test("a queued Type dropped or replaced before it runs releases its measuring", async () => {
  let current = "type:A";
  const h = harness(() => current);
  h.ask("type:A");
  await turn();
  assert.equal(h.state.typeHeat.status, "loading");
  current = "type:B";
  h.releaseIdle();
  await turn();
  assert.deepEqual(h.queried, []);
  assert.equal(h.state.typeHeat.status, "idle");

  h.busy();
  current = "type:C";
  h.ask("type:C");
  current = "type:D";
  h.ask("type:D");
  current = "type:C";
  // C was replaced in the queue by D: the only loading state is D's, which the
  // host shows only while D is current.
  const readState = (): TypeHeatState => h.state.typeHeat;
  const queuedState = readState();
  assert.equal(queuedState.status, "loading");
  if (queuedState.status === "loading")
    assert.equal(queuedState.request.typeDefinitionId, "type:D");
});
