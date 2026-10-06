import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  dirname,
  join,
  resolve,
} from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const facadeModules = [
  "inspect-web-host",
  "inspect-web-package",
  "inspect-web-library",
  "inspect-web-metadata",
  "inspect-web-analysis",
  "inspect-web-source",
  "inspect-web-call-graph",
  "inspect-web-catalog",
] as const;

interface Options {
  readonly sources: string;
  readonly output: string | undefined;
  readonly declarations: string | undefined;
  readonly modules: string | undefined;
}

interface CommandResult {
  readonly stdout: string;
  readonly stderr: string;
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

function run(command: string, args: readonly string[], cwd: string): CommandResult {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 10 * 1024 * 1024,
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(
      `${command} ${args.join(" ")} failed with exit code ${String(result.status)}`
        + (result.stdout ? `\n${result.stdout}` : "")
        + (result.stderr ? `\n${result.stderr}` : ""),
    );
  }
  return {
    stdout: result.stdout,
    stderr: result.stderr,
  };
}

function parseOptions(inspectWeb: string): Options {
  let sources = resolve(inspectWeb, "DotnetInspect.Web/facades");
  let output: string | undefined;
  let declarations: string | undefined;
  let modules: string | undefined;
  let installRequested = false;

  for (let index = 2; index < process.argv.length; index++) {
    const argument = process.argv[index];
    const value = process.argv[index + 1];
    if (argument === "--sources" && value) {
      sources = resolve(value);
      index++;
    } else if (argument === "--output" && value) {
      output = resolve(value);
      index++;
    } else if (argument === "--install") {
      installRequested = true;
    } else if (argument === "--declarations" && value) {
      declarations = resolve(value);
      index++;
    } else if (argument === "--modules" && value) {
      modules = resolve(value);
      index++;
    } else {
      throw new Error(
        "Usage: compile-engine-facades.ts [--sources <directory>] "
          + "(--output <directory> | --install "
          + "[--declarations <directory>] [--modules <directory>])",
      );
    }
  }

  if (installRequested) {
    declarations ??= resolve(inspectWeb, "src/facades");
    modules ??= resolve(inspectWeb, "DotnetInspect.Web/wwwroot");
  }
  const writesOutput = output !== undefined;
  const installsOutput = declarations !== undefined || modules !== undefined;
  if (writesOutput === installsOutput) {
    throw new Error("Choose exactly one of --output or --install.");
  }
  if ((declarations === undefined) !== (modules === undefined)) {
    throw new Error("--declarations and --modules must be supplied together.");
  }
  return { sources, output, declarations, modules };
}

function assertInventory(
  directory: string,
  predicate: (name: string) => boolean,
  expected: readonly string[],
  label: string,
): void {
  const present = readdirSync(directory)
    .filter(predicate)
    .sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(present) !== JSON.stringify(wanted)) {
    throw new Error(
      `${directory} holds a different ${label} set than the consumer map: `
        + `expected ${wanted.join(", ")}; found ${present.join(", ")}`,
    );
  }
}

function resolveDotnetDeclaration(
  repoRoot: string,
  inspectWeb: string,
): string {
  const configured = process.env.DOTNET_DTS;
  if (configured) {
    const declaration = resolve(configured);
    if (!existsSync(declaration)) {
      throw new Error(`DOTNET_DTS does not exist: ${declaration}`);
    }
    return declaration;
  }

  const dotnet = process.env.DOTNET ?? "dotnet";
  const project = resolve(
    inspectWeb,
    "../src/DotnetInspect.Web/DotnetInspect.Web.csproj",
  );
  const result = run(dotnet, [
    "msbuild",
    project,
    "-nologo",
    "-property:InspectWebIncludeFrontend=true",
    "-target:ProcessFrameworkReferences",
    "-getProperty:NuGetPackageRoot",
    "-getItem:RuntimePack",
  ], repoRoot);
  const parsed: unknown = JSON.parse(result.stdout);
  if (!isRecord(parsed) || !isRecord(parsed.Items)) {
    throw new Error("MSBuild returned no RuntimePack item set.");
  }
  const runtimePacks = parsed.Items.RuntimePack;
  if (!Array.isArray(runtimePacks)) {
    throw new Error("MSBuild returned an invalid RuntimePack item set.");
  }
  const packs: readonly unknown[] = runtimePacks;
  const matches = packs.filter(pack =>
    isRecord(pack)
      && pack.Identity === "Microsoft.NETCore.App.Runtime.Mono.browser-wasm");
  if (matches.length !== 1) {
    throw new Error(
      `Expected one resolved browser-wasm runtime pack; found ${matches.length}.`,
    );
  }
  const pack = matches[0];
  if (!isRecord(pack)) {
    throw new Error("The resolved browser-wasm runtime pack is invalid.");
  }

  const configuredPackageDirectory = pack.PackageDirectory;
  let packageDirectory: string;
  if (
    typeof configuredPackageDirectory === "string"
    && configuredPackageDirectory.length > 0
  ) {
    packageDirectory = configuredPackageDirectory;
  } else {
    const packageRoot = parsed.Properties;
    const id = pack.NuGetPackageId;
    const version = pack.NuGetPackageVersion;
    if (
      !isRecord(packageRoot)
      || typeof packageRoot.NuGetPackageRoot !== "string"
      || typeof id !== "string"
      || typeof version !== "string"
    ) {
      throw new Error(
        "The resolved browser-wasm runtime pack has no package location.",
      );
    }
    packageDirectory = join(
      packageRoot.NuGetPackageRoot,
      id.toLowerCase(),
      version,
    );
  }

  const declaration = join(
    packageDirectory,
    "runtimes/browser-wasm/native/dotnet.d.ts",
  );
  if (!existsSync(declaration)) {
    throw new Error(
      `SDK-owned browser dotnet.d.ts was not found in ${packageDirectory}.`,
    );
  }
  return declaration;
}

function compile(
  repoRoot: string,
  inspectWeb: string,
  sources: string,
  compiled: string,
): void {
  const expectedSources = facadeModules.map(module => `${module}.ts`);
  assertInventory(
    sources,
    name => name.endsWith(".ts"),
    expectedSources,
    "canonical TypeScript facade",
  );

  const scratch = mkdtempSync(join(tmpdir(), "inspect-web-facades-"));
  try {
    const compilerSources = resolve(scratch, "sources");
    mkdirSync(resolve(compilerSources, "_framework"), { recursive: true });
    for (const source of expectedSources) {
      copyFileSync(resolve(sources, source), resolve(compilerSources, source));
    }
    copyFileSync(
      resolveDotnetDeclaration(repoRoot, inspectWeb),
      resolve(compilerSources, "_framework/dotnet.d.ts"),
    );
    copyFileSync(
      resolve(inspectWeb, "DotnetInspect.Web/wwwroot/runtime-loader.js"),
      resolve(compilerSources, "runtime-loader.js"),
    );

    writeFileSync(
      resolve(compilerSources, "tsconfig.json"),
      `${JSON.stringify({
        compilerOptions: {
          allowJs: true,
          checkJs: true,
          declaration: true,
          exactOptionalPropertyTypes: true,
          lib: ["DOM", "ES2022"],
          module: "ESNext",
          moduleResolution: "Bundler",
          newLine: "lf",
          noImplicitReturns: true,
          noUncheckedIndexedAccess: true,
          outDir: "out",
          strict: true,
          target: "ES2022",
          types: [],
          verbatimModuleSyntax: true,
        },
        include: [...expectedSources, "runtime-loader.js"],
      }, null, 2)}\n`,
    );

    const tsc = resolve(inspectWeb, "node_modules/typescript/bin/tsc");
    if (!existsSync(tsc)) {
      throw new Error(
        `TypeScript compiler not found at ${tsc}; run npm ci in inspect-web.`,
      );
    }
    run(process.execPath, [tsc, "-p", "tsconfig.json"], compilerSources);

    const compilerOutput = resolve(compilerSources, "out");
    const declarations = [
      ...facadeModules.map(module => `${module}.d.ts`),
      "runtime-loader.d.ts",
    ];
    const modules = [
      ...facadeModules.map(module => `${module}.js`),
      "runtime-loader.js",
    ];
    assertInventory(
      compilerOutput,
      name => name.endsWith(".d.ts"),
      declarations,
      "compiled declaration",
    );
    assertInventory(
      compilerOutput,
      name => name.endsWith(".js"),
      modules,
      "compiled JavaScript module",
    );

    for (const module of facadeModules) {
      const declaration = readFileSync(
        resolve(compilerOutput, `${module}.d.ts`),
        "utf8",
      );
      if (/RuntimeAPI|dotnet(?:\.js)?/u.test(declaration)) {
        throw new Error(
          `Generated public declaration ${module}.d.ts leaked an SDK runtime type.`,
        );
      }
    }

    writeFileSync(
      resolve(compilerOutput, "package.json"),
      '{ "type": "module" }\n',
    );
    run(process.execPath, [
      resolve(inspectWeb, "scripts/verify-engine-facade-runtime.ts"),
      compilerOutput,
    ], inspectWeb);

    if (existsSync(compiled)) {
      throw new Error(`Output directory already exists: ${compiled}`);
    }
    mkdirSync(compiled, { recursive: true });
    for (const name of [...declarations, ...modules, "package.json"]) {
      copyFileSync(resolve(compilerOutput, name), resolve(compiled, name));
    }
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

function install(
  compiled: string,
  declarations: string,
  modules: string,
): void {
  rmSync(declarations, { recursive: true, force: true });
  mkdirSync(declarations, { recursive: true });
  mkdirSync(modules, { recursive: true });

  for (const name of readdirSync(modules)) {
    if (/^inspect-web-.*\.js$/u.test(name)) {
      rmSync(resolve(modules, name));
    }
  }
  for (const module of facadeModules) {
    copyFileSync(
      resolve(compiled, `${module}.d.ts`),
      resolve(declarations, `${module}.d.ts`),
    );
    copyFileSync(
      resolve(compiled, `${module}.js`),
      resolve(modules, `${module}.js`),
    );
  }

  assertInventory(
    declarations,
    name => name.endsWith(".d.ts"),
    facadeModules.map(module => `${module}.d.ts`),
    "transient declaration",
  );
  assertInventory(
    modules,
    name => /^inspect-web-.*\.js$/u.test(name),
    facadeModules.map(module => `${module}.js`),
    "transient JavaScript facade",
  );
}

function main(): void {
  const inspectWeb = resolve(dirname(fileURLToPath(import.meta.url)), "..");
  const repoRoot = resolve(inspectWeb, "..");
  const options = parseOptions(inspectWeb);
  const temporaryOutput = options.output === undefined
    ? mkdtempSync(join(tmpdir(), "inspect-web-facade-output-"))
    : undefined;
  const compiled = options.output ?? resolve(temporaryOutput ?? "");

  try {
    if (temporaryOutput !== undefined) {
      rmSync(temporaryOutput, { recursive: true });
    }
    compile(repoRoot, inspectWeb, options.sources, compiled);
    if (options.declarations !== undefined && options.modules !== undefined) {
      install(compiled, options.declarations, options.modules);
      console.log("Generated transient inspect-web facade declarations and modules.");
    } else {
      console.log(`Compiled inspect-web facades to ${compiled}.`);
    }
  } finally {
    if (temporaryOutput !== undefined) {
      rmSync(temporaryOutput, { recursive: true, force: true });
    }
  }
}

const invokedPath = process.argv[1];
if (
  invokedPath !== undefined
  && import.meta.url === pathToFileURL(resolve(invokedPath)).href
) {
  main();
}
