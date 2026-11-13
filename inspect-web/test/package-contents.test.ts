import assert from "node:assert/strict";
import test from "node:test";
import {
  createPackageContentsCoordinator,
  packageContentsChildren,
  renderPackageContents,
} from "../src/package-contents.ts";
import type {
  BrowserPackageContentsDocument,
  BrowserPackageContentsFile,
} from "../src/facades/inspect-web-package.d.ts";

const files: BrowserPackageContentsFile[] = [
  { path: "README.md", size: 12 },
  { path: "build/.NETFramework/v4.8/mscorlib.dll", size: 34 },
  { path: "build/.NETFramework/v4.8/FrameworkList.xml", size: 56 },
  { path: "build/.NETFramework/v4.7.2/Other.dll", size: 78 },
  { path: "lib/net8.0/Example.dll", size: 90 },
];

function completedInventory(
  entries: readonly BrowserPackageContentsFile[],
): BrowserPackageContentsDocument {
  return {
    status: "Completed",
    packageId: "Example.Package",
    version: "1.0.0",
    count: entries.length,
    files: [...entries],
    detail: null,
  };
}

test("Package Contents projects only immediate children in ordinal order", () => {
  assert.deepEqual(packageContentsChildren(files, ""), [
    { kind: "directory", path: "build", name: "build" },
    { kind: "directory", path: "lib", name: "lib" },
    { kind: "file", path: "README.md", name: "README.md", size: 12 },
  ]);
  assert.deepEqual(packageContentsChildren(files, "build/.NETFramework/v4.8"), [
    {
      kind: "file",
      path: "build/.NETFramework/v4.8/FrameworkList.xml",
      name: "FrameworkList.xml",
      size: 56,
    },
    {
      kind: "file",
      path: "build/.NETFramework/v4.8/mscorlib.dll",
      name: "mscorlib.dll",
      size: 34,
    },
  ]);
});

test("Package Contents opens DLLs as Libraries and keeps other files inert", () => {
  const html = renderPackageContents({
    status: "ready",
    directory: "build/.NETFramework/v4.8",
    count: files.length,
    files,
  }, value => value.replaceAll("&", "&amp;").replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;").replaceAll(">", "&gt;"));

  assert.match(html, /data-package-content-directory="build\/\.NETFramework\/v4\.8"/);
  assert.match(html, /data-package-content-assembly="build\/\.NETFramework\/v4\.8\/mscorlib\.dll"/);
  assert.match(html, /Open Library/);
  assert.doesNotMatch(html, /data-package-content-assembly="[^"]+FrameworkList\.xml"/);
});

test("Package Contents reports unavailable inventories and retries explicitly", async () => {
  let queries = 0;
  const packageCoordinate = { id: "Example.Package", version: "1.0.0" };
  const coordinator = createPackageContentsCoordinator({
    query: async () => {
      queries++;
      return queries === 1
        ? {
            status: "Unavailable",
            packageId: packageCoordinate.id,
            version: packageCoordinate.version,
            count: 0,
            files: [],
            detail: "The package archive could not be read.",
          }
        : completedInventory(files);
    },
    describeError: error => error instanceof Error ? error.message : String(error),
    render: () => undefined,
  });

  await coordinator.ensure(packageCoordinate);
  assert.deepEqual(coordinator.get(packageCoordinate), {
    status: "failed",
    directory: "",
    error: "The package archive could not be read.",
  });

  coordinator.retry(packageCoordinate);
  await new Promise<void>(resolve => setImmediate(resolve));
  assert.equal(coordinator.get(packageCoordinate).status, "ready");
  assert.equal(queries, 2);
});
