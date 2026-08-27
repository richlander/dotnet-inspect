import { defineConfig } from "vite";

export default defineConfig({
  build: {
    manifest: "manifest.json",
    rollupOptions: {
      external: ["/inspect-web-engine.js"],
    },
  },
});
