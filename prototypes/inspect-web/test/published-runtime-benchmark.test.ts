import assert from "node:assert/strict";
import test from "node:test";
import {
  benchmarkUsage,
  evaluateBuildComparability,
  isBenchmarkResultAccepted,
  parseBenchmarkArguments,
  summarize,
} from "../scripts/published-runtime-benchmark-model.ts";

test("published runtime benchmark parses explicit sites and bounds", () => {
  assert.deepEqual(parseBenchmarkArguments([
    "--site", "mono=https://dotnet-inspect.ca/",
    "--site", "coreclr=http://127.0.0.1:4175/",
    "--samples", "5",
    "--member-count", "12",
    "--output", "artifacts/report.json",
    "--allow-mismatched-commits",
  ]), {
    sites: [
      { name: "mono", url: "https://dotnet-inspect.ca" },
      { name: "coreclr", url: "http://127.0.0.1:4175" },
    ],
    samples: 5,
    memberCount: 12,
    outputPath: "artifacts/report.json",
    allowMismatchedCommits: true,
    help: false,
  });
});

test("published runtime benchmark rejects ambiguous inputs", () => {
  assert.throws(
    () => parseBenchmarkArguments([]),
    /At least one --site is required/u,
  );
  assert.throws(
    () => parseBenchmarkArguments([
      "--site", "mono=https://dotnet-inspect.ca",
      "--site", "mono=https://example.test",
    ]),
    /Site name 'mono' is duplicated/u,
  );
  assert.throws(
    () => parseBenchmarkArguments(["--site", "Mono=https://example.test"]),
    /must use lowercase letters/u,
  );
  assert.throws(
    () => parseBenchmarkArguments([
      "--site", "mono=https://example.test",
      "--samples", "0",
    ]),
    /--samples must be a positive integer/u,
  );
  assert.match(benchmarkUsage(), /--allow-mismatched-commits/u);
});

test("published runtime benchmark reports deterministic distributions", () => {
  assert.deepEqual(summarize([7, 1, 3, 9, 5]), {
    count: 5,
    minimum: 1,
    median: 5,
    mean: 5,
    p95: 9,
    maximum: 9,
  });
  assert.equal(summarize([2, 4]).median, 3);
  assert.throws(() => summarize([]), /empty sample/u);
  assert.throws(() => summarize([1, Number.NaN]), /finite/u);
});

test("published runtime benchmark requires one stable shared product commit", () => {
  assert.deepEqual(evaluateBuildComparability([
    { site: "mono", commit: "abc" },
    { site: "coreclr", commit: "abc" },
    { site: "mono", commit: "abc" },
    { site: "coreclr", commit: "abc" },
  ]), {
    comparable: true,
    commitsBySite: {
      mono: ["abc"],
      coreclr: ["abc"],
    },
    reasons: [],
  });

  const mismatch = evaluateBuildComparability([
    { site: "mono", commit: "abc" },
    { site: "coreclr", commit: "def" },
  ]);
  assert.equal(mismatch.comparable, false);
  assert.match(mismatch.reasons.join("\n"), /do not share one product commit/u);

  const movement = evaluateBuildComparability([
    { site: "mono", commit: "abc" },
    { site: "mono", commit: "def" },
  ]);
  assert.match(movement.reasons.join("\n"), /changed commits/u);
});

test("diagnostic override permits only build-commit mismatch", () => {
  assert.equal(isBenchmarkResultAccepted(false, true, 0, false), false);
  assert.equal(isBenchmarkResultAccepted(false, true, 0, true), true);
  assert.equal(isBenchmarkResultAccepted(true, false, 0, true), false);
  assert.equal(isBenchmarkResultAccepted(true, true, 1, true), false);
});
