import {
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { dirname, resolve } from "node:path";
import {
  discoverRuntimeCohortCandidate,
  overrideRuntimeCohortPin,
  parseRuntimeCohortPin,
  parseRuntimeProductCommit,
  resolveRuntimeCohortCandidate,
  type RuntimeCohortPin,
} from "./runtime-cohort-pin-model.ts";

function argumentAfter(
  arguments_: readonly string[],
  option: string,
): string {
  const index = arguments_.indexOf(option);
  const value = index < 0 ? undefined : arguments_[index + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}

function optionalArgumentAfter(
  arguments_: readonly string[],
  option: string,
): string | undefined {
  const index = arguments_.indexOf(option);
  if (index < 0) return undefined;
  const value = arguments_[index + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}

function readText(path: string): string {
  return readFileSync(resolve(path), "utf8");
}

function readJson(path: string): unknown {
  return JSON.parse(readText(path));
}

function writeText(path: string, text: string): void {
  const output = resolve(path);
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, text);
}

function writeJson(path: string, value: unknown): void {
  writeText(path, `${JSON.stringify(value, null, 2)}\n`);
}

function appendGithubOutputs(
  path: string | undefined,
  outputs: Readonly<Record<string, string>>,
): void {
  if (!path) return;
  const text = Object.entries(outputs)
    .map(([key, value]) => `${key}=${value}\n`)
    .join("");
  writeFileSync(resolve(path), text, { flag: "a" });
}

function pinEnvironment(pin: RuntimeCohortPin): string {
  const entries = {
    DOTNET_DAILY_FEED: pin.feeds.daily,
    DOTNET_NUGET_FEED: pin.feeds.nuget,
    DOTNET_RUNTIME_VERSION: pin.runtime.version,
    DOTNET_SDK_FEATURE_BAND: pin.sdk.featureBand,
    DOTNET_SDK_VERSION: pin.sdk.version,
    DOTNET_VMR_COMMIT: pin.runtime.commit,
    DOTNET_VMR_REPOSITORY: pin.runtime.repository,
    INSPECT_WEB_RUNTIME_TARGET_FRAMEWORK: pin.workload.targetFramework,
    INSPECT_WEB_RUNTIME_WORKLOAD: pin.workload.id,
    INSPECT_WEB_RUNTIME_WORKLOAD_MANIFEST: pin.workload.manifestId,
  };
  return Object.entries(entries)
    .map(([key, value]) => `${key}=${value}\n`)
    .join("");
}

function pinWithEnvironmentOverride(pin: RuntimeCohortPin): RuntimeCohortPin {
  const sdkVersion = process.env.RUNTIME_COHORT_SDK_VERSION_OVERRIDE;
  const sdkFeatureBand =
    process.env.RUNTIME_COHORT_SDK_FEATURE_BAND_OVERRIDE;
  const runtimeVersion = process.env.RUNTIME_COHORT_RUNTIME_VERSION_OVERRIDE;
  const vmrCommit = process.env.RUNTIME_COHORT_VMR_COMMIT_OVERRIDE;
  const values = [sdkVersion, sdkFeatureBand, runtimeVersion, vmrCommit];
  if (values.every(value => !value)) return pin;
  if (values.some(value => !value)) {
    throw new Error("Runtime cohort overrides must be supplied as one complete set.");
  }
  if (!sdkVersion || !sdkFeatureBand || !runtimeVersion || !vmrCommit) {
    throw new Error("Runtime cohort override validation failed.");
  }
  return overrideRuntimeCohortPin(pin, {
    sdkVersion,
    sdkFeatureBand,
    runtimeVersion,
    vmrCommit,
  });
}

const arguments_ = process.argv.slice(2);
const command = arguments_[0];

if (command === "export") {
  const pin = pinWithEnvironmentOverride(parseRuntimeCohortPin(
    readJson(argumentAfter(arguments_, "--pin")),
  ));
  const environment = pinEnvironment(pin);
  const output = optionalArgumentAfter(arguments_, "--output");
  if (output) writeText(output, environment);
  else process.stdout.write(environment);
} else if (command === "discover") {
  const current = parseRuntimeCohortPin(
    readJson(argumentAfter(arguments_, "--current")),
  );
  const productCommit = parseRuntimeProductCommit(
    readJson(argumentAfter(arguments_, "--product-commit")),
  );
  const discovery = discoverRuntimeCohortCandidate(current, productCommit);
  appendGithubOutputs(
    optionalArgumentAfter(arguments_, "--github-output"),
    {
      update_available: String(discovery.status === "newer"),
      candidate_status: discovery.status,
      sdk_version: discovery.sdkVersion,
      runtime_version: discovery.runtimeVersion,
      vmr_commit: discovery.vmrCommit,
    },
  );
  console.log(JSON.stringify(discovery));
} else if (command === "resolve") {
  const current = parseRuntimeCohortPin(
    readJson(argumentAfter(arguments_, "--current")),
  );
  const productCommit = parseRuntimeProductCommit(
    readJson(argumentAfter(arguments_, "--product-commit")),
  );
  const candidate = resolveRuntimeCohortCandidate(
    current,
    productCommit,
    argumentAfter(arguments_, "--sdk-feature-band"),
    readJson(argumentAfter(arguments_, "--workload-manifest")),
    readText(argumentAfter(arguments_, "--webassembly-pack-nuspec")),
  );
  writeJson(argumentAfter(arguments_, "--output"), candidate);
  appendGithubOutputs(
    optionalArgumentAfter(arguments_, "--github-output"),
    {
      update_available: "true",
      sdk_version: candidate.sdk.version,
      sdk_feature_band: candidate.sdk.featureBand,
      runtime_version: candidate.runtime.version,
      vmr_commit: candidate.runtime.commit,
    },
  );
  console.log(
    `Resolved ${candidate.sdk.version} / ${candidate.runtime.version} `
      + `from ${candidate.runtime.commit}.`,
  );
} else {
  throw new Error(
    "Usage: runtime-cohort-pin.ts export --pin <file> [--output <file>]\n"
      + "   or: runtime-cohort-pin.ts discover --current <file> "
      + "--product-commit <file> [--github-output <file>]\n"
      + "   or: runtime-cohort-pin.ts resolve --current <file> "
      + "--product-commit <file> --sdk-feature-band <version> "
      + "--workload-manifest <file> --webassembly-pack-nuspec <file> "
      + "--output <file> [--github-output <file>]",
  );
}
