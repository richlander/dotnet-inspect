import {
  copyFileSync,
  existsSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface RuntimeModule {
  readonly specifier: string;
  readonly target: string;
}

const runtimeModulePatterns = new Map<string, RegExp>([
  ["./_framework/dotnet.js", /^\.\/_framework\/dotnet\.[A-Za-z0-9_-]+\.js$/],
  [
    "./_framework/dotnet.native.js",
    /^\.\/_framework\/dotnet\.native\.[A-Za-z0-9_-]+\.js$/,
  ],
  [
    "./_framework/dotnet.runtime.js",
    /^\.\/_framework\/dotnet\.runtime\.[A-Za-z0-9_-]+\.js$/,
  ],
]);

function isObjectLike(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

export function publishedRuntimeModules(siteArgument: string): readonly RuntimeModule[] {
  const site = resolve(siteArgument);
  const index = readFileSync(resolve(site, "index.html"), "utf8");
  const importMapSource = /<script\b[^>]*\btype=["']importmap["'][^>]*>([\s\S]*?)<\/script>/i
    .exec(index)?.[1];
  if (importMapSource === undefined) {
    throw new Error("Published index has no import map.");
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(importMapSource);
  } catch (error: unknown) {
    throw new Error("Published import map is not valid JSON.", { cause: error });
  }
  const imports = isObjectLike(parsed) && isObjectLike(parsed.imports)
    ? parsed.imports
    : {};

  const modules = [...runtimeModulePatterns].map(([specifier, pattern]) => {
    const target = imports[specifier];
    if (typeof target !== "string" || !pattern.test(target)) {
      throw new Error(
        `Published import map has no valid fingerprinted ${specifier.split("/").at(-1)} mapping.`);
    }
    if (!existsSync(resolve(site, target))) {
      throw new Error(`Published runtime module '${target}' is missing.`);
    }
    return { specifier, target };
  });
  return modules;
}

export function publishedRuntimeTarget(siteArgument: string): string {
  const target = publishedRuntimeModules(siteArgument)
    .find(module => module.specifier === "./_framework/dotnet.js")?.target;
  if (target === undefined) {
    throw new Error("Published runtime module inventory has no dotnet.js entry.");
  }
  return target;
}

export function verifyPublishedRuntimeModules(siteArgument: string): void {
  const site = resolve(siteArgument);
  const modules = publishedRuntimeModules(site);
  for (const module of modules) {
    const stable = readFileSync(resolve(site, module.specifier));
    const fingerprinted = readFileSync(resolve(site, module.target));
    if (!stable.equals(fingerprinted)) {
      throw new Error(
        `Published runtime alias '${module.specifier}' does not match '${module.target}'.`);
    }
  }
  const loader = readFileSync(resolve(site, "runtime-loader.js"), "utf8");
  const expected = 'export { dotnet } from "./_framework/dotnet.js";\n';
  if (loader !== expected) {
    throw new Error(
      "Published runtime loader does not use the stable Worker runtime module.");
  }
}

export function publishRuntimeLoader(siteArgument: string): void {
  const site = resolve(siteArgument);
  const modules = publishedRuntimeModules(site);
  for (const module of modules) {
    copyFileSync(resolve(site, module.target), resolve(site, module.specifier));
  }
  writeFileSync(
    resolve(site, "runtime-loader.js"),
    'export { dotnet } from "./_framework/dotnet.js";\n',
  );
  verifyPublishedRuntimeModules(site);
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
