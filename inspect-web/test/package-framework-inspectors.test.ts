import assert from "node:assert/strict";
import test from "node:test";
import { isEmptyPackageFramework, packageLensesFor } from "../src/data.ts";

const packageContext = (status: string) => ({
  source: { kind: "nuget.org" },
  packageChildren: { content: { status } },
});

test("explicitly empty framework exposes only Package Overview", () => {
  const pkg = packageContext("SelectedEmpty");
  assert.equal(isEmptyPackageFramework(pkg), true);
  assert.deepEqual(packageLensesFor(pkg), [["overview", "Overview"]]);
});

test("other package outcomes retain their inspectors and failure visibility", () => {
  for (const status of ["Available", "NoCompileAssets", "NoApplicableTarget", "Unavailable"]) {
    const pkg = packageContext(status);
    assert.equal(isEmptyPackageFramework(pkg), false);
    assert.deepEqual(packageLensesFor(pkg).map(([id]) => id),
      ["overview", "compare", "dependencies", "vulnerabilities"]);
  }
});
