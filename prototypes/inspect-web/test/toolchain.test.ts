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
  extname,
  isAbsolute,
  join,
  resolve,
} from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import { scan } from "../scripts/check-no-cross-origin-subresources.ts";
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
import {
  auditedBuild,
  builtinPluginNames,
  bundlerReadFiles,
  shippedArtifacts,
} from "./vite-audit.ts";

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
  readonly plugins?: readonly string[] | null;
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
  readonly plugins?: readonly string[];
  readonly rules?: Readonly<Record<string, unknown>>;
}

interface HtmlValidateConfig {
  readonly root?: boolean;
  readonly extends?: readonly string[];
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
  readonly redirect?: string;
  readonly headers?: Readonly<Record<string, string>>;
}

interface StaticWebAppConfig {
  readonly globalHeaders: Readonly<Record<string, string>>;
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
const htmlValidateConfig
  = readJson<HtmlValidateConfig>("../.htmlvalidate.json");
const browserTsconfig = readJson<TsconfigFile>("../tsconfig.json");
const testTsconfig = readJson<TsconfigFile>("tsconfig.json");
const nodeTsconfig = readJson<TsconfigFile>("../tsconfig.node.json");
const staticWebAppConfig
  = readJson<StaticWebAppConfig>("../staticwebapp.config.json");

// The lint targets are read here rather than inside the gate that checks coverage,
// because the pruning rules below also need to know which directories hold authored
// source. Both answers come from the one `lint` script, so neither can drift from it.
//
// The script chains more than one linter, so the scan stops at the next `&&`. Without
// that, `html-validate` and its glob are read as oxlint targets, and a bogus target is a
// target the coverage gate below will happily consider a file "covered" by.
const lintTokens = (() => {
  const lint = packageJson.scripts?.lint ?? "";
  const oxlintCall = lint.slice(lint.indexOf("oxlint "));
  const tokens = oxlintCall.split(/\s+/u).slice(1);
  const chained = tokens.indexOf("&&");
  return chained === -1 ? tokens : tokens.slice(0, chained);
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
  assert.deepEqual(
    testTsconfig.include,
    ["./**/*.ts", "../browser/**/*.ts", "../playwright.config.ts"],
  );
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
// The production facade set, in the order `eng/generate-inspect-web-engine-facade.sh`
// generates it. Every gate below that names a generated artifact is spelled from this one
// inventory, so a facade added, removed or renamed moves all of them together and none of
// them silently keeps covering a file that no longer exists.
const facadeModules = [
  "inspect-web-host",
  "inspect-web-package",
  "inspect-web-metadata",
  "inspect-web-analysis",
  "inspect-web-source",
  "inspect-web-call-graph",
  "inspect-web-catalog",
] as const;
const generatedFacadeSources =
  facadeModules.map(module => `engine/facades/${module}.ts`);
const publishedFacadeModules =
  facadeModules.map(module => `engine/wwwroot/${module}.js`);

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
// the compiler-derived engine facade, which is Wasm build output rather than authored source.
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
    .filter(file => javaScriptExtensions.some(extension => file.endsWith(extension)))
    .sort();

  assert.deepEqual(present, [...publishedFacadeModules].sort(),
    "authored JavaScript here would be checked by neither the compiler nor the "
      + "type-aware lint rules the rest of the project is held to");
  assert.deepEqual(exempted, present,
    "the lint exemption and the JavaScript it covers must name the same files");
});

// Every gate in this file accounts for *files*: the compiler builds a program out of
// `.ts` files, and oxlint is handed a list of paths. Script written inside a document is
// therefore invisible to both, and the browser runs it anyway.
//
// This was not hypothetical. `index.html` carried a `<script type="module">` block that
// dereferenced `document.querySelector("#app")` without checking it -- the exact defect
// `no-unsafe-member-access` is enabled to catch -- and shipped that way for as long as
// the file existed, because nothing read it. Round 5 (Sol) found it; it is issue #4783
// and `src/bootstrap.ts` is where that code lives now.
//
// The first version of this gate named the three ways HTML runs script -- element
// content, `on*` handler attributes, and the `javascript:` scheme -- and rejected those.
// That is a deny list, and it had the failure a deny list always has. Round 1 (Gemini)
// probed `<object data="javascript:...">`, which it caught; three neighbours of that
// probe walked straight through. `<iframe srcdoc="&lt;script&gt;...">` was the worst,
// because it needs no interaction: all four commands stayed green while `dist/index.html`
// shipped a document that runs script on load. An unquoted `href=javascript:alert(1)` and
// an entity-encoded `&#106;avascript:` also passed, because the scheme test wanted a
// quote before the word and a literal `j` in it.
//
// So the question is inverted. Rather than enumerate what can run script, which is a list
// HTML keeps extending, this says what a document here is allowed to contain and rejects
// everything else. A document may *reference* script and may not *contain* any, which
// leaves a file as the only place script can be. For a relative reference that file is a
// module under `src/`, which every other gate here already reads. For an absolute one it
// is remote code that no gate reads -- and "absolute" here means remote rather than merely
// scheme-bearing, because `//host/path` is remote too. The check below requires a real
// `integrity` digest on those: the property is that the bytes are pinned to a hash, not
// that anything analyzed them. An element or attribute nobody has classified fails, so the next
// HTML feature that can run script is rejected on the grounds that it is unrecognized,
// which is the one property a deny list cannot have.
//
// Comments are deliberately not stripped before this runs. Commented-out script is inert,
// so flagging it is a false positive -- but `<!-->` is a complete comment in HTML5 and
// parsers disagree about the edges, so trusting a comment-stripper here would put a
// second parser between the gate and the truth. Deleting dead script is the better
// resolution anyway.
const inertElements: ReadonlySet<string> = new Set([
  "a", "article", "aside", "b", "base", "body", "br", "button", "code", "div", "em",
  "figcaption", "figure", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header",
  "hr", "html", "i", "img", "label", "li", "link", "main", "meta", "nav", "noscript",
  "ol", "p", "pre", "script", "section", "small", "span", "strong", "style", "table",
  "tbody", "td", "th", "thead", "title", "tr", "ul",
]);

// `on*` handler attributes are absent by construction rather than by a rule that spells
// out `on`, and so are `srcdoc`, `http-equiv` and `object`'s `data`. Each of those is
// script, or a way to reach script, and none of them is here.
const inertAttributes: ReadonlySet<string> = new Set([
  "alt", "as", "async", "charset", "class", "content", "crossorigin", "defer",
  "disabled", "download", "for", "height", "hidden", "href", "id", "integrity", "lang",
  "media", "name", "referrerpolicy", "rel", "role", "sizes", "src", "srcset", "target",
  "title", "type", "width",
]);

const inertAttributePrefixes = ["aria-", "data-"] as const;

// Round 3 (Sol) reported a false positive: every allowed attribute was read as a URL, so
// `title="Status: ready"` was rejected for carrying a `status:` scheme. The scheme rule
// belongs on values a browser dereferences, but enumerating *those* would fail open the
// moment one was forgotten -- the failure this file has now made four times. So the
// exemption is enumerated instead: an attribute is read as a URL unless it is named here
// as text. A new entry in `inertAttributes` is scheme-checked until someone says why not.
//
// `content` is here because the only form that redirects is `http-equiv="refresh"`, and
// `http-equiv` is absent from `inertAttributes`, so that element is rejected before its
// `content` matters. The test named "a text attribute cannot become a URL the browser
// follows" pins that reasoning.
const textAttributes: ReadonlySet<string> = new Set([
  "alt", "charset", "class", "content", "download", "for", "height", "id", "lang",
  "media", "name", "rel", "role", "sizes", "target", "title", "type", "width",
]);

// A relative URL has no scheme, and is fine because the document base is required to be
// local -- see the `base` check in the gate below, without which "relative" would not
// mean "local" at all. Everything else has to be named, which is what rejects
// `javascript:`, `data:` and `vbscript:` without listing any of them.
const inertSchemes: ReadonlySet<string> = new Set(["http", "https", "mailto"]);

function urlScheme(value: string): string | undefined {
  return /^([a-z][\d+.a-z-]*):/i.exec(value)?.[1]?.toLowerCase();
}

// Round 2 (Opus) found the `integrity` requirement keyed on "has a scheme", which reads
// `//cdn.example.com/x.js` as relative and skips the check. It is not relative: Vite
// itself treats `/^(https?:)?\/\//` as external and copies it into the document verbatim,
// so those bytes come from a third party. "Remote" is the property that matters, and a
// scheme-relative URL has it.
function isRemoteReference(value: string): boolean {
  // `//` is not the only spelling of an authority: the URL parser treats a backslash as a
  // slash for special schemes, so `/\host/x.js`, `\\host/x.js` and `\/host/x.js` all
  // resolve to `https://host/x.js`. Normalizing first is what makes one comparison cover
  // every spelling, and getting this wrong would skip the `integrity` requirement below.
  const normalized = value.replaceAll("\\", "/");
  return urlScheme(value) !== undefined || normalized.startsWith("//");
}

// Presence is not pinning. `integrity=""` satisfies a check for the attribute and disables
// subresource integrity in every browser, so require a value the browser will honor: one
// or more whitespace-separated `sha256`/`sha384`/`sha512` digests.
function pinsItsBytes(value: string): boolean {
  // Round 4 (Sol) found this narrower than the grammar it claims to check. Algorithm
  // tokens are matched ASCII case-insensitively, and a hash expression may carry a
  // `?option` suffix, reserved for forward compatibility, that user agents ignore. Both
  // spellings pin the bytes, and both were rejected.
  //
  // A browser keeps the expressions whose algorithm it supports and enforces the
  // strongest of those, ignoring the rest, so one well-formed supported digest is what
  // makes the bytes pinned. An unrecognized entry beside it does not weaken that, which
  // is why this asks for one rather than for all.
  // Round 5 (Sol) found the split too generous. `\s` and `trim()` are Unicode, and a
  // browser separates hash expressions on ASCII whitespace only. Given `bogus\u00A0sha384-`
  // plus a digest, this read two entries and found a good one while a browser read a single
  // entry, recognized no algorithm in it, derived no metadata at all and ran the script
  // unpinned. Splitting the way the browser splits is what makes one entry mean one entry.
  return value.split(/[\t\n\f\r ]+/u).some(entry =>
    /^sha(?:256|384|512)-[\d+/A-Za-z]+={0,2}(?:\?[!-~]*)?$/i.test(entry));
}

// Reading markup with a regular expression is how the previous two versions of this gate
// failed, and the second failure was worse than the first. A pattern that matches whole
// tags *skips* what it cannot match, so markup it does not understand becomes markup it
// does not check. Round 1 (Gemini) landed exactly there: `<iframe src="..." attr=foo'bar>`
// is well-formed HTML -- an unquoted value may contain a quote as long as it does not
// start with one -- and the pattern's `'[^']*'` branch could not match it, so the tag
// never appeared in the loop at all and the whole allow list was skipped with all four
// commands green.
//
// A missing check that reports nothing is the worst failure available to a gate, so the
// question here is not "did a tag match?" but "was every byte accounted for?". This
// tokenizer walks the document once and consumes text and markup explicitly. Anything
// tag-shaped that it cannot tokenize is reported rather than passed over, which is the
// property the regex could not have: a construct nobody anticipated fails.
//
// This is deliberately not a spec-complete HTML parser, and it does not need to be. It
// only needs to be exhaustive -- to have no path that silently drops input -- and to err
// toward reporting. A false positive costs an author one comment; a false negative is
// how unlinted code shipped for as long as `index.html` existed.
interface MarkupAttribute { readonly name: string; readonly value: string }
interface MarkupTag { readonly element: string; readonly attributes: readonly MarkupAttribute[] }

// `script` and `style` hold raw text rather than markup, so a `<` inside them starts
// nothing. Tokenizing their contents would invent tags out of `a < b`.
const rawTextElements: ReadonlySet<string> = new Set(["script", "style", "textarea", "title"]);

function isMarkupSpace(character: string): boolean {
  return character === " " || character === "\t" || character === "\n"
    || character === "\r" || character === "\f";
}

function scanMarkup(source: string, report: (problem: string) => void): MarkupTag[] {
  // HTML5 input preprocessing replaces every CR and CRLF with LF before the tokenizer
  // runs, so a browser never sees a CR at all. Round 2 (Opus) found that omitting this
  // step let `</title\r>` end the element for parse5 and for every browser while this
  // scan read straight past it, swallowing a following `<script>` in the process. Doing
  // the same normalization is what makes the two agree; leaving CR out of one character
  // class and in another is how they diverged.
  const html = source.replaceAll(/\r\n?/g, "\n");
  const tags: MarkupTag[] = [];
  let at = 0;

  while (at < html.length) {
    const open = html.indexOf("<", at);
    if (open < 0) { break; }
    at = open;

    if (html.startsWith("<!--", at)) {
      const close = html.indexOf("-->", at + 4);
      if (close < 0) {
        report("an unterminated `<!--` comment hides the rest of the document");
        return tags;
      }
      // Commented-out markup is inert, so reporting it is a false positive -- but a
      // comment is also the easiest place to hide something from a reader, and `<!-->`
      // is a complete comment in HTML5 that parsers disagree about. Deleting dead markup
      // is cheap; trusting a comment scanner is not.
      if (/<[a-z]/i.test(html.slice(at + 4, close))) {
        report("a comment contains markup. Commented-out markup is inert, but it is not "
          + "reviewed either, so delete it rather than leaving it here");
      }
      at = close + 3;
      continue;
    }

    // Doctypes, CDATA and processing instructions carry no attributes to check.
    if (html.startsWith("<!", at) || html.startsWith("<?", at)) {
      const close = html.indexOf(">", at);
      if (close < 0) {
        report("an unterminated `<!` or `<?` declaration hides the rest of the document");
        return tags;
      }
      at = close + 1;
      continue;
    }

    const isEnd = html.startsWith("</", at);
    const nameAt = at + (isEnd ? 2 : 1);
    const name = /^[a-z][^\s/>]*/i.exec(html.slice(nameAt))?.[0];
    if (name === undefined) {
      // Per HTML5 a `<` that begins nothing is literal text, as in `a < b`.
      at += 1;
      continue;
    }

    const element = name.toLowerCase();
    const attributes: MarkupAttribute[] = [];
    let cursor = nameAt + name.length;
    let closed = false;

    while (cursor < html.length) {
      while (isMarkupSpace(html.charAt(cursor))) { cursor += 1; }
      if (html.charAt(cursor) === ">") { cursor += 1; closed = true; break; }
      if (html.charAt(cursor) === "/" && html.charAt(cursor + 1) === ">") {
        cursor += 2;
        closed = true;
        break;
      }

      const attributeAt = cursor;
      while (cursor < html.length && !isMarkupSpace(html.charAt(cursor))
        && html.charAt(cursor) !== "=" && html.charAt(cursor) !== ">"
        && html.charAt(cursor) !== "/") {
        cursor += 1;
      }
      if (cursor === attributeAt) {
        // No progress is possible from here, so stopping silently would drop the rest of
        // the tag. Report instead.
        report(`<${element}> could not be tokenized at offset ${cursor}, so its `
          + "attributes were never checked");
        return tags;
      }
      const attributeName = html.slice(attributeAt, cursor);

      while (isMarkupSpace(html.charAt(cursor))) { cursor += 1; }
      let value = "";
      if (html.charAt(cursor) === "=") {
        cursor += 1;
        while (isMarkupSpace(html.charAt(cursor))) { cursor += 1; }
        const quote = html.charAt(cursor);
        if (quote === '"' || quote === "'") {
          const close = html.indexOf(quote, cursor + 1);
          if (close < 0) {
            report(`<${element} ${attributeName}> has an unterminated quoted value, `
              + "which hides the rest of the document");
            return tags;
          }
          value = html.slice(cursor + 1, close);
          cursor = close + 1;
        } else {
          // An unquoted value runs to whitespace or `>` and may contain a quote. This is
          // the case the regex could not express.
          const valueAt = cursor;
          while (cursor < html.length && !isMarkupSpace(html.charAt(cursor))
            && html.charAt(cursor) !== ">") {
            cursor += 1;
          }
          value = html.slice(valueAt, cursor);
        }
      }
      attributes.push({ name: attributeName, value });
    }

    if (!closed) {
      report(`<${element}> is never closed by \`>\`, so the rest of the document was `
        + "never checked");
      return tags;
    }

    if (!isEnd) { tags.push({ element, attributes }); }
    at = cursor;

    if (!isEnd && rawTextElements.has(element)) {
      // The end tag is `</name` followed by whitespace, `/` or `>` -- the terminators the
      // tokenizer uses in its end-tag-name state, read here after the CR normalization
      // above so that `</name\r>` is one of them. Round 1 (Opus) found that requiring
      // `</script\s*>` misses `</script/>` and `</script foo="bar">`, both of which close
      // the element and run the body. Accepting a bare `</script` prefix is the opposite
      // error: `</scriptfoo>` closes nothing, and treating it as the end would put the
      // real body outside the element, where nothing reads it.
      const end = new RegExp(`</${element}(?=[\\t\\n\\f />]|$)`, "i")
        .exec(html.slice(cursor));
      if (end === null) {
        report(`<${element}> is never closed, so the rest of the document is inside it `
          + "and was never checked");
        return tags;
      }
      const closeAt = cursor + end.index;
      const body = html.slice(cursor, closeAt).trim();
      if (element === "script" && body.length > 0) {
        report(`<script> has a body of ${body.split("\n").length} line(s)`);
      } else if (element !== "script" && /<[/a-z]/i.test(body)) {
        // Raw text is not markup, so a tag inside one of these is either a mistake or the
        // two tokenizers disagreeing about where the element ends -- and the second case
        // means this scan just swallowed markup it never checked. Report rather than
        // discard. `<title>` and `<style>` are on the allow list, so without this the
        // swallowed region would vanish in silence.
        //
        // Round 4 (Opus) observed that within HTML the `</` half does all the detecting,
        // since a raw-text element can only end early via `</name`, and offered the
        // `<[a-z]` half as removable over-strictness. It is kept deliberately: in foreign
        // content a browser does not use raw text at all, so `<svg><title><script>` runs
        // that script while this scan swallows it, and the start-tag half is what sees
        // it. `svg` is absent from `inertElements` today, which is why that is not a live
        // hole -- but adding an inline icon would make this the only rule catching it,
        // and the cost is a literal `<` in a title needing to be written `&lt;`.
        report(`<${element}> contains markup. Raw text cannot hold a tag, so either the `
          + "content is wrong or this element does not end where it appears to");
      }
      at = closeAt;
    }
  }

  return tags;
}

// The gate below runs this over every document in the project. It is a function rather
// than a loop body so that other tests can hold a specimen against the same rules -- a
// test that restated these checks would keep passing after the real ones were weakened,
// which is the whole failure it would exist to catch.
function markupFindings(name: string, html: string): string[] {
  const findings: string[] = [];
  const tags = scanMarkup(html, problem => findings.push(`${name}: ${problem}`));
  for (const tag of tags) {
    if (!inertElements.has(tag.element)) {
      findings.push(`${name}: <${tag.element}> is not a known inert element. If it `
        + "cannot run script, add it to `inertElements` and say why here");
    }
    // A `script` with an absolute `src` is the one construct the allow list permits that
    // no gate here reads: it is remote code, not a module under a lint target. Round 1
    // (Opus) found the prose claiming otherwise. `integrity` is what makes those bytes
    // pinned to a hash, so require it rather than overstate what the gate buys.
    // `index.html` already pins all three of its CDN scripts.
    if (tag.element === "script") {
      const source = tag.attributes.find(candidate =>
        candidate.name.toLowerCase() === "src");
      if (source !== undefined && isRemoteReference(source.value)) {
        const pinned = tag.attributes.find(candidate =>
          candidate.name.toLowerCase() === "integrity");
        if (pinned === undefined || !pinsItsBytes(pinned.value)) {
          findings.push(`${name}: <script src="${source.value}"> loads remote code `
            + "that no compiler or lint here reads, and pins it to no hash. Add an "
            + "`integrity` digest");
        }
      }
    }
    // Round 3 (Sol) found that every "relative, therefore local" judgement in this gate
    // rests on the document base, and nothing checked it. `<base href="https://host/">`
    // leaves each `src` textually relative while making the bytes -- including the
    // bundle Vite emits at `/assets/` -- come from somewhere else entirely. A browser
    // honours the first `base`, so requiring every one of them to be local is what makes
    // the rest of this reasoning true rather than merely plausible.
    if (tag.element === "base") {
      const target = tag.attributes.find(candidate =>
        candidate.name.toLowerCase() === "href");
      if (target !== undefined && isRemoteReference(target.value)) {
        findings.push(`${name}: <base href="${target.value}"> makes every relative URL `
          + "in this document remote, including the bundle. The rest of this gate reads "
          + "a relative `src` as a local module, which that would silently stop being");
      }
    }
    for (const attribute of tag.attributes) {
      const spelled = attribute.name.toLowerCase();
      if (!inertAttributes.has(spelled)
        && !inertAttributePrefixes.some(prefix => spelled.startsWith(prefix))) {
        findings.push(`${name}: <${tag.element} ${spelled}> is not a known inert `
          + "attribute. Event handlers are spelled this way, and so are `srcdoc` and "
          + "`http-equiv`");
        continue;
      }
      if (textAttributes.has(spelled)
        || inertAttributePrefixes.some(prefix => spelled.startsWith(prefix))) {
        continue;
      }
      const scheme = urlScheme(attribute.value);
      if (scheme !== undefined && !inertSchemes.has(scheme)) {
        findings.push(`${name}: <${tag.element} ${spelled}> carries a \`${scheme}:\` `
          + "URL, and that scheme is not one this project treats as inert");
      }
    }
  }
  return findings;
}

test("subresource integrity is read the way a browser separates it", () => {
  // Round 5 (Sol) found `\s`-splitting accepted a value a browser rejects outright. The
  // browser separates hash expressions on ASCII whitespace, so a non-ASCII space does not
  // start a new entry: the whole value is one unrecognized algorithm token, no metadata is
  // derived and the remote script runs unpinned while this said it was pinned.
  const digest = "sha384-zLRFO4dwowZvh8kzutOb5AWhH7f39HeJp+N7PtHF1SQtTBnifRx0AtmvTYs3F4YV";
  const script = (integrity: string): string =>
    `<script src="https://cdn.example.com/x.js" integrity="${integrity}"></script>`;

  for (const separator of ["\u00A0", "\u2003", "\u3000"]) {
    assert.ok(markupFindings("sri", script(`bogus${separator}${digest}`)).length > 0,
      `a browser splits hash expressions on ASCII whitespace only, so \`bogus${
        JSON.stringify(separator)}…\` is a single unrecognized entry and the bytes are `
        + "not pinned at all. This accepted it");
  }

  // The separators a browser does honor still work, and so does an unsupported algorithm
  // beside a supported one -- a browser enforces the strongest it supports and ignores the
  // rest, so the bytes are pinned.
  for (const accepted of [digest, `md5-abc=  ${digest}`, `${digest}\t${digest}`]) {
    assert.deepEqual(markupFindings("sri", script(accepted)), [],
      `\`${accepted}\` pins the bytes and was rejected`);
  }
});

test("a text attribute cannot become a URL the browser follows", () => {
  // The scheme rule exempts `textAttributes`, so each entry is a claim that a browser
  // never dereferences that value. Two of those claims are load-bearing enough to pin.

  // `content` only redirects as `http-equiv="refresh"`, and that attribute is denied, so
  // the element carrying such a `content` never survives to have its value read. If
  // `http-equiv` were ever allowed, exempting `content` would open a redirect to any
  // scheme -- so this assertion is the reason the exemption above is safe, not a nit.
  assert.ok(!inertAttributes.has("http-equiv"),
    "`content` is exempt from the scheme rule because `http-equiv` is denied. Allowing "
      + "`http-equiv` makes `content` a redirect target, so remove `content` from "
      + "`textAttributes` in the same change");

  // A stale exemption is a hole that no other test can see: it would keep exempting an
  // attribute long after the allow list stopped mentioning it.
  const stale = [...textAttributes].filter(entry => !inertAttributes.has(entry)).sort();
  assert.deepEqual(stale, [],
    "these attributes are exempt from the scheme rule but are not allowed at all, so the "
      + "exemption describes markup this project cannot contain");
});

// A malformed document is not an attack; it is a typo. But error recovery decides what a
// browser actually runs, so markup the scan cannot read has to be reported rather than
// guessed at. Each specimen below is a shape a browser silently repairs while carrying a
// script body the gates would otherwise never read.
test("markup a browser would repair is reported rather than guessed at", () => {
  const repaired = [
    // A `script` is not void, so the solidus does not close it.
    ["a solidus does not close a script",
      "<script src=\"/src/bootstrap.ts\" />globalThis.MY_MARKER = 1;"],
    // A browser keeps the first `src` and drops the second. The scan reads both.
    ["a browser keeps the first src and drops the second",
      "<script src=\"/src/bootstrap.ts\" src=\"javascript:void 0\"></script>"],
    // A `script` between `</head>` and `<body>` is relocated by error recovery.
    ["a script after the head is relocated",
      "<head><title>t</title></head><script>globalThis.MY_MARKER = 1;</script>"],
  ] as const;

  for (const [reason, markup] of repaired) {
    assert.ok(markupFindings(reason, markup).length > 0,
      `${reason}: a browser repairs this document, so what it runs is not what the file `
        + "says, and the scan has to report it rather than guess");
  }
});

test("no HTML document carries script the gates cannot read", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const documents = projectFiles([".html", ".htm", ".xhtml", ".svg"]);

  // Non-vacuity. A walk that silently found nothing would pass every assertion below
  // while proving nothing at all, and the entry document is the reason this gate exists.
  const names = documents.map(file => projectRelative(root, file)).sort();
  assert.ok(names.includes("index.html"),
    `the walk found no entry document, so this gate proved nothing; it saw ${
      names.length > 0 ? names.join(", ") : "no markup at all"}`);

  const findings = documents.flatMap(file =>
    markupFindings(projectRelative(root, file), readFileSync(file, "utf8")));

  assert.deepEqual(findings, [],
    "this markup can run script that neither the compiler nor the lint reads, because "
      + "both of them account for files and none of this is in one. Move the script into "
      + "a module under `src/` and reference it with `src=`, the way `index.html` loads "
      + "`src/bootstrap.ts`");
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
const separatelyCompiledTypeScript = new Set([
  ...generatedFacadeSources,
  "multi-facade-canary/coordinator.ts",
  "multi-facade-canary/exercise.ts",
  "multi-facade-canary/facades/alpha.ts",
  "multi-facade-canary/facades/beta.ts",
  "managed-operation-bridge-canary/initialize.ts",
  "managed-operation-bridge-canary/exercise.ts",
  "managed-operation-bridge-canary/facades/bridge.ts",
]);

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
    .filter(file => !checked.has(resolve(file))
      && !separatelyCompiledTypeScript.has(projectRelative(root, file)))
    .map(file => projectRelative(root, file))
    .sort();

  assert.deepEqual(unchecked, [],
    "these TypeScript files are in no compiler project, so `npm run typecheck` reads "
      + "neither their types nor their errors; add them to a tsconfig `include` or the "
      + "pinned separate compiler gate");
});

test("the generated facade TypeScript uses its SDK-owned compiler gates", () => {
  const engineGenerationScript = readFileSync(
    new URL("../../../eng/generate-inspect-web-engine-facade.sh", import.meta.url),
    "utf8");
  const multiFacadeGenerationScript = readFileSync(
    new URL(
      "../../../eng/generate-inspect-web-multi-facade-canary.sh",
      import.meta.url,
    ),
    "utf8",
  );
  const managedBridgeGenerationScript = readFileSync(
    new URL(
      "../../../eng/generate-inspect-web-managed-operation-bridge-canary.sh",
      import.meta.url,
    ),
    "utf8",
  );

  assert.deepEqual([...separatelyCompiledTypeScript], [
    ...generatedFacadeSources,
    "multi-facade-canary/coordinator.ts",
    "multi-facade-canary/exercise.ts",
    "multi-facade-canary/facades/alpha.ts",
    "multi-facade-canary/facades/beta.ts",
    "managed-operation-bridge-canary/initialize.ts",
    "managed-operation-bridge-canary/exercise.ts",
    "managed-operation-bridge-canary/facades/bridge.ts",
  ]);
  assert.match(
    engineGenerationScript,
    /ts_output_directory="\$inspect_web\/engine\/facades"/);
  assert.match(
    engineGenerationScript,
    /dts_output_directory="\$inspect_web\/src\/facades"/);
  assert.match(
    engineGenerationScript,
    /js_output_directory="\$inspect_web\/engine\/wwwroot"/);
  // The consumer map is the whole membership claim: its domain must be the exact set of
  // canonical artifacts, and its range the exact set of public modules, in one place.
  for (const module of facadeModules) {
    assert.ok(engineGenerationScript.includes(`  "${module}"\n`),
      `the generation script does not map ${module}`);
  }
  for (const artifact of [
    "InspectWeb.Engine.ts",
    "InspectWeb.Engine.PackageExports.ts",
    "InspectWeb.Engine.MetadataExports.ts",
    "InspectWeb.Engine.AnalysisExports.ts",
    "InspectWeb.Engine.SourceExports.ts",
    "InspectWeb.Engine.CallGraphExports.ts",
    "InspectWeb.Engine.CatalogExports.ts",
  ]) {
    assert.ok(engineGenerationScript.includes(`  "${artifact}"\n`),
      `the generation script does not root ${artifact}`);
  }
  assert.match(
    engineGenerationScript,
    /emitted_artifacts" != "\$expected_artifacts/);
  assert.match(
    engineGenerationScript,
    /context_type="InspectWeb\.Engine\.InspectWebJsExportContext"/);
  assert.match(
    engineGenerationScript,
    /--context "\$context_type"/);
  assert.match(
    engineGenerationScript,
    /--assembly-search-path "\$source_assembly_directory"/);
  assert.match(
    engineGenerationScript,
    /generator_build_properties\+=\("-p:VersionPrefix=\$contract_version_prefix"\)/);
  assert.match(
    engineGenerationScript,
    /-p:VersionPrefix="\$version_prefix"[\s\S]*--contract[\s\S]*"\$version_prefix"/);
  // `--contract` produces the complete declaration set into a directory, which is what the
  // paired async deployment lanes compare against the checked-in declarations.
  assert.match(
    engineGenerationScript,
    /--contract <assembly> <declaration-output-directory> <version-prefix>/);
  assert.match(
    engineGenerationScript,
    /Microsoft\.NETCore\.App\.Runtime\.Mono\.browser-wasm[\s\S]*dotnet\.d\.ts/);
  assert.match(
    engineGenerationScript,
    /-target:ProcessFrameworkReferences[\s\S]*-getItem:RuntimePack/);
  assert.doesNotMatch(engineGenerationScript, /DOTNET_ROOT|sort -V/);
  assert.match(engineGenerationScript, /"newLine": "lf"/);
  assert.match(
    engineGenerationScript,
    /"\$tsc" -p "\$scratch\/sources\/tsconfig\.json"/);
  // The whole set is compiled by one program built from an exact file inventory, not from a
  // directory glob that would admit an unowned source.
  assert.match(
    engineGenerationScript,
    /printf '"%s\.ts"' "\$\{facade_modules\[\$index\]\}"/);
  assert.doesNotMatch(engineGenerationScript, /"include": \["\*/);

  assert.match(
    multiFacadeGenerationScript,
    /canary="\$repo_root\/prototypes\/inspect-web\/multi-facade-canary"/);
  assert.match(
    multiFacadeGenerationScript,
    /Microsoft\.NETCore\.App\.Runtime\.Mono\.browser-wasm[\s\S]*dotnet\.d\.ts/);
  assert.match(
    multiFacadeGenerationScript,
    /-target:ProcessFrameworkReferences[\s\S]*-getItem:RuntimePack/);
  assert.doesNotMatch(multiFacadeGenerationScript, /DOTNET_ROOT|sort -V/);
  assert.match(multiFacadeGenerationScript, /"newLine": "lf"/);
  assert.match(
    multiFacadeGenerationScript,
    /"include": \["facades\/\*\.ts", "coordinator\.ts", "exercise\.ts"\]/);
  assert.match(
    multiFacadeGenerationScript,
    /"\$tsc" -p "\$scratch\/tsconfig\.json"/);

  assert.match(
    managedBridgeGenerationScript,
    /canary="\$repo_root\/prototypes\/inspect-web\/managed-operation-bridge-canary"/);
  assert.match(
    managedBridgeGenerationScript,
    /Microsoft\.NETCore\.App\.Runtime\.Mono\.browser-wasm[\s\S]*dotnet\.d\.ts/);
  assert.match(
    managedBridgeGenerationScript,
    /-target:ProcessFrameworkReferences[\s\S]*-getItem:RuntimePack/);
  assert.doesNotMatch(managedBridgeGenerationScript, /DOTNET_ROOT|sort -V/);
  assert.match(managedBridgeGenerationScript, /"newLine": "lf"/);
  assert.match(
    managedBridgeGenerationScript,
    /"include": \["facades\/\*\.ts", "initialize\.ts", "exercise\.ts"\]/);
  assert.match(
    managedBridgeGenerationScript,
    /"\$tsc" -p "\$scratch\/tsconfig\.json"/);
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
// So the question moved from what the bundler emitted to what it took as input. Vite 8's
// Rolldown graph accounts for modules, entry HTML and CSS, and a second build with asset
// inlining disabled exposes every asset source through `originalFileNames`. Both answers
// come from the real Vite build; neither parses source or guesses which extensions matter.
// This fixture keeps the pathological small-asset case executable.
test("the bundler input audit includes inlined asset sources", async () => {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-vite-audit-"));
  try {
    writeFileSync(
      join(root, "index.html"),
      '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(
      join(root, "main.js"),
      'globalThis.asset = new URL("./payload.js", import.meta.url).href;',
    );
    writeFileSync(join(root, "payload.js"), "unchecked payload");

    const read = (await bundlerReadFiles(root)).map(file => resolve(file));

    assert.ok(read.includes(resolve(root, "payload.js")));
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the bundler input audit includes query-bearing module sources", async () => {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-vite-audit-"));
  try {
    writeFileSync(
      join(root, "index.html"),
      '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(
      join(root, "main.js"),
      'import payload from "./payload.txt?raw"; globalThis.payload = payload;',
    );
    writeFileSync(join(root, "payload.txt"), "unchecked payload");

    const read = (await bundlerReadFiles(root)).map(file => resolve(file));

    assert.ok(read.includes(resolve(root, "payload.txt")));
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the bundler input audit includes worker module sources", async () => {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-vite-audit-"));
  try {
    writeFileSync(
      join(root, "index.html"),
      '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(
      join(root, "main.js"),
      'new Worker(new URL("./worker-payload.js", import.meta.url), { type: "module" });',
    );
    writeFileSync(join(root, "worker-payload.js"), "self.postMessage('unchecked payload');");

    const read = (await bundlerReadFiles(root)).map(file => resolve(file));

    assert.ok(read.includes(resolve(root, "worker-payload.js")));
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the bundler input audit includes CSS imports and explicitly inlined assets", async () => {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-vite-audit-"));
  try {
    writeFileSync(
      join(root, "index.html"),
      '<link rel="stylesheet" href="/styles.css">'
        + '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(join(root, "main.js"), "globalThis.main = true;");
    writeFileSync(
      join(root, "styles.css"),
      '@import "./more.css"; .asset { background: url("./payload.svg?inline"); }',
    );
    writeFileSync(join(root, "more.css"), ".imported { color: green; }");
    writeFileSync(join(root, "payload.svg"), "<svg>unchecked payload</svg>");

    const read = (await bundlerReadFiles(root)).map(file => resolve(file));

    assert.ok(read.includes(resolve(root, "more.css")));
    assert.ok(read.includes(resolve(root, "payload.svg")));
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the bundler input audit includes worker asset sources", async () => {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-vite-audit-"));
  try {
    writeFileSync(
      join(root, "index.html"),
      '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(
      join(root, "main.js"),
      'new Worker(new URL("./worker.js", import.meta.url), { type: "module" });',
    );
    writeFileSync(
      join(root, "worker.js"),
      'import "./worker.css"; self.asset = new URL("./payload.svg", import.meta.url).href;',
    );
    writeFileSync(join(root, "payload.svg"), "<svg>unchecked payload</svg>");
    writeFileSync(
      join(root, "worker.css"),
      '@import "./worker-more.css"; .asset { background: url("./worker-inline.svg?inline"); }',
    );
    writeFileSync(join(root, "worker-more.css"), ".imported { color: purple; }");
    writeFileSync(join(root, "worker-inline.svg"), "<svg>worker inline payload</svg>");

    const read = (await bundlerReadFiles(root)).map(file => resolve(file));

    assert.ok(read.includes(resolve(root, "payload.svg")));
    assert.ok(read.includes(resolve(root, "worker-more.css")));
    assert.ok(read.includes(resolve(root, "worker-inline.svg")));
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("the shipped comparison includes HTML and non-JavaScript output", async () => {
  const parent = fileURLToPath(new URL("../", import.meta.url));
  const root = mkdtempSync(join(parent, ".vite-audit-"));
  try {
    writeFileSync(
      join(root, "package.json"),
      JSON.stringify({ type: "module", scripts: { build: "vite build" } }),
    );
    writeFileSync(
      join(root, "index.html"),
      '<script type="module" src="/main.js"></script>',
    );
    writeFileSync(join(root, "main.js"), "globalThis.main = true;");
    writeFileSync(
      join(root, "vite.config.js"),
      "export default { plugins: process.env.npm_lifecycle_event === \"build\""
        + " ? [{ name: \"conditional-html\", transformIndexHtml(html) {"
        + " return html + \"<!-- conditional payload -->\"; } }] : [] };\n",
    );

    const audited = await auditedBuild(root);
    const shipped = shippedArtifacts(root);
    const auditedJavaScript = audited.artifacts
      .filter(artifact => artifact.fileName.endsWith(".js"));
    const shippedJavaScript = shipped
      .filter(artifact => artifact.fileName.endsWith(".js"));
    const auditedHtml = audited.artifacts
      .find(artifact => artifact.fileName === "index.html");
    const shippedHtml = shipped
      .find(artifact => artifact.fileName === "index.html");

    assert.deepEqual(shippedJavaScript, auditedJavaScript,
      "the fixture must differ only outside JavaScript output");
    assert.ok(auditedHtml !== undefined);
    assert.ok(shippedHtml !== undefined,
      "the shipped snapshot must include non-JavaScript output");
    assert.ok(!auditedHtml.contents.includes("conditional payload"));
    assert.ok(shippedHtml.contents.includes("conditional payload"),
      "the shipped snapshot must expose the conditional HTML change");
    assert.notDeepEqual(shipped, audited.artifacts,
      "an HTML-only conditional plugin must change the complete shipped artifact");
  }
  finally {
    rmSync(root, { recursive: true, force: true });
  }
});

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

  // Three graph inputs are not checked source, and each is already accounted
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
  // which is issue #4780.
  //
  // `index.html` is the entry document. It is read by the bundler and gated by neither
  // half of the test below -- oxlint reads script, and the compiler has no account of a
  // document -- so it stays pinned here. What it may *contain* is a separate gate: it
  // carried an unchecked `<script>` block until #4783, and a gate above now fails unless
  // every element, attribute and URL scheme in a document this project owns is one it
  // lists as inert. So the script it can reach is a module under a lint target, or a
  // remote URL carrying an `integrity` digest -- which that gate requires precisely
  // because no compiler or lint here reads those bytes.
  //
  // `src/styles.css` is style content. It sits under a lint target, but oxlint reads
  // script and the compiler has no account of a stylesheet at all, so it cannot clear
  // either half of the test below. A browser will not execute it either.
  //
  // Pinning the exact list is what makes this fail closed. Anything else the build reads
  // changes it and fails -- including a second stylesheet, which is a small cost for a
  // gate that otherwise has to guess which extensions are harmless. Closing #4780 changes
  // it too, so the pin has to be deleted deliberately rather than quietly outliving the
  // gaps it describes.
  const knownReadButUngated = [
    "../annotated-source-viewer/src/document-model.js",
    "index.html",
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
    "the resolved build config declares direct Rolldown plugins outside Vite's plugin "
      + "list. `build.rollupOptions.plugins` is handed straight to Rolldown, and because "
      + "such a plugin transforms both builds alike the gate below sees nothing to "
      + "disagree about either");
  assert.deepStrictEqual(audited.rollupOutputPlugins, [],
    "the build declares Rolldown output plugins. Those run at generate time and can rewrite "
      + "a chunk after every gate above has read it, so this project keeps none");
  assert.equal(audited.workerPluginCount, 0,
    "the build declares project worker plugins. The audit injects its own worker plugin "
      + "to account for every nested worker graph; another plugin could add inputs outside "
      + "that accounting, so project worker plugins remain forbidden");
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

  // The list above names five rules, so it says nothing about a sixth. Round 4 (Sol,
  // both seats) suppressed `promise/always-return` inline at one of the two
  // `observeAsync` continuations, dropped its explicit return, and left the whole suite
  // green: the rule is still `deny` in the resolved config, and the directive is
  // genuinely used, so `reportUnusedDisableDirectives` has nothing to say either.
  //
  // What is pinned is therefore the set of rules this project suppresses inline at all,
  // rather than the sites. A twenty-seventh assertion in a test does not churn this;
  // switching off a newly adopted rule does.
  const suppressed = new Set(
    files.flatMap(file => [...readFileSync(file, "utf8").matchAll(directive)]
      .flatMap(match => ((match[1] ?? "").split("--")[0] ?? "")
        .split(",")
        .map(rule => rule.trim())
        .filter(rule => rule.length > 0))));

  assert.ok(suppressed.size > 0,
    "this scan found no directive at all, so it is passing without reading anything");
  assert.deepEqual([...suppressed].sort(), [
    "typescript/no-unnecessary-type-parameters",
    "typescript/no-unsafe-type-assertion",
  ], "an inline directive is stock analysis switched off for the code underneath it, and "
    + "no severity, category, option or override read elsewhere in this file can see one");
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

// oxlint's `plugins` key *replaces* its defaults rather than adding to them. Verified
// directly: with `"plugins": ["import"]` a file containing `x instanceof Array` draws no
// `unicorn/no-instanceof-array`, while the same file under the default set does. So the
// defaults oxlint enables on its own have to be re-listed alongside the added ones, and
// dropping one from that list silently retires a whole family of rules with every command
// green -- the same failure shape as a narrowed `include` glob.
//
// Round 1 (Sol, both seats) showed that checking the array for a name proves neither
// direction of that. `eslint` is not a toggle at all -- its 69 core rules stay enabled
// whether or not the list names it -- and `node` was listed while contributing zero rules
// at this project's categories, so the list asserted an adoption that was not happening.
// Both were removed, and the gate below now reads oxlint's own effective configuration
// instead of the file that is supposed to produce it.
const requiredOxlintRuleFamilies
  = ["eslint", "typescript", "unicorn", "oxc", "import", "jsdoc", "promise"] as const;

// `--print-config` is oxlint resolving categories, plugins, overrides and rule entries
// into the configuration it will actually run. Reading that output is what makes a
// dropped plugin, a narrowed category, a plugin that contributes nothing, or a named rule
// switched off fail here rather than read as a clean run.
//
// Overrides matter as much as the base. Round 2 (Sol, seat B) showed that oxlint keeps
// them as a separate array rather than folding them into `rules`, so an override scoped
// to `src/**/*.ts` can drop a plugin or silence a rule for every product source while
// anything reading the top-level object still sees the full set. They are read here too.
interface PrintedOxlintOverride {
  readonly files: readonly string[];
  readonly plugins?: readonly string[] | null;
  readonly rules?: Readonly<Record<string, string | readonly unknown[]>>;
  readonly env?: Readonly<Record<string, boolean>> | null;
  readonly globals?: Readonly<Record<string, string>> | null;
}

interface PrintedOxlintConfig {
  readonly categories: Readonly<Record<string, string>>;
  readonly rules: Readonly<Record<string, string | readonly unknown[]>>;
  readonly overrides?: readonly PrintedOxlintOverride[];
  readonly settings?: unknown;
  readonly env?: Readonly<Record<string, boolean>> | null;
  readonly globals?: Readonly<Record<string, string>> | null;
}

// `src/dotnet-inspect.ts` rather than an arbitrary file: it is the product source the two
// `observeAsync` continuations live in, so an override aimed at product code is in scope
// for this read.
function printedOxlintConfig(
  root: string,
  configPath?: string,
): PrintedOxlintConfig {
  const run = spawnSync(
    "npx",
    [
      "--no",
      "--",
      "oxlint",
      ...(configPath === undefined ? [] : ["-c", configPath]),
      "--print-config",
      "src/dotnet-inspect.ts",
    ],
    { cwd: root, encoding: "utf8" },
  );
  const output = run.stdout.trim();
  assert.ok(output.startsWith("{"),
    `oxlint printed no usable configuration: ${run.stderr || output || "no output"}`);

  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return JSON.parse(output) as PrintedOxlintConfig;
}

function severityOf(entry: string | readonly unknown[] | undefined): unknown {
  return Array.isArray(entry) ? entry[0] : entry;
}

// Everything after the severity. A rule left at `deny` stops reporting when its options
// exempt the code it was enabled for, and severity is all the reads above can see.
function optionsOf(entry: string | readonly unknown[] | undefined): readonly unknown[] {
  return Array.isArray(entry) ? entry.slice(1) : [];
}

// oxlint normalises severities to its own `deny`/`allow` spelling rather than echoing the
// `error` and `off` written in the config, so everything below compares those.
function enabledOxlintRuleFamilies(printed: PrintedOxlintConfig): Map<string, number> {
  const families = new Map<string, number>();
  for (const [rule, entry] of Object.entries(printed.rules)) {
    if (severityOf(entry) === "allow") {
      continue;
    }
    const family = rule.includes("/") ? rule.slice(0, rule.indexOf("/")) : "eslint";
    families.set(family, (families.get(family) ?? 0) + 1);
  }
  return families;
}

test("every plugin this project declares contributes rules oxlint actually runs", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const families = enabledOxlintRuleFamilies(printedOxlintConfig(root));

  for (const family of requiredOxlintRuleFamilies) {
    assert.ok((families.get(family) ?? 0) > 0,
      `oxlint runs no ${family} rule, so this project's analysis no longer covers that `
        + "family; the plugin list or the categories above have narrowed");
  }

  // The other direction. A name that enables nothing reads as adoption in the config and
  // in the README while changing no behaviour, which is exactly what `node` was doing.
  const declared = oxlintConfig.plugins ?? [];
  assert.notEqual(declared.length, 0,
    "the plugin list is what enables the added plugins; without it they do not run");
  for (const plugin of declared) {
    assert.ok((families.get(plugin) ?? 0) > 0,
      `${plugin} is declared but enables no rule at this project's categories, so it `
        + "claims an adoption that is not happening");
  }
});

// A family surviving does not mean the analysis this project describes survived. Round 2
// (Sol) got past the family counts three ways at once: `promise/always-return` off leaves
// five other `promise` rules; dropping the `suspicious` category leaves every family
// populated from `correctness` alone; and an override can retire a plugin for product
// code while the base object still lists it. Each is read directly here instead.
test("the lint runs the categories, plugins and named rules it claims", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const printed = printedOxlintConfig(root);

  assert.deepEqual(printed.categories, { correctness: "deny", suspicious: "deny" },
    "both categories are the adoption; dropping one leaves every rule family populated "
      + "from the other, so nothing else here would notice");

  assert.equal(severityOf(printed.rules["promise/always-return"]), "deny",
    "the README explains why the two `observeAsync` continuations return explicitly; "
      + "with the rule off that explanation describes a check that is not running");

  // Every override inherits the project-wide plugin set. One that declares its own
  // replaces it for the files it matches -- the same replacement semantics as the
  // top-level key, applied where no gate reading that key can see it.
  for (const override of printed.overrides ?? []) {
    assert.ok(override.plugins === null || override.plugins === undefined,
      `the override for ${override.files.join(", ")} declares its own plugin list, which `
        + "replaces the project-wide set for those files rather than adding to it");
  }
});

// The relaxations are the whole surface through which stock analysis gets weaker, so the
// set is pinned rather than each member. Round 2 (Sol, seat A) showed the same shape on
// the html-validate side: an assertion about one entry says nothing about a second one
// added beside it.
//
// Read from `--print-config` so that a relaxation counts however it is spelled and
// wherever it is written, including inside an override.
test("the oxlint configuration relaxes only the rules it documents", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const printed = printedOxlintConfig(root);
  const generatedTypeScriptFacadeScope = [
    ...generatedFacadeSources,
    "multi-facade-canary/facades/alpha.ts",
    "multi-facade-canary/facades/beta.ts",
    "managed-operation-bridge-canary/facades/bridge.ts",
  ].join(", ");
  const publishedFacadeScope = publishedFacadeModules.join(", ");

  const relaxed = Object.entries(printed.rules)
    .filter(([, entry]) => severityOf(entry) === "allow")
    .map(([rule]) => rule)
    .sort();

  // The README names these four: underscore spelling, function relocation, listener API
  // preference, and `Array.prototype.sort`, which prescribes the ES2023 `toSorted` while
  // this project targets ES2022.
  assert.deepEqual(relaxed, [
    "no-underscore-dangle",
    "unicorn/consistent-function-scoping",
    "unicorn/no-array-sort",
    "unicorn/prefer-add-event-listener",
  ], "a rule turned off here is stock analysis this project stops doing, so it needs a "
    + "stated reason in the README rather than a quiet config line");

  const scopedRelaxations = (printed.overrides ?? []).map(override => [
    override.files.join(", "),
    Object.entries(override.rules ?? {})
      .filter(([, entry]) => severityOf(entry) === "allow")
      .map(([rule]) => rule)
      .sort(),
  ]);

  // The scoped exceptions are the generated TypeScript handoffs and the production
  // facade's compiler-derived publish artifact. The TypeScript sources are compiled
  // separately against the SDK-owned runtime declaration; the JavaScript import resolves
  // only after Wasm publish. Authored source keeps the complete rule set held by the
  // gates above.
  assert.deepEqual(scopedRelaxations, [
    ["scripts/*.ts, test/**/*.ts, **/vite.config.ts", []],
    [publishedFacadeScope, [
      "typescript/no-unsafe-argument",
      "typescript/no-unsafe-assignment",
      "typescript/no-unsafe-call",
      "typescript/no-unsafe-member-access",
      "typescript/no-unsafe-return",
      "typescript/use-unknown-in-catch-callback-variable",
    ]],
    [generatedTypeScriptFacadeScope, [
      "typescript/no-redundant-type-constituents",
      "typescript/no-unsafe-argument",
      "typescript/no-unsafe-assignment",
      "typescript/no-unsafe-call",
      "typescript/no-unsafe-member-access",
      "typescript/no-unsafe-return",
      "typescript/no-unsafe-type-assertion",
    ]],
  ], "an override is the other place a rule can be turned off, and the top-level list "
    + "above cannot see it");

  // Off is not the only way down. Round 3 (Sol, seat A) left `eslint/no-unused-vars` at
  // `deny` and gave it `argsIgnorePattern: ".*"`, which reported nothing while every
  // severity read above -- the category map, the family counts, the two lists here --
  // was unchanged. Options are pinned as one set for the same reason the relaxations
  // are: an assertion naming today's option-bearing rules says nothing about options
  // added to a rule beside them.
  const configuredOptions = Object.fromEntries([
    ...Object.entries(printed.rules).map(([rule, entry]) => [rule, entry] as const),
    ...(printed.overrides ?? []).flatMap(override =>
      Object.entries(override.rules ?? {})
        .map(([rule, entry]) => [`${override.files.join(", ")} :: ${rule}`, entry] as const)),
  ].filter(([, entry]) => optionsOf(entry).length > 0));

  // Two exceptions this project configures. `node:test` returns a promise nobody is
  // expected to await, so `test(...)` at the top level of a test file is not a floating
  // promise. Prism ships each language grammar as a module whose only effect is
  // registering itself onto the core, so there is nothing to bind and the import is
  // unassigned by construction; the allowance names that path and nothing else, so an
  // unassigned import anywhere outside `prismjs/components/` still reports.
  assert.deepEqual(configuredOptions, {
    "import/no-unassigned-import": ["deny", [{ allow: ["prismjs/components/*"] }]],
    "typescript/no-floating-promises": ["deny", [{
      allowForKnownSafeCalls: [{ from: "package", name: "test", package: "node:test" }],
    }]],
  }, "an option that exempts code from an enabled rule is the same loss of coverage as "
    + "turning it off, and leaves every severity in this file reading exactly as before");

  // Plugin settings are a third way down, beside severities and options, and they reach
  // rules wholesale rather than one at a time. Round 4 (Sol, seat A) set
  // `settings.jsdoc.ignorePrivate`, which exempts every `@private` symbol from the whole
  // jsdoc family at once while categories, families, severities and options all read
  // exactly as before.
  //
  // Compared against oxlint's own resolution of an empty config rather than a copied
  // literal: the claim is that this project changes no setting, and stating it
  // differentially means an oxlint release that adds or renames a plugin's settings block
  // does not churn this assertion.
  const stock = join(mkdtempSync(join(tmpdir(), "oxlint-stock-")), "stock.json");
  writeFileSync(stock, "{}\n");
  try {
    assert.deepEqual(printed.settings, printedOxlintConfig(root, stock).settings,
      "this project configures a plugin setting; settings exempt whole families of rules "
        + "without changing any severity, option or category read above, so a deliberate "
        + "one belongs in the README with the other documented relaxations");
  } finally {
    rmSync(dirname(stock), { force: true, recursive: true });
  }

  // Severities, options and settings all describe what the rules do. The environment
  // describes what they can see, and a rule that sees nothing reports nothing.
  // `eslint/no-global-assign` fires only on a name the configuration calls a read-only
  // global, so there are two ways to silence it without touching a severity: re-declare
  // the name as writable through `globals`, or remove it from the environment by dropping
  // the `env` that supplied it. Round 4 (Sol, seat A) found the first with
  // `globals: { document: "writable" }`; the second turned out to be the same hole
  // through the neighbouring key, since deleting `browser` from `env` silences the
  // identical assignment.
  //
  // Both scopes are read, because an override carries `env` and `globals` too and round 2
  // established that an override is exactly where a relaxation goes to stay invisible.
  // Pinned as one map rather than as four assertions, for the reason the relaxation sets
  // are: naming today's environment says nothing about a `globals` block added to an
  // override beside it.
  const environments = Object.fromEntries([
    ["<top level>", {
      env: printed.env ?? {},
      globals: printed.globals ?? {},
    }] as const,
    ...(printed.overrides ?? []).map(override => [
      override.files.join(", "),
      { env: override.env ?? {}, globals: override.globals ?? {} },
    ] as const),
  ]);

  assert.deepEqual(environments, {
    "<top level>": {
      env: { browser: true, es2022: true },
      globals: {},
    },
    "scripts/*.ts, test/**/*.ts, **/vite.config.ts": {
      env: { browser: false, node: true },
      globals: {},
    },
    [publishedFacadeScope]: {
      env: {},
      globals: {},
    },
    [generatedTypeScriptFacadeScope]: {
      env: {},
      globals: {},
    },
  }, "the environment decides which names the enabled rules treat as globals, so a "
    + "`globals` entry or a dropped `env` narrows a rule as effectively as turning it "
    + "off, and leaves every category, family, severity, option and setting read above "
    + "reading exactly as before");
});

// Documents are the one kind of authored file every gate above is blind to: the compiler
// builds a program out of `.ts` files and oxlint is handed a list of source paths, so
// nothing in this project read `index.html` at all until html-validate was wired in.
//
// The whole invocation is read out of the `lint` script rather than restated, so a lint
// that stops covering an extension, or stops pinning its configuration, fails here
// instead of quietly narrowing. Round 1 (Sol, both seats) landed a nested
// `.htmlvalidate.json` that turned rules off for its own subtree while the specimens --
// all written at the project root -- stayed green; `--config` is what makes the committed
// file the one that runs, and reading the flag from the script is what keeps it there.
const htmlValidateInvocation = (() => {
  const lint = packageJson.scripts?.lint ?? "";
  const match = /html-validate\s+--config\s+(\S+)\s+"([^"]+)"/u.exec(lint);
  return match === null
    ? undefined
    : { config: match[1] ?? "", glob: match[2] ?? "" };
})();

const htmlDocumentExtensions = [".html", ".htm", ".xhtml"] as const;

interface HtmlValidateReport {
  readonly filePath: string;
  readonly messages: readonly { readonly ruleId: string }[];
}

// Running the committed configuration rather than reading it. A rule that is listed but
// no longer exists, a preset that stops being resolved, and a linter that is not
// installed at all are indistinguishable from "clean" to anything that only parses JSON.
//
// `--no` is what makes the third case fail here: without it npx reaches for the registry
// when the binary is missing, so an uninstalled linter reads as a slow gate rather than a
// broken one. The `--` keeps npx from claiming `--formatter` for itself.
//
// The `--config` flag comes from the lint script, so these specimens are checked by the
// same configuration resolution `npm run lint` performs rather than by whatever
// html-validate would discover on its own.
function htmlValidateRules(root: string, targets: readonly string[]): Set<string> {
  assert.ok(htmlValidateInvocation !== undefined,
    "the lint script must invoke html-validate with a pinned --config and a quoted glob");
  const run = spawnSync(
    "npx",
    [
      "--no",
      "--",
      "html-validate",
      "--config",
      htmlValidateInvocation.config,
      "--formatter=json",
      ...targets,
    ],
    { cwd: root, encoding: "utf8" },
  );
  const output = run.stdout.trim();
  assert.ok(output.startsWith("["),
    `html-validate produced no usable report: ${run.stderr || output || "no output"}`);

  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const report = JSON.parse(output) as readonly HtmlValidateReport[];
  return new Set(report.flatMap(file => file.messages.map(message => message.ruleId)));
}

// A specimen is written beside the real documents rather than into a temporary directory
// on purpose: `.htmlvalidate.json` sets `root: true` and applies to this directory tree,
// so a specimen anywhere else would be checked by html-validate's built-in defaults and
// would prove nothing about the configuration this project actually commits.
function withSpecimen<T>(
  root: string,
  name: string,
  markup: string,
  body: (relativePath: string) => T,
): T {
  const full = join(root, name);
  const directory = dirname(full);
  // Round 4 (Opus, seat B): removing the file but not the directory left an empty
  // `src/dist` in the working tree after every run, because round 1's fix moved a
  // specimen into a directory this project does not otherwise have. Only a directory
  // this helper created is removed, so a specimen written beside real files cannot take
  // them with it.
  const created = !existsSync(directory);
  mkdirSync(directory, { recursive: true });
  writeFileSync(full, markup);
  try {
    return body(projectRelative(root, full));
  } finally {
    rmSync(full, { force: true });
    if (created) {
      rmSync(directory, { force: true, recursive: true });
    }
  }
}

test("the lint hands every document extension it claims to cover to html-validate", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  assert.ok(htmlValidateInvocation !== undefined,
    "the lint script must invoke html-validate with a pinned --config and a quoted glob");
  const { glob } = htmlValidateInvocation;

  // Nested, and one specimen per extension. The previous glob in this project's history
  // matched `.html` alone while the prose claimed four extensions, so `npm run lint`
  // passed markup that a later gate rejected -- the divergence that teaches people to
  // distrust the fast check.
  //
  // `src/dist` is one of the placements: round 1 (Sol, seat B) showed that an unanchored
  // `dist` ignore entry matches a directory of that name at any depth, so an authored
  // document under `src/dist` was excluded from linting while the inventory walk -- which
  // prunes `dist` only at the project root -- still counted it as covered.
  const placements = ["src", join("src", "dist")];
  for (const extension of htmlDocumentExtensions) {
    for (const placement of placements) {
      const reported = withSpecimen(
        root,
        join(placement, `toolchain-specimen${extension}`),
        "<!doctype html>\n<html lang=\"en\"><head><title>x</title></head>"
          + "<body><div></body></html>\n",
        () => htmlValidateRules(root, [glob]),
      );
      assert.ok(reported.has("close-order"),
        `the lint glob does not reach a ${extension} document under ${placement}`);
    }
  }
});

// The glob and the inventory walk have to agree on which documents exist, and two things
// can pull them apart. Round 1 (Sol, both seats) demonstrated each: a descendant
// `.htmlvalidate.json` replaces the committed rules for its own subtree, and a descendant
// `.htmlvalidateignore` removes documents from the run outright. `--config` closes the
// first, but neither flag closes the second, and neither is visible to any specimen
// written somewhere else.
//
// So the tree may hold exactly one of each, at the root, where the gates below read them.
// This is derived from a walk rather than a list of known placements: a file added
// anywhere fails, without anyone having to predict where.
test("html-validate reads one configuration and one ignore file for the whole tree", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const controlFiles = [".htmlvalidate.json", ".htmlvalidateignore"];
  const found: string[] = [];
  const walk = (directory: string): void => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const full = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!isGenerated(directory, entry.name, root)) {
          walk(full);
        }
      } else if (controlFiles.includes(entry.name)) {
        found.push(projectRelative(root, full));
      }
    }
  };
  walk(root);

  assert.deepEqual(found.sort(), [".htmlvalidate.json", ".htmlvalidateignore"],
    "a configuration or ignore file below the project root applies to its own subtree "
      + "only, so it weakens or silences linting for documents no gate here would notice");
});

// html-validate expands `**` with dot-directories excluded, so a document under a dotted
// path is never handed to it however the ignore file reads. The inventory walk has no
// such rule, so it counts that document as covered and the two disagree -- which is how
// round 1 (Sol, both seats) put invalid markup through `npm run lint`, `npm run build`
// and the whole suite with everything green.
//
// Keeping authored documents out of dotted directories is what makes the glob's reachable
// set equal the inventory. Nothing this project ships needs one, so this closes the gap
// where it starts rather than trying to widen a glob to match a walk.
test("no authored document sits where the lint glob cannot reach it", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const documents = projectSourceFiles(root, htmlDocumentExtensions, unprunedRoots)
    .map(file => projectRelative(root, file));

  const unreachable = documents
    .filter(document => document.split("/").some(segment => segment.startsWith(".")));

  assert.deepEqual(unreachable, [],
    "`**` does not descend into dotted directories, so this document is linted by "
      + "nothing while the inventory walk still reports it as covered");

  // The same disagreement, reached by case rather than by placement. Node matches glob
  // patterns case-insensitively on macOS and Windows and case-sensitively everywhere
  // else -- `nocase: isWindows || isMacOS` in `lib/internal/fs/glob.js`, which is what
  // `html-validate`'s CLI calls -- while the walk above lowercases before comparing
  // extensions. So `probe.HTML` is counted as covered here and linted on a developer's
  // Mac, and is silently skipped on the Ubuntu runners that gate merges and deploy the
  // site. Round 3 (Sol, seat A) found this; the extension is normalised at the source
  // rather than the glob widened to spell every case variant.
  const misCased = documents
    .filter(document => extname(document) !== extname(document).toLowerCase());

  assert.deepEqual(misCased, [],
    "this document's name is not lowercase, so the lint glob reaches it on macOS and "
      + "Windows but not on the Linux runners, where it would be checked by nothing");
});

// The configuration gates above all read files. A directive reads nothing: it is written
// in the document itself, and it turns a rule off exactly where that rule was about to
// report. Round 3 (Sol, both seats) used both halves of the gap -- widening this project's
// one directive from `disable-next` to file-wide `disable`, which silences the rule for
// every element below it, and adding a second directive next to a fresh violation.
// Neither is visible to `no-unused-disable`, because both suppressions are genuinely used.
//
// So the directives are inventoried and pinned as a set, action included. This project
// needs exactly one, for one element, for one rule.
test("authored documents carry only the one suppression this project explains", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const documents = projectSourceFiles(root, htmlDocumentExtensions, unprunedRoots);
  assert.ok(documents.length > 0,
    "this project owns no documents, so the inventory below proves nothing");

  const directive
    = /html-validate-(?<action>disable-next|disable-block|disable|enable)(?<rules>[^\]\r\n]*)/gu;
  const found = documents.flatMap((file) => {
    const document = projectRelative(root, file);
    return [...readFileSync(file, "utf8").matchAll(directive)].map((match) => {
      const { action = "", rules = "" } = match.groups ?? {};
      // Everything before `--` is the rule list; the rest is the required explanation.
      const named = (rules.split("--")[0] ?? "").trim();
      return `${document}: ${action} ${named}`.trimEnd();
    });
  }).sort();

  assert.deepEqual(found, [
    "index.html: disable-next element-required-attributes",
  ], "a directive is stock analysis switched off for the markup underneath it; a second "
    + "one, a different rule, or a wider action than `disable-next` is a rule this "
      + "project stopped running with nothing else here reporting the change");
});

// Every gate above reasons about where a control file may sit, which extension a glob
// reaches, and which directory an ignore entry anchors to. Round 4 (Sol, seat B) showed
// the limit of that approach: html-validate resolves `.htmlvalidateignore` by walking
// *upward* from each document, so a file at `prototypes/.htmlvalidateignore` -- one
// directory above this project, still inside the repository -- excluded an authored
// document and took `npm run lint` from exit 1 to exit 0 with all gates green. `root:
// true` stops configuration merging; it does not stop ignore discovery, and a walk that
// only descends can never see an ancestor.
//
// So rather than enumerating another placement, this asks html-validate which documents
// it actually read. `--dump-source` prints one `Source <path>` header per processed file,
// under the same `--config` and glob the lint uses, which makes the answer authoritative:
// an ancestor ignore, a descendant ignore, a dotted directory, an uppercase extension and
// a narrowed glob all show up here as a document the inventory has and the linter does
// not. The gates above still run, because each names its cause; this one states the
// property they exist to protect.
test("html-validate reads exactly the documents this project owns", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  assert.ok(htmlValidateInvocation !== undefined,
    "the lint script must invoke html-validate with a pinned --config and a quoted glob");

  const run = spawnSync(
    "npx",
    [
      "--no",
      "--",
      "html-validate",
      "--config",
      htmlValidateInvocation.config,
      "--dump-source",
      htmlValidateInvocation.glob,
    ],
    { cwd: root, encoding: "utf8" },
  );

  const read = [...run.stdout.matchAll(/^Source (?<path>.+?)@\d+:\d+/gmu)]
    .map(match => projectRelative(root, match.groups?.path ?? ""))
    .sort();
  const owned = projectSourceFiles(root, htmlDocumentExtensions, unprunedRoots)
    .map(file => projectRelative(root, file))
    .sort();

  assert.ok(owned.length > 0,
    "this project owns no documents, so this comparison proves nothing");

  // The whole-glob read above answers the *extras* direction: a document html-validate
  // processed that this project does not own. It cannot answer the direction that
  // matters, because `--dump-source` prints each document's full text after its header
  // and the headers are recovered from that same stream. Round 4 (Opus, seat B) wrote a
  // document whose body contained a well-formed `Source <path>@1:1` line naming a file an
  // ancestor `.htmlvalidateignore` had excluded: the set matched, `npm run lint` exited
  // 0, and `index.html` carried an unreported `<img>` with no `alt`. An oracle recovered
  // by pattern-matching the data it is measuring is not an oracle.
  //
  // Asking per document removes the channel instead of hardening the pattern. When
  // html-validate is handed one path, the only document text that can reach stdout is
  // that document's own, and it reaches stdout only if the file was opened -- an ignored
  // path prints `No files matching patterns` and nothing else. Requiring a delimiter
  // after the header would not have helped; an author can write both lines.
  //
  // The header must *name the document asked about*, not merely exist. Round 5 (Sol,
  // seat A) showed why: html-validate expands its path arguments as globs, so probing a
  // document whose name contains glob metacharacters opens a different file. An ignored
  // `src/[a].html` probed by name returns the header for `src/a.html`, and a test that
  // only asked "is there a header" read that as coverage. Comparing identity closes it
  // without enumerating which characters are dangerous. The first header is the one
  // html-validate printed before any document text, so it cannot be forged from a body.
  const skipped = owned.filter((document) => {
    const probe = spawnSync(
      "npx",
      [
        "--no",
        "--",
        "html-validate",
        "--config",
        htmlValidateInvocation.config,
        "--dump-source",
        document,
      ],
      { cwd: root, encoding: "utf8" },
    );
    const header = /^Source (?<path>.+?)@\d+:\d+/mu.exec(probe.stdout);
    const opened = header === null
      ? undefined
      : projectRelative(root, header.groups?.path ?? "");
    return opened !== document;
  });

  assert.deepEqual(skipped, [],
    "html-validate was handed this document on its own and did not open it -- either an "
      + "ignore file somewhere above, beside or below this project excludes it, or its "
      + "name expanded as a glob onto a different file, and `npm run lint` reports clean "
      + "over markup nothing read");
  assert.deepEqual(read, owned,
    "html-validate processed a different set of documents than this project owns, so "
      + "`npm run lint` is reporting clean over markup nothing checked");
});

test("the committed html-validate configuration rejects what it is kept for", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));

  // Each specimen names the rule that must reject it. Asserting the rule id rather than
  // "some error" is what makes a preset that stops resolving, or an option edited to
  // narrow a rule, fail here rather than pass as a clean run.
  const specimens = [
    ["close-order", "<body><div></body>"],
    ["element-required-attributes", "<body><img alt=\"x\" /></body>"],
    ["wcag/h37", "<body><img src=\"/x.png\" /></body>"],
    [
      "require-sri",
      "<head><script src=\"https://cdn.example/x.js\" "
        + "crossorigin=\"anonymous\"></script></head>",
    ],
    ["attribute-allowed-values", "<body><input type=\"nonsense\" /></body>"],
  ] as const;

  for (const [rule, fragment] of specimens) {
    const reported = withSpecimen(
      root,
      "toolchain-specimen.html",
      `<!doctype html>\n<html lang="en"><head><title>x</title></head>\n${fragment}\n`
        + "</html>\n",
      specimen => htmlValidateRules(root, [specimen]),
    );
    assert.ok(reported.has(rule),
      `the committed configuration no longer rejects ${rule}; reported `
        + ([...reported].join(", ") || "nothing"));
  }
});

// `require-sri` is the one rule this project configures away from its default, and the
// reason is the only same-origin case that would otherwise fail: the local stylesheet and
// the module entry point are files Vite emits, not third-party bytes to pin.
//
// The whole `rules` object is pinned, not that one entry. Round 2 (Sol, seat A) added
// `"no-dup-id": "off"` beside it and kept `npm run lint` and all 35 tests green: an
// assertion about one key says nothing about a second one added next to it, and every
// specimen below names a rule that was still on. Pinning the object makes any further
// relaxation of the stock presets land here.
test("html-validate still demands a digest on third-party bytes", () => {
  assert.deepEqual(htmlValidateConfig.rules, {
    "require-sri": ["error", { target: "crossorigin" }],
  }, "the only intended relaxation is same-origin; `target: all`, the rule being off, or "
    + "a second rule configured beside it are all different properties");
  assert.deepEqual([...(htmlValidateConfig.extends ?? [])],
    ["html-validate:standard", "html-validate:document", "html-validate:a11y"],
    "the presets are what this adoption is for; narrowing them is not a config tweak");
  assert.equal(htmlValidateConfig.root, true,
    "without this html-validate walks up and merges configuration from outside the "
      + "project, so the committed file is not the one that runs");

  // Rules are not the only way this file weakens the presets. Round 3 (Sol, both seats)
  // added an `elements` entry that dropped `<button>`'s `type` metadata: the presets
  // still resolved, every rule above was still on, and `attribute-allowed-values` simply
  // had nothing left to check that element against. `plugins`, `transform` and `aria`
  // reach the same place by other routes, so the key set is pinned rather than the three
  // keys that happen to be interesting.
  assert.deepEqual(Object.keys(htmlValidateConfig).sort(),
    ["$schema", "extends", "root", "rules"],
    "a key here that is not one of these -- `elements`, `plugins`, `transform`, `aria` "
      + "-- changes what the stock presets are checking against without changing any "
      + "rule, preset or severity the assertions above read");
});

// html-validate drops an ignored file silently when other targets remain, so an authored
// document added to this file would leave analysis with every gate green. Both entries
// are build or dependency output; nothing authored may be listed.
//
// The entries are *not* a mirror of the inventory walk, and round 4 (Opus, seat B) was
// right to object to an earlier comment here that said they were. The walk exempts
// anything under `public/`, `src/`, `test/` and `scripts/` outright and prunes `bin` and
// `obj` only beside a `.csproj`; these entries match at any depth unconditionally, so the
// ignore file is strictly broader. What licenses the comparison is containment in the
// safe direction -- everything the walk prunes is also ignored -- so no owned document is
// ever measured against a file the linter refused to open. Where they diverge the set
// comparison fails, which is the loud outcome. `dist` is the one anchored entry, because
// it is generated at the project root only and round 1 (Sol, seat B) showed the
// unanchored spelling silently excluding an authored `src/dist`.
test("the html-validate ignore file names only generated directories", () => {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const ignored = readFileSync(join(root, ".htmlvalidateignore"), "utf8")
    .split("\n")
    .map(line => line.trim())
    .filter(line => line !== "");

  assert.deepEqual(ignored, ["/dist", "node_modules", "bin", "obj"],
    "every entry here must be generated output, and must stay a superset of what the "
      + "inventory walk prunes; an entry that covers authored markup hides it from the "
      + "lint");

  // Anything the walk reports is a document the lint is expected to reach. A new entry
  // above that covered authored markup would make this list non-empty rather than making
  // the lint quietly smaller.
  const documents = projectSourceFiles(root, htmlDocumentExtensions, unprunedRoots)
    .map(file => projectRelative(root, file));
  assert.ok(documents.length > 0,
    "this project owns no documents, so the gates above prove nothing");
  for (const document of documents) {
    for (const entry of ignored) {
      const directory = entry.replace(/^\//u, "");
      assert.ok(
        entry.startsWith("/")
          ? !document.startsWith(`${directory}/`)
          : !document.split("/").slice(0, -1).includes(directory),
        `${document} sits under an ignored directory and is linted by nothing`);
    }
  }
});


test("static hosting serves credits links through the application entry point", () => {
  const creditsRoutes = staticWebAppConfig.routes
    .filter(route => route.route === "/credits" || route.route === "/credits/");

  // Azure Static Web Apps normalizes a trailing slash away when matching routes, so a
  // separate "/credits/" rule collides with "/credits" and fails deployment (#4634,
  // reintroduced by #5039 and refixed here). One rule covers both forms.
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

test("static hosting sends its security headers on every static response", () => {
  // These are response-header protections, so nothing in the source tree can stand in for
  // them: a linter reads the markup this project ships, while these constrain what a
  // browser will do with it once shipped. `nosniff` stops content-type guessing on the
  // JSON, TSV and wasm this site serves, and the other three are the cheap defaults that
  // need no coordination with page content.
  //
  // "static" in this test's name is load-bearing. Azure Static Web Apps does not apply
  // `globalHeaders` to responses produced by the managed functions under `/api/*`; those
  // carry whatever headers the function itself sets. So this covers the static site and
  // says nothing about the MSDL proxy's responses, which is why the name does not claim
  // "every response".
  assert.deepEqual(staticWebAppConfig.globalHeaders, {
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
    "X-Frame-Options": "DENY",
    "Strict-Transport-Security": "max-age=63072000; includeSubDomains",
  });

  // Azure Static Web Apps returns the union of `globalHeaders` and a matching route's
  // `headers`, with the route winning per key. A route that names one of these keys
  // therefore replaces the global value for its own paths, and the response says nothing
  // about the substitution -- the file still reads as though the protection is global.
  // Requiring the two key sets to stay disjoint is what keeps "on every static response"
  // in this test's name true, and it is a property of the config rather than an
  // enumeration of the ways a route could weaken one.
  // Compared case-insensitively because HTTP header names are. A route spelling
  // `x-frame-options` overrides a global `X-Frame-Options` on the wire, so a
  // case-sensitive comparison here would call that pair disjoint and miss the one thing
  // this assertion exists to catch.
  const globalKeys = new Set(
    Object.keys(staticWebAppConfig.globalHeaders).map(header => header.toLowerCase()),
  );
  const overriding = staticWebAppConfig.routes
    .filter(route => Object.keys(route.headers ?? {})
      .some(header => globalKeys.has(header.toLowerCase())))
    .map(route => route.route);

  assert.deepEqual(overriding, [],
    "this route sets a header that `globalHeaders` also sets, and Azure Static Web Apps "
      + "lets the route value win, so paths under it would carry a weaker policy than "
      + "this file appears to apply everywhere");

  // Key disjointness is necessary but not sufficient, because a redirect route drops the
  // global headers without naming any of them. Azure has acknowledged this since 2022
  // (Azure/static-web-apps#739): a route with `redirect` returns neither `globalHeaders`
  // nor its own `headers`. Such a route passes the disjointness check above while serving
  // a 302 with none of the four headers on it, which is exactly the silent weakening this
  // test exists to prevent. There are no redirect routes today; this keeps it that way
  // rather than waiting for one to be added and quietly punch a hole.
  const redirecting = staticWebAppConfig.routes
    .filter(route => route.redirect !== undefined)
    .map(route => route.route);

  assert.deepEqual(redirecting, [],
    "Azure Static Web Apps omits `globalHeaders` on redirect responses "
      + "(Azure/static-web-apps#739), so this route would answer without any of the four "
      + "headers while the config still reads as though they are global; serve the "
      + "redirect from a route that does not use `redirect`, or narrow this test's claim "
      + "deliberately");
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
      + "oxlint --no-ignore --disable-nested-config src test browser scripts "
      + "multi-facade-canary/coordinator.ts multi-facade-canary/exercise.ts "
      + "multi-facade-canary/facades "
      + "managed-operation-bridge-canary/initialize.ts "
      + "managed-operation-bridge-canary/exercise.ts "
      + "managed-operation-bridge-canary/facades engine/facades "
      + `${publishedFacadeModules.join(" ")} vite.config.ts `
      + "playwright.config.ts && "
      + "html-validate --config .htmlvalidate.json \"**/*.{html,htm,xhtml}\" && "
      + "node scripts/check-no-cross-origin-subresources.ts",
  );
});

test("the lint gate includes all compiler-derived facade artifacts", () => {
  for (const module of facadeModules) {
    assert.ok(
      !(oxlintConfig.ignorePatterns ?? []).includes(`src/facades/${module}.d.ts`),
    );
  }
  // Reading the script through an index signature makes its absence a real possibility
  // rather than a silent `undefined` handed to `assert.match`, which would fail with a
  // type error about the argument instead of naming the missing script.
  const lintScript = packageJson.scripts.lint;
  assert.ok(lintScript !== undefined, "package.json must define a lint script");
  assert.match(lintScript, /(?:^| )src(?: |$)/);
  assert.match(
    lintScript,
    /(?:^| )multi-facade-canary\/coordinator\.ts(?: |$)/,
  );
  assert.match(
    lintScript,
    /(?:^| )multi-facade-canary\/exercise\.ts(?: |$)/,
  );
  assert.match(lintScript, /(?:^| )multi-facade-canary\/facades(?: |$)/);
  assert.match(lintScript, /(?:^| )engine\/facades(?: |$)/);
  for (const module of publishedFacadeModules) {
    assert.ok(
      new RegExp(`(?:^| )${module.replaceAll(/[./]/g, String.raw`\$&`)}(?: |$)`)
        .test(lintScript),
      `the lint gate does not name ${module}`);
  }
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

// The containment guard replaced a weekly network check with a per-PR one, so it is the
// only thing standing behind "the shipped documents reach no origin but their own".
// Round 1 (Sol, seats A and B) found it enforcing a narrower property than it claimed:
// it examined only the elements Subresource Integrity applies to, and it resolved
// relative URLs against the origin rather than the document's `<base>`. Both were
// reachable by ordinary markup that passed html-validate. These cases pin the claim
// itself -- every load the browser performs -- rather than the element list that
// happened to implement it.
async function scanFixture(documents: Readonly<Record<string, string>>) {
  const root = mkdtempSync(join(tmpdir(), "cross-origin-"));
  try {
    for (const [name, contents] of Object.entries(documents)) {
      const file = join(root, name);
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, contents);
    }
    return await scan(root);
  } finally {
    rmSync(root, { force: true, recursive: true });
  }
}

const sameOriginPage = (body: string) =>
  `<!DOCTYPE html><html lang="en"><head><title>t</title>`
  + `<link rel="stylesheet" href="/src/styles.css">`
  + `</head><body>${body}</body></html>`;

test("the containment guard sees every load, not only the SRI-eligible ones", async () => {
  // Each of these is a real browser fetch that carries no integrity attribute, so an
  // SRI-shaped element list cannot see any of them.
  const cases = [
    ['<img alt="p" src="https://cdn.example/p.png">', "img"],
    ['<iframe src="https://cdn.example/f.html"></iframe>', "iframe"],
    ['<video poster="https://cdn.example/p.jpg"></video>', "video"],
    ['<object data="https://cdn.example/o.swf"></object>', "object"],
    ['<img alt="p" src="/a.png" srcset="/a.png 1x, https://cdn.example/b.png 2x">', "img"],
  ] as const;

  for (const [markup, element] of cases) {
    const result = await scanFixture({ "index.html": sameOriginPage(markup) });
    const crossOrigin = result.subresources.filter(subresource => subresource.crossOrigin);
    assert.equal(crossOrigin.length, 1, `expected one cross-origin load for ${markup}`);
    assert.equal(crossOrigin[0]?.element, element);
    assert.equal(crossOrigin[0]?.url.startsWith("https://cdn.example/"), true);
  }
});

test("the containment guard resolves relative URLs against the document base", async () => {
  // `index.html` already ships a `<base href="/">`, so this is the shipped shape rather
  // than a hypothetical one. A base pointing elsewhere redirects every relative fetch in
  // the document without any URL in the markup looking cross-origin.
  const redirected = await scanFixture({
    "index.html":
      `<!DOCTYPE html><html lang="en"><head><title>t</title>`
      + `<base href="https://cdn.example/assets/">`
      + `<script src="probe.js"></script>`
      + `</head><body></body></html>`,
  });

  const script = redirected.subresources.find(subresource => subresource.element === "script");
  assert.equal(script?.url, "https://cdn.example/assets/probe.js");
  assert.equal(script?.crossOrigin, true);
  assert.equal(redirected.baseUrls[0]?.crossOrigin, true);

  const local = await scanFixture({
    "index.html":
      `<!DOCTYPE html><html lang="en"><head><title>t</title>`
      + `<base href="/"><script src="probe.js"></script>`
      + `</head><body></body></html>`,
  });
  assert.equal(local.subresources.some(subresource => subresource.crossOrigin), false);
});

test("the containment guard reads CSS it cannot otherwise see through", async () => {
  // A stylesheet's `url()` is a fetch that no markup parse can reach. The guard asserts
  // the construct is absent rather than growing a CSS parser, so introducing one is a
  // deliberate change here instead of a silent hole.
  const inStylesheet = await scanFixture({
    "index.html": sameOriginPage(""),
    "src/styles.css": ".x { background: url(https://cdn.example/b.png); }",
  });
  assert.deepEqual(inStylesheet.cssFetches, ["src/styles.css"]);

  const inline = await scanFixture({
    "index.html": sameOriginPage('<div style="background:url(https://cdn.example/b.png)"></div>'),
  });
  assert.equal(inline.cssFetches.length, 1);

  const clean = await scanFixture({
    "index.html": sameOriginPage(""),
    "src/styles.css": ".x { color: red; }",
  });
  assert.deepEqual(clean.cssFetches, []);
});

test("the containment guard ignores inert markup and non-fetching URLs", async () => {
  // Reporting these would make the guard cry wolf, and a guard that cries wolf gets
  // relaxed. `<template>` and `<noscript>` content is not fetched on load, `data:` and
  // `#fragment` reach no network, and a link is a destination rather than a load.
  const result = await scanFixture({
    "index.html": sameOriginPage(
      '<noscript><img alt="p" src="https://cdn.example/a.png"></noscript>'
      + '<template><img alt="p" src="https://cdn.example/b.png"></template>'
      + '<img alt="p" src="data:image/gif;base64,R0lGOD">'
      + '<a href="https://example.com/docs">docs</a>'),
  });
  assert.deepEqual(result.subresources.filter(subresource => subresource.crossOrigin), []);
  assert.deepEqual(result.cssFetches, []);
});

test("the containment guard holds the shipped documents", async () => {
  // The non-vacuity claim: the real project must present documents and subresources to
  // examine, so a passing run cannot mean the extraction stopped seeing markup.
  const root = resolve(fileURLToPath(new URL("../", import.meta.url)));
  const result = await scan(root);
  assert.ok(result.documents.length > 0, "no documents discovered");
  assert.ok(result.subresources.length > 0, "no subresources discovered");
  assert.deepEqual(result.subresources.filter(subresource => subresource.crossOrigin), []);
  assert.deepEqual(result.baseUrls.filter(base => base.crossOrigin), []);
  assert.deepEqual(result.cssFetches, []);
});
