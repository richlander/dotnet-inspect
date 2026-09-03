import type {
  NavigationHistorySnapshot,
  WorkspaceView,
} from "./workspace-navigation.ts";
import type { BrowserWorkspaceShareState } from "./inspect-web-engine.d.ts";

export const MAX_LIVE_WORKSPACES = 4;
const DEFAULT_WORKSPACE_NAME = "Default";

const WORKSPACE_HISTORY_KEY = "dotnetInspectWorkspaceId";

export interface LiveWorkspace<TPackage> {
  id: string;
  name: string;
  isDefault: boolean;
  packages: TPackage[];
  activePackageKey: string | null;
  shareBasis: BrowserWorkspaceShareState | null;
  navigation: NavigationHistorySnapshot<WorkspaceView>;
}

export interface LiveWorkspaceSession<TPackage> {
  workspaces: LiveWorkspace<TPackage>[];
  selectedWorkspaceId: string;
  nextWorkspaceNumber: number;
}

export interface WorkspaceProjection<TPackage> {
  packages: readonly TPackage[];
  activePackageKey: string | null;
  shareBasis: BrowserWorkspaceShareState | null;
  navigation: NavigationHistorySnapshot<WorkspaceView>;
}

function emptyNavigation(): NavigationHistorySnapshot<WorkspaceView> {
  return { stack: [], index: -1 };
}

export function createLiveWorkspaceSession<TPackage>(
  defaultWorkspaceId: string,
): LiveWorkspaceSession<TPackage> {
  return {
    workspaces: [{
      id: defaultWorkspaceId,
      name: DEFAULT_WORKSPACE_NAME,
      isDefault: true,
      packages: [],
      activePackageKey: null,
      shareBasis: null,
      navigation: emptyNavigation(),
    }],
    selectedWorkspaceId: defaultWorkspaceId,
    nextWorkspaceNumber: 2,
  };
}

export function selectedLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
): LiveWorkspace<TPackage> {
  return session.workspaces.find(
    workspace => workspace.id === session.selectedWorkspaceId)
    ?? session.workspaces[0]!;
}

export function defaultLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
): LiveWorkspace<TPackage> {
  return session.workspaces.find(workspace => workspace.isDefault)
    ?? session.workspaces[0]!;
}

export function updateSelectedLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  projection: WorkspaceProjection<TPackage>,
): void {
  const workspace = selectedLiveWorkspace(session);
  workspace.packages = [...projection.packages];
  workspace.activePackageKey = projection.activePackageKey;
  workspace.shareBasis = projection.shareBasis;
  workspace.navigation = {
    stack: projection.navigation.stack.map(entry => ({ ...entry })),
    index: projection.navigation.index,
  };
}

export function createLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  workspaceId: string,
): LiveWorkspace<TPackage> | null {
  if (session.workspaces.length >= MAX_LIVE_WORKSPACES) return null;
  const workspace: LiveWorkspace<TPackage> = {
    id: workspaceId,
    name: `Workspace ${session.nextWorkspaceNumber++}`,
    isDefault: false,
    packages: [],
    activePackageKey: null,
    shareBasis: null,
    navigation: emptyNavigation(),
  };
  session.workspaces.push(workspace);
  session.selectedWorkspaceId = workspace.id;
  return workspace;
}

export function selectLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  workspaceId: string,
): LiveWorkspace<TPackage> | null {
  const workspace = session.workspaces.find(
    candidate => candidate.id === workspaceId);
  if (!workspace) return null;
  session.selectedWorkspaceId = workspace.id;
  return workspace;
}

export function removeLiveWorkspace<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  workspaceId: string,
): LiveWorkspace<TPackage> | null {
  const index = session.workspaces.findIndex(
    workspace => workspace.id === workspaceId);
  const workspace = session.workspaces[index];
  if (!workspace || workspace.isDefault) return null;
  session.workspaces.splice(index, 1);
  if (session.selectedWorkspaceId === workspaceId) {
    session.selectedWorkspaceId = defaultLiveWorkspace(session).id;
  }
  return workspace;
}

function historyRecord(value: unknown): Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? { ...value }
    : {};
}

export function workspaceHistoryId(value: unknown): string | null {
  const workspaceId = historyRecord(value)[WORKSPACE_HISTORY_KEY];
  return typeof workspaceId === "string" && workspaceId.length > 0
    ? workspaceId
    : null;
}

export function withWorkspaceHistoryId(
  value: unknown,
  workspaceId: string,
): Record<string, unknown> {
  return {
    ...historyRecord(value),
    [WORKSPACE_HISTORY_KEY]: workspaceId,
  };
}

export function workspaceForHistory<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  historyState: unknown,
): LiveWorkspace<TPackage> {
  const workspaceId = workspaceHistoryId(historyState);
  return session.workspaces.find(workspace => workspace.id === workspaceId)
    ?? defaultLiveWorkspace(session);
}

export type WorkspaceHistoryMembershipStatus =
  | "unassociated"
  | "current"
  | "stale";

export function workspaceHistoryMembershipStatus<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  historyState: unknown,
  requestedPackageKeys: readonly string[],
  packageKey: (value: TPackage) => string,
  exact: boolean,
): WorkspaceHistoryMembershipStatus {
  const workspaceId = workspaceHistoryId(historyState);
  const workspace = session.workspaces.find(
    candidate => candidate.id === workspaceId);
  if (!workspace) return "unassociated";
  const currentPackageKeys = workspace.packages.map(packageKey);
  const matches = exact
    ? currentPackageKeys.length === requestedPackageKeys.length
      && currentPackageKeys.every(
        (key, index) => key === requestedPackageKeys[index])
    : requestedPackageKeys.every(
        requested => currentPackageKeys.includes(requested));
  return matches ? "current" : "stale";
}

export function rememberedLiveWorkspaceHref<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  canonicalHrefs: ReadonlyMap<string, string>,
): string | null {
  return canonicalHrefs.get(session.selectedWorkspaceId) ?? null;
}

export interface WorkspaceOperationOwner {
  workspaceId: string;
  navigationSequence: number;
}

export interface WorkspaceProjectionTransactionController {
  begin(owner: WorkspaceOperationOwner): void;
  commit(owner: WorkspaceOperationOwner): boolean;
  abandon(): void;
  blocksSelectedWorkspaceSynchronization(): boolean;
  matches(owner: WorkspaceOperationOwner): boolean;
}

export interface WorkspaceProjectionTransactionDependencies<TPackage> {
  currentPackages(): readonly TPackage[];
  synchronize(): void;
  restore(workspace: LiveWorkspace<TPackage>): void;
  release(packageModel: TPackage): void;
}

export function createWorkspaceProjectionTransactionController<TPackage>(
  currentSession: () => LiveWorkspaceSession<TPackage>,
  dependencies: WorkspaceProjectionTransactionDependencies<TPackage>,
): WorkspaceProjectionTransactionController {
  let transaction: WorkspaceOperationOwner | null = null;
  const matches = (owner: WorkspaceOperationOwner) =>
    transaction?.workspaceId === owner.workspaceId
    && transaction.navigationSequence === owner.navigationSequence;

  return {
    begin(owner) {
      transaction = owner;
    },
    commit(owner) {
      if (!matches(owner)) return false;
      const session = currentSession();
      const replacedPackages = session.workspaces.find(
        workspace => workspace.id === owner.workspaceId)?.packages ?? [];
      transaction = null;
      dependencies.synchronize();
      for (const packageModel of replacedPackages) {
        if (!session.workspaces.some(
          workspace => workspace.packages.includes(packageModel))) {
          dependencies.release(packageModel);
        }
      }
      return true;
    },
    abandon() {
      const abandoned = transaction;
      if (!abandoned) return;
      transaction = null;
      const session = currentSession();
      if (session.selectedWorkspaceId !== abandoned.workspaceId) return;
      const transientPackages = [...dependencies.currentPackages()];
      const workspace = selectedLiveWorkspace(session);
      dependencies.restore(workspace);
      for (const packageModel of transientPackages) {
        if (!workspace.packages.includes(packageModel))
          dependencies.release(packageModel);
      }
    },
    blocksSelectedWorkspaceSynchronization() {
      const session = currentSession();
      return transaction?.workspaceId === session.selectedWorkspaceId;
    },
    matches,
  };
}

export function workspaceOperationIsCurrent<TPackage>(
  session: LiveWorkspaceSession<TPackage>,
  owner: WorkspaceOperationOwner,
  currentNavigationSequence: number,
): boolean {
  return session.selectedWorkspaceId === owner.workspaceId
    && owner.navigationSequence === currentNavigationSequence;
}
