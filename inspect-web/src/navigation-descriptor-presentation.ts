import type {
  BrowserPackageSurface,
  BrowserRetainedNavigationAction,
  BrowserRetainedNavigationLensDescriptor,
  BrowserRetainedNavigationPackageDescriptor,
  BrowserRetainedNavigationSnapshot,
  BrowserRetainedNavigationSubjectDescriptor,
  BrowserRetainedWorkspacePackage,
  BrowserRetainedWorkspacePosting,
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
  readonly surface: BrowserPackageSurface;
}

export interface NavigationDescriptorPresentation {
  readonly workspace: NavigationDescriptorPresentationItem;
  readonly subjects: readonly NavigationDescriptorPresentationItem[];
  readonly inspectors: readonly NavigationDescriptorPresentationItem[];
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
    subjects: hierarchy.filter(item =>
      item.kind.toLowerCase() !== "workspace"),
    inspectors: snapshot.lenses.map(lensPresentation),
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
    evidence: null,
  };
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

function packagePresentation(
  descriptors: readonly BrowserRetainedNavigationPackageDescriptor[],
  surfaces: readonly BrowserRetainedWorkspacePackage[],
): readonly NavigationPackagePresentationItem[] {
  const surfacesBySubject = new Map<string, BrowserRetainedWorkspacePackage>();
  for (const surface of surfaces) {
    if (surfacesBySubject.has(surface.consumerPackageSubjectId)) {
      throw new Error(
        `The retained Workspace supplied duplicate Package presentation '${surface.consumerPackageSubjectId}'.`,
      );
    }
    surfacesBySubject.set(surface.consumerPackageSubjectId, surface);
  }

  const projected = descriptors
    .map((descriptor, index) => ({ descriptor, index }))
    .sort((left, right) =>
      left.descriptor.order - right.descriptor.order
      || left.index - right.index)
    .map(({ descriptor }) => {
      const surface = surfacesBySubject.get(descriptor.subject.id);
      if (surface === undefined) {
        throw new Error(
          `The retained Workspace omitted Package presentation '${descriptor.subject.id}'.`,
        );
      }
      surfacesBySubject.delete(descriptor.subject.id);
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
        surface: surface.surface,
      };
    });

  if (surfacesBySubject.size > 0) {
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
