import assert from "node:assert/strict";
import test from "node:test";
import {
  isProductEcosystemsPath,
  productEcosystemCatalog,
  productEcosystemsViewHtml,
  setProductEcosystemCatalog,
} from "../src/product-ecosystems.ts";

const ecosystems = [{
  id: "ecosystem.runtime",
  title: ".NET Runtime",
  summary: ".NET Runtime libraries and product demos.",
  corePackageCount: 0,
  namespaceRootCount: 1,
  toolPackageCount: 0,
  demoCount: 3,
  hasPackageSet: false,
  hasScanner: false,
  hasPopulationLoader: true,
  hasWorkspaceRegistration: true,
}, {
  id: "ecosystem.aspire",
  title: "Aspire",
  summary: "Aspire package and demo content.",
  corePackageCount: 2,
  namespaceRootCount: 1,
  toolPackageCount: 1,
  demoCount: 2,
  hasPackageSet: true,
  hasScanner: true,
  hasPopulationLoader: false,
  hasWorkspaceRegistration: true,
}] as const;

test("Ecosystems renders managed catalog order and capability metadata", () => {
  setProductEcosystemCatalog(ecosystems);
  const html = productEcosystemsViewHtml(value => String(value), "");

  assert.deepEqual(productEcosystemCatalog(), ecosystems);
  assert.match(html, /<h1 id="ecosystems-heading" tabindex="-1">Ecosystems<\/h1>/);
  assert.match(
    html,
    /data-ecosystem="ecosystem.runtime"[\s\S]*data-ecosystem="ecosystem.aspire"/);
  assert.match(html, /\.NET Runtime libraries and product demos/);
  assert.match(html, /2 core packages/);
  assert.match(html, /1 tool/);
  assert.match(html, /Package set/);
  assert.match(html, /Integration scanner/);
  assert.match(html, /Platform libraries/);
  assert.match(html, /Workspace-ready/);
  assert.doesNotMatch(html, /0 core packages|0 tools/);
  assert.doesNotMatch(html, /button|href=|Open|Load more/);
});

test("Ecosystems distinguishes an empty catalog from a visible failure", () => {
  setProductEcosystemCatalog([]);
  assert.match(
    productEcosystemsViewHtml(value => String(value), ""),
    /No Ecosystems are available/);
  const failed = productEcosystemsViewHtml(
    value => String(value).replaceAll("<", "&lt;"),
    "Ecosystems are unavailable: <offline>");
  assert.match(
    failed,
    /role="alert">Ecosystems are unavailable: &lt;offline>/);
  assert.doesNotMatch(failed, /No Ecosystems/);
});

test("Ecosystems owns one canonical application route", () => {
  assert.equal(isProductEcosystemsPath("/ecosystems"), true);
  assert.equal(isProductEcosystemsPath("/ecosystems/"), true);
  assert.equal(isProductEcosystemsPath("/ECOSYSTEMS"), false);
  assert.equal(isProductEcosystemsPath("/ecosystems//"), false);
  assert.equal(isProductEcosystemsPath("/ecosystem"), false);
  assert.equal(isProductEcosystemsPath("/"), false);
});
