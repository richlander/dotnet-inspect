import assert from "node:assert/strict";
import test from "node:test";
import {
  callGraphDiagnosticsMessage,
  dependencyGroupSelectionMessage,
  dependencyGraphGroupSelectionIndex,
  MARKDOWN_SANITIZE_OPTIONS,
  MAX_WORKSPACE_PACKAGES,
  packageIdentityKey,
  parameterTitleHtml,
  removeWorkspacePackage,
  retainWorkspacePackage,
  scopedRequestState,
  selectedDependencyGroup,
} from "../src/data.ts";
import type { CallGraphDiagnostics } from "../src/data.ts";

import {
  packageAt,
  appSource,
  workspaceNavigationSource,
  graphSource,
  packageControlsSource,
  workspaceSubjectSource,
} from "./composition-root-test-fixture.ts";
const engineCallGraphDiagnostics = (
  fixture: CallGraphDiagnostics & {
    isIncomplete?: boolean;
    hasUnexploredTraversalBoundary?: boolean;
  },
): CallGraphDiagnostics => fixture;

test("call graph diagnostics distinguish failures from expected bounds", () => {
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 2,
    incompleteEdges: 1,
    bindingIdentityConflicts: 3,
    hasUnexploredTraversalBoundary: true
  })), "Partial call graph: 2 incomplete nodes, 1 incomplete edge, and 3 binding identity conflicts.");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true
  })), "");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasAnalysisFailureBoundary: true
  })), "Partial call graph: one or more method bodies could not be analyzed.");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 1,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true,
    hasAnalysisFailureBoundary: true
  })), "Partial call graph: 1 incomplete node and one or more method bodies could not be analyzed.");
  assert.equal(
    callGraphDiagnosticsMessage(engineCallGraphDiagnostics({ isIncomplete: false })),
    "");
});

test("parameter titles preserve generic identities and contain metadata text", () => {
  assert.equal(
    parameterTitleHtml([
      { type: "System.Collections.Generic.Dictionary<System.String, Example.Widget>" },
      { type: '<img src=x onerror="alert(1)">' }
    ]),
    "(System.Collections.Generic.Dictionary&lt;System.String, Example.Widget&gt;, &lt;img src=x onerror=&quot;alert(1)&quot;&gt;)");
});

test("package Markdown has no styling or resource-loading authority", () => {
  for (const tag of ["style", "img", "iframe", "video", "audio", "source", "link", "svg"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_TAGS.includes(tag), false);
  }
  for (const attribute of ["style", "src", "srcset", "href", "poster", "class", "id"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_ATTR.includes(attribute), false);
  }
});

test("workspace package models retain the active and newest coordinates within the limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  assert.ok(active, "the workspace fixture must hold at least one package");
  const incoming = packageAt("13.0.0", "net10.0");

  const retained = retainWorkspacePackage(packages, active, incoming);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), true);
  assert.equal(retained.packages.includes(incoming), true);
  assert.deepEqual(retained.evicted, [packages[1]]);
});

test("workspace package replacement reuses its slot at the package limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  assert.ok(active, "the workspace fixture must hold at least one package");
  const replacement = packageAt("99.0.0", "net10.0");

  const retained = retainWorkspacePackage(
    packages,
    active,
    replacement,
    active);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), false);
  assert.equal(retained.packages.includes(replacement), true);
  assert.deepEqual(retained.evicted, [active]);
});

test("closing a package removes its coordinate and selects the adjacent coordinate", () => {
  const first = packageAt("1.0.0", "net8.0");
  const active = packageAt("2.0.0", "net9.0");
  const last = packageAt("3.0.0", "net10.0");

  const removed = removeWorkspacePackage(
    [first, active, last],
    active,
    packageIdentityKey(active));

  assert.deepEqual(removed.packages, [first, last]);
  assert.equal(removed.active, last);
  assert.equal(removed.closed, active);

  const only = removeWorkspacePackage(
    [active],
    active,
    packageIdentityKey(active));
  assert.deepEqual(only.packages, []);
  assert.equal(only.active, null);

  const missing = removeWorkspacePackage(
    [first, active, last],
    active,
    "Missing.Package\u00001.0.0\u0000net10.0");
  assert.deepEqual(missing.packages, [first, active, last]);
  assert.equal(missing.active, active);
  assert.equal(missing.closed, null);
});

test("workspace UI routes replacements and restore notices through bounded paths", () => {
  assert.match(
    packageControlsSource,
    /onFrameworkSelect\(framework\.value, "legacy"\)/);
  assert.match(
    appSource,
    /selectFramework: \(framework, source\) => \{\s*if \(contentFrameUsesPush\(\)\) contentFramePane = "detail";\s*observeAsync\(\s*switchPackageFramework\(\s*framework,\s*source === "legacy" \? "framework" : "package-framework"\),\s*"Switching the package framework"\);\s*\}/);
  assert.match(appSource, /switchPackageFramework\(argument\)/);
  assert.doesNotMatch(
    appSource,
    /loadPackage\(state\.package\.id, state\.package\.version, (?:button\.dataset\.frameworkChip|argument)\)/);
  assert.match(
    appSource,
    /location: loc,\s+navigationSeq,\s+queryNotice: state\.queryNotice/);
  assert.match(
    appSource,
    /clearWorkspacePackages\(\);\s+render\(\);/);
  assert.match(
    appSource,
    /if \(loc\.tabs\?\.length && !workspaceCoordinatesMatch\(state\.packages, loc\.tabs\)\) \{\s+observeAsync\(\s*restoreHistoryWorkspace\(\)/);
  assert.match(
    appSource,
    /for \(const packageModel of discarded\)\s+releasePackageModelCaches\(packageModel\);/);
  assert.match(
    appSource,
    /type: type\.queryId \?\? type\.id,\s+typeIdentity: type\.definitionId \?\? type\.id/);
  assert.match(
    workspaceNavigationSource,
    /if \(!pkg && tabs\.length\) \{\s+const target = tabs\[/);
  assert.match(
    appSource,
    /function clearNavigationError\(\) \{\s+if \(state\.engineStartupFailed\) return;\s+state\.error = "";\s+state\.errorTitle = "";\s+state\.errorDetail = "";\s+state\.retryAction = null;\s+\}/);
  assert.match(
    appSource,
    /if \(isCreditsPath\(location\.pathname\)\) \{\s+clearNavigationError\(\);[\s\S]*if \(bareHome\) \{[\s\S]*clearNavigationError\(\)/);
  assert.match(
    appSource,
    /function toggleCreditsTheme\(\): "light" \| "dark" \{\s+setTheme\(state\.theme === "dark" \? "light" : "dark", false\);\s+return state\.theme === "light" \? "light" : "dark";\s+\}[\s\S]*onToggleTheme: toggleCreditsTheme/);
  assert.match(
    appSource,
    /onRetry: \(\) => \{\s*if \(state\.retryAction === retryUnavailable\) return;\s*observeAction\(\s*state\.retryAction \?\? bootstrap,\s*"Retrying the inspection"\);\s*\}/);
  assert.match(
    appSource,
    /platformLibraryRetry = options\.retryAction/);
  assert.match(
    appSource,
    /state\.retryAction = retry/);
  assert.match(
    appSource,
    /appendQueryNotice\(\s+friendly\.message,\s+options\.retryAction/);
  assert.doesNotMatch(
    workspaceSubjectSource,
    /data-workspace-close=/);
  assert.doesNotMatch(
    appSource,
    /onClose: closeWorkspacePackage/);
  assert.match(
    appSource,
    /onSelect: selectRetainedWorkspace,\s+onActivateWorkspace: selectRetainedWorkspace,\s+onDeleteWorkspace: deleteRetainedWorkspace,\s+onActivate: action =>\s+observeAction\(\s+\(\) => activateWorkspacePackageOccurrence\(action\)/);
  assert.match(
    appSource,
    /function selectRetainedWorkspace\(workspaceId: string\): void \{\s*navigationSequence\.begin\(\)/);
  assert.match(
    appSource,
    /function deleteRetainedWorkspace\(workspaceId: string\): void \{\s*try \{\s*navigationSequence\.begin\(\)/);
  assert.match(
    appSource,
    /onScopeSelect: target => \{[\s\S]*if \(target === "workspace"\) \{\s*navigationSequence\.begin\(\);/);
  assert.match(
    appSource,
    /const revision = workspaceOccurrenceRevision;[\s\S]*superseded = view\.superseded;[\s\S]*const ownsCurrentRequest =\s*revision === workspaceOccurrenceRevision\s*&& signature === state\.workspaceOccurrenceSignature;[\s\S]*const desiredSignature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);[\s\S]*!state\.workspaceOccurrenceLoading[\s\S]*state\.workspaceOccurrenceSignature !== desiredSignature/);
  assert.match(
    appSource,
    /if \(!workspaceOccurrenceViewIsVisible\(\)\s*&& \(state\.workspaceOccurrenceSignature\s*\|\| state\.workspaceOccurrences\)\) \{\s*clearWorkspaceOccurrenceView\(\)/);
  assert.match(
    appSource,
    /function packageLibraryInventory\(\)[\s\S]*state\.package\.assemblies\.map\(assembly =>/);
  assert.match(
    appSource,
    /function packageLibraries\(\) \{\s*return packageLibraryInventory\(\);\s*\}/);
  assert.match(
    appSource,
    /function currentLibraryQueryMatchIds\(\)[\s\S]*inspection\.content\.results\.map\(result => result\.assetId\)/);
  assert.match(
    appSource,
    /const matchingLibraryIds = currentLibraryQueryMatchIds\(\);[\s\S]*renderLibrarySubjectNav\(\{[\s\S]*libraries: packageLibraries\(\)[\s\S]*matchingLibraryIds/);
  assert.match(
    appSource,
    /assemblyDescriptorForType\(pkg\.assemblies, type\)/);
  assert.match(
    appSource,
    /activatePackage\((?:target|source\.focusPackage), \{ resetAccessibility: true \}\)/);
});

test("member documentation state is scoped to the exact request", () => {
  assert.deepEqual(
    scopedRequestState("package-a\u0000member-a", "package-b\u0000member-b", false, "bad XML"),
    { loading: false, error: "" });
  assert.deepEqual(
    scopedRequestState("same", "same", true, ""),
    { loading: true, error: "" });
});

test("dependency selection exposes a missing exact framework", () => {
  assert.equal(
    dependencyGroupSelectionMessage({
      dependencyGroupError: "No exact dependency group."
    }),
    "No exact dependency group.");
  assert.equal(dependencyGroupSelectionMessage({}), "");
});

test("dependency group selection resets when package identity changes", () => {
  assert.match(
    appSource,
    /const changed = !packageIdentityEquals\(state\.package, pkg\);\s+state\.workspaceSubjectOpen = false;\s+state\.package = pkg;\s+if \(changed\) \{[\s\S]*state\.dependenciesGroupIndex = null;[\s\S]*\}/);
});

test("missing exact dependency groups never create graph edges", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [{
      index: 0,
      framework: "net9.0",
      isActive: false,
      dependencies: [{ id: "Wrong.Dependency", versionRange: "1.0.0" }]
    }]
  };

  assert.equal(selectedDependencyGroup(data), null);
});

test("dependency graph honors explicit selection after an exact group miss", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: false, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
});

test("dependency graph does not turn display fallback into explicit selection", () => {
  const missingExact = {
    dependencyGroupError: "No exact dependency group."
  };

  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, null, 0),
    null);
  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, 1, 0),
    1);
  assert.equal(
    dependencyGraphGroupSelectionIndex({}, null, 1),
    1);
  assert.match(
    graphSource,
    /dependencyGraphGroupSelectionIndex\(\s*model\.packageDependencies,\s*model\.dependenciesGroupIndex,\s*fallbackGroupIndex\)/);
});

test("dependency graph uses each cached package's product-selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "any", isActive: false, dependencies: [] },
      { index: 1, framework: "any", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data),
    data.dependencyGroups[1]);
});

test("dependency graph uses the active package's explicitly selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
  assert.match(
    graphSource,
    /selectedDependencyGroup\(\s*model\.packageDependencies,\s*selectedGroupIndex\)/);
});
