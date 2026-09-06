import assert from "node:assert/strict";
import test from "node:test";
import { createMainThreadStartupClient } from "../src/engine-startup.ts";
import type { BrowserBuildIdentity } from "../src/facades/inspect-web-host.d.ts";
import type {
  BrowserHomeDemoCatalog,
  BrowserVocabularyDocument,
} from "../src/facades/inspect-web-catalog.d.ts";
import type {
  BrowserGalleryDiscoveryCatalog,
  BrowserPackageQueryFacetCatalog,
} from "../src/facades/inspect-web-package.d.ts";

const identity: BrowserBuildIdentity = {
  version: "1.0.0",
  commit: null,
  builtAtUtc: null,
  commitUrl: null,
};
const vocabulary: BrowserVocabularyDocument = {
  schema_version: 1,
  sections: [],
};
const demos: BrowserHomeDemoCatalog = {
  demos: [{ id: "example", title: "Example", summary: "A startup catalog entry." }],
};
const facets: BrowserPackageQueryFacetCatalog = { facets: [] };
const gallery: BrowserGalleryDiscoveryCatalog = {
  packageType: {
    id: "package-type",
    label: "Package type",
    summary: "Gallery package types.",
    suggestions: [{ value: "DotnetTool", label: ".NET Tool" }],
  },
  orders: [{ id: "downloads", label: "Downloads", summary: "Most downloaded first." }],
};

function createFacades(calls: string[] = []) {
  return {
    host: {
      buildIdentity() {
        calls.push("buildIdentity");
        return identity;
      },
    },
    catalog: {
      listVocabulary() {
        calls.push("listVocabulary");
        return vocabulary;
      },
      listHomeDemos() {
        calls.push("listHomeDemos");
        return demos;
      },
    },
    package: {
      listPackageQueryFacets() {
        calls.push("listPackageQueryFacets");
        return facets;
      },
      listGalleryDiscoveryCatalog() {
        calls.push("listGalleryDiscoveryCatalog");
        return gallery;
      },
    },
  };
}

test("startup bindings defer reads until called and preserve generated results in Promises", async () => {
  const calls: string[] = [];
  const client = createMainThreadStartupClient(createFacades(calls));
  assert.deepEqual(calls, []);

  const reads = [
    { invoke: () => client.host.buildIdentity(), expected: identity },
    { invoke: () => client.catalog.listVocabulary(), expected: vocabulary },
    { invoke: () => client.catalog.listHomeDemos(), expected: demos },
    { invoke: () => client.package.listPackageQueryFacets(), expected: facets },
    { invoke: () => client.package.listGalleryDiscoveryCatalog(), expected: gallery },
  ];
  for (const read of reads) {
    const result = read.invoke();
    assert.ok(result instanceof Promise);
    assert.equal(await result, read.expected);
  }
  assert.deepEqual(calls, [
    "buildIdentity",
    "listVocabulary",
    "listHomeDemos",
    "listPackageQueryFacets",
    "listGalleryDiscoveryCatalog",
  ]);
});

test("each startup binding turns a thrown failure into the same Promise rejection", async () => {
  for (const failure of [new Error("Facade read failed."), new Error("")]) {
    const fail = (): never => { throw failure; };
    const client = createMainThreadStartupClient({
      host: { buildIdentity: fail },
      catalog: { listVocabulary: fail, listHomeDemos: fail },
      package: { listPackageQueryFacets: fail, listGalleryDiscoveryCatalog: fail },
    });
    const reads = [
      () => client.host.buildIdentity(),
      () => client.catalog.listVocabulary(),
      () => client.catalog.listHomeDemos(),
      () => client.package.listPackageQueryFacets(),
      () => client.package.listGalleryDiscoveryCatalog(),
    ];
    for (const read of reads) {
      const result = read();
      assert.ok(result instanceof Promise);
      await assert.rejects(result, (error: unknown) => error === failure);
    }
  }
});

test("a rejected startup read does not poison neighboring catalog bindings", async () => {
  const facades = createFacades();
  const failure = new Error("Style vocabulary unavailable.");
  facades.catalog.listVocabulary = () => { throw failure; };
  const client = createMainThreadStartupClient(facades);

  await assert.rejects(client.catalog.listVocabulary(), error => error === failure);
  assert.equal(await client.catalog.listHomeDemos(), demos);
  assert.equal(await client.package.listPackageQueryFacets(), facets);
  assert.equal(await client.package.listGalleryDiscoveryCatalog(), gallery);
});
