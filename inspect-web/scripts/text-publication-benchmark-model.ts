export interface TextPublicationBenchmarkOptions {
  readonly site: string;
  readonly samples: number;
  readonly expectedCommit: string;
  readonly expectedVersion: string;
  readonly outputPath: string | null;
  readonly help: boolean;
}

const defaultSamples = 20;

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

function siteUrl(value: string): string {
  const parsed = new URL(value);
  if (parsed.protocol !== "https:" && parsed.protocol !== "http:") {
    throw new Error("--site must use an HTTP or HTTPS URL.");
  }
  parsed.hash = "";
  parsed.search = "";
  parsed.pathname = parsed.pathname.replace(/\/+$/u, "") || "/";
  return parsed.href.replace(/\/$/u, "");
}

function commit(value: string): string {
  if (!/^[0-9a-f]{40}$/iu.test(value)) {
    throw new Error("--expected-commit must be one complete Git SHA.");
  }
  return value.toLowerCase();
}

function nonEmpty(value: string, option: string): string {
  if (value.trim().length === 0) {
    throw new Error(`${option} must not be empty.`);
  }
  return value;
}

export function textPublicationBenchmarkUsage(): string {
  return [
    "Usage:",
    "  npm run benchmark:text-publication -- \\",
    "    --site http://127.0.0.1:4193 \\",
    "    --expected-commit <40-character-sha> \\",
    "    --expected-version <version> \\",
    "    [--samples 20] [--output <report.json>]",
  ].join("\n");
}

export function parseTextPublicationBenchmarkArguments(
  arguments_: readonly string[],
): TextPublicationBenchmarkOptions {
  let site: string | null = null;
  let samples = defaultSamples;
  let expectedCommit: string | null = null;
  let expectedVersion: string | null = null;
  let outputPath: string | null = null;
  let help = false;

  for (let index = 0; index < arguments_.length; index++) {
    const argument = arguments_[index];
    switch (argument) {
      case "--site":
        site = siteUrl(valueAfter(arguments_, index, argument));
        index++;
        break;
      case "--samples":
        samples = positiveInteger(
          valueAfter(arguments_, index, argument),
          argument,
        );
        index++;
        break;
      case "--expected-commit":
        expectedCommit = commit(valueAfter(arguments_, index, argument));
        index++;
        break;
      case "--expected-version":
        expectedVersion = nonEmpty(
          valueAfter(arguments_, index, argument),
          argument,
        );
        index++;
        break;
      case "--output":
        outputPath = valueAfter(arguments_, index, argument);
        index++;
        break;
      case "--help":
      case "-h":
        help = true;
        break;
      default:
        throw new Error(`Unknown argument '${argument ?? ""}'.`);
    }
  }

  if (!help && site === null) {
    throw new Error("--site is required.");
  }
  if (!help && expectedCommit === null) {
    throw new Error("--expected-commit is required.");
  }
  if (!help && expectedVersion === null) {
    throw new Error("--expected-version is required.");
  }

  return {
    site: site ?? "",
    samples,
    expectedCommit: expectedCommit ?? "",
    expectedVersion: expectedVersion ?? "",
    outputPath,
    help,
  };
}
