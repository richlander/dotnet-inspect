// The bundler is the third oracle these gates rely on, alongside the directory walk and
// the compiler. Round 7 established that it has to be asked rather than predicted; round
// 8 (Sol) established that source maps are the wrong question to ask it. A file pulled in
// by `new URL("...", import.meta.url)` is emitted as an asset rather than bundled as a
// module, so it never appears in any map's `sources` -- and under Vite's inline limit it
// is base64'd straight into a chunk, emitting no asset file and no manifest entry either.
//
// Rollup already keeps the answer. `getWatchFiles` is the set of files the build read,
// which is what "what did the bundler take from this repository" actually means: modules,
// entry HTML, assets whether emitted or inlined, and anything a plugin read. It is
// derived from the build rather than reconstructed from its output, so it does not care
// how a file was referenced or what it is called.
//
// The project's own `vite.config.ts` is used rather than a restatement of it, so the
// audit reads what the real build reads. Only the output is suppressed: `write: false`
// keeps this off disk, because the audit needs the module graph rather than an artifact,
// and the shipped build keeps its own shape and gains no source maps.
import { execFileSync } from "node:child_process";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { build } from "vite";

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

// Round 12 (Gemini 3.1 Pro) reached Rollup without going through Vite's plugin list at
// all. `build.rollupOptions.plugins` is handed straight to Rollup, so it never appears in
// the `config.plugins` that `configResolved` reports, and because it transforms in both
// builds identically the equivalence gate below sees nothing to disagree about. The
// payload shipped with all four commands green.
//
// Asking Vite was the wrong end of the pipe. Rollup is what actually runs the plugins, so
// its `options` and `outputOptions` hooks are asked instead: every plugin Rollup has must
// be one Vite resolved, and there must be no output plugins at all. Neither list is
// enumerated here -- Rollup reports both, and the comparison is against Vite's own account
// of what it installed. The difference is taken as a multiset so a plugin cannot hide by
// borrowing the name of one that is legitimately present.
function pluginNamesOf(plugins: unknown): string[] {
  if (!Array.isArray(plugins)) {
    return [];
  }
  return plugins.flatMap((plugin: unknown) => {
    if (typeof plugin !== "object" || plugin === null || !("name" in plugin)) {
      return [];
    }
    const { name } = plugin;
    return typeof name === "string" ? [name] : [];
  });
}

function multisetDifference(actual: string[], accounted: string[]): string[] {
  const remaining = [...accounted];
  return actual.flatMap(name => {
    const found = remaining.indexOf(name);
    if (found === -1) {
      return [name];
    }
    remaining.splice(found, 1);
    return [];
  });
}

export async function auditedBuild(root: string): Promise<AuditedBuild> {
  const read = new Set<string>();
  let observed: { mode: string; publicDir: string; pluginNames: string[]; workerPluginCount: number }
    | undefined;
  let configNames: string[] = [];
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
        configNames = config.plugins.map(plugin => plugin.name);
        observed = {
          mode: config.mode,
          publicDir: config.publicDir,
          pluginNames: withoutAuditPlugin(config.plugins.map(plugin => plugin.name)),
          workerPluginCount: Array.isArray(worker) ? worker.length : 0,
        };
      },
      options(options) {
        rollupInputNames = pluginNamesOf(options.plugins);
        return null;
      },
      outputOptions(options) {
        rollupOutputNames = pluginNamesOf(options.plugins);
        return null;
      },
      buildEnd() {
        for (const file of this.getWatchFiles()) {
          read.add(file);
        }
      },
    }],
  });
  const results = Array.isArray(result) ? result : [result];
  const chunks = results
    .flatMap(one => "output" in one ? one.output : [])
    .flatMap(one => one.type === "chunk" ? [one.code] : [])
    .sort();
  if (observed === undefined) {
    throw new Error("the audited build never resolved a config");
  }
  return {
    readFiles: [...read],
    chunks,
    ...observed,
    unaccountedRollupPlugins: multisetDifference(rollupInputNames, configNames),
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
// to know what Vite's own set is -- 34 plugins here, none of them named in this repo.
// However the config is spelled, composed, imported or computed, the difference shows up
// after resolution.
export async function builtinPluginNames(root: string, mode: string): Promise<string[]> {
  const { resolveConfig } = await import("vite");
  const builtin = await resolveConfig({ root, mode, configFile: false, logLevel: "error" }, "build");
  return builtin.plugins.map(plugin => plugin.name).sort();
}
