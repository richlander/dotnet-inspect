import type { AppPackage, InspectedPackageIcon } from "./package-acquisition.ts";

type IconSubject = Pick<AppPackage, "id" | "version" | "icon">;

export function createSubjectIconLoader(options: {
  afterPaint: () => Promise<void>;
  current: () => IconSubject | null;
  query: (id: string, version: string) => Promise<{
    status: string | number; packageId: string; packageVersion: string;
    icon: InspectedPackageIcon | null;
  }>;
  apply: (icon: InspectedPackageIcon) => void;
}) {
  const requests = new WeakMap<IconSubject, Promise<void>>();
  return (pkg: IconSubject): Promise<void> => {
    const prior = requests.get(pkg);
    if (prior) return prior;
    const request = options.afterPaint().then(async () => {
      if (options.current() !== pkg) {
        requests.delete(pkg);
        return;
      }
      try {
        const result = await options.query(pkg.id, pkg.version);
        if (result.status !== "Available" || !result.icon
          || result.packageId !== pkg.id || result.packageVersion !== pkg.version) return;
        pkg.icon = result.icon;
        if (options.current() === pkg) options.apply(result.icon);
      } catch {
        // Keep the default icon when optional icon acquisition is unavailable.
      }
    });
    requests.set(pkg, request);
    return request;
  };
}
