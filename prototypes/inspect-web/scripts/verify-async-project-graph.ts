import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  readdirSync,
  readFileSync,
  realpathSync,
  writeFileSync,
} from "node:fs";
import {
  basename,
  extname,
  isAbsolute,
  relative,
  resolve,
  sep,
} from "node:path";

const [
  lowering,
  repositoryArgument,
  graphArgument,
  receiptsArgument,
  resultArgument,
] =
  process.argv.slice(2);
if ((lowering !== "compiler" && lowering !== "runtime")
    || !repositoryArgument
    || !graphArgument
    || !receiptsArgument
    || !resultArgument) {
  throw new Error(
    "Usage: verify-async-project-graph.ts <compiler|runtime> "
      + "<repository> <restore-graph.json> <receipt-directory> <result.json>");
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
const repositoryProjects: string[] = [];
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
  repositoryProjects.push(repositoryRelative.split(sep).join("/"));
}
repositoryProjects.sort();

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
  `${lowering}-async compile receipts do not match the browser engine project graph`);

writeFileSync(
  resolve(resultArgument),
  `${JSON.stringify({
    repository_projects: repositoryProjects,
    repository_project_count: repositoryProjects.length,
    repository_project_sha256: createHash("sha256")
      .update(`${repositoryProjects.join("\n")}\n`)
      .digest("hex"),
  })}\n`,
);
console.log(
  `${lowering === "compiler" ? "Compiler" : "Runtime"} async reached all `
    + `${expected.size} repository projects in the `
    + "browser engine graph.");
