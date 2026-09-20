import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserPackageSurface,
  BrowserRetainedNavigationAction,
  BrowserRetainedNavigationSnapshot,
  BrowserRetainedNavigationSubject,
  BrowserRetainedWorkspacePosting,
} from "../src/facades/inspect-web-catalog.d.ts";
import { fakeDom } from "./fake-dom.ts";
import {
  bindNavigationDescriptorActions,
  createNavigationDescriptorPresentation,
  resolveNavigationPresentationAction,
} from "../src/navigation-descriptor-presentation.ts";
import {
  captureScopeBarFocus,
  renderApplicationScopeBar,
  renderNavigationDescriptorBar,
} from "../src/scope-bar.ts";
import { renderWorkspaceView } from "../src/workspace-subject.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function subject(
  id: string,
  kind: string,
  label: string,
  parent: string | null,
): BrowserRetainedNavigationSubject {
  return { id, kind, label, summary: `${label} summary`, parent };
}

function action(
  id: string,
  source: string,
  kind = "Subject",
): BrowserRetainedNavigationAction {
  return {
    session: "session",
    generation: "generation",
    id,
    source,
    kind,
  };
}

function packageSurface(
  packageId: string,
  version: string,
  framework: string,
): BrowserPackageSurface {
  return {
    package: packageId,
    version,
    frameworks: [framework],
    activeFramework: framework,
    icon: null,
    defaultAssemblyId: null,
    compileLibrary: {
      status: "Selected",
      targetFramework: framework,
      message: null,
    },
    assemblies: [],
    types: [],
    accessibility: [],
    totalMembers: 0,
    documents: [],
    inspectionErrors: [],
    inspectionError: null,
  };
}

function posting(): BrowserRetainedWorkspacePosting {
  const workspace = subject("workspace", "Workspace", "Workspace", null);
  const packageOne = subject("package-1", "Package", "Duplicate", "workspace");
  const packageTwo = subject("package-2", "Package", "Duplicate", "workspace");
  const library = subject("library", "Library", "Library", "package-2");
  const snapshot: BrowserRetainedNavigationSnapshot = {
    generation: "generation",
    scope: { kind: "Current", runtimeFailure: null },
    workspace,
    activePackage: packageTwo.id,
    activeSubject: library,
    typeInventoryLibraryContext: null,
    packages: [{
      order: 20,
      subject: packageOne,
      packageId: "First",
      version: "1.0.0",
      framework: "net10.0",
      runtimeIdentifier: null,
      realization: "Pending",
      realizationFailure: null,
      state: "Pending",
      isCurrent: false,
      action: null,
    }, {
      order: 10,
      subject: packageTwo,
      packageId: "Second",
      version: "2.0.0",
      framework: "net11.0",
      runtimeIdentifier: null,
      realization: "Ready",
      realizationFailure: null,
      state: "Available",
      isCurrent: true,
      action: null,
    }],
    hierarchy: [{
      kind: "Workspace",
      label: "Workspace",
      subject: workspace,
      state: "Available",
      isActive: false,
      isRetained: true,
      action: action("workspace-action", library.id),
    }, {
      kind: "Package",
      label: "Duplicate",
      subject: packageTwo,
      state: "Available",
      isActive: false,
      isRetained: true,
      action: action("package-hierarchy-action", library.id),
    }, {
      kind: "Library",
      label: "Library",
      subject: library,
      state: "Available",
      isActive: true,
      isRetained: true,
      action: null,
    }, {
      kind: "Type",
      label: "Type",
      subject: null,
      state: "Unavailable",
      isActive: false,
      isRetained: false,
      action: null,
    }, {
      kind: "Member",
      label: "Member",
      subject: null,
      state: "SelectionRequired",
      isActive: false,
      isRetained: false,
      action: null,
    }],
    libraries: [],
    types: [],
    members: [],
    lenses: [{
      facet: {
        id: "library.overview",
        kind: "Library",
        title: "Overview",
        summary: "Library overview",
        order: 0,
        role: null,
      },
      state: "Available",
      isCurrent: true,
      target: {
        id: "library-overview",
        subject: library,
        facet: "library.overview",
      },
      unavailability: null,
      message: null,
      action: null,
    }, {
      facet: {
        id: "library.compare",
        kind: "Library",
        title: "Compare",
        summary: "Library comparison",
        order: 1,
        role: null,
      },
      state: "Failed",
      isCurrent: false,
      target: null,
      unavailability: null,
      message: "Comparison failed",
      action: null,
    }],
    lensOutcome: {
      kind: "Applied",
      basis: "Recommendation",
      subject: library,
      effectiveLens: {
        id: "library-overview",
        subject: library,
        facet: "library.overview",
      },
      request: null,
      preferredRole: null,
      policyFailure: null,
      resolution: null,
      suspension: null,
    },
    diagnostics: [],
  };
  return {
    retainedDefinitionId: "definition",
    label: "Workspace",
    canonicalLocation: "/",
    canonicalPacket: "packet",
    realizationId: "realization",
    publicationOrdinal: 1,
    navigation: {
      operation: "Initialize",
      request: "request",
      snapshot,
      outcome: {
        kind: "Applied",
        rejection: null,
        failureSource: null,
        message: null,
        request: null,
        resolution: null,
        scope: null,
        diagnostics: [],
        coordinateRetention: null,
      },
      synchronization: "SynchronizationRequired",
      authority: {
        session: "session",
        revision: "revision",
        intent: "intent",
        epoch: "epoch",
      },
    },
    packages: [{
      navigationId: "navigation-1",
      consumerPackageSubjectId: packageOne.id,
      surface: packageSurface("First", "1.0.0", "net10.0"),
    }, {
      navigationId: "navigation-2",
      consumerPackageSubjectId: packageTwo.id,
      surface: packageSurface("Second", "2.0.0", "net11.0"),
    }],
    predecessor: null,
    cleanup: null,
  };
}

test("product descriptors own order, labels, status, and exact action identity", () => {
  const value = posting();
  const presentation = createNavigationDescriptorPresentation(value);

  assert.deepEqual(
    presentation.packages.map(item => item.package),
    ["Second", "First"]);
  assert.deepEqual(
    presentation.packages.map(item => item.subject.identity),
    ["package-2", "package-1"]);
  assert.equal(presentation.subjects[0]!.label, "Duplicate");
  assert.equal(presentation.subjects[1]!.current, true);
  assert.equal(presentation.subjects[2]!.state, "Unavailable");
  assert.strictEqual(
    resolveNavigationPresentationAction(
      presentation,
      "package-hierarchy-action"),
    value.navigation.snapshot.hierarchy[1]!.action);
});

test("descriptor bar preserves duplicate labels by identity and shows failures", () => {
  const presentation = createNavigationDescriptorPresentation(posting());
  const html = renderNavigationDescriptorBar({
    subjects: [
      presentation.subjects[0]!,
      {
        ...presentation.subjects[0]!,
        key: "package-other",
        identity: "package-other",
        action: "package-hierarchy-action",
      },
      ...presentation.subjects.slice(1),
    ],
    inspectors: presentation.inspectors,
    escapeHtml,
  });

  assert.match(
    html,
    /data-product-navigation-id="package-2"[\s\S]*>Duplicate</);
  assert.match(
    html,
    /data-product-navigation-id="package-other"[\s\S]*>Duplicate</);
  assert.match(
    html,
    /data-navigation-id="unavailable:Type"[^>]*aria-disabled="true"/);
  assert.match(
    html,
    /Compare<span class="navigation-status"> Failed: Comparison failed<\/span>/);
  assert.match(
    html,
    /data-product-navigation-action="package-hierarchy-action"/);
  assert.match(
    html,
    /data-local-navigation-action="choose-member"[^>]*aria-disabled="false"[^>]*aria-controls="content-navigation-pane"[^>]*>[\s\S]*Choose a member/);
  assert.match(
    html,
    /data-local-navigation-action="choose-member"[^>]*role="menuitem"(?!radio)/);
});

test("Workspace entry and Package rows render product labels, order, and status", () => {
  const descriptorPresentation =
    createNavigationDescriptorPresentation(posting());
  const application = renderApplicationScopeBar(
    null,
    true,
    escapeHtml,
    {
      ...descriptorPresentation.workspace,
      label: "Product Workspace",
    });
  const workspace = renderWorkspaceView({
    occurrences: [],
    navigationPackages: descriptorPresentation.packages,
    packages: [],
    platform: {
      tfm: "net11.0",
      version: "11.0.0",
      includeAllLibraries: false,
      filter: "",
    },
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(
    application,
    /data-application-scope="workspace"[^>]*data-product-navigation-action="workspace-action"[^>]*>Product Workspace</);
  assert.match(
    workspace,
    /data-navigation-order="10"[\s\S]*Second[\s\S]*data-navigation-order="20"[\s\S]*First/);
  assert.match(workspace, /data-navigation-state="Pending"/);
  assert.match(
    workspace,
    /data-product-navigation-id="package-2"[^>]*aria-current="page"/);
  assert.match(workspace, /Second[\s\S]*Current/);
  assert.match(workspace, /data-workspace-platform/);
});

test("descriptor focus capture uses product identity rather than label", () => {
  const target = captureScopeBarFocus(fakeDom.htmlElement({
    dataset: {
      productNavigationItem: "",
      navigationId: "package-2",
      navigationItem: "tab",
    },
  }));

  assert.deepEqual(target, {
    kind: "product-navigation",
    value: "package-2",
    presentation: "tab",
  });
});

test("descriptor action binding returns the exact product action object", () => {
  const value = posting();
  const presentation = createNavigationDescriptorPresentation(value);
  const expected = value.navigation.snapshot.hierarchy[1]!.action!;
  const element = {
    dataset: { productNavigationAction: expected.id },
    addEventListener(
      _type: string,
      listener: EventListener,
    ) {
      listener(fakeDom.event());
    },
  };
  const root = fakeDom.parentNode({
    querySelectorAll: (selector: string) =>
      selector.includes("product-navigation") ? [element] : [],
  });
  let actual: BrowserRetainedNavigationAction | null = null;

  bindNavigationDescriptorActions(
    root,
    presentation,
    {
      activate: selected => actual = selected,
      chooseMember: () => assert.fail("Unexpected local action."),
    });

  assert.strictEqual(actual, expected);
});

test("SelectionRequired invokes only the local Member-choice action", () => {
  const presentation = createNavigationDescriptorPresentation(posting());
  const element = {
    dataset: { localNavigationAction: "choose-member" },
    addEventListener(
      _type: string,
      listener: EventListener,
    ) {
      listener(fakeDom.event());
    },
  };
  const root = fakeDom.parentNode({
    querySelectorAll: (selector: string) =>
      selector.includes("local-navigation") ? [element] : [],
  });
  let choices = 0;

  bindNavigationDescriptorActions(root, presentation, {
    activate: () => assert.fail("SelectionRequired submitted a product action."),
    chooseMember: () => choices++,
  });

  assert.equal(choices, 1);
});

test("lens presentation identity survives availability changes", () => {
  const available = posting();
  const failedSource = posting();
  const failed = {
    ...failedSource,
    navigation: {
      ...failedSource.navigation,
      snapshot: {
        ...failedSource.navigation.snapshot,
        lenses: [{
          ...failedSource.navigation.snapshot.lenses[0]!,
          state: "Failed",
          target: null,
          message: "Overview failed",
        }, ...failedSource.navigation.snapshot.lenses.slice(1)],
      },
    },
  };

  const availablePresentation =
    createNavigationDescriptorPresentation(available);
  const failedPresentation =
    createNavigationDescriptorPresentation(failed);

  assert.equal(
    availablePresentation.inspectors[0]!.key,
    failedPresentation.inspectors[0]!.key);
  assert.equal(
    availablePresentation.inspectors[0]!.key,
    "library.overview");
});

test("package presentation rejects missing and aliased subject joins", () => {
  const original = posting();
  const missing = {
    ...original,
    packages: original.packages.slice(1),
  };
  assert.throws(
    () => createNavigationDescriptorPresentation(missing),
    /omitted Package presentation 'package-1'/);

  const duplicateSource = posting();
  const duplicate = {
    ...duplicateSource,
    packages: [
      duplicateSource.packages[0]!,
      {
        ...duplicateSource.packages[1]!,
        consumerPackageSubjectId: "package-1",
      },
    ],
  };
  assert.throws(
    () => createNavigationDescriptorPresentation(duplicate),
    /duplicate Package presentation 'package-1'/);
});
