import type {
  AllCardRow,
  AvatarRow,
  CardPackRow,
  GachaPoolEntry,
  GachaRarityWeight,
  WallpaperRow
} from "./api";

function escapeCell(value: string): string {
  const protectedValue = value.length > 0 && "+=-@".includes(value[0]) ? `'${value}` : value;
  return /[",\r\n]/.test(protectedValue)
    ? `"${protectedValue.replace(/"/g, '""')}"`
    : protectedValue;
}

function row(cells: Array<string | number>): string {
  return cells.map((cell) => escapeCell(String(cell))).join(",");
}

export function gachaToCsv(entries: GachaPoolEntry[], weights: GachaRarityWeight[]): string {
  const lines = ["Table,PoolKey,CardId,Weight,Rarity"];
  for (const entry of entries) {
    lines.push(row(["Card", entry.poolKey, entry.cardId, entry.weight, ""]));
  }
  for (const weight of weights) {
    lines.push(row(["Rarity", "", "", weight.weight, weight.rarity]));
  }
  return lines.join("\r\n") + "\r\n";
}

export function cardsToCsv(cards: AllCardRow[]): string {
  const lines = ["CardId,SourcePool"];
  for (const card of cards) {
    lines.push(row([card.cardId, card.sourcePool]));
  }
  return lines.join("\r\n") + "\r\n";
}

export function gameConfigToCsv(
  avatars: AvatarRow[],
  wallpapers: WallpaperRow[],
  cardPacks: CardPackRow[]
): string {
  const lines = [
    "Table,Id,Name,ResourceKey,PriceGold,StartsAt,EndsAt,SortOrder,IsEnabled,PoolKey"
  ];
  const itemRow = (
    table: string,
    id: number,
    name: string,
    resourceKey: string,
    priceGold: number,
    startsAt: string | null | undefined,
    endsAt: string | null | undefined,
    sortOrder: number,
    isEnabled: boolean,
    poolKey: string
  ) =>
    row([
      table,
      id,
      name,
      resourceKey,
      priceGold,
      startsAt ?? "",
      endsAt ?? "",
      sortOrder,
      isEnabled ? "True" : "False",
      poolKey
    ]);

  for (const item of avatars) {
    lines.push(
      itemRow(
        "Avatar",
        item.id,
        item.name,
        item.resourceKey,
        item.priceGold,
        item.startsAt,
        item.endsAt,
        item.sortOrder,
        item.isEnabled,
        ""
      )
    );
  }
  for (const item of wallpapers) {
    lines.push(
      itemRow(
        "Wallpaper",
        item.id,
        item.name,
        item.resourceKey,
        item.priceGold,
        item.startsAt,
        item.endsAt,
        item.sortOrder,
        item.isEnabled,
        ""
      )
    );
  }
  for (const item of cardPacks) {
    lines.push(
      itemRow(
        "CardPack",
        item.id,
        item.title,
        item.coverResourceKey,
        item.priceGold,
        item.startsAt,
        item.endsAt,
        item.sortOrder,
        item.isEnabled,
        item.poolKey
      )
    );
  }
  return lines.join("\r\n") + "\r\n";
}

export function downloadText(filename: string, text: string) {
  const blob = new Blob([text], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
