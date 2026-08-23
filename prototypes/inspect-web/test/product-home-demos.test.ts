import assert from "node:assert/strict";
import test from "node:test";
import {
  EXTENSIONS_CALLGRAPH,
  EXTENSIONS_CALLGRAPH_DEMO_ID,
  PLATFORM_LIST_DEMO_ID,
  PLATFORM_LIST_LIBRARY,
  PLATFORM_LIST_TYPE,
  PLATFORM_RUNTIME_PACK,
  PRODUCT_HOME_DEMO_CATALOG,
  STJ_SERIALIZER_DEMO_ID,
  STJ_SERIALIZER_PACKAGE,
  STJ_SERIALIZER_TYPE,
  isProductHomeDemoId,
  productHomeDemoLocationHref,
} from "../src/product-home-demos.ts";
import { parseWorkspaceLocation } from "../src/workspace-navigation.ts";

function parseDemoHref(href: string) {
  const url = new URL(href, "https://inspect.local/");
  return {
    url,
    location: parseWorkspaceLocation({
      href: url.href,
      pathname: url.pathname,
      search: url.search,
      hash: url.hash,
    }),
  };
}

test("product home catalog uses ProductInspectionDemos ids and labels", () => {
  assert.deepEqual(
    PRODUCT_HOME_DEMO_CATALOG.map(entry => entry.id),
    [STJ_SERIALIZER_DEMO_ID, EXTENSIONS_CALLGRAPH_DEMO_ID, PLATFORM_LIST_DEMO_ID],
  );
  assert.equal(
    PRODUCT_HOME_DEMO_CATALOG.find(e => e.id === STJ_SERIALIZER_DEMO_ID)?.title,
    "System.Text.Json",
  );
  assert.equal(
    PRODUCT_HOME_DEMO_CATALOG.find(e => e.id === EXTENSIONS_CALLGRAPH_DEMO_ID)?.summary,
    "Trace calls across three packages",
  );
  assert.equal(
    PRODUCT_HOME_DEMO_CATALOG.find(e => e.id === PLATFORM_LIST_DEMO_ID)?.title,
    ".NET Platform",
  );
});

test("isProductHomeDemoId accepts only product ids", () => {
  assert.equal(isProductHomeDemoId("stj-serializer"), true);
  assert.equal(isProductHomeDemoId("platform-list"), true);
  assert.equal(isProductHomeDemoId("extensions-callgraph"), true);
  assert.equal(isProductHomeDemoId("stj"), false);
  assert.equal(isProductHomeDemoId("runtime"), false);
  assert.equal(isProductHomeDemoId("callgraph"), false);
  assert.equal(isProductHomeDemoId(""), false);
  assert.equal(isProductHomeDemoId(undefined), false);
});

test("stj-serializer deep link selects JsonSerializer on STJ 10.0.0", () => {
  const href = productHomeDemoLocationHref(STJ_SERIALIZER_DEMO_ID);
  assert.ok(href);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), STJ_SERIALIZER_PACKAGE.id);
  assert.deepEqual(location.tabs, [{ ...STJ_SERIALIZER_PACKAGE }]);
  assert.equal(location.active, 0);
  assert.equal(location.type, STJ_SERIALIZER_TYPE);
  assert.equal(location.package, STJ_SERIALIZER_PACKAGE.id);
});

test("platform-list deep link focuses CoreLib List`1 on runtime pack", () => {
  const href = productHomeDemoLocationHref(PLATFORM_LIST_DEMO_ID);
  assert.ok(href);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), PLATFORM_RUNTIME_PACK.id);
  assert.deepEqual(location.tabs, [
    { ...STJ_SERIALIZER_PACKAGE },
    { ...PLATFORM_RUNTIME_PACK },
  ]);
  assert.equal(location.active, 1);
  assert.equal(location.library, PLATFORM_LIST_LIBRARY);
  assert.equal(location.type, PLATFORM_LIST_TYPE);
  assert.equal(location.package, PLATFORM_RUNTIME_PACK.id);
});

test("extensions-callgraph has no deep link and keeps product packages/anchor", () => {
  assert.equal(productHomeDemoLocationHref(EXTENSIONS_CALLGRAPH_DEMO_ID), null);
  assert.equal(EXTENSIONS_CALLGRAPH.packages.length, 3);
  assert.equal(EXTENSIONS_CALLGRAPH.memberAnchorDigest, "74b6b4b321");
  assert.equal(EXTENSIONS_CALLGRAPH.memberSection, "call-graph");
  assert.equal(
    EXTENSIONS_CALLGRAPH.packages[0].id,
    "Microsoft.Extensions.DependencyInjection.Abstractions",
  );
});
