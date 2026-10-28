import assert from "node:assert/strict";
import test from "node:test";
import type { BrowserProjectInputInspection } from "../src/facades/inspect-web-catalog.d.ts";
import {
  localProjectInputKind,
  projectPackageActions,
  renderProjectInput,
} from "../src/project-input.ts";

const authored: BrowserProjectInputInspection = {
  content: {
    kind: "AuthoredProject",
    declaredName: "Example.csproj",
    providerOutcome: "Incomplete",
    traversalCompletion: null,
    rootSetCompletion: "Complete",
    isTruncated: false,
    metadata: [{ name: "MissingVersionConstraint", value: "1" }],
    roots: [],
    declarations: [{
      packageId: "markout",
      versionConstraint: null,
      sourcePackageId: "Markout",
      sourceVersionConstraint: null,
      condition: "Unconditional",
      sourceOccurrences: 1,
      groupIndex: null,
    }, {
      packageId: "newtonsoft.json",
      versionConstraint: "[13.0.3, )",
      sourcePackageId: "Newtonsoft.Json",
      sourceVersionConstraint: "13.0.3",
      condition: "<unresolved>",
      sourceOccurrences: 1,
      groupIndex: null,
    }],
    packages: [],
    edges: [],
    failures: [],
  },
  share: {
    kind: "nonProjectable", fullUrl: null, packet: null,
    path: "project/input", reason: "The supplied file is tab-local.",
  },
  diagnostics: [],
};

test("Project dispatch recognizes only the supplied Project formats without changing Library fallback", () => {
  assert.equal(localProjectInputKind("Example.CSPROJ"), "authoredProject");
  assert.equal(localProjectInputKind("PROJECT.ASSETS.JSON"), "restoredProject");
  assert.equal(localProjectInputKind("config.json"), null);
  assert.equal(localProjectInputKind("Example.dll"), null);
});

test("authored Dependencies keeps versionless evidence and resolves only explicit constraints", () => {
  const html = renderProjectInput(authored, "project.dependencies", String);
  assert.match(html, /Markout/);
  assert.match(html, /Unresolved declaration/);
  assert.match(html, /Constraints are not resolved versions/);
  assert.equal((html.match(/data-project-package-index=/g) ?? []).length, 1);
  assert.deepEqual(projectPackageActions(authored), [{
    packageId: "newtonsoft.json", version: "[13.0.3, )", exact: false,
  }]);
});

test("Project presentation contains provider text and retains partial failure evidence", () => {
  const inspection: BrowserProjectInputInspection = {
    ...authored,
    content: {
      ...authored.content,
      kind: "RestoredProject",
      providerOutcome: "Available",
      traversalCompletion: "Partial",
      failures: [{
        phase: "Graph", reason: "MissingNode", message: "<missing>", occurrences: 1,
      }],
    },
  };
  const escape = (value: unknown) =>
    String(value).replaceAll("<", "&lt;").replaceAll(">", "&gt;");
  const overview = renderProjectInput(inspection, "project.overview", escape);
  assert.match(overview, /Available/);
  assert.match(overview, /Partial/);
  assert.match(overview, /&lt;missing&gt;/);
  assert.match(overview, /supplied file is tab-local/);
  assert.doesNotMatch(overview, /<missing>/);
  const dependencies = renderProjectInput(authored, "project.dependencies", escape);
  assert.match(dependencies, /&lt;unresolved&gt;/);
});

test("unavailable restored graphs keep declarations without inventing exact Package actions", () => {
  const inspection: BrowserProjectInputInspection = {
    ...authored,
    content: {
      ...authored.content,
      kind: "RestoredProject",
      providerOutcome: "Unavailable",
      declarations: [{
        packageId: "newtonsoft.json",
        versionConstraint: "[13.0.3, )",
        sourcePackageId: "Newtonsoft.Json",
        sourceVersionConstraint: ">= 13.0.3",
        condition: "net11.0",
        sourceOccurrences: 1,
        groupIndex: 0,
      }],
    },
  };
  const html = renderProjectInput(inspection, "project.dependencies", String);
  assert.match(html, /Newtonsoft.Json/);
  assert.match(html, />= 13\.0\.3/);
  assert.match(html, /net11\.0/);
  assert.match(html, /No resolved Package coordinates/);
  assert.doesNotMatch(html, /data-project-package-index=/);
  assert.deepEqual(projectPackageActions(inspection), []);
});
