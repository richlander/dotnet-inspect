import type {
  BrowserRetainedNavigationAction,
  BrowserRetainedNavigationDiagnostic,
  BrowserRetainedNavigationLensDescriptor,
  BrowserRetainedNavigationLensOutcome,
  BrowserRetainedNavigationPackageDescriptor,
  BrowserRetainedNavigationSnapshot,
  BrowserRetainedNavigationSubjectDescriptor,
  BrowserRetainedWorkspacePackageInventory,
  BrowserRetainedWorkspacePosting,
  BrowserRetainedWorkspaceSurfaceSummary,
} from "./facades/inspect-web-catalog.d.ts";

interface NavigationDescriptorPresentationItem {
  readonly key: string;
  readonly identity: string | null;
  readonly kind: string;
  readonly label: string;
  readonly summary: string | null;
  readonly state: string;
  readonly current: boolean;
  readonly retained: boolean;
  readonly action: string | null;
  readonly localAction: "choose-member" | null;
  readonly evidence: string | null;
}

export interface NavigationPackagePresentationItem {
  readonly order: number;
  readonly subject: NavigationDescriptorPresentationItem;
  readonly package: string;
  readonly version: string;
  readonly framework: string | null;
  readonly runtimeIdentifier: string | null;
  readonly realization: string;
  readonly realizationFailure: string | null;
  readonly summary: BrowserRetainedWorkspaceSurfaceSummary;
}

interface NavigationLensOutcomePresentation {
  readonly status: "Lens unavailable" | "Lens failed";
  readonly evidence: string | null;
}

export interface NavigationDescriptorPresentation {
  readonly workspace: NavigationDescriptorPresentationItem;
  readonly subjectLabel: string;
  readonly subjects: readonly NavigationDescriptorPresentationItem[];
  readonly inspectors: readonly NavigationDescriptorPresentationItem[];
  readonly lensOutcome: NavigationLensOutcomePresentation | null;
  readonly packages: readonly NavigationPackagePresentationItem[];
  readonly actions: ReadonlyMap<string, BrowserRetainedNavigationAction>;
}

export function createNavigationDescriptorPresentation(
  posting: BrowserRetainedWorkspacePosting,
): NavigationDescriptorPresentation {
  const snapshot = posting.navigation.snapshot;
  const actions = collectActions(snapshot);
  const hierarchy = snapshot.hierarchy.map(subjectPresentation);
  const workspace = hierarchy.find(item =>
    item.kind.toLowerCase() === "workspace");
  if (workspace === undefined) {
    throw new Error(
      "The retained Navigation snapshot omitted its Workspace descriptor.",
    );
  }

  return {
    workspace,
    subjectLabel: snapshot.activeSubject.label,
    subjects: hierarchy.filter(item =>
      item.kind.toLowerCase() !== "workspace"),
    inspectors: snapshot.lenses.map(lensPresentation),
    lensOutcome: lensOutcomePresentation(snapshot.lensOutcome),
    packages: packagePresentation(snapshot.packages, posting.packages),
    actions,
  };
}

export function resolveNavigationPresentationAction(
  presentation: NavigationDescriptorPresentation,
  token: string,
): BrowserRetainedNavigationAction {
  const action = presentation.actions.get(token);
  if (action === undefined) {
    throw new Error(`Unknown product Navigation action '${token}'.`);
  }
  return action;
}

export function bindNavigationDescriptorActions(
  root: ParentNode,
  presentation: NavigationDescriptorPresentation,
  actions: {
    activate(action: BrowserRetainedNavigationAction): void;
    chooseMember(): void;
  },
): void {
  root.querySelectorAll<HTMLElement>(
    "[data-product-navigation-action]",
  ).forEach(element => element.addEventListener("click", () => {
    const token = element.dataset.productNavigationAction;
    if (token !== undefined) {
      actions.activate(
        resolveNavigationPresentationAction(presentation, token));
    }
  }));
  root.querySelectorAll<HTMLElement>(
    '[data-local-navigation-action="choose-member"]',
  ).forEach(element => element.addEventListener(
    "click",
    () => actions.chooseMember()));
}

function subjectPresentation(
  descriptor: BrowserRetainedNavigationSubjectDescriptor,
): NavigationDescriptorPresentationItem {
  const subject = descriptor.subject;
  const selectionRequired =
    descriptor.kind.toLowerCase() === "member"
    && descriptor.state.toLowerCase() === "selectionrequired";
  return {
    key: subject?.id ?? `unavailable:${descriptor.kind}`,
    identity: subject?.id ?? null,
    kind: descriptor.kind,
    label: selectionRequired ? "Choose a member" : descriptor.label,
    summary: subject?.summary ?? null,
    state: descriptor.state,
    current: descriptor.isActive,
    retained: descriptor.isRetained,
    action: descriptor.action?.id ?? null,
    localAction: selectionRequired ? "choose-member" : null,
    evidence: hierarchyEvidence(descriptor.evidence),
  };
}

function hierarchyEvidence(
  evidence: readonly BrowserRetainedNavigationDiagnostic[],
): string | null {
  return evidence.length === 0
    ? null
    : evidence.map(diagnostic => diagnostic.message).join("; ");
}

function lensPresentation(
  descriptor: BrowserRetainedNavigationLensDescriptor,
): NavigationDescriptorPresentationItem {
  const identity = descriptor.facet.id;
  return {
    key: identity,
    identity,
    kind: descriptor.facet.kind,
    label: descriptor.facet.title,
    summary: descriptor.facet.summary,
    state: descriptor.state,
    current: descriptor.isCurrent,
    retained: descriptor.isCurrent,
    action: descriptor.action?.id ?? null,
    localAction: null,
    evidence: descriptor.message ?? descriptor.unavailability,
  };
}

function lensOutcomePresentation(
  outcome: BrowserRetainedNavigationLensOutcome,
): NavigationLensOutcomePresentation | null {
  if (outcome.effectiveLens !== null) return null;

  const status = outcome.kind.toLowerCase() === "unavailable"
    ? "Lens unavailable"
    : outcome.kind.toLowerCase() === "failed"
      ? "Lens failed"
      : null;
  if (status === null) {
    throw new Error(
      `Navigation supplied '${outcome.kind}' without an effective lens.`);
  }

  return {
    status,
    evidence: outcome.suspension?.failure
      ?? outcome.policyFailure
      ?? outcome.resolution?.message
      ?? outcome.resolution?.unavailability
      ?? outcome.suspension?.kind
      ?? null,
  };
}

function packagePresentation(
  descriptors: readonly BrowserRetainedNavigationPackageDescriptor[],
  inventories: readonly BrowserRetainedWorkspacePackageInventory[],
): readonly NavigationPackagePresentationItem[] {
  const inventoriesBySubject =
    new Map<string, BrowserRetainedWorkspacePackageInventory>();
  for (const inventory of inventories) {
    if (inventoriesBySubject.has(inventory.consumerPackageSubjectId)) {
      throw new Error(
        `The retained Workspace supplied duplicate Package presentation '${inventory.consumerPackageSubjectId}'.`,
      );
    }
    inventoriesBySubject.set(inventory.consumerPackageSubjectId, inventory);
  }

  const projected = descriptors
    .map((descriptor, index) => ({ descriptor, index }))
    .sort((left, right) =>
      left.descriptor.order - right.descriptor.order
      || left.index - right.index)
    .map(({ descriptor }) => {
      const inventory = inventoriesBySubject.get(descriptor.subject.id);
      if (inventory === undefined) {
        throw new Error(
          `The retained Workspace omitted Package presentation '${descriptor.subject.id}'.`,
        );
      }
      inventoriesBySubject.delete(descriptor.subject.id);
      return {
        order: descriptor.order,
        subject: {
          key: descriptor.subject.id,
          identity: descriptor.subject.id,
          kind: descriptor.subject.kind,
          label: descriptor.subject.label,
          summary: descriptor.subject.summary,
          state: descriptor.state,
          current: descriptor.isCurrent,
          retained: true,
          action: descriptor.action?.id ?? null,
          localAction: null,
          evidence: descriptor.realizationFailure,
        },
        package: descriptor.packageId,
        version: descriptor.version,
        framework: descriptor.framework,
        runtimeIdentifier: descriptor.runtimeIdentifier,
        realization: descriptor.realization,
        realizationFailure: descriptor.realizationFailure,
        summary: inventory.summary,
      };
    });

  if (inventoriesBySubject.size > 0) {
    throw new Error(
      "The retained Workspace supplied Package presentations without matching Navigation descriptors.",
    );
  }
  return projected;
}

function collectActions(
  snapshot: BrowserRetainedNavigationSnapshot,
): ReadonlyMap<string, BrowserRetainedNavigationAction> {
  const actions = new Map<string, BrowserRetainedNavigationAction>();
  const add = (action: BrowserRetainedNavigationAction | null) => {
    if (action === null) return;
    const existing = actions.get(action.id);
    if (existing !== undefined
      && (existing.session !== action.session
        || existing.generation !== action.generation
        || existing.source !== action.source
        || existing.kind !== action.kind)) {
      throw new Error(
        `Product Navigation action '${action.id}' aliases different requests.`,
      );
    }
    actions.set(action.id, action);
  };
  const addLens = (lens: BrowserRetainedNavigationLensDescriptor) =>
    add(lens.action);

  snapshot.packages.forEach(descriptor => add(descriptor.action));
  snapshot.hierarchy.forEach(descriptor => add(descriptor.action));
  snapshot.libraries.forEach(descriptor => add(descriptor.navigation.action));
  snapshot.types.forEach(descriptor => {
    add(descriptor.navigation.action);
    descriptor.descendantLenses.forEach(addLens);
  });
  snapshot.members.forEach(descriptor => {
    add(descriptor.navigation.action);
    descriptor.descendantLenses.forEach(addLens);
  });
  snapshot.lenses.forEach(addLens);
  return actions;
}
