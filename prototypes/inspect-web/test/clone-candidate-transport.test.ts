import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
  queryCloneCandidates,
} from "../src/facades/inspect-web-analysis.d.ts";

const request = {
  schemaVersion: 1,
  packages: [{
    packageId: "Example.Package",
    version: "1.2.3",
    targetFramework: "net11.0",
  }],
  selectedPackageIndex: 0,
  assembly: "Example.Package.dll",
  seed: {
    kind: "Member",
    typeDefinitionId: "Example.Widget",
    member: {
      stableSelector: "Run",
      canonicalSignature: "void Example.Widget.Run()",
      fingerprint: "example-fingerprint",
      typeFullName: "Example.Widget",
      memberName: "Run",
    },
    body: {
      memberName: "Run",
      selectorKey: "Run",
      metadataToken: 0x06000001,
    },
  },
  breadth: "Everything",
  discovery: "SimilarNames",
} as const satisfies BrowserCloneCandidateRequest;

const rejected = {
  schemaVersion: 1,
  request,
  kind: "Rejected",
  document: null,
  seedLibrary: {
    name: "Example.Package",
    version: "1.2.3.0",
    culture: null,
    publicKeyToken: null,
  },
  openFailureKind: "InvalidImage",
  failure: null,
  presentationRejectionKind: null,
  subject: null,
  detail: "The selected library is not a managed image.",
  metadataRootReason: null,
} as const satisfies BrowserCloneCandidateResult;

function generatedCloneTransportRejectsMutation(
  result: BrowserCloneCandidateResult,
): void {
  // @ts-expect-error Generated wire properties are producer-owned snapshots.
  result.kind = "Available";
  // @ts-expect-error Generated wire collections are readonly.
  result.request.packages[0] = request.packages[0];
}
void generatedCloneTransportRejectsMutation;

type GeneratedQueryResult = Awaited<ReturnType<typeof queryCloneCandidates>>;
const typedResult: GeneratedQueryResult = rejected;
void typedResult;

test("Clone Candidates generated transport carries typed request and closed outcome", () => {
  assert.equal(rejected.request.breadth, "Everything");
  assert.equal(rejected.request.discovery, "SimilarNames");
  assert.equal(rejected.request.seed.body?.metadataToken, 0x06000001);
  assert.equal(rejected.kind, "Rejected");
  assert.equal(rejected.openFailureKind, "InvalidImage");
});
