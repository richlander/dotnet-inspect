import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import {
  appSource,
  functionDeclaration,
} from "./composition-root-test-fixture.ts";

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
    error: "",
    errorTitle: "",
    errorDetail: "",
    retryAction: null,
  };
  let sequence = 1;
  let published = 0;
  let renders = 0;
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
    activeRetainedWorkspacePosting: posting,
    activeWorkspaceUrl: null as string | null,
    installedRetainedLocation: null as unknown,
    retainedWorkspacePresentation: {
      packages: options.activeKind === "package" ? inventories : [],
      platforms: options.activeKind === "platform" ? inventories : [],
    },
    state,
    navigationSequence,
    admitRetainedPackage: (
      _posting: unknown,
      inventory: TestPresentationItem,
    ) => inventory.navigationId === activeNavigationId
      ? options.initial.promise
      : options.newer(),
    admitRetainedPlatform: (
      _posting: unknown,
      inventory: TestPresentationItem,
    ) => inventory.navigationId === activeNavigationId
      ? options.initial.promise
      : options.newer(),
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
    retainedLocationIntents: {
      classify: (_intent: unknown, outcome: unknown) => outcome,
      publish: () => {
        published += 1;
        return true;
      },
    },
    history: {},
    render: () => {
      renders += 1;
    },
    posting,
    locationIntent: {},
    newerNavigationId,
  };
  const declarations = [
    "installRetainedWorkspacePosting",
    "activateRetainedPackageAction",
    "activateRetainedPlatformAction",
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
    published: () => published,
    renders: () => renders,
    install: () => Promise.resolve(runInNewContext(
      "installRetainedWorkspacePosting(posting, locationIntent)",
      context,
    )),
    activatePackage: () => Promise.resolve(runInNewContext(
      "activateRetainedPackageAction(newerNavigationId)",
      context,
    )),
    activatePlatform: () => Promise.resolve(runInNewContext(
      "activateRetainedPlatformAction(newerNavigationId)",
      context,
    )),
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

  const installation = harness.install();
  await harness.activatePackage();
  initial.reject(new Error("stale initial failure"));
  await installation;

  assert.equal(harness.state.package, newer);
  assert.equal(harness.state.workspaceSubjectOpen, false);
  assert.equal(harness.state.packages.length, 1);
  assert.equal(harness.state.packages[0], newer);
  assert.equal(
    harness.context.retainedWorkspacePresentation.packages[0]?.detailFailure,
    null,
  );
  assert.equal(harness.state.loading, false);
  assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
  assert.equal(harness.published(), 1);
});

test("stale initial Platform detail preserves a newer row failure", async () => {
  const initial = deferred<{ surface: TestPackage }>();
  const harness = retainedWorkspaceInstallationHarness({
    activeKind: "platform",
    initial,
    newer: () => Promise.reject(new Error("newer Platform failure")),
  });

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
  assert.equal(
    harness.context.retainedWorkspacePresentation.platforms[0]?.detailFailure,
    null,
  );
  assert.equal(
    harness.context.retainedWorkspacePresentation.platforms[1]?.detailFailure,
    "newer Platform failure",
  );
  assert.equal(harness.state.loading, false);
  assert.equal(harness.context.activeWorkspaceUrl, "/workspace");
  assert.equal(harness.published(), 1);
  assert.ok(harness.renders() >= 2);
});
