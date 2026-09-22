import type {
  BrowserPackageSurface,
  BrowserRetainedWorkspacePackageAdmissionResult,
  BrowserRetainedWorkspacePackageInventory,
  BrowserRetainedWorkspacePlatformAdmissionResult,
  BrowserRetainedWorkspacePlatformInventory,
  BrowserRetainedWorkspacePosting,
  BrowserWorkspacePackageSourceRequirement,
} from "./facades/inspect-web-catalog.d.ts";
import {
  createNuGetPackageModel,
  createRuntimePackageModel,
  type AppPackage,
} from "./package-acquisition.ts";
import {
  createRetainedWorkspaceActivationController,
  type RetainedWorkspaceActivationClient,
  type RetainedWorkspaceActivationController,
} from "./retained-workspace-activation.ts";

export interface RetainedWorkspaceSurfaceClient {
  admitRetainedWorkspacePackage(
    retainedDefinitionId: string,
    realizationId: string,
    navigationId: string,
    typeOffset: number,
  ): Promise<BrowserRetainedWorkspacePackageAdmissionResult>;
  admitRetainedWorkspacePlatform(
    retainedDefinitionId: string,
    realizationId: string,
    navigationId: string,
    typeOffset: number,
  ): Promise<BrowserRetainedWorkspacePlatformAdmissionResult>;
}

export interface RetainedWorkspacePackageModel {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly consumerPackageSubjectId: string;
  readonly packageModel: AppPackage;
}

export interface RetainedWorkspacePlatformModel {
  readonly navigationId: string;
  readonly contextIndex: number;
  readonly family: string;
  readonly runtimeIdentifier: string | null;
  readonly packageModel: AppPackage;
}

export interface RetainedWorkspaceModels {
  readonly packages: readonly RetainedWorkspacePackageModel[];
  readonly platforms: readonly RetainedWorkspacePlatformModel[];
}

export interface WorkspaceCredentialPromptModel {
  readonly requirements: readonly BrowserWorkspacePackageSourceRequirement[];
  readonly submitting: boolean;
  readonly error: string;
}

export interface WorkspaceFeedActivationDependencies<TRollback> {
  readonly client:
    RetainedWorkspaceActivationClient & RetainedWorkspaceSurfaceClient;
  readonly document: {
    querySelector(selectors: string): Element | null;
    createElement(localName: "template"): HTMLTemplateElement;
    readonly body: { append(...nodes: (Node | string)[]): void };
  };
  readonly applicationRoot: { inert: boolean };
  readonly maxVisibleModels: number;
  isCurrent(navigationSequence: number): boolean;
  beginNavigation(): number;
  hasVisibleWorkspace(): boolean;
  captureRollback(): TRollback;
  cloneRollback(rollback: TRollback): TRollback;
  restoreRollback(rollback: TRollback): void;
  releaseRollback(rollback: TRollback): void;
  publish(
    posting: BrowserRetainedWorkspacePosting,
    models: RetainedWorkspaceModels,
  ): void;
  setLoading(): void;
  pushLocation(location: string): void;
  reportFailure(message: string, retry: () => void): void;
  reportPredecessorFailure(error: unknown): void;
  observe(promise: Promise<unknown>, label: string): void;
  errorMessage(error: unknown): string;
  escapeHtml(value: unknown): string;
  trapModalTab(dialog: HTMLElement, event: KeyboardEvent): void;
}

export interface WorkspaceFeedActivationCoordinator<TRollback = never> {
  readonly activeUrl: string | null;
  readonly blocksUrlSynchronization: boolean;
  captureCommittedRollback(): TRollback | null;
  transferCommittedRollback(): TRollback | null;
  tryOpen(
    url: URL,
    navigationSequence: number,
    commitHistory?: boolean,
  ): Promise<boolean>;
  cancelPrompt(showFailure?: boolean): void;
  clearActiveUrl(): void;
}

interface PendingWorkspaceCredentialPrompt {
  retainedDefinitionId: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
  readonly navigationSequence: number;
  readonly requirements: readonly BrowserWorkspacePackageSourceRequirement[];
  readonly hadVisibleWorkspace: boolean;
  readonly commitHistory: boolean;
  submitting: boolean;
  error: string;
}

export function createWorkspaceFeedActivationCoordinator<TRollback>(
  dependencies: WorkspaceFeedActivationDependencies<TRollback>,
): WorkspaceFeedActivationCoordinator<TRollback> {
  let controller: RetainedWorkspaceActivationController | null = null;
  let prompt: PendingWorkspaceCredentialPrompt | null = null;
  let posted: {
    readonly value: BrowserRetainedWorkspacePosting;
    readonly navigationSequence: number;
    readonly published: boolean;
  } | null = null;
  let rollback: {
    readonly retainedDefinitionId: string;
    readonly navigationSequence: number;
    readonly state: TRollback;
    readonly sourceUrl: string | null;
  } | null = null;
  const activationSequences = new Map<string, number>();
  const deliveredDefinitionIds = new Set<string>();
  let activeUrl: string | null = null;
  let lastFailure: string | null = null;
  let pendingNavigationSequence: number | null = null;

  function activationController(): RetainedWorkspaceActivationController {
    controller ??= createRetainedWorkspaceActivationController(
      dependencies.client,
      {
        post(posting) {
          const navigationSequence =
            activationSequences.get(posting.retainedDefinitionId);
          if (navigationSequence === undefined) {
            throw new Error(
              "The retained Workspace posting has no activation owner.");
          }
          posted = {
            value: posting,
            navigationSequence,
            published: false,
          };
        },
        clear() {
          if (posted !== null) {
            reconcileRollback(
              posted.value.retainedDefinitionId,
              posted.navigationSequence);
          }
          posted = null;
        },
        predecessorSettled() {},
        predecessorObservationFailed(_observation, error) {
          dependencies.reportPredecessorFailure(error);
        },
      },
    );
    return controller;
  }

  async function retainDefinition(
    canonicalLocation: string,
    canonicalPacket: string,
  ): Promise<string> {
    const currentController = activationController();
    const existing = currentController.state.definitions.find(definition =>
      definition.canonicalLocation === canonicalLocation
      && definition.canonicalPacket === canonicalPacket);
    if (existing !== undefined) {
      if (currentController.state.activeDefinitionId !== existing.id
        || deliveredDefinitionIds.has(existing.id)) {
        return existing.id;
      }
      await currentController.delete(existing.id, {
        successorDefinitionId: null,
      });
      deliveredDefinitionIds.delete(existing.id);
    }

    const protectedIds = new Set([
      currentController.state.activeDefinitionId,
      currentController.state.pendingDefinitionId,
      ...currentController.state.unsettledDefinitionIds,
    ]);
    for (const definition of currentController.state.definitions) {
      if (!protectedIds.has(definition.id)) {
        await currentController.delete(definition.id);
        deliveredDefinitionIds.delete(definition.id);
      }
    }

    return currentController.retain({
      label: "Shared Workspace",
      canonicalLocation,
      canonicalPacket,
    }).id;
  }

  async function tryOpen(
    url: URL,
    navigationSequence: number,
    commitHistory = false,
  ): Promise<boolean> {
    const packet = url.searchParams.get("w");
    if (!packet) return false;
    const description =
      await dependencies.client.describeWorkspacePackageSources?.(packet);
    if (!dependencies.isCurrent(navigationSequence)
      || description === undefined
      || !description.succeeded
      || description.sources.length === 0) {
      return false;
    }

    cancelPrompt(false);
    const retainedDefinitionId = await retainDefinition(
      url.toString(),
      packet);
    if (!dependencies.isCurrent(navigationSequence)) return false;
    pendingNavigationSequence = navigationSequence;
    if (!ownsTentativeVisibleProjection()) {
      releaseRollback();
      rollback = {
        retainedDefinitionId,
        navigationSequence,
        state: dependencies.captureRollback(),
        sourceUrl: activeUrl,
      };
    }
    const required = description.sources.filter(
      source => source.authentication === "AuthenticationRequired");
    if (required.length > 0) {
      prompt = {
        retainedDefinitionId,
        canonicalLocation: url.toString(),
        canonicalPacket: packet,
        navigationSequence,
        requirements: required,
        hadVisibleWorkspace: dependencies.hasVisibleWorkspace(),
        commitHistory,
        submitting: false,
        error: "",
      };
      mountPrompt();
      return true;
    }

    await activate(
      retainedDefinitionId,
      url.toString(),
      navigationSequence,
      {},
      commitHistory);
    return true;
  }

  function mountPrompt(): void {
    dependencies.document
      .querySelector("#workspace-credential-backdrop")
      ?.remove();
    if (prompt === null) {
      dependencies.applicationRoot.inert = false;
      return;
    }
    const current = prompt;
    const template = dependencies.document.createElement("template");
    template.innerHTML = workspaceCredentialPromptHtml({
      requirements: current.requirements,
      submitting: current.submitting,
      error: current.error,
    }, value => dependencies.escapeHtml(value));
    const backdrop = template.content.firstElementChild;
    if (!(backdrop instanceof HTMLElement)) {
      throw new Error("The Workspace credential prompt did not render.");
    }
    dependencies.document.body.append(backdrop);
    dependencies.applicationRoot.inert = true;
    const dialog =
      backdrop.querySelector<HTMLElement>("#workspace-credential-dialog");
    const form =
      backdrop.querySelector<HTMLFormElement>("#workspace-credential-form");
    const cancel =
      backdrop.querySelector<HTMLButtonElement>("#workspace-credential-cancel");
    if (!dialog || !form || !cancel) {
      throw new Error("The Workspace credential prompt is incomplete.");
    }
    dialog.addEventListener("keydown", event => {
      if (event.key === "Tab") dependencies.trapModalTab(dialog, event);
      if (event.key === "Escape" && !current.submitting) {
        event.preventDefault();
        cancelPrompt();
      }
    });
    cancel.addEventListener("click", () => cancelPrompt());
    form.addEventListener("submit", event => {
      event.preventDefault();
      void submitCredentials(form, current);
    });
    const focusTarget = current.error
      ? backdrop.querySelector<HTMLElement>(".workspace-credential-error")
      : backdrop.querySelector<HTMLInputElement>(
          "#workspace-source-username-0");
    focusTarget?.focus({ preventScroll: true });
  }

  function cancelPrompt(showFailure = true): void {
    const current = prompt;
    if (current === null) return;
    const preserveCommittedRollback =
      ownsTentativeVisibleProjection();
    const cancelled = activationController().cancelPending();
    if (cancelled && !preserveCommittedRollback) {
      releaseRollback(
        current.retainedDefinitionId,
        current.navigationSequence);
    }
    clearPendingNavigation(current.navigationSequence);
    prompt = null;
    dependencies.document
      .querySelector("#workspace-credential-backdrop")
      ?.remove();
    dependencies.applicationRoot.inert = false;
    if (showFailure && !current.hadVisibleWorkspace) {
      dependencies.reportFailure(
        "This Workspace was not opened because its NuGet credentials were not provided.",
        () => retry(current.canonicalLocation, current.commitHistory));
    }
  }

  async function submitCredentials(
    form: HTMLFormElement,
    current: PendingWorkspaceCredentialPrompt,
  ): Promise<void> {
    if (prompt !== current || current.submitting) return;
    const credentials: Record<string, { username: string; pat: string }> = {};
    const secretValues: string[] = [];
    for (let index = 0; index < current.requirements.length; index++) {
      const username = form.querySelector<HTMLInputElement>(
        `#workspace-source-username-${index}`)?.value ?? "";
      const patInput = form.querySelector<HTMLInputElement>(
        `#workspace-source-pat-${index}`);
      const pat = patInput?.value ?? "";
      if (username.trim().length === 0 || pat.length === 0) {
        current.error =
          "Each source requires a username and personal access token.";
        mountPrompt();
        return;
      }
      credentials[current.requirements[index]!.endpoint] = { username, pat };
      secretValues.push(pat);
      if (patInput) patInput.value = "";
    }
    current.submitting = true;
    current.error = "";
    mountPrompt();
    try {
      const retainedDefinitionId = await retainDefinition(
        current.canonicalLocation,
        current.canonicalPacket);
      if (!dependencies.isCurrent(current.navigationSequence)
        || prompt !== current) {
        return;
      }
      if (retainedDefinitionId !== current.retainedDefinitionId) {
        retargetRollback(
          current.retainedDefinitionId,
          retainedDefinitionId,
          current.navigationSequence);
        current.retainedDefinitionId = retainedDefinitionId;
      }
      if (rollback === null) {
        rollback = {
          retainedDefinitionId,
          navigationSequence: current.navigationSequence,
          state: dependencies.captureRollback(),
          sourceUrl: activeUrl,
        };
      }
      const succeeded = await activate(
        retainedDefinitionId,
        current.canonicalLocation,
        current.navigationSequence,
        credentials,
        current.commitHistory);
      if (!dependencies.isCurrent(current.navigationSequence)
        || prompt !== current) {
        return;
      }
      if (succeeded) {
        prompt = null;
        clearPendingNavigation(current.navigationSequence);
        dependencies.document
          .querySelector("#workspace-credential-backdrop")
          ?.remove();
        dependencies.applicationRoot.inert = false;
        return;
      }
      current.submitting = false;
      current.error = redactCredentialValues(
        lastFailure
          ?? activationController().state.lastFailure
          ?? "The Workspace could not be opened with those credentials.",
        secretValues);
      mountPrompt();
    } catch (error) {
      if (!dependencies.isCurrent(current.navigationSequence)
        || prompt !== current) {
        return;
      }
      current.submitting = false;
      current.error = redactCredentialValues(
        dependencies.errorMessage(error),
        secretValues);
      mountPrompt();
    } finally {
      for (const credential of Object.values(credentials)) {
        credential.username = "";
        credential.pat = "";
      }
      secretValues.fill("");
    }
  }

  async function activate(
    retainedDefinitionId: string,
    canonicalLocation: string,
    navigationSequence: number,
    credentials: Readonly<
      Record<string, { readonly username: string; readonly pat: string }>
    >,
    commitHistory: boolean,
  ): Promise<boolean> {
    let projected = false;
    let publicationAttempted = false;
    const currentController = activationController();
    lastFailure = null;
    activationSequences.set(retainedDefinitionId, navigationSequence);

    async function projectPosting(
      posting: BrowserRetainedWorkspacePosting,
    ): Promise<void> {
      if (!dependencies.isCurrent(navigationSequence)) {
        throw new Error(
          "Workspace activation was superseded before publication.");
      }
      const models = await loadRetainedWorkspaceModels(
        dependencies.client,
        posting);
      if (!dependencies.isCurrent(navigationSequence)) {
        throw new Error(
          "Workspace activation was superseded before publication.");
      }
      if (posted?.value !== posting
        || posted.navigationSequence !== navigationSequence) {
        throw new Error(
          "The retained Workspace posting changed before visible publication.");
      }
      publicationAttempted = true;
      dependencies.publish(posting, models);
      posted = {
        value: posting,
        navigationSequence,
        published: true,
      };
      projected = true;
    }

    try {
      const result = await currentController.activate(
        retainedDefinitionId,
        preparation =>
          dependencies.isCurrent(navigationSequence)
          && preparation.packages.length + preparation.platforms.length
            <= dependencies.maxVisibleModels,
        projectPosting,
        () => {
          if (dependencies.isCurrent(navigationSequence)
            && !dependencies.hasVisibleWorkspace()) {
            dependencies.setLoading();
            if (prompt !== null) mountPrompt();
          }
        },
        credentials,
      );
      if (!dependencies.isCurrent(navigationSequence)) {
        releaseRollback(retainedDefinitionId, navigationSequence);
        return false;
      }
      if (result.status === "noEffect" && result.posting !== null) {
        if (!deliveredDefinitionIds.has(retainedDefinitionId)) {
          throw new Error(
            "The retained Workspace did not complete its prior publication.");
        }
        posted = {
          value: result.posting,
          navigationSequence,
          published: false,
        };
        await projectPosting(result.posting);
      }
      if ((result.status === "activated" || result.status === "noEffect")
        && projected) {
        deliveredDefinitionIds.add(retainedDefinitionId);
        activeUrl = canonicalLocation;
        releaseRollback(retainedDefinitionId, navigationSequence);
        if (commitHistory) dependencies.pushLocation(canonicalLocation);
        return true;
      }
      lastFailure = result.failure?.message
        ?? currentController.state.lastFailure
        ?? "The shared Workspace could not be opened.";
      if (prompt === null) {
        releaseRollback(retainedDefinitionId, navigationSequence);
        dependencies.reportFailure(
          lastFailure,
          () => retry(canonicalLocation, commitHistory));
      }
      return false;
    } catch (error) {
      lastFailure = dependencies.errorMessage(error);
      if (publicationAttempted
        && posted?.value.retainedDefinitionId === retainedDefinitionId
        && posted.navigationSequence === navigationSequence) {
        reconcileRollback(retainedDefinitionId, navigationSequence);
        posted = null;
      } else if (!dependencies.isCurrent(navigationSequence)
        || prompt === null) {
        releaseRollback(retainedDefinitionId, navigationSequence);
      }
      if (!dependencies.isCurrent(navigationSequence)) return false;
      if (prompt === null) {
        dependencies.reportFailure(
          lastFailure,
          () => retry(canonicalLocation, commitHistory));
      }
      return false;
    } finally {
      if (activationSequences.get(retainedDefinitionId)
        === navigationSequence) {
        activationSequences.delete(retainedDefinitionId);
      }
      if (prompt === null) clearPendingNavigation(navigationSequence);
    }
  }

  function clearPendingNavigation(navigationSequence: number): void {
    if (pendingNavigationSequence === navigationSequence) {
      pendingNavigationSequence = null;
    }
  }

  function retargetRollback(
    priorRetainedDefinitionId: string,
    retainedDefinitionId: string,
    navigationSequence: number,
  ): void {
    if (rollback?.retainedDefinitionId !== priorRetainedDefinitionId
      || rollback.navigationSequence !== navigationSequence) {
      return;
    }
    rollback = {
      ...rollback,
      retainedDefinitionId,
    };
  }

  function retry(canonicalLocation: string, commitHistory: boolean): void {
    const navigationSequence = dependencies.beginNavigation();
    dependencies.observe(
      tryOpen(
        new URL(canonicalLocation),
        navigationSequence,
        commitHistory),
      "Opening private Workspace");
  }

  function reconcileRollback(
    retainedDefinitionId: string,
    navigationSequence: number,
  ): void {
    const prior = rollback;
    if (prior === null
      || prior.retainedDefinitionId !== retainedDefinitionId
      || prior.navigationSequence !== navigationSequence) {
      return;
    }
    rollback = null;
    const stillOwnsVisiblePosting =
      posted?.value.retainedDefinitionId === retainedDefinitionId
      && posted.navigationSequence === navigationSequence
      && posted.published;
    if (!dependencies.isCurrent(navigationSequence)
      && !stillOwnsVisiblePosting) {
      dependencies.releaseRollback(prior.state);
      return;
    }
    dependencies.restoreRollback(prior.state);
    activeUrl = prior.sourceUrl;
    if (prompt?.retainedDefinitionId === retainedDefinitionId
      && prompt.navigationSequence === navigationSequence) {
      rollback = {
        retainedDefinitionId,
        navigationSequence,
        state: dependencies.captureRollback(),
        sourceUrl: prior.sourceUrl,
      };
    }
  }

  function releaseRollback(
    retainedDefinitionId?: string,
    navigationSequence?: number,
  ): void {
    if (rollback === null) return;
    if (retainedDefinitionId !== undefined
      && rollback.retainedDefinitionId !== retainedDefinitionId) return;
    if (navigationSequence !== undefined
      && rollback.navigationSequence !== navigationSequence) return;
    dependencies.releaseRollback(rollback.state);
    rollback = null;
  }

  function ownsTentativeVisibleProjection(): boolean {
    return posted !== null
      && posted.published
      && !deliveredDefinitionIds.has(posted.value.retainedDefinitionId)
      && rollback?.retainedDefinitionId
        === posted.value.retainedDefinitionId
      && rollback.navigationSequence === posted.navigationSequence;
  }

  return {
    get activeUrl() {
      return activeUrl;
    },
    get blocksUrlSynchronization() {
      return ownsTentativeVisibleProjection()
        || (pendingNavigationSequence !== null
          && dependencies.isCurrent(pendingNavigationSequence));
    },
    captureCommittedRollback() {
      return ownsTentativeVisibleProjection() && rollback !== null
        ? dependencies.cloneRollback(rollback.state)
        : null;
    },
    transferCommittedRollback() {
      if (!ownsTentativeVisibleProjection() || rollback === null) return null;
      const transferred = dependencies.cloneRollback(rollback.state);
      releaseRollback();
      posted = null;
      return transferred;
    },
    tryOpen,
    cancelPrompt,
    clearActiveUrl() {
      releaseRollback();
      posted = null;
      activeUrl = null;
    },
  };
}

function redactCredentialValues(
  message: string,
  secrets: readonly string[],
): string {
  let redacted = message;
  for (const secret of secrets) {
    if (secret.length > 0)
      redacted = redacted.replaceAll(secret, "[credential]");
  }
  return redacted;
}

export function workspaceCredentialPromptHtml(
  model: WorkspaceCredentialPromptModel,
  escapeHtml: (value: unknown) => string,
): string {
  const fields = model.requirements.map((requirement, index) => {
    const endpoint = escapeHtml(requirement.endpoint);
    return `<fieldset class="workspace-credential-source">
      <legend>${endpoint}</legend>
      <label for="workspace-source-username-${index}">Username</label>
      <input id="workspace-source-username-${index}" name="username-${index}"
        type="text" autocomplete="off" required
        ${model.submitting ? "disabled" : ""}>
      <label for="workspace-source-pat-${index}">Personal access token</label>
      <input id="workspace-source-pat-${index}" name="pat-${index}"
        type="password" autocomplete="off" required
        ${model.submitting ? "disabled" : ""}>
    </fieldset>`;
  }).join("");
  return `<div id="workspace-credential-backdrop" class="modal-backdrop">
    <section id="workspace-credential-dialog"
      class="application-dialog workspace-credential-dialog"
      role="dialog" aria-modal="true"
      aria-labelledby="workspace-credential-title"
      aria-describedby="workspace-credential-description">
      <header class="application-dialog-head">
        <div>
          <p class="section-eyebrow">Private NuGet sources</p>
          <h2 id="workspace-credential-title" tabindex="-1">Sign in to open this Workspace</h2>
        </div>
      </header>
      <form id="workspace-credential-form" class="workspace-credential-form"
        autocomplete="off">
        <p id="workspace-credential-description">
          These credentials are used only for this page session. They are not
          added to the Workspace link or browser storage.
        </p>
        ${fields}
        ${model.error
          ? `<p class="workspace-credential-error" role="alert">${escapeHtml(model.error)}</p>`
          : ""}
        <div class="workspace-credential-actions">
          <button id="workspace-credential-cancel" type="button"
            ${model.submitting ? "disabled" : ""}>Cancel</button>
          <button type="submit" ${model.submitting ? "disabled" : ""}>
            ${model.submitting ? "Opening…" : "Open Workspace"}
          </button>
        </div>
      </form>
    </section>
  </div>`;
}

export async function loadRetainedWorkspaceModels(
  client: RetainedWorkspaceSurfaceClient,
  posting: BrowserRetainedWorkspacePosting,
): Promise<RetainedWorkspaceModels> {
  const packages = await Promise.all(posting.packages.map(async inventory => ({
    navigationId: inventory.navigationId,
    contextIndex: inventory.contextIndex,
    consumerPackageSubjectId: inventory.consumerPackageSubjectId,
    packageModel: workspacePackageModel(
      await loadPackageSurface(client, posting, inventory)),
  })));
  const platforms = await Promise.all(
    posting.platforms.map(async inventory => ({
      navigationId: inventory.navigationId,
      contextIndex: inventory.contextIndex,
      family: inventory.family,
      runtimeIdentifier: inventory.runtimeIdentifier,
      packageModel: {
        ...createRuntimePackageModel(
          await loadPlatformSurface(client, posting, inventory)),
        runtimeIdentifier: inventory.runtimeIdentifier,
      },
    })),
  );
  return { packages, platforms };
}

function workspacePackageModel(surface: BrowserPackageSurface): AppPackage {
  return {
    ...createNuGetPackageModel(surface),
    source: { kind: "unknown" },
    producerLabel: "NuGet",
  };
}

async function loadPackageSurface(
  client: RetainedWorkspaceSurfaceClient,
  posting: BrowserRetainedWorkspacePosting,
  inventory: BrowserRetainedWorkspacePackageInventory,
): Promise<BrowserPackageSurface> {
  let offset = 0;
  let surface: BrowserPackageSurface | null = null;
  while (true) {
    const result = await client.admitRetainedWorkspacePackage(
      posting.retainedDefinitionId,
      posting.realizationId,
      inventory.navigationId,
      offset);
    if (result.status !== "admitted" || result.package === null) {
      throw new Error(admissionFailure(
        "Package",
        result.status,
        result.message));
    }
    const admission = result.package;
    if (admission.typePage.offset !== offset) {
      throw new Error(
        `Retained Package admission returned Type offset `
        + `${admission.typePage.offset}; expected ${offset}.`);
    }
    surface = appendTypePage(surface, admission.surface);
    if (admission.typePage.nextOffset === null) return surface;
    assertNextOffset(
      "Package",
      offset,
      admission.typePage.nextOffset,
      admission.typePage.totalTypes);
    offset = admission.typePage.nextOffset;
  }
}

async function loadPlatformSurface(
  client: RetainedWorkspaceSurfaceClient,
  posting: BrowserRetainedWorkspacePosting,
  inventory: BrowserRetainedWorkspacePlatformInventory,
): Promise<BrowserPackageSurface> {
  let offset = 0;
  let surface: BrowserPackageSurface | null = null;
  while (true) {
    const result = await client.admitRetainedWorkspacePlatform(
      posting.retainedDefinitionId,
      posting.realizationId,
      inventory.navigationId,
      offset);
    if (result.status !== "admitted" || result.platform === null) {
      throw new Error(admissionFailure(
        "Platform",
        result.status,
        result.message));
    }
    const admission = result.platform;
    if (admission.typePage.offset !== offset) {
      throw new Error(
        `Retained Platform admission returned Type offset `
        + `${admission.typePage.offset}; expected ${offset}.`);
    }
    surface = appendTypePage(surface, admission.surface);
    if (admission.typePage.nextOffset === null) return surface;
    assertNextOffset(
      "Platform",
      offset,
      admission.typePage.nextOffset,
      admission.typePage.totalTypes);
    offset = admission.typePage.nextOffset;
  }
}

function appendTypePage(
  current: BrowserPackageSurface | null,
  page: BrowserPackageSurface,
): BrowserPackageSurface {
  if (current === null) return page;
  if (current.package !== page.package
    || current.version !== page.version
    || current.activeFramework !== page.activeFramework) {
    throw new Error(
      "Retained Workspace Type pages changed package identity during admission.");
  }
  return { ...current, types: [...current.types, ...page.types] };
}

function assertNextOffset(
  kind: "Package" | "Platform",
  current: number,
  next: number,
  total: number,
): void {
  if (next <= current || next > total) {
    throw new Error(
      `Retained ${kind} admission returned invalid next Type offset ${next}.`);
  }
}

function admissionFailure(
  kind: "Package" | "Platform",
  status: string,
  message: string | null,
): string {
  return message
    ? `Retained ${kind} admission failed: ${message}`
    : `Retained ${kind} admission ended with status '${status}'.`;
}
