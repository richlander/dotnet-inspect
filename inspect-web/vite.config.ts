import { defineConfig } from "vite";

const facadeModules = [
  "/inspect-web-host.js",
  "/inspect-web-package.js",
  "/inspect-web-library.js",
  "/inspect-web-metadata.js",
  "/inspect-web-analysis.js",
  "/inspect-web-source.js",
  "/inspect-web-call-graph.js",
  "/inspect-web-catalog.js",
];

export default defineConfig({
  // Anything under `publicDir` is copied into the build output verbatim, without the
  // bundler, the compiler or the lint ever reading it. This project ships no such assets,
  // so the path stays closed; `test/toolchain.test.ts` fails if it reopens.
  publicDir: false,
  worker: {
    format: "es",
    rollupOptions: { external: facadeModules },
  },
  build: {
    manifest: "manifest.json",
    rollupOptions: {
      input: [
        "index.html",
        "src/engine-worker-client.ts",
        "src/browser-package-entry-cache.ts",
        "src/ecosystem-find-index-assets.ts",
      ],
      output: {
        entryFileNames: chunk => {
          if (chunk.name === "browser-package-entry-cache")
            return "browser-package-entry-cache.js";
          if (chunk.name === "ecosystem-find-index-assets")
            return "ecosystem-find-index-assets.js";
          return "assets/[name]-[hash].js";
        },
      },
      preserveEntrySignatures: "strict",
      external: facadeModules,
    },
  },
});
