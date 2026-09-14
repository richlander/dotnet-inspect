import { createHash } from "node:crypto";
import {
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import {
  createRuntimeCohortBenchmarkReceipt,
  createRuntimeCohortReceipt,
  parseRuntimeVariantReceipt,
  runtimeCohortVariantNames,
  validateRuntimePinAdvancementCohort,
  validateRuntimeVariantPublicationEvidence,
} from "./runtime-cohort-model.ts";

function argumentAfter(
  arguments_: readonly string[],
  option: string,
): string {
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

function writeJson(path: string, value: unknown): void {
  const output = resolve(path);
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, `${JSON.stringify(value, null, 2)}\n`);
  console.log(`Wrote ${output}`);
}

const arguments_ = process.argv.slice(2);
const command = arguments_[0];

if (command === "record") {
  const sourceCommit = argumentAfter(arguments_, "--source-commit");
  const evidenceRoot = resolve(argumentAfter(arguments_, "--evidence-root"));
  const output = argumentAfter(arguments_, "--output");
  const evidence = runtimeCohortVariantNames.map(name => {
    const variantDirectory = join(evidenceRoot, "sites", name);
    const admissionDirectory = join(evidenceRoot, "admission");
    const logFile = join(admissionDirectory, `${name}.log`);
    const logText = readText(logFile);
    const receipt = parseRuntimeVariantReceipt(JSON.parse(
      readText(join(variantDirectory, "runtime-variant.json")),
    ));
    validateRuntimeVariantPublicationEvidence(receipt, {
      frontendManifestText: readText(
        join(variantDirectory, "frontend-sha256.txt"),
      ),
      siteManifestText: readText(join(variantDirectory, "site-sha256.txt")),
      asyncLoweringReceiptText: readText(
        join(variantDirectory, "async-lowering.json"),
      ),
      runtimeCohortReceiptText: name === "mono"
        ? null
        : readText(join(
          variantDirectory,
          "runtime-cohort",
          "runtime-cohort.json",
        )),
    });
    return {
      receipt,
      exitCode: Number(readText(
        join(admissionDirectory, `${name}.exit-code`),
      ).trim()),
      logFile: `admission/${name}.log`,
      logSha256: createHash("sha256").update(logText).digest("hex"),
      logText,
    };
  });
  const receipt = createRuntimeCohortReceipt(
    sourceCommit,
    new Date().toISOString(),
    evidence,
  );
  writeJson(output, receipt);
  process.exitCode = receipt.status === "accepted" ? 0 : 1;
} else if (command === "benchmark") {
  const receipt = createRuntimeCohortBenchmarkReceipt(
    readText(argumentAfter(arguments_, "--cohort")),
    readText(argumentAfter(arguments_, "--report")),
    readText(argumentAfter(arguments_, "--trend")),
  );
  writeJson(argumentAfter(arguments_, "--output"), receipt);
} else if (command === "validate-advancement") {
  validateRuntimePinAdvancementCohort(
    readText(argumentAfter(arguments_, "--cohort")),
    argumentAfter(arguments_, "--source-commit"),
  );
  console.log("Runtime cohort is admissible for pin advancement.");
} else {
  throw new Error(
    "Usage: record-runtime-cohort.ts record "
      + "--source-commit <sha> --evidence-root <directory> --output <file>\n"
      + "   or: record-runtime-cohort.ts benchmark "
      + "--cohort <file> --report <file> --trend <file> --output <file>\n"
      + "   or: record-runtime-cohort.ts validate-advancement "
      + "--cohort <file> --source-commit <sha>",
  );
}
