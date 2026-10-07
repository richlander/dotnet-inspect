import assert from "node:assert/strict";
import test from "node:test";
import { createSpotlightEcosystemClassification } from "../src/spotlight-ecosystem.ts";
import type { BrowserEcosystemPackageClassification } from "../src/facades/inspect-web-package.d.ts";
import type { PlatformCatalogTarget } from "../src/platform-index.ts";
import type { SpotlightResult } from "../src/spotlight.ts";

const hits: SpotlightResult[] = [{ kind: "pkg-nuget", hit: { id: "System.Text.Json", version: "9.0.0" }, ranges: [] }];
const target: PlatformCatalogTarget = { tfm: "net10.0", version: "10.0.12", rows: [], supplies: [] };
const annotation: BrowserEcosystemPackageClassification = {
  id: "System.Text.Json", version: "9.0.0", ecosystemId: "ecosystem.runtime",
  ecosystemTitle: ".NET Runtime", platformLayer: "DotNetRuntime", isPruned: true,
};
const tick = () => new Promise<void>(resolve => setImmediate(resolve));

test("pending local classification never delays results and unchanged renders reuse it", async () => {
  let complete!: (values: readonly BrowserEcosystemPackageClassification[]) => void;
  let calls = 0;
  let updates = 0;
  const coordinator = createSpotlightEcosystemClassification({
    classify: () => { calls++; return new Promise(resolve => { complete = resolve; }); },
    updateResults: () => { updates++; },
  });
  assert.deepEqual(coordinator.project(hits, "net10.0", target), hits);
  coordinator.project(hits, "net10.0", target);
  await tick();
  assert.equal(calls, 1);
  complete([annotation]);
  await tick();
  const annotated = coordinator.project(hits, "net10.0", target);
  assert.equal(annotated[0]?.kind, "pkg-nuget");
  if (annotated[0]?.kind === "pkg-nuget") assert.equal(annotated[0].ecosystem?.isPruned, true);
  assert.equal(calls, 1);
  assert.equal(updates, 1);
});

test("catalog arrival and target replacement invalidate pending evidence", async () => {
  const complete: ((values: readonly BrowserEcosystemPackageClassification[]) => void)[] = [];
  const inventories: string[] = [];
  let updates = 0;
  const coordinator = createSpotlightEcosystemClassification({
    classify: (_tfm, _candidates, inventory) => {
      inventories.push(JSON.stringify(inventory));
      return new Promise(resolve => { complete.push(resolve); });
    },
    updateResults: () => { updates++; },
  });
  coordinator.project(hits, "net10.0", null);
  await tick();
  assert.deepEqual(JSON.parse(inventories[0]!), { tfm: "net10.0", version: "", supplies: null });
  coordinator.project(hits, "net10.0", target);
  await tick();
  complete[0]!([annotation]);
  await tick();
  assert.equal(updates, 0);
  coordinator.project(hits, "net9.0", null);
  await tick();
  complete[1]!([annotation]);
  await tick();
  assert.equal(updates, 0);
  complete[2]!([{ ...annotation, isPruned: null, platformLayer: null }]);
  await tick();
  const results = coordinator.project(hits, "net9.0", null);
  if (results[0]?.kind === "pkg-nuget") {
    assert.equal(results[0].ecosystem?.isPruned, null);
    assert.equal(results[0].ecosystem?.traversalTfm, "net9.0");
  }
  assert.equal(updates, 1);
});

test("failure preserves results, is visible, and does not spin on refresh", async () => {
  let calls = 0;
  const coordinator = createSpotlightEcosystemClassification({
    classify: () => { calls++; throw new Error("Worker unavailable"); },
    updateResults: () => {},
  });
  assert.deepEqual(coordinator.project(hits, "net10.0", target), hits);
  await tick();
  assert.match(coordinator.error(), /Worker unavailable/);
  assert.deepEqual(coordinator.project(hits, "net10.0", target), hits);
  await tick();
  assert.equal(calls, 1);
});

test("returning to an earlier batch ignores its superseded success and failure", async () => {
  const pending: { resolve: (values: readonly BrowserEcosystemPackageClassification[]) => void; reject: (error: Error) => void }[] = [];
  let updates = 0;
  const coordinator = createSpotlightEcosystemClassification({
    classify: () => new Promise((resolve, reject) => { pending.push({ resolve, reject }); }),
    updateResults: () => { updates++; },
  });
  const other: SpotlightResult[] = [{ kind: "pkg-nuget", hit: { id: "System.Linq", version: "4.3.0" }, ranges: [] }];
  for (const batch of [hits, other, hits, other, hits]) {
    coordinator.project(batch, "net10.0", target);
    await tick();
  }
  pending[4]!.resolve([annotation]);
  await tick();
  pending[0]!.resolve([{ ...annotation, isPruned: false }]);
  pending[2]!.reject(new Error("superseded Worker failure"));
  await tick();
  const results = coordinator.project(hits, "net10.0", target);
  if (results[0]?.kind === "pkg-nuget") assert.equal(results[0].ecosystem?.isPruned, true);
  assert.equal(coordinator.error(), "");
  assert.equal(updates, 1);
});


test("Library pruning links only positive same-name evidence at the exact layer and target", async () => {
  const library: SpotlightResult = { kind: "framework-lib", assembly: "system.text.json", pack: "netcore.app", tfm: "net10.0", version: "10.0.12", publicTypes: 1, ranges: [] };
  const variants: SpotlightResult[] = [library,
    { ...library, assembly: "System.Text.Json.Nodes" },
    { ...library, pack: "aspnetcore.app" },
    { ...library, tfm: "net9.0" },
    { ...library, version: "10.0.13" },
  ];
  for (const isPruned of [true, false, null]) {
    const coordinator = createSpotlightEcosystemClassification({
      classify: async () => [{ ...annotation, isPruned }], updateResults: () => {},
    });
    const batch = [...hits, ...variants];
    assert.deepEqual(coordinator.project(batch, "net10.0", target), batch);
    await tick();
    const results = coordinator.project(batch, "net10.0", target);
    for (const [index, result] of results.slice(1).entries()) {
      assert.equal(result.kind, "framework-lib");
      if (result.kind === "framework-lib") assert.deepEqual(result.pruning,
        isPruned === true && index === 0
          ? { traversalTfm: "net10.0", platformVersion: "10.0.12" } : undefined);
    }
    const reset = coordinator.project(results, "net9.0", null);
    for (const result of reset) if (result.kind === "framework-lib") assert.equal(result.pruning, undefined);
  }
});
