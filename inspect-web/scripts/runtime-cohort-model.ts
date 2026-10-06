import { createHash } from "node:crypto";

export const runtimeCohortVariantNames = [
  "mono",
  "coreclr-il",
  "coreclr-r2r",
] as const;

export type RuntimeCohortVariantName =
  typeof runtimeCohortVariantNames[number];

export interface RuntimeVariantReceipt {
  readonly schema: 1;
  readonly name: RuntimeCohortVariantName;
  readonly role: "required" | "candidate";
  readonly sourceCommit: string;
  readonly frontendManifestSha256: string;
  readonly siteManifestSha256: string;
  readonly asyncLoweringReceiptSha256: string;
  readonly runtimeCohortReceiptSha256: string | null;
  readonly runtime: {
    readonly implementation: "Mono" | "CoreCLR";
    readonly sdkVersion: string;
    readonly runtimeVersion: string;
    readonly workload: string;
    readonly vmrCommit: string | null;
    readonly nativeWasmSha256: string | null;
    readonly nativeJavaScriptSha256: string | null;
  };
  readonly configuration: {
    readonly asyncLowering: "compiler" | "runtime";
    readonly publishReadyToRun: boolean;
    readonly publishReadyToRunComposite: boolean | null;
    readonly wasmBuildNative: false;
  };
}

export interface RuntimeVariantAdmissionEvidence {
  readonly receipt: RuntimeVariantReceipt;
  readonly exitCode: number;
  readonly logFile: string;
  readonly logSha256: string;
  readonly logText: string;
}

export interface RuntimeVariantPublicationEvidence {
  readonly frontendManifestText: string;
  readonly siteManifestText: string;
  readonly asyncLoweringReceiptText: string;
  readonly runtimeCohortReceiptText: string | null;
}

export interface RuntimeCohortVariant {
  readonly name: RuntimeCohortVariantName;
  readonly role: "required" | "candidate";
  readonly publication: RuntimeVariantReceipt;
  readonly admission: {
    readonly status:
      | "admitted"
      | "correctness-rejection"
      | "evaluation-failed";
    readonly exitCode: number;
    readonly logFile: string;
    readonly logSha256: string;
    readonly knownIssue: string | null;
  };
}

export interface RuntimeCohortReceipt {
  readonly schema: 1;
  readonly generatedAtUtc: string;
  readonly sourceCommit: string;
  readonly status: "accepted" | "failed";
  readonly benchmarkSites: readonly RuntimeCohortVariantName[];
  readonly variants: readonly RuntimeCohortVariant[];
}

export interface RuntimeCohortBenchmarkReceipt {
  readonly schema: 1;
  readonly generatedAtUtc: string;
  readonly sourceCommit: string;
  readonly sites: readonly RuntimeCohortVariantName[];
  readonly cohortReceiptSha256: string;
  readonly reportSha256: string;
  readonly trendPointSha256: string;
}

export interface RuntimeSiteDeploymentReceipt {
  readonly schema: 2;
  readonly site: "coreclr-il" | "coreclr-r2r";
  readonly sourceCommit: string;
  readonly candidate: {
    readonly runId: string;
    readonly attempt: number;
  };
  readonly cohortGeneratedAtUtc: string;
  readonly frontendManifestSha256: string;
  readonly siteManifestSha256: string;
  readonly runtime: RuntimeVariantReceipt["runtime"];
  readonly configuration: RuntimeVariantReceipt["configuration"];
  readonly admission: RuntimeCohortVariant["admission"];
}

const sha256Pattern = /^[0-9a-f]{64}$/u;
const commitPattern = /^[0-9a-f]{40,64}$/u;
const knownR2RIssue =
  "dotnet/runtime#129622; dotnet/runtime#129857";
const knownR2RManagedCallFailure =
  "Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.";
const knownR2RProducerFailure =
  "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:producer-contract";
const knownR2RBoundsFailure = "index out of bounds";
const productOperationFailureFragment =
  "INSPECT_WEB_PRODUCT_OPERATION_FAILURE:";

function sha256(text: string): string {
  return createHash("sha256").update(text).digest("hex");
}

function requireCondition(
  condition: unknown,
  message: string,
): asserts condition {
  if (!condition) throw new Error(message);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isUnknownArray(value: unknown): value is unknown[] {
  return Array.isArray(value);
}

function isRuntimeCohortVariantName(
  value: unknown,
): value is RuntimeCohortVariantName {
  return typeof value === "string"
    && runtimeCohortVariantNames.some(name => name === value);
}

function requireString(
  value: unknown,
  description: string,
): asserts value is string {
  requireCondition(
    typeof value === "string" && value.length > 0,
    `${description} must be a non-empty string.`,
  );
}

function requireSha256(value: unknown, description: string): asserts value is string {
  requireCondition(
    typeof value === "string" && sha256Pattern.test(value),
    `${description} must be a lowercase SHA-256 digest.`,
  );
}

export function parseRuntimeVariantReceipt(
  value: unknown,
): RuntimeVariantReceipt {
  requireCondition(isRecord(value), "Runtime variant receipt must be an object.");
  requireCondition(value.schema === 1, "Runtime variant receipt schema must be 1.");
  requireCondition(
    isRuntimeCohortVariantName(value.name),
    "Runtime variant receipt has an unknown name.",
  );
  const name = value.name;
  const expectedRole = name === "coreclr-r2r" ? "candidate" : "required";
  requireCondition(
    value.role === expectedRole,
    `${name} must have role '${expectedRole}'.`,
  );
  const sourceCommit = value.sourceCommit;
  requireCondition(
    typeof sourceCommit === "string"
      && commitPattern.test(sourceCommit),
    `${name} sourceCommit must be a lowercase Git commit.`,
  );
  const frontendManifestSha256 = value.frontendManifestSha256;
  const siteManifestSha256 = value.siteManifestSha256;
  const asyncLoweringReceiptSha256 = value.asyncLoweringReceiptSha256;
  requireSha256(frontendManifestSha256, `${name} frontend manifest`);
  requireSha256(siteManifestSha256, `${name} site manifest`);
  requireSha256(asyncLoweringReceiptSha256, `${name} async receipt`);
  const runtimeCohortReceiptSha256 = value.runtimeCohortReceiptSha256;
  requireCondition(
    runtimeCohortReceiptSha256 === null
      || (typeof runtimeCohortReceiptSha256 === "string"
        && sha256Pattern.test(runtimeCohortReceiptSha256)),
    `${name} runtime cohort receipt must be null or a SHA-256 digest.`,
  );
  const runtime = value.runtime;
  requireCondition(isRecord(runtime), `${name} runtime must be an object.`);
  const sdkVersion = runtime.sdkVersion;
  const runtimeVersion = runtime.runtimeVersion;
  const workload = runtime.workload;
  requireString(sdkVersion, `${name} SDK version`);
  requireString(runtimeVersion, `${name} runtime version`);
  requireString(workload, `${name} workload`);
  const vmrCommit = runtime.vmrCommit;
  requireCondition(
    vmrCommit === null
      || (typeof vmrCommit === "string"
        && commitPattern.test(vmrCommit)),
    `${name} VMR commit must be null or a lowercase Git commit.`,
  );
  const nativeWasmSha256 = runtime.nativeWasmSha256;
  const nativeJavaScriptSha256 = runtime.nativeJavaScriptSha256;
  requireCondition(
    nativeWasmSha256 === null
      || (typeof nativeWasmSha256 === "string"
        && sha256Pattern.test(nativeWasmSha256)),
    `${name} native Wasm digest must be null or a SHA-256 digest.`,
  );
  requireCondition(
    nativeJavaScriptSha256 === null
      || (typeof nativeJavaScriptSha256 === "string"
        && sha256Pattern.test(nativeJavaScriptSha256)),
    `${name} native JavaScript digest must be null or a SHA-256 digest.`,
  );
  const configuration = value.configuration;
  requireCondition(
    isRecord(configuration),
    `${name} configuration must be an object.`,
  );
  requireCondition(
    configuration.wasmBuildNative === false,
    `${name} must keep WasmBuildNative disabled.`,
  );

  if (name === "mono") {
    requireCondition(
      runtime.implementation === "Mono",
      "mono must use the Mono runtime.",
    );
    requireCondition(
      vmrCommit === null,
      "mono must not declare a CoreCLR VMR commit.",
    );
    requireCondition(
      nativeWasmSha256 === null && nativeJavaScriptSha256 === null,
      "mono must not declare CoreCLR native asset digests.",
    );
    requireCondition(
      runtimeCohortReceiptSha256 === null,
      "mono must not declare a CoreCLR runtime cohort receipt.",
    );
    requireCondition(
      configuration.asyncLowering === "compiler",
      "mono must use compiler async lowering.",
    );
    requireCondition(
      configuration.publishReadyToRun === false
        && configuration.publishReadyToRunComposite === null,
      "mono must not publish ReadyToRun.",
    );
  } else {
    requireCondition(
      runtime.implementation === "CoreCLR",
      `${name} must use the CoreCLR runtime.`,
    );
    requireCondition(
      typeof vmrCommit === "string",
      `${name} must declare the CoreCLR VMR commit.`,
    );
    requireCondition(
      typeof nativeWasmSha256 === "string"
        && typeof nativeJavaScriptSha256 === "string",
      `${name} must declare CoreCLR native asset digests.`,
    );
    requireCondition(
      typeof runtimeCohortReceiptSha256 === "string",
      `${name} must declare its runtime cohort receipt.`,
    );
    requireCondition(
      configuration.asyncLowering === "runtime",
      `${name} must use runtime async lowering.`,
    );
    const readyToRun = name === "coreclr-r2r";
    requireCondition(
      configuration.publishReadyToRun === readyToRun,
      `${name} has the wrong ReadyToRun setting.`,
    );
    requireCondition(
      configuration.publishReadyToRunComposite
        === (readyToRun ? false : null),
      `${name} has the wrong composite ReadyToRun setting.`,
    );
  }

  return {
    schema: 1,
    name,
    role: expectedRole,
    sourceCommit,
    frontendManifestSha256,
    siteManifestSha256,
    asyncLoweringReceiptSha256,
    runtimeCohortReceiptSha256,
    runtime: {
      implementation: name === "mono" ? "Mono" : "CoreCLR",
      sdkVersion,
      runtimeVersion,
      workload,
      vmrCommit,
      nativeWasmSha256,
      nativeJavaScriptSha256,
    },
    configuration: {
      asyncLowering: name === "mono" ? "compiler" : "runtime",
      publishReadyToRun: name === "coreclr-r2r",
      publishReadyToRunComposite: name === "coreclr-r2r" ? false : null,
      wasmBuildNative: false,
    },
  };
}

export function validateRuntimeVariantPublicationEvidence(
  receipt: RuntimeVariantReceipt,
  evidence: RuntimeVariantPublicationEvidence,
): void {
  requireCondition(
    sha256(evidence.frontendManifestText)
      === receipt.frontendManifestSha256,
    `${receipt.name} frontend manifest digest does not match its receipt.`,
  );
  requireCondition(
    sha256(evidence.siteManifestText) === receipt.siteManifestSha256,
    `${receipt.name} site manifest digest does not match its receipt.`,
  );
  requireCondition(
    sha256(evidence.asyncLoweringReceiptText)
      === receipt.asyncLoweringReceiptSha256,
    `${receipt.name} async receipt digest does not match its receipt.`,
  );

  if (receipt.name === "mono") {
    requireCondition(
      evidence.runtimeCohortReceiptText === null,
      "mono must not carry a CoreCLR runtime cohort receipt.",
    );
    return;
  }

  requireCondition(
    evidence.runtimeCohortReceiptText !== null,
    `${receipt.name} must carry a CoreCLR runtime cohort receipt.`,
  );
  requireCondition(
    sha256(evidence.runtimeCohortReceiptText)
      === receipt.runtimeCohortReceiptSha256,
    `${receipt.name} runtime cohort digest does not match its receipt.`,
  );
  const runtimeCohort: unknown = JSON.parse(evidence.runtimeCohortReceiptText);
  requireCondition(
    isRecord(runtimeCohort),
    `${receipt.name} runtime cohort receipt must be an object.`,
  );
  const sdk = runtimeCohort.sdk;
  const runtime = runtimeCohort.runtime;
  const configuration = runtimeCohort.configuration;
  requireCondition(
    isRecord(sdk)
      && sdk.version === receipt.runtime.sdkVersion,
    `${receipt.name} runtime cohort SDK does not match its variant receipt.`,
  );
  requireCondition(
    isRecord(runtime)
      && runtime.version === receipt.runtime.runtimeVersion
      && runtime.implementation === receipt.runtime.implementation,
    `${receipt.name} runtime cohort does not match its variant receipt.`,
  );
  const source = runtime.source;
  requireCondition(
    isRecord(source) && source.commit === receipt.runtime.vmrCommit,
    `${receipt.name} VMR commit does not match its variant receipt.`,
  );
  const assets = runtime.assets;
  requireCondition(
    isRecord(assets)
      && assets.nativeWasmSha256 === receipt.runtime.nativeWasmSha256
      && assets.nativeJavaScriptSha256
        === receipt.runtime.nativeJavaScriptSha256,
    `${receipt.name} native asset digests do not match its variant receipt.`,
  );
  requireCondition(
    isRecord(configuration)
      && configuration.asyncLowering
        === receipt.configuration.asyncLowering
      && configuration.publishReadyToRun
        === receipt.configuration.publishReadyToRun
      && configuration.wasmBuildNative === false,
    `${receipt.name} runtime configuration does not match its variant receipt.`,
  );
  if (receipt.name === "coreclr-r2r") {
    requireCondition(
      configuration.publishReadyToRunComposite === false
        && configuration.readyToRunContainer === "wasm",
      "coreclr-r2r must use non-composite Wasm ReadyToRun.",
    );
  } else {
    requireCondition(
      !Object.hasOwn(configuration, "publishReadyToRunComposite")
        && !Object.hasOwn(configuration, "readyToRunContainer"),
      "coreclr-il must not declare ReadyToRun container settings.",
    );
  }
}

function classifyAdmission(
  evidence: RuntimeVariantAdmissionEvidence,
): RuntimeCohortVariant["admission"] {
  const common = {
    exitCode: evidence.exitCode,
    logFile: evidence.logFile,
    logSha256: evidence.logSha256,
  };
  if (evidence.exitCode === 0) {
    return {
      ...common,
      status: "admitted",
      knownIssue: null,
    };
  }
  const productCorrectnessRejection = evidence.receipt.name === "coreclr-r2r"
    && evidence.logText.includes(productOperationFailureFragment);
  if (productCorrectnessRejection) {
    const knownIssue = evidence.logText.includes(knownR2RManagedCallFailure)
      || (
        evidence.logText.includes(knownR2RProducerFailure)
        && evidence.logText.includes(knownR2RBoundsFailure)
      )
      ? knownR2RIssue
      : null;
    return {
      ...common,
      status: "correctness-rejection",
      knownIssue,
    };
  }
  return {
    ...common,
    status: "evaluation-failed",
    knownIssue: null,
  };
}

export function createRuntimeCohortReceipt(
  sourceCommit: string,
  generatedAtUtc: string,
  evidence: readonly RuntimeVariantAdmissionEvidence[],
): RuntimeCohortReceipt {
  requireCondition(
    commitPattern.test(sourceCommit),
    "Cohort source commit must be a lowercase Git commit.",
  );
  requireCondition(
    Number.isFinite(Date.parse(generatedAtUtc)),
    "Cohort generatedAtUtc must be a timestamp.",
  );
  requireCondition(
    evidence.length === runtimeCohortVariantNames.length,
    "Cohort evidence must contain exactly three variants.",
  );

  const byName = new Map<RuntimeCohortVariantName, RuntimeVariantAdmissionEvidence>();
  for (const item of evidence) {
    const receipt = parseRuntimeVariantReceipt(item.receipt);
    requireCondition(
      receipt.sourceCommit === sourceCommit,
      `${receipt.name} does not match the cohort source commit.`,
    );
    requireCondition(
      Number.isInteger(item.exitCode) && item.exitCode >= 0,
      `${receipt.name} admission exit code must be a non-negative integer.`,
    );
    requireString(item.logFile, `${receipt.name} admission log file`);
    requireSha256(item.logSha256, `${receipt.name} admission log`);
    requireCondition(
      item.logSha256 === sha256(item.logText),
      `${receipt.name} admission log digest does not match its contents.`,
    );
    requireCondition(
      !byName.has(receipt.name),
      `Cohort variant '${receipt.name}' is duplicated.`,
    );
    byName.set(receipt.name, { ...item, receipt });
  }

  const orderedEvidence = runtimeCohortVariantNames.map(name => {
    const item = byName.get(name);
    requireCondition(item !== undefined, `Cohort variant '${name}' is missing.`);
    return item;
  });
  const frontendDigests = new Set(
    orderedEvidence.map(item => item.receipt.frontendManifestSha256),
  );
  requireCondition(
    frontendDigests.size === 1,
    "Cohort variants must use one frontend artifact.",
  );
  const [coreclrIl, coreclrR2R] = orderedEvidence.slice(1).map(
    item => item.receipt.runtime,
  );
  requireCondition(
    coreclrIl !== undefined && coreclrR2R !== undefined
      && coreclrIl.sdkVersion === coreclrR2R.sdkVersion
      && coreclrIl.runtimeVersion === coreclrR2R.runtimeVersion
      && coreclrIl.workload === coreclrR2R.workload
      && coreclrIl.vmrCommit === coreclrR2R.vmrCommit
      && coreclrIl.nativeWasmSha256 === coreclrR2R.nativeWasmSha256
      && coreclrIl.nativeJavaScriptSha256
        === coreclrR2R.nativeJavaScriptSha256,
    "CoreCLR IL and R2R must use one runtime cohort.",
  );

  const variants = orderedEvidence.map(item => ({
    name: item.receipt.name,
    role: item.receipt.role,
    publication: item.receipt,
    admission: classifyAdmission(item),
  }));
  const requiredAdmitted = variants
    .filter(variant => variant.role === "required")
    .every(variant => variant.admission.status === "admitted");
  const evaluationFailed = variants.some(
    variant => variant.admission.status === "evaluation-failed",
  );
  return {
    schema: 1,
    generatedAtUtc,
    sourceCommit,
    status: requiredAdmitted && !evaluationFailed ? "accepted" : "failed",
    benchmarkSites: variants
      .filter(variant => variant.admission.status === "admitted")
      .map(variant => variant.name),
    variants,
  };
}

export function validateRuntimePinAdvancementCohort(
  cohortText: string,
  expectedSourceCommit: string,
): void {
  requireCondition(
    commitPattern.test(expectedSourceCommit),
    "Expected cohort source commit must be a lowercase Git commit.",
  );
  const cohort: unknown = JSON.parse(cohortText);
  requireCondition(isRecord(cohort), "Cohort receipt must be an object.");
  requireCondition(cohort.schema === 1, "Cohort receipt schema must be 1.");
  requireCondition(
    cohort.sourceCommit === expectedSourceCommit,
    "Cohort receipt does not match the proposal source commit.",
  );
  requireCondition(
    cohort.status === "accepted",
    "Runtime pin advancement requires an accepted cohort.",
  );
  requireCondition(
    isUnknownArray(cohort.variants),
    "Cohort variants must be an array.",
  );
  const r2rVariants = cohort.variants.filter(variant =>
    isRecord(variant) && variant.name === "coreclr-r2r"
  );
  requireCondition(
    r2rVariants.length === 1,
    "Cohort must contain exactly one coreclr-r2r variant.",
  );
  const r2rVariant = r2rVariants[0];
  requireCondition(
    isRecord(r2rVariant),
    "CoreCLR R2R variant must be an object.",
  );
  const r2rAdmission = r2rVariant.admission;
  requireCondition(
    isRecord(r2rAdmission),
    "CoreCLR R2R admission must be an object.",
  );
  if (r2rAdmission.status === "admitted") return;
  requireCondition(
    r2rAdmission.status === "correctness-rejection"
      && r2rAdmission.knownIssue === knownR2RIssue,
    "Runtime pin advancement requires admitted R2R or the recognized retained R2R issue.",
  );
}

export function createRuntimeSiteDeploymentReceipt(
  cohortText: string,
  variantText: string,
  expectedSourceCommit: string,
  candidateRunId: string,
  candidateAttempt: number,
): RuntimeSiteDeploymentReceipt {
  requireCondition(
    commitPattern.test(expectedSourceCommit),
    "Expected deployment source commit must be a lowercase Git commit.",
  );
  requireCondition(
    /^[1-9][0-9]*$/u.test(candidateRunId),
    "Candidate run ID must be a positive decimal integer.",
  );
  requireCondition(
    Number.isInteger(candidateAttempt) && candidateAttempt > 0,
    "Candidate attempt must be a positive integer.",
  );
  const variant = parseRuntimeVariantReceipt(JSON.parse(variantText));
  requireCondition(
    variant.name === "coreclr-il" || variant.name === "coreclr-r2r",
    "Only CoreCLR variants can become public runtime sites.",
  );
  requireCondition(
    variant.sourceCommit === expectedSourceCommit,
    "Runtime site variant does not match the deployment source commit.",
  );

  const cohort: unknown = JSON.parse(cohortText);
  requireCondition(isRecord(cohort), "Cohort receipt must be an object.");
  requireCondition(cohort.schema === 1, "Cohort receipt schema must be 1.");
  requireCondition(
    cohort.status === "accepted",
    "Runtime site deployment requires an accepted cohort.",
  );
  requireCondition(
    cohort.sourceCommit === expectedSourceCommit,
    "Cohort receipt does not match the deployment source commit.",
  );
  requireString(cohort.generatedAtUtc, "Cohort generation time");
  requireCondition(
    Number.isFinite(Date.parse(cohort.generatedAtUtc)),
    "Cohort generatedAtUtc must be a timestamp.",
  );
  requireCondition(
    isUnknownArray(cohort.variants),
    "Cohort variants must be an array.",
  );
  const matches = cohort.variants.filter(item =>
    isRecord(item) && item.name === variant.name
  );
  requireCondition(
    matches.length === 1,
    `Cohort must contain exactly one ${variant.name} variant.`,
  );
  const cohortVariant = matches[0];
  requireCondition(isRecord(cohortVariant), "Cohort variant must be an object.");
  requireCondition(
    JSON.stringify(cohortVariant.publication) === JSON.stringify(variant),
    `${variant.name} publication does not match the cohort receipt.`,
  );
  const admission = cohortVariant.admission;
  requireCondition(
    isRecord(admission),
    `${variant.name} admission must be an object.`,
  );
  const status = admission.status;
  requireCondition(
    status === "admitted"
      || status === "correctness-rejection"
      || status === "evaluation-failed",
    `${variant.name} admission status is invalid.`,
  );
  const knownIssue = admission.knownIssue;
  requireCondition(
    knownIssue === null || typeof knownIssue === "string",
    `${variant.name} admission known issue is invalid.`,
  );
  const deployable = variant.name === "coreclr-il"
    ? status === "admitted" && knownIssue === null
    : status === "admitted" && knownIssue === null
      || status === "correctness-rejection"
        && knownIssue === knownR2RIssue;
  requireCondition(
    deployable,
    `${variant.name} does not have a deployable admission result.`,
  );
  requireCondition(
    Number.isInteger(admission.exitCode) && Number(admission.exitCode) >= 0,
    `${variant.name} admission exit code is invalid.`,
  );
  requireString(admission.logFile, `${variant.name} admission log file`);
  requireSha256(admission.logSha256, `${variant.name} admission log`);

  return {
    schema: 2,
    site: variant.name,
    sourceCommit: expectedSourceCommit,
    candidate: {
      runId: candidateRunId,
      attempt: candidateAttempt,
    },
    cohortGeneratedAtUtc: cohort.generatedAtUtc,
    frontendManifestSha256: variant.frontendManifestSha256,
    siteManifestSha256: variant.siteManifestSha256,
    runtime: variant.runtime,
    configuration: variant.configuration,
    admission: {
      status,
      exitCode: Number(admission.exitCode),
      logFile: admission.logFile,
      logSha256: admission.logSha256,
      knownIssue,
    },
  };
}

export function createRuntimeCohortBenchmarkReceipt(
  cohortText: string,
  reportText: string,
  trendPointText: string,
): RuntimeCohortBenchmarkReceipt {
  const cohort: unknown = JSON.parse(cohortText);
  const report: unknown = JSON.parse(reportText);
  const trend: unknown = JSON.parse(trendPointText);
  requireCondition(isRecord(cohort), "Cohort receipt must be an object.");
  requireCondition(cohort.schema === 1, "Cohort receipt schema must be 1.");
  requireCondition(cohort.status === "accepted", "Cohort must be accepted.");
  const sourceCommit = cohort.sourceCommit;
  requireCondition(
    typeof sourceCommit === "string" && commitPattern.test(sourceCommit),
    "Cohort source commit must be a lowercase Git commit.",
  );
  const benchmarkSites = cohort.benchmarkSites;
  requireCondition(
    isUnknownArray(benchmarkSites)
      && benchmarkSites.every(isRuntimeCohortVariantName),
    "Cohort benchmark sites are invalid.",
  );
  requireCondition(isRecord(report), "Benchmark report must be an object.");
  const comparison = report.comparison;
  requireCondition(
    isRecord(comparison),
    "Benchmark report comparison must be an object.",
  );
  requireCondition(
    comparison.comparable === true,
    "Benchmark report must be comparable.",
  );
  const reportSites = readSummarySites(report.summaries, "Benchmark report");
  requireCondition(isRecord(trend), "Trend point must be an object.");
  const trendSites = readSummarySites(trend.summaries, "Trend point");
  requireCondition(
    JSON.stringify(reportSites) === JSON.stringify(benchmarkSites),
    "Benchmark report sites do not match admitted cohort variants.",
  );
  requireCondition(
    JSON.stringify(trendSites) === JSON.stringify(benchmarkSites),
    "Trend sites do not match admitted cohort variants.",
  );
  requireCondition(
    trend.productCommit === sourceCommit,
    "Trend product commit does not match the cohort source commit.",
  );
  const reportSha256 = sha256(reportText);
  requireCondition(
    trend.sourceReportSha256 === reportSha256,
    "Trend point is not derived from the supplied benchmark report.",
  );
  return {
    schema: 1,
    generatedAtUtc: new Date().toISOString(),
    sourceCommit,
    sites: benchmarkSites,
    cohortReceiptSha256: sha256(cohortText),
    reportSha256,
    trendPointSha256: sha256(trendPointText),
  };
}

function readSummarySites(
  value: unknown,
  description: string,
): RuntimeCohortVariantName[] {
  requireCondition(
    isUnknownArray(value),
    `${description} summaries must be an array.`,
  );
  return value.map((summary, index) => {
    requireCondition(
      isRecord(summary),
      `${description} summary ${index} must be an object.`,
    );
    requireCondition(
      isRuntimeCohortVariantName(summary.site),
      `${description} summary ${index} has an invalid site.`,
    );
    return summary.site;
  });
}
