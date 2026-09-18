export const MAX_RETAINED_WORKSPACES = 4;

interface LegacyRetainedWorkspace<TSnapshot> {
  id: string;
  label: string;
  kind: "legacy";
  snapshot: TSnapshot | null;
}

export interface ManagedRetainedWorkspace {
  id: string;
  label: string;
  kind: "managed";
  packageCount: number;
}

export type RetainedWorkspace<TSnapshot> =
  | LegacyRetainedWorkspace<TSnapshot>
  | ManagedRetainedWorkspace;

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
      `Inspect Web retains at most ${MAX_RETAINED_WORKSPACES} Workspace definitions. Delete one before opening another.`);
  }
  if (collection.activeWorkspaceId !== null && currentSnapshot === null) {
    throw new Error("The active Workspace must be retained before publishing another.");
  }
  const active = collection.workspaces.find(
    workspace => workspace.id === collection.activeWorkspaceId);
  if (active?.kind === "managed") {
    throw new Error(
      "Deactivate the managed Workspace before publishing a compatibility Workspace.",
    );
  }

  const id = `workspace-${collection.nextOrdinal}`;
  const workspaces = collection.workspaces.map(workspace =>
    workspace.id === collection.activeWorkspaceId
      ? workspace.kind === "legacy"
        ? { ...workspace, snapshot: currentSnapshot }
        : workspace
      : workspace);
  workspaces.push({
    id,
    label: `Workspace ${collection.nextOrdinal}`,
    kind: "legacy",
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
  if (!target || target.kind !== "legacy" || target.snapshot === null) {
    throw new Error("That Workspace is no longer available.");
  }
  const active = collection.workspaces.find(
    workspace => workspace.id === collection.activeWorkspaceId);
  if (active?.kind === "managed") {
    throw new Error(
      "Deactivate the managed Workspace before restoring a compatibility Workspace.",
    );
  }

  const activatedSnapshot = target.snapshot;
  return {
    collection: {
      ...collection,
      activeWorkspaceId: workspaceId,
      workspaces: collection.workspaces.map(workspace => {
        if (workspace.id === collection.activeWorkspaceId) {
          if (workspace.kind !== "legacy") {
            throw new Error(
              "A managed Workspace cannot retain a compatibility snapshot.",
            );
          }
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
  if (removed.kind !== "legacy") {
    throw new Error(
      "Managed Workspace deletion must use the retained activation controller.",
    );
  }
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
  if (successor?.kind === "managed") {
    throw new Error(
      "Managed Workspace activation must be coordinated before deleting the active compatibility Workspace.",
    );
  }
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
          ? workspace.kind === "legacy"
            ? { ...workspace, snapshot: null }
            : workspace
          : workspace),
    },
    activatedSnapshot: successor?.snapshot ?? null,
    removedSnapshot: currentSnapshot,
  };
}

export function retainManagedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  definition: Omit<ManagedRetainedWorkspace, "kind">,
): RetainedWorkspaceCollection<TSnapshot> {
  if (collection.workspaces.length >= MAX_RETAINED_WORKSPACES) {
    throw new Error(
      `Inspect Web retains at most ${MAX_RETAINED_WORKSPACES} Workspace definitions. Delete one before retaining another.`,
    );
  }
  if (collection.workspaces.some(workspace => workspace.id === definition.id)) {
    throw new Error(
      `Retained Workspace identity '${definition.id}' already exists.`,
    );
  }
  if (!Number.isInteger(definition.packageCount)
    || definition.packageCount < 0) {
    throw new Error("A managed Workspace requires a valid coordinate count.");
  }
  return {
    ...collection,
    workspaces: [
      ...collection.workspaces,
      { ...definition, kind: "managed" },
    ],
  };
}

export function publishLegacyWorkspaceAfterManaged<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
): RetainedWorkspaceCollection<TSnapshot> {
  if (collection.workspaces.length >= MAX_RETAINED_WORKSPACES) {
    throw new Error(
      `Inspect Web retains at most ${MAX_RETAINED_WORKSPACES} Workspace definitions. Delete one before opening another.`,
    );
  }
  const active = collection.workspaces.find(
    workspace => workspace.id === collection.activeWorkspaceId);
  if (active?.kind !== "managed") {
    throw new Error(
      "A managed Workspace must be active before publishing its compatibility successor.",
    );
  }

  const id = `workspace-${collection.nextOrdinal}`;
  return {
    workspaces: [
      ...collection.workspaces,
      {
        id,
        label: `Workspace ${collection.nextOrdinal}`,
        kind: "legacy",
        snapshot: null,
      },
    ],
    activeWorkspaceId: id,
    nextOrdinal: collection.nextOrdinal + 1,
  };
}

export function activateManagedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  workspaceId: string,
  currentLegacySnapshot: TSnapshot | null,
): RetainedWorkspaceCollection<TSnapshot> {
  const target = collection.workspaces.find(
    workspace => workspace.id === workspaceId);
  if (!target || target.kind !== "managed") {
    throw new Error("That managed Workspace is no longer available.");
  }
  if (workspaceId === collection.activeWorkspaceId) return collection;

  return {
    ...collection,
    activeWorkspaceId: workspaceId,
    workspaces: collection.workspaces.map(workspace => {
      if (workspace.id !== collection.activeWorkspaceId) return workspace;
      if (workspace.kind === "managed") return workspace;
      if (currentLegacySnapshot === null) {
        throw new Error(
          "The active compatibility Workspace must be retained before managed activation.",
        );
      }
      return { ...workspace, snapshot: currentLegacySnapshot };
    }),
  };
}

export function activateLegacyWorkspaceAfterManaged<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  workspaceId: string,
): RetainedWorkspaceTransition<TSnapshot> {
  const target = collection.workspaces.find(
    workspace => workspace.id === workspaceId);
  if (!target || target.kind !== "legacy" || target.snapshot === null) {
    throw new Error("That Workspace is no longer available.");
  }
  const active = collection.workspaces.find(
    workspace => workspace.id === collection.activeWorkspaceId);
  if (active?.kind !== "managed") {
    throw new Error(
      "A managed Workspace must be active before this compatibility transition.",
    );
  }
  return {
    collection: {
      ...collection,
      activeWorkspaceId: workspaceId,
      workspaces: collection.workspaces.map(workspace =>
        workspace.id === workspaceId
          ? { ...workspace, snapshot: null }
          : workspace),
    },
    activatedSnapshot: target.snapshot,
    removedSnapshot: null,
  };
}

export function removeRetainedWorkspace<TSnapshot>(
  collection: RetainedWorkspaceCollection<TSnapshot>,
  workspaceId: string,
): {
  collection: RetainedWorkspaceCollection<TSnapshot>;
  removed: RetainedWorkspace<TSnapshot>;
} {
  const removed = collection.workspaces.find(
    workspace => workspace.id === workspaceId);
  if (!removed) throw new Error("That Workspace is no longer available.");
  if (collection.activeWorkspaceId === workspaceId) {
    throw new Error("Deactivate the Workspace before removing it.");
  }
  return {
    collection: {
      ...collection,
      workspaces: collection.workspaces.filter(
        workspace => workspace.id !== workspaceId),
    },
    removed,
  };
}
