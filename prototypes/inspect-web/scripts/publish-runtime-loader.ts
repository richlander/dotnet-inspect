import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export function publishedRuntimeTarget(siteArgument: string): string {
  const site = resolve(siteArgument);
  const index = readFileSync(resolve(site, "index.html"), "utf8");
  const target = /"\.\/_framework\/dotnet\.js"\s*:\s*"(\.\/_framework\/dotnet\.[^"/]+\.js)"/
    .exec(index)?.[1];
  if (!target) {
    throw new Error(
      "Published import map has no valid fingerprinted dotnet.js mapping.");
  }
  if (!existsSync(resolve(site, target))) {
    throw new Error(`Published runtime module '${target}' is missing.`);
  }
  return target;
}

export function publishRuntimeLoader(siteArgument: string): void {
  const target = publishedRuntimeTarget(siteArgument);
  writeFileSync(
    resolve(siteArgument, "runtime-loader.js"),
    `export { dotnet } from ${JSON.stringify(target)};\n`,
  );
}

const invokedPath = process.argv[1];
if (invokedPath !== undefined
  && import.meta.url === pathToFileURL(resolve(invokedPath)).href) {
  const siteArgument = process.argv[2];
  if (!siteArgument) {
    throw new Error("Usage: node scripts/publish-runtime-loader.ts <published-wwwroot>");
  }
  publishRuntimeLoader(siteArgument);
}
