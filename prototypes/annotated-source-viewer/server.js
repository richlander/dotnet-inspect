import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { dirname, extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const port = Number.parseInt(process.env.PORT ?? "5199", 10);
const contentTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
]);

export function createViewerServer(directory = root) {
  return createServer(async (request, response) => {
    let relativePath;
    try {
      const url = new URL(request.url ?? "/", "http://127.0.0.1");
      relativePath = url.pathname === "/" ? "index.html" : decodeURIComponent(url.pathname.slice(1));
    } catch {
      response.writeHead(400).end("Malformed request path");
      return;
    }
    const path = resolve(directory, relativePath);

    if (path !== directory && !path.startsWith(directory + sep)) {
      response.writeHead(403).end("Forbidden");
      return;
    }

    try {
      const content = await readFile(path);
      response.writeHead(200, {
        "Content-Type": contentTypes.get(extname(path)) ?? "application/octet-stream",
        "Cache-Control": "no-store",
      });
      response.end(content);
    } catch (error) {
      if (error?.code === "ENOENT" || error?.code === "EISDIR") {
        response.writeHead(404).end("Not found");
        return;
      }
      response.writeHead(500).end("Failed to read the requested file");
      console.error(error);
    }
  });
}

if (resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  createViewerServer().listen(port, "127.0.0.1", () => {
    console.log(`Annotated source viewer: http://127.0.0.1:${port}`);
  });
}
