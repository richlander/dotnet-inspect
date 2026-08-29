type PackageQueryAnnouncementInput = {
  catalogError: string;
  navigationError: string;
  failures: readonly string[];
};

export type PackageQueryAnnouncementTracker = {
  take: (input: PackageQueryAnnouncementInput) => string;
  reset: () => void;
};

export function createPackageQueryAnnouncementTracker():
  PackageQueryAnnouncementTracker {
  let catalogError = "";
  let navigationError = "";
  let failureCount = 0;

  return {
    take(input) {
      const announcement: string[] = [];
      if (input.catalogError && input.catalogError !== catalogError) {
        announcement.push(input.catalogError);
      }
      catalogError = input.catalogError;

      if (input.navigationError
        && input.navigationError !== navigationError) {
        announcement.push(input.navigationError);
      }
      navigationError = input.navigationError;

      if (input.failures.length < failureCount) failureCount = 0;
      announcement.push(...input.failures.slice(failureCount));
      failureCount = input.failures.length;
      return announcement.join(" ");
    },
    reset() {
      catalogError = "";
      navigationError = "";
      failureCount = 0;
    },
  };
}
