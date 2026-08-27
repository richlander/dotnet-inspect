import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  supportedAnalysisHosts,
  verifyAnalysisHost,
} from "../scripts/verify-analysis-host.js";
import { verifySiteArtifact } from "../scripts/verify-site-artifact.js";

interface PackageLockEntry {
  readonly link?: boolean;
  readonly resolved?: string;
  readonly integrity?: string;
  readonly optionalDependencies?: Readonly<Record<string, string>>;
  readonly os?: readonly string[];
  readonly cpu?: readonly string[];
  readonly libc?: readonly string[];
}

interface PackageLock {
  readonly packages: Readonly<Record<string, PackageLockEntry>>;
}

interface PackageJson {
  readonly scripts: Readonly<Record<string, string>>;
}

interface OxlintConfig {
  readonly ignorePatterns?: readonly string[];
}

interface TsconfigFile {
  readonly extends?: string;
  readonly compilerOptions: Readonly<Record<string, unknown>>;
  readonly include?: readonly string[];
}

interface StaticWebAppRoute {
  readonly route: string;
  readonly rewrite?: string;
  readonly headers?: Readonly<Record<string, string>>;
}

interface StaticWebAppConfig {
  readonly routes: readonly StaticWebAppRoute[];
  readonly navigationFallback: {
    readonly rewrite: string;
    readonly exclude: readonly string[];
  };
}

// `JSON.parse` is typed `any`, and an `any` here would silently switch off checking for
// every config read below -- the same defeat this file's own strictness tests guard
// against. Naming the shape each test relies on keeps those reads checked, so a renamed
// config key becomes a compile error rather than an `undefined` comparison.
//
// These are repo-owned files rather than untrusted input, and a shape that stops matching
// fails the assertion that reads it, so this asserts the shape instead of validating it.
// That mirrors `parseEngineJson` in `src/dotnet-inspect.ts`, including the two targeted
// disables it needs. `reportUnusedDisableDirectives` is an error here, so these directives
// cannot outlive the rules they suppress.
// oxlint-disable-next-line typescript/no-unnecessary-type-parameters
function readJson<T>(specifier: string): T {
  const parsed: unknown = JSON.parse(
    readFileSync(new URL(specifier, import.meta.url), "utf8"),
  );
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return parsed as T;
}

const packageLock = readJson<PackageLock>("../package-lock.json");
const packageJson = readJson<PackageJson>("../package.json");
const oxlintConfig = readJson<OxlintConfig>("../.oxlintrc.json");
const browserTsconfig = readJson<TsconfigFile>("../tsconfig.json");
const testTsconfig = readJson<TsconfigFile>("tsconfig.json");
const staticWebAppConfig
  = readJson<StaticWebAppConfig>("../staticwebapp.config.json");
const siteIndexHtml = readFileSync(
  new URL("../index.html", import.meta.url),
  "utf8",
);

test("the package lock pins every registry artifact", () => {
  const missingArtifactIdentity = Object.entries(packageLock.packages)
    .filter(([path, entry]) =>
      path
      && !entry.link
      && (typeof entry.resolved !== "string" || typeof entry.integrity !== "string"))
    .map(([path]) => path);

  assert.deepEqual(missingArtifactIdentity, []);
});

test("TypeScript compiler contexts keep Node globals out of browser source", () => {
  assert.deepEqual(browserTsconfig.compilerOptions.types, []);
  assert.deepEqual(browserTsconfig.include, ["src/**/*.ts"]);
  assert.equal(testTsconfig.extends, "../tsconfig.json");
  assert.deepEqual(testTsconfig.compilerOptions.types, ["node"]);
  assert.deepEqual(testTsconfig.include, ["./**/*.ts"]);
  assert.equal(
    packageJson.scripts.typecheck,
    "tsc --noEmit && tsc --noEmit -p test/tsconfig.json "
      + "&& tsc --noEmit -p tsconfig.runtime-wrapper.json",
  );
});

test("the strictness options this project relies on stay enabled", () => {
  // Adversarial review (Claude Opus 5) pointed out that deleting
  // `noUncheckedIndexedAccess` left the entire suite green: the option is this project's
  // deliverable and nothing asserted it. Every guard written to satisfy it would still
  // compile without it, so its removal is silent and permanent.
  //
  // This pin is the cheap half of that answer, and on its own it is only a restatement of
  // the config. The test below is the half that actually holds, because it asserts the
  // *effect* rather than the declaration.
  for (const option of ["strict", "noUncheckedIndexedAccess", "noImplicitReturns"]) {
    assert.equal(
      browserTsconfig.compilerOptions[option],
      true,
      `${option} must stay enabled`);
  }
  assert.equal(testTsconfig.extends, "../tsconfig.json");
  assert.equal(testTsconfig.compilerOptions.noUncheckedIndexedAccess, undefined,
    "the test project must inherit the option rather than restate it");
});

// Round 6 review (GPT-5.6 Sol, converging with Claude Opus 5) defeated the pin above
// without touching any value it reads: adding `"noCheck": true` leaves every pinned
// option literally `true` while TypeScript stops checking anything at all, and the suite
// stayed green with a genuinely unsafe indexed read restored. A per-file suppression
// directive walked past it the same way.
//
// Enumerating the neutering options is the losing move -- `noCheck` was already the
// second one found, and the compiler keeps adding surface. So this asserts the property
// the project actually depends on: *this configuration rejects an unchecked indexed
// read*. Anything that turns checking off, at any level, fails here regardless of how it
// spells itself, because the fixture stops being rejected.
//
// Opus established that the remaining vector, narrowing `include`/`exclude` to drop files
// from the program, is already caught by `npm run analyze`: oxlint's type-aware rules
// lose their type information and fail. So this covers the vectors that gate leaves open.
for (const [name, project] of [
  ["browser", "tsconfig.json"],
  ["test", "test/tsconfig.json"],
] as const) {
  test(`the ${name} project rejects an unchecked indexed read`, () => {
    const root = new URL("../", import.meta.url);
    const probe = mkdtempSync(join(tmpdir(), "inspect-web-strictness-"));
    try {
      // An indexed read used without a presence test. Under `noUncheckedIndexedAccess`
      // the element type includes `undefined`, so `.length` on it cannot compile.
      writeFileSync(
        join(probe, "probe.ts"),
        "export function first(values: string[]): number {\n"
          + "  return values[0].length;\n"
          + "}\n",
      );
      writeFileSync(
        join(probe, "tsconfig.json"),
        JSON.stringify({
          extends: fileURLToPath(new URL(project, root)),
          compilerOptions: { noEmit: true, types: [] },
          include: ["probe.ts"],
        }),
      );

      const compile = spawnSync(
        process.execPath,
        [fileURLToPath(new URL("node_modules/typescript/bin/tsc", root)), "-p", probe],
        { encoding: "utf8" },
      );

      assert.notEqual(
        compile.status,
        0,
        `the ${name} project accepted an unchecked indexed read, so its strictness is not `
          + `in effect however the options are spelled:\n${compile.stdout}`);
      // Pin the reason as well as the failure. Any config error would also be non-zero,
      // and would leave this passing while proving nothing about strictness.
      assert.match(
        compile.stdout,
        /probe\.ts\(2,10\): error TS18048|probe\.ts\(2,10\): error TS2532/,
        `the ${name} project failed for some reason other than the unchecked read:\n`
          + compile.stdout);
    } finally {
      rmSync(probe, { recursive: true, force: true });
    }
  });
}

// The other half of the same vector: `noCheck` turns the compiler off for a project, and
// the per-file suppression directives turn it off for a single file. The fixture above
// cannot see the second, because it compiles a file of its own. Unlike a naming or roster
// ban, the set of suppression directives is closed and owned by the compiler rather than
// by us, so listing them here is not a restatement that can drift out of date.
//
// Converting this file from JavaScript put it inside its own scan. The directives are
// therefore assembled from parts rather than spelled out: a literal would make this gate
// report itself, and excluding this file would leave a hole in the one test that closes
// the per-file vector. Assembling keeps this file in scope and the scan exactly as
// strict, which is why the prose above names the directives only in the abstract.
function checkedSourceFiles(extensions: readonly string[]): string[] {
  const root = new URL("../", import.meta.url);
  const files: string[] = [];
  for (const directory of ["src", "test"]) {
    for (const entry of readdirSync(new URL(directory, root), {
      recursive: true,
      withFileTypes: true,
    })) {
      if (entry.isFile()
          && extensions.some(extension => entry.name.endsWith(extension))) {
        files.push(join(entry.parentPath, entry.name));
      }
    }
  }
  return files;
}

test("no source file suppresses type checking", () => {
  const root = new URL("../", import.meta.url);
  const files = checkedSourceFiles([".ts"]);

  assert.ok(files.length > 50, `expected the TypeScript sources, found ${files.length}`);
  const suppressionPattern = new RegExp(
    ["nocheck", "ignore"].map(directive => `@ts-${directive}`).join("|"),
  );
  // This file is one of the scanned files, so the assembly above is load-bearing rather
  // than stylistic; a literal pattern would fail this assertion on this very file.
  assert.ok(
    files.includes(fileURLToPath(import.meta.url)),
    "the scan must cover the file that defines it");
  const suppressed = files.filter(file =>
    suppressionPattern.test(readFileSync(file, "utf8")));
  assert.deepEqual(
    suppressed.map(file => file.slice(fileURLToPath(root).length)),
    [],
    "these files opt out of type checking; use a narrowing guard or @ts-expect-error");
});

// The third way out of type checking is to not write TypeScript at all. The oxlint config
// turns the `no-unsafe-*` family off for `**/*.js`, which is right for the build script,
// the generated engine wrapper, and the Vite config, but would silently exempt a new
// JavaScript file dropped into the application or its tests. Both directories are now
// wholly TypeScript, so the cheapest way to keep the exemption scoped to the files that
// need it is to assert that neither directory has any JavaScript to exempt.
test("the application and its tests are wholly TypeScript", () => {
  const root = new URL("../", import.meta.url);
  const unchecked = checkedSourceFiles([".js", ".jsx", ".mjs", ".cjs"]);

  assert.deepEqual(
    unchecked.map(file => file.slice(fileURLToPath(root).length)),
    [],
    "src and test are TypeScript-only; the oxlint `**/*.js` override would exempt these "
      + "files from the no-unsafe rules that the rest of the application is held to");
});

test("static hosting serves credits links through the application entry point", () => {
  const creditsRoutes = staticWebAppConfig.routes
    .filter(route => route.route === "/credits" || route.route === "/credits/");

  assert.deepEqual(creditsRoutes, [
    {
      route: "/credits",
      rewrite: "/index.html",
      headers: {
        "Cache-Control": "no-cache, no-store, must-revalidate",
      },
    },
  ]);
  assert.equal(staticWebAppConfig.navigationFallback.rewrite, "/index.html");
  assert.deepEqual(
    staticWebAppConfig.navigationFallback.exclude,
    ["/api/*", "/assets/*", "/_framework/*"],
  );
  assert.match(siteIndexHtml, /<base href="\/" \/>/);
});

const linuxLibcs = ["glibc", "musl"];

function optionalNativeVariants(
  packagePath: string,
  dependencyPrefix: string,
): Set<string> {
  const packageEntry = packageLock.packages[packagePath];
  assert.ok(packageEntry);
  const dependencies = Object.keys(packageEntry.optionalDependencies ?? {})
    .filter(dependency => dependency.startsWith(dependencyPrefix));
  assert.notEqual(dependencies.length, 0);

  const variants = new Set<string>();
  for (const dependency of dependencies) {
    const nativeEntry = packageLock.packages[`node_modules/${dependency}`];
    assert.ok(nativeEntry);
    const { os, cpu } = nativeEntry;
    assert.equal(os?.length, 1);
    assert.equal(cpu?.length, 1);
    assert.ok(nativeEntry.libc === undefined || nativeEntry.libc.length === 1);

    // The length assertions above already establish these, but they are `assert` calls
    // rather than narrowing, so the indexed reads still need a guard to compile.
    const osName = os?.[0];
    const cpuName = cpu?.[0];
    assert.ok(osName !== undefined && cpuName !== undefined);

    const host = `${osName}-${cpuName}`;
    const libcs = nativeEntry.libc
      ?? (osName === "linux" ? linuxLibcs : ["none"]);
    for (const libc of libcs) {
      variants.add(`${host}/${libc}`);
    }
  }
  return variants;
}

function completeAnalyzerHosts(
  oxlintVariants: ReadonlySet<string>,
  tsgolintVariants: ReadonlySet<string>,
): Set<string> {
  const sharedVariants = new Set(
    [...tsgolintVariants].filter(variant => oxlintVariants.has(variant)),
  );
  const hosts = new Set(
    [...oxlintVariants, ...tsgolintVariants]
      .map(variant => variant.slice(0, variant.indexOf("/"))),
  );

  return new Set([...hosts].filter(host => {
    const requiredLibcs = host.startsWith("linux-") ? linuxLibcs : ["none"];
    return requiredLibcs.every(libc => sharedVariants.has(`${host}/${libc}`));
  }));
}

test("the analysis host check matches locked native packages and lint wiring", () => {
  const oxlintVariants = optionalNativeVariants(
    "node_modules/oxlint",
    "@oxlint/binding-",
  );
  const tsgolintVariants = optionalNativeVariants(
    "node_modules/oxlint-tsgolint",
    "@oxlint-tsgolint/",
  );
  const expectedHosts = completeAnalyzerHosts(oxlintVariants, tsgolintVariants);

  assert.deepEqual(new Set(supportedAnalysisHosts), expectedHosts);
  const availableHosts = new Set(
    [...oxlintVariants, ...tsgolintVariants]
      .map(variant => variant.slice(0, variant.indexOf("/"))),
  );
  for (const host of availableHosts) {
    const separator = host.indexOf("-");
    const platform = host.slice(0, separator);
    const architecture = host.slice(separator + 1);
    const verify = () => verifyAnalysisHost(platform, architecture);
    if (expectedHosts.has(host)) {
      assert.doesNotThrow(verify);
    } else {
      assert.throws(verify, new RegExp(`current host is ${host}`));
    }
  }

  assert.equal(
    packageJson.scripts.lint,
    "node scripts/verify-analysis-host.js && oxlint src test scripts "
      + "engine/wwwroot/inspect-web-engine.js vite.config.js",
  );
});

test("the lint gate includes both generated tsbindgen outputs", () => {
  assert.ok(
    !(oxlintConfig.ignorePatterns ?? []).includes("src/inspect-web-engine.d.ts"),
  );
  // Reading the script through an index signature makes its absence a real possibility
  // rather than a silent `undefined` handed to `assert.match`, which would fail with a
  // type error about the argument instead of naming the missing script.
  const lintScript = packageJson.scripts.lint;
  assert.ok(lintScript !== undefined, "package.json must define a lint script");
  assert.match(lintScript, /(?:^| )src(?: |$)/);
  assert.match(
    lintScript,
    /(?:^| )engine\/wwwroot\/inspect-web-engine\.js(?: |$)/,
  );
});

// The fixture below is mutated in place across the assertions, so it is typed rather than
// inferred: an inferred object literal makes every key required, which forbids `delete`,
// and an indexed read back would be `ManifestEntry | undefined`. Holding the entry that
// the test mutates in its own binding keeps those mutations checked and undefined-free.
interface ManifestEntry {
  file: string;
  css?: readonly string[];
  dynamicImports?: readonly string[];
  isEntry?: boolean;
  isDynamicEntry?: boolean;
}

test("the site artifact rejects a missing Vite output", (context) => {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-artifact-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  mkdirSync(join(site, "assets"));
  const indexEntry: ManifestEntry = {
    file: "assets/index.js",
    css: ["assets/index.css"],
    dynamicImports: ["src/dotnet-inspect.ts"],
    isEntry: true,
  };
  const manifest: Record<string, ManifestEntry> = {
    "index.html": indexEntry,
    "src/dotnet-inspect.ts": {
      file: "assets/app.js",
      isDynamicEntry: true,
    },
  };
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  writeFileSync(
    join(site, "index.html"),
    '<base href="/">'
      + '<link rel="preload" href="/_framework/dotnet.js">'
      + '<script type="importmap">{}</script>'
      + '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  writeFileSync(join(site, "assets/index.js"), "");
  writeFileSync(join(site, "assets/index.css"), "");
  writeFileSync(join(site, "assets/app.js"), "");

  assert.doesNotThrow(() => verifySiteArtifact(site));
  writeFileSync(
    join(site, "index.html"),
    '<link rel="preload" href="/_framework/dotnet.js">'
      + '<script type="importmap">{}</script>'
      + '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  assert.throws(
    () => verifySiteArtifact(site),
    /index\.html is missing <base href="\/">/,
  );
  writeFileSync(
    join(site, "index.html"),
    '<link rel="preload" href="/_framework/dotnet.js">'
      + '<script type="importmap">{}</script>'
      + '<base href="/">'
      + '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  assert.throws(
    () => verifySiteArtifact(site),
    /index\.html places <base href="\/"> after the runtime preload/,
  );
  writeFileSync(
    join(site, "index.html"),
    '<script type="importmap">{}</script>'
      + '<base href="/">'
      + '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  assert.throws(
    () => verifySiteArtifact(site),
    /index\.html places <base href="\/"> after the import map/,
  );
  writeFileSync(
    join(site, "index.html"),
    '<base href="/">'
      + '<link rel="preload" href="/_framework/dotnet.js">'
      + '<script type="importmap">{}</script>'
      + '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  delete manifest["src/dotnet-inspect.ts"];
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  assert.throws(
    () => verifySiteArtifact(site),
    /entry 'index\.html' imports missing entry 'src\/dotnet-inspect\.ts'/,
  );

  manifest["src/dotnet-inspect.ts"] = {
    file: "assets/app.js",
    isDynamicEntry: true,
  };
  indexEntry.file = "assets/../index.html";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest contains invalid asset 'assets\/\.\.\/index\.html'/,
  );

  indexEntry.file = "assets/index.js";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  unlinkSync(join(site, "assets/index.js"));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest references missing asset 'assets\/index\.js'/,
  );
});
