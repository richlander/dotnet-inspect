import { defineConfig } from "vite";

export default defineConfig({
  // Anything under `publicDir` is copied into the build output verbatim, without the
  // bundler, the compiler or the lint ever reading it. This project ships no such assets,
  // so the path stays closed; `test/toolchain.test.ts` fails if it reopens.
  publicDir: false,
  build: {
    manifest: "manifest.json",
    rollupOptions: {
      external: ["/inspect-web-engine.js"],
    },
  },
});
