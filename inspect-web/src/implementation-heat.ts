import type {
  BrowserImplementationHeatFamily,
  BrowserTypeImplementationHeat,
} from "./facades/inspect-web-analysis.d.ts";
import {
  createKeyedResultCache,
  type KeyedResultCache,
} from "./implementation-profiles.ts";
import type {
  OperationAuthorityPage,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

// Member-list heat for one Type (docs/design/inspect-web-implementation-profiles.md):
// one request per selected Type after its member list paints, projected into
// family-relative heat and hub state for each eligible overload family.

export interface PackageTypeHeatRequest {
  readonly kind: "package";
  readonly workspaceGeneration: string;
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly assemblyName: string;
  readonly typeDefinitionId: string;
}

interface PlatformTypeHeatRequest {
  readonly kind: "platform";
  readonly workspaceGeneration: string;
  readonly targetFramework: string;
  readonly platformVersion: string;
  readonly pack: string;
  readonly assemblyFileName: string;
  readonly typeDefinitionId: string;
}

export type TypeHeatRequest = PackageTypeHeatRequest | PlatformTypeHeatRequest;

export interface ImplementationHeatFamilyCandidate {
  readonly name: string;
  readonly kind: string;
  readonly overloads: ReadonlyArray<{
    readonly accessibility: string;
    readonly declaringTypeDefinitionId?: string | null;
  }>;
}

export function implementationHeatVisibleFamilyIsEligible(
  groups: ReadonlyArray<ImplementationHeatFamilyCandidate>,
  group: ImplementationHeatFamilyCandidate,
): boolean {
  const declaringType = group.overloads[0]?.declaringTypeDefinitionId;
  const supportedKind = group.kind === "method"
    || (group.kind === "extension-method"
      && Boolean(declaringType)
      && group.overloads.every(
        overload => overload.declaringTypeDefinitionId === declaringType));
  return supportedKind
    && group.overloads.length > 1
    && groups.every(candidate =>
      candidate.name !== group.name || candidate.kind === group.kind);
}

export function implementationHeatFamilyIsEligible(
  typeAccessibility: string,
  groups: ReadonlyArray<ImplementationHeatFamilyCandidate>,
  group: ImplementationHeatFamilyCandidate,
): boolean {
  return implementationHeatVisibleFamilyIsEligible(groups, group)
    && typeAccessibility === "public"
    && group.overloads.every(overload => overload.accessibility === "public");
}

export function typeHeatCacheKey(request: TypeHeatRequest): string {
  return request.kind === "package"
    ? JSON.stringify([
        "type-implementation-heat",
        request.workspaceGeneration,
        "package",
        request.packageId,
        request.version,
        request.targetFramework,
        request.assemblyName,
        request.typeDefinitionId,
      ])
    : JSON.stringify([
        "type-implementation-heat",
        request.workspaceGeneration,
        "platform",
        request.targetFramework,
        request.platformVersion,
        request.pack,
        request.assemblyFileName,
        request.typeDefinitionId,
      ]);
}

function validateTypeHeat(
  heat: BrowserTypeImplementationHeat,
  request: TypeHeatRequest,
): void {
  if (heat.schemaVersion !== 1)
    throw new Error("Unsupported type implementation-heat schema version.");
  switch (heat.outcome) {
    case "available": {
      const content = heat.content;
      if (heat.subject === null
        || content === null
        || heat.failure !== null
        || heat.share === null) {
        throw new Error("Available type implementation heat is incomplete.");
      }
      if (content.typeDefinitionId !== request.typeDefinitionId)
        throw new Error("Type implementation heat names another Type.");
      const members = new Set<string>();
      for (const family of content.families) {
        if (members.has(family.member))
          throw new Error(`Duplicate implementation-heat family '${family.member}'.`);
        members.add(family.member);
        const tokens = new Set(family.methods.map(method => method.metadataToken));
        for (const member of family.roster) {
          if (member.typeDefinitionId !== request.typeDefinitionId
            || !tokens.has(member.metadataToken)) {
            throw new Error(
              `Implementation-heat roster member '${member.stableSelector}' is inconsistent.`,
            );
          }
        }
        for (const relationship of family.relationships) {
          if (!tokens.has(relationship.callerToken)
            || !tokens.has(relationship.calleeToken)) {
            throw new Error(
              "Implementation-heat relationship leaves its analyzed family.",
            );
          }
        }
      }
      return;
    }
    case "rejected":
    case "failed":
      if (heat.content !== null || heat.failure === null)
        throw new Error(`Type implementation-heat ${heat.outcome} result is incomplete.`);
      return;
    case "unavailable":
      if (heat.subject !== null || heat.content !== null || heat.failure === null)
        throw new Error("Unavailable type implementation heat is inconsistent.");
      return;
    default:
      throw new Error(`Unknown type implementation-heat outcome: ${heat.outcome}.`);
  }
}

export interface VisibleOverloadHeatIdentity {
  readonly stableSelector: string;
  readonly metadataToken: number;
}

/** Member-list evidence for one visible overload. */
interface OverloadHeat {
  readonly stableSelector: string;
  readonly metadataToken: number;
  readonly size: number | null;
  /** Tint strength in (0, 1]; null when the row is untinted. */
  readonly heatStrength: number | null;
  readonly hub: boolean;
  readonly incomingCallers: number;
  readonly description: string;
}

/**
 * `unknown-maximum`: an analyzed body is unavailable or incomplete.
 * `suppressed`: comparison would add noise.
 */
type FamilyHeatStatus = "shown" | "suppressed" | "unknown-maximum";

interface AnalyzedMethodHeat {
  readonly metadataToken: number;
  readonly isRosterMember: boolean;
  readonly size: number | null;
  readonly heatStrength: number | null;
  readonly hub: boolean;
  readonly incomingCallers: number;
}

export interface FamilyHeat {
  readonly status: FamilyHeatStatus;
  readonly maximum: number | null;
  readonly maximumIsUnlisted: boolean;
  readonly overloads: ReadonlyArray<OverloadHeat>;
  readonly roster: ReadonlyArray<VisibleOverloadHeatIdentity>;
  readonly methods: ReadonlyMap<number, AnalyzedMethodHeat>;
}

/** Overloads at or above this share of the family maximum are tinted. */
const heatThreshold = 0.5;

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

/**
 * Projects one family record into roster-ordered heat and hub state. Sizes and
 * trivial flags are query-issued and never recomputed here.
 */
export function projectFamilyHeat(
  family: BrowserImplementationHeatFamily,
): FamilyHeat {
  const measured = family.methods.filter(method => method.size !== null);
  const unknownMaximum = family.unavailableBodies.length > 0
    || family.methods.some(method => method.hasBody && !method.isComplete);
  let maximum: number | null = null;
  for (const method of measured) {
    const size = method.size ?? 0;
    if (maximum === null || size > maximum) {
      maximum = size;
    }
  }
  const status: FamilyHeatStatus = unknownMaximum
    ? "unknown-maximum"
    : measured.length < 2
      || maximum === null
      || maximum === 0
      || measured.every(method => method.isTrivial)
      ? "suppressed"
      : "shown";

  const methods = new Map(family.methods.map(method => {
    const token = method.metadataToken;
    const size = method?.size ?? null;
    const callers = new Set(family.relationships
      .filter(relationship =>
        relationship.calleeToken === token
        && relationship.callerToken !== token)
      .map(relationship => relationship.callerToken));
    const callsSibling = family.relationships.some(relationship =>
      relationship.callerToken === token
      && relationship.calleeToken !== token);
    // A hub is where calls converge and code lives: a bodyless (abstract or
    // extern) overload is never a hub.
    const hub = method !== undefined
      && method.hasBody
      && method.isComplete
      && callers.size > 0
      && !callsSibling;
    const heatStrength = status === "shown"
      && size !== null
      && maximum !== null
      && size >= maximum * heatThreshold
      ? Math.sqrt(size / maximum)
      : null;
    return [token, {
      metadataToken: token,
      isRosterMember: method.isRosterMember,
      size,
      heatStrength,
      hub,
      incomingCallers: callers.size,
    }] as const;
  }));
  const roster = family.roster.map(member => ({
    stableSelector: member.stableSelector,
    metadataToken: member.metadataToken,
  }));
  return projectVisibleFamilyHeat(
    { status, maximum, roster, methods },
    roster);
}

function projectVisibleFamilyHeat(
  family: Pick<FamilyHeat, "status" | "maximum" | "roster" | "methods">,
  visible: ReadonlyArray<VisibleOverloadHeatIdentity>,
): FamilyHeat {
  const maximumIsUnlisted = family.maximum !== null
    && !visible.some(overload =>
      family.methods.get(overload.metadataToken)?.size === family.maximum);
  const overloads = visible.map(overload => {
    const method = family.methods.get(overload.metadataToken);
    const size = method?.size ?? null;
    const parts = [size === null
      ? "No measured body"
      : plural(size, "instruction")];
    if (family.status === "shown"
      && size !== null
      && family.maximum !== null) {
      parts.push(`${Math.round((size / family.maximum) * 100)}% of the largest body in this family${
        maximumIsUnlisted ? ", which is not a listed overload" : ""}`);
    }
    if (method?.hub) {
      parts.push(`hub called by ${plural(
        method.incomingCallers,
        "same-name method")}`);
    }
    return {
      stableSelector: overload.stableSelector,
      metadataToken: overload.metadataToken,
      size,
      heatStrength: method?.heatStrength ?? null,
      hub: method?.hub ?? false,
      incomingCallers: method?.incomingCallers ?? 0,
      description: parts.join("; "),
    };
  });
  return {
    ...family,
    maximumIsUnlisted,
    overloads,
  };
}

export type TypeHeatState =
  | { readonly status: "idle" }
  | {
      readonly status: "loading";
      readonly request: TypeHeatRequest;
      readonly isCurrent: () => boolean;
    }
  | {
      readonly status: "ready";
      readonly request: TypeHeatRequest;
      readonly isCurrent: () => boolean;
      readonly families: ReadonlyMap<string, FamilyHeat>;
    }
  | {
      readonly status: "failed";
      readonly request: TypeHeatRequest;
      readonly isCurrent: () => boolean;
      readonly outcome: "rejected" | "failed" | "unavailable" | "producer-failed";
      readonly message: string;
      /** Owner-issued diagnostics as `code: summary`, for settled Content. */
      readonly diagnostics?: ReadonlyArray<string>;
    };

export interface TypeHeatStateHost {
  typeHeat: TypeHeatState;
}

export type TypeHeatCue =
  | { readonly text: "measuring"; readonly tone: "progress" }
  | {
      readonly text: "heat incomplete" | "heat unavailable";
      readonly tone: "problem";
    };

/**
 * Looks up one member-list family's heat. Public rows must match the request
 * roster exactly. Non-public rows join only exact analyzed MethodDef tokens.
 */
export function familyHeatFor(
  state: TypeHeatState,
  member: string,
  accessibility: string,
  visible: ReadonlyArray<VisibleOverloadHeatIdentity>,
): FamilyHeat | null {
  if (state.status !== "ready") return null;
  const family = state.families.get(member);
  if (family === undefined || visible.length < 2) return null;
  const tokens = new Set(visible.map(overload => overload.metadataToken));
  if (tokens.size !== visible.length) return null;
  if (accessibility === "public") {
    if (family.roster.length !== visible.length
      || family.roster.some(rosterMember => !visible.some(overload =>
        overload.metadataToken === rosterMember.metadataToken
        && overload.stableSelector === rosterMember.stableSelector))) {
      return null;
    }
  } else if (visible.some(overload => {
    const method = family.methods.get(overload.metadataToken);
    return method === undefined || method.isRosterMember;
  })) {
    return null;
  }
  return projectVisibleFamilyHeat(family, visible);
}

export function familyHeatCue(
  state: TypeHeatState,
  member: string,
  accessibility: string,
  visible: ReadonlyArray<VisibleOverloadHeatIdentity>,
): TypeHeatCue | null {
  switch (state.status) {
    case "idle":
      return null;
    case "loading":
      return { text: "measuring", tone: "progress" };
    case "failed":
      return { text: "heat unavailable", tone: "problem" };
    case "ready":
      return familyHeatFor(state, member, accessibility, visible)?.status
        === "unknown-maximum"
        ? { text: "heat incomplete", tone: "problem" }
        : null;
  }
  throw new Error("Unknown type implementation-heat state.");
}

function project(
  heat: BrowserTypeImplementationHeat,
  request: TypeHeatRequest,
  isCurrent: () => boolean,
): TypeHeatState {
  if (heat.outcome !== "available" || heat.content === null) {
    return {
      status: "failed",
      request,
      isCurrent,
      outcome: heat.outcome === "rejected"
        || heat.outcome === "failed"
        || heat.outcome === "unavailable"
        ? heat.outcome
        : "failed",
      message: heat.failure?.detail ?? "Type implementation heat is unavailable.",
      diagnostics: heat.diagnostics.map(diagnostic =>
        `${diagnostic.code}: ${diagnostic.summary}`),
    };
  }
  return {
    status: "ready",
    request,
    isCurrent,
    families: new Map(heat.content.families.map(family =>
      [family.member, projectFamilyHeat(family)])),
  };
}

export interface TypeHeatCoordinatorDependencies {
  readonly state: TypeHeatStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  readonly cache?: KeyedResultCache<TypeHeatRequest, BrowserTypeImplementationHeat>;
  query(request: TypeHeatRequest): Promise<BrowserTypeImplementationHeat>;
  /** Resolves once no ordinary-Worker request is outstanding. */
  whenWorkerIdle(): Promise<void>;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface TypeHeatCoordinator {
  /** Requests heat for the Type the member list shows; idempotent per Type. */
  request(request: TypeHeatRequest, isCurrent: () => boolean): void;
  /** Replaces a producer failure and requests again. */
  retry(request: TypeHeatRequest, isCurrent: () => boolean): void;
}

interface TypeHeatInput {
  readonly request: TypeHeatRequest;
  readonly isCurrent: () => boolean;
}

export function createTypeHeatCoordinator(
  dependencies: TypeHeatCoordinatorDependencies,
): TypeHeatCoordinator {
  type FeatureEvent = OperationFeatureEvent<TypeHeatState, unknown, never>;
  type Session = OperationSession<
    TypeHeatInput,
    TypeHeatState,
    unknown,
    never,
    never
  >;

  const cache = dependencies.cache
    ?? createKeyedResultCache(typeHeatCacheKey, validateTypeHeat);
  const inputs = new Map<OperationId, TypeHeatInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Type implementation-heat operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const input = inputFor(event.operation.id);
        if (!input.isCurrent()) break;
        dependencies.state.typeHeat = {
          status: "loading",
          request: input.request,
          isCurrent: input.isCurrent,
        };
        dependencies.render();
        break;
      }
      case "terminal": {
        const input = inputFor(event.operationId);
        if (!input.isCurrent()) {
          releaseLoading(input);
          break;
        }
        dependencies.state.typeHeat = event.outcome.kind === "succeeded"
          ? event.outcome.value
          : {
              status: "failed",
              request: input.request,
              isCurrent: input.isCurrent,
              outcome: "producer-failed",
              message: dependencies.describeError(event.outcome.error),
            };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
      case "progress":
        break;
    }
    return undefined;
  };
  // A request that ends or is dropped without publishing releases the loading
  // state it published, so returning to its Type re-requests (a cache hit)
  // instead of showing `measuring` with nothing queued or running.
  const releaseLoading = (input: TypeHeatInput) => {
    const published = dependencies.state.typeHeat;
    if (published.status === "loading" && published.request === input.request)
      dependencies.state.typeHeat = { status: "idle" };
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    TypeHeatInput,
    TypeHeatState,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      let quiesced = false;
      const quiesce = (): undefined => {
        if (quiesced) return undefined;
        quiesced = true;
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const finish = (heat: BrowserTypeImplementationHeat): undefined => {
        if (!input.isCurrent()) {
          releaseLoading(input);
          sink.reportTerminal({ kind: "canceled", reason: "superseded" });
          return quiesce();
        }
        try {
          sink.reportTerminal({
            kind: "succeeded",
            value: project(heat, input.request, input.isCurrent),
          });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      const fail = (error: unknown): undefined => {
        if (!input.isCurrent()) {
          releaseLoading(input);
          sink.reportTerminal({ kind: "canceled", reason: "superseded" });
          return quiesce();
        }
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: () => undefined,
          activate: () => {
            let result: Promise<BrowserTypeImplementationHeat>;
            try {
              result = cache.load(
                input.request,
                request => dependencies.query(request),
              );
            } catch (error: unknown) {
              return fail(error);
            }
            void result.then(finish, fail);
            return undefined;
          },
          abandon: () => {
            inputs.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const start = (input: TypeHeatInput): Promise<void> => {
    const result = session.start(input, adapter);
    if (result.kind === "rejected") {
      if (input.isCurrent()) {
        dependencies.state.typeHeat = {
          status: "failed",
          request: input.request,
          isCurrent: input.isCurrent,
          outcome: "producer-failed",
          message: `Type implementation heat could not start: ${result.reason.kind}.`,
        };
        dependencies.render();
      }
      return Promise.resolve();
    }
    return result.handle.quiesced;
  };

  // One heat run at a time, sent only once the ordinary Worker is idle so an
  // interactive request that is already waiting goes first. Only the latest
  // requested Type waits; a queued Type the reader has left is dropped.
  let running = false;
  let queued: TypeHeatInput | null = null;
  const pump = async (): Promise<void> => {
    if (running) return;
    const next = queued;
    if (next === null) return;
    queued = null;
    if (!next.isCurrent()) {
      releaseLoading(next);
      return pump();
    }
    running = true;
    try {
      try {
        await dependencies.whenWorkerIdle();
      } catch {
        // Idle tracking failure must not strand the request.
      }
      if (next.isCurrent()) await start(next);
      else releaseLoading(next);
    } finally {
      running = false;
    }
    return pump();
  };

  const requestHeat = (
    heatRequest: TypeHeatRequest,
    isCurrent: () => boolean,
  ) => {
    const input = { request: heatRequest, isCurrent };
    if (cache.status(heatRequest) !== "missing") {
      void start(input);
      return;
    }
    if (queued !== null) releaseLoading(queued);
    queued = input;
    if (isCurrent()) {
      dependencies.state.typeHeat = {
        status: "loading",
        request: heatRequest,
        isCurrent,
      };
      dependencies.render();
    }
    void pump();
  };

  return {
    request: requestHeat,
    retry(retried, isCurrent) {
      cache.retry(retried);
      requestHeat(retried, isCurrent);
    },
  };
}
