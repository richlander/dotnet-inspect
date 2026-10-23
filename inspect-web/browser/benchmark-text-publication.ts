import { spawnSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import {
  arch,
  cpus,
  platform,
  release,
  totalmem,
} from "node:os";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { firefox, type Page } from "@playwright/test";
import {
  publishedRuntimeBenchmarkParameter,
} from "../src/published-runtime-benchmark-bridge.ts";
import {
  summarize,
  type DistributionSummary,
} from "../scripts/published-runtime-benchmark-model.ts";
import {
  parseTextPublicationBenchmarkArguments,
  textPublicationBenchmarkUsage,
  type TextPublicationBenchmarkOptions,
} from "../scripts/text-publication-benchmark-model.ts";

const scenario = {
  packageId: "Microsoft.Extensions.Primitives",
  version: "10.0.0",
  targetFramework: "net10.0",
  typeIdentity: "Microsoft.Extensions.Primitives.StringSegment",
  spelling: "metadata",
  accessibility: "all",
} as const;

interface BuildIdentity {
  readonly version: string;
  readonly commit: string | null;
  readonly builtAtUtc: string | null;
  readonly commitUrl: string | null;
}

interface PayloadCensus {
  readonly strings: number;
  readonly utf16CodeUnits: number;
  readonly utf8Bytes: number;
  readonly serializedCharacters: number;
  readonly serializedUtf8Bytes: number;
}

interface ResultIdentity {
  readonly assemblyName: string;
  readonly assemblyVersion: string;
  readonly moduleVersionId: string;
  readonly typeIdentity: string;
  readonly typeDefinitionToken: number;
  readonly groups: number;
  readonly completeMembers: number;
}

interface TextPublicationSample {
  readonly sequence: number;
  readonly milliseconds: number;
}

interface BrowserMeasurement {
  readonly packageWarmup: {
    readonly assemblyName: string;
    readonly assemblies: number;
    readonly types: number;
    readonly members: number;
  };
  readonly semanticSha256: string;
  readonly resultIdentity: ResultIdentity;
  readonly perResultPayload: PayloadCensus;
  readonly retainedResults: number;
  readonly retainedPayload: PayloadCensus;
  readonly samples: readonly TextPublicationSample[];
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
    readonly site: string;
    readonly samples: number;
    readonly scenario: typeof scenario;
    readonly browserCache: "one-context";
    readonly packageWarmup: "excluded";
    readonly resultRetention: "all-measured-results-until-census";
  };
  readonly buildIdentity: BuildIdentity;
  readonly semanticSha256: string;
  readonly resultIdentity: ResultIdentity;
  readonly latency: {
    readonly firstMilliseconds: number;
    readonly warmMilliseconds: DistributionSummary | null;
    readonly samples: readonly TextPublicationSample[];
  };
  readonly payload: {
    readonly perResult: PayloadCensus;
    readonly retainedResults: number;
    readonly retained: PayloadCensus;
    readonly browserHeapBytes: null;
  };
  readonly packageWarmup: BrowserMeasurement["packageWarmup"];
}

function round(value: number): number {
  return Math.round(value * 1_000) / 1_000;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function gitValue(arguments_: readonly string[]): string | null {
  const repository = fileURLToPath(new URL("../..", import.meta.url));
  const result = spawnSync("git", arguments_, {
    cwd: repository,
    encoding: "utf8",
  });
  return result.status === 0 ? result.stdout.trim() : null;
}

function assertBuildIdentity(
  identity: BuildIdentity,
  options: TextPublicationBenchmarkOptions,
): void {
  if (identity.commit?.toLowerCase() !== options.expectedCommit) {
    throw new Error(
      `Expected published commit ${options.expectedCommit}; `
        + `site reported ${identity.commit ?? "none"}.`,
    );
  }
  if (identity.version !== options.expectedVersion) {
    throw new Error(
      `Expected published version ${options.expectedVersion}; `
        + `site reported ${identity.version}.`,
    );
  }
}

async function openSite(
  page: Page,
  options: TextPublicationBenchmarkOptions,
): Promise<BuildIdentity> {
  const url = new URL(options.site);
  url.searchParams.set(publishedRuntimeBenchmarkParameter, "1");
  await page.goto(url.href, {
    waitUntil: "commit",
    timeout: 180_000,
  });
  await page.waitForFunction(
    () => window.__inspectWebRuntimeBenchmark !== undefined,
    null,
    { timeout: 180_000 },
  );
  const identity = await page.evaluate(
    () => window.__inspectWebRuntimeBenchmark!.host.buildIdentity(),
  );
  assertBuildIdentity(identity, options);
  return identity;
}

async function measure(
  page: Page,
  samples: number,
): Promise<BrowserMeasurement> {
  return page.evaluate(async input => {
    const bridge = window.__inspectWebRuntimeBenchmark;
    if (!bridge) {
      throw new Error("The production Worker benchmark bridge is unavailable.");
    }
    const surfaceResult = await bridge.package.queryPackage(
      input.scenario.packageId,
      input.scenario.version,
      input.scenario.targetFramework,
      "",
    );
    const surface = surfaceResult.surface;
    if (surface === null) {
      throw new Error(
        surfaceResult.versionSettlement.content.failure?.reason
          ?? "Package version settlement did not produce a surface.",
      );
    }
    if (surface.inspectionError) {
      throw new Error(`Package inspection failed: ${surface.inspectionError}`);
    }
    const type = surface.types.find(
      candidate => candidate.definitionId === input.scenario.typeIdentity,
    );
    if (!type) {
      throw new Error(
        `Package inspection did not return ${input.scenario.typeIdentity}.`,
      );
    }

    const encoder = new TextEncoder();
    function census(value: unknown): PayloadCensus {
      let strings = 0;
      let utf16CodeUnits = 0;
      let utf8Bytes = 0;
      const visited = new Set<object>();
      function visit(candidate: unknown): void {
        if (typeof candidate === "string") {
          strings++;
          utf16CodeUnits += candidate.length;
          utf8Bytes += encoder.encode(candidate).byteLength;
          return;
        }
        if (candidate === null || typeof candidate !== "object") return;
        if (visited.has(candidate)) return;
        visited.add(candidate);
        if (Array.isArray(candidate)) {
          for (const item of candidate) visit(item);
          return;
        }
        for (const item of Object.values(candidate)) visit(item);
      }
      visit(value);
      const serialized = JSON.stringify(value);
      return {
        strings,
        utf16CodeUnits,
        utf8Bytes,
        serializedCharacters: serialized.length,
        serializedUtf8Bytes: encoder.encode(serialized).byteLength,
      };
    }
    async function sha256(value: string): Promise<string> {
      const digest = await crypto.subtle.digest(
        "SHA-256",
        encoder.encode(value),
      );
      return [...new Uint8Array(digest)]
        .map(byte => byte.toString(16).padStart(2, "0"))
        .join("");
    }

    const retained: unknown[] = [];
    const measurements: TextPublicationSample[] = [];
    let firstSerialized: string | undefined;
    let firstInspection: Awaited<
      ReturnType<typeof bridge.metadata.queryTypeOverviewDocument>
    > | undefined;
    for (let sequence = 1; sequence <= input.samples; sequence++) {
      const started = performance.now();
      const inspection = await bridge.metadata.queryTypeOverviewDocument(
        surface.package,
        surface.version,
        surface.activeFramework,
        type.assembly,
        type.definitionId,
        input.scenario.spelling,
        input.scenario.accessibility,
        surfaceResult.runtimePopulation?.selectedRuntimeIdentifier ?? null,
      );
      const milliseconds = performance.now() - started;
      if (inspection.outcome !== "Available" || inspection.document === null) {
        throw new Error(
          `Type overview was not available: ${inspection.detail ?? "unknown"}.`,
        );
      }
      if (inspection.diagnostics.length !== 0) {
        throw new Error(
          `Type overview returned diagnostics: ${
            JSON.stringify(inspection.diagnostics)
          }.`,
        );
      }
      const serialized = JSON.stringify(inspection);
      firstSerialized ??= serialized;
      firstInspection ??= inspection;
      if (serialized !== firstSerialized) {
        throw new Error(
          `Type overview changed semantic result at sample ${sequence}.`,
        );
      }
      retained.push(inspection);
      measurements.push({ sequence, milliseconds });
    }
    if (!firstInspection || !firstSerialized) {
      throw new Error("Text publication produced no measured result.");
    }
    const document = firstInspection.document;
    if (document === null) {
      throw new Error("Available Type overview had no document.");
    }
    return {
      packageWarmup: {
        assemblyName: type.assembly,
        assemblies: surface.assemblies.length,
        types: surface.types.length,
        members: surface.totalMembers,
      },
      semanticSha256: await sha256(firstSerialized),
      resultIdentity: {
        assemblyName: document.assembly.name,
        assemblyVersion: document.assembly.version,
        moduleVersionId: document.moduleVersionId,
        typeIdentity: document.typeIdentity,
        typeDefinitionToken: document.typeDefinitionToken,
        groups: document.population.groups.length,
        completeMembers: document.population.groups.reduce(
          (total, group) => total + group.completeCount,
          0,
        ),
      },
      perResultPayload: census(firstInspection),
      retainedResults: retained.length,
      retainedPayload: census(retained),
      samples: measurements,
    };
  }, { scenario, samples });
}

async function runBenchmark(
  options: TextPublicationBenchmarkOptions,
): Promise<void> {
  const processors = cpus();
  const browser = await firefox.launch({ headless: true });
  const browserVersion = browser.version();
  let identity: BuildIdentity;
  let measurement: BrowserMeasurement;
  try {
    const context = await browser.newContext({ serviceWorkers: "block" });
    try {
      const page = await context.newPage();
      page.setDefaultTimeout(180_000);
      identity = await openSite(page, options);
      measurement = await measure(page, options.samples);
      const finalIdentity = await page.evaluate(
        () => window.__inspectWebRuntimeBenchmark!.host.buildIdentity(),
      );
      assertBuildIdentity(finalIdentity, options);
      if (JSON.stringify(finalIdentity) !== JSON.stringify(identity)) {
        throw new Error("Published build identity changed during measurement.");
      }
    } finally {
      await context.close();
    }
  } finally {
    await browser.close();
  }

  const first = measurement.samples[0];
  if (!first) {
    throw new Error("Text publication produced no latency sample.");
  }
  const warm = measurement.samples
    .slice(1)
    .map(sample => sample.milliseconds);
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
      cpu: processors[0]?.model ?? null,
      logicalProcessors: processors.length,
      totalMemoryBytes: totalmem(),
      node: process.version,
      browser: browserVersion,
    },
    configuration: {
      site: options.site,
      samples: options.samples,
      scenario,
      browserCache: "one-context",
      packageWarmup: "excluded",
      resultRetention: "all-measured-results-until-census",
    },
    buildIdentity: identity,
    semanticSha256: measurement.semanticSha256,
    resultIdentity: measurement.resultIdentity,
    latency: {
      firstMilliseconds: round(first.milliseconds),
      warmMilliseconds: warm.length === 0 ? null : summarize(warm),
      samples: measurement.samples.map(sample => ({
        ...sample,
        milliseconds: round(sample.milliseconds),
      })),
    },
    payload: {
      perResult: measurement.perResultPayload,
      retainedResults: measurement.retainedResults,
      retained: measurement.retainedPayload,
      browserHeapBytes: null,
    },
    packageWarmup: measurement.packageWarmup,
  };

  const json = `${JSON.stringify(report, null, 2)}\n`;
  if (options.outputPath === null) {
    process.stdout.write(json);
  } else {
    const output = resolve(options.outputPath);
    mkdirSync(dirname(output), { recursive: true });
    writeFileSync(output, json);
    console.log(`report=${output}`);
  }
  console.log("text-publication-ok");
  console.log(`semantic-sha256=${report.semanticSha256}`);
  console.log(`groups=${report.resultIdentity.groups}`);
  console.log(`complete-members=${report.resultIdentity.completeMembers}`);
  console.log(`retained-results=${report.payload.retainedResults}`);
  console.log(
    `first-ms=${report.latency.firstMilliseconds} `
      + `warm-median-ms=${report.latency.warmMilliseconds?.median ?? "n/a"} `
      + `warm-p95-ms=${report.latency.warmMilliseconds?.p95 ?? "n/a"}`,
  );
  console.log(
    `per-result-utf16=${report.payload.perResult.utf16CodeUnits} `
      + `per-result-utf8=${report.payload.perResult.utf8Bytes} `
      + `serialized-utf8=${report.payload.perResult.serializedUtf8Bytes}`,
  );
}

let options: TextPublicationBenchmarkOptions;
try {
  options = parseTextPublicationBenchmarkArguments(process.argv.slice(2));
} catch (error) {
  console.error(errorMessage(error));
  console.error(textPublicationBenchmarkUsage());
  process.exitCode = 1;
  process.exit();
}

if (options.help) {
  console.log(textPublicationBenchmarkUsage());
} else {
  await runBenchmark(options);
}
