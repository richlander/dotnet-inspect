import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryUnsafeSurface } from "../src/library-unsafe.ts";
import type {
  BrowserPackageUnsafeFindings,
  BrowserUnsafeFinding,
} from "../src/facades/inspect-web-analysis.d.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const baseOptions = {
  libraryName: "Test.Assembly",
  assemblyIdentity:
    "Test.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
  assetPath: "lib/net10.0/Test.Assembly.dll",
  coordinate: "net10.0 · Test.Package@1.0.0",
  requireLibrary: false,
  pickerHtml: "",
  fresh: true,
  loading: false,
  error: "",
  data: null,
  escapeHtml,
};

function finding(
  overrides: Partial<BrowserUnsafeFinding> = {},
): BrowserUnsafeFinding {
  return {
    assembly: "Test.Assembly.dll",
    typeId: "Test.Namespace.Widget",
    memberName: "Pin",
    stableSelector: "M:Test.Namespace.Widget.Pin",
    kind: "Pinned local",
    offset: "IL_0004",
    operation: "Pinned local",
    evidence: "Pinned local V_0",
    ...overrides,
  };
}

function result(
  overrides: Partial<BrowserPackageUnsafeFindings> = {},
): BrowserPackageUnsafeFindings {
  return {
    findings: [finding()],
    inspectionError: null,
    nonPublicFindings: 2,
    totalFindings: 3,
    compileLibrary: {
      status: "Selected",
      targetFramework: "net10.0",
      message: null,
    },
    ...overrides,
  };
}

test("unsafe rows are ungraded evidence with stable member navigation", () => {
  const html = renderLibraryUnsafeSurface({
    ...baseOptions,
    data: result({
      findings: [
        finding(),
        finding({
          stableSelector: "F:Test.Namespace.Widget.Pointer",
          memberName: "Pointer",
          kind: "Unsafe signature",
          offset: null,
          operation: "System.Int32*",
          evidence: "Field type contains a pointer",
        }),
      ],
    }),
  });

  assert.match(html, /2 public findings · 2 members · 2 non-public/);
  assert.match(html, /data-unsafe-selector="M:Test\.Namespace\.Widget\.Pin"/);
  assert.match(html, /data-unsafe-assembly="Test\.Assembly\.dll"/);
  assert.match(html, /data-unsafe-type="Test\.Namespace\.Widget"/);
  assert.match(html, /Widget\.Pin/);
  assert.match(html, /IL_0004/);
  assert.match(html, /declaration/);
  assert.match(html, /Pinned local V_0/);
  assert.match(html, /neither audit results nor recommendations/);
  assert.doesNotMatch(html, /severity|confidence|risk|remove|rewrite/i);
});

test("unsafe content and failures are escaped", () => {
  const row = renderLibraryUnsafeSurface({
    ...baseOptions,
    data: result({
      findings: [finding({
        memberName: "<Pin>",
        operation: "<operation>",
        evidence: "<evidence>",
        kind: "<kind>",
      })],
    }),
  });
  const failure = renderLibraryUnsafeSurface({
    ...baseOptions,
    error: "<boom> failed",
  });

  assert.match(row, /Widget\.&lt;Pin&gt;/);
  assert.match(row, /&lt;operation&gt;/);
  assert.match(row, /&lt;evidence&gt;/);
  assert.match(row, /&lt;kind&gt;/);
  assert.doesNotMatch(row, /<Pin>|<operation>|<evidence>|<kind>/);
  assert.match(failure, /&lt;boom&gt; failed/);
});

test("loading and library-selection states remain distinct", () => {
  const selected = renderLibraryUnsafeSurface({
    ...baseOptions,
    requireLibrary: true,
    pickerHtml: "<select><option>PICKER</option></select>",
    fresh: false,
  });
  const fresh = renderLibraryUnsafeSurface({ ...baseOptions, loading: true });
  const stale = renderLibraryUnsafeSurface({
    ...baseOptions,
    loading: true,
    fresh: false,
  });

  assert.match(selected, /Pick a library to analyze/);
  assert.match(fresh, /Analyzing unsafe findings/);
  assert.match(fresh, /without grading or auditing/);
  assert.match(stale, /Loading…/);
});

test("complete and partial empty results make only scoped claims", () => {
  const complete = renderLibraryUnsafeSurface({
    ...baseOptions,
    data: result({ findings: [], totalFindings: 2 }),
  });
  const partial = renderLibraryUnsafeSurface({
    ...baseOptions,
    data: result({
      findings: [],
      inspectionError: "Finding output was truncated.",
    }),
  });

  assert.match(complete, /No public unsafe findings/);
  assert.match(complete, /compiled declaration and IL analysis/i);
  assert.match(complete, /2 findings are on non-public members/);
  assert.doesNotMatch(complete, /library is safe/i);
  assert.match(partial, /Analysis incomplete/);
  assert.match(partial, /Finding output was truncated/);
  assert.match(partial, /partial/);
  assert.doesNotMatch(partial, /No public unsafe findings/);
});

test("partial findings remain navigable with a visible diagnostic", () => {
  const html = renderLibraryUnsafeSurface({
    ...baseOptions,
    data: result({ inspectionError: "<bad> body" }),
  });

  assert.match(html, /This library could not be analyzed completely/);
  assert.match(html, /&lt;bad&gt; body/);
  assert.match(html, /data-unsafe-selector=/);
});
