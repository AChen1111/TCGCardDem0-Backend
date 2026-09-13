import { FormEvent, useState } from "react";
import { type AdminPlayer, addGold, getPlayer, rarityLabel } from "../api";
import { DataTable, type Column } from "../ui/DataTable";

const cardColumns: Column<{ cardId: string; rarity: string; count: number }>[] = [
  { key: "cardId", title: "卡牌 Id" },
  { key: "rarity", title: "稀有度" },
  { key: "count", title: "数量", kind: "number" }
];

function formatTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function PlayerPage({ onRefreshed }: { onRefreshed: (text: string) => void }) {
  const [username, setUsername] = useState("");
  const [amount, setAmount] = useState("100");
  const [player, setPlayer] = useState<AdminPlayer | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  const lookup = async (event?: FormEvent) => {
    event?.preventDefault();
    const name = username.trim();
    if (!name) {
      return;
    }
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const next = await getPlayer(name);
      setPlayer(next);
      onRefreshed(next.username);
    } catch (reason) {
      setPlayer(null);
      setError(reason instanceof Error ? reason.message : "查询失败");
    } finally {
      setBusy(false);
    }
  };

  const grant = async (event: FormEvent) => {
    event.preventDefault();
    if (!player) {
      return;
    }
    const gold = Number(amount);
    if (!Number.isFinite(gold) || gold <= 0) {
      setError("金币数量必须大于 0");
      return;
    }
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const result = await addGold(player.username, gold);
      setPlayer({ ...player, gold: result.gold, revision: result.revision });
      setMessage(`已加 ${result.addedAmount}，当前 ${result.gold}`);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "加金币失败");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page">
      <h1>玩家</h1>
      <p className="muted">按用户名精确查找，大小写不敏感。</p>
      <form className="player-search" onSubmit={(event) => void lookup(event)}>
        <input
          value={username}
          placeholder="用户名"
          onChange={(event) => setUsername(event.target.value)}
        />
        <button type="submit" disabled={busy || username.trim().length === 0}>
          查找
        </button>
      </form>
      {error ? <p className="notice">{error}</p> : null}
      {message ? <p className="notice ok">{message}</p> : null}

      {player ? (
        <>
          <div className="kv">
            <span>账号</span>
            <strong>{player.username}</strong>
            <span>昵称</span>
            <div>{player.nickname}</div>
            <span>金币</span>
            <div>{player.gold}</div>
            <span>Revision</span>
            <div>{player.revision}</div>
            <span>头像</span>
            <div>
              {player.avatarId ?? "无"} / 拥有 {player.ownedAvatarIds.join(", ") || "无"}
            </div>
            <span>壁纸</span>
            <div>
              {player.backgroundId ?? "无"} / 拥有 {player.ownedBackgroundIds.join(", ") || "无"}
            </div>
            <span>创建</span>
            <div>{formatTime(player.createdAt)}</div>
            <span>更新</span>
            <div>{formatTime(player.updatedAt)}</div>
          </div>

          <form className="gold-form" onSubmit={(event) => void grant(event)}>
            <input
              type="number"
              min={1}
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
            />
            <button type="submit" disabled={busy}>
              加金币
            </button>
          </form>

          <section className="section">
            <h2>持有卡牌 {player.ownedCards.length}</h2>
            <DataTable
              rows={player.ownedCards.map((card) => ({
                cardId: card.cardId,
                rarity: rarityLabel(card.rarity),
                count: card.count
              }))}
              columns={cardColumns}
              readOnly
            />
          </section>
        </>
      ) : null}
    </div>
  );
}
