import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  basename,
  dirname,
  isAbsolute,
  join,
  resolve,
} from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import {
  supportedAnalysisHosts,
  verifyAnalysisHost,
} from "../scripts/verify-analysis-host.ts";
import {
  isGeneratedDirectory,
  javaScriptSourceExtensions,
  projectRelative,
  projectSourceFiles,
  typeScriptSourceExtensions,
} from "./project-source-inventory.ts";
import { verifySiteArtifact } from "../scripts/verify-site-artifact.ts";
import { auditedBuild, builtinPluginNames, bundlerReadFiles, shippedChunks } from "./vite-audit.ts";

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

interface OxlintOverride {
  readonly files: readonly string[];
  readonly rules?: Readonly<Record<string, unknown>>;
}

interface OxlintOptions {
  readonly denyWarnings?: boolean;
  readonly reportUnusedDisableDirectives?: string;
  readonly typeAware?: boolean;
}

interface OxlintConfig {
  readonly ignorePatterns?: readonly string[];
  readonly options?: OxlintOptions;
  readonly overrides?: readonly OxlintOverride[];
  readonly rules?: Readonly<Record<string, unknown>>;
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
const nodeTsconfig = readJson<TsconfigFile>("../tsconfig.node.json");
const staticWebAppConfig
  = readJson<StaticWebAppConfig>("../staticwebapp.config.json");

// The lint targets are read here rather than inside the gate that checks coverage,
// because the pruning rules below also need to know which directories hold authored
// source. Both answers come from the one `lint` script, so neither can drift from it.
const lintTokens = (() => {
  const lint = packageJson.scripts?.lint ?? "";
  const oxlintCall = lint.slice(lint.indexOf("oxlint "));
  return oxlintCall.split(/\s+/u).slice(1);
})();
const lintTargets = lintTokens.filter(token => !token.startsWith("-"));
const lintTargetDirectories = lintTargets.filter(target => {
  const full = fileURLToPath(new URL(`../${target}`, import.meta.url));
  return existsSync(full) && statSync(full).isDirectory();
});
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
  // The toolchain scripts and the Vite config are Node programs rather than browser
  // source, so they get Node globals from their own project instead of widening the
  // browser one. Without this project the Vite config would be checked by nothing: no
  // test imports it, so it would not be pulled into the test program either.
  assert.equal(nodeTsconfig.extends, "./tsconfig.json");
  assert.deepEqual(nodeTsconfig.compilerOptions.types, ["node"]);
  assert.deepEqual(nodeTsconfig.include, ["scripts/**/*.ts", "vite.config.ts"]);
  // Adversarial review (GPT-5.6 Sol and Gemini 3.1 Pro, independently) found that
  // inheriting the browser `lib` let `document.title` compile in a Node script, which
  // then failed only when the build or lint gate actually ran it.
  assert.deepEqual(nodeTsconfig.compilerOptions.lib, ["ES2022"]);
  assert.equal(
    packageJson.scripts.typecheck,
    "tsc --noEmit && tsc --noEmit -p test/tsconfig.json"
      + " && tsc --noEmit -p tsconfig.node.json",
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
  ["node", "tsconfig.node.json"],
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
// The scan prunes dependency and build output rather than listing the directories to
// cover, so a newly created authored directory is included by default. Adversarial review
// (GPT-5.6 Sol) defeated the previous list-based version by putting an unchecked
// JavaScript file in `public/`, which Vite copies verbatim into `dist/`: it shipped while
// `npm test`, `npm run analyze`, and `npm run build` all stayed green.
//
// Pruning by bare directory name was the next round's finding, from both reviewers: `bin`
// and `obj` are MSBuild output next to a project file, and `dist` is Vite output at the
// project root, but the name alone means nothing anywhere else. `public/bin/probe.js`
// (Sol) and `public/dist/bypass.js` (Gemini 3.1 Pro) each shipped through that hole. A
// directory is therefore pruned for what produced it, not for what it is called.
//
// Round 3 showed that reasoning is still not enough on its own, because it asks only what
// produced a directory and never where the directory sits. Sol put a file in
// `public/node_modules`, and Gemini dropped an empty `src/fake.csproj` next to a `src/bin`
// -- both spoofing a role test that was perfectly correct in isolation. Neither location
// is a place build output can legitimately be: `public` is copied into the site verbatim,
// and the lint targets are where the project's authored source lives. Inside those roots
// nothing is pruned at all, whatever it calls itself and whatever sits beside it, so
// there is no role left to spoof.
const unprunedRoots = ["public", ...lintTargetDirectories];

function isGenerated(directory: string, name: string, root: string): boolean {
  return isGeneratedDirectory(directory, name, root, unprunedRoots);
}

// Converting this file from JavaScript put it inside its own scan. The suppression
// directives below are therefore assembled from parts rather than spelled out: a literal
// would make that gate report itself, and excluding this file would leave a hole in the
// one test that closes the per-file vector. Assembling keeps this file in scope and the
// scan exactly as strict, which is why the prose names the directives only in the
// abstract.
//
// Extensions are compared case-insensitively. Round 2 (Sol) shipped `public/probe.JS`
// through every gate on a case-insensitive filesystem, where the bundler and the browser
// treat it as JavaScript and only this comparison did not.
function projectFiles(extensions: readonly string[]): string[] {
  const root = fileURLToPath(new URL("../", import.meta.url));
  return projectSourceFiles(root, extensions, unprunedRoots);
}

// A symbolic link is neither a file nor a directory to `readdirSync`, so the walk above
// used to step over one entirely. Round 2 (Sol) shipped a symlinked `public/probe.js`
// that way: Vite dereferences it and copies the content, so unchecked JavaScript reached
// `dist/` while every gate stayed green.
//
// Including links in the walk covers the extensions the gates know about, but a link can
// also point outside the tree, at a directory, or at a name with any extension at all, so
// none of the reasoning the other gates do about paths holds for one. The authored tree
// contains no symbolic links, and this keeps it that way rather than trying to decide
// which ones would have been safe.
function symbolicLinks(): string[] {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const links: string[] = [];
  const walk = (directory: string): void => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const full = join(directory, entry.name);
      if (entry.isSymbolicLink()) {
        links.push(full);
      } else if (entry.isDirectory() && !isGenerated(directory, entry.name, root)) {
        walk(full);
      }
    }
  };
  walk(root);
  return links;
}

test("no source directory reaches content through a symbolic link", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));

  assert.deepEqual(
    symbolicLinks().map(link => projectRelative(root, link)).sort(),
    [],
    "a symbolic link is followed by the bundler but not by the checks above, so its "
      + "target ships without having been compiled or linted");
});

// Every extension the TypeScript compiler and oxlint both recognise as source. Listing
// them in one place is what lets the two coverage gates below ask "is every source file
// checked?" instead of "is every file I remembered to think of checked?" -- `.mts` was
// Sol's finding: `scripts/probe.mts` with a type error in it passed every gate, because
// `tsconfig.node.json` included `scripts/**/*.ts` and nothing considered `.mts` at all.
const typeScriptExtensions = typeScriptSourceExtensions;
const javaScriptExtensions = javaScriptSourceExtensions;


test("no source file suppresses type checking", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  // Round 3 (Sol) hid a suppression directive in `scripts/probe.mts`: the scan asked only
  // for `.ts` while the compiler happily reads all four TypeScript extensions, so a file
  // the program did type-check could turn that checking off and say nothing.
  const files = projectFiles(typeScriptExtensions);

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
    suppressed.map(file => projectRelative(root, file)).sort(),
    [],
    "these files opt out of type checking; use a narrowing guard or @ts-expect-error");
});

// The third way out of type checking is to not write TypeScript at all. The oxlint config
// used to turn the `no-unsafe-*` family off for `**/*.js`, because the toolchain scripts
// and the Vite config were JavaScript; a new JavaScript file anywhere inherited that
// exemption for free. Those files are TypeScript now, and the one remaining exemption is
// the generated engine wrapper, which is Wasm build output rather than authored source.
//
// Rather than restate that file name twice, the two sets are asserted against each other:
// the JavaScript actually present must be exactly the JavaScript actually exempted. A new
// authored file fails, a widened exemption fails, and an exemption left behind by a
// deleted file fails too.
test("the only JavaScript is the file the lint exemption names", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const present = projectFiles(javaScriptExtensions)
    .map(file => projectRelative(root, file))
    .sort();
  const exempted = (oxlintConfig.overrides ?? [])
    .filter(override => override.rules !== undefined)
    .flatMap(override => override.files)
    .sort();

  assert.deepEqual(present, ["engine/wwwroot/inspect-web-engine.js"],
    "authored JavaScript here would be checked by neither the compiler nor the "
      + "type-aware lint rules the rest of the project is held to");
  assert.deepEqual(exempted, present,
    "the lint exemption and the JavaScript it covers must name the same files");
});

// The gate above asks whether a file is TypeScript. It does not ask whether anything
// compiles it, and those are different questions: `scripts/probe.mts` and a root-level
// `bypass.ts` are both unimpeachably TypeScript, and both sailed through every gate in
// round 1 because no `tsconfig` include glob happened to match them.
//
// Restating the globs here would reproduce the bug, so this asks the compiler instead.
// `--listFilesOnly` reports the files the program actually consists of, which is the
// real answer to "what is checked?" and a stronger one than the resolved `include` list:
// it includes files reached only by import. Round 2 (Gemini 3.1 Pro) needed exactly that
// distinction, hiding `engine.Tests/bin/hidden/bypass.ts` in a pruned build-output
// directory and importing it from `src/`, where the bundler traced the import and shipped
// code the lint had never seen.
const compilerProjects = ["tsconfig.json", "test/tsconfig.json", "tsconfig.node.json"];

function programFiles(): Set<string> {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const files = new Set<string>();
  for (const project of compilerProjects) {
    const listed = spawnSync(
      "npx",
      ["tsc", "--noEmit", "--listFilesOnly", "-p", project],
      { cwd: root, encoding: "utf8" });
    assert.equal(listed.status, 0,
      `tsc --listFilesOnly -p ${project} failed: ${listed.stderr}`);
    const lines = listed.stdout.split("\n")
      .map(line => line.trim())
      .filter(line => line.length > 0);
    assert.ok(lines.length > 0, `${project} resolved to no files at all`);
    for (const line of lines) {
      files.add(resolve(line));
    }
  }
  return files;
}

// The compiler reports its own dependencies and the sibling prototypes it imports from,
// neither of which this project's gates govern. Only paths inside this project, and
// outside its dependency directory, are this project's to answer for.
function isInsideProject(file: string, root: string): boolean {
  const path = projectRelative(root, file);
  return !path.startsWith("..") && !isAbsolute(path);
}

function isProjectOwned(file: string, root: string): boolean {
  return isInsideProject(file, root)
    && !projectRelative(root, file).split("/").includes("node_modules");
}

test("every TypeScript file belongs to a compiler project", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const checked = programFiles();

  const authored = projectFiles(typeScriptExtensions);
  assert.ok(authored.length > 50,
    `expected the TypeScript sources, found ${authored.length}; a walk that finds `
      + "nothing would satisfy the emptiness assertion below without checking anything");

  // A count threshold only notices catastrophe, as Gemini 3.1 Pro pointed out in round 2:
  // a mutation that hides one file keeps the total comfortably above it. So the walk is
  // checked against an independent account of the same tree rather than against a number.
  // Every file the compiler reports, other than the ones sitting in a directory the walk
  // deliberately prunes, is a file the walk should have found; anything else means the
  // walk lost sources, and every gate built on it is quietly reporting less than it says.
  const prunedAway = (file: string): boolean => {
    for (let directory = dirname(file);
      directory.startsWith(root);
      directory = dirname(directory)) {
      if (isGenerated(dirname(directory), basename(directory), root)) {
        return true;
      }
    }
    return false;
  };
  const walked = new Set(authored.map(file => resolve(file)));
  const missedByWalk = [...checked]
    .filter(file => isProjectOwned(file, root)
      && typeScriptExtensions.some(extension => file.toLowerCase().endsWith(extension))
      && !walked.has(file)
      && !prunedAway(file))
    .map(file => projectRelative(root, file))
    .sort();
  assert.deepEqual(missedByWalk, [],
    "the compiler reads these files but the directory walk did not find them, so the "
      + "gates built on that walk are weaker than they report");

  const unchecked = authored
    .filter(file => !checked.has(resolve(file)))
    .map(file => projectRelative(root, file))
    .sort();

  assert.deepEqual(unchecked, [],
    "these TypeScript files are in no compiler project, so `npm run typecheck` reads "
      + "neither their types nor their errors; add them to a tsconfig `include`");
});

// And the same question for the lint, which is invoked on an explicit list of paths
// rather than on the project. A file outside every one of those paths is linted by
// nothing, which is how a root-level script escaped the `no-unsafe-*` rules entirely.
test("every source file is covered by a lint target", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));

  // Round 2 (Sol) turned this parse against itself with `--ignore-pattern public`: the
  // operand does not start with `-`, so it was read as a target, and `public/` counted as
  // covered while oxlint was being told to skip it. Guessing which flags take a separate
  // operand would put the enumeration this file just removed straight back in, so the
  // parse fails closed instead. Only flags known to take no operand are allowed, and
  // anything else stops the gate rather than being silently classified.
  const operandlessFlags = new Set(["--no-ignore", "--disable-nested-config"]);
  const flags = lintTokens.filter(token => token.startsWith("-"));
  const unknown = flags.filter(flag => !operandlessFlags.has(flag));
  assert.deepEqual(unknown, [],
    "this gate reads the remaining arguments as paths, which is only sound while every "
      + "flag is known to take no separate operand; add the flag to the allowed set once "
      + "its arity is accounted for");

  const targets = lintTargets;
  assert.ok(targets.length > 0, "the lint script names no files to lint");

  const sources = projectFiles([...typeScriptExtensions, ...javaScriptExtensions]);
  assert.ok(sources.length > 50,
    `expected the project sources, found ${sources.length}`);

  // The walk answers "what is authored here", which is not the same question as "what
  // gets compiled and shipped". A pruned build-output directory is legitimately not
  // walked, but a file inside one that `src/` imports is compiled and bundled all the
  // same, and Gemini 3.1 Pro shipped an unlinted `any` that way in round 2. Anything the
  // compiler pulls into the program is therefore held to the same lint coverage, whether
  // the walk reaches it or not.
  const compiled = [...programFiles()].filter(file => isProjectOwned(file, root));
  const covered = (file: string): boolean => {
    const path = projectRelative(root, file);
    return targets.some(target => path === target || path.startsWith(`${target}/`));
  };
  const unlinted = [...new Set([...sources.map(file => resolve(file)), ...compiled])]
    .filter(file => !covered(file))
    .map(file => projectRelative(root, file))
    .sort();

  assert.deepEqual(unlinted, [],
    "these files are outside every path the lint script passes to oxlint; add the path "
      + "to the `lint` script rather than leaving the file unlinted");
});

// Every gate above derives what this project owns from two oracles: the directory walk
// and the compiler. Round 7 (Gemini 3.1 Pro) found the third one missing. Vite does not
// take its entry points from `tsconfig`; it takes them from `index.html`, and the
// compiler never parses `index.html`. A `<script type="module" src="...">` pointing into
// a pruned build-output directory is therefore bundled and executed while sitting outside
// the walk, outside the program, and outside every gate built on either -- an unsafe
// `any` shipped in `dist/` with all four commands green.
//
// Round 7 asked the bundler through the `sources` of the source maps it emits. Round 8
// (Sol) showed that this reads the module graph and mistakes it for the build. A file
// referenced by `new URL("...", import.meta.url)` travels Vite's *asset* pipeline: over
// the inline limit it is emitted as a separate asset, and under it the bytes are base64'd
// into a chunk with no asset file, no manifest entry and no map entry at all. Either way
// it ships and runs, and either way no source map ever names it.
//
// So the question moved from what the bundler emitted to what it read. `getWatchFiles`
// is Rollup's own record of that, and it does not care how a file was referenced or what
// it is called -- modules, entry HTML, assets emitted or inlined, plugin reads. Both
// reviewers across both rounds proposed widening an enumeration instead: more extensions,
// a ban on script shapes, an audit of asset `originalFileNames`. That is the move that
// failed in rounds 3, 4 and 5, and the asset pipeline is exactly what it would have
// missed again.
test("the lint covers every file the bundler reads", async () => {
  const root = fileURLToPath(new URL("../", import.meta.url));

  // Round 8 (Gemini 3.1 Pro) attacked the *filter* rather than the oracle. `node_modules`
  // is third-party by convention, not by nature, and `isProjectOwned` says so by name:
  // an authored payload written into a directory of that name and referenced from
  // `index.html` was bundled and executed while this gate discarded it as somebody
  // else's code. Reading the name is the enumeration this file keeps removing, so
  // ownership is derived instead -- from the lockfile, which is npm's own account of
  // what is allowed to be in there. A path under `node_modules` is third-party when some
  // package directory above it is one the lock declares, and is this project's problem
  // otherwise. The gate that pins the lock's contents runs above, so this cannot be
  // satisfied by inventing a lock entry either.
  //
  // Round 10 (Gemini 3.1 Pro) found the cost of pairing that with round 9's removal of
  // the out-of-root filter. npm hoists: with a workspace or a parent install, a real
  // dependency lands in a `node_modules` above this project, and asking *this* lockfile
  // about a `../node_modules/is-odd` key can only ever miss. The package was reported as
  // unchecked project source, which is the safe direction to fail but the wrong answer,
  // and a gate that misfires on an ordinary npm layout is a gate somebody eventually
  // deletes. So the lookup finds the lockfile that governs the file -- the nearest one at
  // or above it -- and asks that, with keys relative to that lockfile's own directory.
  // For everything in this project that is the same lockfile and the same answer as
  // before. Note that only this project's lock is pinned by the gate above; a lock
  // outside it is trusted as npm's account of its own tree, which holds here because no
  // such tree is committable in this repository.
  // Round 11 (Gemini 3.1 Pro) forged one. The search started at the file and walked up,
  // so a `package-lock.json` planted inside `node_modules` was found *before* this
  // project's own, and a payload at
  // `node_modules/my-evil-package/node_modules/inner-evil/evil.ts` was excused by a lock
  // the attacker wrote. Walking up from the file was never what hoisting needed: npm
  // hoists *upward*, so the lock that governs an installed tree is always at or above the
  // project, never inside it. The search runs over this project's directory and its
  // ancestors only, and takes the first that both declares a lock and contains the file.
  // A lockfile below the project is not consulted at all, so planting one changes nothing.
  const lockCache = new Map<string, PackageLock | undefined>();
  const lockAt = (directory: string): PackageLock | undefined => {
    if (!lockCache.has(directory)) {
      const candidate = join(directory, "package-lock.json");
      lockCache.set(directory, existsSync(candidate)
        ? readJson<PackageLock>(pathToFileURL(candidate).href)
        : undefined);
    }
    return lockCache.get(directory);
  };

  const governingLock = (file: string): { root: string; lock: PackageLock } | undefined => {
    for (let current = root; ; current = dirname(current)) {
      const lock = lockAt(current);
      if (lock !== undefined && !projectRelative(current, file).startsWith("../")) {
        return { root: current, lock };
      }
      const parent = dirname(current);
      if (parent === current) {
        return undefined;
      }
    }
  };

  const declaredDependency = (file: string): boolean => {
    if (!projectRelative(root, file).split("/").includes("node_modules")) {
      return false;
    }
    const governing = governingLock(file);
    if (governing === undefined) {
      return false;
    }
    for (let directory = dirname(projectRelative(governing.root, file));
      directory !== "." && directory !== "";
      directory = dirname(directory)) {
      if (Object.hasOwn(governing.lock.packages, directory)) {
        return true;
      }
    }
    return false;
  };

  const read = (await bundlerReadFiles(root))
    .map(file => resolve(file))
    .filter(file => !declaredDependency(file));
  assert.ok(read.length > 20,
    `expected the files the build reads, found ${read.length}; a build that reads almost `
      + "nothing would satisfy the assertion below without covering anything");

  // Five files this build reads are not checked source, and each is already accounted
  // for elsewhere, so they are pinned as an exact list rather than filtered by a rule
  // that would also let a payload through.
  //
  // Round 9 (Gemini 3.1 Pro) removed the last such rule. This gate used to discard
  // everything outside the project directory, which reads as "not ours" but behaves as a
  // blanket suppression: a payload committed one level up at `prototypes/payload.js` and
  // imported through `new URL("../../payload.js", import.meta.url)` was base64'd into the
  // bundle and executed with all four commands green. Sitting outside the directory says
  // nothing about whether the build ships it, so the filter is gone and the two files it
  // was really there for are named instead.
  //
  // `../annotated-source-viewer/*` is the sibling prototype this project imports from.
  // Its `document-model.js` is in the TypeScript program but covered by no lint target,
  // which is issue #4780, and its `package.json` is read for resolution. Both predate
  // this branch.
  //
  // `index.html` is the entry document. The JavaScript written inside it is bundled and
  // executed while no lint target and no compiler project can name it, which is issue
  // #4783; that gap predates this branch and the file is untouched here.
  //
  // `package.json` is read for dependency resolution rather than compiled, and the gates
  // above already assert its contents field by field.
  //
  // `src/styles.css` is style content. It sits under a lint target, but oxlint reads
  // script and the compiler has no account of a stylesheet at all, so it cannot clear
  // either half of the test below. A browser will not execute it either.
  //
  // Pinning the exact list is what makes this fail closed. Anything else the build reads
  // changes it and fails -- including a second stylesheet, which is a small cost for a
  // gate that otherwise has to guess which extensions are harmless. Closing #4780 or
  // #4783 changes it too, so the pin has to be deleted deliberately rather than quietly
  // outliving the gaps it describes.
  const knownReadButUngated = [
    "../annotated-source-viewer/package.json",
    "../annotated-source-viewer/src/document-model.js",
    "index.html",
    "package.json",
    "src/styles.css",
  ];

  const targets = lintTargets;
  const covered = (file: string): boolean => {
    const path = projectRelative(root, file);
    return targets.some(target => path === target || path.startsWith(`${target}/`));
  };
  // Lint coverage and type checking are separate defeats and the bundler vectors evade
  // both, so a file has to clear both accounts to be considered gated.
  const checked = programFiles();
  const ungated = read
    .filter(file => !covered(file) || !checked.has(file))
    .map(file => projectRelative(root, file))
    .sort();

  assert.deepEqual(ungated, knownReadButUngated,
    "the build reads these files, but they are outside the lint targets or outside "
      + "every compiler project, so they reach the shipped output without being "
      + "checked; they are reachable from `index.html`, an import, or an asset "
      + "reference, and no gate above can see them");
});

// `publicDir` is the bundler's verbatim-copy path: anything in it lands in `dist/`
// unread by Vite, unparsed by the compiler and unlinted, whatever its extension. Round 7
// (Sol) shipped a payload that way. The gate that audits what the build *reads* cannot
// see this one, because a verbatim copy is never read. This project has no such directory
// and does not need one -- `manifest.json` is Vite's own build manifest and `assets/` is
// imported through the bundler -- so the path is switched off rather than policed.
//
// Round 9 (Sol) found the second path of the same kind: a plugin can `readFileSync` a file
// and splice it into a module in `transform`, registering nothing with Rollup, so
// `getWatchFiles` never sees it. Round 9 pinned both by matching the text of
// `vite.config.ts`. Round 10 broke that from both seats -- a plugin spread in from an
// imported helper, and a plugin assigned as `conf.plugins` -- and the pins moved to
// Vite's own resolution.
//
// Round 11 broke that too, three ways, and they share one mistake. Resolving the config
// *here* asks what the config looks like in this process at this moment, which is not the
// same question as what the build used. Sol guarded a plugin on
// `npm_lifecycle_event === "build"` and Gemini guarded one on `process.argv` containing
// `vite`; both are absent under `node --test` and present under `npm run build`. Worse,
// my own check for those found that a separate `resolveConfig` call here defaults to
// *development* mode while the build runs in production, so a plugin guarded on
// `mode === "production"` was invisible to the pin and active in every real build.
//
// So nothing is resolved separately any more. The audited build reports its own resolved
// config through `configResolved`, which is by construction the config that build used --
// its mode, its `publicDir` and its plugin list. The baseline it is compared against is
// resolved with `configFile: false` at that same observed mode, so this suite still never
// has to know what Vite installs on its own. Paired with the gate below, which proves the
// audited build is the shipped build, that closes the gap the three round 11 findings all
// went through: a conditional plugin either changes the audited build's plugin list, or
// makes the audited and shipped builds disagree.
test("the bundler has no unread path into the shipped output", async () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const audited = await auditedBuild(root);
  assert.equal(audited.publicDir, "",
    "the resolved config enables `publicDir`; while it is enabled, any file placed "
      + "there is copied into `dist/` without the compiler or the lint ever reading it");
  assert.ok(!existsSync(join(root, "public")),
    "a `public/` directory exists; with `publicDir` disabled it ships nothing, so "
      + "remove it rather than leaving a directory that reads as shipped content");
  assert.deepStrictEqual(audited.pluginNames, await builtinPluginNames(root, audited.mode),
    "the build resolved plugins Vite did not install itself; a plugin can read a file "
      + "and splice it into a module without Rollup ever watching it, which is invisible "
      + "to the gate that audits what the build reads. Adding one means answering for "
      + "what it injects, so this gate has to be reckoned with rather than edited away");
  assert.deepStrictEqual(audited.unaccountedRollupPlugins, [],
    "Rollup is running plugins Vite never resolved, so they are invisible to the plugin "
      + "list above. `build.rollupOptions.plugins` is handed straight to Rollup and does "
      + "this, and because such a plugin transforms both builds alike the gate below sees "
      + "nothing to disagree about either");
  assert.deepStrictEqual(audited.rollupOutputPlugins, [],
    "the build declares Rollup output plugins. Those run at generate time and can rewrite "
      + "a chunk after every gate above has read it, so this project keeps none");
  assert.equal(audited.workerPluginCount, 0,
    "the build declares worker plugins. This project bundles no workers, and a worker "
      + "plugin injects into a bundle these gates do not audit, so the two have to stay "
      + "that way together");
});

// Round 10 derived the two settings above from Vite's resolution instead of the text of
// the config, which closed every spelling of *declaring* a plugin. Round 11 (Sol) showed
// that resolution is still a question asked at a particular moment, in a particular
// process. A plugin guarded by `process.env.npm_lifecycle_event === "build"` is simply
// not there when `npm test` resolves the config, and is there when `npm run build` runs:
// the gate above saw Vite's own plugin list unchanged, all four commands stayed green,
// and the payload shipped in `dist/assets/dotnet-inspect-B2MTdysw.js`.
//
// That is not only an evasion. Config conditional on mode or environment is ordinary Vite
// practice, so the plugin gate was equally blind to an honest conditional plugin. Every
// gate in this file audits a build that the *test* runs, and each one inherits this: they
// describe the build the test could see, not the build that ships.
//
// So this asserts the two are the same build. `npm run build` runs in its own process
// with whatever environment npm gives it, and its chunks must match the audited build's
// exactly. It models nothing about what a config may do -- a config that behaves
// differently when it is being watched makes the two disagree, whatever it switched on.
// The gates above keep their value because this one says they were looking at the
// artifact that ships.
test("the audited build is the build that ships", async () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const audited = await auditedBuild(root);
  assert.ok(audited.chunks.length > 0, "the audited build emitted no chunks to compare");
  assert.deepStrictEqual(shippedChunks(root), audited.chunks,
    "`npm run build` emits different code than the build these gates audit, so the "
      + "audit describes something other than what ships. A config that resolves "
      + "differently under the build than under the test -- conditional on mode, on the "
      + "npm lifecycle, or on any other environment -- does this, and so does anything "
      + "that injects into one build and not the other");
});

// Being under a lint target turns out not to mean the lint reads the file. oxlint applies
// the repository's `.gitignore` while it walks a directory, and `--no-ignore` does not
// change that -- it governs `.eslintignore` and `--ignore-pattern`. Round 3 (Gemini 3.1
// Pro) put an unsafe `any` in `src/test-results/bypass.ts`, which the coverage gate above
// counted as covered because the path does start with `src/`, and which oxlint then
// skipped in silence while Vite bundled it.
//
// So coverage is asked of the ignore rules directly, with git as the oracle for its own
// file. Anything the project compiles or ships that git would ignore is a file the lint
// cannot see, whatever its path looks like.
// `--no-index` is what makes git's answer match oxlint's. Without it `check-ignore`
// reports a tracked file as not ignored, because from git's point of view a file it is
// already tracking is not being ignored -- but oxlint applies the ignore *patterns* and
// never consults the index, so it skips that file anyway. Round 4 (Sol) force-added a
// file under an ignored path and the gate went green while the lint stayed blind.
function ignoredFiles(directory: string, candidates: readonly string[]): string[] {
  // `core.excludesFile` is the developer's own file, not the repository's, and oxlint does
  // not read it. Round 5 (Sol) pointed out that leaving it in scope lets a global `*.ts`
  // entry fail this gate over files the lint reads perfectly well, so git is asked about
  // the repository's ignore rules alone. `.git/info/exclude` stays in scope deliberately:
  // oxlint honours that one, so git and oxlint still agree about it.
  //
  // `check-ignore` exits 0 when it ignored something, 1 when it ignored nothing, and
  // anything else is a real failure that must not read as "nothing ignored".
  const checked = spawnSync("git",
    ["-c", "core.excludesFile=", "check-ignore", "--no-index", "--stdin"],
    { cwd: directory, encoding: "utf8", input: candidates.join("\n") });
  assert.ok(checked.status === 0 || checked.status === 1,
    `git check-ignore failed: ${checked.stderr}`);
  return checked.stdout.split("\n")
    .map(line => line.trim())
    .filter(line => line.length > 0);
}

test("no file the build compiles is hidden from the lint by an ignore rule", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const sources = projectFiles([...typeScriptExtensions, ...javaScriptExtensions]);
  const compiled = [...programFiles()].filter(file => isProjectOwned(file, root));
  const candidates = [...new Set([...sources.map(file => resolve(file)), ...compiled])];
  assert.ok(candidates.length > 50,
    `expected the project sources, found ${candidates.length}`);

  const ignored = ignoredFiles(root, candidates)
    .map(file => projectRelative(root, file))
    .sort();

  assert.deepEqual(ignored, [],
    "oxlint applies .gitignore while walking, so these files are compiled or shipped but "
      + "never linted; move them out of the ignored path rather than relying on being "
      + "under a lint target");
});

// The gate above cannot demonstrate its own `--no-index` on this repository, because a
// file that would prove it is exactly a file the gate refuses to allow. So the reason for
// the flag is pinned on a scratch repository, exercising the same helper the gate uses:
// without `--no-index` a force-added file reads as "not ignored" and the gate goes green
// over a file oxlint will never open. Dropping the flag as redundant tidying fails here
// rather than silently reopening round 4.
test("the ignore-rule gate reads ignore patterns rather than tracked status", () => {
  const scratch = mkdtempSync(join(tmpdir(), "inspect-web-ignore-"));
  try {
    const git = (...args: string[]): void => {
      const run = spawnSync("git", args, { cwd: scratch, encoding: "utf8" });
      assert.equal(run.status, 0, `git ${args.join(" ")} failed: ${run.stderr}`);
    };
    git("init", "-q");
    git("config", "user.email", "gate@example.invalid");
    git("config", "user.name", "gate");
    writeFileSync(join(scratch, ".gitignore"), "ignored/\n");
    mkdirSync(join(scratch, "ignored"));
    const hidden = join(scratch, "ignored", "hidden.ts");
    writeFileSync(hidden, "export const value = 1;\n");
    git("add", "-f", "ignored/hidden.ts");

    assert.deepEqual(ignoredFiles(scratch, [hidden]), [hidden],
      "a force-added file under an ignored path must still report as ignored, because "
        + "oxlint skips it on the pattern alone and never consults the index");
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

// Rounds 3, 4 and 5 each found a different reason oxlint declines to open a file that
// every gate above counts as covered: `.gitignore` applied while walking, a force-added
// file under an ignored path, and -- round 5 (Gemini 3.1 Pro) -- a filename heuristic
// that refuses any path containing `.min.` or `-min.` as a minified asset, for every
// extension oxlint otherwise reads. The pattern is the failure rather than the three
// holes: the gates above predict which files oxlint will skip, and oxlint's skip rules
// are its own and undocumented, so the prediction is always one rule out of date.
//
// This gate stops predicting. oxlint's JSON report states how many files it actually
// read, so running the lint script's own arguments and comparing that count against the
// files this project owns asks oxlint what it did instead of modelling what it will do.
// A file skipped for any reason -- including a heuristic added by a future oxlint
// release, which no amount of reading today's source could anticipate -- makes the two
// counts disagree and fails here.
function oxlintFileCount(directory: string, args: readonly string[]): number {
  const run = spawnSync("npx", ["oxlint", ...args, "--format=json"],
    { cwd: directory, encoding: "utf8" });
  const output = run.stdout.trim();

  // When every path it was given is one it refuses to open, oxlint answers in plain text
  // rather than JSON -- which is exactly the case this gate exists to detect, so parsing
  // it as JSON would kill the per-file diagnostic below on the one input it most needs to
  // report. Only this specific answer counts as zero; anything else unparseable is a real
  // failure and must not read as "oxlint skipped everything".
  if (output.startsWith("No files found to lint")) {
    return 0;
  }
  assert.ok(output.startsWith("{"),
    `oxlint produced no usable report: ${run.stderr || output || "no output"}`);

  const report: unknown = JSON.parse(output);
  assert.ok(typeof report === "object" && report !== null && "number_of_files" in report,
    "oxlint's JSON report no longer states how many files it read, which is the fact "
      + "this gate depends on; re-establish the count before relaxing this assertion");
  const counted = report.number_of_files;
  assert.ok(typeof counted === "number",
    `oxlint reported a non-numeric file count: ${String(counted)}`);
  return counted;
}

test("the lint reads every file this project owns", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));

  // The count equality below is only sound while no file can be read twice. oxlint does
  // not deduplicate its inputs -- naming a file both directly and through its directory
  // makes it count twice -- so an overlapping target list can offset a skipped file
  // exactly and hide it. That is not hypothetical: adding one already-covered file as an
  // extra target masked a `.min.` file here completely, and only the lint-script pin
  // further down caught it. A pin over the script text is the wrong thing to rely on,
  // because it is exactly what a developer updates when changing the lint legitimately.
  // So the gate that depends on disjoint targets asserts it itself.
  assert.deepEqual([...new Set(lintTargets)], [...lintTargets],
    "the lint script names a target more than once, which makes oxlint read every file "
      + "beneath it twice and lets the count below hide a skipped file");
  const overlapping = lintTargets
    .filter(target => lintTargets.some(other => target.startsWith(`${other}/`)))
    .sort();
  assert.deepEqual(overlapping, [],
    "these lint targets sit inside another lint target, so oxlint reads them twice; "
      + "remove the redundant target rather than leaving the count able to cancel out");

  const sources = projectFiles([...typeScriptExtensions, ...javaScriptExtensions]);
  const compiled = [...programFiles()].filter(file => isProjectOwned(file, root));
  const owned = [...new Set([...sources.map(file => resolve(file)), ...compiled])];
  assert.ok(owned.length > 50, `expected the project sources, found ${owned.length}`);

  const read = oxlintFileCount(root, lintTokens);

  // The traversal count above compares two differently-derived sets, and round 6 (Sol)
  // cancelled them out: oxlint reads extensions this walk does not collect, so an
  // unimported `count-offset.vue` added one to oxlint's side while an unsafe
  // `bypass.min.ts` -- refused by name -- removed one from it. 85 met 85 and unchecked
  // code reached the bundle.
  //
  // Widening the walk to whatever oxlint reads today would be the same losing game as
  // predicting its skip rules. Instead oxlint is handed exactly the files this project
  // owns, so nothing it finds on its own can pad the total and any file it refuses shows
  // up as a shortfall. Offsetting now needs two failures that cancel within one fixed set,
  // rather than one file of an extension nobody thought to walk.
  const named = oxlintFileCount(root, [...lintTokens.filter(token => token.startsWith("-")),
    ...owned.map(file => projectRelative(root, file))]);
  assert.equal(named, owned.length,
    `oxlint read ${named} of the ${owned.length} files it was handed by name, so it `
      + "refuses some of them outright; the shortfall is unlinted source");

  if (read === owned.length) {
    return;
  }

  // Naming a file explicitly defeats some skips but not others -- an ignored path linted
  // when named directly is exactly round 3 -- so this identifies what it can and says so
  // plainly rather than implying the list is complete.
  const flags = lintTokens.filter(token => token.startsWith("-"));
  const refused = owned
    .filter(file => oxlintFileCount(root, [...flags, file]) === 0)
    .map(file => projectRelative(root, file))
    .sort();

  assert.fail(
    `the lint reads ${read} files but this project owns ${owned.length}; oxlint is `
      + "skipping source that every coverage gate counts as linted"
      + (refused.length > 0
        ? `\noxlint refuses these outright:\n  ${refused.join("\n  ")}`
        : "\nno single file is refused on its own, so the skip happens while walking; "
          + "the ignore-rule gate above names that case")
      + "\nrename or relocate the file so oxlint will read it, rather than relying on it "
      + "being under a lint target");
});

// The rules this conversion exists to enforce. Hoisted so the gate that pins them in the
// config and the gate that rejects inline suppressions of them cannot drift apart.
const protectedRules = [
  "typescript/no-unsafe-argument",
  "typescript/no-unsafe-assignment",
  "typescript/no-unsafe-call",
  "typescript/no-unsafe-member-access",
  "typescript/no-unsafe-return",
] as const;

// Sol also walked past the assertion above entirely by turning a rule off at the top
// level, where no override is involved and the exemption sets still matched. The rules
// the conversion exists to apply are therefore pinned where they are declared.
test("the unsafe-operation rules stay enabled for authored source", () => {
  for (const rule of protectedRules) {
    assert.equal(oxlintConfig.rules?.[rule], "error", `${rule} must stay enabled`);
  }
  // The other way to reach every file at once. `ignorePatterns` drops files from the lint
  // run entirely, which would leave the rules above enabled and enforced against nothing.
  assert.deepEqual(oxlintConfig.ignorePatterns ?? [], [],
    "an ignore pattern removes files from the lint run rather than from these rules");
});

// A rule stays enabled and still does nothing if the code turns it off one line at a
// time. Round 6 (Sol) suppressed `no-unsafe-member-access` with an inline directive in an
// imported module: typecheck, tests, lint and build all passed, and the unchecked write
// reached the production bundle. The config pin above is untouched by that, because the
// rule really is still enabled -- it just is not applied there.
//
// Which directive forms to refuse was measured against oxlint rather than assumed. A
// blanket directive suppresses; one naming a protected rule suppresses; the `eslint-`
// spelling suppresses too; and one mentioned mid-sentence in prose does not suppress at
// all. So the scan anchors where oxlint actually honours a directive -- the start of a
// comment -- which also leaves this file's own prose about directives alone.
//
// Directives naming other rules stay legal, and several files rely on that for
// `no-unsafe-type-assertion`. Only the protected rules, and blanket directives that would
// cover them, are refused.
test("no source file suppresses the unsafe-operation rules", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const files = projectFiles([...typeScriptExtensions, ...javaScriptExtensions]);
  assert.ok(files.includes(fileURLToPath(import.meta.url)),
    "the scan must cover the file that defines it");

  // Assembled from parts so this file does not match its own scan while describing it.
  const disable = ["dis", "able"].join("");
  const directive = new RegExp(
    String.raw`(?://|/\*)\s*(?:oxlint|eslint)-${disable}(?:-next-line|-line)?([^\n*]*)`,
    "gu");

  const offenders: string[] = [];
  for (const file of files) {
    for (const match of readFileSync(file, "utf8").matchAll(directive)) {
      const named = ((match[1] ?? "").split("--")[0] ?? "")
        .split(",")
        .map(rule => rule.trim())
        .filter(rule => rule.length > 0);
      // A directive naming nothing switches off everything, protected rules included.
      const reachesProtected = named.length === 0
        || named.some(rule => protectedRules.some(guarded =>
          rule === guarded || rule === guarded.slice(guarded.indexOf("/") + 1)));
      if (reachesProtected) {
        offenders.push(`${projectRelative(root, file)}: ${match[0].trim()}`);
      }
    }
  }

  assert.deepEqual(offenders.sort(), [],
    "these directives switch off an unsafe-operation rule that the lint exists to "
      + "enforce; narrow the type or fix the code rather than suppressing the rule");
});

// Every rule above is type-aware, and oxlint runs type-aware rules only when asked. Sol
// found that `options.typeAware: false` left them all silently inert; the only thing that
// caught it was incidental, existing `oxlint-disable` directives in the test suite
// becoming "unused" and tripping `reportUnusedDisableDirectives`. Turning that off too
// was completely green. Relying on an accident is not enforcement, so the switches are
// pinned directly.
test("the lint runs in the mode the unsafe-operation rules require", () => {
  assert.equal(oxlintConfig.options?.typeAware, true,
    "the no-unsafe-* rules are type-aware and do not run at all without this");
  assert.equal(oxlintConfig.options?.reportUnusedDisableDirectives, "error",
    "a stale disable directive is how a rule stops applying without anyone noticing");
  assert.equal(oxlintConfig.options?.denyWarnings, true,
    "a rule demoted to a warning does not fail the lint run");
});

// The config file the gates above read is not necessarily the config the lint obeys.
// oxlint merges a nested `.oxlintrc.json` found beside the linted files, and honours
// `.eslintignore`; either one re-opens every hole this file closes while `.oxlintrc.json`
// still reads exactly as asserted. Both were verified: a nested config in `scripts/` and
// an `.eslintignore` entry each returned `npm run analyze` to green with an unsafe `any`
// file present. The invocation therefore refuses both, and this pins the refusal.
test("the lint invocation refuses config and ignore files it did not declare", () => {
  const lint = packageJson.scripts?.lint ?? "";

  assert.match(lint, /\boxlint\b/u, "the lint script must run oxlint");
  assert.ok(lint.includes("--no-ignore"),
    "without this an .eslintignore file silently drops sources from the lint run");
  assert.ok(lint.includes("--disable-nested-config"),
    "without this a nested .oxlintrc.json silently overrides the rules pinned above");
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
    "node scripts/verify-analysis-host.ts && "
      + "oxlint --no-ignore --disable-nested-config src test scripts "
      + "engine/wwwroot/inspect-web-engine.js vite.config.ts",
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
