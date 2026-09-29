import assert from "node:assert/strict";
import test from "node:test";
import type { BrowserLibrarySurfaceLeverage } from "../src/facades/inspect-web-analysis.d.ts";
import {
  createTypeLeverageCoordinator,
  projectTypeLeverage,
  typeLeverageMatchesFilter,
} from "../src/type-leverage.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import type { TypeLeverageLoadState } from "../src/type-leverage.ts";

const result: BrowserLibrarySurfaceLeverage = {
  schemaVersion: 1,
  outcome: "available",
  methodologyVersion: "type-leverage.v2",
  disposition: "complete",
  coverage: {
    considered: 8,
    examined: 8,
    unavailable: 0,
    limited: 0,
  },
  types: [
    {
      typeDefinitionId: "Example.Sea",
      typeDisplay: "Example.Sea",
      rankingEligible: true,
      signatureIncomingDegree: 8,
      signatureOutgoingDegree: 1,
      role: "foundation",
    },
    {
      typeDefinitionId: "Example.Both",
      typeDisplay: "Example.Both",
      rankingEligible: true,
      signatureIncomingDegree: 4,
      signatureOutgoingDegree: 6,
      role: "hub",
    },
    {
      typeDefinitionId: "Example.Peak",
      typeDisplay: "Example.Peak",
      rankingEligible: true,
      signatureIncomingDegree: 1,
      signatureOutgoingDegree: 10,
      role: "orchestrator",
    },
    {
      typeDefinitionId: "Example.Noise",
      typeDisplay: "Example.Noise",
      rankingEligible: true,
      signatureIncomingDegree: 3,
      signatureOutgoingDegree: 4,
      role: "hub",
    },
  ],
  seaLevelOrder: [
    "Example.Sea",
    "Example.Both",
    "Example.Noise",
    "Example.Peak",
  ],
  mountainPeakOrder: [
    "Example.Peak",
    "Example.Both",
    "Example.Noise",
    "Example.Sea",
  ],
  diagnostics: [],
  failure: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net11.0",
    message: null,
  },
};

test("relative categories preserve overlap and suppress sub-half noise", () => {
  const projection = projectTypeLeverage(result);
  const sea = projection.byType.get("Example.Sea");
  const both = projection.byType.get("Example.Both");

  assert.equal(projection.seaLevelCount, 2);
  assert.equal(projection.mountainPeakCount, 2);
  assert.equal(sea?.seaLevel, true);
  assert.equal(sea?.mountainPeak, false);
  assert.equal(both?.seaLevel, true);
  assert.equal(both?.mountainPeak, true);
  assert.equal(projection.byType.has("Example.Noise"), false);
  assert.equal(
    typeLeverageMatchesFilter(both, "sea-level"),
    true,
  );
  assert.equal(
    typeLeverageMatchesFilter(sea, "mountain-peak"),
    false,
  );
});

test("zero-degree orders do not create presentation categories", () => {
  const projection = projectTypeLeverage({
    ...result,
    types: [{
      ...result.types[0]!,
      signatureIncomingDegree: 0,
      signatureOutgoingDegree: 0,
    }],
    seaLevelOrder: ["Example.Sea"],
    mountainPeakOrder: ["Example.Sea"],
  });

  assert.equal(projection.byType.size, 0);
  assert.equal(projection.seaLevelCount, 0);
  assert.equal(projection.mountainPeakCount, 0);
});

test("malformed exact-identity orders fail visibly", () => {
  assert.throws(
    () => projectTypeLeverage({
      ...result,
      seaLevelOrder: ["Example.Missing"],
    }),
    /ineligible Type 'Example\.Missing'/,
  );
});

test("qualified evidence and diagnostics remain visible to presentation", () => {
  const projection = projectTypeLeverage({
    ...result,
    disposition: "Qualified",
    coverage: {
      considered: 8,
      examined: 6,
      unavailable: 1,
      limited: 1,
    },
    diagnostics: ["SIG001: two signatures were unavailable"],
  });

  assert.equal(projection.disposition, "Qualified");
  assert.deepEqual(projection.coverage, {
    considered: 8,
    examined: 6,
    unavailable: 1,
    limited: 1,
  });
  assert.deepEqual(
    projection.diagnostics,
    ["SIG001: two signatures were unavailable"],
  );
});

test("operation authority suppresses stale publication and retains its cache", async () => {
  interface Request {
    readonly key: string;
  }
  const pending = new Map<
    string,
    (value: BrowserLibrarySurfaceLeverage) => void
  >();
  const published: TypeLeverageLoadState[] = [];
  let current = "A";
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    query: request => new Promise(resolve => pending.set(request.key, resolve)),
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: state => published.push(state),
  });

  coordinator.request({ key: "A" });
  current = "B";
  coordinator.request({ key: "B" });
  pending.get("A")?.(result);
  await Promise.resolve();
  pending.get("B")?.(result);
  await Promise.resolve();

  assert.deepEqual(
    published.map(state => `${state.status}:${state.key}`),
    ["loading:A", "loading:B", "ready:B"],
  );

  current = "A";
  coordinator.request({ key: "A" });
  assert.equal(published.at(-1)?.status, "ready");
  assert.equal(published.at(-1)?.key, "A");
});
