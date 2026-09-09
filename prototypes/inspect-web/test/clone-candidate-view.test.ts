import assert from "node:assert/strict";
import test from "node:test";

import {
  createCloneCandidateInspectionState,
} from "../src/clone-candidate-inspection.ts";
import {
  bindCloneCandidateInspection,
  renderCloneCandidateInspection,
} from "../src/clone-candidate-view.ts";
import type {
  BrowserCloneCandidateDocument,
  BrowserCloneCandidateMethod,
  BrowserCloneCandidateParticipant,
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
} from "../src/facades/inspect-web-analysis.d.ts";
import { fakeDom } from "./fake-dom.ts";

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll('"', "&quot;");

const request: BrowserCloneCandidateRequest = {
  schemaVersion: 1,
  packages: [{
    packageId: "Example.Package",
    version: "1.0.0",
    targetFramework: "net11.0",
  }],
  selectedPackageIndex: 0,
  assembly: "lib/net11.0/Example.Package.dll",
  seed: {
    kind: "Library",
    typeDefinitionId: null,
    member: null,
    body: null,
  },
  breadth: "Everything",
  discovery: "SimilarNames",
};

const participant: BrowserCloneCandidateParticipant = {
  ordinal: 0,
  assembly: {
    name: "Example.Package",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
  },
  provenance: {
    kind: "Package",
    packageId: "Example.Package",
    packageVersion: "1.0.0",
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
  moduleVersionId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
};

function method(
  addressDisplay: string,
  token: number,
): BrowserCloneCandidateMethod {
  return {
    participant,
    moduleVersionId: participant.moduleVersionId ?? "",
    methodDefinitionToken: token,
    addressDisplay,
  };
}

const document: BrowserCloneCandidateDocument = {
  schemaVersion: 1,
  seed: { kind: "Library", type: null, member: null },
  breadth: "Everything",
  discovery: "SimilarNames",
  nameSimilarityThreshold: 0.72,
  limits: {
    maximumResults: 100,
    maximumSeedMethods: 500,
    maximumCandidateMethods: 5000,
    maximumParticipants: 12,
    maximumRetrievalPairs: 50_000,
    maximumRetrievalChunkMethods: 500,
    maximumNameCharacters: 256,
    maximumNameComparisonWork: 1_000_000,
    maximumNameCacheCells: 10_000,
    comparisonLimits: null,
  },
  scopeChangedDuringSearch: false,
  coverageIsComplete: true,
  rows: [
    {
      rank: 1,
      left: method("Example.Widget.Build", 0x06000001),
      right: method("<script>candidate</script>", 0x06000002),
      similarity: {
        score: 9875,
        operationScore: 10000,
        positionScore: 9750,
        blockScore: 9500,
        edgeScore: 9000,
        localScore: 8750,
        seedInstructions: 12,
        candidateInstructions: 13,
        seedBlocks: 3,
        candidateBlocks: 3,
        seedEdges: 2,
        candidateEdges: 2,
        seedLocals: 1,
        candidateLocals: 2,
      },
      nameQualification: {
        declaringTypeSimilarity: 0.9,
        memberSimilarity: 0.8,
      },
    },
  ],
  seeds: [{
    seed: method("Example.Widget.Build", 0x06000001),
    disposition: "Completed",
    rankedPairs: 1,
    suppressedPairs: 0,
    blockers: [],
    failures: [],
    isComplete: true,
  }],
  libraries: [{
    participant,
    membership: "ContainingLibrary",
    admitted: true,
    candidateMethods: 2,
    discoveredMethods: 2,
    retrievalPairs: 1,
    nameComparisonWork: 2,
    failures: [],
    analysisBlockers: [],
    isComplete: true,
  }],
  receipt: {
    seedMethods: 1,
    candidateMethods: 2,
    discoveredMethods: 2,
    admittedLibraries: 1,
    excludedLibraries: 0,
    nameComparisonWork: 2,
    retrievalPairs: 1,
    retrievalCalls: 1,
    rankedPairs: 1,
    suppressedPairs: 0,
    returnedPairs: 1,
    resultLimitReached: false,
  },
  resultLimitReached: false,
  resultLimitOmittedPairs: 0,
};

function available(): BrowserCloneCandidateResult {
  return {
    schemaVersion: 1,
    request,
    kind: "Available",
    document,
    seedLibrary: participant.assembly,
    openFailureKind: null,
    failure: null,
    presentationRejectionKind: null,
    subject: null,
    detail: null,
    metadataRootReason: null,
  };
}

test("master-detail preserves global rank and exposes evidence without overclaiming", () => {
  const state = createCloneCandidateInspectionState();
  state.result = available();
  state.selectedRank = 1;
  const html = renderCloneCandidateInspection(state, escapeHtml);

  assert.match(html, /Everything/);
  assert.match(html, /Similar names/);
  assert.match(html, /Globally ranked candidates/);
  assert.match(html, /#1/);
  assert.match(html, /98\.8%/);
  assert.match(html, /Structural similarity/);
  assert.match(html, /Search receipt/);
  assert.match(html, /do not establish clone identity/);
  assert.match(html, /&lt;script>candidate&lt;\/script>/);
  assert.doesNotMatch(html, /<script>candidate<\/script>/);
});

test("closed non-available outcomes remain visible", () => {
  const state = createCloneCandidateInspectionState();
  state.result = {
    ...available(),
    kind: "Unrepresentable",
    document: null,
    presentationRejectionKind: "ParticipantCoverageMissing",
    detail: "Participant evidence is incomplete.",
  };
  assert.match(
    renderCloneCandidateInspection(state, escapeHtml),
    /ParticipantCoverageMissing: Participant evidence is incomplete/);
});

test("empty rankings retain incomplete coverage and seed failures", () => {
  const state = createCloneCandidateInspectionState();
  state.result = {
    ...available(),
    document: {
      ...document,
      rows: [],
      coverageIsComplete: false,
      resultLimitReached: true,
      resultLimitOmittedPairs: 3,
      seeds: [{
        ...document.seeds[0]!,
        disposition: "Failed",
        failures: [{
          kind: "MetadataInspectionFailed",
          subject: participant.assembly,
          detail: "Seed metadata could not be read.",
        }],
        isComplete: false,
      }],
    },
  };
  const html = renderCloneCandidateInspection(state, escapeHtml);

  assert.match(html, /No structural clone candidates were found/);
  assert.match(html, /Coverage is incomplete/);
  assert.match(html, /3 ranked pairs were omitted/);
  assert.match(html, /Seed metadata could not be read/);
  assert.match(html, /Example\.Widget\.Build/);
});

test("bindings dispatch controls, row selection, and exact navigation coordinates", () => {
  class Input {
    checked = true;
    value: string;
    onchange: (() => void) | null = null;
    constructor(value: string) {
      this.value = value;
    }
  }
  class Button {
    dataset: Record<string, string>;
    onclick: (() => void) | null = null;
    constructor(dataset: Record<string, string>) {
      this.dataset = dataset;
    }
  }
  const breadth = new Input("Self");
  const discovery = new Input("All");
  const row = new Button({ cloneRank: "4" });
  const navigate = new Button({
    participantOrdinal: "2",
    moduleVersionId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    methodDefinitionToken: String(0x06000009),
  });
  const events: unknown[] = [];
  bindCloneCandidateInspection(fakeDom.parentNode({
    querySelectorAll: (selector: string) => {
      if (selector === "[data-clone-breadth]") return [breadth];
      if (selector === "[data-clone-discovery]") return [discovery];
      if (selector === "[data-clone-rank]") return [row];
      if (selector === "[data-clone-navigate]") return [navigate];
      return [];
    },
  }), {
    onAction: action => events.push(action),
  });

  breadth.onchange?.();
  discovery.onchange?.();
  row.onclick?.();
  navigate.onclick?.();
  assert.deepEqual(events, [
    { kind: "breadth", value: "Self" },
    { kind: "discovery", value: "All" },
    { kind: "select", rank: 4 },
    {
      kind: "navigate",
      participantOrdinal: 2,
      moduleVersionId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      methodDefinitionToken: 0x06000009,
    },
  ]);
});
