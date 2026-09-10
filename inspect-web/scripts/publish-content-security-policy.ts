import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export function publishContentSecurityPolicy(siteArgument: string): void {
  const site = resolve(siteArgument);
  // HTML parsing normalizes CRLF and lone CR before CSP hashes script text.
  const index = readFileSync(resolve(site, "index.html"), "utf8").replace(/\r\n?/g, "\n");
  const maps = [...index.matchAll(/<script type="importmap">([\s\S]*?)<\/script>/g)];
  const text = maps[0]?.[1];
  if (maps.length !== 1 || !text?.trim()) {
    throw new Error("Expected one populated SDK import map in published index.html.");
  }

  const config: unknown = JSON.parse(readFileSync(
    new URL("../staticwebapp.config.json", import.meta.url), "utf8",
  ));
  if (typeof config !== "object" || config === null
    || !("globalHeaders" in config)
    || typeof config.globalHeaders !== "object" || config.globalHeaders === null
    || !("Content-Security-Policy" in config.globalHeaders)
    || typeof config.globalHeaders["Content-Security-Policy"] !== "string") {
    throw new Error("Static Web Apps configuration is missing Content-Security-Policy.");
  }
  const template = config.globalHeaders["Content-Security-Policy"];
  const placeholder = "{{IMPORT_MAP_HASH}}";
  if (template.split(placeholder).length !== 2) {
    throw new Error("Content-Security-Policy must contain one import-map hash placeholder.");
  }
  config.globalHeaders["Content-Security-Policy"] = template.replace(
    placeholder, createHash("sha256").update(text).digest("base64"),
  );
  writeFileSync(resolve(site, "staticwebapp.config.json"), `${JSON.stringify(config, null, 2)}\n`);
}

const invokedPath = process.argv[1];
if (invokedPath !== undefined
  && import.meta.url === pathToFileURL(resolve(invokedPath)).href) {
  const siteArgument = process.argv[2];
  if (!siteArgument) {
    throw new Error("Usage: node scripts/publish-content-security-policy.ts <published-wwwroot>");
  }
  publishContentSecurityPolicy(siteArgument);
}
