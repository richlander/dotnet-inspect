type PackageQueryAnnouncementInput = {
  catalogError: string;
  navigationError: string;
  failures: readonly string[];
  terminalFailure: string;
};

export type PackageQueryAnnouncementTracker = {
  beginNavigationAttempt: () => void;
  take: (input: PackageQueryAnnouncementInput) => string;
  reset: () => void;
};

export function createPackageQueryAnnouncementTracker():
  PackageQueryAnnouncementTracker {
  let catalogError = "";
  let navigationError = "";
  let failureCount = 0;
  let terminalFailure = "";

  return {
    beginNavigationAttempt() {
      navigationError = "";
    },
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

      if (input.terminalFailure
        && input.terminalFailure !== terminalFailure) {
        announcement.push(input.terminalFailure);
      }
      terminalFailure = input.terminalFailure;
      return announcement.join(" ");
    },
    reset() {
      catalogError = "";
      navigationError = "";
      failureCount = 0;
      terminalFailure = "";
    },
  };
}
