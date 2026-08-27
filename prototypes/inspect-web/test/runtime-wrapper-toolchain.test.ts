import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

interface TsconfigFile {
  readonly extends?: string;
  readonly compilerOptions: Readonly<Record<string, unknown>>;
  readonly include?: readonly string[];
}

// oxlint-disable-next-line typescript/no-unnecessary-type-parameters
function readJson<T>(specifier: string): T {
  const parsed: unknown = JSON.parse(
    readFileSync(new URL(specifier, import.meta.url), "utf8"),
  );
  // Repo-owned configuration is asserted field-by-field below.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return parsed as T;
}

test("the generated runtime wrapper has a dedicated checked-JavaScript project", () => {
  const config = readJson<TsconfigFile>("../tsconfig.runtime-wrapper.json");

  assert.equal(config.extends, "./tsconfig.json");
  assert.equal(config.compilerOptions.checkJs, true);
  assert.deepEqual(
    config.compilerOptions.rootDirs,
    ["./engine/wwwroot", "./runtime-types"],
  );
  assert.deepEqual(
    config.include,
    [
      "./engine/wwwroot/inspect-web-engine.js",
      "./runtime-types/**/*.d.ts",
      "./src/inspect-web-engine.d.ts",
    ],
  );
});

test("the generated runtime wrapper keeps the real relative host import", () => {
  const wrapper = readFileSync(
    new URL("../engine/wwwroot/inspect-web-engine.js", import.meta.url),
    "utf8",
  );

  assert.match(
    wrapper,
    /^\/\/ GENERATED FILE[\s\S]*import \{ dotnet \} from "\.\/_framework\/dotnet\.js";/u,
  );
  assert.match(
    wrapper,
    /@typedef \{import\("\/inspect-web-engine\.js"\)\./u,
  );
  assert.doesNotMatch(wrapper, /@ts-(?:check|ignore|nocheck|expect-error)/u);
});

test("the wrapper project rejects drift from a qualified managed export path", () => {
  const root = new URL("../", import.meta.url);
  const original = readFileSync(
    new URL("engine/wwwroot/inspect-web-engine.js", root),
    "utf8",
  );
  const mutated = original.replace(
    "exports.InspectionEngine.QueryPackageMetadata(",
    "exports.InspectionEngine.QueryPackageMetadataMissing(",
  );
  assert.notEqual(mutated, original, "the managed export mutation must apply");

  const probe = mkdtempSync(join(tmpdir(), "inspect-web-runtime-wrapper-"));
  try {
    mkdirSync(join(probe, "engine/wwwroot"), { recursive: true });
    mkdirSync(join(probe, "runtime-types/_framework"), { recursive: true });
    mkdirSync(join(probe, "src"), { recursive: true });
    writeFileSync(
      join(probe, "engine/wwwroot/inspect-web-engine.js"),
      mutated,
    );
    writeFileSync(
      join(probe, "runtime-types/_framework/dotnet.d.ts"),
      readFileSync(new URL("runtime-types/_framework/dotnet.d.ts", root)),
    );
    writeFileSync(
      join(probe, "src/inspect-web-engine.d.ts"),
      readFileSync(new URL("src/inspect-web-engine.d.ts", root)),
    );
    writeFileSync(
      join(probe, "tsconfig.json"),
      JSON.stringify({
        compilerOptions: {
          allowJs: true,
          checkJs: true,
          lib: ["DOM", "ES2022"],
          module: "ESNext",
          moduleResolution: "Bundler",
          noEmit: true,
          paths: {
            "/inspect-web-engine.js": ["./src/inspect-web-engine.d.ts"],
          },
          rootDirs: ["./engine/wwwroot", "./runtime-types"],
          strict: true,
          target: "ES2022",
          types: [],
        },
        include: [
          "./engine/wwwroot/inspect-web-engine.js",
          "./runtime-types/**/*.d.ts",
          "./src/inspect-web-engine.d.ts",
        ],
      }),
    );

    const compile = spawnSync(
      process.execPath,
      [
        fileURLToPath(new URL("node_modules/typescript/bin/tsc", root)),
        "-p",
        probe,
      ],
      { encoding: "utf8" },
    );

    assert.notEqual(
      compile.status,
      0,
      "the wrapper project accepted a managed export path absent from its generated shape",
    );
    assert.match(
      compile.stdout,
      /Property 'QueryPackageMetadataMissing' does not exist/u,
      `the mutation failed for an unrelated reason:\n${compile.stdout}`,
    );
  } finally {
    rmSync(probe, { recursive: true, force: true });
  }
});

test("the wrapper fails before initialization and reuses its initialized runtime", () => {
  const root = new URL("../", import.meta.url);
  const probe = mkdtempSync(join(tmpdir(), "inspect-web-runtime-behavior-"));
  try {
    mkdirSync(join(probe, "_framework"), { recursive: true });
    writeFileSync(
      join(probe, "package.json"),
      JSON.stringify({ type: "module" }),
    );
    writeFileSync(
      join(probe, "inspect-web-engine.js"),
      readFileSync(
        new URL("engine/wwwroot/inspect-web-engine.js", root),
      ),
    );
    writeFileSync(
      join(probe, "_framework/dotnet.js"),
      `
      export let createCount = 0;
      const inspectionEngine = new Proxy(
        { ConfigureHost() {} },
        { get(target, property) { return target[property] ?? (() => "{}"); } },
      );
      export const dotnet = {
        async create() {
          createCount++;
          return {
            async getAssemblyExports() {
              return { InspectionEngine: inspectionEngine };
            },
            async runMain() {},
          };
        },
      };
      `,
    );
    writeFileSync(
      join(probe, "run.mjs"),
      `
      import {
        buildIdentity,
        initializeEngine,
      } from "./inspect-web-engine.js";
      import { createCount } from "./_framework/dotnet.js";

      let preInitializationError;
      try {
        buildIdentity();
      } catch (error) {
        preInitializationError = error;
      }
      if (!(preInitializationError instanceof Error)
          || preInitializationError.message
            !== "The browser inspection engine is not initialized.") {
        throw new Error("the pre-initialization call did not expose the expected failure");
      }

      globalThis.window = { location: { origin: "https://example.invalid" } };
      await initializeEngine();
      buildIdentity();
      if (createCount !== 1) {
        throw new Error(\`expected one runtime, observed \${createCount}\`);
      }
      `,
    );

    const run = spawnSync(process.execPath, [join(probe, "run.mjs")], {
      encoding: "utf8",
    });

    assert.equal(
      run.status,
      0,
      `the generated wrapper behavior probe failed:\n${run.stderr}`,
    );
  } finally {
    rmSync(probe, { recursive: true, force: true });
  }
});
