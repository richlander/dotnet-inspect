import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserPackageSurface,
  BrowserRetainedWorkspacePackageAdmissionResult,
  BrowserRetainedWorkspacePosting,
  BrowserRetainedWorkspacePreparationResult,
  BrowserTypeSurface,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  createWorkspaceFeedActivationCoordinator,
  loadRetainedWorkspaceModels,
  workspaceCredentialPromptHtml,
  type RetainedWorkspaceSurfaceClient,
} from "../src/workspace-feed-activation.ts";
import type {
  RetainedWorkspaceActivationClient,
} from "../src/retained-workspace-activation.ts";

function type(id: string): BrowserTypeSurface {
  return {
    id,
    definitionId: id,
    queryId: id,
    metadataId: id,
    name: id,
    displayName: id,
    namespace: "Example",
    kind: "Class",
    accessibility: "public",
    accessibilityId: "public",
    assembly: "Example.dll",
    assemblyId: "Example",
    assemblyName: "Example",
    members: 0,
    signature: `public class ${id}`,
    api: [],
    platformPack: null,
  };
}

function surface(types: BrowserTypeSurface[]): BrowserPackageSurface {
  return {
    package: "Example.Package",
    version: "1.0.0",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    icon: null,
    defaultAssemblyId: "Example",
    compileLibrary: {
      status: "Selected",
      targetFramework: "net10.0",
      message: null,
    },
    assemblies: [{
      id: "Example",
      name: "Example.dll",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null,
      asset: "ref/net10.0/Example.dll",
      platformPack: null,
      publicTypes: 2,
      publicMembers: 0,
    }],
    types,
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 2,
    }],
    totalMembers: 0,
    documents: [],
    inspectionErrors: [],
    inspectionError: null,
  };
}

function posting(
  retainedDefinitionId = "definition",
  canonicalLocation = "https://example.test/?w=packet",
  publicationOrdinal = 1,
): BrowserRetainedWorkspacePosting {
  return {
    retainedDefinitionId,
    label: "Private Workspace",
    canonicalLocation,
    canonicalPacket: "packet",
    realizationId: "realization",
    publicationOrdinal,
    definition: {
      tabs: [],
      contexts: [],
      registrations: [],
      activeTabId: null,
      selectedContextId: null,
    },
    navigation: {
      operation: "Initialize",
      request: `request-${publicationOrdinal}`,
      snapshot: {
        generation: `generation-${publicationOrdinal}`,
        scope: {
          kind: "Current",
          runtimeFailure: null,
        },
        workspace: {
          id: `workspace-${publicationOrdinal}`,
          kind: "Workspace",
          label: retainedDefinitionId,
          summary: null,
          parent: null,
        },
        activePackage: "subject",
        activeSubject: {
          id: "subject",
          kind: "Package",
          label: "Example.Package",
          summary: null,
          parent: `workspace-${publicationOrdinal}`,
        },
        typeInventoryLibraryContext: null,
        packages: [],
        hierarchy: [],
        libraries: [],
        types: [],
        members: [],
        lenses: [],
        lensOutcome: {
          kind: "Applied",
          basis: "Recommendation",
          subject: {
            id: "subject",
            kind: "Package",
            label: "Example.Package",
            summary: null,
            parent: `workspace-${publicationOrdinal}`,
          },
          effectiveLens: null,
          request: null,
          preferredRole: null,
          policyFailure: null,
          resolution: null,
          suspension: null,
        },
        diagnostics: [],
      },
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
      navigationId: "package-navigation",
      contextIndex: 0,
      consumerPackageSubjectId: "subject",
      summary: {
        selectedCompileFramework: "net10.0",
        libraryCount: 1,
        typeCount: 2,
        memberCount: 0,
        documentCount: 0,
        hasInspectionNotices: false,
      },
    }],
    platforms: [],
    predecessor: null,
    cleanup: null,
  };
}

type WorkspaceTestClient = RetainedWorkspaceActivationClient
  & RetainedWorkspaceSurfaceClient;

interface WorkspaceTestClientOptions {
  readonly noEffectWhenActive?: boolean;
  readonly failSuccessfulCompletion?: boolean;
  readonly admitPackage?:
    WorkspaceTestClient["admitRetainedWorkspacePackage"];
}

function createWorkspaceTestClient(
  events: string[],
  options: WorkspaceTestClientOptions = {},
): WorkspaceTestClient {
  let activePosting: BrowserRetainedWorkspacePosting | null = null;
  let prepared = {
    id: "definition",
    canonicalLocation: "https://example.test/?w=packet",
    publicationOrdinal: 0,
  };

  async function prepareDefinition(
    retainedDefinitionId: string,
    _label: string,
    canonicalLocation: string,
    canonicalPacket: string,
  ): Promise<BrowserRetainedWorkspacePreparationResult> {
    events.push("prepare");
    if (options.noEffectWhenActive
      && activePosting?.retainedDefinitionId === retainedDefinitionId) {
      return {
        status: "noEffect",
        receipt: null,
        preparation: null,
        posting: activePosting,
        failure: null,
      };
    }
    prepared = {
      id: retainedDefinitionId,
      canonicalLocation,
      publicationOrdinal: prepared.publicationOrdinal + 1,
    };
    const candidate = posting(
      retainedDefinitionId,
      canonicalLocation,
      prepared.publicationOrdinal);
    return {
      status: "prepared",
      receipt: `receipt-${prepared.publicationOrdinal}`,
      preparation: {
        retainedDefinitionId,
        label: candidate.label,
        canonicalLocation,
        canonicalPacket,
        definition: candidate.definition,
        navigation: candidate.navigation,
        packages: candidate.packages,
        platforms: candidate.platforms,
      },
      posting: null,
      failure: null,
    };
  }

  return {
    describeWorkspacePackageSources() {
      return {
        succeeded: true,
        sources: [{
          endpoint: "https://packages.example.test/v3/index.json",
          authentication: "Anonymous" as const,
        }],
        failure: null,
      };
    },
    prepareRetainedWorkspaceDefinition: prepareDefinition,
    prepareRetainedWorkspaceDefinitionWithCredentials(
      retainedDefinitionId,
      label,
      canonicalLocation,
      canonicalPacket,
    ) {
      return prepareDefinition(
        retainedDefinitionId,
        label,
        canonicalLocation,
        canonicalPacket);
    },
    async commitRetainedWorkspaceActivation() {
      events.push("commit");
      activePosting = posting(
        prepared.id,
        prepared.canonicalLocation,
        prepared.publicationOrdinal);
      return {
        status: "activated",
        posting: activePosting,
        failure: null,
      };
    },
    async cancelRetainedWorkspaceActivation() {
      events.push("cancel");
      return {
        status: "superseded",
        posting: null,
        failure: null,
      };
    },
    async completeRetainedWorkspaceActivation(
      _receipt,
      succeeded,
    ) {
      events.push(`complete:${succeeded}`);
      if (succeeded && options.failSuccessfulCompletion) {
        throw new Error("Consumer completion could not be delivered.");
      }
      return {
        status: "completed",
        succeeded,
        failure: null,
        message: null,
      };
    },
    async deactivateRetainedWorkspaceDefinition() {
      throw new Error("Unexpected retained Workspace deactivation.");
    },
    async completeRetainedWorkspaceDeactivation() {
      throw new Error("Unexpected retained Workspace deactivation completion.");
    },
    async observeRetainedWorkspaceSettlement() {
      throw new Error("Unexpected predecessor settlement.");
    },
    validateRetainedWorkspaceNavigationAuthority() {
      events.push("validate");
      return true;
    },
    recordRetainedWorkspaceNavigationPosting() {
      events.push("record");
      return "accepted";
    },
    acknowledgeRetainedWorkspaceNavigation() {
      events.push("acknowledge");
      return "accepted";
    },
    abandonRetainedWorkspaceNavigation() {
      events.push("abandon");
      return "accepted";
    },
    async admitRetainedWorkspacePackage(
      retainedDefinitionId,
      realizationId,
      navigationId,
      typeOffset,
    ): Promise<BrowserRetainedWorkspacePackageAdmissionResult> {
      events.push("admit");
      if (options.admitPackage) {
        return options.admitPackage(
          retainedDefinitionId,
          realizationId,
          navigationId,
          typeOffset);
      }
      return {
        status: "admitted",
        package: {
          navigationId: "package-navigation",
          contextIndex: 0,
          consumerPackageSubjectId: "subject",
          surface: surface([type("One")]),
          typePage: {
            offset: 0,
            totalTypes: 1,
            nextOffset: null,
          },
        },
        message: null,
      };
    },
    async admitRetainedWorkspacePlatform() {
      throw new Error("Unexpected Platform admission.");
    },
  };
}

function deferred<T>() {
  let resolve: (value: T | PromiseLike<T>) => void = () => {};
  const promise = new Promise<T>(resolver => {
    resolve = resolver;
  });
  return { promise, resolve };
}

function createCoordinatorHarness(
  client: WorkspaceTestClient,
  events: string[],
  initialVisible = "incumbent",
) {
  let navigationSequence = 1;
  let visible = initialVisible;
  let failure: string | null = null;
  const coordinator = createWorkspaceFeedActivationCoordinator({
    client,
    document: {
      querySelector() {
        return null;
      },
      createElement(): never {
        throw new Error("Anonymous activation must not render a prompt.");
      },
      body: { append() {} },
    },
    applicationRoot: { inert: false },
    maxVisibleModels: 8,
    isCurrent: sequence => sequence === navigationSequence,
    beginNavigation: () => ++navigationSequence,
    hasVisibleWorkspace: () => visible.length > 0,
    captureRollback: () => visible,
    restoreRollback(restored) {
      events.push("restore");
      visible = restored;
    },
    releaseRollback() {
      events.push("release");
    },
    publish(posted) {
      events.push("publish");
      visible = posted.canonicalLocation;
    },
    setLoading() {},
    pushLocation() {
      events.push("push");
    },
    reportFailure(message) {
      failure = message;
    },
    reportPredecessorFailure(error) {
      assert.fail(String(error));
    },
    observe() {},
    errorMessage: String,
    escapeHtml: String,
    trapModalTab() {},
  });
  return {
    coordinator,
    get sequence() {
      return navigationSequence;
    },
    advance(visibleWorkspace: string) {
      navigationSequence++;
      visible = visibleWorkspace;
    },
    get visible() {
      return visible;
    },
    get failure() {
      return failure;
    },
  };
}

test("credential prompt names endpoints without retaining credential fields", () => {
  const html = workspaceCredentialPromptHtml({
    requirements: [{
      endpoint: "https://nuget.pkg.github.com/example/index.json",
      authentication: "AuthenticationRequired",
    }],
    submitting: false,
    error: "",
  }, String);

  assert.match(html, /nuget\.pkg\.github\.com\/example\/index\.json/);
  assert.match(html, /type="password"/);
  assert.match(
    html,
    /not\s+added to the Workspace link or browser storage/);
  assert.equal(html.match(/autocomplete="off"/g)?.length, 3);
  assert.doesNotMatch(html, /value="[^"]+"[^>]*name="pat-/);
});

test("retained package admission joins all Type pages", async () => {
  const offsets: number[] = [];
  const client: RetainedWorkspaceSurfaceClient = {
    async admitRetainedWorkspacePackage(
      _definition,
      _realization,
      _navigation,
      offset,
    ) {
      offsets.push(offset);
      const first = offset === 0;
      return {
        status: "admitted",
        package: {
          navigationId: "package-navigation",
          contextIndex: 0,
          consumerPackageSubjectId: "subject",
          surface: surface([type(first ? "One" : "Two")]),
          typePage: {
            offset,
            totalTypes: 2,
            nextOffset: first ? 1 : null,
          },
        },
        message: null,
      };
    },
    async admitRetainedWorkspacePlatform() {
      throw new Error("Unexpected Platform admission.");
    },
  };

  const models = await loadRetainedWorkspaceModels(client, posting());

  assert.deepEqual(offsets, [0, 1]);
  assert.deepEqual(
    models.packages[0]?.packageModel.types.map(candidate => candidate.id),
    ["One", "Two"]);
  assert.deepEqual(models.packages[0]?.packageModel.source, { kind: "unknown" });
  assert.equal(models.packages[0]?.packageModel.producerLabel, "NuGet");
});

test("retained admission rejects non-progressing Type pages", async () => {
  const client: RetainedWorkspaceSurfaceClient = {
    async admitRetainedWorkspacePackage() {
      return {
        status: "admitted",
        package: {
          navigationId: "package-navigation",
          contextIndex: 0,
          consumerPackageSubjectId: "subject",
          surface: surface([type("One")]),
          typePage: {
            offset: 0,
            totalTypes: 2,
            nextOffset: 0,
          },
        },
        message: null,
      };
    },
    async admitRetainedWorkspacePlatform() {
      throw new Error("Unexpected Platform admission.");
    },
  };

  await assert.rejects(
    loadRetainedWorkspaceModels(client, posting()),
    /invalid next Type offset 0/);
});

test("anonymous source activation publishes before committing browser history", async () => {
  const events: string[] = [];
  let preparedDefinition = {
    id: "definition",
    canonicalLocation: "https://example.test/?w=packet",
    publicationOrdinal: 0,
  };
  const client = {
    describeWorkspacePackageSources() {
      return {
        succeeded: true,
        sources: [{
          endpoint: "https://packages.example.test/v3/index.json",
          authentication: "Anonymous" as const,
        }],
        failure: null,
      };
    },
    async prepareRetainedWorkspaceDefinition(
      retainedDefinitionId: string,
      _label: string,
      canonicalLocation: string,
    ) {
      events.push("prepare");
      preparedDefinition = {
        id: retainedDefinitionId,
        canonicalLocation,
        publicationOrdinal: preparedDefinition.publicationOrdinal + 1,
      };
      const activationPosting = posting(
        retainedDefinitionId,
        canonicalLocation,
        preparedDefinition.publicationOrdinal);
      return {
        status: "prepared",
        receipt: "receipt",
        preparation: {
          retainedDefinitionId: activationPosting.retainedDefinitionId,
          label: activationPosting.label,
          canonicalLocation: activationPosting.canonicalLocation,
          canonicalPacket: activationPosting.canonicalPacket,
          definition: activationPosting.definition,
          navigation: activationPosting.navigation,
          packages: activationPosting.packages,
          platforms: activationPosting.platforms,
        },
        posting: null,
        failure: null,
      };
    },
    async prepareRetainedWorkspaceDefinitionWithCredentials() {
      throw new Error("Anonymous activation must not send credentials.");
    },
    async commitRetainedWorkspaceActivation() {
      events.push("commit");
      return {
        status: "activated",
        posting: posting(
          preparedDefinition.id,
          preparedDefinition.canonicalLocation,
          preparedDefinition.publicationOrdinal),
        failure: null,
      };
    },
    async cancelRetainedWorkspaceActivation() {
      throw new Error("Anonymous activation must not cancel.");
    },
    async completeRetainedWorkspaceActivation() {
      events.push("complete");
      return {
        status: "completed",
        succeeded: true,
        failure: null,
        message: null,
      };
    },
    async deactivateRetainedWorkspaceDefinition() {
      throw new Error("Anonymous activation must not deactivate.");
    },
    async completeRetainedWorkspaceDeactivation() {
      throw new Error("Anonymous activation must not complete deactivation.");
    },
    async observeRetainedWorkspaceSettlement() {
      throw new Error("Anonymous activation has no predecessor.");
    },
    validateRetainedWorkspaceNavigationAuthority() {
      events.push("validate");
      return true;
    },
    recordRetainedWorkspaceNavigationPosting() {
      events.push("record");
      return "accepted";
    },
    acknowledgeRetainedWorkspaceNavigation() {
      events.push("acknowledge");
      return "accepted";
    },
    abandonRetainedWorkspaceNavigation() {
      throw new Error("Anonymous activation must not abandon.");
    },
    async admitRetainedWorkspacePackage() {
      events.push("admit");
      return {
        status: "admitted",
        package: {
          navigationId: "package-navigation",
          contextIndex: 0,
          consumerPackageSubjectId: "subject",
          surface: surface([type("One")]),
          typePage: {
            offset: 0,
            totalTypes: 1,
            nextOffset: null,
          },
        },
        message: null,
      };
    },
    async admitRetainedWorkspacePlatform() {
      throw new Error("Unexpected Platform admission.");
    },
  } satisfies RetainedWorkspaceActivationClient
    & RetainedWorkspaceSurfaceClient;
  const applicationRoot = { inert: false };
  let released = false;
  const coordinator = createWorkspaceFeedActivationCoordinator({
    client,
    document: {
      querySelector() {
        return null;
      },
      createElement(): never {
        throw new Error("Anonymous activation must not render a prompt.");
      },
      body: { append() {} },
    },
    applicationRoot,
    maxVisibleModels: 8,
    isCurrent: sequence => sequence === 7,
    beginNavigation: () => 8,
    hasVisibleWorkspace: () => false,
    captureRollback: () => ({ id: "rollback" }),
    restoreRollback() {
      throw new Error("Successful activation must not roll back.");
    },
    releaseRollback() {
      released = true;
    },
    publish(_posting, models) {
      events.push("publish");
      assert.equal(models.packages.length, 1);
    },
    setLoading() {
      events.push("loading");
    },
    pushLocation() {
      events.push("push");
    },
    reportFailure(message) {
      assert.fail(message);
    },
    reportPredecessorFailure(error) {
      assert.fail(String(error));
    },
    observe() {},
    errorMessage: String,
    escapeHtml: String,
    trapModalTab() {},
  });

  for (let index = 0; index < 6; index++) {
    const opened = await coordinator.tryOpen(
      new URL(`https://example.test/?w=packet-${index}`),
      7,
      true);
    assert.equal(opened, true);
  }

  assert.equal(coordinator.activeUrl, "https://example.test/?w=packet-5");
  assert.equal(released, true);
  assert.ok(events.indexOf("publish") < events.indexOf("acknowledge"));
  assert.ok(events.indexOf("acknowledge") < events.indexOf("complete"));
  assert.ok(events.indexOf("complete") < events.indexOf("push"));
  assert.equal(applicationRoot.inert, false);
});

test("an identical active definition republishes its retained posting", async () => {
  const events: string[] = [];
  const harness = createCoordinatorHarness(
    createWorkspaceTestClient(events, { noEffectWhenActive: true }),
    events);
  const location = "https://example.test/?w=packet";

  assert.equal(
    await harness.coordinator.tryOpen(
      new URL(location),
      harness.sequence,
      true),
    true);
  assert.equal(harness.visible, location);

  harness.coordinator.clearActiveUrl();
  harness.advance("ordinary-workspace");
  assert.equal(
    await harness.coordinator.tryOpen(
      new URL(location),
      harness.sequence,
      true),
    true);

  assert.equal(harness.visible, location);
  assert.equal(events.filter(event => event === "publish").length, 2);
  assert.equal(events.filter(event => event === "commit").length, 1);
  assert.equal(harness.failure, null);
});

test("superseded delayed admission releases rather than restores rollback", async () => {
  const events: string[] = [];
  const admission = deferred<BrowserRetainedWorkspacePackageAdmissionResult>();
  const admissionStarted = deferred<void>();
  const client = createWorkspaceTestClient(events, {
    admitPackage(..._arguments) {
      admissionStarted.resolve();
      return admission.promise;
    },
  });
  const harness = createCoordinatorHarness(client, events);

  const opening = harness.coordinator.tryOpen(
    new URL("https://example.test/?w=packet"),
    harness.sequence,
    true);
  await admissionStarted.promise;
  harness.advance("successor-workspace");
  admission.resolve({
    status: "admitted",
    package: {
      navigationId: "package-navigation",
      contextIndex: 0,
      consumerPackageSubjectId: "subject",
      surface: surface([type("One")]),
      typePage: {
        offset: 0,
        totalTypes: 1,
        nextOffset: null,
      },
    },
    message: null,
  });

  assert.equal(await opening, true);
  assert.equal(harness.visible, "successor-workspace");
  assert.equal(events.includes("restore"), false);
  assert.equal(events.includes("release"), true);
  assert.equal(harness.failure, null);
});

test("consumer-completion failure restores the incumbent workspace", async () => {
  const events: string[] = [];
  const harness = createCoordinatorHarness(
    createWorkspaceTestClient(events, { failSuccessfulCompletion: true }),
    events);

  assert.equal(
    await harness.coordinator.tryOpen(
      new URL("https://example.test/?w=packet"),
      harness.sequence,
      true),
    true);

  assert.equal(harness.visible, "incumbent");
  assert.equal(events.includes("publish"), true);
  assert.equal(events.includes("restore"), true);
  assert.equal(events.includes("release"), false);
  assert.match(harness.failure ?? "", /Consumer completion could not be delivered/);
  assert.equal(harness.coordinator.activeUrl, null);
});
