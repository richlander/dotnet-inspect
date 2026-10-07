import type { BrowserMemberSourceResult } from "./facades/inspect-web-source.d.ts";

export interface TriageCodeTarget {
  assembly: string;
  typeId: string;
  memberName: string;
  selector: string;
  methodToken: number;
}

export function triageMemberLabel(typeId: string, memberName: string): string {
  const generic = typeId.indexOf("<");
  const head = generic < 0 ? typeId : typeId.slice(0, generic);
  const tail = generic < 0 ? "" : typeId.slice(generic);
  const short = head.slice(head.lastIndexOf(".") + 1).replaceAll("+", ".") + tail;
  return short ? `${short}.${memberName}` : memberName;
}

export function renderTriageCode(target: TriageCodeTarget, escape: (value: string) => string): string {
  return `<details class="triage-code" data-triage-code data-triage-assembly="${escape(target.assembly)}" data-triage-type="${escape(target.typeId)}" data-triage-member="${escape(target.memberName)}" data-triage-selector="${escape(target.selector)}" data-triage-token="${target.methodToken}"><summary>Decompiled method</summary><pre><code>Expand to decompile this method.</code></pre></details>`;
}

export function bindTriageCode(
  root: ParentNode,
  load: (target: TriageCodeTarget) => Promise<BrowserMemberSourceResult>,
): void {
  root.querySelectorAll<HTMLDetailsElement>("[data-triage-code]").forEach(details => {
    let pending = false;
    let loaded = false;
    const loadCode = async () => {
      if (!details.open || pending || loaded) return;
      const code = details.querySelector("code");
      if (!code) return;
      pending = true;
      code.textContent = "Decompiling…";
      try {
        const result = await load({
          assembly: details.dataset.triageAssembly ?? "",
          typeId: details.dataset.triageType ?? "",
          memberName: details.dataset.triageMember ?? "",
          selector: details.dataset.triageSelector ?? "",
          methodToken: Number(details.dataset.triageToken),
        });
        if (!details.isConnected) return;
        const source = result.value;
        const memberSpans = source?.parts.filter(part => part.kind === "Member")
          .flatMap(part => part.spans) ?? [];
        code.textContent = source
          ? memberSpans.length
            ? memberSpans.map(span => source.source.text.slice(span.start, span.start + span.length)).join("\n\n")
            : source.source.text
          : result.error ?? "Decompiled source unavailable.";
        loaded = Boolean(result.value);
      } catch (error) {
        if (details.isConnected) code.textContent = error instanceof Error ? error.message : "Decompilation failed.";
      } finally {
        pending = false;
      }
    };
    details.addEventListener("toggle", () => { void loadCode(); });
  });
}
