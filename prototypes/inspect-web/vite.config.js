import { defineConfig } from "vite";

export default defineConfig({
  build: {
    manifest: "manifest.json",
    rollupOptions: {
      external: ["/engine.js", "/inspect-web-engine.js"],
    },
  },
});
