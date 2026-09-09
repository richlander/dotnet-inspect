import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";

const site = resolve(
  process.env.INSPECT_WEB_WORKER_SITE ?? "../../artifacts/inspect-web-publish/wwwroot",
);
await readFile(resolve(site, "manifest.json"));
const config: unknown = JSON.parse(await readFile(resolve(site, "staticwebapp.config.json"), "utf8"));
if (typeof config !== "object" || config === null || !("globalHeaders" in config)
  || typeof config.globalHeaders !== "object" || config.globalHeaders === null) {
  throw new Error("Published Static Web Apps configuration is missing globalHeaders.");
}
const headers = new Map<string, string>();
for (const [name, value] of Object.entries(config.globalHeaders)) {
  if (typeof value !== "string") throw new Error(`Invalid published header: ${name}`);
  headers.set(name, value);
}
const contentTypes: Readonly<Record<string, string>> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json",
  ".wasm": "application/wasm",
  ".css": "text/css",
};

createServer((request, response) => {
  for (const [name, value] of headers) response.setHeader(name, value);
  const pathname = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
  if (pathname === "/worker-runtime-gate.html") {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end("<!doctype html><html lang=\"en\"><title>Worker runtime gate</title>"
      + "<body><button id=\"input\" type=\"button\">Input</button><output id=\"count\">0</output></body></html>");
    return;
  }
  if (pathname === "/inspect-web-host.js"
    && request.headers.cookie?.split("; ")
      .includes("worker-runtime-gate=reject-bootstrap")) {
    response.writeHead(503);
    response.end("Worker bootstrap rejection fixture.");
    return;
  }
  const file = resolve(site, `.${pathname}`);
  if (!file.startsWith(`${site}${sep}`)) {
    response.writeHead(404);
    response.end();
    return;
  }
  const observeEpochReporter = pathname === "/inspect-web-host.js"
    && request.headers.cookie?.split("; ")
      .includes("worker-runtime-gate=observe-epoch-reporter");
  const observeStartup = request.headers.cookie?.split("; ")
    .includes("worker-runtime-gate=observe-startup");
  const startupCaptures: Readonly<Record<string, string>> = {
    "/inspect-web-host.js": "host = { buildIdentity }",
    "/inspect-web-catalog.js": "catalog = { listVocabulary, listHomeDemos }",
    "/inspect-web-package.js": "package = { listPackageQueryFacets, listGalleryDiscoveryCatalog }",
  };
  const startupCapture = observeStartup ? startupCaptures[pathname] : undefined;
  void readFile(file).then(
    contents => {
      response.writeHead(200, {
        "Content-Type": contentTypes[extname(file)] ?? "application/octet-stream",
        "Cache-Control": "no-store",
      });
      // Capture the generated functions during ordinary module loading, not
      // by asking the Firefox Worker debugger to import another module.
      if (observeEpochReporter) {
        response.end(`${contents.toString("utf8")}\nglobalThis.engineWorkerEpochReporterGate = { registerEpochWorkReporter, drainEpochWorkReporter, unregisterEpochWorkReporter };\n`);
      } else if (startupCapture !== undefined) {
        response.end(`${contents.toString("utf8")}\nglobalThis.engineWorkerStartupGate ??= {};\nglobalThis.engineWorkerStartupGate.${startupCapture};\n`);
      } else {
        response.end(contents);
      }
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
}).listen(4186, "127.0.0.1", () => {
  console.log(`Worker runtime gate: http://127.0.0.1:4186 (${site})`);
});
