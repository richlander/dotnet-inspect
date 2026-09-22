import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserRetainedNavigationAction,
  BrowserRetainedNavigationSnapshot,
  BrowserRetainedNavigationSubject,
  BrowserRetainedWorkspacePosting,
  BrowserRetainedWorkspaceSurfaceSummary,
} from "../src/facades/inspect-web-catalog.d.ts";
import { fakeDom } from "./fake-dom.ts";
import {
  bindNavigationDescriptorActions,
  createNavigationDescriptorPresentation,
  resolveNavigationPresentationAction,
  withNavigationPackageDetailFailure,
  withNavigationPlatformDetailFailure,
} from "../src/navigation-descriptor-presentation.ts";
import {
  captureScopeBarFocus,
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

function packageSummary(
  framework: string,
): BrowserRetainedWorkspaceSurfaceSummary {
  return {
    selectedCompileFramework: framework,
    libraryCount: 1,
    typeCount: 1,
    memberCount: 1,
    documentCount: 1,
    hasInspectionNotices: false,
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
      evidence: [],
      action: action("workspace-action", library.id),
    }, {
      kind: "Package",
      label: "Duplicate",
      subject: packageTwo,
      state: "Available",
      isActive: false,
      isRetained: true,
      evidence: [],
      action: action("package-hierarchy-action", library.id),
    }, {
      kind: "Library",
      label: "Library",
      subject: library,
      state: "Available",
      isActive: true,
      isRetained: true,
      evidence: [],
      action: null,
    }, {
      kind: "Type",
      label: "Type",
      subject: null,
      state: "Unavailable",
      isActive: false,
      isRetained: false,
      evidence: [],
      action: null,
    }, {
      kind: "Member",
      label: "Member",
      subject: null,
      state: "SelectionRequired",
      isActive: false,
      isRetained: false,
      evidence: [],
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
    definition: {
      tabs: [],
      contexts: [],
      registrations: [],
      activeTabId: null,
      selectedContextId: null,
    },
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
      contextIndex: 0,
      consumerPackageSubjectId: packageOne.id,
      summary: packageSummary("net10.0"),
    }, {
      navigationId: "navigation-2",
      contextIndex: 1,
      consumerPackageSubjectId: packageTwo.id,
      summary: packageSummary("net11.0"),
    }],
    platforms: [],
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
    inspectors: [
      ...presentation.inspectors,
      {
        ...presentation.inspectors[1]!,
        key: "library.compare.other",
        identity: "library.compare.other",
      },
    ],
    subjectLabel: presentation.subjectLabel,
    lensOutcome: presentation.lensOutcome,
    escapeHtml,
  });

  assert.match(
    html,
    /^<nav class="lensbar" data-scope-bar aria-label="Subjects and inspectors">/);
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
  assert.match(
    html,
    /aria-describedby="inspector-tab-1-navigation-description"/);
  assert.match(
    html,
    /id="inspector-tab-1-navigation-description"[^>]*>Library comparison · library\.compare</);
  assert.match(
    html,
    /id="inspector-tab-2-navigation-description"[^>]*>Library comparison · library\.compare\.other</);
  assert.doesNotMatch(
    html,
    /data-navigation-id="library\.compare"[^>]*aria-controls="inspector-panel"/);
});

test("failed hierarchy items render only their exact descriptor evidence", () => {
  const source = posting();
  const failed = {
    ...source,
    navigation: {
      ...source.navigation,
      snapshot: {
        ...source.navigation.snapshot,
        hierarchy: source.navigation.snapshot.hierarchy.map(descriptor =>
          descriptor.kind === "Type"
            ? {
                ...descriptor,
                state: "Failed",
                evidence: [{
                  kind: "InspectionFailed",
                  library: "library",
                  message: "decode type: invalid",
                }, {
                  kind: "ParticipantFailed",
                  library: "library",
                  message: "metadata type: unavailable",
                }],
              }
            : descriptor),
      },
    },
  };

  const presentation = createNavigationDescriptorPresentation(failed);
  const type = presentation.subjects.find(item => item.kind === "Type");
  const member = presentation.subjects.find(item => item.kind === "Member");
  const html = renderNavigationDescriptorBar({
    subjects: presentation.subjects,
    inspectors: presentation.inspectors,
    subjectLabel: presentation.subjectLabel,
    lensOutcome: presentation.lensOutcome,
    escapeHtml,
  });

  assert.equal(
    type?.evidence,
    "decode type: invalid; metadata type: unavailable");
  assert.equal(member?.evidence, null);
  assert.match(
    html,
    /Type<span class="navigation-status"> Failed: decode type: invalid; metadata type: unavailable<\/span>/);
  assert.doesNotMatch(
    html,
    /Choose a member<span class="navigation-status">[^<]*(?:decode|metadata)/);
});

test("Package rows render product labels, order, and status", () => {
  const descriptorPresentation =
    createNavigationDescriptorPresentation(posting());
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
    workspace,
    /data-navigation-order="10"[\s\S]*Second[\s\S]*data-navigation-order="20"[\s\S]*First/);
  assert.match(workspace, /data-navigation-state="Pending"/);
  assert.match(
    workspace,
    /data-product-navigation-id="package-2"[^>]*aria-current="page"/);
  assert.match(
    workspace,
    /data-product-package-action="navigation-2"/);
  assert.match(workspace, /Second[\s\S]*Current/);
  assert.match(workspace, /data-workspace-platform/);
});

test("Package detail failure decorates only the exact compact row", () => {
  const presentation = createNavigationDescriptorPresentation(posting());
  const failed = withNavigationPackageDetailFailure(
    presentation,
    "package-1",
    "Package details are unavailable.",
  );
  const workspace = renderWorkspaceView({
    occurrences: [],
    navigationPackages: failed.packages,
    packages: [],
    platform: null,
    loading: false,
    error: "",
    escapeHtml,
  });

  test("Platform inventory stays separately typed and keeps row-local failure", () => {
    const source = posting();
    const platformPosting = {
      ...source,
      definition: {
        ...source.definition,
        tabs: [{
          id: "platform-1",
          kind: "group",
          source: ":Platform",
          version: "11.0.0",
          framework: "net11.0",
          runtimeIdentifier: "linux-x64",
        }],
        activeTabId: "platform-1",
      },
      platforms: [{
        navigationId: "platform-1",
        contextIndex: 0,
        family: ".NET",
        runtimeIdentifier: "linux-x64",
        summary: packageSummary("net11.0"),
      }],
    };
    const platformPresentation = withNavigationPlatformDetailFailure(
      createNavigationDescriptorPresentation(platformPosting),
      "platform-1",
      "Platform details are unavailable.",
    );
    const workspaceHtml = renderWorkspaceView({
      occurrences: [],
      navigationPackages: [],
      navigationPlatforms: platformPresentation.platforms,
      packages: [],
      platform: null,
      loading: false,
      error: "",
      escapeHtml,
    });

    assert.deepEqual(platformPresentation.platforms, [{
      order: 0,
      navigationId: "platform-1",
      family: ".NET",
      version: "11.0.0",
      framework: "net11.0",
      runtimeIdentifier: "linux-x64",
      current: true,
      detailFailure: "Platform details are unavailable.",
      summary: packageSummary("net11.0"),
    }]);
    assert.match(workspaceHtml, /data-product-platform-action="platform-1"/);
    assert.match(workspaceHtml, />Platform<\/span><strong>\.NET<\/strong>/);
    assert.match(workspaceHtml, /Platform details are unavailable\./);
    assert.doesNotMatch(workspaceHtml, /<span>NuGet package<\/span>/);
  });

  assert.equal(
    failed.packages.find(item => item.subject.identity === "package-1")
      ?.detailFailure,
    "Package details are unavailable.",
  );
  assert.equal(
    failed.packages.find(item => item.subject.identity === "package-2")
      ?.detailFailure,
    null,
  );
  assert.match(
    workspace,
    /First[\s\S]*Package details are unavailable\./,
  );
  assert.doesNotMatch(
    workspace,
    /aria-label="Inspect Second[^"]*Package details are unavailable\./,
  );
});

test("no-effective-lens outcomes retain status and exact evidence", () => {
  const source = posting();
  const unavailable = {
    ...source,
    navigation: {
      ...source.navigation,
      snapshot: {
        ...source.navigation.snapshot,
        lenses: source.navigation.snapshot.lenses.map(descriptor => ({
          ...descriptor,
          isCurrent: false,
          target: null,
        })),
        lensOutcome: {
          ...source.navigation.snapshot.lensOutcome,
          kind: "Unavailable",
          effectiveLens: null,
          resolution: {
            kind: "Unavailable",
            descriptor: null,
            unavailability: "No compatible lens",
            message: null,
          },
        },
      },
    },
  };
  const unavailablePresentation =
    createNavigationDescriptorPresentation(unavailable);
  const unavailableHtml = renderNavigationDescriptorBar({
    subjects: unavailablePresentation.subjects,
    inspectors: unavailablePresentation.inspectors,
    subjectLabel: unavailablePresentation.subjectLabel,
    lensOutcome: unavailablePresentation.lensOutcome,
    escapeHtml,
  });

  assert.deepEqual(unavailablePresentation.lensOutcome, {
    status: "Lens unavailable",
    evidence: "No compatible lens",
  });
  assert.match(
    unavailableHtml,
    /role="status" aria-label="Library: Lens unavailable: No compatible lens">Lens unavailable: No compatible lens/);
  assert.doesNotMatch(unavailableHtml, /aria-controls="inspector-panel"/);

  const failed = {
    ...unavailable,
    navigation: {
      ...unavailable.navigation,
      snapshot: {
        ...unavailable.navigation.snapshot,
        lensOutcome: {
          ...unavailable.navigation.snapshot.lensOutcome,
          kind: "Failed",
          policyFailure: "EmptyOptions",
          resolution: null,
        },
      },
    },
  };
  const failedPresentation = createNavigationDescriptorPresentation(failed);
  const failedHtml = renderNavigationDescriptorBar({
    subjects: failedPresentation.subjects,
    inspectors: failedPresentation.inspectors,
    subjectLabel: failedPresentation.subjectLabel,
    lensOutcome: failedPresentation.lensOutcome,
    escapeHtml,
  });

  assert.deepEqual(failedPresentation.lensOutcome, {
    status: "Lens failed",
    evidence: "EmptyOptions",
  });
  assert.match(failedHtml, /Lens failed: EmptyOptions/);
});

test("Workspace Package rows retain realization-failure evidence", () => {
  const source = posting();
  const failed = {
    ...source,
    navigation: {
      ...source.navigation,
      snapshot: {
        ...source.navigation.snapshot,
        packages: source.navigation.snapshot.packages.map(
          (descriptor, index) => index === 0
            ? {
                ...descriptor,
                state: "Failed",
                realization: "Failed",
                realizationFailure: "Exact realization failure",
              }
            : descriptor),
      },
    },
  };
  const presentation = createNavigationDescriptorPresentation(failed);
  const html = renderWorkspaceView({
    occurrences: [],
    navigationPackages: presentation.packages,
    packages: [],
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(html, /Failed · Exact realization failure/);
  assert.match(
    html,
    /aria-label="Inspect First 1\.0\.0 net10\.0\. Failed\. Exact realization failure"/);
});

test("descriptor focus capture uses product identity rather than label", () => {
  const target = captureScopeBarFocus(fakeDom.htmlElement({
    dataset: {
      productNavigationItem: "",
      navigationGroup: "subject",
      navigationId: "package-2",
      navigationItem: "tab",
    },
  }));

  assert.deepEqual(target, {
    kind: "product-navigation",
    value: "package-2",
    group: "subject",
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
