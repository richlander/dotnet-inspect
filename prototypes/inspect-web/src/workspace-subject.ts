import type { PackageControlPackage } from "./package-controls.ts";
import type {
  BrowserHomeDemoMember,
  BrowserHomeDemoResolved,
} from "./inspect-web-engine.d.ts";

export interface WorkspaceSubjectRenderOptions {
  packets: readonly BrowserHomeDemoResolved[];
  selectedPacketId: string | null;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspacePacketViewRenderOptions {
  packet: BrowserHomeDemoResolved | null;
  packages: readonly PackageControlPackage[];
  activePackage: PackageControlPackage | null;
  escapeHtml: (value: unknown) => string;
  packageIdentityKey: (pkg: PackageControlPackage) => string;
}

export interface WorkspaceSubjectBindingActions {
  onSelect: (packetId: string) => void;
  onOpen: (packetId: string) => void;
  onClose: (packageKey: string) => void;
}

export function renderWorkspaceSubject(
  options: WorkspaceSubjectRenderOptions,
): string {
  const {
    packets,
    selectedPacketId,
    escapeHtml,
  } = options;
  const rows = packets.map(packet => {
    const active = packet.id === selectedPacketId;
    const target = [packet.view.section, packet.view.type]
      .filter((value): value is string => Boolean(value))
      .join(" · ") || "Workspace";
    return `<button class="workspace-packet${active ? " active" : ""}" type="button" data-workspace-packet="${escapeHtml(packet.id)}" aria-current="${active ? "true" : "false"}">
      <strong>${escapeHtml(packet.title)}</strong>
      <span>${escapeHtml(packet.summary)}</span>
      <small>${escapeHtml(target)}</small>
    </button>`;
  }).join("");
  const content = rows
    || `<p class="workspace-packet-empty">Open a demo to retain its workspace packet here.</p>`;
  return `<aside class="type-browser workspace-nav">
    <header class="browser-head"><span>WORKSPACE PACKETS</span><small>${packets.length}</small></header>
    <div class="workspace-packet-list">${content}</div>
  </aside>`;
}

function coordinateDetail(
  member: BrowserHomeDemoMember,
  escapeHtml: (value: unknown) => string,
): string {
  const details = [member.version, member.framework, member.assembly]
    .filter((value): value is string => Boolean(value))
    .map(escapeHtml)
    .join(" · ");
  const kind = member.kind === "package"
    ? "NuGet package"
    : member.kind === "platform"
      ? "Platform"
      : member.kind;
  return `<li>
    <span>${escapeHtml(kind)}</span>
    <strong>${escapeHtml(member.id)}</strong>
    ${details ? `<small>${details}</small>` : ""}
  </li>`;
}

export function renderWorkspacePacketView(
  options: WorkspacePacketViewRenderOptions,
): string {
  const {
    packet,
    packages,
    activePackage,
    escapeHtml,
    packageIdentityKey,
  } = options;
  const title = packet?.title ?? "Current workspace";
  const summary = packet?.summary
    ?? "Live coordinates retained by this browser session.";
  const declaredMembers = packet?.workspaceMembers.map(member =>
    coordinateDetail(member, escapeHtml)).join("") ?? "";
  const focusedTab = packet
    ? packet.tabs[Math.min(
        Math.max(packet.focusTabIndex, 0),
        Math.max(packet.tabs.length - 1, 0))]
    : null;
  const viewRows = packet
    ? [
        ["Starts at", focusedTab?.member.id ?? null],
        ["Section", packet.view.section],
        ["Library", packet.view.library],
        ["Type", packet.view.type],
        ["Member", packet.view.memberKey ?? packet.view.memberAnchor],
      ].filter((row): row is [string, string] => Boolean(row[1]))
    : [];
  const loadedCoordinates = packages.map(item => {
    const key = packageIdentityKey(item);
    const active = Boolean(
      activePackage
      && packageIdentityKey(activePackage) === key);
    const label = `${item.id} ${item.version} ${item.activeFramework}`;
    return `<li class="${active ? "active" : ""}">
      <span>${item.isRuntimePack ? "Platform" : "Loaded package"}</span>
      <strong>${escapeHtml(item.id)}</strong>
      <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
      ${item.isRuntimePack
        ? ""
        : `<button type="button" data-workspace-close="${escapeHtml(key)}" aria-label="Close ${escapeHtml(label)}">Close</button>`}
    </li>`;
  }).join("");
  const packetDetails = packet
    ? `<section class="document-section workspace-packet-section">
        <div class="section-title"><h2>Packet workspace</h2><span>${packet.workspaceMembers.length} member${packet.workspaceMembers.length === 1 ? "" : "s"}</span></div>
        <ul class="workspace-detail-list">${declaredMembers}</ul>
      </section>
      <section class="document-section workspace-packet-section">
        <div class="section-title"><h2>Initial view</h2><span>selection only</span></div>
        <dl class="workspace-view-details">${viewRows.map(([label, value]) =>
          `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd></div>`).join("")}</dl>
      </section>`
    : "";
  return `<header class="type-heading workspace-heading">
    <div class="type-badge">W</div>
    <div>
      <div class="type-namespace">${packet ? "Workspace packet" : "Inspection workspace"}</div>
      <h1>${escapeHtml(title)}</h1>
      <code class="type-signature">${packet
        ? `${packet.workspaceMembers.length} workspace member${packet.workspaceMembers.length === 1 ? "" : "s"} · ${packet.tabs.length} navigation target${packet.tabs.length === 1 ? "" : "s"}`
        : `${packages.length} loaded coordinate${packages.length === 1 ? "" : "s"}`}</code>
    </div>
  </header>
  <div class="workspace-overview">
    <div class="workspace-packet-introduction">
      <p>${escapeHtml(summary)}</p>
      ${packet
        ? `<button class="primary-action" type="button" data-workspace-open="${escapeHtml(packet.id)}">Open workspace</button>`
        : ""}
    </div>
    ${packetDetails}
    <section class="document-section workspace-packet-section">
      <div class="section-title"><h2>Loaded workspace</h2><span>${packages.length} coordinate${packages.length === 1 ? "" : "s"}</span></div>
      <p>Workspace packets may share these loaded coordinates without becoming the same packet.</p>
      <ul class="workspace-detail-list loaded">${loadedCoordinates}</ul>
    </section>
  </div>`;
}

export function bindWorkspaceSubject(
  root: ParentNode,
  actions: WorkspaceSubjectBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-workspace-packet]").forEach(button =>
    button.addEventListener("click", () => {
      const id = button.dataset.workspacePacket;
      if (id !== undefined) actions.onSelect(id);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-open]").forEach(button =>
    button.addEventListener("click", () => {
      const id = button.dataset.workspaceOpen;
      if (id !== undefined) actions.onOpen(id);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-close]").forEach(button =>
    button.addEventListener("click", () => {
      const key = button.dataset.workspaceClose;
      if (key !== undefined) actions.onClose(key);
    }));
}

export function focusWorkspacePacket(
  root: ParentNode,
  packetId: string,
): boolean {
  for (const button of root.querySelectorAll<HTMLElement>("[data-workspace-packet]")) {
    if (button.dataset.workspacePacket !== packetId) continue;
    button.focus();
    return true;
  }
  return false;
}
