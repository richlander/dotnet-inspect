import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  supportedAnalysisHosts,
  verifyAnalysisHost,
} from "../scripts/verify-analysis-host.js";
import { verifySiteArtifact } from "../scripts/verify-site-artifact.js";

const packageLock = JSON.parse(
  readFileSync(new URL("../package-lock.json", import.meta.url), "utf8"),
);
const packageJson = JSON.parse(
  readFileSync(new URL("../package.json", import.meta.url), "utf8"),
);
const oxlintConfig = JSON.parse(
  readFileSync(new URL("../.oxlintrc.json", import.meta.url), "utf8"),
);
const browserTsconfig = JSON.parse(
  readFileSync(new URL("../tsconfig.json", import.meta.url), "utf8"),
);
const testTsconfig = JSON.parse(
  readFileSync(new URL("tsconfig.json", import.meta.url), "utf8"),
);
const staticWebAppConfig = JSON.parse(
  readFileSync(new URL("../staticwebapp.config.json", import.meta.url), "utf8"),
);
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
    "tsc --noEmit && tsc --noEmit -p test/tsconfig.json",
  );
});

test("static hosting configures non-overlapping credits routes", () => {
  const creditsRoutes = staticWebAppConfig.routes
    .filter(route => route.route.startsWith("/credits"));

  assert.deepEqual(creditsRoutes, [
    {
      route: "/credits",
      rewrite: "/index.html",
      headers: {
        "Cache-Control": "no-cache, no-store, must-revalidate",
      },
    },
    {
      route: "/credits/*",
      rewrite: "/index.html",
      headers: {
        "Cache-Control": "no-cache, no-store, must-revalidate",
      },
    },
  ]);
  const normalizedRoutes = staticWebAppConfig.routes
    .map(({ route }) => route === "/" ? route : route.replace(/\/+$/, ""));
  assert.equal(new Set(normalizedRoutes).size, normalizedRoutes.length);
  assert.equal(staticWebAppConfig.navigationFallback.rewrite, "/index.html");
  assert.deepEqual(
    staticWebAppConfig.navigationFallback.exclude,
    ["/api/*", "/assets/*", "/_framework/*"],
  );
  assert.match(siteIndexHtml, /<base href="\/" \/>/);
});

const linuxLibcs = ["glibc", "musl"];

function optionalNativeVariants(packagePath, dependencyPrefix) {
  const packageEntry = packageLock.packages[packagePath];
  assert.ok(packageEntry);
  const dependencies = Object.keys(packageEntry.optionalDependencies ?? {})
    .filter(dependency => dependency.startsWith(dependencyPrefix));
  assert.notEqual(dependencies.length, 0);

  const variants = new Set();
  for (const dependency of dependencies) {
    const nativeEntry = packageLock.packages[`node_modules/${dependency}`];
    assert.ok(nativeEntry);
    assert.equal(nativeEntry.os?.length, 1);
    assert.equal(nativeEntry.cpu?.length, 1);
    assert.ok(nativeEntry.libc === undefined || nativeEntry.libc.length === 1);

    const host = `${nativeEntry.os[0]}-${nativeEntry.cpu[0]}`;
    const libcs = nativeEntry.libc
      ?? (nativeEntry.os[0] === "linux" ? linuxLibcs : ["none"]);
    for (const libc of libcs) {
      variants.add(`${host}/${libc}`);
    }
  }
  return variants;
}

function completeAnalyzerHosts(oxlintVariants, tsgolintVariants) {
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
  assert.match(packageJson.scripts.lint, /(?:^| )src(?: |$)/);
  assert.match(
    packageJson.scripts.lint,
    /(?:^| )engine\/wwwroot\/inspect-web-engine\.js(?: |$)/,
  );
});

test("the site artifact rejects a missing Vite output", (context) => {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-artifact-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  mkdirSync(join(site, "assets"));
  const manifest = {
    "index.html": {
      file: "assets/index.js",
      css: ["assets/index.css"],
      dynamicImports: ["src/dotnet-inspect.ts"],
      isEntry: true,
    },
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
  manifest["index.html"].file = "assets/../index.html";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest contains invalid asset 'assets\/\.\.\/index\.html'/,
  );

  manifest["index.html"].file = "assets/index.js";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  unlinkSync(join(site, "assets/index.js"));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest references missing asset 'assets\/index\.js'/,
  );
});
