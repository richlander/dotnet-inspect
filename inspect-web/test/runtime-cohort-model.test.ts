import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import {
  createRuntimeCohortBenchmarkReceipt,
  createRuntimeCohortReceipt,
  createRuntimeSiteDeploymentReceipt,
  type RuntimeCohortVariantName,
  type RuntimeVariantAdmissionEvidence,
  type RuntimeVariantReceipt,
  validateRuntimePinAdvancementCohort,
  validateRuntimeVariantPublicationEvidence,
} from "../scripts/runtime-cohort-model.ts";

const sourceCommit = "a".repeat(40);
const sha = "b".repeat(64);

function variant(
  name: RuntimeCohortVariantName,
  overrides: Partial<RuntimeVariantReceipt> = {},
): RuntimeVariantReceipt {
  const mono = name === "mono";
  const readyToRun = name === "coreclr-r2r";
  return {
    schema: 1,
    name,
    role: readyToRun ? "candidate" : "required",
    sourceCommit,
    frontendManifestSha256: sha,
    siteManifestSha256: name === "mono" ? "c".repeat(64) : "d".repeat(64),
    asyncLoweringReceiptSha256: "e".repeat(64),
    runtimeCohortReceiptSha256: mono ? null : "f".repeat(64),
    runtime: {
      implementation: mono ? "Mono" : "CoreCLR",
      sdkVersion: mono ? "11.0.100-rc.1.1" : "12.0.100-alpha.1.1",
      runtimeVersion: mono ? "11.0.0-rc.1.1" : "12.0.0-alpha.1.1",
      workload: mono ? "wasm-experimental" : "wasm-tools",
      vmrCommit: mono ? null : "1".repeat(40),
      nativeWasmSha256: mono ? null : "2".repeat(64),
      nativeJavaScriptSha256: mono ? null : "3".repeat(64),
    },
    configuration: {
      asyncLowering: mono ? "compiler" : "runtime",
      publishReadyToRun: readyToRun,
      publishReadyToRunComposite: readyToRun ? false : null,
      wasmBuildNative: false,
    },
    ...overrides,
  };
}

function evidence(
  name: RuntimeCohortVariantName,
  exitCode = 0,
  logText = "passed\n",
): RuntimeVariantAdmissionEvidence {
  return {
    receipt: variant(name),
    exitCode,
    logFile: `admission/${name}.log`,
    logSha256: createHash("sha256").update(logText).digest("hex"),
    logText,
  };
}

test("accepted cohort benchmarks every admitted variant", () => {
  const receipt = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r"),
    ],
  );
  assert.equal(receipt.status, "accepted");
  assert.deepEqual(receipt.benchmarkSites, [
    "mono",
    "coreclr-il",
    "coreclr-r2r",
  ]);
  assert.ok(receipt.variants.every(
    item => item.admission.status === "admitted",
  ));
  assert.doesNotThrow(() => validateRuntimePinAdvancementCohort(
    `${JSON.stringify(receipt)}\n`,
    sourceCommit,
  ));
});

test("known R2R thunk rejection preserves the required comparison", () => {
  const knownFailure = [
    "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:operation-error",
    "Fatal error.",
    "Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.",
    "RuntimeError: index out of bounds",
  ].join("\n");
  const receipt = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, knownFailure),
    ],
  );
  assert.equal(receipt.status, "accepted");
  assert.deepEqual(receipt.benchmarkSites, ["mono", "coreclr-il"]);
  assert.equal(
    receipt.variants[2]?.admission.status,
    "correctness-rejection",
  );
  assert.equal(
    receipt.variants[2]?.admission.knownIssue,
    "dotnet/runtime#129622; dotnet/runtime#129857",
  );
  assert.doesNotThrow(() => validateRuntimePinAdvancementCohort(
    `${JSON.stringify(receipt)}\n`,
    sourceCommit,
  ));
});

test("unknown candidate and required failures reject the cohort", () => {
  const unknownCandidate = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, "Browser executable missing.\n"),
    ],
  );
  assert.equal(unknownCandidate.status, "failed");
  assert.equal(
    unknownCandidate.variants[2]?.admission.status,
    "evaluation-failed",
  );

  const requiredFailure = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono", 1, "Product operation failed.\n"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r"),
    ],
  );
  assert.equal(requiredFailure.status, "failed");
});

test("runtime pin advancement rejects unfamiliar R2R product failures", () => {
  const retainedFailure = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, [
        "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:producer-contract",
        "Error: page.evaluate: index out of bounds",
      ].join("\n")),
    ],
  );
  assert.doesNotThrow(() => validateRuntimePinAdvancementCohort(
    `${JSON.stringify(retainedFailure)}\n`,
    sourceCommit,
  ));

  const unfamiliarFailure = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, [
        "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:producer-contract",
        "Error: page.evaluate: unreachable",
      ].join("\n")),
    ],
  );
  assert.equal(unfamiliarFailure.status, "accepted");
  assert.equal(
    unfamiliarFailure.variants[2]?.admission.knownIssue,
    null,
  );
  assert.throws(
    () => validateRuntimePinAdvancementCohort(
      `${JSON.stringify(unfamiliarFailure)}\n`,
      sourceCommit,
    ),
    /recognized retained R2R issue/u,
  );
  assert.throws(
    () => validateRuntimePinAdvancementCohort(
      `${JSON.stringify(retainedFailure)}\n`,
      "b".repeat(40),
    ),
    /proposal source commit/u,
  );
});

test("runtime sites require exact accepted deployment evidence", () => {
  const knownFailure = [
    "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:producer-contract",
    "Error: page.evaluate: index out of bounds",
  ].join("\n");
  const cohort = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-14T00:47:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, knownFailure),
    ],
  );
  const cohortText = `${JSON.stringify(cohort)}\n`;
  const il = createRuntimeSiteDeploymentReceipt(
    cohortText,
    `${JSON.stringify(variant("coreclr-il"))}\n`,
    sourceCommit,
    "36090530150",
    2,
  );
  assert.equal(il.admission.status, "admitted");
  assert.deepEqual(il.candidate, {
    runId: "36090530150",
    attempt: 2,
  });
  const r2r = createRuntimeSiteDeploymentReceipt(
    cohortText,
    `${JSON.stringify(variant("coreclr-r2r"))}\n`,
    sourceCommit,
    "36090530150",
    2,
  );
  assert.equal(r2r.admission.status, "correctness-rejection");
  assert.equal(
    r2r.admission.knownIssue,
    "dotnet/runtime#129622; dotnet/runtime#129857",
  );

  const unfamiliar = {
    ...cohort,
    variants: cohort.variants.map(item => item.name === "coreclr-r2r"
      ? {
        ...item,
        admission: {
          ...item.admission,
          knownIssue: null,
        },
      }
      : item),
  };
  assert.throws(
    () => createRuntimeSiteDeploymentReceipt(
      JSON.stringify(unfamiliar),
      JSON.stringify(variant("coreclr-r2r")),
      sourceCommit,
      "36090530150",
      2,
    ),
    /deployable admission/u,
  );
  assert.throws(
    () => createRuntimeSiteDeploymentReceipt(
      cohortText,
      JSON.stringify(variant("coreclr-il")),
      "9".repeat(40),
      "36090530150",
      2,
    ),
    /deployment source commit/u,
  );
  const mismatchedVariant = variant("coreclr-il", {
    siteManifestSha256: "a".repeat(64),
  });
  assert.throws(
    () => createRuntimeSiteDeploymentReceipt(
      cohortText,
      JSON.stringify(mismatchedVariant),
      sourceCommit,
      "36090530150",
      2,
    ),
    /publication does not match/u,
  );
  assert.throws(
    () => createRuntimeSiteDeploymentReceipt(
      cohortText,
      JSON.stringify(variant("coreclr-il")),
      sourceCommit,
      "0",
      2,
    ),
    /run ID/u,
  );
  assert.throws(
    () => createRuntimeSiteDeploymentReceipt(
      cohortText,
      JSON.stringify(variant("coreclr-il")),
      sourceCommit,
      "36090530150",
      0,
    ),
    /attempt/u,
  );
});

test("cohort rejects identity and configuration drift", () => {
  assert.throws(
    () => createRuntimeCohortReceipt(
      sourceCommit,
      "2026-09-12T00:00:00Z",
      [
        evidence("mono"),
        evidence("coreclr-il"),
        {
          ...evidence("coreclr-r2r"),
          receipt: variant("coreclr-r2r", {
            frontendManifestSha256: "9".repeat(64),
          }),
        },
      ],
    ),
    /one frontend artifact/u,
  );
  assert.throws(
    () => createRuntimeCohortReceipt(
      sourceCommit,
      "2026-09-12T00:00:00Z",
      [
        evidence("mono"),
        evidence("coreclr-il"),
        {
          ...evidence("coreclr-r2r"),
          receipt: variant("coreclr-r2r", {
            runtime: {
              ...variant("coreclr-r2r").runtime,
              runtimeVersion: "12.0.0-alpha.1.2",
            },
          }),
        },
      ],
    ),
    /one runtime cohort/u,
  );
});

test("benchmark receipt binds accepted report and trend to the cohort", () => {
  const cohort = createRuntimeCohortReceipt(
    sourceCommit,
    "2026-09-12T00:00:00Z",
    [
      evidence("mono"),
      evidence("coreclr-il"),
      evidence("coreclr-r2r", 1, [
        "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:operation-error",
        "Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.",
        "RuntimeError: index out of bounds",
      ].join("\n")),
    ],
  );
  const cohortText = `${JSON.stringify(cohort, null, 2)}\n`;
  const report = {
    comparison: { comparable: true },
    summaries: [{ site: "mono" }, { site: "coreclr-il" }],
  };
  const reportText = `${JSON.stringify(report, null, 2)}\n`;
  const trendText = `${JSON.stringify({
    productCommit: sourceCommit,
    sourceReportSha256: createHash("sha256").update(reportText).digest("hex"),
    summaries: report.summaries,
  }, null, 2)}\n`;
  const receipt = createRuntimeCohortBenchmarkReceipt(
    cohortText,
    reportText,
    trendText,
  );
  assert.equal(receipt.sourceCommit, sourceCommit);
  assert.deepEqual(receipt.sites, ["mono", "coreclr-il"]);
  assert.match(receipt.cohortReceiptSha256, /^[0-9a-f]{64}$/u);

  assert.throws(
    () => createRuntimeCohortBenchmarkReceipt(
      cohortText,
      reportText,
      trendText.replace("\"coreclr-il\"", "\"coreclr-r2r\""),
    ),
    /Trend sites/u,
  );
});

test("publication evidence replays receipt digests and CoreCLR identity", () => {
  const frontendManifestText = "frontend\n";
  const siteManifestText = "site\n";
  const asyncLoweringReceiptText = "{}\n";
  const runtimeCohortReceiptText = `${JSON.stringify({
    sdk: { version: "12.0.100-alpha.1.1" },
    runtime: {
      version: "12.0.0-alpha.1.1",
      implementation: "CoreCLR",
      source: { commit: "1".repeat(40) },
      assets: {
        nativeWasmSha256: "2".repeat(64),
        nativeJavaScriptSha256: "3".repeat(64),
      },
    },
    configuration: {
      asyncLowering: "runtime",
      publishReadyToRun: false,
      wasmBuildNative: false,
    },
  }, null, 2)}\n`;
  const receipt = variant("coreclr-il", {
    frontendManifestSha256: createHash("sha256")
      .update(frontendManifestText).digest("hex"),
    siteManifestSha256: createHash("sha256")
      .update(siteManifestText).digest("hex"),
    asyncLoweringReceiptSha256: createHash("sha256")
      .update(asyncLoweringReceiptText).digest("hex"),
    runtimeCohortReceiptSha256: createHash("sha256")
      .update(runtimeCohortReceiptText).digest("hex"),
  });
  const publicationEvidence = {
    frontendManifestText,
    siteManifestText,
    asyncLoweringReceiptText,
    runtimeCohortReceiptText,
  };
  validateRuntimeVariantPublicationEvidence(receipt, publicationEvidence);
  assert.throws(
    () => validateRuntimeVariantPublicationEvidence(receipt, {
      ...publicationEvidence,
      siteManifestText: "mutated\n",
    }),
    /site manifest digest/u,
  );
});
