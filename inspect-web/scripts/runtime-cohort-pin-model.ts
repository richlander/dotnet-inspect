export interface RuntimeCohortPin {
  readonly schema: 1;
  readonly discovery: {
    readonly channel: string;
    readonly rid: string;
    readonly productCommitUrl: string;
  };
  readonly sdk: {
    readonly version: string;
    readonly featureBand: string;
  };
  readonly runtime: {
    readonly version: string;
    readonly repository: string;
    readonly commit: string;
  };
  readonly workload: {
    readonly id: string;
    readonly targetFramework: string;
    readonly manifestId: string;
    readonly packIds: readonly string[];
  };
  readonly feeds: {
    readonly daily: string;
    readonly nuget: string;
  };
}

export interface RuntimeProductCommit {
  readonly sdk: {
    readonly version: string;
    readonly commit: string;
  };
  readonly runtime: {
    readonly version: string;
    readonly commit: string;
  };
  readonly aspnetcore: {
    readonly version: string;
    readonly commit: string;
  };
  readonly windowsdesktop: {
    readonly version: string;
    readonly commit: string;
  };
}

export interface RuntimeCohortCandidateDiscovery {
  readonly status: "current" | "older" | "newer";
  readonly sdkVersion: string;
  readonly runtimeVersion: string;
  readonly vmrCommit: string;
}

interface SemVer {
  readonly major: number;
  readonly minor: number;
  readonly patch: number;
  readonly prerelease: readonly string[];
}

const commitPattern = /^[0-9a-f]{40}$/u;
const identifierPattern = /^[0-9A-Za-z-]+$/u;
const versionPattern =
  /^([0-9]+)\.([0-9]+)\.([0-9]+)(?:-([0-9A-Za-z.-]+))?$/u;

function requireCondition(
  condition: unknown,
  message: string,
): asserts condition {
  if (!condition) throw new Error(message);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function requireRecord(
  value: unknown,
  description: string,
): asserts value is Record<string, unknown> {
  requireCondition(isRecord(value), `${description} must be an object.`);
}

function requireExactKeys(
  value: Record<string, unknown>,
  keys: readonly string[],
  description: string,
): void {
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  requireCondition(
    actual.length === expected.length
      && actual.every((key, index) => key === expected[index]),
    `${description} must contain exactly: ${expected.join(", ")}.`,
  );
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

function requireStringArray(
  value: unknown,
  description: string,
): asserts value is string[] {
  requireCondition(
    Array.isArray(value)
      && value.length > 0
      && value.every(item => typeof item === "string" && item.length > 0),
    `${description} must be a non-empty string array.`,
  );
}

function requireCommit(
  value: unknown,
  description: string,
): asserts value is string {
  requireCondition(
    typeof value === "string" && commitPattern.test(value),
    `${description} must be a lowercase 40-character Git commit.`,
  );
}

function parseSemVer(value: string, description: string): SemVer {
  const match = versionPattern.exec(value);
  requireCondition(match, `${description} must be a semantic version.`);
  const prerelease = match[4]?.split(".") ?? [];
  requireCondition(
    prerelease.every(identifier =>
      identifier.length > 0 && identifierPattern.test(identifier)),
    `${description} has an invalid prerelease identifier.`,
  );
  requireCondition(
    prerelease.every(identifier =>
      !/^[0-9]+$/u.test(identifier)
      || identifier === "0"
      || !identifier.startsWith("0")),
    `${description} has a numeric prerelease identifier with a leading zero.`,
  );
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    prerelease,
  };
}

function compareSemVer(left: SemVer, right: SemVer): number {
  for (const key of ["major", "minor", "patch"] as const) {
    if (left[key] !== right[key]) return left[key] < right[key] ? -1 : 1;
  }
  if (left.prerelease.length === 0 || right.prerelease.length === 0) {
    if (left.prerelease.length === right.prerelease.length) return 0;
    return left.prerelease.length === 0 ? 1 : -1;
  }
  const length = Math.max(left.prerelease.length, right.prerelease.length);
  for (let index = 0; index < length; index += 1) {
    const leftIdentifier = left.prerelease[index];
    const rightIdentifier = right.prerelease[index];
    if (leftIdentifier === rightIdentifier) continue;
    if (leftIdentifier === undefined) return -1;
    if (rightIdentifier === undefined) return 1;
    const leftNumeric = /^[0-9]+$/u.test(leftIdentifier);
    const rightNumeric = /^[0-9]+$/u.test(rightIdentifier);
    if (leftNumeric && rightNumeric) {
      return Number(leftIdentifier) < Number(rightIdentifier) ? -1 : 1;
    }
    if (leftNumeric !== rightNumeric) return leftNumeric ? -1 : 1;
    return leftIdentifier < rightIdentifier ? -1 : 1;
  }
  return 0;
}

function requireDailyVersionPair(
  sdkVersion: string,
  runtimeVersion: string,
  description: string,
): void {
  const sdk = parseSemVer(sdkVersion, `${description} SDK version`);
  const runtime = parseSemVer(
    runtimeVersion,
    `${description} runtime version`,
  );
  requireCondition(
    sdk.major === 12
      && sdk.minor === 0
      && sdk.patch === 100
      && runtime.major === 12
      && runtime.minor === 0
      && runtime.patch === 0,
    `${description} must be a .NET 12 SDK/runtime pair.`,
  );
  requireCondition(
    sdk.prerelease.length > 0
      && sdk.prerelease.length === runtime.prerelease.length
      && sdk.prerelease.every(
        (identifier, index) => identifier === runtime.prerelease[index],
      ),
    `${description} SDK and runtime daily suffixes must match.`,
  );
}

function parseProductComponent(
  value: unknown,
  description: string,
): RuntimeProductCommit["runtime"] {
  requireRecord(value, description);
  requireExactKeys(value, ["commit", "version"], description);
  const version = value.version;
  const commit = value.commit;
  requireString(version, `${description}.version`);
  requireCommit(commit, `${description}.commit`);
  parseSemVer(version, `${description}.version`);
  return { version, commit };
}

export function parseRuntimeCohortPin(value: unknown): RuntimeCohortPin {
  requireRecord(value, "Runtime cohort pin");
  requireExactKeys(
    value,
    ["discovery", "feeds", "runtime", "schema", "sdk", "workload"],
    "Runtime cohort pin",
  );
  requireCondition(value.schema === 1, "Runtime cohort pin schema must be 1.");

  const discovery = value.discovery;
  requireRecord(discovery, "Runtime cohort pin discovery");
  requireExactKeys(
    discovery,
    ["channel", "productCommitUrl", "rid"],
    "Runtime cohort pin discovery",
  );
  const channel = discovery.channel;
  const rid = discovery.rid;
  const productCommitUrl = discovery.productCommitUrl;
  requireString(channel, "Runtime cohort pin discovery.channel");
  requireString(rid, "Runtime cohort pin discovery.rid");
  requireString(
    productCommitUrl,
    "Runtime cohort pin discovery.productCommitUrl",
  );
  requireCondition(channel === "12.0", "Runtime cohort channel must be 12.0.");
  requireCondition(rid === "linux-x64", "Runtime cohort RID must be linux-x64.");
  requireCondition(
    productCommitUrl
      === "https://aka.ms/dotnet/12.0/daily/productCommit-linux-x64.json",
    "Runtime cohort product-commit URL is not authoritative.",
  );

  const sdk = value.sdk;
  requireRecord(sdk, "Runtime cohort pin SDK");
  requireExactKeys(sdk, ["featureBand", "version"], "Runtime cohort pin SDK");
  const sdkVersion = sdk.version;
  const sdkFeatureBand = sdk.featureBand;
  requireString(sdkVersion, "Runtime cohort pin sdk.version");
  requireString(sdkFeatureBand, "Runtime cohort pin sdk.featureBand");

  const runtime = value.runtime;
  requireRecord(runtime, "Runtime cohort pin runtime");
  requireExactKeys(
    runtime,
    ["commit", "repository", "version"],
    "Runtime cohort pin runtime",
  );
  const runtimeVersion = runtime.version;
  const runtimeRepository = runtime.repository;
  const runtimeCommit = runtime.commit;
  requireString(runtimeVersion, "Runtime cohort pin runtime.version");
  requireString(runtimeRepository, "Runtime cohort pin runtime.repository");
  requireCommit(runtimeCommit, "Runtime cohort pin runtime.commit");
  requireCondition(
    runtimeRepository === "https://github.com/dotnet/dotnet",
    "Runtime cohort VMR repository must be dotnet/dotnet.",
  );
  requireDailyVersionPair(sdkVersion, runtimeVersion, "Runtime cohort pin");
  requireCondition(
    sdkVersion.startsWith(`${sdkFeatureBand}.`),
    "Runtime cohort SDK version must belong to its feature band.",
  );

  const workload = value.workload;
  requireRecord(workload, "Runtime cohort pin workload");
  requireExactKeys(
    workload,
    ["id", "manifestId", "packIds", "targetFramework"],
    "Runtime cohort pin workload",
  );
  const workloadId = workload.id;
  const targetFramework = workload.targetFramework;
  const manifestId = workload.manifestId;
  const packIds = workload.packIds;
  requireString(workloadId, "Runtime cohort pin workload.id");
  requireString(
    targetFramework,
    "Runtime cohort pin workload.targetFramework",
  );
  requireString(manifestId, "Runtime cohort pin workload.manifestId");
  requireStringArray(packIds, "Runtime cohort pin workload.packIds");
  requireCondition(workloadId === "wasm-tools", "Runtime workload must be wasm-tools.");
  requireCondition(
    targetFramework === "net11.0",
    "Runtime workload target framework must be net11.0.",
  );
  requireCondition(
    manifestId === "microsoft.net.workload.mono.toolchain.current",
    "Runtime workload manifest ID is unexpected.",
  );
  requireCondition(
    new Set(packIds).size === packIds.length,
    "Runtime workload pack IDs must be unique.",
  );

  const feeds = value.feeds;
  requireRecord(feeds, "Runtime cohort pin feeds");
  requireExactKeys(feeds, ["daily", "nuget"], "Runtime cohort pin feeds");
  const dailyFeed = feeds.daily;
  const nugetFeed = feeds.nuget;
  requireString(dailyFeed, "Runtime cohort pin feeds.daily");
  requireString(nugetFeed, "Runtime cohort pin feeds.nuget");
  requireCondition(
    dailyFeed
      === "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json",
    "Runtime cohort daily feed is unexpected.",
  );
  requireCondition(
    nugetFeed === "https://api.nuget.org/v3/index.json",
    "Runtime cohort NuGet feed is unexpected.",
  );

  return {
    schema: 1,
    discovery: { channel, rid, productCommitUrl },
    sdk: { version: sdkVersion, featureBand: sdkFeatureBand },
    runtime: {
      version: runtimeVersion,
      repository: runtimeRepository,
      commit: runtimeCommit,
    },
    workload: {
      id: workloadId,
      targetFramework,
      manifestId,
      packIds,
    },
    feeds: { daily: dailyFeed, nuget: nugetFeed },
  };
}

export function parseRuntimeProductCommit(
  value: unknown,
): RuntimeProductCommit {
  requireRecord(value, "Runtime product-commit metadata");
  const sdk = parseProductComponent(value.sdk, "productCommit.sdk");
  const runtime = parseProductComponent(
    value.runtime,
    "productCommit.runtime",
  );
  const aspnetcore = parseProductComponent(
    value.aspnetcore,
    "productCommit.aspnetcore",
  );
  const windowsdesktop = parseProductComponent(
    value.windowsdesktop,
    "productCommit.windowsdesktop",
  );
  const commit = sdk.commit;
  requireCondition(
    runtime.commit === commit
      && aspnetcore.commit === commit
      && windowsdesktop.commit === commit,
    "Runtime product-commit components must name one VMR commit.",
  );
  requireCondition(
    aspnetcore.version === runtime.version
      && windowsdesktop.version === runtime.version,
    "Runtime product-commit runtime component versions must match.",
  );
  requireDailyVersionPair(
    sdk.version,
    runtime.version,
    "Runtime product-commit candidate",
  );
  return { sdk, runtime, aspnetcore, windowsdesktop };
}

export function discoverRuntimeCohortCandidate(
  current: RuntimeCohortPin,
  productCommit: RuntimeProductCommit,
): RuntimeCohortCandidateDiscovery {
  const sdkComparison = compareSemVer(
    parseSemVer(productCommit.sdk.version, "Candidate SDK version"),
    parseSemVer(current.sdk.version, "Current SDK version"),
  );
  const runtimeComparison = compareSemVer(
    parseSemVer(productCommit.runtime.version, "Candidate runtime version"),
    parseSemVer(current.runtime.version, "Current runtime version"),
  );
  requireCondition(
    Math.sign(sdkComparison) === Math.sign(runtimeComparison),
    "Candidate SDK and runtime must advance together.",
  );
  if (sdkComparison === 0) {
    requireCondition(
      productCommit.sdk.commit === current.runtime.commit,
      "The current SDK version resolved to a different VMR commit.",
    );
  }
  return {
    status: sdkComparison > 0
      ? "newer"
      : sdkComparison < 0
        ? "older"
        : "current",
    sdkVersion: productCommit.sdk.version,
    runtimeVersion: productCommit.runtime.version,
    vmrCommit: productCommit.sdk.commit,
  };
}

function parseNuspecRepository(
  nuspec: string,
): {
  readonly id: string;
  readonly version: string;
  readonly repository: string;
  readonly commit: string;
} {
  const id = /<id>([^<]+)<\/id>/u.exec(nuspec)?.[1];
  const version = /<version>([^<]+)<\/version>/u.exec(nuspec)?.[1];
  const repositoryElement = /<repository\s+([^>]+)\/?>/u.exec(nuspec)?.[1];
  requireCondition(id, "WebAssembly pack nuspec must contain an ID.");
  requireCondition(version, "WebAssembly pack nuspec must contain a version.");
  requireCondition(
    repositoryElement,
    "WebAssembly pack nuspec must contain repository metadata.",
  );
  const attributes = new Map<string, string>();
  for (const match of repositoryElement.matchAll(/([A-Za-z]+)="([^"]*)"/gu)) {
    const key = match[1];
    const value = match[2];
    requireCondition(
      key !== undefined && value !== undefined,
      "WebAssembly pack repository attribute could not be parsed.",
    );
    attributes.set(key, value);
  }
  requireCondition(
    attributes.get("type") === "git",
    "WebAssembly pack repository must be Git.",
  );
  const repository = attributes.get("url");
  const commit = attributes.get("commit");
  requireString(repository, "WebAssembly pack repository URL");
  requireCommit(commit, "WebAssembly pack repository commit");
  return { id, version, repository, commit };
}

export function resolveRuntimeCohortCandidate(
  current: RuntimeCohortPin,
  productCommit: RuntimeProductCommit,
  sdkFeatureBand: string,
  workloadManifestValue: unknown,
  webAssemblyPackNuspec: string,
): RuntimeCohortPin {
  const discovery = discoverRuntimeCohortCandidate(current, productCommit);
  requireCondition(
    discovery.status === "newer",
    "Runtime cohort resolution requires a newer candidate.",
  );
  requireString(sdkFeatureBand, "Candidate SDK feature band");
  requireCondition(
    discovery.sdkVersion.startsWith(`${sdkFeatureBand}.`),
    "Candidate SDK version must belong to the installed feature band.",
  );

  requireRecord(workloadManifestValue, "Candidate workload manifest");
  const manifestVersion = workloadManifestValue.version;
  requireCondition(
    manifestVersion === discovery.sdkVersion,
    "Candidate workload manifest version must equal the SDK version.",
  );
  const workloads = workloadManifestValue.workloads;
  requireRecord(workloads, "Candidate workload manifest workloads");
  const workload = workloads[current.workload.id];
  requireRecord(workload, `Candidate ${current.workload.id} workload`);
  const workloadPacks = workload.packs;
  requireStringArray(
    workloadPacks,
    `Candidate ${current.workload.id} workload packs`,
  );
  requireCondition(
    workloadPacks.length === current.workload.packIds.length
      && current.workload.packIds.every(id => workloadPacks.includes(id)),
    `Candidate ${current.workload.id} workload packs changed.`,
  );
  const packs = workloadManifestValue.packs;
  requireRecord(packs, "Candidate workload manifest packs");
  for (const id of current.workload.packIds) {
    const pack = packs[id];
    requireRecord(pack, `Candidate workload pack ${id}`);
    requireCondition(
      pack.version === discovery.runtimeVersion,
      `Candidate workload pack ${id} must use the runtime version.`,
    );
  }

  const webAssemblyPack = packs["Microsoft.NET.Sdk.WebAssembly.Pack.net11"];
  requireRecord(
    webAssemblyPack,
    "Candidate Microsoft.NET.Sdk.WebAssembly.Pack.net11",
  );
  const alias = webAssemblyPack["alias-to"];
  requireRecord(alias, "Candidate WebAssembly pack alias");
  const installedPackId = alias.any;
  requireString(installedPackId, "Candidate WebAssembly installed pack ID");

  const nuspec = parseNuspecRepository(webAssemblyPackNuspec);
  requireCondition(
    nuspec.id === installedPackId,
    "Candidate WebAssembly pack nuspec ID does not match the manifest alias.",
  );
  requireCondition(
    nuspec.version === discovery.runtimeVersion,
    "Candidate WebAssembly pack nuspec version must equal the runtime version.",
  );
  requireCondition(
    nuspec.repository === current.runtime.repository,
    "Candidate WebAssembly pack repository changed.",
  );
  requireCondition(
    nuspec.commit === discovery.vmrCommit,
    "Candidate WebAssembly pack VMR commit does not match product metadata.",
  );

  return {
    ...current,
    sdk: {
      version: discovery.sdkVersion,
      featureBand: sdkFeatureBand,
    },
    runtime: {
      ...current.runtime,
      version: discovery.runtimeVersion,
      commit: discovery.vmrCommit,
    },
  };
}

export function overrideRuntimeCohortPin(
  current: RuntimeCohortPin,
  overrides: {
    readonly sdkVersion: string;
    readonly sdkFeatureBand: string;
    readonly runtimeVersion: string;
    readonly vmrCommit: string;
  },
): RuntimeCohortPin {
  return parseRuntimeCohortPin({
    ...current,
    sdk: {
      version: overrides.sdkVersion,
      featureBand: overrides.sdkFeatureBand,
    },
    runtime: {
      ...current.runtime,
      version: overrides.runtimeVersion,
      commit: overrides.vmrCommit,
    },
  });
}
