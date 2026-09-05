import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";

// Serves the published production engine artifact so the artifact-backed package
// scope adoption gate drives the real generated facades in a browser. This
// mirrors serve-worker-runtime-gate.ts: a minimal static server over the
// published wwwroot plus one synthetic entry page. Package acquisition still
// leaves the browser as ordinary fetches to the NuGet Gallery CDN; the gate's
// Playwright spec intercepts those to serve deterministic local fixtures.

const site = resolve(
  process.env.INSPECT_WEB_PACKAGE_ADOPTION_SITE
    ?? "../../artifacts/inspect-web-publish/wwwroot",
);
await readFile(resolve(site, "inspect-web-package.js"));
await readFile(resolve(site, "inspect-web-analysis.js"));

const contentTypes: Readonly<Record<string, string>> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json",
  ".wasm": "application/wasm",
  ".css": "text/css",
};

const port = Number(process.env.INSPECT_WEB_PACKAGE_ADOPTION_PORT ?? 4187);

createServer((request, response) => {
  const pathname = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
  if (pathname === "/package-adoption-gate.html") {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end(
      "<!doctype html><html lang=\"en\"><title>Package adoption gate</title>"
        + "<body><main id=\"gate\">Package adoption gate</main></body></html>",
    );
    return;
  }
  const file = resolve(site, `.${pathname}`);
  if (!file.startsWith(`${site}${sep}`)) {
    response.writeHead(404);
    response.end();
    return;
  }
  void readFile(file).then(
    contents => {
      response.writeHead(200, {
        "Content-Type": contentTypes[extname(file)] ?? "application/octet-stream",
        "Cache-Control": "no-store",
      });
      response.end(contents);
      return undefined;
    },
    (error: unknown) => {
      const missing = error instanceof Error
        && "code" in error
        && (error.code === "ENOENT" || error.code === "ENOTDIR");
      if (!missing) console.error(error);
      response.writeHead(missing ? 404 : 500);
      response.end();
      return undefined;
    },
  );
}).listen(port, "127.0.0.1", () => {
  console.log(`Package adoption gate: http://127.0.0.1:${port} (${site})`);
});
