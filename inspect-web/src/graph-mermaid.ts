import {
  dependencyGraphExternalKey,
  dependencyGraphGroupSelectionIndex,
  dependencyGraphPackageKey,
  ensureBoundedGraphNode,
  mermaidLabel,
  packageIdentityKey,
  selectedDependencyGroup,
  type DependencyGroup,
  type DependencyGroupData,
  type DependencyGraphNodeInfo,
  type DependencyGraphResult,
  type PackageIdentity,
} from "./data.ts";
import type {
  BrowserAnnotatedSourceCallRelationship,
  BrowserCallGraphTarget,
} from "./facades/inspect-web-source.d.ts";

export function resolveMermaidCssVariables(
  definition: string,
  readProperty: (name: string) => string,
): string {
  return definition.replace(
    /var\((--[\w-]+)\)/g,
    (whole: string, name: string) => readProperty(name).trim() || whole);
}

export interface AnnotatedRelationshipGraphEdge {
  edgeRow: number;
  factIds: readonly number[];
  destinations: readonly AnnotatedRelationshipGraphDestination[];
  kinds: readonly BrowserAnnotatedSourceCallRelationship["kind"][];
  inLoop: boolean;
}

export interface AnnotatedRelationshipGraphDestination {
  factIds: readonly number[];
  relationshipIndex: number;
  target: BrowserCallGraphTarget;
}

export interface AnnotatedRelationshipGraph {
  definition: string;
  edges: readonly AnnotatedRelationshipGraphEdge[];
}

export function annotatedRelationshipTargetLabel(
  target: BrowserCallGraphTarget,
): string {
  const member = target.genericArity === 0
    ? target.memberName
    : `${target.memberName}\`${target.genericArity}`;
  return `${target.typeFullName}.${member}(${target.parameterTypes.join(", ")})`;
}

export function annotatedRelationshipKindLabel(
  value: BrowserAnnotatedSourceCallRelationship["kind"],
): string {
  switch (value) {
    case "Call":
      return "Call";
    case "CallVirtual":
      return "Virtual call";
    case "NewObject":
      return "Object creation";
    case "LoadFunction":
      return "Function pointer";
    case "LoadVirtualFunction":
      return "Virtual function pointer";
    case "CallIndirect":
      return "Indirect call";
    default:
      return String(value);
  }
}

export function groupAnnotatedRelationships(
  relationships: readonly BrowserAnnotatedSourceCallRelationship[],
): readonly AnnotatedRelationshipGraphEdge[] {
  const byEdgeRow = new Map<number, {
    destinations: Map<string, {
      relationshipIndex: number;
      target: BrowserCallGraphTarget;
      factIds: number[];
    }>;
    factIds: number[];
    kinds: BrowserAnnotatedSourceCallRelationship["kind"][];
    inLoop: boolean;
  }>();
  relationships.forEach((relationship, relationshipIndex) => {
    const existing = byEdgeRow.get(relationship.edgeRow);
    if (existing) {
      existing.factIds.push(relationship.factId);
      addAnnotatedRelationshipDestination(
        existing.destinations,
        relationship,
        relationshipIndex);
      if (!existing.kinds.includes(relationship.kind)) {
        existing.kinds.push(relationship.kind);
      }
      existing.inLoop ||= relationship.inLoop;
      return;
    }
    const destinations = new Map<string, {
      relationshipIndex: number;
      target: BrowserCallGraphTarget;
      factIds: number[];
    }>();
    addAnnotatedRelationshipDestination(
      destinations,
      relationship,
      relationshipIndex);
    byEdgeRow.set(relationship.edgeRow, {
      destinations,
      factIds: [relationship.factId],
      kinds: [relationship.kind],
      inLoop: relationship.inLoop,
    });
  });
  return [...byEdgeRow.entries()].map(([edgeRow, group]) => ({
    edgeRow,
    factIds: group.factIds,
    destinations: [...group.destinations.values()],
    kinds: group.kinds,
    inLoop: group.inLoop,
  }));
}

function addAnnotatedRelationshipDestination(
  destinations: Map<string, {
    relationshipIndex: number;
    target: BrowserCallGraphTarget;
    factIds: number[];
  }>,
  relationship: BrowserAnnotatedSourceCallRelationship,
  relationshipIndex: number,
): void {
  const key = annotatedRelationshipTargetKey(relationship.target);
  const existing = destinations.get(key);
  if (existing) {
    existing.factIds.push(relationship.factId);
    return;
  }
  destinations.set(key, {
    relationshipIndex,
    target: relationship.target,
    factIds: [relationship.factId],
  });
}

function annotatedRelationshipTargetKey(
  target: BrowserCallGraphTarget,
): string {
  return JSON.stringify([
    target.id,
    target.assembly,
    target.assemblyVersion,
    target.assemblyCulture,
    target.assemblyPublicKeyToken,
    target.typeFullName,
    target.typeMetadataId,
    target.typeDefinitionId,
    target.memberName,
    target.parameterTypes,
    target.returnType,
    target.genericArity,
    target.metadataToken,
    target.selectorKey,
    target.kind,
    target.platformPack,
    target.surfaceAssemblyId,
  ]);
}

export function buildAnnotatedRelationshipGraphMermaid(
  relationships: readonly BrowserAnnotatedSourceCallRelationship[],
): AnnotatedRelationshipGraph | null {
  const edges = groupAnnotatedRelationships(relationships);
  if (edges.length === 0) return null;

  const lines = [
    "flowchart TD",
    '  ar0["Current body"]:::self',
  ];
  edges.forEach((edge, index) => {
    const nodeId = `ar${index + 1}`;
    const target = edge.destinations[0]!.target;
    const targetLabel = mermaidLabel(
      annotatedRelationshipTargetLabel(target));
    const kindLabel = edge.kinds
      .map(annotatedRelationshipKindLabel)
      .join(" / ");
    const occurrenceLabel = edge.factIds.length === 1
      ? kindLabel
      : `${kindLabel} \u00d7${edge.factIds.length}`;
    const edgeLabel = mermaidLabel(
      edge.inLoop ? `${occurrenceLabel} \u00b7 loop` : occurrenceLabel);
    lines.push(`  ${nodeId}["${targetLabel}"]:::target`);
    lines.push(`  ar0 -->|"${edgeLabel}"| ${nodeId}`);
  });
  lines.push(
    "classDef self fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),color:var(--graph-target-text),stroke-width:2px;",
    "classDef target fill:var(--panel),stroke:var(--line-strong),color:var(--text);",
  );
  return {
    definition: lines.join("\n"),
    edges,
  };
}

export interface CallGraphMermaidTarget {
  id: string;
  assembly: string;
  assemblyVersion?: string | null;
  assemblyCulture?: string | null;
  assemblyPublicKeyToken?: string | null;
  typeDefinitionId?: string | null;
  typeMetadataId?: string | null;
  kind: string;
  surfaceAssemblyId?: string | null;
}

function callGraphTypeId(target: CallGraphMermaidTarget): string {
  return target.typeDefinitionId || target.typeMetadataId || "";
}

function callGraphTargetsShareAssembly(
  left: CallGraphMermaidTarget,
  right: CallGraphMermaidTarget,
): boolean {
  if (left.surfaceAssemblyId && right.surfaceAssemblyId) {
    return left.surfaceAssemblyId.toLowerCase()
      === right.surfaceAssemblyId.toLowerCase();
  }
  const culture = (value: string | null | undefined) =>
    value?.toLowerCase() === "neutral" ? "" : value?.toLowerCase() || "";
  return left.assembly.toLowerCase() === right.assembly.toLowerCase()
    && (left.assemblyVersion || "") === (right.assemblyVersion || "")
    && culture(left.assemblyCulture) === culture(right.assemblyCulture)
    && (left.assemblyPublicKeyToken || "").toLowerCase()
      === (right.assemblyPublicKeyToken || "").toLowerCase();
}

export function styleCallGraphMermaid(
  definition: string,
  targets: readonly CallGraphMermaidTarget[],
): string {
  const focus =
    targets.find(target => target.kind.toLowerCase() === "focus")
    ?? targets.find(target => target.id === "n0");
  if (!focus) return definition;

  const roles = {
    target: [] as string[],
    baselineConnector: [] as string[],
    supplyChainBoundary: [] as string[],
    unclassifiedBoundary: [] as string[],
    sameType: [] as string[],
    differentType: [] as string[],
    differentAssembly: [] as string[],
  };
  const focusTypeId = callGraphTypeId(focus);
  for (const target of targets) {
    const sharesAssembly = callGraphTargetsShareAssembly(target, focus);
    const kind = target.kind.toLowerCase();
    if (target.id === focus.id
      || kind === "focus") {
      roles.target.push(target.id);
    } else if (kind === "connector") {
      roles.baselineConnector.push(target.id);
    } else if (kind === "boundary") {
      roles.supplyChainBoundary.push(target.id);
    } else if (kind === "unclassified-boundary") {
      roles.unclassifiedBoundary.push(target.id);
    } else if (sharesAssembly
      && focusTypeId
      && callGraphTypeId(target) === focusTypeId) {
      roles.sameType.push(target.id);
    } else if (sharesAssembly) {
      roles.differentType.push(target.id);
    } else {
      roles.differentAssembly.push(target.id);
    }
  }

  const lines = [
    definition,
    "classDef target fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),color:var(--graph-target-text),stroke-width:2px;",
    "classDef baselineConnector fill:var(--graph-different-type-fill),stroke:var(--graph-different-type-stroke),color:var(--graph-different-type-text);",
    "classDef supplyChainBoundary fill:var(--graph-same-type-fill),stroke:var(--graph-same-type-stroke),color:var(--graph-same-type-text);",
    "classDef unclassifiedBoundary fill:var(--graph-different-assembly-fill),stroke:var(--graph-different-assembly-stroke),color:var(--graph-different-assembly-text);",
    "classDef sameType fill:var(--graph-same-type-fill),stroke:var(--graph-same-type-stroke),color:var(--graph-same-type-text);",
    "classDef differentType fill:var(--graph-different-type-fill),stroke:var(--graph-different-type-stroke),color:var(--graph-different-type-text);",
    "classDef differentAssembly fill:var(--graph-different-assembly-fill),stroke:var(--graph-different-assembly-stroke),color:var(--graph-different-assembly-text);",
  ];
  for (const [role, ids] of Object.entries(roles)) {
    if (ids.length > 0) lines.push(`class ${ids.join(",")} ${role};`);
  }
  return lines.join("\n");
}

function shortTypeName(fullName: string): string {
  const generic = fullName.indexOf("<");
  const head = generic < 0 ? fullName : fullName.slice(0, generic);
  const tail = generic < 0 ? "" : fullName.slice(generic);
  const dot = head.lastIndexOf(".");
  return (dot < 0 ? head : head.slice(dot + 1)) + tail;
}

export interface TypeGraphNode {
  id: string;
  displayName: string;
  role: string;
}

export interface TypeGraphEdge {
  fromId: string;
  toId: string;
}

export interface TypeGraphMeta {
  graphNodes?: readonly TypeGraphNode[];
  graphEdges?: readonly TypeGraphEdge[];
}

export function buildTypeGraphMermaid(meta: TypeGraphMeta): string | null {
  const nodes = meta.graphNodes || [];
  const edges = meta.graphEdges || [];
  if (nodes.length < 2) return null;
  const idOf = new Map<string, string>();
  nodes.forEach((node, index) => idOf.set(node.id, `t${index}`));
  const lines = ["flowchart TD"];
  for (const node of nodes) {
    const label = mermaidLabel(shortTypeName(node.displayName));
    lines.push(`  ${idOf.get(node.id)}["${label}"]:::${node.role}`);
  }
  for (const edge of edges) {
    const from = idOf.get(edge.fromId);
    const to = idOf.get(edge.toId);
    if (from && to) lines.push(`  ${from} --> ${to}`);
  }
  lines.push("classDef self fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),color:var(--graph-target-text),stroke-width:2px;");
  lines.push("classDef base fill:var(--panel-active),stroke:var(--line-strong),color:var(--text);");
  lines.push("classDef interface fill:transparent,stroke:var(--line-strong),color:var(--dim);");
  lines.push("classDef derived fill:var(--panel),stroke:var(--line),color:var(--text);");
  return lines.join("\n");
}

export interface DependencyGraphPackage extends PackageIdentity {
  isRuntimePack?: boolean;
}

export interface DependencyGraphWorkspaceDependency {
  id: string;
  versionRange?: string;
}

export interface DependencyGraphModel {
  package: DependencyGraphPackage;
  packages: readonly DependencyGraphPackage[];
  packageDependencies?: DependencyGroupData;
  dependenciesGroupIndex: number | null;
  workspaceDependencies: Record<string, DependencyGroupData | undefined>;
}

type UniqueCompatiblePackage = (
  packages: readonly DependencyGraphPackage[],
  packageId: string,
  versionRange: string | undefined,
) => DependencyGraphPackage | null | Promise<DependencyGraphPackage | null>;

interface MermaidGraphNodeInfo extends DependencyGraphNodeInfo {
  key: string;
  id: string;
  label: string;
}

export type DependencyGraphPresentationRole =
  | "inspected"
  | "samePrefix"
  | "external";

type ClassifyDependencyGraphPresentationRoles = (
  inspectedPackageId: string,
  packageIds: readonly string[],
) => readonly string[] | Promise<readonly string[]>;

export async function buildDependencyGraphMermaid(
  model: DependencyGraphModel,
  uniqueCompatiblePackage: UniqueCompatiblePackage,
  classifyPresentationRoles: ClassifyDependencyGraphPresentationRoles,
): Promise<DependencyGraphResult | null> {
  const MAX_DEPTH = 3;
  const MAX_NODES = 80;
  const nodeInfo = new Map<string, MermaidGraphNodeInfo>();
  let truncated = false;
  const ensureNode = (key: string, create: () => MermaidGraphNodeInfo): MermaidGraphNodeInfo | null => {
    const result = ensureBoundedGraphNode(
      nodeInfo,
      key,
      create,
      MAX_NODES);
    truncated ||= result.truncated;
    return result.node;
  };
  const openPackageNode = (pkg: DependencyGraphPackage, kind = "open"): MermaidGraphNodeInfo | null => {
    const packageKey = packageIdentityKey(pkg);
    const key = dependencyGraphPackageKey(pkg);
    return ensureNode(key, () => {
      const sameIdCount = model.packages.filter(candidate =>
        candidate.id.toLowerCase() === pkg.id.toLowerCase()).length;
      return {
        key,
        id: pkg.id,
        kind,
        packageKey,
        versionRange: "",
        label: sameIdCount > 1
          ? `${pkg.id}@${pkg.version} · ${pkg.activeFramework}`
          : pkg.id
      };
    });
  };
  const dependencyNode = async (dependency: DependencyGraphWorkspaceDependency): Promise<MermaidGraphNodeInfo | null> => {
    const open = await uniqueCompatiblePackage(
      model.packages,
      dependency.id,
      dependency.versionRange);
    if (open) return openPackageNode(open);

    const versionRange = dependency.versionRange || "";
    const key = dependencyGraphExternalKey(dependency.id, versionRange);
    return ensureNode(key, () => {
      return {
        key,
        id: dependency.id,
        kind: "external",
        packageKey: "",
        versionRange,
        label: versionRange
          ? `${dependency.id} ${versionRange}`
          : dependency.id
      };
    });
  };
  openPackageNode(model.package, "self");

  const edgeSet = new Set<string>();
  const edges: { from: string; to: string }[] = [];
  const addEdge = (from: MermaidGraphNodeInfo, to: MermaidGraphNodeInfo) => {
    const key = `${from.key}\u0001${to.key}`;
    if (edgeSet.has(key)) return;
    edgeSet.add(key);
    edges.push({ from: from.key, to: to.key });
  };

  const workspaceDependencyKey = (pkg: DependencyGraphPackage) => [
    pkg.id.toLowerCase(),
    pkg.version.toLowerCase(),
    pkg.activeFramework.toLowerCase()
  ].join("@");
  const groupFor = (pkg: DependencyGraphPackage): DependencyGroup | null => {
    if (packageIdentityKey(pkg) === packageIdentityKey(model.package)) {
      const groups = model.packageDependencies?.dependencyGroups || [];
      const fallbackGroupIndex = groups.some(
        group => group.index === model.dependenciesGroupIndex)
        ? model.dependenciesGroupIndex
        : groups.find(group => group.isActive)?.index ?? groups[0]?.index ?? null;
      const selectedGroupIndex = dependencyGraphGroupSelectionIndex(
        model.packageDependencies,
        model.dependenciesGroupIndex,
        fallbackGroupIndex);
      return selectedDependencyGroup(
        model.packageDependencies,
        selectedGroupIndex);
    }

    const data = model.workspaceDependencies[workspaceDependencyKey(pkg)];
    return selectedDependencyGroup(data);
  };

  let downFrontier = [model.package];
  const downVisited = new Set([packageIdentityKey(model.package)]);
  for (let depth = 0; depth < MAX_DEPTH && downFrontier.length; depth++) {
    if (truncated) break;
    const next: DependencyGraphPackage[] = [];
    for (const pkg of downFrontier) {
      const group = groupFor(pkg);
      if (!group) continue;
      const source = openPackageNode(
        pkg,
        packageIdentityKey(pkg) === packageIdentityKey(model.package)
          ? "self"
          : "open");
      if (!source) continue;
      for (const dependency of group.dependencies || []) {
        const target = await dependencyNode(dependency);
        if (!target) break;
        addEdge(source, target);
        if (target.packageKey && !downVisited.has(target.packageKey)) {
          downVisited.add(target.packageKey);
          const open = model.packages.find(candidate =>
            packageIdentityKey(candidate) === target.packageKey);
          if (open) next.push(open);
        }
        if (truncated) break;
      }
    }
    downFrontier = next;
  }

  let upFrontier = [model.package];
  const upVisited = new Set([packageIdentityKey(model.package)]);
  for (let depth = 0; depth < MAX_DEPTH && upFrontier.length; depth++) {
    if (truncated) break;
    const next: DependencyGraphPackage[] = [];
    for (const targetPackage of upFrontier) {
      const target = openPackageNode(
        targetPackage,
        packageIdentityKey(targetPackage) === packageIdentityKey(model.package)
          ? "self"
          : "open");
      if (!target) break;
      for (const pkg of model.packages) {
        const pkgKey = packageIdentityKey(pkg);
        if (pkgKey === target.packageKey) continue;
        const group = groupFor(pkg);
        if (!group) continue;
        let dependsOnTarget = false;
        for (const dependency of group.dependencies || []) {
          if (packageIdentityKey(await uniqueCompatiblePackage(
            model.packages,
            dependency.id,
            dependency.versionRange)) === target.packageKey) {
            dependsOnTarget = true;
            break;
          }
        }
        if (dependsOnTarget) {
          const caller = openPackageNode(pkg);
          if (!caller) break;
          addEdge(caller, target);
          if (!upVisited.has(pkgKey)) {
            upVisited.add(pkgKey);
            next.push(pkg);
          }
        }
        if (truncated) break;
      }
    }
    upFrontier = next;
  }

  if (!edges.length) return null;

  const keys = [...nodeInfo.keys()];
  const roleValues = await classifyPresentationRoles(
    model.package.id,
    keys.map(key => nodeInfo.get(key)!.id),
  );
  if (roleValues.length !== keys.length) {
    throw new Error(
      `Package graph classification returned ${roleValues.length} roles for ${keys.length} nodes.`,
    );
  }
  const roles = roleValues.map((role, index): DependencyGraphPresentationRole => {
    if (role === "inspected" || role === "samePrefix" || role === "external")
      return role;
    throw new Error(`Package graph classification returned invalid role '${role}' at index ${index}.`);
  });
  const idOf = new Map<string, string>();
  keys.forEach((key, index) => idOf.set(key, `d${index}`));
  const lines = ["flowchart TD"];
  keys.forEach((key, index) => {
    const info = nodeInfo.get(key)!;
    const label = mermaidLabel(info.label);
    lines.push(`  ${idOf.get(key)}["${label}"]:::${roles[index]}`);
  });
  for (const edge of edges) {
    lines.push(`  ${idOf.get(edge.from)} --> ${idOf.get(edge.to)}`);
  }
  lines.push("classDef inspected fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),color:var(--graph-target-text),stroke-width:2px;");
  lines.push("classDef samePrefix fill:var(--graph-package-same-prefix-fill),stroke:var(--graph-package-same-prefix-stroke),color:var(--graph-package-same-prefix-text);");
  lines.push("classDef external fill:var(--graph-package-external-fill),stroke:var(--graph-package-external-stroke),color:var(--graph-package-external-text);");
  const nodeInfoById = new Map<string, DependencyGraphNodeInfo>();
  for (const key of keys) {
    const id = idOf.get(key);
    const info = nodeInfo.get(key);
    if (id && info) {
      const navigationInfo: DependencyGraphNodeInfo = { kind: info.kind };
      if (info.packageKey !== undefined)
        navigationInfo.packageKey = info.packageKey;
      if (info.id !== undefined) navigationInfo.id = info.id;
      if (info.versionRange !== undefined)
        navigationInfo.versionRange = info.versionRange;
      nodeInfoById.set(id, navigationInfo);
    }
  }
  return {
    definition: lines.join("\n"),
    nodeInfoById,
    truncated,
    nodeLimit: MAX_NODES
  };
}
