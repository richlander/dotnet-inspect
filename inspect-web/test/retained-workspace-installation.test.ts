import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import {
  appSource,
  functionDeclaration,
} from "./composition-root-test-fixture.ts";
import { createNavigationLocationIntentArbiter } from "../src/navigation-location-intent.ts";

interface Deferred<T> {
  promise: Promise<T>;
  resolve(value: T): void;
  reject(reason: unknown): void;
}

interface TestPackage {
  id: string;
  isRuntimePack: boolean;
  source: { kind: "package" | "platform" };
}

interface TestPresentationItem {
  navigationId: string;
  consumerPackageSubjectId?: string;
  detailFailure: string | null;
}

interface TestPresentation {
  packages: TestPresentationItem[];
  platforms: TestPresentationItem[];
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function retainedWorkspaceInstallationHarness(options: {
  activeKind: "package" | "platform";
  initial: Deferred<{ surface: TestPackage }>;
  newer: () => Promise<{ surface: TestPackage }>;
}) {
  const activeNavigationId = `initial-${options.activeKind}`;
  const newerNavigationId = `newer-${options.activeKind}`;
  const item = (navigationId: string): TestPresentationItem => ({
    navigationId,
    ...(options.activeKind === "package"
      ? { consumerPackageSubjectId: `${navigationId}-subject` }
      : {}),
    detailFailure: null,
  });
  const inventories = [item(activeNavigationId), item(newerNavigationId)];
  const posting = {
    definition: { activeTabId: activeNavigationId },
    packages: options.activeKind === "package" ? inventories : [],
    platforms: options.activeKind === "platform" ? inventories : [],
    realizationId: "realization",
    retainedDefinitionId: "definition",
    canonicalLocation: "/workspace",
    navigation: { synchronization: "Current" },
  };
  const state = {
    packages: [] as TestPackage[],
    package: null as TestPackage | null,
    platformSelection: null as {
      tfm: string;
      version: string;
      includeAllLibraries: boolean;
      filter: string;
    } | null,
    rootKind: null as "package" | "platform" | null,
    workspaceSubjectOpen: true,
    atPackageRoot: true,
    atLibraryRoot: false,
    loading: true,
    home: false,
    credits: false,
    packageQueryOpen: false,
    packageActivityOpen: false,
    memberCallGraphSeq: 0,
    memberCallGraphExpanding: false,
    error: "",
    errorTitle: "",
    errorDetail: "",
    retryAction: null,
  };
  let sequence = 1;
  let renders = 0;
  let focusSchedules = 0;
  let workspaceFocuses = 0;
  let initialAdmissions = 0;
  const historyWrites: string[] = [];
  const routedLocations: string[] = [];
  const location = { href: "/incumbent" };
  const history = {
    state: {},
    pushState(_data: unknown, _unused: string, url?: string | URL | null) {
      historyWrites.push(String(url));
      location.href = String(url);
    },
    replaceState(_data: unknown, _unused: string, url?: string | URL | null) {
      historyWrites.push(String(url));
      location.href = String(url);
    },
  };
  const retainedLocationIntents = createNavigationLocationIntentArbiter();
  const locationIntent =
    retainedLocationIntents.admitNonBrowser("push", null, null);
  const navigationSequence = {
    current: () => sequence,
    isCurrent: (candidate: number) => candidate === sequence,
    begin: () => ++sequence,
  };
  const updateFailure = (
    presentation: TestPresentation,
    navigationId: string,
    failure: string | null,
  ): TestPresentation => ({
    packages: presentation.packages.map(candidate =>
      candidate.navigationId === navigationId
        || candidate.consumerPackageSubjectId === navigationId
        ? { ...candidate, detailFailure: failure }
        : candidate),
    platforms: presentation.platforms.map(candidate =>
      candidate.navigationId === navigationId
        ? { ...candidate, detailFailure: failure }
        : candidate),
  });
  const context = {
    activeRetainedWorkspacePosting: null as typeof posting | null,
    activeWorkspaceUrl: null as string | null,
    installedRetainedLocation: null as unknown,
    retainedWorkspacePresentation: null as TestPresentation | null,
    retainedWorkspaceInitialDetailAuthority: null as {
      realizationId: string;
      navigationSeq: number;
      presentationCurrent: boolean;
    } | null,
    retainedWorkspacePostings: new Map<string, typeof posting>(),
    state,
    navigationSequence,
    document: { activeElement: null },
    HTMLElement: Object,
    captureWorkspaceFocus: () => null,
    focusApplicationMenuButton: () => {},
    parkActiveCompatibilityWorkspace: () => {},
    createNavigationDescriptorPresentation: () => ({
      packages: options.activeKind === "package" ? inventories : [],
      platforms: options.activeKind === "platform" ? inventories : [],
    }),
    prepareUnpublishedWorkspace: () => {},
    admitRetainedPackage: (
      _posting: unknown,
      inventory: TestPresentationItem,
    ) => {
      if (inventory.navigationId !== activeNavigationId) {
        return options.newer();
      }
      initialAdmissions += 1;
      return options.initial.promise;
    },
    admitRetainedPlatform: (
      _posting: unknown,
      inventory: TestPresentationItem,
    ) => {
      if (inventory.navigationId !== activeNavigationId) {
        return options.newer();
      }
      initialAdmissions += 1;
      return options.initial.promise;
    },
    createNuGetPackageModel: (surface: TestPackage) => surface,
    createRuntimePackageModel: (surface: TestPackage) => surface,
    errorMessage: (error: unknown) =>
      error instanceof Error ? error.message : String(error),
    packageIdentityKey: (candidate: TestPackage) => candidate.id,
    selectWorkspacePackage: (
      selected: TestPackage,
      _selection: unknown,
    ) => {
      state.package = selected;
      state.rootKind = selected.source.kind;
      state.workspaceSubjectOpen = false;
    },
    withNavigationPackageDetailFailure: updateFailure,
    withNavigationPlatformDetailFailure: updateFailure,
    retainedLocationHistoryState: () => ({ definition: "definition" }),
    retainedLocationIntents,
    retainedLocationFallbackAssociation: () => ({
      identity: Symbol("fallback"),
      canonicalLocation: "/incumbent",
      historyState: {},
    }),
    history,
    location,
    invalidateGraphMemberNavigation: () => {},
    clearNavigationError: () => {},
    clearWorkspaceRouteFailure: () => true,
    discardPackageQueryTermEditors: () => {},
    packageQueryController: { cancel: () => {} },
    packageChangesController: { cancel: () => {} },
    spotlight: { reset: () => {} },
    workspaceLocation: {
      push: (url: string) => {
        routedLocations.push(url);
        location.href = url;
        return true;
      },
    },
    render: () => {
      renders += 1;
    },
    afterCurrentNavigationFrame: (action: () => void) => {
      focusSchedules += 1;
      action();
    },
    focusWorkspaceOrHeading: () => {
      workspaceFocuses += 1;
    },
    posting,
    locationIntent,
    newerNavigationId,
  };
  const declarations = [
    "postRetainedWorkspace",
    "retainedLocationPresentationCurrent",
    "supersedeRetainedLocationIntentForRoutedNavigation",
    "installRetainedWorkspacePosting",
    "completeRetainedActivationPresentation",
    "activateRetainedPackageAction",
    "activateRetainedPlatformAction",
    "goHome",
    "openCredits",
  ].map(name => {
    const declaration = functionDeclaration(name);
    return appSource.slice(declaration.start, declaration.end);
  }).join("\n");
  runInNewContext(stripTypeScriptTypes(declarations), context);

  return {
    context,
    state,
    posting,
    activeNavigationId,
    newerNavigationId,
    historyWrites: () => historyWrites,
    routedLocations: () => routedLocations,
    renders: () => renders,
    focusSchedules: () => focusSchedules,
    workspaceFocuses: () => workspaceFocuses,
    initialAdmissions: () => initialAdmissions,
    presentationCurrent: () => Boolean(runInNewContext(
      "retainedLocationPresentationCurrent(locationIntent, posting.canonicalLocation)",
      context,
    )),
    post: (presentationCurrent = true) => {
      runInNewContext(
        `postRetainedWorkspace(posting, ${presentationCurrent})`,
        context,
      );
    },
    install: () => Promise.resolve(runInNewContext(
      "installRetainedWorkspacePosting(posting, locationIntent)",
      context,
    )),
    completePresentation: () => {
      runInNewContext(
        "completeRetainedActivationPresentation({ posting }, locationIntent)",
        context,
      );
    },
    activatePackage: () => Promise.resolve(runInNewContext(
      "activateRetainedPackageAction(newerNavigationId)",
      context,
    )),
    activatePlatform: () => Promise.resolve(runInNewContext(
      "activateRetainedPlatformAction(newerNavigationId)",
      context,
    )),
    goHome: () => {
      runInNewContext("goHome()", context);
    },
    openCredits: () => {
      runInNewContext("openCredits()", context);
    },
  };
}

test("stale initial Package detail cannot replace a newer row selection", async () => {
  const initial = deferred<{ surface: TestPackage }>();
  const newer: TestPackage = {
    id: "newer-package",
    isRuntimePack: false,
    source: { kind: "package" },
  };
  const harness = retainedWorkspaceInstallationHarness({
    activeKind: "package",
    initial,
    newer: () => Promise.resolve({ surface: newer }),
  });

  harness.post();
  const installation = harness.install();
  await harness.activatePackage();
  initial.reject(new Error("stale initial failure"));
  await installation;

  assert.equal(harness.state.package, newer);
  assert.equal(harness.state.workspaceSubjectOpen, false);
  assert.equal(harness.state.packages.length, 1);
  assert.equal(harness.state.packages[0], newer);
  const presentation = harness.context.retainedWorkspacePresentation;
  assert.ok(presentation);
  assert.equal(
    presentation.packages[0]?.detailFailure,
    null,
  );
  assert.equal(harness.state.loading, false);
  assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
  assert.deepEqual(harness.historyWrites(), ["/workspace"]);
  assert.equal(harness.presentationCurrent(), true);
  const rendersBeforeCompletion = harness.renders();
  harness.completePresentation();
  assert.equal(harness.renders(), rendersBeforeCompletion + 1);
  assert.equal(harness.focusSchedules(), 1);
  assert.equal(harness.workspaceFocuses(), 1);
});

test("stale initial Platform detail preserves a newer row failure", async () => {
  const initial = deferred<{ surface: TestPackage }>();
  const harness = retainedWorkspaceInstallationHarness({
    activeKind: "platform",
    initial,
    newer: () => Promise.reject(new Error("newer Platform failure")),
  });

  harness.post();
  const installation = harness.install();
  await harness.activatePlatform();
  initial.resolve({
    surface: {
      id: "stale-platform",
      isRuntimePack: true,
      source: { kind: "platform" },
    },
  });
  await installation;

  assert.equal(harness.state.package, null);
  assert.deepEqual(harness.state.packages, []);
  assert.equal(harness.state.workspaceSubjectOpen, true);
  const presentation = harness.context.retainedWorkspacePresentation;
  assert.ok(presentation);
  assert.equal(
    presentation.platforms[0]?.detailFailure,
    null,
  );
  assert.equal(
    presentation.platforms[1]?.detailFailure,
    "newer Platform failure",
  );
  assert.equal(harness.state.loading, false);
  assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
  assert.deepEqual(harness.historyWrites(), ["/workspace"]);
  assert.ok(harness.renders() >= 2);
});

test("posting captures initial detail authority before rows become interactive", async () => {
  const initial = deferred<{ surface: TestPackage }>();
  const postingRecord = deferred<void>();
  const newer: TestPackage = {
    id: "newer-package",
    isRuntimePack: false,
    source: { kind: "package" },
  };
  const harness = retainedWorkspaceInstallationHarness({
    activeKind: "package",
    initial,
    newer: () => Promise.resolve({ surface: newer }),
  });

  harness.post();
  const installation = (async () => {
    await postingRecord.promise;
    await harness.install();
  })();
  await harness.activatePackage();
  postingRecord.resolve();
  await installation;

  assert.equal(harness.state.package, newer);
  assert.equal(harness.state.workspaceSubjectOpen, false);
  assert.equal(harness.state.packages.length, 1);
  assert.equal(harness.state.packages[0], newer);
  assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
  assert.deepEqual(harness.historyWrites(), ["/workspace"]);
  assert.equal(harness.initialAdmissions(), 0);
});

for (const route of ["home", "credits"] as const) {
  test(`pre-post ${route} remains visible after committed installation`, async () => {
    const initial = deferred<{ surface: TestPackage }>();
    const harness = retainedWorkspaceInstallationHarness({
      activeKind: "package",
      initial,
      newer: () => Promise.reject(new Error("unused")),
    });

    if (route === "home") harness.goHome();
    else harness.openCredits();
    harness.post(false);
    const installation = harness.install();
    await installation;
    harness.completePresentation();

    assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
    assert.ok(harness.context.activeRetainedWorkspacePosting);
    assert.ok(harness.context.installedRetainedLocation);
    assert.equal(harness.state.package, null);
    assert.deepEqual(harness.state.packages, []);
    assert.equal(harness.state.home, true);
    assert.equal(harness.state.credits, route === "credits");
    assert.deepEqual(
      harness.routedLocations(),
      [route === "home" ? "/" : "/credits"],
    );
    assert.deepEqual(harness.historyWrites(), []);
    assert.equal(harness.presentationCurrent(), false);
    assert.equal(harness.initialAdmissions(), 0);
    assert.equal(harness.focusSchedules(), 0);
    assert.equal(harness.workspaceFocuses(), 0);
    assert.equal(harness.renders(), 1);
  });

  test(`delayed installation yields its location to newer ${route}`, async () => {
    const initial = deferred<{ surface: TestPackage }>();
    const postingRecord = deferred<void>();
    const harness = retainedWorkspaceInstallationHarness({
      activeKind: "package",
      initial,
      newer: () => Promise.reject(new Error("unused")),
    });

    harness.post();
    const installation = (async () => {
      await postingRecord.promise;
      await harness.install();
    })();
    if (route === "home") harness.goHome();
    else harness.openCredits();
    postingRecord.resolve();
    await installation;

    assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
    assert.ok(harness.context.installedRetainedLocation);
    assert.equal(harness.state.home, true);
    assert.equal(harness.state.credits, route === "credits");
    assert.deepEqual(
      harness.routedLocations(),
      [route === "home" ? "/" : "/credits"],
    );
    assert.deepEqual(harness.historyWrites(), []);
    assert.equal(harness.presentationCurrent(), false);
    assert.equal(harness.initialAdmissions(), 0);
    const rendersBeforeCompletion = harness.renders();
    harness.completePresentation();
    assert.equal(harness.renders(), rendersBeforeCompletion);
    assert.equal(harness.focusSchedules(), 0);
    assert.equal(harness.workspaceFocuses(), 0);
  });
}
