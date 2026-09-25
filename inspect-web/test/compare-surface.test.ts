import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";
import {
  bindCompareFrame,
  renderCompareFrame,
  restoreCompareTabFocus,
} from "../src/compare-surface.ts";
import { isCompareMode } from "../src/package-comparison-targets.ts";
import { fakeDom } from "./fake-dom.ts";

const appSource = readFileSync(
  new URL("../src/dotnet-inspect.ts", import.meta.url),
  "utf8",
);
const dataSource = readFileSync(
  new URL("../src/data.ts", import.meta.url),
  "utf8",
);

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");

for (const mode of ["diff", "clone"] as const) {
  test(`${mode} renders one Compare frame with two mode tabs, a target row, and a labelled panel`, () => {
    const html = renderCompareFrame({
      subjectKind: "type",
      subjectLabel: "Example.Widget<T>",
      mode,
      targetText: mode === "diff" ? "1.0.0 → 2.0.0" : "Workspace: 2 loaded Packages",
      status: "Comparison complete.",
      content: "<p>rows</p>",
      escapeHtml,
    });
    assert.equal(html.match(/<h1\b/g)?.length, 1);
    assert.match(html, /<p class="compare-kicker">Compare · Type<\/p>/);
    assert.match(html, /<h1 id="compare-title">Example\.Widget&lt;T&gt;<\/h1>/);
    const header = html.match(/<header\b[^>]*>[\s\S]*?<\/header>/)?.[0] ?? "";
    assert.equal(header.match(/role="tab"/g)?.length, 2);
    assert.match(header, /<p class="compare-status" role="status">Comparison complete\.<\/p>/);
    assert.equal(html.match(/aria-selected="true"/g)?.length, 1);
    assert.match(html, new RegExp(`data-compare-mode="${mode}" aria-selected="true" aria-controls="compare-panel" tabindex="0"`));
    assert.match(html, new RegExp(`role="tabpanel" aria-labelledby="compare-mode-${mode}"`));
    assert.match(html, mode === "diff"
      ? /Diff baseline<\/span>\s*<span class="compare-target-value">1\.0\.0 → 2\.0\.0<\/span>/
      : /Clone scope<\/span>\s*<span class="compare-target-value">Workspace: 2 loaded Packages<\/span>/);
    assert.match(html, /id="compare-change-target">Change target</);
    assert.equal(html.match(/class="compare-status"/g)?.length, 1);
    assert.match(html, /<p>rows<\/p>/);
    // Compare explains the Package-owned target; it never renders a second editor.
    assert.doesNotMatch(html, /<select/);
  });
}

test("Compare modes are a closed two-value vocabulary", () => {
  assert.equal(isCompareMode("diff"), true);
  assert.equal(isCompareMode("clone"), true);
  assert.equal(isCompareMode("omni"), false);
  assert.equal(isCompareMode(undefined), false);
});

test("Diff and Clone are modes inside Compare, not separate inspectors or lenses", () => {
  const catalogs = dataSource.match(/const lenses = \[[\s\S]*?\] as const;[\s\S]*?export const memberSectionDefinitions = \[[\s\S]*?\] as const;/)?.[0] ?? "";
  assert.match(catalogs, /\["compare", "Compare"\]/);
  assert.doesNotMatch(catalogs, /"clone"|"diff"/);
});

test("tab bindings use manual activation with Home/End and arrow focus movement", () => {
  const selected: string[] = [];
  const focused: string[] = [];
  const tabs = ["diff", "clone"].map(mode => {
    const listeners = new Map<string, (event: unknown) => void>();
    return {
      dataset: { compareMode: mode },
      tabIndex: mode === "diff" ? 0 : -1,
      addEventListener: (type: string, listener: (event: unknown) => void) => {
        listeners.set(type, listener);
      },
      focus: () => focused.push(mode),
      fire: (type: string, event: object) => listeners.get(type)?.(event),
    };
  });
  const [diff, clone] = tabs;
  if (!diff || !clone) throw new Error("Expected two tabs.");
  let changeTargets = 0;
  let retries = 0;
  bindCompareFrame(fakeDom.parentNode({
    querySelectorAll: (selector: string) =>
      selector === "[data-compare-mode]" ? tabs : [],
    querySelector: (selector: string) =>
      selector === "#compare-change-target"
        ? { addEventListener: (_: string, listener: () => void) => { changeTargets++; listener(); } }
        : selector === "#compare-retry"
          ? { addEventListener: (_: string, listener: () => void) => { retries++; listener(); } }
          : null,
  }), {
    selectMode: mode => selected.push(mode),
    changeTarget: () => selected.push("change-target"),
    retry: () => selected.push("retry"),
  });
  assert.equal(changeTargets, 1);
  assert.equal(retries, 1);

  const key = (name: string) => ({
    key: name,
    preventDefault: () => {},
    stopPropagation: () => {},
  });
  diff.fire("keydown", key("ArrowRight"));
  assert.deepEqual(focused, ["clone"]);
  assert.deepEqual([diff.tabIndex, clone.tabIndex], [-1, 0]);
  // Focus movement never selects a mode; selection is the tab's own click.
  assert.deepEqual(selected, ["change-target", "retry"]);
  clone.fire("keydown", key("ArrowRight"));
  clone.fire("keydown", key("Home"));
  diff.fire("keydown", key("End"));
  diff.fire("keydown", key("ArrowLeft"));
  assert.deepEqual(focused, ["clone", "diff", "diff", "clone", "clone"]);
  clone.fire("click", {});
  diff.fire("click", {});
  assert.deepEqual(selected, ["change-target", "retry", "clone", "diff"]);

  focused.length = 0;
  restoreCompareTabFocus(fakeDom.parentNode({
    querySelectorAll: () => tabs,
  }), "clone");
  assert.deepEqual(focused, ["clone"]);
  assert.deepEqual([diff.tabIndex, clone.tabIndex], [-1, 0]);
});

test("the app retains Compare mode per Package and reconciles both modes from render", () => {
  assert.match(
    appSource,
    /function selectCompareMode\(mode: CompareMode\) \{\s*const pkg = state\.package;\s*if \(!pkg \|\| currentCompareMode\(\) === mode\) return;\s*packageComparisonTargets\.selectMode\(pkg, mode\);/);
  assert.match(
    appSource,
    /libraryApiDiff\.reconcile\(currentLibraryApiDiffSelection\(\)\);\s*compareClone\.reconcile\(currentCompareCloneTarget\(\)\);/);
  // Compare is the same surface at Library, Type, and Member.
  assert.match(appSource, /case "compare": return renderCompareSurface\(\);/);
  assert.match(appSource, /case "compare":\s*return renderCompareSurface\(\);/);
  assert.match(appSource, /state\.memberSection === "compare"\) \{\s*content = renderCompareSurface\(\);/);
  // Drill-down installs the destination subject and the Compare lens in one
  // transition without an intermediate lens.
  const activateType = appSource.match(/function activateCompareType\([\s\S]*?\n\}/)?.[0] ?? "";
  assert.match(activateType, /enterTypeSubject\(target\);[\s\S]*state\.lens = "compare";\s*state\.compareCloneSelectedRank = null;\s*render\(\);/);
  assert.equal(activateType.match(/render\(\)/g)?.length, 1);
  const activateMember = appSource.match(/function activateCompareMember\([\s\S]*?\n\}/)?.[0] ?? "";
  assert.match(activateMember, /navigateToMember\(\s*subject\.pkg,\s*type,\s*match\.group,[\s\S]*"compare"\);/);
  assert.match(activateMember, /overload\.anchorDigest !== memberFingerprint/);
  assert.doesNotMatch(activateMember, /textContent|innerText|display/);
  // Change target returns to Package Overview's Comparison targets work area.
  assert.match(appSource, /const control = currentCompareMode\(\) === "clone"\s*\? "#package-clone-target"\s*: "#package-diff-target";/);
});
