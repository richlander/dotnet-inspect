import { defineConfig } from "vite";

import { failOnHtmlParseErrors } from "./scripts/html-parse-gate.ts";

export default defineConfig({
  // Vite reports a document parse5 cannot parse and then builds it anyway. This makes
  // that report fatal, so the parser that actually reads `index.html` gets a veto.
  customLogger: failOnHtmlParseErrors(),
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
