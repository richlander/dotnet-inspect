export const MAX_ITEM_ACHIEVEMENTS = 2;

export type ItemAchievementKind = "sea-level" | "mountain-peak";

export interface ItemAchievement {
  kind: ItemAchievementKind;
  description: string;
}

export type EscapeAchievementHtml = (value: unknown) => string;

export function itemAchievementClassNames(
  achievements: readonly ItemAchievement[],
): string {
  return achievements.map(achievement => achievement.kind).join(" ");
}

export function renderItemAchievementRail(
  achievements: readonly ItemAchievement[],
  escapeHtml: EscapeAchievementHtml,
): string {
  if (achievements.length > MAX_ITEM_ACHIEVEMENTS) {
    throw new RangeError(
      `Item achievement rail supports at most ${MAX_ITEM_ACHIEVEMENTS} glyphs.`,
    );
  }
  if (new Set(achievements.map(achievement => achievement.kind)).size
    !== achievements.length) {
    throw new Error("Item achievement rail requires distinct glyph kinds.");
  }
  if (achievements.length === 0) {
    return '<span class="item-achievement-rail" aria-hidden="true"></span>';
  }

  const description = achievements
    .map(achievement => achievement.description)
    .join("; ");
  return `<span class="item-achievement-rail" role="img" aria-label="${escapeHtml(description)}" title="${escapeHtml(description)}">${
    achievements.map(achievement =>
      `<span class="item-achievement-glyph ${achievement.kind}" aria-hidden="true"></span>`
    ).join("")
  }</span>`;
}
