import assert from "node:assert/strict";
import test from "node:test";
import {
  createPackageCacheStatsRefresh,
} from "../src/package-cache-stats.ts";
import type {
  BrowserPackageCacheStats,
} from "../src/facades/inspect-web-package.d.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => { resolve = complete; });
  return { promise, resolve };
}

function stats(packages: number): BrowserPackageCacheStats {
  return {
    packages,
    resident: packages,
    residentBytes: packages * 1024,
    workspaces: 1,
  };
}

test("delayed package cache statistics publish after updating state", async () => {
  const pending = deferred<BrowserPackageCacheStats>();
  const updates: BrowserPackageCacheStats[] = [];
  const publications: BrowserPackageCacheStats[] = [];
  const refresh = createPackageCacheStatsRefresh({
    load: () => pending.promise,
    update: value => updates.push(value),
    publish: () => publications.push(updates.at(-1)!),
  });

  const completion = refresh.refreshAndPublish();
  assert.equal(updates.length, 0);
  assert.equal(publications.length, 0);

  pending.resolve(stats(2));
  await completion;
  assert.equal(updates[0]?.packages, 2);
  assert.equal(publications[0]?.packages, 2);
});

test("superseded package cache statistics cannot update or publish", async () => {
  const first = deferred<BrowserPackageCacheStats>();
  const second = deferred<BrowserPackageCacheStats>();
  const pending = [first, second];
  const updates: number[] = [];
  let publications = 0;
  const refresh = createPackageCacheStatsRefresh({
    load: () => pending.shift()!.promise,
    update: value => updates.push(value.packages),
    publish: () => publications++,
  });

  const older = refresh.refreshAndPublish();
  const newer = refresh.refreshAndPublish();
  second.resolve(stats(2));
  await newer;
  first.resolve(stats(1));
  await older;

  assert.deepEqual(updates, [2]);
  assert.equal(publications, 1);
});

test("non-publishing package cache refresh updates state without rendering", async () => {
  const updates: number[] = [];
  let publications = 0;
  const refresh = createPackageCacheStatsRefresh({
    load: async () => stats(3),
    update: value => updates.push(value.packages),
    publish: () => publications++,
  });

  await refresh.refresh();

  assert.deepEqual(updates, [3]);
  assert.equal(publications, 0);
});
