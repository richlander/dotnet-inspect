import assert from "node:assert/strict";
import test from "node:test";
import {
  createImplementationProfileCoordinator,
  createImplementationProfileResultCache,
  implementationProfileCacheKey,
  projectImplementationProfileFamily,
  renderImplementationProfileState,
} from "../src/implementation-profiles.ts";
import type {
  ImplementationProfileFamilySelection,
  ImplementationProfileFamilyRequest,
  ImplementationProfileStateHost,
  PackageImplementationProfileRequest,
  PlatformImplementationProfileRequest,
} from "../src/implementation-profiles.ts";
import type {
  BrowserImplementationProfile,
  BrowserImplementationProfileContent,
  BrowserImplementationProfileMethod,
  BrowserImplementationProfiles,
} from "../src/facades/inspect-web-analysis.d.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
  readonly reject: (error: unknown) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  let rejectPromise: ((error: unknown) => void) | undefined;
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve;
    rejectPromise = reject;
  });
  return {
    promise,
    resolve: value => resolvePromise?.(value),
    reject: error => rejectPromise?.(error),
  };
}

const packageRequest: PackageImplementationProfileRequest = {
  kind: "package",
  workspaceGeneration: "workspace-1",
  packageId: "Example.Package",
  version: "1.2.3",
  targetFramework: "net11.0",
  assemblyName: "Example.dll",
  typeDefinitionId: "type:Example.Widget",
  stableSelectors: ["M(int)", "M(string)"],
};

const platformRequest: PlatformImplementationProfileRequest = {
  kind: "platform",
  workspaceGeneration: "workspace-1",
  targetFramework: "net11.0",
  platformVersion: "11.0.0",
  pack: "Microsoft.NETCore.App",
  assemblyFileName: "System.Private.CoreLib.dll",
  typeDefinitionId: "type:Example.Widget",
  stableSelectors: ["M(int)", "M(string)"],
};

function selection(
  current: () => boolean = () => true,
  selectors: ReadonlyArray<string> = ["M(int)", "M(string)"],
): ImplementationProfileFamilySelection {
  return {
    typeDefinitionId: "type:Example.Widget",
    display: "Example.Widget.M",
    members: selectors.map((stableSelector, index) => ({
      typeDefinitionId: "type:Example.Widget",
      stableSelector,
      display: stableSelector,
      bodyTokens: [0x06000001 + index],
      selected: index === 0,
    })),
    isCurrent: current,
  };
}

function method(
  key: string,
  metadataToken: number,
  display = key,
): BrowserImplementationProfileMethod {
  return {
    key,
    assemblyName: "Example",
    moduleVersionId: "11111111-1111-1111-1111-111111111111",
    declaringType: "Example.Widget",
    name: display,
    parameterTypes: [],
    returnType: "System.Void",
    metadataToken,
    isStatic: false,
    isExtension: false,
    callerUnsafeMode: "None",
    genericArity: 0,
    genericParameterNames: [],
    display,
  };
}

function profile(
  overrides: Partial<BrowserImplementationProfile> = {},
): BrowserImplementationProfile {
  return {
    methodKey: "logical-int",
    evidenceMethodKey: "logical-int",
    ilBytes: 20,
    instructionCount: 20,
    distinctOpcodeCount: 8,
    basicBlockCount: 3,
    branchCount: 2,
    conditionalBranchCount: 1,
    switchCount: 0,
    switchTargetCount: 0,
    normalFlowCyclomaticComplexity: 2,
    loopCount: 0,
    catchCount: 0,
    filterCount: 0,
    finallyCount: 0,
    faultCount: 0,
    localCount: 1,
    directCallCount: 2,
    distinctCalleeCount: 2,
    allocationCount: 0,
    throwCount: 0,
    async: false,
    unsafe: false,
    reflectionCallCount: 0,
    incomingOverloadCallerCount: 0,
    outgoingOverloadTargetCount: 1,
    isComplete: true,
    incompleteReasons: [],
    publicMembers: [{
      typeDefinitionId: "type:Example.Widget",
      member: "M",
      stableSelector: "M(int)",
      bodyTokens: [0x06000001],
    }],
    ...overrides,
  };
}

function content(
  overrides: Partial<BrowserImplementationProfileContent> = {},
): BrowserImplementationProfileContent {
  return {
    members: [{
      typeDefinitionId: "type:Example.Widget",
      member: "M",
      stableSelector: "M(int)",
      bodyTokens: [0x06000001, 0x06000003],
    }, {
      typeDefinitionId: "type:Example.Widget",
      member: "M",
      stableSelector: "M(string)",
      bodyTokens: [0x06000002],
    }],
    methods: [
      method("logical-int", 0x06000001, "Widget.M(int)"),
      method("logical-string", 0x06000002, "Widget.M(string)"),
      method("generated-int", 0x06000003, "Widget.<M>d__1.MoveNext()"),
    ],
    profiles: [
      profile(),
      profile({
        evidenceMethodKey: "generated-int",
        instructionCount: 10,
        branchCount: 1,
        async: true,
      }),
      profile({
        methodKey: "logical-string",
        evidenceMethodKey: "logical-string",
        instructionCount: 5,
        branchCount: 0,
        outgoingOverloadTargetCount: 0,
        publicMembers: [{
          typeDefinitionId: "type:Example.Widget",
          member: "M",
          stableSelector: "M(string)",
          bodyTokens: [0x06000002],
        }],
      }),
    ],
    coverage: {
      wasRequested: true,
      hasFullMethodEvidenceScope: false,
      declaredMethodKeys: ["logical-int", "logical-string"],
      managedMethodBodyKeys: [
        "logical-int",
        "logical-string",
        "generated-int",
      ],
      profiledEvidenceBodyKeys: [
        "logical-int",
        "logical-string",
        "generated-int",
      ],
      unavailableBodies: [],
      diagnostics: [],
    },
    overloadRelationships: [{
      callerKey: "logical-int",
      calleeKey: "logical-string",
      evidenceMethodKey: "generated-int",
      ilOffset: 12,
      kind: "Direct",
    }],
    generatedFrameworkTypes: ["Example.Widget+<M>d__1"],
    analysisDiagnostics: [],
    apiSurfaceInspectionFailures: [],
    ...overrides,
  };
}

function available(
  overrides: Partial<BrowserImplementationProfiles> = {},
): BrowserImplementationProfiles {
  return {
    schemaVersion: 2,
    outcome: "available",
    subject: {
      identity: {
        name: "Example",
        version: "1.0.0.0",
        culture: null,
        publicKeyToken: null,
      },
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      provenance: {
        kind: "Package",
        packageId: "Example.Package",
        packageVersion: "1.2.3",
        framework: "net11.0",
        frameworkVersion: null,
        runtimeIdentifier: null,
        assetPath: "lib/net11.0/Example.dll",
        resolverSource: null,
        project: null,
        contentRef: null,
        digest: null,
        declaredName: null,
      },
    },
    content: content(),
    failure: null,
    share: {
      kind: "Available",
      fullUrl: "https://example.test/share",
      packet: "packet",
      path: "implementation-profile-family/share",
      reason: null,
    },
    diagnostics: [],
    compileLibrary: {
      status: "Selected",
      targetFramework: "net11.0",
      message: null,
    },
    ...overrides,
  };
}

function ownerFailure(
  outcome: "rejected" | "failed" | "unavailable",
): BrowserImplementationProfiles {
  const unavailable = outcome === "unavailable";
  return {
    schemaVersion: 2,
    outcome,
    subject: unavailable ? null : available().subject,
    content: null,
    failure: {
      kind: `${outcome}-kind`,
      detail: `${outcome}-detail`,
      metadataRootReason: null,
    },
    share: unavailable ? null : available().share,
    diagnostics: [{
      code: "IP001",
      severity: 2,
      summary: `${outcome} diagnostic`,
      correspondence: null,
    }],
    compileLibrary: {
      status: unavailable ? "NoCompileAssets" : "Selected",
      targetFramework: "net11.0",
      message: unavailable ? "No compile Library is available." : null,
    },
  };
}

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

async function settle(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
}

test("package and platform cache keys include exact family identity", () => {
  const packageKey = implementationProfileCacheKey(packageRequest);
  const platformKey = implementationProfileCacheKey(platformRequest);

  assert.notEqual(packageKey, platformKey);
  for (const changed of [
    { ...packageRequest, workspaceGeneration: "workspace-2" },
    { ...packageRequest, packageId: "Other.Package" },
    { ...packageRequest, version: "1.2.4" },
    { ...packageRequest, targetFramework: "net10.0" },
    { ...packageRequest, assemblyName: "Other.dll" },
    { ...packageRequest, typeDefinitionId: "type:Example.Other" },
    { ...packageRequest, stableSelectors: ["M(int)", "M(object)"] },
  ])
    assert.notEqual(implementationProfileCacheKey(changed), packageKey);
  for (const changed of [
    { ...platformRequest, workspaceGeneration: "workspace-2" },
    { ...platformRequest, targetFramework: "net10.0" },
    { ...platformRequest, platformVersion: "10.0.0" },
    { ...platformRequest, pack: "Microsoft.WindowsDesktop.App" },
    { ...platformRequest, assemblyFileName: "System.Runtime.dll" },
    { ...platformRequest, typeDefinitionId: "type:Example.Other" },
    { ...platformRequest, stableSelectors: ["M(int)", "M(object)"] },
  ])
    assert.notEqual(implementationProfileCacheKey(changed), platformKey);
  assert.equal(
    implementationProfileCacheKey({
      ...packageRequest,
      stableSelectors: [
        packageRequest.stableSelectors[1]!,
        packageRequest.stableSelectors[0]!,
      ],
    }),
    packageKey,
  );
});

test("family cache keys reject incomplete or ambiguous selector sets", () => {
  assert.throws(
    () => implementationProfileCacheKey({
      ...packageRequest,
      stableSelectors: [packageRequest.stableSelectors[0]!],
    }),
    /at least two selectors/,
  );
  assert.throws(
    () => implementationProfileCacheKey({
      ...packageRequest,
      stableSelectors: [
        packageRequest.stableSelectors[0]!,
        packageRequest.stableSelectors[0]!,
      ],
    }),
    /must be distinct/,
  );
  assert.throws(
    () => implementationProfileCacheKey({
      ...packageRequest,
      stableSelectors: [packageRequest.stableSelectors[0]!, " "],
    }),
    /cannot be blank/,
  );
});

test("the cache is single-flight only for the same exact family", async () => {
  const cache = createImplementationProfileResultCache();
  const pending = deferred<BrowserImplementationProfiles>();
  let queries = 0;
  const producer = () => {
    queries++;
    return pending.promise;
  };

  const first = cache.load(packageRequest, producer);
  const second = cache.load(packageRequest, producer);
  assert.equal(first, second);
  assert.equal(queries, 1);
  assert.equal(cache.status(packageRequest), "in-flight");

  const result = available();
  pending.resolve(result);
  assert.equal(await first, result);
  assert.equal(await second, result);
  assert.equal(cache.status(packageRequest), "settled");
  assert.equal(
    await cache.load(packageRequest, producer),
    result,
  );
  assert.equal(queries, 1);
});

test("producer failure remains terminal until explicit retry", async () => {
  const cache = createImplementationProfileResultCache();
  let queries = 0;
  const failure = new Error("worker failed");
  const producer = () => {
    queries++;
    return queries === 1
      ? Promise.reject(failure)
      : Promise.resolve(available());
  };

  await assert.rejects(cache.load(packageRequest, producer), failure);
  assert.equal(cache.status(packageRequest), "producer-failed");
  await assert.rejects(cache.load(packageRequest, producer), failure);
  assert.equal(queries, 1);
  assert.equal(cache.retry(packageRequest), true);
  assert.equal(
    (await cache.load(packageRequest, producer)).outcome,
    "available",
  );
  assert.equal(queries, 2);
});

test("a mismatched producer family remains terminal until explicit retry", async () => {
  const cache = createImplementationProfileResultCache();
  const mismatched = available({
    content: content({
      members: [content().members[0]!],
    }),
  });

  await assert.rejects(
    cache.load(packageRequest, () => Promise.resolve(mismatched)),
    /does not match the requested family/,
  );
  assert.equal(cache.status(packageRequest), "producer-failed");
  await assert.rejects(
    cache.load(packageRequest, () => Promise.resolve(available())),
    /does not match the requested family/,
  );
  assert.equal(cache.retry(packageRequest), true);
  assert.equal(
    (await cache.load(
      packageRequest,
      () => Promise.resolve(available()),
    )).outcome,
    "available",
  );
});

test("family projection uses Type definition ID plus stable selector and keeps physical evidence separate", () => {
  const envelopeDiagnostic = {
    code: "IP100",
    severity: 1,
    summary: "Envelope diagnostic",
    correspondence: "method",
  } as const;
  const inspection = available({ diagnostics: [envelopeDiagnostic] });
  const relationship = inspection.content?.overloadRelationships[0];
  assert.ok(relationship);
  const projected = projectImplementationProfileFamily(
    inspection,
    selection(),
  );

  assert.equal(projected.status, "incomplete");
  if (projected.status !== "incomplete") return;
  assert.deepEqual(
    projected.family.rows.map(row => row.member.stableSelector),
    ["M(int)", "M(string)"],
  );
  assert.equal(projected.family.rows[0]?.physicalRows.length, 2);
  assert.equal(
    projected.family.rows[0]?.physicalRows[1]?.generatedEvidence,
    true,
  );
  assert.equal(
    projected.family.rows[0]?.physicalRows[1]?.evidenceMethod.key,
    "generated-int",
  );
  assert.deepEqual(
    projected.family.rows.flatMap(row =>
      row.physicalRows.map(physical =>
        physical.relativeInstructionPercent)),
    [100, 50, 25],
  );
  assert.equal(projected.family.relationships[0], relationship);
  assert.equal(
    projected.family.rows[0]?.physicalRows[1]?.relationships[0],
    relationship,
  );
  assert.equal(projected.family.diagnostics[0], envelopeDiagnostic);
  assert.equal(projected.family.share, inspection.share);
  assert.equal(projected.family.subject, inspection.subject);
  assert.ok(
    projected.family.rows[0]?.physicalRows[0]?.rawMetrics.some(metric =>
      metric.label === "Incoming sibling-overload callers"
      && metric.value === "0"),
  );
});

test("family projection never uses rendered display text as identity", () => {
  const original = selection();
  const renamed: ImplementationProfileFamilySelection = {
    ...original,
    display: "Completely different family display",
    members: original.members.map(member => ({
      ...member,
      display: `Different display for ${member.stableSelector}`,
    })),
  };
  const projected = projectImplementationProfileFamily(
    available(),
    renamed,
  );

  assert.equal(projected.status, "available");
  if (projected.status !== "available") return;
  assert.deepEqual(
    projected.family.rows.map(row => row.member.stableSelector),
    ["M(int)", "M(string)"],
  );
  assert.match(
    projected.family.rows[0]?.member.display ?? "",
    /Different display/,
  );
});

test("family projection rejects a returned roster that differs from the request", () => {
  assert.throws(
    () => projectImplementationProfileFamily(
      available({
        content: content({
          members: [content().members[0]!],
        }),
      }),
      selection(),
    ),
    /omitted selector 'M\(string\)'/,
  );
});

test("family projection rejects physical profiles attributed outside the request", () => {
  assert.throws(
    () => projectImplementationProfileFamily(
      available({
        content: content({
          profiles: [
            profile({
              publicMembers: [{
                typeDefinitionId: "type:Example.Other",
                member: "M",
                stableSelector: "M(int)",
                bodyTokens: [0x06000001],
              }],
            }),
          ],
        }),
      }),
      selection(),
    ),
    /outside the requested family/,
  );
});

test("family projection preserves relationships to bodyless siblings", () => {
  const projected = projectImplementationProfileFamily(
    available({
      content: content({
        profiles: [profile()],
        overloadRelationships: [{
          callerKey: "logical-int",
          calleeKey: "logical-string",
          evidenceMethodKey: "logical-int",
          ilOffset: 12,
          kind: "Direct",
        }],
      }),
    }),
    selection(),
  );

  assert.equal(projected.status, "available");
  if (projected.status !== "available") return;
  assert.equal(projected.family.relationships.length, 1);
  assert.equal(
    projected.family.rows[0]?.physicalRows[0]?.relationships.length,
    1,
  );
  assert.equal(projected.family.rows[1]?.physicalRows.length, 0);
});

test("family ordering uses largest physical body, roster ties, and measured-first placement", () => {
  const orderedSelection: ImplementationProfileFamilySelection = {
    typeDefinitionId: "type:Example.Widget",
    display: "Example.Widget.M",
    members: ["A", "B", "C", "D", "E"].map((stableSelector, index) => ({
      typeDefinitionId: "type:Example.Widget",
      stableSelector,
      display: stableSelector,
      bodyTokens: [0x06000010 + index],
      selected: false,
    })),
    isCurrent: () => true,
  };
  const attributedProfile = (
    stableSelector: string,
    methodKey: string,
    evidenceMethodKey: string,
    instructionCount: number,
  ) => profile({
    methodKey,
    evidenceMethodKey,
    instructionCount,
    publicMembers: [{
      typeDefinitionId: "type:Example.Widget",
      member: "M",
      stableSelector,
      bodyTokens: [],
    }],
  });
  const projected = projectImplementationProfileFamily(
    available({
      content: content({
        members: orderedSelection.members.map(member => ({
          typeDefinitionId: member.typeDefinitionId,
          member: "M",
          stableSelector: member.stableSelector,
          bodyTokens: [...member.bodyTokens],
        })),
        methods: [
          method("logical-a", 0x06000010),
          method("evidence-a-small", 0x06000011),
          method("evidence-a-large", 0x06000012),
          method("logical-b", 0x06000013),
          method("logical-c", 0x06000014),
        ],
        profiles: [
          attributedProfile("A", "logical-a", "evidence-a-small", 10),
          attributedProfile("B", "logical-b", "logical-b", 30),
          attributedProfile("C", "logical-c", "logical-c", 40),
          attributedProfile("A", "logical-a", "evidence-a-large", 30),
        ],
        overloadRelationships: [],
      }),
    }),
    orderedSelection,
  );

  assert.equal(projected.status, "available");
  if (projected.status !== "available") return;
  assert.deepEqual(
    projected.family.rows.map(row => row.member.stableSelector),
    ["C", "A", "B", "D", "E"],
  );
  assert.deepEqual(
    projected.family.rows[1]?.physicalRows.map(row =>
      row.profile.instructionCount),
    [30, 10],
  );
});

test("relative bars are suppressed for one row and uniformly tiny families", () => {
  const oneProfile = available({
    content: content({
      profiles: [profile()],
      overloadRelationships: [],
    }),
  });
  const one = projectImplementationProfileFamily(
    oneProfile,
    selection(),
  );
  assert.equal(one.status, "available");
  if (one.status === "available") {
    assert.equal(one.family.relativeBarsVisible, false);
    assert.equal(
      one.family.rows[0]?.physicalRows[0]?.relativeInstructionPercent,
      null,
    );
  }

  const tiny = available({
    content: content({
      profiles: [
        profile({ instructionCount: 8, branchCount: 0 }),
        profile({
          methodKey: "logical-string",
          evidenceMethodKey: "logical-string",
          instructionCount: 4,
          branchCount: 0,
          outgoingOverloadTargetCount: 0,
          publicMembers: [{
            typeDefinitionId: "type:Example.Widget",
            member: "M",
            stableSelector: "M(string)",
            bodyTokens: [0x06000002],
          }],
        }),
      ],
      overloadRelationships: [],
    }),
  });
  const projectedTiny = projectImplementationProfileFamily(tiny, selection());
  assert.equal(projectedTiny.status, "available");
  if (projectedTiny.status === "available")
    assert.equal(projectedTiny.family.relativeBarsVisible, false);

  const tinyContent = tiny.content;
  assert.ok(tinyContent);
  const tinyWithBranch = available({
    content: {
      ...tinyContent,
      profiles: tinyContent.profiles.map((item, index) =>
        index === 1 ? { ...item, branchCount: 1 } : item),
    },
  });
  const structural = projectImplementationProfileFamily(
    tinyWithBranch,
    selection(),
  );
  assert.equal(structural.status, "available");
  if (structural.status === "available")
    assert.equal(structural.family.relativeBarsVisible, true);
});

test("available, incomplete, empty, rejected, failed, and unavailable remain distinct", () => {
  assert.equal(
    projectImplementationProfileFamily(available(), selection()).status,
    "available",
  );
  assert.equal(
    projectImplementationProfileFamily(available({
      content: content({
        profiles: [profile({
          isComplete: false,
          incompleteReasons: ["Unsupported instruction"],
        })],
      }),
    }), selection()).status,
    "incomplete",
  );
  assert.equal(
    projectImplementationProfileFamily(available({
      content: content({ profiles: [], overloadRelationships: [] }),
    }), selection()).status,
    "empty",
  );
  assert.equal(
    projectImplementationProfileFamily(
      ownerFailure("rejected"),
      selection(),
    ).status,
    "rejected",
  );
  assert.equal(
    projectImplementationProfileFamily(
      ownerFailure("failed"),
      selection(),
    ).status,
    "failed",
  );
  assert.equal(
    projectImplementationProfileFamily(
      ownerFailure("unavailable"),
      selection(),
    ).status,
    "unavailable",
  );
});

test("operation authority suppresses stale publication while exact-family results remain reusable", async () => {
  const state: ImplementationProfileStateHost = {
    implementationProfiles: { status: "idle" },
  };
  const packagePending = deferred<BrowserImplementationProfiles>();
  const otherFamilyPending = deferred<BrowserImplementationProfiles>();
  const queries = new Map<string, number>();
  let packageCurrent = true;
  const otherFamilyRequest: PackageImplementationProfileRequest = {
    ...packageRequest,
    typeDefinitionId: "type:Example.OtherWidget",
    stableSelectors: ["N(int)", "N(string)"],
  };
  const coordinator = createImplementationProfileCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: request => {
      const key = implementationProfileCacheKey(request);
      queries.set(key, (queries.get(key) ?? 0) + 1);
      return request.typeDefinitionId === packageRequest.typeDefinitionId
        ? packagePending.promise
        : otherFamilyPending.promise;
    },
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  const readState = () => state.implementationProfiles;

  assert.equal(coordinator.hasActivated(packageRequest), false);
  const first = coordinator.activate(
    packageRequest,
    selection(() => packageCurrent),
  );
  assert.equal(coordinator.hasActivated(packageRequest), true);
  packageCurrent = false;
  const second = coordinator.activate(
    otherFamilyRequest,
    {
      ...selection(),
      typeDefinitionId: otherFamilyRequest.typeDefinitionId,
      members: selection().members.map(member => ({
        ...member,
        typeDefinitionId: otherFamilyRequest.typeDefinitionId,
        stableSelector: member.stableSelector.replace("M", "N"),
      })),
    },
  );
  packagePending.resolve(available());
  await first;
  await settle();
  const loadingState = readState();
  assert.equal(loadingState.status, "loading");
  if (loadingState.status === "loading")
    assert.equal(
      loadingState.request.typeDefinitionId,
      otherFamilyRequest.typeDefinitionId,
    );

  otherFamilyPending.resolve(ownerFailure("rejected"));
  await second;
  assert.equal(readState().status, "rejected");

  packageCurrent = true;
  await coordinator.activate(packageRequest, selection());
  assert.equal(readState().status, "available");
  assert.equal(
    queries.get(implementationProfileCacheKey(packageRequest)),
    1,
  );
});

test("unavailable body coverage remains visible without successful profile attribution", () => {
  const base = content();
  const failedToken = 0x06000004;
  const projected = projectImplementationProfileFamily(
    available({
      content: content({
        members: base.members.map(member =>
          member.stableSelector === "M(int)"
            ? { ...member, bodyTokens: [] }
            : member),
        profiles: [profile({
          methodKey: "logical-string",
          evidenceMethodKey: "logical-string",
          publicMembers: [{
            typeDefinitionId: "type:Example.Widget",
            member: "M",
            stableSelector: "M(string)",
            bodyTokens: [0x06000002],
          }],
        })],
        overloadRelationships: [],
        coverage: {
          ...base.coverage,
          profiledEvidenceBodyKeys: ["logical-string"],
          unavailableBodies: [{
            evidenceMethodKey: null,
            methodToken: failedToken,
            reason: "AnalysisFailed",
            diagnostic: {
              methodToken: failedToken,
              method: "Widget.M(int)",
              message: "Decoder failed.",
              sourceMethodToken: null,
              declaringType: "Example.Widget",
              sourceDeclaringType: null,
            },
          }],
        },
      }),
    }),
    selection(),
  );

  assert.equal(projected.status, "incomplete");
  if (projected.status !== "incomplete") return;
  const affected = projected.family.rows.find(
    row => row.member.stableSelector === "M(int)",
  );
  assert.equal(affected?.physicalRows.length, 0);
  assert.equal(affected?.unavailableBodies.length, 0);

  const html = renderImplementationProfileState(projected, escapeHtml);
  assert.match(html, /Unavailable physical bodies/);
  assert.match(html, new RegExp(`Method token ${failedToken}`));
  assert.match(html, /AnalysisFailed/);
  assert.match(html, /Decoder failed\./);
});

test("row-associated unavailable bodies remain visible beside physical profiles", () => {
  const base = content();
  const projected = projectImplementationProfileFamily(
    available({
      content: content({
        profiles: [profile()],
        overloadRelationships: [],
        coverage: {
          ...base.coverage,
          profiledEvidenceBodyKeys: ["logical-int"],
          unavailableBodies: [{
            evidenceMethodKey: "generated-int",
            methodToken: 0x06000003,
            reason: "AnalysisFailed",
            diagnostic: null,
          }],
        },
      }),
    }),
    selection(),
  );

  assert.equal(projected.status, "incomplete");
  if (projected.status !== "incomplete") return;
  const affected = projected.family.rows.find(
    row => row.member.stableSelector === "M(int)",
  );
  assert.equal(affected?.physicalRows.length, 1);
  assert.equal(affected?.unavailableBodies.length, 1);

  const html = renderImplementationProfileState(projected, escapeHtml);
  assert.match(html, /Additional unavailable physical evidence/);
  assert.match(html, /generated-int/);
  assert.match(html, /AnalysisFailed/);
});

test("the coordinator retains producer-failed state and retries only explicitly", async () => {
  const state: ImplementationProfileStateHost = {
    implementationProfiles: { status: "idle" },
  };
  let queries = 0;
  const coordinator = createImplementationProfileCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queries++;
      return queries === 1
        ? Promise.reject(new Error("<worker> failed"))
        : Promise.resolve(available());
    },
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });

  await coordinator.activate(packageRequest, selection());
  assert.equal(state.implementationProfiles.status, "producer-failed");
  await coordinator.activate(packageRequest, selection());
  assert.equal(state.implementationProfiles.status, "producer-failed");
  assert.equal(queries, 1);
  await coordinator.retry(packageRequest, selection());
  assert.equal(state.implementationProfiles.status, "available");
  assert.equal(queries, 2);
});

test("rendering exposes textual evidence, raw metrics, relationships, and accessible relative bars", () => {
  const projected = projectImplementationProfileFamily(
    available(),
    selection(),
  );
  assert.equal(projected.status, "available");
  if (projected.status !== "available") return;
  const html = renderImplementationProfileState(projected, escapeHtml);

  assert.match(html, /Generated physical body/);
  assert.match(html, /Branches: 2/);
  assert.match(html, /Async evidence/);
  assert.match(
    html,
    /role="img" aria-label="20 instructions; 100% of the largest physical body in this overload family"/,
  );
  assert.match(html, /Raw implementation metrics/);
  assert.match(html, /Distinct opcode count/);
  assert.match(html, /Incoming sibling-overload callers/);
  assert.match(html, /Exact sibling-overload relationships \(1\)/);
  assert.match(html, /logical-int/);
  assert.match(html, /logical-string/);
  assert.match(html, /Raw counts remain authoritative/);
});

test("rendering distinguishes incomplete and producer failure without relying on color", () => {
  const incomplete = projectImplementationProfileFamily(
    available({
      diagnostics: [{
        code: "IP<&",
        severity: 1,
        summary: "<partial>",
        correspondence: null,
      }],
    }),
    selection(),
  );
  assert.equal(incomplete.status, "incomplete");
  const incompleteHtml = renderImplementationProfileState(
    incomplete,
    escapeHtml,
  );
  assert.match(incompleteHtml, /Implementation profiles are incomplete/);
  assert.match(incompleteHtml, /IP&lt;&amp;/);
  assert.match(incompleteHtml, /&lt;partial&gt;/);

  const producerState = {
    status: "producer-failed",
    request: packageRequest,
    selection: selection(),
    error: new Error("<worker>"),
    message: "<worker> failed",
  } as const;
  const producerHtml = renderImplementationProfileState(
    producerState,
    escapeHtml,
  );
  assert.match(producerHtml, /Implementation profile producer failed/);
  assert.match(producerHtml, /&lt;worker&gt; failed/);
  assert.match(producerHtml, /data-implementation-profile-retry/);
});

test("different exact keys may execute independently", async () => {
  const cache = createImplementationProfileResultCache();
  const calls: ImplementationProfileFamilyRequest[] = [];
  const producer = (request: ImplementationProfileFamilyRequest) => {
    calls.push(request);
    return Promise.resolve(available());
  };

  await Promise.all([
    cache.load(packageRequest, producer),
    cache.load(platformRequest, producer),
  ]);
  assert.deepEqual(calls, [packageRequest, platformRequest]);
});
