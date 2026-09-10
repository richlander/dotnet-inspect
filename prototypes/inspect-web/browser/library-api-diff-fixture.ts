import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { storedZip } from "./package-adoption-nupkg.ts";

// Wraps the real, compiler-produced LibraryApiDiff.V1/V2 fixture assemblies (#6423) — the
// same fixture pair BrowserLibraryApiDiffOperationTests and
// LibraryApiDiffPresentationTests exercise directly — in a minimal in-memory nupkg, so the
// published browser site can acquire them exactly as it would any other cataloged Gallery
// package. Nothing here reconstructs a changed-Type count, a compatibility change, or a
// member relation; only package bytes are supplied.
const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const framework = "net11.0";
const assemblyFileName = "LibraryApiDiffFixture.dll";

function fixtureAssemblyPath(project: "LibraryApiDiff.V1" | "LibraryApiDiff.V2"): string {
  return join(repoRoot, "artifacts", "bin", project, "release", assemblyFileName);
}

/** Builds the V1 (Before) fixture package bytes. */
export function libraryApiDiffFixtureV1Nupkg(): Buffer {
  return storedZip([{
    name: `lib/${framework}/${assemblyFileName}`,
    bytes: readFileSync(fixtureAssemblyPath("LibraryApiDiff.V1")),
  }]);
}

/** Builds the V2 (After) fixture package bytes. */
export function libraryApiDiffFixtureV2Nupkg(): Buffer {
  return storedZip([{
    name: `lib/${framework}/${assemblyFileName}`,
    bytes: readFileSync(fixtureAssemblyPath("LibraryApiDiff.V2")),
  }]);
}

/** A syntactically valid but structurally unreadable package, for the incomplete-endpoint case. */
export function libraryApiDiffCorruptNupkg(): Buffer {
  const garbage = new Uint8Array(4096);
  for (let index = 0; index < garbage.length; index++) {
    garbage[index] = (index * 37 + 11) & 0xff;
  }
  return storedZip([{ name: `lib/${framework}/${assemblyFileName}`, bytes: garbage }]);
}

export { framework as libraryApiDiffFixtureFramework };
