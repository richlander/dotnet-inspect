import { createHash } from "node:crypto";
import {
  cpSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import {
  createRuntimeSiteDeploymentReceipt,
  parseRuntimeVariantReceipt,
  validateRuntimeVariantPublicationEvidence,
} from "./runtime-cohort-model.ts";

function argumentAfter(arguments_: readonly string[], option: string): string {
  const index = arguments_.indexOf(option);
  const value = index < 0 ? undefined : arguments_[index + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}

function readText(path: string): string {
  return readFileSync(resolve(path), "utf8");
}

function sha256File(path: string): string {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function validateSiteFiles(root: string, manifestText: string): void {
  const prefix = `${resolve(root)}${sep}`;
  for (const line of manifestText.trimEnd().split("\n")) {
    const match = /^([0-9a-f]{64})  (\.\/.+)$/u.exec(line);
    if (!match) throw new Error("Site manifest contains an invalid entry.");
    const expected = match[1];
    const relativePath = match[2];
    if (!expected || !relativePath) {
      throw new Error("Site manifest contains an incomplete entry.");
    }
    const path = resolve(root, relativePath);
    if (!path.startsWith(prefix) || !statSync(path).isFile()) {
      throw new Error("Site manifest entry is outside the published site.");
    }
    if (sha256File(path) !== expected) {
      throw new Error(`Published site digest mismatch for ${relativePath}.`);
    }
  }
}

function filesUnder(root: string): string[] {
  const files: string[] = [];
  const visit = (directory: string): void => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile()) files.push(path);
    }
  };
  visit(root);
  return files.sort();
}

const arguments_ = process.argv.slice(2);
const variantRoot = resolve(argumentAfter(arguments_, "--variant-root"));
const apiRoot = resolve(argumentAfter(arguments_, "--api-root"));
const cohortPath = resolve(argumentAfter(arguments_, "--cohort"));
const candidateIdentityPath = resolve(
  argumentAfter(arguments_, "--candidate-identity"),
);
const sourceCommit = argumentAfter(arguments_, "--source-commit");
const candidateRunId = argumentAfter(arguments_, "--candidate-run-id");
const candidateAttempt = Number(argumentAfter(arguments_, "--candidate-attempt"));
const output = resolve(argumentAfter(arguments_, "--output"));
const candidateIdentity: unknown = JSON.parse(readText(candidateIdentityPath));
if (
  typeof candidateIdentity !== "object"
  || candidateIdentity === null
  || !("schema" in candidateIdentity)
  || candidateIdentity.schema !== 1
  || !("runId" in candidateIdentity)
  || candidateIdentity.runId !== candidateRunId
  || !("attempt" in candidateIdentity)
  || candidateIdentity.attempt !== candidateAttempt
  || !("sourceCommit" in candidateIdentity)
  || candidateIdentity.sourceCommit !== sourceCommit
) {
  throw new Error("Candidate identity evidence does not match deployment.");
}

const variantText = readText(join(variantRoot, "runtime-variant.json"));
const variant = parseRuntimeVariantReceipt(JSON.parse(variantText));
const siteManifestText = readText(join(variantRoot, "site-sha256.txt"));
validateRuntimeVariantPublicationEvidence(variant, {
  frontendManifestText: readText(join(variantRoot, "frontend-sha256.txt")),
  siteManifestText,
  asyncLoweringReceiptText: readText(join(variantRoot, "async-lowering.json")),
  runtimeCohortReceiptText: readText(
    join(variantRoot, "runtime-cohort", "runtime-cohort.json"),
  ),
});
validateSiteFiles(join(variantRoot, "wwwroot"), siteManifestText);

for (const required of [
  "host.json",
  "functions.metadata",
  "worker.config.json",
  ".azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll",
]) {
  if (!statSync(join(apiRoot, required)).isFile()) {
    throw new Error(`Managed API is missing ${required}.`);
  }
}

const deployment = createRuntimeSiteDeploymentReceipt(
  readText(cohortPath),
  variantText,
  sourceCommit,
  candidateRunId,
  candidateAttempt,
);
rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });
cpSync(join(variantRoot, "wwwroot"), join(output, "wwwroot"), {
  recursive: true,
});
cpSync(apiRoot, join(output, "api"), { recursive: true });
mkdirSync(join(output, "evidence"), { recursive: true });
cpSync(
  join(variantRoot, "runtime-cohort"),
  join(output, "evidence", "runtime-cohort"),
  { recursive: true },
);
writeFileSync(
  join(output, "wwwroot", "runtime-site.json"),
  `${JSON.stringify(deployment, null, 2)}\n`,
);
writeFileSync(
  join(output, "evidence", "runtime-variant.json"),
  variantText,
);
writeFileSync(
  join(output, "evidence", "runtime-cohort.json"),
  readText(cohortPath),
);
writeFileSync(
  join(output, "evidence", "candidate-identity.json"),
  readText(candidateIdentityPath),
);

const manifest = filesUnder(output)
  .map(path => `${sha256File(path)}  ./${relative(output, path)}`)
  .join("\n");
writeFileSync(join(output, "deployment-sha256.txt"), `${manifest}\n`);
console.log(`Prepared ${deployment.site} deployment at ${output}.`);
