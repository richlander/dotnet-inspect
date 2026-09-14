import assert from "node:assert/strict";
import test from "node:test";
import {
  discoverRuntimeCohortCandidate,
  parseRuntimeCohortPin,
  parseRuntimeProductCommit,
  resolveRuntimeCohortCandidate,
  type RuntimeCohortPin,
  type RuntimeProductCommit,
} from "../scripts/runtime-cohort-pin-model.ts";

const oldCommit = "1".repeat(40);
const newCommit = "2".repeat(40);
const oldSdk = "12.0.100-alpha.1.26459.112";
const oldRuntime = "12.0.0-alpha.1.26459.112";
const newSdk = "12.0.100-alpha.1.26462.105";
const newRuntime = "12.0.0-alpha.1.26462.105";
const packIds = [
  "Microsoft.NET.Runtime.WebAssembly.Sdk.net11",
  "Microsoft.NET.Sdk.WebAssembly.Pack.net11",
  "Microsoft.NETCore.App.Runtime.Mono.net11.browser-wasm",
  "Microsoft.NETCore.App.Runtime.net11.browser-wasm",
  "Microsoft.NETCore.App.Runtime.AOT.Cross.net11.browser-wasm",
] as const;

function pin(): RuntimeCohortPin {
  return parseRuntimeCohortPin({
    schema: 1,
    discovery: {
      channel: "12.0",
      rid: "linux-x64",
      productCommitUrl:
        "https://aka.ms/dotnet/12.0/daily/productCommit-linux-x64.json",
    },
    sdk: {
      version: oldSdk,
      featureBand: "12.0.100-alpha.1",
    },
    runtime: {
      version: oldRuntime,
      repository: "https://github.com/dotnet/dotnet",
      commit: oldCommit,
    },
    workload: {
      id: "wasm-tools",
      targetFramework: "net11.0",
      manifestId: "microsoft.net.workload.mono.toolchain.current",
      packIds,
    },
    feeds: {
      daily:
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json",
      nuget: "https://api.nuget.org/v3/index.json",
    },
  });
}

function productCommit(
  sdkVersion = newSdk,
  runtimeVersion = newRuntime,
  commit = newCommit,
): RuntimeProductCommit {
  return parseRuntimeProductCommit({
    sdk: { version: sdkVersion, commit },
    runtime: { version: runtimeVersion, commit },
    aspnetcore: { version: runtimeVersion, commit },
    windowsdesktop: { version: runtimeVersion, commit },
  });
}

function manifest(
  sdkVersion = newSdk,
  runtimeVersion = newRuntime,
  workloadPackIds: readonly string[] = packIds,
  versionOverrides: Readonly<Record<string, string>> = {},
): Record<string, unknown> {
  return {
    version: sdkVersion,
    workloads: {
      "wasm-tools": {
        packs: [...workloadPackIds],
      },
    },
    packs: Object.fromEntries(packIds.map(id => [
      id,
      id === "Microsoft.NET.Sdk.WebAssembly.Pack.net11"
        ? {
          version: versionOverrides[id] ?? runtimeVersion,
          "alias-to": { any: "Microsoft.NET.Sdk.WebAssembly.Pack" },
        }
        : { version: versionOverrides[id] ?? runtimeVersion },
    ])),
  };
}

function nuspec(
  version = newRuntime,
  commit = newCommit,
): string {
  return [
    "<package><metadata>",
    "<id>Microsoft.NET.Sdk.WebAssembly.Pack</id>",
    `<version>${version}</version>`,
    `<repository commit="${commit}" type="git" `,
    'url="https://github.com/dotnet/dotnet" />',
    "</metadata></package>",
  ].join("");
}

test("new coherent daily candidate advances the shared pin", () => {
  const candidate = resolveRuntimeCohortCandidate(
    pin(),
    productCommit(),
    "12.0.100-alpha.1",
    manifest(),
    nuspec(),
  );
  assert.equal(candidate.sdk.version, newSdk);
  assert.equal(candidate.runtime.version, newRuntime);
  assert.equal(candidate.runtime.commit, newCommit);
  assert.deepEqual(candidate.workload.packIds, packIds);
});

test("same and older candidates are no-ops", () => {
  assert.equal(
    discoverRuntimeCohortCandidate(
      pin(),
      productCommit(oldSdk, oldRuntime, oldCommit),
    ).status,
    "current",
  );
  assert.equal(
    discoverRuntimeCohortCandidate(
      pin(),
      productCommit(
        "12.0.100-alpha.1.26458.100",
        "12.0.0-alpha.1.26458.100",
      ),
    ).status,
    "older",
  );
});

test("product metadata rejects split commits and version suffixes", () => {
  assert.throws(
    () => parseRuntimeProductCommit({
      sdk: { version: newSdk, commit: newCommit },
      runtime: { version: newRuntime, commit: oldCommit },
      aspnetcore: { version: newRuntime, commit: newCommit },
      windowsdesktop: { version: newRuntime, commit: newCommit },
    }),
    /one VMR commit/u,
  );
  assert.throws(
    () => productCommit(
      newSdk,
      "12.0.0-alpha.1.26462.106",
    ),
    /daily suffixes must match/u,
  );
});

test("an immutable current version cannot resolve to another VMR", () => {
  assert.throws(
    () => discoverRuntimeCohortCandidate(
      pin(),
      productCommit(oldSdk, oldRuntime, newCommit),
    ),
    /different VMR commit/u,
  );
});

test("workload manifest must preserve packs and runtime versions", () => {
  assert.throws(
    () => resolveRuntimeCohortCandidate(
      pin(),
      productCommit(),
      "12.0.100-alpha.1",
      manifest(newSdk, newRuntime, packIds.slice(0, -1)),
      nuspec(),
    ),
    /workload packs changed/u,
  );

  assert.throws(
    () => resolveRuntimeCohortCandidate(
      pin(),
      productCommit(),
      "12.0.100-alpha.1",
      manifest(
        newSdk,
        newRuntime,
        packIds,
        { [packIds[0]]: oldRuntime },
      ),
      nuspec(),
    ),
    /must use the runtime version/u,
  );
});

test("WebAssembly package provenance must match the candidate VMR", () => {
  assert.throws(
    () => resolveRuntimeCohortCandidate(
      pin(),
      productCommit(),
      "12.0.100-alpha.1",
      manifest(),
      nuspec(newRuntime, oldCommit),
    ),
    /VMR commit/u,
  );
});
