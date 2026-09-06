export interface BenchmarkSite {
  readonly name: string;
  readonly url: string;
}

export interface BenchmarkOptions {
  readonly sites: readonly BenchmarkSite[];
  readonly samples: number;
  readonly memberCount: number;
  readonly outputPath: string | null;
  readonly trendOutputPath: string | null;
  readonly allowMismatchedCommits: boolean;
  readonly help: boolean;
}

export interface DistributionSummary {
  readonly count: number;
  readonly minimum: number;
  readonly median: number;
  readonly mean: number;
  readonly p95: number;
  readonly maximum: number;
}

export interface BuildCommitObservation {
  readonly site: string;
  readonly commit: string | null;
}

export interface BuildComparability {
  readonly comparable: boolean;
  readonly commitsBySite: Readonly<Record<string, readonly string[]>>;
  readonly reasons: readonly string[];
}

export function isBenchmarkResultAccepted(
  buildComparable: boolean,
  semanticResultsEquivalent: boolean,
  failedRuns: number,
  allowMismatchedCommits: boolean,
): boolean {
  return semanticResultsEquivalent
    && failedRuns === 0
    && (buildComparable || allowMismatchedCommits);
}

const defaultSamples = 3;
const defaultMemberCount = 10;

function valueAfter(
  arguments_: readonly string[],
  index: number,
  option: string,
): string {
  const value = arguments_[index + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}

function positiveInteger(value: string, option: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`${option} must be a positive integer.`);
  }
  return parsed;
}

function parseSite(value: string): BenchmarkSite {
  const separator = value.indexOf("=");
  if (separator <= 0 || separator === value.length - 1) {
    throw new Error("--site must use <name>=<url>.");
  }
  const name = value.slice(0, separator);
  if (!/^[a-z][a-z0-9-]*$/u.test(name)) {
    throw new Error(
      `Site name '${name}' must use lowercase letters, digits, and hyphens.`);
  }
  const parsed = new URL(value.slice(separator + 1));
  if (parsed.protocol !== "https:" && parsed.protocol !== "http:") {
    throw new Error(`Site '${name}' must use an HTTP or HTTPS URL.`);
  }
  parsed.hash = "";
  parsed.search = "";
  parsed.pathname = parsed.pathname.replace(/\/+$/u, "") || "/";
  return { name, url: parsed.href.replace(/\/$/u, "") };
}

export function benchmarkUsage(): string {
  return [
    "Usage:",
    "  npm run benchmark:published -- \\",
    "    --site mono=https://dotnet-inspect.ca \\",
    "    --site coreclr=https://coreclr.dotnet-inspect.ca \\",
    "    [--samples 3] [--member-count 10] [--output <report.json>] \\",
    "    [--trend-output <trend-point.json>] \\",
    "    [--allow-mismatched-commits]",
  ].join("\n");
}

export function parseBenchmarkArguments(
  arguments_: readonly string[],
): BenchmarkOptions {
  const sites: BenchmarkSite[] = [];
  let samples = defaultSamples;
  let memberCount = defaultMemberCount;
  let outputPath: string | null = null;
  let trendOutputPath: string | null = null;
  let allowMismatchedCommits = false;
  let help = false;

  for (let index = 0; index < arguments_.length; index++) {
    const argument = arguments_[index];
    switch (argument) {
      case "--site":
        sites.push(parseSite(valueAfter(arguments_, index, argument)));
        index++;
        break;
      case "--samples":
        samples = positiveInteger(
          valueAfter(arguments_, index, argument),
          argument,
        );
        index++;
        break;
      case "--member-count":
        memberCount = positiveInteger(
          valueAfter(arguments_, index, argument),
          argument,
        );
        index++;
        break;
      case "--output":
        outputPath = valueAfter(arguments_, index, argument);
        index++;
        break;
      case "--trend-output":
        trendOutputPath = valueAfter(arguments_, index, argument);
        index++;
        break;
      case "--allow-mismatched-commits":
        allowMismatchedCommits = true;
        break;
      case "--help":
      case "-h":
        help = true;
        break;
      default:
        throw new Error(`Unknown argument '${argument ?? ""}'.`);
    }
  }

  if (!help && sites.length === 0) {
    throw new Error("At least one --site is required.");
  }
  const duplicate = sites.find(
    (site, index) => sites.findIndex(candidate => candidate.name === site.name)
      !== index,
  );
  if (duplicate) {
    throw new Error(`Site name '${duplicate.name}' is duplicated.`);
  }

  return {
    sites,
    samples,
    memberCount,
    outputPath,
    trendOutputPath,
    allowMismatchedCommits,
    help,
  };
}

function rounded(value: number): number {
  return Math.round(value * 1_000) / 1_000;
}

export function summarize(values: readonly number[]): DistributionSummary {
  if (values.length === 0) {
    throw new Error("Cannot summarize an empty sample.");
  }
  if (values.some(value => !Number.isFinite(value) || value < 0)) {
    throw new Error("Samples must be finite non-negative numbers.");
  }
  const ordered = [...values].sort((left, right) => left - right);
  function valueAt(index: number): number {
    const value = ordered[index];
    if (value === undefined) {
      throw new Error(`Sample index ${index} is outside the distribution.`);
    }
    return value;
  }
  const middle = Math.floor(ordered.length / 2);
  const median = ordered.length % 2 === 0
    ? (valueAt(middle - 1) + valueAt(middle)) / 2
    : valueAt(middle);
  const p95Index = Math.max(0, Math.ceil(ordered.length * 0.95) - 1);
  return {
    count: ordered.length,
    minimum: rounded(valueAt(0)),
    median: rounded(median),
    mean: rounded(
      ordered.reduce((total, value) => total + value, 0) / ordered.length,
    ),
    p95: rounded(valueAt(p95Index)),
    maximum: rounded(valueAt(ordered.length - 1)),
  };
}

export function evaluateBuildComparability(
  observations: readonly BuildCommitObservation[],
): BuildComparability {
  const commits = new Map<string, Set<string>>();
  const missing = new Set<string>();
  for (const observation of observations) {
    let siteCommits = commits.get(observation.site);
    if (!siteCommits) {
      siteCommits = new Set<string>();
      commits.set(observation.site, siteCommits);
    }
    if (observation.commit) {
      siteCommits.add(observation.commit);
    } else {
      missing.add(observation.site);
    }
  }

  const reasons: string[] = [];
  for (const site of missing) {
    reasons.push(`Site '${site}' did not report a commit identity.`);
  }
  for (const [site, siteCommits] of commits) {
    if (siteCommits.size > 1) {
      reasons.push(
        `Site '${site}' changed commits during the benchmark: ${
          [...siteCommits].join(", ")}.`,
      );
    }
  }
  const allCommits = new Set(
    [...commits.values()].flatMap(siteCommits => [...siteCommits]),
  );
  if (allCommits.size > 1) {
    reasons.push(
      `Sites do not share one product commit: ${[...allCommits].join(", ")}.`,
    );
  }

  return {
    comparable: reasons.length === 0 && observations.length > 0,
    commitsBySite: Object.fromEntries(
      [...commits].map(([site, siteCommits]) =>
        [site, [...siteCommits].sort()] as const),
    ),
    reasons,
  };
}
