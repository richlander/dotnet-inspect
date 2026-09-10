import { createHash } from "node:crypto";
import {
  existsSync,
  readFileSync,
  readdirSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface ManagedAsset {
  readonly name: string;
  readonly virtualPath: string;
}

interface FileGroupSize {
  readonly fileCount: number;
  readonly bytes: number;
}

interface ReadyToRunAssetEvidence {
  readonly virtualPath: string;
  readonly publishedFile: string;
  readonly readyToRunFile: string;
  readonly bytes: number;
  readonly sha256: string;
}

interface IlOnlyAssetEvidence {
  readonly virtualPath: string;
  readonly publishedFile: string;
  readonly bytes: number;
  readonly sha256: string;
}

interface ReadyToRunEvidence {
  readonly schema: 1;
  readonly format: "crossgen2-webassembly";
  readonly mode: "per-assembly";
  readonly loaderConfigFile: string;
  readonly managedAssets: {
    readonly publishedCount: number;
    readonly readyToRunCount: number;
    readonly readyToRunBytes: number;
    readonly inspectWebApplicationPublishedCount: number;
    readonly inspectWebApplicationReadyToRunCount: number;
    readonly ilOnlyCount: number;
    readonly ilOnly: readonly IlOnlyAssetEvidence[];
  };
  readonly readyToRunAssets: readonly ReadyToRunAssetEvidence[];
  readonly frameworkPayload: {
    readonly uncompressed: FileGroupSize;
    readonly brotli: FileGroupSize;
    readonly gzip: FileGroupSize;
    readonly total: FileGroupSize;
  };
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

function managedAsset(value: unknown): ManagedAsset {
  if (
    !isRecord(value)
    || typeof value.name !== "string"
    || typeof value.virtualPath !== "string"
    || !value.virtualPath.endsWith(".wasm")
  ) {
    throw new Error("The runtime loader contains an invalid managed asset.");
  }
  return {
    name: value.name,
    virtualPath: value.virtualPath,
  };
}

function sha256(bytes: Buffer | string): string {
  return createHash("sha256").update(bytes).digest("hex");
}

function serialized(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

function fileGroupSize(
  frameworkDirectory: string,
  files: readonly string[],
): FileGroupSize {
  return {
    fileCount: files.length,
    bytes: files.reduce(
      (total, file) => total + statSync(join(frameworkDirectory, file)).size,
      0,
    ),
  };
}

function runtimeLoaderConfig(
  frameworkDirectory: string,
): { readonly file: string; readonly config: Readonly<Record<string, unknown>> } {
  const loaders = readdirSync(frameworkDirectory)
    .filter(file => /^dotnet\.(?!native\.|runtime\.)[^.]+\.js$/.test(file))
    .map(file => ({
      file,
      text: readFileSync(join(frameworkDirectory, file), "utf8"),
    }))
    .filter(loader => loader.text.includes("/*json-start*/"));
  if (loaders.length !== 1) {
    throw new Error(
      `Expected one fingerprinted dotnet.js loader, found ${loaders.length}.`,
    );
  }

  const loader = loaders[0];
  if (loader === undefined) {
    throw new Error("The fingerprinted dotnet.js loader is missing.");
  }
  const matches = [
    ...loader.text.matchAll(/\/\*json-start\*\/([\s\S]*?)\/\*json-end\*\//g),
  ];
  const match = matches[0];
  if (matches.length !== 1 || match?.[1] === undefined) {
    throw new Error("The fingerprinted dotnet.js loader has no unique runtime config.");
  }
  const parsed: unknown = JSON.parse(match[1]);
  if (!isRecord(parsed)) {
    throw new Error("The runtime loader config is not an object.");
  }
  return { file: loader.file, config: parsed };
}

function managedAssets(config: Readonly<Record<string, unknown>>): readonly ManagedAsset[] {
  const resources = config.resources;
  if (!isRecord(resources)) {
    throw new Error("The runtime loader config has no resources object.");
  }
  const coreAssembly = resources.coreAssembly;
  const assembly = resources.assembly;
  if (!Array.isArray(coreAssembly) || !Array.isArray(assembly)) {
    throw new Error("The runtime loader config has no managed assembly inventory.");
  }
  const assets: ManagedAsset[] = [];
  for (const value of coreAssembly) {
    assets.push(managedAsset(value));
  }
  for (const value of assembly) {
    assets.push(managedAsset(value));
  }
  const virtualPaths = new Set(assets.map(asset => asset.virtualPath));
  const publishedFiles = new Set(assets.map(asset => asset.name));
  if (
    virtualPaths.size !== assets.length
    || publishedFiles.size !== assets.length
  ) {
    throw new Error("The runtime loader config contains duplicate managed assets.");
  }
  return assets.sort((left, right) =>
    left.virtualPath < right.virtualPath
      ? -1
      : left.virtualPath > right.virtualPath ? 1 : 0);
}

function buildEvidence(site: string, readyToRunDirectory: string): ReadyToRunEvidence {
  const frameworkDirectory = join(site, "_framework");
  if (!existsSync(frameworkDirectory) || !existsSync(readyToRunDirectory)) {
    throw new Error("The published framework or ReadyToRun directory is missing.");
  }

  const loader = runtimeLoaderConfig(frameworkDirectory);
  const publishedAssets = managedAssets(loader.config);
  const assetByReadyToRunFile = new Map(
    publishedAssets.map(asset => [
      asset.virtualPath.replace(/\.wasm$/, ".dll"),
      asset,
    ]),
  );
  const readyToRunFiles = readdirSync(readyToRunDirectory)
    .filter(file => file.endsWith(".dll"))
    .sort();
  if (readyToRunFiles.length < 2) {
    throw new Error("ReadyToRun publication did not produce per-assembly output.");
  }

  const readyToRunAssets = readyToRunFiles.map(file => {
    const asset = assetByReadyToRunFile.get(file);
    if (!asset) {
      throw new Error(`ReadyToRun output '${file}' has no published managed asset.`);
    }
    const source = readFileSync(join(readyToRunDirectory, file));
    const published = readFileSync(join(frameworkDirectory, asset.name));
    if (
      source.length < 4
      || !source.subarray(0, 4).equals(Buffer.from([0x00, 0x61, 0x73, 0x6d]))
    ) {
      throw new Error(`ReadyToRun output '${file}' is not a WebAssembly module.`);
    }
    if (!source.equals(published)) {
      throw new Error(
        `Published managed asset '${asset.name}' differs from '${file}'.`,
      );
    }
    return {
      virtualPath: asset.virtualPath,
      publishedFile: asset.name,
      readyToRunFile: file,
      bytes: source.length,
      sha256: sha256(source),
    };
  });

  const readyToRunVirtualPaths = new Set(
    readyToRunAssets.map(asset => asset.virtualPath),
  );
  const ilOnly = publishedAssets
    .filter(asset => !readyToRunVirtualPaths.has(asset.virtualPath))
    .map(asset => {
      const published = readFileSync(join(frameworkDirectory, asset.name));
      return {
        virtualPath: asset.virtualPath,
        publishedFile: asset.name,
        bytes: published.length,
        sha256: sha256(published),
      };
    });
  const applicationAssets = publishedAssets
    .filter(asset => asset.virtualPath.startsWith("InspectWeb.Engine"));
  const applicationReadyToRunAssets = applicationAssets
    .filter(asset => readyToRunVirtualPaths.has(asset.virtualPath));
  if (
    applicationAssets.length === 0
    || applicationReadyToRunAssets.length !== applicationAssets.length
  ) {
    throw new Error(
      "Every published InspectWeb.Engine application asset must be ReadyToRun.",
    );
  }
  if (!readyToRunVirtualPaths.has("System.Private.CoreLib.wasm")) {
    throw new Error("System.Private.CoreLib is not a published ReadyToRun asset.");
  }

  const frameworkFiles = readdirSync(frameworkDirectory).sort();
  const uncompressedFiles = frameworkFiles
    .filter(file => !file.endsWith(".br") && !file.endsWith(".gz"));
  const brotliFiles = frameworkFiles.filter(file => file.endsWith(".br"));
  const gzipFiles = frameworkFiles.filter(file => file.endsWith(".gz"));

  return {
    schema: 1,
    format: "crossgen2-webassembly",
    mode: "per-assembly",
    loaderConfigFile: loader.file,
    managedAssets: {
      publishedCount: publishedAssets.length,
      readyToRunCount: readyToRunAssets.length,
      readyToRunBytes: readyToRunAssets
        .reduce((total, asset) => total + asset.bytes, 0),
      inspectWebApplicationPublishedCount: applicationAssets.length,
      inspectWebApplicationReadyToRunCount:
        applicationReadyToRunAssets.length,
      ilOnlyCount: ilOnly.length,
      ilOnly,
    },
    readyToRunAssets,
    frameworkPayload: {
      uncompressed: fileGroupSize(frameworkDirectory, uncompressedFiles),
      brotli: fileGroupSize(frameworkDirectory, brotliFiles),
      gzip: fileGroupSize(frameworkDirectory, gzipFiles),
      total: fileGroupSize(frameworkDirectory, frameworkFiles),
    },
  };
}

function readyToRunReceipt(evidence: ReadyToRunEvidence, evidenceText: string): unknown {
  return {
    evidenceFile: "ready-to-run-assets.json",
    evidenceSha256: sha256(evidenceText),
    format: evidence.format,
    mode: evidence.mode,
    publishedManagedAssetCount: evidence.managedAssets.publishedCount,
    readyToRunAssetCount: evidence.managedAssets.readyToRunCount,
    readyToRunBytes: evidence.managedAssets.readyToRunBytes,
    inspectWebApplicationAssetCount:
      evidence.managedAssets.inspectWebApplicationPublishedCount,
    ilOnlyAssets: evidence.managedAssets.ilOnly
      .map(asset => asset.virtualPath),
    frameworkPayload: evidence.frameworkPayload,
  };
}

function readCohortReceipt(path: string): Record<string, unknown> {
  const parsed: unknown = JSON.parse(readFileSync(path, "utf8"));
  if (!isRecord(parsed)) {
    throw new Error("The runtime cohort receipt is not an object.");
  }
  const configuration = parsed.configuration;
  if (
    !isRecord(configuration)
    || configuration.publishReadyToRun !== true
    || configuration.publishReadyToRunComposite !== false
    || configuration.readyToRunContainer !== "wasm"
  ) {
    throw new Error("The runtime cohort receipt does not select non-composite Wasm ReadyToRun.");
  }
  return { ...parsed };
}

export function recordCoreClrReadyToRunEvidence(
  siteArgument: string,
  readyToRunArgument: string,
  cohortArgument: string,
): void {
  const site = resolve(siteArgument);
  const readyToRunDirectory = resolve(readyToRunArgument);
  const cohortDirectory = resolve(cohortArgument);
  const evidencePath = join(cohortDirectory, "ready-to-run-assets.json");
  const cohortPath = join(cohortDirectory, "runtime-cohort.json");
  const evidence = buildEvidence(site, readyToRunDirectory);
  const evidenceText = serialized(evidence);
  const cohort = readCohortReceipt(cohortPath);
  cohort.readyToRun = readyToRunReceipt(evidence, evidenceText);
  writeFileSync(evidencePath, evidenceText);
  writeFileSync(cohortPath, serialized(cohort));
}

export function verifyCoreClrReadyToRunEvidence(
  siteArgument: string,
  readyToRunArgument: string,
  cohortArgument: string,
): void {
  const site = resolve(siteArgument);
  const readyToRunDirectory = resolve(readyToRunArgument);
  const cohortDirectory = resolve(cohortArgument);
  const evidencePath = join(cohortDirectory, "ready-to-run-assets.json");
  const cohortPath = join(cohortDirectory, "runtime-cohort.json");
  const evidence = buildEvidence(site, readyToRunDirectory);
  const evidenceText = serialized(evidence);
  if (readFileSync(evidencePath, "utf8") !== evidenceText) {
    throw new Error("The recorded ReadyToRun asset evidence does not match publication.");
  }
  const cohort = readCohortReceipt(cohortPath);
  const expectedReceipt = readyToRunReceipt(evidence, evidenceText);
  if (JSON.stringify(cohort.readyToRun) !== JSON.stringify(expectedReceipt)) {
    throw new Error("The runtime cohort ReadyToRun receipt does not match publication.");
  }
}

const invokedPath = process.argv[1];
if (
  invokedPath !== undefined
  && import.meta.url === pathToFileURL(resolve(invokedPath)).href
) {
  const [mode, site, readyToRunDirectory, cohortDirectory] = process.argv.slice(2);
  if (
    (mode !== "--record" && mode !== "--verify")
    || !site
    || !readyToRunDirectory
    || !cohortDirectory
  ) {
    throw new Error(
      `Usage: node ${basename(invokedPath)} (--record|--verify) `
      + "<site-directory> <ready-to-run-directory> <cohort-directory>",
    );
  }
  if (mode === "--record") {
    recordCoreClrReadyToRunEvidence(site, readyToRunDirectory, cohortDirectory);
  }
  else {
    verifyCoreClrReadyToRunEvidence(site, readyToRunDirectory, cohortDirectory);
  }
}
