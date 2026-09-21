import assert from "node:assert/strict";
import test from "node:test";

import { renderPackageInfo } from "../src/package-info.ts";
import type {
  BrowserPackageInfoMeasurementInspection,
} from "../src/facades/inspect-web-package.d.ts";

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;");

function inspection(
  content: BrowserPackageInfoMeasurementInspection["content"],
): BrowserPackageInfoMeasurementInspection {
  return {
    content,
    contentKind: "result",
    portableProjection: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: "package-info-measurements/share",
      reason: "notSupported",
      explanation: "No canonical Workspace share projection.",
    },
    diagnostics: [],
  };
}

test("Package Info renders shared selected-slice measurements", () => {
  const html = renderPackageInfo(inspection({
    status: "Measured",
    packageId: "Example.Package",
    packageVersion: "1.2.3",
    compressedPackageBytes: 4096,
    selectedTargetFramework: "net8.0",
    availableTargetFrameworks: ["net8.0", "net7.0", "net6.0"],
    selectedTargetFrameworkFolders: ["lib", "runtimes", String.raw`\u202Eevil`],
    selectedLibraryPayloadBytes: 1536,
    selectedLibraryCount: 2,
    detail: null,
    unavailableReason: null,
    hasSelectedSlice: true,
  }), escapeHtml);

  assert.match(html, /Package Size \(compressed\)/);
  assert.match(html, /4 KB/);
  assert.match(html, /Selected TFM/);
  assert.match(html, /net8\.0/);
  assert.match(html, /lib, runtimes, \\u202Eevil/);
  assert.match(html, /Selected-TFM Library Count/);
  assert.match(html, /1\.5 KB/);
  assert.match(html, /TFMs/);
  assert.match(html, /net8\.0, net7\.0, net6\.0/);
});

test("Package Info renders typed non-success instead of empty measurements", () => {
  const html = renderPackageInfo(inspection({
    status: "NoApplicableSlice",
    packageId: "Example.Package",
    packageVersion: "1.2.3",
    compressedPackageBytes: 2048,
    selectedTargetFramework: null,
    availableTargetFrameworks: [
      "net8.0",
      String.raw`net6.0\u202EHOSTILE`,
    ],
    selectedTargetFrameworkFolders: null,
    selectedLibraryPayloadBytes: null,
    selectedLibraryCount: null,
    detail: "No compile slice applies to net10.0.",
    unavailableReason: null,
    hasSelectedSlice: false,
  }), escapeHtml);

  assert.match(html, /No Applicable Slice/);
  assert.match(html, /No compile slice applies to net10\.0\./);
  assert.match(html, /net8\.0, net6\.0\\u202EHOSTILE/);
  assert.doesNotMatch(html, /\u202E/);
  assert.doesNotMatch(html, /Selected-TFM Size/);
});
