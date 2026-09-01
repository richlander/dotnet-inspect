// The bundler is the third oracle these gates rely on, alongside the directory walk and
// the compiler. Round 7 established that it has to be asked rather than predicted; round
// 8 (Sol) established that source maps are the wrong question to ask it. A file pulled in
// by `new URL("...", import.meta.url)` is emitted as an asset rather than bundled as a
// module, so it never appears in any map's `sources` -- and under Vite's inline limit it
// is base64'd straight into a chunk, emitting no asset file and no manifest entry either.
//
// Vite 8's Rolldown context exposes the module graph but no longer exposes Rollup's
// `getWatchFiles`. The graph covers modules, entry HTML and CSS, while a second build
// with asset inlining disabled makes every asset source visible through output
// provenance. Both answers come from Vite builds using the project's real config and
// plugins rather than from parsing source or enumerating extensions.
//
// The project's own `vite.config.ts` is used rather than a restatement of it, so the
// audit reads what the real build reads. Only the output is suppressed: `write: false`
// keeps this off disk, because the audit needs the module graph rather than an artifact,
// and the shipped build keeps its own shape and gains no source maps.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { isAbsolute, join, resolve } from "node:path";
import { build, type Rolldown } from "vite";

export interface AuditedBuild {
  readonly readFiles: string[];
  readonly chunks: string[];
  readonly mode: string;
  readonly publicDir: string;
  readonly pluginNames: string[];
  readonly workerPluginCount: number;
  readonly unaccountedRollupPlugins: string[];
  readonly rollupOutputPlugins: string[];
}

// The audit installs one plugin of its own, so exactly one instance of that name is
// removed rather than every match. A plugin that borrows the name to hide behind it
// leaves the second instance in the list, and the gate still fails.
const auditPluginName = "toolchain-gate-audit";

function withoutAuditPlugin(names: string[]): string[] {
  const remaining = [...names];
  const mine = remaining.indexOf(auditPluginName);
  if (mine !== -1) {
    remaining.splice(mine, 1);
  }
  return remaining.sort();
}

// Round 12 (Gemini 3.1 Pro) reached the bundler without going through Vite's plugin list.
// `build.rollupOptions.plugins` is handed directly to the bundler, so the resolved config
// must account for the input and output plugin slots separately. Vite 8 rewrites many of
// its own plugins into differently named native Rolldown plugins, so comparing names from
// the two layers is no longer meaningful. Direct plugin configuration remains forbidden,
// and any Vite plugin capable of mutating the options later is already caught by the
// resolved-plugin baseline.
function isUnknownArray(value: unknown): value is unknown[] {
  return Array.isArray(value);
}

function configuredPluginNames(plugins: unknown): string[] {
  if (plugins === undefined || plugins === null || plugins === false) {
    return [];
  }
  if (isUnknownArray(plugins)) {
    return plugins.flatMap(configuredPluginNames);
  }
  if (typeof plugins === "object" && "name" in plugins) {
    const { name } = plugins;
    if (typeof name === "string") {
      return [name];
    }
  }
  return ["<configured-plugin>"];
}

function hasPlugins(value: object): value is { plugins: unknown } {
  return "plugins" in value;
}

function configuredOutputPluginNames(output: unknown): string[] {
  const outputs = isUnknownArray(output) ? output : [output];
  return outputs.flatMap(options => {
    if (typeof options === "object" && options !== null && hasPlugins(options)) {
      return configuredPluginNames(options.plugins);
    }
    return [];
  });
}

function outputsOf(
  result: Awaited<ReturnType<typeof build>>,
): Rolldown.RolldownOutput[] {
  if (Array.isArray(result)) {
    return result;
  }
  if ("output" in result) {
    return [result];
  }
  throw new Error("the audited build unexpectedly returned a watcher");
}

export async function auditedBuild(root: string): Promise<AuditedBuild> {
  const read = new Set<string>();
  let observed: { mode: string; publicDir: string; pluginNames: string[]; workerPluginCount: number }
    | undefined;
  let rollupInputNames: string[] = [];
  let rollupOutputNames: string[] = [];
  const result = await build({
    root,
    logLevel: "error",
    build: { write: false, sourcemap: false },
    plugins: [{
      name: auditPluginName,
      configResolved(config) {
        const worker: unknown = config.worker.plugins;
        rollupInputNames = configuredPluginNames(config.build.rolldownOptions.plugins);
        rollupOutputNames = configuredOutputPluginNames(config.build.rolldownOptions.output);
        observed = {
          mode: config.mode,
          publicDir: config.publicDir,
          pluginNames: withoutAuditPlugin(config.plugins.map(plugin => plugin.name)),
          workerPluginCount: Array.isArray(worker) ? worker.length : 0,
        };
      },
      buildEnd(this: Rolldown.PluginContext) {
        for (const id of this.getModuleIds()) {
          if (existsSync(id)) {
            read.add(id);
          }
        }
      },
    }],
  });
  const results = outputsOf(result);
  const chunks = results
    .flatMap(one => one.output)
    .flatMap(one => one.type === "chunk" ? [one.code] : [])
    .sort();
  const assetResults = outputsOf(await build({
    root,
    logLevel: "error",
    build: { write: false, sourcemap: false, assetsInlineLimit: 0 },
  }));
  for (const asset of assetResults.flatMap(one => one.output)) {
    if (asset.type !== "asset") {
      continue;
    }
    for (const source of asset.originalFileNames) {
      const file = isAbsolute(source) ? source : resolve(root, source);
      if (existsSync(file)) {
        read.add(file);
      }
    }
  }
  if (observed === undefined) {
    throw new Error("the audited build never resolved a config");
  }
  return {
    readFiles: [...read],
    chunks,
    ...observed,
    unaccountedRollupPlugins: rollupInputNames,
    rollupOutputPlugins: rollupOutputNames,
  };
}

export async function bundlerReadFiles(root: string): Promise<string[]> {
  return (await auditedBuild(root)).readFiles;
}

// Round 11 (Sol) showed that resolving the config is still asking a question rather than
// watching what happens. A plugin guarded by `process.env.npm_lifecycle_event === "build"`
// is absent when `npm test` resolves the config and present when `npm run build` runs, so
// the plugin gate saw an empty list while the payload shipped. Nothing about that is
// exotic: config conditional on mode or environment is ordinary Vite practice, which
// means the gate had a false negative for honest configs as well as evasive ones.
//
// The audit cannot out-model a config that is a function of its own environment, so it
// stops trying. It runs the project's real build command in its own process -- the same
// command that produces what ships, with whatever environment npm gives it -- and the
// gate requires the audited build and that one to emit identical chunks. The audit then
// either describes the shipped bundle or the build fails, and no config can be one thing
// under test and another under `npm run build` without the two disagreeing.
export function shippedChunks(root: string): string[] {
  execFileSync("npm", ["run", "build"], { cwd: root, stdio: "ignore" });
  const assets = join(root, "dist", "assets");
  return readdirSync(assets)
    .filter(name => name.endsWith(".js"))
    .map(name => readFileSync(join(assets, name), "utf8"))
    .sort();
}

// Round 9 pinned `publicDir: false` and the absence of plugins by matching the text of
// `vite.config.ts`. Round 10 (Sol) showed what that is worth: a plugin declared in an
// imported helper and spread in as `...unwatchedInputConfig()` never writes `plugins:`
// in that file, so the pattern found nothing while the plugin injected an unwatched
// payload into the bundle. The pin was reading the source of a setting instead of the
// setting, and that gap costs an attacker one ordinary refactor.
//
// Vite resolves the config it is going to use, so it can be asked instead. Resolving the
// project's real config and resolving with `configFile: false` gives the plugins the
// project added on top of the ones Vite always installs, without this file ever having
// to know what Vite's own set is, with none of them named in this repo.
// However the config is spelled, composed, imported or computed, the difference shows up
// after resolution.
export async function builtinPluginNames(root: string, mode: string): Promise<string[]> {
  const { resolveConfig } = await import("vite");
  const builtin = await resolveConfig({ root, mode, configFile: false, logLevel: "error" }, "build");
  return builtin.plugins.map(plugin => plugin.name).sort();
}
