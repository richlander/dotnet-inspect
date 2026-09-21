export interface InstalledLocationAssociation {
  readonly identity: symbol;
  readonly canonicalLocation: string;
  readonly historyState: unknown;
}

interface BrowserSelectedEntry {
  readonly url: string;
  readonly historyState: unknown;
  readonly retainedDefinitionId: string | null;
  readonly incumbent: InstalledLocationAssociation | null;
}

type NonBrowserLocationPolicy = "push" | "replace" | "none";

export type LocationIntentDeclaration =
  | {
    readonly id: symbol;
    readonly source: "browser";
    readonly selectedEntry: BrowserSelectedEntry;
  }
  | {
    readonly id: symbol;
    readonly source: "non-browser";
    readonly policy: NonBrowserLocationPolicy;
  };

type LocationSemanticOutcome =
  | "applied"
  | "unavailable"
  | "rejected"
  | "failed"
  | "aborted"
  | "synchronization";

type LocationSynchronizationDisposition =
  | "current"
  | "synchronization-required";

export interface LocationResult {
  readonly outcome: LocationSemanticOutcome;
  readonly synchronization: LocationSynchronizationDisposition;
  readonly association: InstalledLocationAssociation;
  readonly browserRestoration?: "exact" | "changed";
}

type LocationEffectKind =
  | "push"
  | "replace"
  | "adopt"
  | "realign";

export type LocationEffect =
  | {
    readonly kind: LocationEffectKind;
    readonly intentId: symbol;
    readonly association: InstalledLocationAssociation;
    readonly selectedEntry: BrowserSelectedEntry | null;
  }
  | {
    readonly kind: "none";
    readonly intentId: symbol;
    readonly reason: "stale" | "current-no-write";
  };

interface UnresolvedLocationObligation {
  readonly intentId: symbol;
  readonly association: InstalledLocationAssociation | null;
  readonly selectedEntry: BrowserSelectedEntry | null;
}

export interface NavigationLocationIntentArbiter {
  readonly currentIntentId: symbol | null;
  readonly unresolved: UnresolvedLocationObligation | null;
  selectBrowserEntry(input: {
    readonly url: string;
    readonly historyState: unknown;
    readonly retainedDefinitionId: string | null;
    readonly incumbent: InstalledLocationAssociation | null;
  }): LocationIntentDeclaration;
  admitNonBrowser(
    policy: NonBrowserLocationPolicy,
    installed: InstalledLocationAssociation | null,
    history: BrowserHistoryWriter | null,
  ): LocationIntentDeclaration;
  classify(
    declaration: LocationIntentDeclaration,
    result: LocationResult,
  ): LocationEffect;
  publish(effect: LocationEffect, history: BrowserHistoryWriter): boolean;
  settle(effect: LocationEffect, succeeded: boolean): void;
}

interface BrowserHistoryWriter {
  pushState(data: unknown, unused: string, url?: string | URL | null): void;
  replaceState(data: unknown, unused: string, url?: string | URL | null): void;
}

export function classifyLocationEffect(
  declaration: LocationIntentDeclaration,
  currentIntentId: symbol | null,
  result: LocationResult,
): LocationEffect {
  if (declaration.id !== currentIntentId) {
    return {
      kind: "none",
      intentId: declaration.id,
      reason: "stale",
    };
  }

  if (declaration.source === "browser") {
    if (result.synchronization === "synchronization-required"
      || result.browserRestoration === "changed") {
      return writeEffect("replace", declaration, result.association);
    }
    if (result.outcome === "applied") {
      if (result.browserRestoration !== "exact") {
        throw new Error(
          "An applied Browser restoration requires an exact or changed classification.",
        );
      }
      return writeEffect("adopt", declaration, result.association);
    }
    return writeEffect("realign", declaration, result.association);
  }

  if (result.outcome === "applied") {
    return declaration.policy === "none"
      ? noWrite(declaration.id)
      : writeEffect(declaration.policy, declaration, result.association);
  }
  if (result.synchronization === "synchronization-required") {
    return writeEffect("replace", declaration, result.association);
  }
  return noWrite(declaration.id);
}

export function createNavigationLocationIntentArbiter():
NavigationLocationIntentArbiter {
  let nextIdentity = 0;
  let currentIntentId: symbol | null = null;
  let unresolved: UnresolvedLocationObligation | null = null;
  let publicationInProgress = false;

  function issueId(): symbol {
    return Symbol(`location-intent-${++nextIdentity}`);
  }

  function selectBrowserEntry(input: {
    readonly url: string;
    readonly historyState: unknown;
    readonly retainedDefinitionId: string | null;
    readonly incumbent: InstalledLocationAssociation | null;
  }): LocationIntentDeclaration {
    if (publicationInProgress) {
      throw new Error(
        "A Browser-selected intent cannot be admitted while location publication is in progress.",
      );
    }
    const declaration: LocationIntentDeclaration = {
      id: issueId(),
      source: "browser",
      selectedEntry: {
        url: input.url,
        historyState: structuredClone(input.historyState),
        retainedDefinitionId: input.retainedDefinitionId,
        incumbent: cloneAssociation(input.incumbent),
      },
    };
    currentIntentId = declaration.id;
    unresolved = {
      intentId: declaration.id,
      association: cloneAssociation(input.incumbent),
      selectedEntry: declaration.selectedEntry,
    };
    return declaration;
  }

  function admitNonBrowser(
    policy: NonBrowserLocationPolicy,
    installed: InstalledLocationAssociation | null,
    history: BrowserHistoryWriter | null,
  ): LocationIntentDeclaration {
    if (publicationInProgress) {
      throw new Error(
        "A non-browser intent cannot be admitted while location publication is in progress.",
      );
    }
    if (unresolved !== null) {
      if (installed === null || history === null) {
        throw new Error(
          "A new non-browser intent cannot be admitted until the selected entry is aligned with an installed location.",
        );
      }
      const repair: LocationEffect = {
        kind: "realign",
        intentId: unresolved.intentId,
        association: cloneAssociation(installed)!,
        selectedEntry: unresolved.selectedEntry,
      };
      if (!publish(repair, history)) {
        throw new Error(
          "Location realignment did not settle before admitting the new intent.",
        );
      }
    }

    const declaration: LocationIntentDeclaration = {
      id: issueId(),
      source: "non-browser",
      policy,
    };
    currentIntentId = declaration.id;
    return declaration;
  }

  function classify(
    declaration: LocationIntentDeclaration,
    result: LocationResult,
  ): LocationEffect {
    return classifyLocationEffect(declaration, currentIntentId, result);
  }

  function settle(effect: LocationEffect, succeeded: boolean): void {
    if (effect.intentId !== currentIntentId) return;
    if (effect.kind === "none") {
      if (effect.reason === "current-no-write" && succeeded) {
        currentIntentId = null;
        unresolved = null;
      }
      return;
    }
    if (succeeded) {
      currentIntentId = null;
      unresolved = null;
      return;
    }
    unresolved = {
      intentId: effect.intentId,
      association: cloneAssociation(effect.association),
      selectedEntry: effect.selectedEntry,
    };
  }

  function publish(
    effect: LocationEffect,
    history: BrowserHistoryWriter,
  ): boolean {
    if (effect.intentId !== currentIntentId || publicationInProgress) {
      return false;
    }
    publicationInProgress = true;
    try {
      applyLocationEffect(effect, history);
      settle(effect, true);
    } catch (error) {
      settle(effect, false);
      throw error;
    } finally {
      publicationInProgress = false;
    }
    return true;
  }

  return {
    get currentIntentId(): symbol | null {
      return currentIntentId;
    },
    get unresolved(): UnresolvedLocationObligation | null {
      return unresolved;
    },
    selectBrowserEntry,
    admitNonBrowser,
    classify,
    publish,
    settle,
  };
}

function applyLocationEffect(
  effect: LocationEffect,
  history: BrowserHistoryWriter,
): void {
  if (effect.kind === "none" || effect.kind === "adopt") return;
  if (effect.kind === "push") {
    history.pushState(
      effect.association.historyState,
      "",
      effect.association.canonicalLocation);
    return;
  }
  history.replaceState(
    effect.association.historyState,
    "",
    effect.association.canonicalLocation);
}

function writeEffect(
  kind: LocationEffectKind,
  declaration: LocationIntentDeclaration,
  association: InstalledLocationAssociation,
): LocationEffect {
  return {
    kind,
    intentId: declaration.id,
    association: cloneAssociation(association)!,
    selectedEntry:
      declaration.source === "browser" ? declaration.selectedEntry : null,
  };
}

function noWrite(intentId: symbol): LocationEffect {
  return {
    kind: "none",
    intentId,
    reason: "current-no-write",
  };
}

function cloneAssociation(
  association: InstalledLocationAssociation | null,
): InstalledLocationAssociation | null {
  return association === null
    ? null
    : {
      ...association,
      historyState: structuredClone(association.historyState),
    };
}
