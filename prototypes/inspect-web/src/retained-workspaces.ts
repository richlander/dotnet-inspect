export const MAX_RETAINED_WORKSPACES = 4;

interface RetainedWorkspace<TSnapshot> {
  id: string;
  label: string;
  snapshot: TSnapshot | null;
}

export interface RetainedWorkspaceCollection<TSnapshot> {
  workspaces: RetainedWorkspace<TSnapshot>[];
  activeWorkspaceId: string | null;
  nextOrdinal: number;
}

export interface RetainedWorkspaceTransition<TSnapshot> {
  collection: RetainedWorkspaceCollection<TSnapshot>;
  activatedSnapshot: TSnapshot | null;
  removedSnapshot: TSnapshot | null;
}

export function createRetainedWorkspaceCollection<TSnapshot>():
RetainedWorkspaceCollection<TSnapshot> {
  return {
    workspaces: [],
    activeWorkspaceId: null,
    nextOrdinal: 1,
  };
}

export function publishRetainedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  currentSnapshot: TSnapshot | null,
): RetainedWorkspaceCollection<TSnapshot> {
  if (collection.workspaces.length >= MAX_RETAINED_WORKSPACES) {
    throw new Error(
      `Inspect Web retains at most ${MAX_RETAINED_WORKSPACES} live Workspaces. Delete one before opening another.`);
  }
  if (collection.activeWorkspaceId !== null && currentSnapshot === null) {
    throw new Error("The active Workspace must be retained before publishing another.");
  }

  const id = `workspace-${collection.nextOrdinal}`;
  const workspaces = collection.workspaces.map(workspace =>
    workspace.id === collection.activeWorkspaceId
      ? { ...workspace, snapshot: currentSnapshot }
      : workspace);
  workspaces.push({
    id,
    label: `Workspace ${collection.nextOrdinal}`,
    snapshot: null,
  });
  return {
    workspaces,
    activeWorkspaceId: id,
    nextOrdinal: collection.nextOrdinal + 1,
  };
}

export function activateRetainedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  workspaceId: string,
  currentSnapshot: TSnapshot,
): RetainedWorkspaceTransition<TSnapshot> {
  if (workspaceId === collection.activeWorkspaceId) {
    return {
      collection,
      activatedSnapshot: null,
      removedSnapshot: null,
    };
  }
  const target = collection.workspaces.find(workspace => workspace.id === workspaceId);
  if (!target || target.snapshot === null) {
    throw new Error("That Workspace is no longer available.");
  }

  const activatedSnapshot = target.snapshot;
  return {
    collection: {
      ...collection,
      activeWorkspaceId: workspaceId,
      workspaces: collection.workspaces.map(workspace => {
        if (workspace.id === collection.activeWorkspaceId) {
          return { ...workspace, snapshot: currentSnapshot };
        }
        if (workspace.id === workspaceId) {
          return { ...workspace, snapshot: null };
        }
        return workspace;
      }),
    },
    activatedSnapshot,
    removedSnapshot: null,
  };
}

export function deleteRetainedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  workspaceId: string,
  currentSnapshot: TSnapshot | null,
): RetainedWorkspaceTransition<TSnapshot> {
  const index = collection.workspaces.findIndex(
    workspace => workspace.id === workspaceId);
  if (index < 0) throw new Error("That Workspace is no longer available.");

  const removed = collection.workspaces[index]!;
  if (workspaceId !== collection.activeWorkspaceId) {
    if (removed.snapshot === null) {
      throw new Error("An inactive Workspace must retain its snapshot.");
    }
    return {
      collection: {
        ...collection,
        workspaces: collection.workspaces.filter(
          workspace => workspace.id !== workspaceId),
      },
      activatedSnapshot: null,
      removedSnapshot: removed.snapshot,
    };
  }
  if (currentSnapshot === null) {
    throw new Error("The active Workspace must be captured before deletion.");
  }

  const successor =
    collection.workspaces[index + 1]
    ?? collection.workspaces[index - 1]
    ?? null;
  if (successor && successor.snapshot === null) {
    throw new Error("The successor Workspace must retain its snapshot.");
  }
  return {
    collection: {
      ...collection,
      activeWorkspaceId: successor?.id ?? null,
      workspaces: collection.workspaces
        .filter(workspace => workspace.id !== workspaceId)
        .map(workspace => workspace.id === successor?.id
          ? { ...workspace, snapshot: null }
          : workspace),
    },
    activatedSnapshot: successor?.snapshot ?? null,
    removedSnapshot: currentSnapshot,
  };
}
