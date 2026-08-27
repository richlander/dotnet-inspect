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
import { build } from "vite";

export async function bundlerReadFiles(root: string): Promise<string[]> {
  const read = new Set<string>();
  await build({
    root,
    logLevel: "error",
    build: { write: false, sourcemap: false },
    plugins: [{
      name: "toolchain-gate-audit",
      buildEnd() {
        for (const file of this.getWatchFiles()) {
          read.add(file);
        }
      },
    }],
  });
  return [...read];
}
