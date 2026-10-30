import assert from "node:assert/strict";
import test from "node:test";
import {
  unsafeCensusRequestView,
  unsafeCensusScope,
  unsafeCensusSourceFor,
  unsafeMemberCensusKey,
  unsafeTypeCensusKey,
} from "../src/unsafe-members-scope.ts";
import type { BrowserUnsafeMembers } from "../src/facades/inspect-web-analysis.d.ts";

const result: BrowserUnsafeMembers = {
  outcome: "incomplete", assembly: "Fixture", counts: { members: 0, public: 0, propagating: 0, safeBoundaries: 0, safeBoundaryUndecided: 0 },
  types: [], limitations: ["ReferenceAssembly"], inspectionError: null,
};

test("a Library census covers Type and Member, and a Type census covers Member", () => {
  const none = { library: false, type: false };
  assert.equal(unsafeCensusSourceFor("library", none), "library");
  assert.equal(unsafeCensusSourceFor("type", { library: true, type: false }), "library");
  assert.equal(unsafeCensusSourceFor("member", { library: true, type: true }), "library");
  assert.equal(unsafeCensusSourceFor("member", { library: false, type: true }), "type");
  // Otherwise each view uses its own scope.
  assert.equal(unsafeCensusSourceFor("type", none), "type");
  assert.equal(unsafeCensusSourceFor("member", none), "member");
  // A Type census never covers the Library.
  assert.equal(unsafeCensusSourceFor("library", { library: false, type: true }), "library");
});

test("each source requests only its own census breadth", () => {
  assert.deepEqual(unsafeCensusScope("library", "T", "S"), { typeIdentity: "", stableSelector: "" });
  assert.deepEqual(unsafeCensusScope("type", "T", "S"), { typeIdentity: "T", stableSelector: "" });
  assert.deepEqual(unsafeCensusScope("member", "T", "S"), { typeIdentity: "T", stableSelector: "S" });
});

test("narrow census keys extend the exact Library key and never collide", () => {
  const library = "Pkg@1.0.0/net10.0#lib/net10.0/Pkg.dll";
  const keys = [
    library,
    unsafeTypeCensusKey(library, "Pkg.A"),
    unsafeTypeCensusKey(library, "Pkg.B"),
    unsafeMemberCensusKey(library, "Pkg.A", "Run"),
    unsafeMemberCensusKey(library, "Pkg.A", "Stop"),
    unsafeMemberCensusKey(library, "Pkg.B", "Run"),
    unsafeTypeCensusKey("Pkg@2.0.0/net10.0#lib/net10.0/Pkg.dll", "Pkg.A"),
    // A selector spelled like a key fragment stays distinct.
    unsafeMemberCensusKey(library, "Pkg.A#member=Run", ""),
  ];
  assert.equal(new Set(keys).size, keys.length);
  assert.ok(unsafeMemberCensusKey(library, "Pkg.A", "Run").startsWith(`${unsafeTypeCensusKey(library, "Pkg.A")}#member=`));
});

test("a request view is fresh only for its own key and keeps its outcome", () => {
  const key = "lib#type=A";
  assert.deepEqual(unsafeCensusRequestView({ status: "idle" }, key), { fresh: false, loading: false, error: "", data: null });
  assert.deepEqual(unsafeCensusRequestView({ status: "loading", request: { key } }, key), { fresh: true, loading: true, error: "", data: null });
  assert.equal(unsafeCensusRequestView({ status: "ready", key, result }, key).data, result);
  assert.equal(unsafeCensusRequestView({ status: "ready", key, result }, "lib#type=B").fresh, false);
  assert.deepEqual(unsafeCensusRequestView({ status: "failed", key, error: "<boom>" }, key), { fresh: true, loading: false, error: "<boom>", data: null });
});
