// Library API Diff results shared by the Compare and background member-cue
// tests.
import type {
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffResult,
  InspectionShare,
} from "../src/facades/inspect-web-metadata.d.ts";
import { metadataInertStringFixture } from "./inert-string-fixture.ts";

export const endpoint = (
  version: string,
  compileAssetId = "lib/net11.0/Example.dll",
): BrowserLibraryApiDiffEndpoint => ({
  packageId: "Example.Package",
  version,
  framework: "net11.0",
  asset: {
    id: compileAssetId,
    path: compileAssetId,
    assemblyName: "Example",
  },
  assembly: {
    name: "Example",
    version,
    culture: null,
    publicKeyToken: null,
  },
  scope: "Public",
  isComplete: true,
  issues: [],
});

export function succeeded(
  targetVersion: string,
  currentVersion = "2.0.0",
  compileAssetId = "lib/net11.0/Example.dll",
): BrowserLibraryApiDiffResult {
  return {
    schemaVersion: 3,
    request: {
      schemaVersion: 3,
      packageId: "Example.Package",
      currentVersion,
      targetVersion,
      targetFramework: "net11.0",
      compileAssetId,
      surface: "Library",
      analyses: ["api"],
      views: "Changes",
      typeNames: [],
      memberTargetIdentities: [],
      predicate: null,
    },
    kind: "Succeeded",
    value: {
      libraryIdentifier: "Example",
      libraryDisplay: "Example",
      target: endpoint(targetVersion, compileAssetId),
      current: endpoint(currentVersion, compileAssetId),
      aggregate: {
        changedTypeCount: 2,
        addedTypeCount: 1,
        removedTypeCount: 0,
        changedMemberCount: 3,
        breakingCount: 1,
        additiveCount: 2,
        potentiallyBreakingCount: 0,
      },
      types: [
        {
          documentIdentifier: "Example.Widget",
          display: "Example.Widget",
          state: "Diff",
          typeDefinitionChanged: true,
          changedMemberCount: 2,
          breakingCount: 1,
          additiveCount: 1,
          potentiallyBreakingCount: 0,
          before: {
            identifier: "before-widget",
            namespace: "Example",
            segments: ["Widget"],
            display: "Example.Widget",
          },
          after: {
            identifier: "after-widget",
            namespace: "Example",
            segments: ["Widget"],
            display: "Example.Widget",
          },
          members: [],
          changes: [],
        },
        {
          documentIdentifier: "Example.Options",
          display: "Example.Options",
          state: "Addition",
          typeDefinitionChanged: null,
          changedMemberCount: 1,
          breakingCount: 0,
          additiveCount: 1,
          potentiallyBreakingCount: 0,
          before: null,
          after: {
            identifier: "after-options",
            namespace: "Example",
            segments: ["Options"],
            display: "Example.Options",
          },
          members: [],
          changes: [],
        },
      ],
    },
    unavailable: null,
    rejected: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    inspection: inspection(),
  };
}

export function inspection(
  content: unknown = { outcome: "available", document: {} },
  analysis: {
    readonly surface?: "Library" | "Type" | "Member";
    readonly views?: string;
    readonly analyses?: readonly string[];
    readonly predicate?: {
      readonly key: string;
      readonly operator: string;
      readonly value: string;
    };
    readonly outcomes?: readonly {
      readonly analysis: string;
      readonly kind: "Compared" | "Unavailable" | "Failed";
      readonly findings: readonly string[];
      readonly detail?: string | null;
    }[];
    readonly transitions?: readonly unknown[];
  } = {},
): NonNullable<BrowserLibraryApiDiffResult["inspection"]> {
  const share: InspectionShare = {
    kind: "nonProjectable",
    path: "comparison/endpoints",
    reason: metadataInertStringFixture(
      "Ordered endpoints are not shareable."),
    fullUrl: null,
    packet: null,
  };
  return {
    content: {
      comparison: {
        name: "Example.Package",
        beforeVersion: "1.0.0",
        afterVersion: "2.0.0",
        surface: analysis.surface ?? "Library",
        views: analysis.views ?? "Changes",
        analyses: analysis.analyses ?? ["api"],
        predicates: analysis.predicate === undefined
          ? []
          : [{
              key: analysis.predicate.key,
              operator: analysis.predicate.operator === "Contains"
                ? "contains"
                : "starts-with",
              value: analysis.predicate.value,
            }],
      },
      outcomes: analysis.outcomes ?? [{
        analysis: "api",
        kind: "Compared",
        findings: ["metadata.type", "metadata.member"],
        detail: null,
      }],
      transitions: analysis.transitions ?? null,
      apiInspectionFailures: [],
      changes: { types: [] },
      libraryApi: content,
    },
    share,
    diagnostics: [],
  };
}

export function withMembers(): BrowserLibraryApiDiffResult {
  const result = succeeded("1.0.0");
  if (result.value === null) throw new Error("Expected success.");
  const [widget, options] = result.value.types;
  if (widget === undefined || options === undefined)
    throw new Error("Expected two Types.");
  const identity = (
    fingerprint: string,
    canonicalSignature: string,
    display: string,
  ) => ({
    declaringTypeIdentifier: "after-widget",
    stableSelector: "Run",
    canonicalSignature,
    fingerprint,
    typeFullName: "Example.Widget",
    memberName: "Run",
    display,
  });
  const exploreEndpoint = (
    version: string,
    member: ReturnType<typeof identity> | null,
  ) => {
    const value = endpoint(version);
    return {
      packageId: value.packageId,
      version: value.version,
      framework: value.framework,
      asset: value.asset,
      assembly: value.assembly,
      member,
    };
  };
  const explore = (
    before: ReturnType<typeof identity> | null,
    after: ReturnType<typeof identity> | null,
  ) => ({
    kind: "member-diff" as const,
    target: exploreEndpoint("1.0.0", before),
    current: exploreEndpoint("2.0.0", after),
  });
  const changedBefore = identity(
    "digest-before",
    "void Run(int)",
    "Run(int)",
  );
  const changedAfter = identity(
    "digest-run",
    "void Run(long)",
    "Run(long)",
  );
  const addedAfter = identity("digest-new", "void New()", "New()");
  const removedBefore = identity("digest-gone", "void Gone()", "Gone()");
  return {
    ...result,
    value: {
      ...result.value,
      types: [
        {
          ...widget,
          members: [
            {
              documentIdentifier: "relation-changed",
              pairKind: "Changed",
              role: "Both",
              before: changedBefore,
              after: changedAfter,
              match: null,
              explore: explore(changedBefore, changedAfter),
              changes: [
                {
                  kind: "MemberSignatureChanged",
                  classification: "Breaking",
                  category: "Signature",
                  message: "Parameter type changed from int to long.",
                  oldValue: "void Run(int)",
                  newValue: "void Run(long)",
                },
                {
                  kind: "MemberAttributeAdded",
                  classification: "PotentiallyBreaking",
                  category: "Attribute",
                  message: "[Obsolete] was added.",
                  oldValue: null,
                  newValue: "Obsolete",
                },
              ],
            },
            {
              documentIdentifier: "relation-added",
              pairKind: "Added",
              role: "After",
              before: null,
              after: addedAfter,
              match: null,
              explore: explore(null, addedAfter),
              changes: [{
                kind: "MemberAdded",
                classification: "Additive",
                category: "Signature",
                message: "Member New() was added.",
                oldValue: null,
                newValue: null,
              }],
            },
            {
              documentIdentifier: "relation-removed",
              pairKind: "Removed",
              role: "Before",
              before: removedBefore,
              after: null,
              match: null,
              explore: explore(removedBefore, null),
              changes: [],
            },
          ],
          changes: [{
            kind: "SealedAdded",
            classification: "Breaking",
            category: "Signature",
            message: "Type became sealed.",
            oldValue: null,
            newValue: "sealed",
          }],
        },
        // A removed Type keeps Before-side evidence and no current identity.
        {
          ...options,
          state: "Deletion",
          before: {
            identifier: "before-options",
            namespace: "Example",
            segments: ["Options"],
            display: "Example.Options",
          },
          after: null,
          changes: [{
            kind: "TypeRemoved",
            classification: "Breaking",
            category: "Signature",
            message: "Type Example.Options was removed.",
            oldValue: null,
            newValue: null,
          }],
        },
      ],
    },
  };
}
