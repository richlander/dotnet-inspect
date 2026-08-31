import assert from "node:assert/strict";
import {
  readdirSync,
  readFileSync,
  realpathSync,
} from "node:fs";
import {
  basename,
  extname,
  isAbsolute,
  relative,
  resolve,
  sep,
} from "node:path";

const [repositoryArgument, graphArgument, receiptsArgument] =
  process.argv.slice(2);
if (!repositoryArgument || !graphArgument || !receiptsArgument) {
  throw new Error(
    "Usage: verify-runtime-async-project-graph.ts "
      + "<repository> <restore-graph.json> <receipt-directory>");
}

const repository = realpathSync(repositoryArgument);
const graph: unknown = JSON.parse(
  readFileSync(resolve(graphArgument), "utf8"),
);
assert.ok(
  graph !== null && typeof graph === "object",
  "restore graph is not an object");
const projects: unknown = Reflect.get(graph, "projects");
assert.ok(
  projects !== null && typeof projects === "object" && !Array.isArray(projects),
  "restore graph has no projects");

const expected = new Set<string>();
const projectNames = new Set<string>();
for (const projectPath of Object.keys(projects)) {
  const canonical = realpathSync(projectPath);
  const repositoryRelative = relative(repository, canonical);
  assert.ok(
    repositoryRelative !== ".."
      && !repositoryRelative.startsWith(`..${sep}`)
      && !isAbsolute(repositoryRelative),
    `browser engine project graph escaped the repository: ${canonical}`);

  const projectName = basename(canonical, extname(canonical));
  assert.equal(
    projectNames.has(projectName),
    false,
    `browser engine graph has duplicate project name ${projectName}`);
  projectNames.add(projectName);
  expected.add(canonical);
}

const engineProject = realpathSync(
  resolve(repository, "prototypes/inspect-web/engine/InspectWeb.Engine.csproj"));
const serverProject = realpathSync(
  resolve(repository, "prototypes/inspect-web/msdl-proxy/MsdlProxy.csproj"));
assert.ok(expected.has(engineProject), "browser engine graph omitted its root");
assert.equal(
  expected.has(serverProject),
  false,
  "browser engine graph included the separately published server API");

const actual = new Set<string>();
for (const receiptName of readdirSync(receiptsArgument)) {
  assert.match(
    receiptName,
    /^[A-Za-z0-9_.-]+\.txt$/,
    `unexpected runtime-async receipt ${receiptName}`);
  const receiptProjectPath: string = realpathSync(
    readFileSync(resolve(receiptsArgument, receiptName), "utf8").trim());
  assert.equal(
    actual.has(receiptProjectPath),
    false,
    `duplicate runtime-async receipt for ${receiptProjectPath}`);
  actual.add(receiptProjectPath);
}

const missing = [...expected].filter(project => !actual.has(project)).sort();
const unexpected = [...actual].filter(project => !expected.has(project)).sort();
assert.deepEqual(
  { missing, unexpected },
  { missing: [], unexpected: [] },
  "runtime-async compile receipts do not match the browser engine project graph");

console.log(
  `Runtime async reached all ${expected.size} repository projects in the `
    + "browser engine graph.");
