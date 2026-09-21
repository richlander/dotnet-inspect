import assert from "node:assert/strict";
import { test } from "node:test";
import type {
  BrowserCloneCandidateDocument,
  BrowserCloneCandidateMethod,
  BrowserCloneCandidateResult,
  BrowserCloneCandidateSeedCoverage,
} from "../src/facades/inspect-web-analysis.d.ts";
import {
  bindCompareCloneRows,
  buildCompareCloneRequest,
  createCompareCloneCoordinator,
  joinCloneSeed,
  renderCompareClone,
  type CompareCloneJoin,
  type CompareCloneSelection,
  type CompareCloneState,
  type CompareCloneStateHost,
} from "../src/compare-clone.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import { fakeDom } from "./fake-dom.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

const containing = {
  packageId: "Example.Package",
  version: "2.0.0",
  targetFramework: "net11.0",
  assemblyName: "Example",
};

function selection(
  packageModel: object,
  seed: CompareCloneSelection["seed"] = {
    kind: "Library", typeDefinitionId: null, member: null, body: null,
  },
): CompareCloneSelection {
  return {
    packageModel,
    packages: [
      { packageId: "Example.Package", version: "2.0.0", targetFramework: "net11.0" },
      { packageId: "Other.Package", version: "3.0.0", targetFramework: "net11.0" },
    ],
    selectedPackageIndex: 0,
    assembly: "lib/net11.0/Example.dll",
    seed,
  };
}

function method(
  token: number,
  overrides: Partial<BrowserCloneCandidateMethod> = {},
  participant: Partial<BrowserCloneCandidateMethod["participant"]> = {},
): BrowserCloneCandidateMethod {
  return {
    participant: {
      ordinal: 0,
      assembly: { name: "Example", version: "2.0.0.0", culture: null, publicKeyToken: null },
      provenance: {
        kind: "Package",
        packageId: "example.package",
        packageVersion: "2.0.0",
        tfm: "net11.0",
        rid: null,
        framework: null,
        frameworkVersion: null,
        project: null,
        resolverSource: null,
        contentRef: null,
        digest: null,
        declaredName: null,
      },
      moduleVersionId: "mvid-example",
      ...participant,
    },
    moduleVersionId: "mvid-example",
    methodDefinitionToken: token,
    addressDisplay: `Example.Widget::M${token.toString(16)}`,
    ...overrides,
  };
}

function coverage(
  seed: BrowserCloneCandidateMethod,
  overrides: Partial<BrowserCloneCandidateSeedCoverage> = {},
): BrowserCloneCandidateSeedCoverage {
  return {
    seed,
    disposition: "Completed",
    rankedPairs: 2,
    suppressedPairs: 0,
    blockers: [],
    failures: [],
    isComplete: true,
    ...overrides,
  };
}

function document(
  overrides: Partial<BrowserCloneCandidateDocument> = {},
): BrowserCloneCandidateDocument {
  return {
    schemaVersion: 1,
    seed: { kind: "Library", type: null, member: null },
    breadth: "Everything",
    discovery: "SimilarNames",
    nameSimilarityThreshold: 0.6,
    limits: {
      maximumResults: 100,
      maximumSeedMethods: 1000,
      maximumCandidateMethods: 100000,
      maximumParticipants: 256,
      maximumRetrievalPairs: 1000000,
      maximumRetrievalChunkMethods: 512,
      maximumNameCharacters: 4096,
      maximumNameComparisonWork: 1000000,
      maximumNameCacheCells: 1000000,
      comparisonLimits: null,
    },
    scopeChangedDuringSearch: false,
    coverageIsComplete: true,
    rows: [],
    seeds: [],
    libraries: [{
      participant: method(1).participant,
      membership: "ContainingLibrary",
      admitted: true,
      candidateMethods: 12,
      discoveredMethods: 12,
      retrievalPairs: 40,
      nameComparisonWork: 100,
      failures: [],
      analysisBlockers: [],
      isComplete: true,
    }],
    receipt: {
      seedMethods: 3,
      candidateMethods: 12,
      discoveredMethods: 12,
      admittedLibraries: 1,
      excludedLibraries: 0,
      nameComparisonWork: 100,
      retrievalPairs: 40,
      retrievalCalls: 1,
      rankedPairs: 5,
      suppressedPairs: 1,
      returnedPairs: 5,
      resultLimitReached: false,
    },
    resultLimitReached: false,
    resultLimitOmittedPairs: 0,
    ...overrides,
  };
}

function available(
  packageModel: object,
  overrides: Partial<BrowserCloneCandidateDocument> = {},
  seed?: CompareCloneSelection["seed"],
): BrowserCloneCandidateResult {
  return {
    schemaVersion: 1,
    request: buildCompareCloneRequest(selection(packageModel, seed)),
    kind: "Available",
    document: document(overrides),
    seedLibrary: null,
    openFailureKind: null,
    failure: null,
    presentationRejectionKind: null,
    subject: null,
    detail: null,
    metadataRootReason: null,
  };
}

function join(): CompareCloneJoin {
  const widget = { typeIdentifier: "Example.Widget", typeDisplay: "Example.Widget" };
  const gadget = { typeIdentifier: "Example.Gadget", typeDisplay: "Example.Gadget" };
  return {
    containingLibrary: containing,
    methods: new Map([
      [0x06000001, { ...widget, memberFingerprint: "digest-run", memberDisplay: "void Run()" }],
      [0x06000002, { ...widget, memberFingerprint: "digest-run", memberDisplay: "void Run()" }],
      [0x06000003, { ...widget, memberFingerprint: "digest-stop", memberDisplay: "void Stop()" }],
      [0x06000004, { ...gadget, memberFingerprint: "digest-go", memberDisplay: "void Go()" }],
    ]),
  };
}

test("the request carries the Package-owned scope, exact seed, and producer defaults only", () => {
  const request = buildCompareCloneRequest(selection({}, {
    kind: "Member",
    typeDefinitionId: "Example.Widget",
    member: {
      stableSelector: "Run",
      canonicalSignature: "void Example.Widget.Run()",
      fingerprint: "digest-run",
      typeFullName: "Example.Widget",
      memberName: "Run",
    },
    body: null,
  }));
  assert.equal(request.schemaVersion, 1);
  assert.equal(request.packages.length, 2);
  assert.equal(request.selectedPackageIndex, 0);
  assert.equal(request.assembly, "lib/net11.0/Example.dll");
  assert.equal(request.seed.kind, "Member");
  assert.equal(request.seed.member?.fingerprint, "digest-run");
  assert.equal(request.breadth, "Everything");
  assert.equal(request.discovery, "SimilarNames");
});

test("seeds join only through exact provenance, assembly, consistent MVID, and MethodDef", () => {
  const joined = join();
  assert.equal(joinCloneSeed(method(0x06000001), joined)?.memberFingerprint, "digest-run");
  assert.equal(joinCloneSeed(method(0x06000099), joined), null);
  assert.equal(joinCloneSeed(method(0x06000001, { moduleVersionId: "other" }), joined), null);
  assert.equal(joinCloneSeed(method(0x06000001, {}, { moduleVersionId: null }), joined), null);
  assert.equal(joinCloneSeed(method(0x06000001, {}, {
    assembly: { name: "Example.Tests", version: null, culture: null, publicKeyToken: null },
  }), joined), null);
  assert.equal(joinCloneSeed(method(0x06000001, {}, {
    provenance: { ...method(1).participant.provenance, packageVersion: "1.0.0" },
  }), joined), null);
  assert.equal(joinCloneSeed(method(0x06000001, {}, {
    provenance: { ...method(1).participant.provenance, kind: "Platform" },
  }), joined), null);
});

test("Library Clone groups per-seed coverage by exact Type without candidate pairs or a Type score", () => {
  const packageModel = {};
  const state: CompareCloneState = {
    status: "ready",
    input: {
      packageModel,
      request: buildCompareCloneRequest(selection(packageModel)),
      requestJson: "",
    },
    result: available(packageModel, {
      seeds: [
        coverage(method(0x06000001)),
        coverage(method(0x06000002), { isComplete: false, rankedPairs: 0 }),
        coverage(method(0x06000004), { disposition: "Unsupported", rankedPairs: 0 }),
        coverage(method(0x06000077)),
      ],
      rows: [{
        rank: 1,
        left: method(0x06000001),
        right: method(0x06000004),
        similarity: {
          score: 9600, operationScore: 9600, positionScore: 9600, blockScore: 9600,
          edgeScore: 9600, localScore: 9600, seedInstructions: 10, candidateInstructions: 10,
          seedBlocks: 1, candidateBlocks: 1, seedEdges: 0, candidateEdges: 0,
          seedLocals: 0, candidateLocals: 0,
        },
        nameQualification: null,
      }],
      resultLimitReached: true,
      resultLimitOmittedPairs: 4,
      coverageIsComplete: false,
    }),
  };
  const html = renderCompareClone(state, String, {
    subject: "library",
    subjectLabel: "Example",
    targetText: "Workspace: 2 loaded Packages",
    join: join(),
    selectedRank: null,
  });
  assert.match(html, /data-compare-mode="clone" aria-selected="true"/);
  assert.match(html, /Clone search complete\. 2 Types with seed coverage\. Coverage incomplete; result limit reached, 4 ranked pairs omitted\./);
  assert.match(html, /data-compare-type-id="Example\.Widget"[^>]*>[\s\S]*?2 seed bodies · 2 ranked pairs · 1 incomplete/);
  assert.match(html, /data-compare-type-id="Example\.Gadget"[^>]*>[\s\S]*?1 seed body · 0 ranked pairs · 1 unsupported/);
  // Rows come from complete per-seed coverage, never from the bounded pair rows.
  assert.doesNotMatch(html, /96%/);
  assert.doesNotMatch(html, /data-compare-clone-rank/);
  assert.match(html, /Seed bodies not joined to a loaded subject \(1\)/);
  assert.match(html, /Example\.Widget::M6000077/);
  assert.doesNotMatch(html, /Explore/);
});

test("Type Clone lists logical Members with admitted candidates and offers no whole-Type action", () => {
  const packageModel = {};
  const state: CompareCloneState = {
    status: "ready",
    input: {
      packageModel,
      request: buildCompareCloneRequest(selection(packageModel)),
      requestJson: "",
    },
    result: available(packageModel, {
      seeds: [
        coverage(method(0x06000001)),
        coverage(method(0x06000002), { rankedPairs: 1 }),
        coverage(method(0x06000003), { rankedPairs: 0 }),
      ],
    }),
  };
  const html = renderCompareClone(state, String, {
    subject: "type",
    subjectLabel: "Example.Widget",
    targetText: "Workspace: 2 loaded Packages",
    join: join(),
    selectedRank: null,
  });
  assert.match(html, /Clone search complete\. 1 Members with ranked candidates\./);
  assert.match(html, /data-compare-member-fingerprint="digest-run"[^>]*>[\s\S]*?2 seed bodies · 3 ranked pairs/);
  assert.doesNotMatch(html, /data-compare-member-fingerprint="digest-stop"/);
  assert.doesNotMatch(html, /Whole type/);
  assert.doesNotMatch(html, /Explore/);
});

test("Member Clone ranks candidate pairs, selects one, and keeps retrieval similarity apart from any checked relation", () => {
  const packageModel = {};
  const row = (rank: number, right: BrowserCloneCandidateMethod) => ({
    rank,
    left: method(0x06000001),
    right,
    similarity: {
      score: 9150 - rank * 100, operationScore: 9000, positionScore: 8000, blockScore: 7000,
      edgeScore: 6000, localScore: 5000, seedInstructions: 12, candidateInstructions: 11,
      seedBlocks: 2, candidateBlocks: 2, seedEdges: 1, candidateEdges: 1,
      seedLocals: 1, candidateLocals: 0,
    },
    nameQualification: { declaringTypeSimilarity: 0.8, memberSimilarity: 0.5 },
  });
  const state: CompareCloneState = {
    status: "ready",
    input: {
      packageModel,
      request: buildCompareCloneRequest(selection(packageModel)),
      requestJson: "",
    },
    result: available(packageModel, {
      rows: [row(1, method(0x06000004)), row(2, method(0x06000003))],
      seeds: [coverage(method(0x06000001))],
    }),
  };
  const options = {
    subject: "member" as const,
    subjectLabel: "Example.Widget.Run",
    targetText: "Workspace: 2 loaded Packages",
    join: join(),
    selectedRank: 2,
  };
  const html = renderCompareClone(state, String, options);
  assert.match(html, /Clone search complete\. 2 ranked candidates\./);
  assert.match(html, /data-compare-clone-rank="1"[^>]*>/);
  assert.match(html, /data-compare-clone-rank="2" aria-current="true"/);
  assert.match(html, /<h2>Candidate<\/h2>\s*<p><code>Example\.Widget::M6000003<\/code>/);
  assert.match(html, /<dt>Overall<\/dt><dd>89\.5%<\/dd>/);
  assert.match(html, /Name admission: type 80\.0% · member 50\.0%/);
  assert.match(html, /<h2>Checked relation<\/h2>\s*<p>None issued\./);
  assert.doesNotMatch(html, /Explore/);
  // An unknown selection falls back to the top-ranked pair.
  const fallback = renderCompareClone(state, String, { ...options, selectedRank: 9 });
  assert.match(fallback, /data-compare-clone-rank="1" aria-current="true"/);
});

test("non-available outcomes and unavailable scope stay distinct from empty success inside the frame", () => {
  const packageModel = {};
  const input = {
    packageModel,
    request: buildCompareCloneRequest(selection(packageModel)),
    requestJson: "",
  };
  const options = {
    subject: "library" as const,
    subjectLabel: "Example",
    targetText: "Workspace: 2 loaded Packages",
    join: null,
    selectedRank: null,
  };
  const rejected = renderCompareClone({
    status: "ready",
    input,
    result: {
      ...available(packageModel),
      kind: "Rejected",
      document: null,
      detail: "The selected library is not a managed image.",
    },
  }, String, options);
  assert.match(rejected, /Clone search rejected\./);
  assert.match(rejected, /not a managed image/);
  assert.doesNotMatch(rejected, /No seed bodies/);
  const failed = renderCompareClone({ status: "failed", input, error: "Boom" }, String, options);
  assert.match(failed, /id="compare-retry"/);
  const unavailable = renderCompareClone({
    status: "unavailable",
    message: "Other.Package 3.0.0 is no longer in this Workspace. Choose another Clone scope.",
  }, String, options);
  assert.match(unavailable, /Clone search unavailable/);
  assert.match(unavailable, /id="compare-change-target"/);
  const empty = renderCompareClone({
    status: "ready",
    input,
    result: available(packageModel, { seeds: [], receipt: { ...document().receipt, seedMethods: 0 } }),
  }, String, options);
  assert.match(empty, /Clone search complete\. No seed bodies\. Coverage complete\./);
  for (const html of [rejected, failed, unavailable, empty]) {
    assert.match(html, /role="tablist" aria-label="Compare modes"/);
  }
});

test("superseded Clone work publishes nothing and mode changes retire pending work", async () => {
  const packageOne = {};
  const packageTwo = {};
  const state: CompareCloneStateHost = { compareClone: { status: "idle" } };
  // Read through a function so control-flow narrowing never turns the mutable
  // host state into `never` between assertions.
  const current = (): CompareCloneState => state.compareClone;
  const pending: ReturnType<typeof deferred<BrowserCloneCandidateResult>>[] = [];
  let renders = 0;
  const coordinator = createCompareCloneCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      const result = deferred<BrowserCloneCandidateResult>();
      pending.push(result);
      return result.promise;
    },
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => {
      renders++;
    },
  });
  coordinator.reconcile({ kind: "selection", selection: selection(packageOne) });
  assert.equal(current().status, "loading");
  // Reconciling the same subject and scope again starts no second query.
  coordinator.reconcile({ kind: "selection", selection: selection(packageOne) });
  assert.equal(pending.length, 1);
  coordinator.reconcile({ kind: "selection", selection: selection(packageTwo) });
  assert.equal(pending.length, 2);
  pending[0]?.resolve(available(packageOne));
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(current().status, "loading");
  assert.equal(renders, 0);
  pending[1]?.resolve(available(packageTwo));
  await Promise.resolve();
  await Promise.resolve();
  const completed = current();
  assert.equal(completed.status, "ready");
  if (completed.status === "ready")
    assert.equal(completed.input.packageModel, packageTwo);
  assert.equal(renders, 1);

  // Leaving Clone mode (null target) retires the result; an unavailable scope
  // is its own visible state rather than an empty success.
  coordinator.reconcile(null);
  assert.equal(current().status, "idle");
  coordinator.reconcile({ kind: "unavailable", message: "Choose another Clone scope." });
  assert.equal(current().status, "unavailable");
  coordinator.reconcile({ kind: "unavailable", message: "Choose another Clone scope." });
  assert.equal(current().status, "unavailable");
});

test("a result echoing a different request or a malformed document fails at the boundary", async () => {
  const packageModel = {};
  const state: CompareCloneStateHost = { compareClone: { status: "idle" } };
  const current = (): CompareCloneState => state.compareClone;
  const results: unknown[] = [];
  const coordinator = createCompareCloneCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => Promise.resolve(results.shift()),
    describeError: error => error instanceof Error ? error.message : String(error),
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  results.push(available({}, {}, {
    kind: "Type", typeDefinitionId: "Example.Widget", member: null, body: null,
  }));
  coordinator.reconcile({ kind: "selection", selection: selection(packageModel) });
  await Promise.resolve();
  await Promise.resolve();
  const mismatched = current();
  assert.equal(mismatched.status, "failed");
  if (mismatched.status === "failed")
    assert.match(mismatched.error, /does not match its request/);

  results.push({ ...available(packageModel), document: null });
  coordinator.retry(selection(packageModel));
  await Promise.resolve();
  await Promise.resolve();
  const malformed = current();
  assert.equal(malformed.status, "failed");
  if (malformed.status === "failed")
    assert.match(malformed.error, /has no document/);
});

test("candidate row bindings dispatch the ranked index only", () => {
  const selected: number[] = [];
  const buttons = ["2", "x", "0"].map(rank => {
    let handler: (() => void) | undefined;
    return {
      dataset: { compareCloneRank: rank },
      addEventListener: (type: string, listener: () => void) => {
        if (type === "click") handler = listener;
      },
      click: () => handler?.(),
    };
  });
  bindCompareCloneRows(fakeDom.parentNode({
    querySelectorAll: () => buttons,
  }), { selectRank: rank => selected.push(rank) });
  for (const button of buttons) button.click();
  assert.deepEqual(selected, [2]);
});
