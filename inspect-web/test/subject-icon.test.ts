import assert from "node:assert/strict";
import test from "node:test";
import { createSubjectIconLoader } from "../src/subject-icon.ts";
import type { AppPackage } from "../src/package-acquisition.ts";

test("icon waits for paint and repeated renders share one request", async () => {
  const pkg: Pick<AppPackage, "id" | "version" | "icon"> = { id: "Example", version: "1", icon: null };
  let paint!: () => void;
  let queries = 0;
  let applied = 0;
  const icon = { mediaType: "image/png", base64: "AA==" };
  const load = createSubjectIconLoader({ current: () => pkg,
    afterPaint: () => new Promise<void>(resolve => { paint = resolve; }),
    query: async () => { queries++; return { status: "Available", packageId: pkg.id, packageVersion: pkg.version, icon }; },
    apply: () => { applied++; },
  });
  const first = load(pkg);
  assert.equal(load(pkg), first);
  assert.equal(queries, 0);
  paint(); await first;
  assert.equal(queries, 1); assert.equal(applied, 1); assert.equal(pkg.icon, icon);
});

test("late icon does not update a different package header", async () => {
  const pkg = { id: "Example", version: "1", icon: null };
  let current: Pick<AppPackage, "id" | "version" | "icon"> | null = pkg;
  let finish!: () => void;
  let applied = 0;
  const load = createSubjectIconLoader({ current: () => current, afterPaint: async () => {},
    query: async () => { await new Promise<void>(resolve => { finish = resolve; }); return {
      status: "Available", packageId: pkg.id, packageVersion: pkg.version,
      icon: { mediaType: "image/png", base64: "AA==" } }; },
    apply: () => { applied++; },
  });
  const request = load(pkg); await Promise.resolve();
  current = { id: "Other", version: "2", icon: null }; finish(); await request;
  assert.equal(applied, 0); assert.equal(current.icon, null);
});

test("optional icon failure retains default without repeated retries", async () => {
  const pkg = { id: "Example", version: "1", icon: null };
  let queries = 0;
  const load = createSubjectIconLoader({ current: () => pkg, afterPaint: async () => {},
    query: async () => { queries++; throw new Error("unavailable"); },
    apply: () => assert.fail("must retain default"),
  });
  await load(pkg); await load(pkg);
  assert.equal(queries, 1); assert.equal(pkg.icon, null);
});
