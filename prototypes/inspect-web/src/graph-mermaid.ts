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

export function resolveMermaidCssVariables(
  definition: string,
  readProperty: (name: string) => string,
): string {
  return definition.replace(
    /var\((--[\w-]+)\)/g,
    (whole: string, name: string) => readProperty(name).trim() || whole);
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
  lines.push("classDef self fill:var(--accent-soft),stroke:var(--accent),color:var(--text),stroke-width:2px;");
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
) => DependencyGraphPackage | null;

interface MermaidGraphNodeInfo extends DependencyGraphNodeInfo {
  key: string;
  label: string;
}

export function buildDependencyGraphMermaid(
  model: DependencyGraphModel,
  uniqueCompatiblePackage: UniqueCompatiblePackage,
): DependencyGraphResult | null {
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
  const dependencyNode = (dependency: DependencyGraphWorkspaceDependency): MermaidGraphNodeInfo | null => {
    const open = uniqueCompatiblePackage(
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
        const target = dependencyNode(dependency);
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
        if ((group.dependencies || []).some(dependency =>
          packageIdentityKey(uniqueCompatiblePackage(
            model.packages,
            dependency.id,
            dependency.versionRange)) === target.packageKey)) {
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
  const idOf = new Map<string, string>();
  keys.forEach((key, index) => idOf.set(key, `d${index}`));
  const lines = ["flowchart TD"];
  for (const key of keys) {
    const info = nodeInfo.get(key)!;
    const label = mermaidLabel(info.label);
    lines.push(`  ${idOf.get(key)}["${label}"]:::${info.kind}`);
  }
  for (const edge of edges) {
    lines.push(`  ${idOf.get(edge.from)} --> ${idOf.get(edge.to)}`);
  }
  lines.push("classDef self fill:var(--accent-soft),stroke:var(--accent),color:var(--text),stroke-width:2px;");
  lines.push("classDef open fill:var(--panel-active),stroke:var(--blue),color:var(--text);");
  lines.push("classDef external fill:transparent,stroke:var(--line-strong),color:var(--dim);");
  const nodeInfoById = new Map<string, DependencyGraphNodeInfo>();
  for (const key of keys) {
    const id = idOf.get(key);
    const info = nodeInfo.get(key);
    if (id && info) nodeInfoById.set(id, info);
  }
  return {
    definition: lines.join("\n"),
    nodeInfoById,
    truncated,
    nodeLimit: MAX_NODES
  };
}
