import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { delimiter, join } from "node:path";
import test, { type TestContext } from "node:test";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("../scripts/audit-dependencies.sh", import.meta.url));
const options = { skip: process.platform === "win32" };

interface AuditResult {
  readonly status: number;
  readonly stdout: string;
}

function audit(context: TestContext, results: readonly AuditResult[]) {
  const directory = mkdtempSync(join(tmpdir(), "npm-audit-test-"));
  context.after(() => rmSync(directory, { recursive: true, force: true }));
  const bin = join(directory, "bin");
  const reports = join(directory, "reports");
  mkdirSync(bin);
  writeFileSync(join(directory, "results.json"), JSON.stringify(results));
  writeFileSync(join(directory, "calls"), "");
  writeFileSync(join(directory, "delays"), "");
  writeFileSync(join(bin, "npm"), `#!/bin/sh
exec "$AUDIT_TEST_NODE" "$AUDIT_TEST_DIRECTORY/npm.cjs" "$@"
`, { mode: 0o755 });
  writeFileSync(join(bin, "sleep"), `#!/bin/sh
printf '%s\\n' "$1" >> "$AUDIT_TEST_DIRECTORY/delays"
`, { mode: 0o755 });
  writeFileSync(join(directory, "npm.cjs"), `
const fs = require("node:fs");
const directory = process.env.AUDIT_TEST_DIRECTORY;
const calls = fs.readFileSync(directory + "/calls", "utf8").trim().split("\\n").filter(Boolean);
const results = JSON.parse(fs.readFileSync(directory + "/results.json", "utf8"));
const result = results[calls.length];
fs.appendFileSync(directory + "/calls", JSON.stringify(process.argv.slice(2)) + "\\n");
if (!result) throw new Error("Unexpected extra audit attempt");
process.stdout.write(result.stdout);
process.stderr.write("npm diagnostic " + (calls.length + 1));
process.exitCode = result.status;
`);
  const execution = spawnSync("bash", [script, reports], {
    cwd: directory,
    encoding: "utf8",
    env: {
      ...process.env,
      PATH: `${bin}${delimiter}${process.env.PATH ?? ""}`,
      AUDIT_TEST_NODE: process.execPath,
      AUDIT_TEST_DIRECTORY: directory,
      GITHUB_STEP_SUMMARY: join(directory, "summary"),
    },
    timeout: 10_000,
  });
  assert.ifError(execution.error);
  const calls = readFileSync(join(directory, "calls"), "utf8").trim().split("\n");
  for (const call of calls) {
    assert.deepEqual(JSON.parse(call), [
      "audit", "--package-lock-only", "--include=dev", "--audit-level=info", "--json",
    ]);
  }
  return {
    execution,
    calls,
    reports,
    summary: readFileSync(join(directory, "summary"), "utf8"),
    delays: readFileSync(join(directory, "delays"), "utf8"),
  };
}

const clean = { status: 0, stdout: '{"metadata":{"vulnerabilities":{"total":0}}}' };
const advisory = { status: 1, stdout: '{"metadata":{"vulnerabilities":{"info":1,"total":1}}}' };
const unavailable = { status: 1, stdout: '{"error":{"code":"E503","summary":"Service Unavailable"}}' };

test("a successful npm audit is reported without retries", options, context => {
  const result = audit(context, [clean]);
  assert.equal(result.execution.status, 0);
  assert.equal(result.summary, "### npm audit: no known advisories\n");
  assert.equal(result.calls.length, 1);
  assert.equal(result.delays, "");
  assert.equal(readFileSync(join(result.reports, "attempt-1.json"), "utf8"), clean.stdout);
});

test("an informational advisory fails immediately without retrying", options, context => {
  const result = audit(context, [advisory]);
  assert.equal(result.execution.status, 1);
  assert.equal(result.summary, "### npm audit: advisories found\n");
  assert.match(result.execution.stdout, /::error title=npm audit advisories::/);
  assert.equal(result.calls.length, 1);
  assert.equal(result.delays, "");
});

test("an incomplete audit can recover on the next attempt", options, context => {
  const result = audit(context, [unavailable, clean]);
  assert.equal(result.execution.status, 0);
  assert.equal(result.calls.length, 2);
  assert.equal(result.delays, "10\n");
  assert.equal(readFileSync(join(result.reports, "attempt-1.json"), "utf8"), unavailable.stdout);
  assert.equal(readFileSync(join(result.reports, "attempt-2.json"), "utf8"), clean.stdout);
});

test("an advisory after an incomplete attempt still fails as an advisory", options, context => {
  const result = audit(context, [unavailable, advisory]);
  assert.equal(result.execution.status, 1);
  assert.equal(result.summary, "### npm audit: advisories found\n");
  assert.equal(result.calls.length, 2);
});

for (const [name, failure] of [
  ["endpoint failure", unavailable],
  ["malformed output", { status: 1, stdout: "not JSON" }],
  ["missing report", { status: 1, stdout: "" }],
  ["incomplete report with advisory counts", {
    status: 1,
    stdout: '{"error":{"code":"E503"},"metadata":{"vulnerabilities":{"total":1}}}',
  }],
] as const) {
  test(`${name} remains a visible incomplete audit after bounded backoff`, options, context => {
    const result = audit(context, [failure, failure, failure]);
    assert.equal(result.execution.status, 2);
    assert.equal(result.summary, "### npm audit: incomplete after three attempts\n");
    assert.match(result.execution.stdout, /::error title=npm audit incomplete::/);
    assert.equal(result.calls.length, 3);
    assert.equal(result.delays, "10\n30\n");
    assert.equal(readdirSync(result.reports).length, 6);
    for (let attempt = 1; attempt <= 3; attempt++) {
      assert.equal(readFileSync(join(result.reports, `attempt-${attempt}.json`), "utf8"), failure.stdout);
      assert.equal(readFileSync(join(result.reports, `attempt-${attempt}.stderr`), "utf8"), `npm diagnostic ${attempt}`);
    }
  });
}
