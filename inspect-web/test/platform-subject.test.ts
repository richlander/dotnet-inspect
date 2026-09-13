import assert from "node:assert/strict";
import test from "node:test";
import type { PlatformAssemblyRow, PlatformCatalogTarget } from "../src/platform-index.ts";
import {
  platformInventory, platformLibraryKey, platformLibraryRole, platformTargetKey,
  parsePlatformVersions, requireMatchingPlatformTarget, renderPlatformSubject,
  platformSupportsRuntimeAcquisition,
  platformAssemblyRequest, platformLibraryMatchesDescriptor,
} from "../src/platform-subject.ts";
import { renderScopeBar } from "../src/scope-bar.ts";
import { spotlightResultIdentity } from "../src/spotlight.ts";

const version = "11.0.0-preview.7.26381.103";
const row: PlatformAssemblyRow = {
  tfm: "net11.0", pack: "netcore.app", assembly: "System.Text.Json",
  file: "System.Text.Json.dll", kind: "impl", forwardsTo: null,
  version: "11.0.0.0", publicTypes: 100, inReferencePack: true,
  hasImplementation: true, packVersion: version,
};
const facade = { ...row, assembly: "System.Facade", file: "System.Facade.dll", kind: "facade" as const };
const privateLibrary = { ...row, assembly: "System.Private.CoreLib", file: "System.Private.CoreLib.dll", inReferencePack: false };
const referenceOnly = { ...row, assembly: "System.RefOnly", file: "System.RefOnly.dll", kind: "ref" as const, hasImplementation: false };
const target: PlatformCatalogTarget = {
  tfm: row.tfm,
  version,
  rows: [row, facade, privateLibrary, referenceOnly],
  supplies: [],
};
const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

test("reference membership, not implementation kind or public type count, defines the default inventory", () => {
  assert.deepEqual(platformInventory(target, false, "").map(item => item.assembly),
    ["System.Facade", "System.RefOnly", "System.Text.Json"]);
  assert.equal(platformInventory(target, true, "").length, 4);
  assert.deepEqual(platformInventory(target, true, "PRIVATE"), [privateLibrary]);
  assert.deepEqual(platformInventory(target, false, "PRIVATE"), []);
});

test("roles distinguish facade, implementation, private implementation and unsupported reference without color", () => {
  assert.deepEqual([row, facade, privateLibrary, referenceOnly].map(item => {
    const role = platformLibraryRole(item);
    return [role.id, role.icon, role.label];
  }), [
    ["implementation", "I", "Implementation"], ["facade", "F", "Facade"],
    ["private", "P", "Private implementation"], ["reference", "R", "Reference only"],
  ]);
  const html = renderPlatformSubject({
    target, selection: { tfm: target.tfm, version, includeAllLibraries: true, filter: "" },
    frameworks: [target.tfm], versions: [version],
    catalog: { loading: false, error: "" }, discovery: { loading: false, error: "" },
    warmup: { loading: false, error: "Archive offline" }, opening: { loading: true, error: "" },
    escapeHtml,
  });
  assert.match(html, /System\.Private\.CoreLib/);
  assert.match(html, /data-platform-role="private"><button type="button" data-platform-library=/);
  assert.match(html, /Unsupported: no runtime implementation/);
  assert.match(html, /Archive offline[\s\S]*data-platform-retry="warmup"/);
  assert.doesNotMatch(html, /disabled|data-package-lens|package-version/);
});

test("exact source and version are identity, not the displayed name", () => {
  assert.notEqual(platformLibraryKey(row), platformLibraryKey({ ...row, pack: "aspnetcore.app" }));
  assert.notEqual(platformTargetKey(target), platformTargetKey({ ...target, version: "11.0.0" }));
  assert.notEqual(spotlightResultIdentity({ kind: "platform-lib", assembly: row.assembly, pack: row.pack,
    publicTypes: 100, tfm: target.tfm, version, ranges: [] }),
  spotlightResultIdentity({ kind: "platform-lib", assembly: row.assembly, pack: row.pack,
    publicTypes: 100, tfm: target.tfm, version: "11.0.0", ranges: [] }));
  const renamed = { ...row, file: "PhysicalPayload.dll" };
  assert.equal(platformAssemblyRequest(renamed), "System.Text.Json.dll");
  assert.notEqual(platformLibraryKey(row), platformLibraryKey(renamed));
  const descriptor = {
    id: "exact-library", name: row.assembly, version: row.version,
    culture: null, publicKeyToken: null, asset: "runtimes/linux-x64/native/PhysicalPayload.dll",
    publicTypes: 100, publicMembers: 100, platformPack: row.pack,
  };
  assert.equal(platformLibraryMatchesDescriptor(renamed, descriptor), true);
  assert.equal(platformLibraryMatchesDescriptor(row, descriptor), false);
  assert.equal(platformLibraryMatchesDescriptor(renamed, { ...descriptor, platformPack: "aspnetcore.app" }), false);
});

test("dynamic discovery keeps previews and rejects malformed or mismatched catalogs", () => {
  assert.deepEqual(parsePlatformVersions(["11.0.0", version]), ["11.0.0", version]);
  assert.throws(() => parsePlatformVersions([]), /invalid version list/);
  assert.throws(() => parsePlatformVersions(["11.0.0", null]), /invalid version list/);
  assert.equal(requireMatchingPlatformTarget(target, target.tfm, version), target);
  assert.throws(() => requireMatchingPlatformTarget(target, target.tfm, "11.0.0"), /does not match/);
  assert.equal(platformSupportsRuntimeAcquisition(target), true);
  assert.equal(platformSupportsRuntimeAcquisition({
    ...target, rows: [{ ...referenceOnly, pack: "netstandard" }],
  }), false);
});

test("Platform occupies the root slot without Package lenses or manufactured descendants", () => {
  const html = renderScopeBar({ scope: "platform", availableScopes: ["platform"],
    strip: [], activeStripId: null, stripAttribute: "data-platform-lens", escapeHtml });
  assert.match(html, /data-scope="platform"[^>]*aria-selected="true"/);
  assert.doesNotMatch(html, /data-scope="package"|data-scope="library"|data-inspector-tab/);
  assert.equal(renderScopeBar({ scope: "workspace", availableScopes: ["workspace"],
    strip: [], activeStripId: null, stripAttribute: "data-workspace-lens", escapeHtml }), "");
});
