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
  RetainedWorkspaceActivationController,
} from "../src/retained-workspace-activation.ts";
import {
  createRetainedWorkspaceActivationController,
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
  readonly deactivate?:
    WorkspaceTestClient["deactivateRetainedWorkspaceDefinition"];
}

function createWorkspaceTestClient(
  events: string[],
  options: WorkspaceTestClientOptions = {},
): WorkspaceTestClient {
  let activePosting: BrowserRetainedWorkspacePosting | null = null;
  let successfulCompletionFailures = 0;
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
      if (succeeded
        && options.failSuccessfulCompletion
        && successfulCompletionFailures++ === 0) {
        throw new Error("Consumer completion could not be delivered.");
      }
      return {
        status: "completed",
        succeeded,
        failure: null,
        message: null,
      };
    },
    async deactivateRetainedWorkspaceDefinition(retainedDefinitionId) {
      events.push("deactivate");
      if (options.deactivate) {
        const result = await options.deactivate(retainedDefinitionId);
        if (result.status === "deactivated") activePosting = null;
        return result;
      }
      activePosting = null;
      return {
        status: "deactivated",
        completionReceipt: "deactivation-receipt",
        settlement: null,
        message: null,
      };
    },
    async completeRetainedWorkspaceDeactivation(
      _receipt,
      succeeded,
    ) {
      events.push(`deactivation-complete:${succeeded}`);
      return {
        status: "completed",
        succeeded,
        failure: null,
        message: null,
      };
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

interface PromptTestElement {
  readonly listeners: Map<string, EventListenerOrEventListenerObject>;
  readonly testChildren: Map<string, PromptTestElement>;
  value: string;
  innerHTML?: string;
  content?: { firstElementChild: PromptTestElement };
  addEventListener(
    type: string,
    listener: EventListenerOrEventListenerObject | null,
  ): void;
  querySelector(selectors: string): PromptTestElement | null;
  focus(): void;
  remove(): void;
}

function promptTestElement(): PromptTestElement {
  const listeners =
    new Map<string, EventListenerOrEventListenerObject>();
  const testChildren = new Map<string, PromptTestElement>();
  return {
    listeners,
    testChildren,
    value: "",
    addEventListener(eventType, listener) {
      if (listener) listeners.set(eventType, listener);
    },
    querySelector(selectors) {
      return testChildren.get(selectors) ?? null;
    },
    focus() {},
    remove() {},
  };
}

function createPromptTestDocument() {
  let backdrop: PromptTestElement | null = null;
  return {
    document: {
      querySelector(): PromptTestElement | null {
        return backdrop;
      },
      createElement(): PromptTestElement {
        backdrop = promptTestElement();
        const form = promptTestElement();
        form.testChildren.set(
          "#workspace-source-username-0",
          promptTestElement());
        form.testChildren.set(
          "#workspace-source-pat-0",
          promptTestElement());
        backdrop.testChildren.set(
          "#workspace-credential-form",
          form);
        backdrop.testChildren.set(
          "#workspace-credential-dialog",
          promptTestElement());
        backdrop.testChildren.set(
          "#workspace-credential-cancel",
          promptTestElement());
        return {
          ...promptTestElement(),
          content: { firstElementChild: backdrop },
        };
      },
      body: { append() {} },
    },
    form(): PromptTestElement {
      const form = backdrop?.testChildren.get(
        "#workspace-credential-form");
      assert.ok(form);
      return form;
    },
  };
}

function createCoordinatorHarness(
  client: WorkspaceTestClient,
  events: string[],
  initialVisible = "incumbent",
  activationController?: RetainedWorkspaceActivationController,
  restoreRollback?: (rollback: string) => void | Promise<void>,
) {
  let navigationSequence = 1;
  let visible = initialVisible;
  let failure: string | null = null;
  const coordinator = createWorkspaceFeedActivationCoordinator({
    client,
    ...(activationController ? { activationController } : {}),
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
    cloneRollback: rollback => rollback,
    restoreRollback: restoreRollback ?? (restored => {
      events.push("restore");
      visible = restored;
    }),
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

test("source and Saved Workspaces share one retained activation authority", async () => {
  const hooks = {
    post() {},
    clear() {},
    predecessorSettled() {},
    predecessorObservationFailed() {},
  };

  {
    const events: string[] = [];
    const client = createWorkspaceTestClient(events, {
      noEffectWhenActive: true,
    });

    const controller =
      createRetainedWorkspaceActivationController(client, hooks);
    const saved = controller.retain({
      label: "Saved",
      canonicalLocation: "https://example.test/?w=saved",
      canonicalPacket: "saved",
    });
    await controller.activate(saved.id);
    const source = createCoordinatorHarness(
      client,
      events,
      "Saved",
      controller);

    assert.equal(
      await source.coordinator.tryOpen(
        new URL("https://example.test/?w=source"),
        source.sequence,
        true),
      true);
    assert.equal(source.visible, "https://example.test/?w=source");
    assert.equal(source.failure, null);
    assert.notEqual(controller.state.activeDefinitionId, saved.id);
    assert.equal(
      controller.state.definitions.some(definition =>
        definition.id === saved.id),
      true);
    assert.equal(
      source.coordinator.ownsRetainedDefinition(
        controller.state.activeDefinitionId!),
      true);
  }

  {
    const events: string[] = [];
    const client = createWorkspaceTestClient(events, {
      noEffectWhenActive: true,
    });
    const savedPosting: {
      value: BrowserRetainedWorkspacePosting | null;
    } = { value: null };
    const controller = createRetainedWorkspaceActivationController(client, {
      ...hooks,
      post(posted) {
        savedPosting.value = posted;
      },
    });
    const source = createCoordinatorHarness(
      client,
      events,
      "incumbent",
      controller);
    await source.coordinator.tryOpen(
      new URL("https://example.test/?w=source"),
      source.sequence,
      true);
    const saved = controller.retain({
      label: "Saved",
      canonicalLocation: "https://example.test/?w=saved",
      canonicalPacket: "saved",
    });

    const result = await controller.activate(saved.id);

    assert.equal(result.status, "activated");
    assert.equal(savedPosting.value?.canonicalLocation,
      "https://example.test/?w=saved");
    assert.equal(controller.state.activeDefinitionId, saved.id);
    assert.equal(source.coordinator.ownsRetainedDefinition(saved.id), false);
  }
});

test("post-publication rollback reactivates incumbent authority", async () => {
  const events: string[] = [];
  const client = createWorkspaceTestClient(events);
  const controller = createRetainedWorkspaceActivationController(client, {
    post() {},
    clear() {},
    predecessorSettled() {},
    predecessorObservationFailed() {},
  });
  const saved = controller.retain({
    label: "Saved",
    canonicalLocation: "https://example.test/?w=saved",
    canonicalPacket: "saved",
  });
  await controller.activate(saved.id);
  const completeActivation =
    client.completeRetainedWorkspaceActivation.bind(client);
  let failNextSuccessfulCompletion = true;
  client.completeRetainedWorkspaceActivation = async (
    receipt,
    succeeded,
    failure,
  ) => {
    if (succeeded && failNextSuccessfulCompletion) {
      failNextSuccessfulCompletion = false;
      throw new Error("Consumer completion delivery failed.");
    }
    return completeActivation(receipt, succeeded, failure);
  };
  let restoredVisible = "";
  let harness: ReturnType<typeof createCoordinatorHarness>;
  harness = createCoordinatorHarness(
    client,
    events,
    "https://example.test/?w=saved",
    controller,
    async restored => {
      const reactivated = await harness.coordinator
        .reactivateRetainedDefinition(
          saved.id,
          harness.sequence,
          restoredPosting => {
            restoredVisible = restoredPosting.canonicalLocation;
          });
      assert.equal(reactivated, true);
      assert.equal(restored, "https://example.test/?w=saved");
    });

  await harness.coordinator.tryOpen(
    new URL("https://example.test/?w=source"),
    harness.sequence,
    true);

  assert.equal(controller.state.activeDefinitionId, saved.id);
  assert.equal(restoredVisible, "https://example.test/?w=saved");
  assert.match(harness.failure ?? "", /Consumer completion/);
});

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
    cloneRollback: structuredClone,
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
  assert.equal(harness.coordinator.blocksUrlSynchronization, true);
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
  assert.equal(harness.coordinator.blocksUrlSynchronization, false);

  const reopened = await harness.coordinator.tryOpen(
    new URL("https://example.test/?w=packet"),
    harness.sequence,
    true);
  assert.equal(reopened, true);
  assert.equal(harness.visible, "https://example.test/?w=packet");
  assert.equal(events.filter(event => event === "commit").length, 2);
  assert.equal(events.filter(event => event === "publish").length, 1);
  assert.equal(events.filter(event => event === "acknowledge").length, 1);
  assert.equal(events.filter(event => event === "complete:true").length, 1);
  assert.equal(events.includes("deactivate"), true);
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

  assert.equal(
    await harness.coordinator.tryOpen(
      new URL("https://example.test/?w=packet"),
      harness.sequence,
      true),
    true);
  assert.equal(harness.visible, "https://example.test/?w=packet");
  assert.equal(events.includes("deactivate"), true);
  assert.equal(events.filter(event => event === "complete:true").length, 2);
  assert.equal(events.filter(event => event === "publish").length, 2);
});

test("superseding a published activation preserves its committed incumbent", async () => {
  const events: string[] = [];
  const completion = deferred<boolean>();
  const completionStarted = deferred<boolean>();
  const client = createWorkspaceTestClient(events);
  const complete = client.completeRetainedWorkspaceActivation.bind(client);
  client.completeRetainedWorkspaceActivation =
    async (receipt, succeeded, failure) => {
      if (succeeded) {
        completionStarted.resolve(true);
        await completion.promise;
        throw new Error("Consumer completion could not be delivered.");
      }
      return complete(receipt, succeeded, failure);
    };
  const harness = createCoordinatorHarness(client, events);

  const opening = harness.coordinator.tryOpen(
    new URL("https://example.test/?w=first"),
    harness.sequence,
    true);
  await completionStarted.promise;
  assert.equal(harness.visible, "https://example.test/?w=first");

  harness.advance(harness.visible);
  assert.equal(
    await harness.coordinator.tryOpen(
      new URL("https://example.test/?w=second"),
      harness.sequence,
      true),
    true);
  assert.equal(harness.coordinator.blocksUrlSynchronization, true);
  assert.equal(
    harness.coordinator.captureCommittedRollback(),
    "incumbent");
  completion.resolve(true);
  assert.equal(await opening, true);

  assert.equal(harness.visible, "incumbent");
  assert.equal(events.includes("restore"), true);
  assert.equal(events.includes("push"), false);
  assert.equal(events.includes("complete:false"), true);
  assert.match(
    harness.failure ?? "",
    /awaiting consumer completion/);
  assert.equal(harness.coordinator.activeUrl, null);
  assert.equal(harness.coordinator.blocksUrlSynchronization, false);
  assert.equal(harness.coordinator.captureCommittedRollback(), null);
});

test("a committed successor retires a published activation rollback", async () => {
  const events: string[] = [];
  const completion = deferred<boolean>();
  const completionStarted = deferred<boolean>();
  const client = createWorkspaceTestClient(events);
  const complete = client.completeRetainedWorkspaceActivation.bind(client);
  client.completeRetainedWorkspaceActivation =
    async (receipt, succeeded, failure) => {
      if (succeeded) {
        completionStarted.resolve(true);
        await completion.promise;
        throw new Error("Consumer completion could not be delivered.");
      }
      return complete(receipt, succeeded, failure);
    };
  const harness = createCoordinatorHarness(client, events);

  const opening = harness.coordinator.tryOpen(
    new URL("https://example.test/?w=first"),
    harness.sequence,
    true);
  await completionStarted.promise;
  harness.advance("committed-successor");
  harness.coordinator.clearActiveUrl();
  assert.equal(harness.coordinator.blocksUrlSynchronization, false);
  assert.equal(harness.coordinator.captureCommittedRollback(), null);
  completion.resolve(true);
  assert.equal(await opening, true);

  assert.equal(harness.visible, "committed-successor");
  assert.equal(events.includes("restore"), false);
  assert.equal(events.includes("release"), true);
  assert.equal(events.includes("push"), false);
  assert.equal(events.includes("complete:false"), true);
  assert.equal(harness.coordinator.activeUrl, null);
});

test("a successor can transfer rollback ownership before it commits", async () => {
  const events: string[] = [];
  const completion = deferred<boolean>();
  const completionStarted = deferred<boolean>();
  const client = createWorkspaceTestClient(events);
  const complete = client.completeRetainedWorkspaceActivation.bind(client);
  client.completeRetainedWorkspaceActivation =
    async (receipt, succeeded, failure) => {
      if (succeeded) {
        completionStarted.resolve(true);
        await completion.promise;
        throw new Error("Consumer completion could not be delivered.");
      }
      return complete(receipt, succeeded, failure);
    };
  const harness = createCoordinatorHarness(client, events);

  const opening = harness.coordinator.tryOpen(
    new URL("https://example.test/?w=first"),
    harness.sequence,
    true);
  await completionStarted.promise;
  harness.advance("successor-under-construction");

  assert.equal(
    harness.coordinator.transferCommittedRollback(),
    "incumbent");
  assert.equal(harness.coordinator.captureCommittedRollback(), null);
  assert.equal(harness.coordinator.blocksUrlSynchronization, false);

  completion.resolve(true);
  assert.equal(await opening, true);
  assert.equal(harness.visible, "successor-under-construction");
  assert.equal(events.includes("restore"), false);
  assert.equal(events.includes("release"), true);
  assert.equal(events.includes("complete:false"), true);
});

test("dismissing a submitted prompt preserves its committed rollback", async () => {
  const htmlElement = Object.getOwnPropertyDescriptor(
    globalThis,
    "HTMLElement");
  Object.defineProperty(globalThis, "HTMLElement", {
    configurable: true,
    value: Object,
  });
  try {
    const events: string[] = [];
    let navigationSequence = 1;
    let visible = "committed-incumbent";
    const completion = deferred<boolean>();
    const completionStarted = deferred<boolean>();
    const completionSettled = deferred<boolean>();
    const client = createWorkspaceTestClient(events);
    client.describeWorkspacePackageSources = () => ({
      succeeded: true,
      sources: [{
        endpoint: "https://packages.example.test/v3/index.json",
        authentication: "AuthenticationRequired",
      }],
      failure: null,
    });
    const complete =
      client.completeRetainedWorkspaceActivation.bind(client);
    client.completeRetainedWorkspaceActivation =
      async (receipt, succeeded, failure) => {
        if (succeeded) {
          completionStarted.resolve(true);
          await completion.promise;
          throw new Error("Consumer completion could not be delivered.");
        }
        const result = await complete(receipt, succeeded, failure);
        completionSettled.resolve(true);
        return result;
      };
    const promptDocument = createPromptTestDocument();
    const coordinator = createWorkspaceFeedActivationCoordinator({
      client,
      // @ts-expect-error This fixture supplies only the DOM members the prompt uses.
      document: promptDocument.document,
      applicationRoot: { inert: false },
      maxVisibleModels: 8,
      isCurrent: sequence => sequence === navigationSequence,
      beginNavigation: () => ++navigationSequence,
      hasVisibleWorkspace: () => true,
      captureRollback: () => visible,
      cloneRollback: rollback => rollback,
      restoreRollback(restored) {
        events.push(`restore:${restored}`);
        visible = restored;
      },
      releaseRollback(released) {
        events.push(`release:${released}`);
      },
      publish(activationPosting) {
        visible = activationPosting.canonicalLocation;
        events.push(`publish:${visible}`);
      },
      setLoading() {},
      pushLocation() {},
      reportFailure(message) {
        events.push(`failure:${message}`);
      },
      reportPredecessorFailure(error) {
        assert.fail(String(error));
      },
      observe() {},
      errorMessage: String,
      escapeHtml: String,
      trapModalTab() {},
    });

    await coordinator.tryOpen(
      new URL("https://example.test/?w=private-A"),
      navigationSequence,
      true);
    const form = promptDocument.form();
    const username = form.querySelector(
      "#workspace-source-username-0");
    const pat = form.querySelector(
      "#workspace-source-pat-0");
    assert.ok(username);
    assert.ok(pat);
    username.value = "example";
    pat.value = "page-session-pat";
    const submit = form.listeners.get("submit");
    if (typeof submit !== "function") {
      assert.fail("The credential form did not bind submission.");
    }
    submit(new Event("submit"));
    await completionStarted.promise;

    navigationSequence++;
    coordinator.cancelPrompt(false);
    await coordinator.tryOpen(
      new URL("https://example.test/?w=private-B"),
      navigationSequence,
      false);
    assert.equal(
      coordinator.captureCommittedRollback(),
      "committed-incumbent");
    assert.equal(coordinator.blocksUrlSynchronization, true);
    completion.resolve(true);
    await completionSettled.promise;
    await new Promise<void>(resolve => setImmediate(resolve));

    assert.equal(visible, "committed-incumbent");
    assert.equal(
      events.includes("release:committed-incumbent"),
      false);
    assert.equal(
      events.includes("restore:committed-incumbent"),
      true);
    coordinator.cancelPrompt();
  } finally {
    if (htmlElement) {
      Object.defineProperty(globalThis, "HTMLElement", htmlElement);
    } else {
      Reflect.deleteProperty(globalThis, "HTMLElement");
    }
  }
});

test("superseded abandoned-definition recovery cannot restart activation", async () => {
  const events: string[] = [];
  const deactivation = deferred<
    Awaited<ReturnType<
      WorkspaceTestClient["deactivateRetainedWorkspaceDefinition"]>>>();
  const deactivationStarted = deferred<void>();
  const client = createWorkspaceTestClient(events, {
    failSuccessfulCompletion: true,
    deactivate() {
      deactivationStarted.resolve();
      return deactivation.promise;
    },
  });
  const harness = createCoordinatorHarness(client, events);
  const location = new URL("https://example.test/?w=packet");

  assert.equal(
    await harness.coordinator.tryOpen(location, harness.sequence, true),
    true);
  const reopening =
    harness.coordinator.tryOpen(location, harness.sequence, true);
  await deactivationStarted.promise;
  harness.advance("successor-workspace");
  deactivation.resolve({
    status: "deactivated",
    completionReceipt: "deactivation-receipt",
    settlement: null,
    message: null,
  });

  assert.equal(await reopening, false);
  assert.equal(harness.visible, "successor-workspace");
  assert.equal(events.filter(event => event === "commit").length, 1);
  assert.equal(events.filter(event => event === "publish").length, 1);
});
