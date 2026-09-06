import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { arch, cpus, platform, release, totalmem } from "node:os";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { firefox, type Page } from "@playwright/test";
import {
  benchmarkUsage,
  evaluateBuildComparability,
  isBenchmarkResultAccepted,
  parseBenchmarkArguments,
  summarize,
  type BenchmarkOptions,
  type BenchmarkSite,
  type DistributionSummary,
} from "../scripts/published-runtime-benchmark-model.ts";

const scenario = {
  packageId: "Microsoft.Extensions.Primitives",
  version: "10.0.0",
  targetFramework: "net10.0",
  comparisonType: "Microsoft.Extensions.Primitives.StringSegment",
  comparisonBefore: "Trim",
  comparisonAfter: "TrimStart",
} as const;

interface BuildIdentity {
  readonly version: string;
  readonly commit: string | null;
  readonly builtAtUtc: string | null;
  readonly commitUrl: string | null;
}

interface FrameworkTransfer {
  readonly resources: number;
  readonly transferBytes: number;
  readonly encodedBodyBytes: number;
  readonly decodedBodyBytes: number;
}

interface StartupMeasurement {
  readonly readyMilliseconds: number;
  readonly frameworkTransfer: FrameworkTransfer;
}

interface PackageMeasurement {
  readonly milliseconds: number;
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assemblyName: string;
  readonly assemblies: number;
  readonly types: number;
  readonly members: number;
}

interface PackagePerformanceResult {
  readonly totalOpportunities: number;
  readonly members: number;
  readonly nonPublicOpportunities: number;
  readonly compileLibraryStatus: string | number;
}

interface PackagePerformanceMeasurement {
  readonly firstMilliseconds: number;
  readonly warmMilliseconds: number;
  readonly result: PackagePerformanceResult;
}

interface MemberFactResult {
  readonly key: string;
  readonly metadataToken: number;
  readonly allocations: number;
  readonly calls: number;
  readonly safety: number;
  readonly exceptionRegions: number;
  readonly performanceOpportunities: number;
  readonly diagnostics: number;
}

interface MemberThroughputMeasurement {
  readonly operations: number;
  readonly totalMilliseconds: number;
  readonly operationsPerSecond: number;
  readonly operationMilliseconds: readonly number[];
  readonly results: readonly MemberFactResult[];
}

interface MethodComparisonResult {
  readonly outcome: string;
  readonly producers: number;
  readonly csharpRows: number;
  readonly ilRows: number;
}

interface MethodComparisonMeasurement {
  readonly preparationMilliseconds: number;
  readonly firstMilliseconds: number;
  readonly warmMilliseconds: number;
  readonly result: MethodComparisonResult;
}

interface SuccessfulRun {
  readonly status: "succeeded";
  readonly site: string;
  readonly url: string;
  readonly sample: number;
  readonly buildIdentity: BuildIdentity;
  readonly startup: StartupMeasurement;
  readonly package: {
    readonly cold: PackageMeasurement;
    readonly warm: PackageMeasurement;
  };
  readonly packagePerformance: PackagePerformanceMeasurement;
  readonly memberThroughput: MemberThroughputMeasurement;
  readonly methodComparison: MethodComparisonMeasurement;
  readonly semanticFingerprint: string;
}

interface FailedRun {
  readonly status: "failed";
  readonly site: string;
  readonly url: string;
  readonly sample: number;
  readonly stage: string;
  readonly error: string;
  readonly buildIdentity: BuildIdentity | null;
  readonly startup: StartupMeasurement | null;
}

type BenchmarkRun = SuccessfulRun | FailedRun;

interface SiteSummary {
  readonly site: string;
  readonly successfulSamples: number;
  readonly failedSamples: number;
  readonly startupMilliseconds: DistributionSummary | null;
  readonly frameworkEncodedBodyBytes: DistributionSummary | null;
  readonly coldPackageMilliseconds: DistributionSummary | null;
  readonly warmPackageMilliseconds: DistributionSummary | null;
  readonly firstPackagePerformanceMilliseconds: DistributionSummary | null;
  readonly warmPackagePerformanceMilliseconds: DistributionSummary | null;
  readonly memberOperationsPerSecond: DistributionSummary | null;
  readonly firstMethodComparisonMilliseconds: DistributionSummary | null;
  readonly warmMethodComparisonMilliseconds: DistributionSummary | null;
}

interface BenchmarkReport {
  readonly schema: 1;
  readonly generatedAtUtc: string;
  readonly harness: {
    readonly commit: string | null;
    readonly dirty: boolean | null;
  };
  readonly environment: {
    readonly platform: string;
    readonly release: string;
    readonly architecture: string;
    readonly cpu: string | null;
    readonly logicalProcessors: number;
    readonly totalMemoryBytes: number;
    readonly node: string;
    readonly browser: string;
  };
  readonly configuration: {
    readonly samples: number;
    readonly memberCount: number;
    readonly sites: readonly BenchmarkSite[];
    readonly scenario: typeof scenario;
    readonly browserCache: "fresh-context-per-sample";
    readonly siteOrder: "alternating";
  };
  readonly comparison: {
    readonly comparable: boolean;
    readonly buildComparable: boolean;
    readonly semanticResultsEquivalent: boolean;
    readonly diagnosticOverride: boolean;
    readonly reasons: readonly string[];
    readonly commitsBySite: Readonly<Record<string, readonly string[]>>;
  };
  readonly summaries: readonly SiteSummary[];
  readonly runs: readonly BenchmarkRun[];
}

function round(value: number): number {
  return Math.round(value * 1_000) / 1_000;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function gitValue(arguments_: readonly string[]): string | null {
  const repository = fileURLToPath(new URL("../../..", import.meta.url));
  const result = spawnSync("git", arguments_, {
    cwd: repository,
    encoding: "utf8",
  });
  return result.status === 0 ? result.stdout.trim() : null;
}

async function openSite(
  page: Page,
  url: string,
): Promise<{ identity: BuildIdentity; startup: StartupMeasurement }> {
  await page.goto(url, {
    waitUntil: "commit",
    timeout: 180_000,
  });

  return page.evaluate(async () => {
    const host = await import("/inspect-web-host.js");
    const deadline = performance.now() + 180_000;
    let identity: BuildIdentity;
    while (true) {
      try {
        identity = host.buildIdentity();
        break;
      } catch (error: unknown) {
        const message = error instanceof Error ? error.message : String(error);
        if (!message.includes(
          "The .NET runtime facade is not initialized.",
        )) {
          throw error;
        }
        if (performance.now() >= deadline) {
          throw new Error(
            "Timed out waiting for the managed runtime facade.",
            { cause: error },
          );
        }
        await new Promise(resolveDelay => setTimeout(resolveDelay, 50));
      }
    }
    const frameworkResources = performance.getEntriesByType("resource")
      .filter(entry => new URL(entry.name).pathname.includes("/_framework/"));
    let transferBytes = 0;
    let encodedBodyBytes = 0;
    let decodedBodyBytes = 0;
    for (const entry of frameworkResources) {
      if ("transferSize" in entry && typeof entry.transferSize === "number") {
        transferBytes += entry.transferSize;
      }
      if ("encodedBodySize" in entry
          && typeof entry.encodedBodySize === "number") {
        encodedBodyBytes += entry.encodedBodySize;
      }
      if ("decodedBodySize" in entry
          && typeof entry.decodedBodySize === "number") {
        decodedBodyBytes += entry.decodedBodySize;
      }
    }
    return {
      identity,
      startup: {
        readyMilliseconds: performance.now(),
        frameworkTransfer: {
          resources: frameworkResources.length,
          transferBytes,
          encodedBodyBytes,
          decodedBodyBytes,
        },
      },
    };
  });
}

async function measurePackage(page: Page): Promise<{
  cold: PackageMeasurement;
  warm: PackageMeasurement;
}> {
  return page.evaluate(async coordinate => {
    const packageFacade = await import("/inspect-web-package.js");
    async function measure(): Promise<PackageMeasurement> {
      const started = performance.now();
      const surface = await packageFacade.queryPackage(
        coordinate.packageId,
        coordinate.version,
        coordinate.targetFramework,
      );
      if (surface.inspectionError) {
        throw new Error(`Package inspection failed: ${surface.inspectionError}`);
      }
      const assembly = surface.assemblies.find(
        candidate => candidate.id === surface.defaultAssemblyId,
      ) ?? surface.assemblies[0];
      if (!assembly) {
        throw new Error("Package inspection returned no assemblies.");
      }
      return {
        milliseconds: performance.now() - started,
        packageId: surface.package,
        version: surface.version,
        framework: surface.activeFramework,
        assemblyName: assembly.name,
        assemblies: surface.assemblies.length,
        types: surface.types.length,
        members: surface.totalMembers,
      };
    }
    const cold = await measure();
    const warm = await measure();
    const coldResult = { ...cold, milliseconds: 0 };
    const warmResult = { ...warm, milliseconds: 0 };
    if (JSON.stringify(coldResult) !== JSON.stringify(warmResult)) {
      throw new Error(
        "Package inspection changed its semantic result after warmup.",
      );
    }
    return { cold, warm };
  }, scenario);
}

async function measurePackagePerformance(
  page: Page,
  assemblyName: string,
): Promise<PackagePerformanceMeasurement> {
  return page.evaluate(async input => {
    const analysis = await import("/inspect-web-analysis.js");
    async function measure() {
      const started = performance.now();
      const result = await analysis.queryPackagePerformance(
        input.coordinate.packageId,
        input.coordinate.version,
        input.coordinate.targetFramework,
        input.assemblyName,
      );
      if (result.inspectionError) {
        throw new Error(
          `Package performance analysis failed: ${result.inspectionError}`,
        );
      }
      return {
        milliseconds: performance.now() - started,
        result: {
          totalOpportunities: result.totalOpportunities,
          members: result.members.length,
          nonPublicOpportunities: result.nonPublicOpportunities,
          compileLibraryStatus: result.compileLibrary.status,
        },
      };
    }
    const first = await measure();
    const warm = await measure();
    if (JSON.stringify(first.result) !== JSON.stringify(warm.result)) {
      throw new Error(
        "Package performance analysis changed its semantic result after warmup.",
      );
    }
    return {
      firstMilliseconds: first.milliseconds,
      warmMilliseconds: warm.milliseconds,
      result: first.result,
    };
  }, { coordinate: scenario, assemblyName });
}

async function measureMemberThroughput(
  page: Page,
  memberCount: number,
): Promise<MemberThroughputMeasurement> {
  return page.evaluate(async input => {
    const packageFacade = await import("/inspect-web-package.js");
    const analysis = await import("/inspect-web-analysis.js");
    const surface = await packageFacade.queryPackage(
      input.coordinate.packageId,
      input.coordinate.version,
      input.coordinate.targetFramework,
    );
    const candidates = surface.types.flatMap(type =>
      type.api.flatMap(member => {
        const body = member.bodySelectors[0];
        return body
          ? [{
            key: [
              type.assembly,
              type.definitionId,
              body.selectorKey,
              body.token,
            ].join("|"),
            assembly: type.assembly,
            typeIdentity: type.definitionId,
            memberName: body.memberName,
            memberSignature: member.signature,
            selectorKey: body.selectorKey,
            metadataToken: body.token,
          }]
          : [];
      }),
    ).sort((left, right) => left.key.localeCompare(right.key));
    if (candidates.length <= input.memberCount) {
      throw new Error(
        `Expected more than ${input.memberCount} analyzable members to reserve `
        + `a distinct warmup member; found ${candidates.length}.`,
      );
    }
    const selected = Array.from(
      { length: input.memberCount },
      (_, index) => candidates[
        Math.floor(index * (candidates.length - 1) / input.memberCount)
      ]!,
    );
    const warmup = candidates.at(-1)!;
    await analysis.queryMemberFacts(
      surface.package,
      surface.version,
      surface.activeFramework,
      warmup.assembly,
      warmup.typeIdentity,
      warmup.memberName,
      warmup.memberSignature,
      warmup.selectorKey,
      warmup.metadataToken,
      true,
    );

    const operationMilliseconds: number[] = [];
    const results: MemberFactResult[] = [];
    const batchStarted = performance.now();
    for (const candidate of selected) {
      const started = performance.now();
      const facts = await analysis.queryMemberFacts(
        surface.package,
        surface.version,
        surface.activeFramework,
        candidate.assembly,
        candidate.typeIdentity,
        candidate.memberName,
        candidate.memberSignature,
        candidate.selectorKey,
        candidate.metadataToken,
        true,
      );
      operationMilliseconds.push(performance.now() - started);
      if (facts.metadataToken !== candidate.metadataToken) {
        throw new Error(
          `Member facts returned token ${facts.metadataToken} for ${
            candidate.metadataToken}.`,
        );
      }
      results.push({
        key: candidate.key,
        metadataToken: facts.metadataToken,
        allocations: facts.allocations.length,
        calls: facts.calls.length,
        safety: facts.safety.length,
        exceptionRegions: facts.exceptionRegions.length,
        performanceOpportunities: facts.performanceOpportunities.length,
        diagnostics: facts.diagnostics.length,
      });
    }
    const totalMilliseconds = performance.now() - batchStarted;
    return {
      operations: selected.length,
      totalMilliseconds,
      operationsPerSecond: selected.length / (totalMilliseconds / 1_000),
      operationMilliseconds,
      results,
    };
  }, { coordinate: scenario, memberCount });
}

async function measureMethodComparison(
  page: Page,
): Promise<MethodComparisonMeasurement> {
  return page.evaluate(async coordinate => {
    const packageFacade = await import("/inspect-web-package.js");
    const source = await import("/inspect-web-source.js");
    const surface = await packageFacade.queryPackage(
      coordinate.packageId,
      coordinate.version,
      coordinate.targetFramework,
    );
    const type = surface.types.find(
      candidate => candidate.definitionId === coordinate.comparisonType,
    );
    const member = type?.api.find(
      candidate => candidate.name === coordinate.comparisonBefore,
    );
    const body = member?.bodySelectors.find(
      candidate => candidate.token === member.metadataToken,
    );
    if (!type || !member || !body) {
      throw new Error("The method-comparison input is absent.");
    }

    let started = performance.now();
    const prepared = await source.queryMethodBodyComparisonTargets(
      `runtime-benchmark-targets-${crypto.randomUUID()}`,
      surface.package,
      surface.version,
      surface.activeFramework,
      type.assemblyId,
      type.definitionId,
      body.memberName,
      body.selectorKey,
      body.token,
    );
    const preparationMilliseconds = performance.now() - started;
    if (prepared.kind !== "Succeeded" || !prepared.value) {
      throw new Error(
        `Method-comparison preparation failed: ${JSON.stringify(prepared)}`,
      );
    }
    const target = prepared.value.methods.find(candidate =>
      candidate.typeIdentity === prepared.value!.before.typeIdentity
      && candidate.memberName === coordinate.comparisonAfter);
    if (!target) {
      throw new Error("The method-comparison target is absent.");
    }
    const request = {
      packageId: prepared.value.packageId,
      version: prepared.value.version,
      framework: prepared.value.framework,
      assembly: prepared.value.assembly,
      moduleVersionId: prepared.value.moduleVersionId,
      before: prepared.value.before,
      after: target,
    };

    async function compare() {
      const comparisonStarted = performance.now();
      const result = await source.queryMethodBodyComparison(
        `runtime-benchmark-comparison-${crypto.randomUUID()}`,
        JSON.stringify(request),
      );
      const milliseconds = performance.now() - comparisonStarted;
      if (result.kind !== "Succeeded"
          || !result.value
          || result.value.outcome !== "Completed") {
        throw new Error(
          `Method-body comparison failed: ${JSON.stringify(result)}`,
        );
      }
      return {
        milliseconds,
        result: {
          outcome: result.value.outcome,
          producers: result.value.producers.length,
          csharpRows: result.value.producers.reduce(
            (count, producer) => count + (producer.cSharp?.rows.length ?? 0),
            0,
          ),
          ilRows: result.value.producers.reduce(
            (count, producer) => count + (producer.il?.rows.length ?? 0),
            0,
          ),
        },
      };
    }

    const first = await compare();
    const warm = await compare();
    if (JSON.stringify(first.result) !== JSON.stringify(warm.result)) {
      throw new Error(
        "Method-body comparison changed its semantic result after warmup.",
      );
    }
    return {
      preparationMilliseconds,
      firstMilliseconds: first.milliseconds,
      warmMilliseconds: warm.milliseconds,
      result: first.result,
    };
  }, scenario);
}

function semanticFingerprint(run: Omit<SuccessfulRun, "semanticFingerprint">) {
  const evidence = {
    package: {
      packageId: run.package.cold.packageId,
      version: run.package.cold.version,
      framework: run.package.cold.framework,
      assemblyName: run.package.cold.assemblyName,
      assemblies: run.package.cold.assemblies,
      types: run.package.cold.types,
      members: run.package.cold.members,
    },
    packagePerformance: run.packagePerformance.result,
    memberFacts: run.memberThroughput.results,
    methodComparison: run.methodComparison.result,
  };
  return createHash("sha256")
    .update(JSON.stringify(evidence))
    .digest("hex");
}

async function measureRun(
  page: Page,
  site: BenchmarkSite,
  sample: number,
  memberCount: number,
): Promise<BenchmarkRun> {
  let stage = "startup";
  let buildIdentity: BuildIdentity | null = null;
  let startup: StartupMeasurement | null = null;
  try {
    const opened = await openSite(page, site.url);
    buildIdentity = opened.identity;
    startup = opened.startup;

    stage = "package";
    const packageMeasurement = await measurePackage(page);
    stage = "package-performance";
    const packagePerformance = await measurePackagePerformance(
      page,
      packageMeasurement.cold.assemblyName,
    );
    stage = "member-throughput";
    const memberThroughput = await measureMemberThroughput(page, memberCount);
    stage = "method-comparison";
    const methodComparison = await measureMethodComparison(page);

    const runWithoutFingerprint = {
      status: "succeeded" as const,
      site: site.name,
      url: site.url,
      sample,
      buildIdentity,
      startup,
      package: packageMeasurement,
      packagePerformance,
      memberThroughput,
      methodComparison,
    };
    return {
      ...runWithoutFingerprint,
      semanticFingerprint: semanticFingerprint(runWithoutFingerprint),
    };
  } catch (error) {
    return {
      status: "failed",
      site: site.name,
      url: site.url,
      sample,
      stage,
      error: errorMessage(error),
      buildIdentity,
      startup,
    };
  }
}

function distribution(
  runs: readonly SuccessfulRun[],
  select: (run: SuccessfulRun) => number,
): DistributionSummary | null {
  return runs.length === 0 ? null : summarize(runs.map(select));
}

function summarizeSite(
  site: BenchmarkSite,
  runs: readonly BenchmarkRun[],
): SiteSummary {
  const siteRuns = runs.filter(run => run.site === site.name);
  const successful = siteRuns.filter(
    (run): run is SuccessfulRun => run.status === "succeeded",
  );
  return {
    site: site.name,
    successfulSamples: successful.length,
    failedSamples: siteRuns.length - successful.length,
    startupMilliseconds: distribution(
      successful,
      run => run.startup.readyMilliseconds,
    ),
    frameworkEncodedBodyBytes: distribution(
      successful,
      run => run.startup.frameworkTransfer.encodedBodyBytes,
    ),
    coldPackageMilliseconds: distribution(
      successful,
      run => run.package.cold.milliseconds,
    ),
    warmPackageMilliseconds: distribution(
      successful,
      run => run.package.warm.milliseconds,
    ),
    firstPackagePerformanceMilliseconds: distribution(
      successful,
      run => run.packagePerformance.firstMilliseconds,
    ),
    warmPackagePerformanceMilliseconds: distribution(
      successful,
      run => run.packagePerformance.warmMilliseconds,
    ),
    memberOperationsPerSecond: distribution(
      successful,
      run => run.memberThroughput.operationsPerSecond,
    ),
    firstMethodComparisonMilliseconds: distribution(
      successful,
      run => run.methodComparison.firstMilliseconds,
    ),
    warmMethodComparisonMilliseconds: distribution(
      successful,
      run => run.methodComparison.warmMilliseconds,
    ),
  };
}

function printSummaries(summaries: readonly SiteSummary[]): void {
  for (const summary of summaries) {
    const startup = summary.startupMilliseconds?.median ?? "n/a";
    const packagePerformance =
      summary.firstPackagePerformanceMilliseconds?.median ?? "n/a";
    const throughput = summary.memberOperationsPerSecond?.median ?? "n/a";
    const comparison =
      summary.firstMethodComparisonMilliseconds?.median ?? "n/a";
    console.error(
      `${summary.site}: startup=${startup}ms, `
      + `package-performance=${packagePerformance}ms, `
      + `member-throughput=${throughput}/s, `
      + `method-comparison=${comparison}ms`,
    );
  }
}

async function runBenchmark(options: BenchmarkOptions): Promise<void> {
  const browser = await firefox.launch({ headless: true });
  const browserVersion = browser.version();
  const runs: BenchmarkRun[] = [];
  try {
    for (let sample = 1; sample <= options.samples; sample++) {
      const sites = sample % 2 === 1
        ? options.sites
        : options.sites.map(
          (_, index, allSites) => allSites[allSites.length - index - 1]!,
        );
      for (const site of sites) {
        console.error(
          `Measuring ${site.name} sample ${sample}/${options.samples}...`,
        );
        const context = await browser.newContext({ serviceWorkers: "block" });
        try {
          const page = await context.newPage();
          page.setDefaultTimeout(180_000);
          runs.push(await measureRun(
            page,
            site,
            sample,
            options.memberCount,
          ));
        } finally {
          await context.close();
        }
      }
    }
  } finally {
    await browser.close();
  }

  const successful = runs.filter(
    (run): run is SuccessfulRun => run.status === "succeeded",
  );
  const failed = runs.filter(run => run.status === "failed");
  const buildComparison = evaluateBuildComparability(
    runs.map(run => ({
      site: run.site,
      commit: run.buildIdentity?.commit ?? null,
    })),
  );
  const semanticFingerprints = new Set(
    successful.map(run => run.semanticFingerprint),
  );
  const semanticResultsEquivalent =
    failed.length === 0
    && successful.length === runs.length
    && semanticFingerprints.size === 1;
  const reasons = [
    ...buildComparison.reasons,
    ...(failed.length > 0
      ? [`${failed.length} benchmark run(s) failed.`]
      : []),
    ...(!semanticResultsEquivalent && failed.length === 0
      ? ["Successful runs did not return one semantic result fingerprint."]
      : []),
  ];
  const comparable =
    buildComparison.comparable
    && semanticResultsEquivalent
    && failed.length === 0;
  const accepted = isBenchmarkResultAccepted(
    buildComparison.comparable,
    semanticResultsEquivalent,
    failed.length,
    options.allowMismatchedCommits,
  );
  const summaries = options.sites.map(site => summarizeSite(site, runs));
  const dirty = gitValue(["status", "--porcelain"]);
  const report: BenchmarkReport = {
    schema: 1,
    generatedAtUtc: new Date().toISOString(),
    harness: {
      commit: gitValue(["rev-parse", "HEAD"]),
      dirty: dirty === null ? null : dirty.length > 0,
    },
    environment: {
      platform: platform(),
      release: release(),
      architecture: arch(),
      cpu: cpus()[0]?.model ?? null,
      logicalProcessors: cpus().length,
      totalMemoryBytes: totalmem(),
      node: process.version,
      browser: browserVersion,
    },
    configuration: {
      samples: options.samples,
      memberCount: options.memberCount,
      sites: options.sites,
      scenario,
      browserCache: "fresh-context-per-sample",
      siteOrder: "alternating",
    },
    comparison: {
      comparable,
      buildComparable: buildComparison.comparable,
      semanticResultsEquivalent,
      diagnosticOverride: options.allowMismatchedCommits,
      reasons,
      commitsBySite: buildComparison.commitsBySite,
    },
    summaries,
    runs: runs.map(run => run.status === "succeeded"
      ? {
        ...run,
        startup: {
          ...run.startup,
          readyMilliseconds: round(run.startup.readyMilliseconds),
        },
        package: {
          cold: {
            ...run.package.cold,
            milliseconds: round(run.package.cold.milliseconds),
          },
          warm: {
            ...run.package.warm,
            milliseconds: round(run.package.warm.milliseconds),
          },
        },
        packagePerformance: {
          ...run.packagePerformance,
          firstMilliseconds: round(
            run.packagePerformance.firstMilliseconds,
          ),
          warmMilliseconds: round(run.packagePerformance.warmMilliseconds),
        },
        memberThroughput: {
          ...run.memberThroughput,
          totalMilliseconds: round(run.memberThroughput.totalMilliseconds),
          operationsPerSecond: round(
            run.memberThroughput.operationsPerSecond,
          ),
          operationMilliseconds:
            run.memberThroughput.operationMilliseconds.map(round),
        },
        methodComparison: {
          ...run.methodComparison,
          preparationMilliseconds: round(
            run.methodComparison.preparationMilliseconds,
          ),
          firstMilliseconds: round(run.methodComparison.firstMilliseconds),
          warmMilliseconds: round(run.methodComparison.warmMilliseconds),
        },
      }
      : run),
  };

  printSummaries(summaries);
  const json = `${JSON.stringify(report, null, 2)}\n`;
  if (options.outputPath) {
    const output = resolve(options.outputPath);
    mkdirSync(dirname(output), { recursive: true });
    writeFileSync(output, json);
    console.log(`Wrote ${output}`);
  } else {
    process.stdout.write(json);
  }

  if (!accepted) {
    process.exitCode = 1;
  }
}

let options: BenchmarkOptions;
try {
  options = parseBenchmarkArguments(process.argv.slice(2));
} catch (error) {
  console.error(errorMessage(error));
  console.error(benchmarkUsage());
  process.exitCode = 1;
  process.exit();
}

if (options.help) {
  console.log(benchmarkUsage());
} else {
  await runBenchmark(options);
}
