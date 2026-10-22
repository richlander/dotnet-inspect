import assert from "node:assert/strict";
import test from "node:test";
import {
  parseTextPublicationBenchmarkArguments,
  textPublicationBenchmarkUsage,
} from "../scripts/text-publication-benchmark-model.ts";

test("text publication benchmark parses an exact published site", () => {
  assert.deepEqual(parseTextPublicationBenchmarkArguments([
    "--site", "http://127.0.0.1:4193/",
    "--samples", "25",
    "--expected-commit", "A".repeat(40),
    "--expected-version", "0.27.0",
    "--output", "artifacts/report.json",
  ]), {
    site: "http://127.0.0.1:4193",
    samples: 25,
    expectedCommit: "a".repeat(40),
    expectedVersion: "0.27.0",
    outputPath: "artifacts/report.json",
    help: false,
  });
});

test("text publication benchmark requires exact identity", () => {
  assert.throws(
    () => parseTextPublicationBenchmarkArguments([]),
    /--site is required/u,
  );
  assert.throws(
    () => parseTextPublicationBenchmarkArguments([
      "--site", "http://127.0.0.1:4193",
      "--expected-commit", "abc",
      "--expected-version", "0.27.0",
    ]),
    /complete Git SHA/u,
  );
  assert.throws(
    () => parseTextPublicationBenchmarkArguments([
      "--site", "http://127.0.0.1:4193",
      "--expected-commit", "a".repeat(40),
      "--expected-version", "0.27.0",
      "--samples", "0",
    ]),
    /positive integer/u,
  );
  assert.match(textPublicationBenchmarkUsage(), /--expected-commit/u);
  assert.match(textPublicationBenchmarkUsage(), /--expected-version/u);
});
