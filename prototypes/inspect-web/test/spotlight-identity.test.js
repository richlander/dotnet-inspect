import assert from "node:assert/strict";
import test from "node:test";

import {
  callGraphTargetTypeId,
  graphMemberSelection,
  mermaidLabel,
  packageForView,
  packageIdentityKey,
  scopedRequestState,
  spotlightCandidateKey,
  spotlightCandidateSignature
} from "../src/data.js";

const packageAt = (version, framework, types = 1) => ({
  id: "Example.Package",
  version,
  activeFramework: framework,
  types: Array.from({ length: types }, (_, index) => ({ id: `Type${index}` }))
});

test("spotlight candidate identity includes version and framework", () => {
  const net8 = packageAt("1.0.0", "net8.0");
  const net9 = packageAt("1.0.0", "net9.0");
  const v2 = packageAt("2.0.0", "net8.0");

  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(net9, "Example.Type"));
  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(v2, "Example.Type"));
});

test("spotlight cache signature changes when a coordinate is replaced", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);
  const newFramework = packageAt("1.0.0", "net9.0", 4);

  const oldSignature = spotlightCandidateSignature(oldPackage, [oldPackage]);
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newVersion, [newVersion]));
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newFramework, [newFramework]));
});

test("member cache signatures use the same complete coordinates", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);

  assert.notEqual(
    spotlightCandidateSignature(oldPackage, [oldPackage]),
    spotlightCandidateSignature(newVersion, [newVersion]));
});

test("history never applies a selection to another coordinate", () => {
  const oldPackage = packageAt("1.0.0", "net8.0");
  const newVersion = packageAt("2.0.0", "net8.0");
  const view = {
    package: oldPackage.id,
    packageKey: packageIdentityKey(oldPackage)
  };

  assert.equal(packageForView([newVersion], view), null);
  assert.equal(packageForView([oldPackage, newVersion], view), oldPackage);
});

test("call graph navigation prefers exact metadata type identity", () => {
  assert.equal(
    callGraphTargetTypeId({
      typeFullName: "Example.Outer.Inner",
      typeMetadataId: "Example.Outer`1+Inner`1"
    }),
    "Example.Outer`1+Inner`1");
  assert.equal(
    callGraphTargetTypeId({ typeFullName: "Example.Legacy" }),
    "");
});

test("call graph navigation resolves accessor body selectors without a token", () => {
  const groups = [{
    overloads: [{
      graphSelectorKey: "property-selector",
      bodySelectors: [{
        token: 123,
        memberName: "get_P",
        selectorKey: "getter-selector"
      }]
    }]
  }];

  assert.deepEqual(
    graphMemberSelection(groups, {
      metadataToken: null,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
});

test("member documentation state is scoped to the exact request", () => {
  assert.deepEqual(
    scopedRequestState("package-a\u0000member-a", "package-b\u0000member-b", false, "bad XML"),
    { loading: false, error: "" });
  assert.deepEqual(
    scopedRequestState("same", "same", true, ""),
    { loading: true, error: "" });
});

test("Mermaid labels contain grammar-significant metadata", () => {
  const encoded = mermaidLabel("A\"B\n<x>&\\\u2028");

  assert.equal(
    encoded,
    "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028");
  for (const character of ['"', "\n", "<", ">", "\\", "\u2028"]) {
    assert.equal(encoded.includes(character), false);
  }
});
