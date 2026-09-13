import { useState } from "react";
import { CardsPage } from "./pages/CardsPage";
import { GameConfigPage } from "./pages/GameConfigPage";
import { GachaPage } from "./pages/GachaPage";
import { PlayerPage } from "./pages/PlayerPage";

type Page = "config" | "gacha" | "cards" | "player";

const NAV: Array<{ id: Page; label: string }> = [
  { id: "config", label: "游戏配置" },
  { id: "gacha", label: "抽卡卡池" },
  { id: "cards", label: "全部卡牌" },
  { id: "player", label: "玩家" }
];

export function App() {
  const [page, setPage] = useState<Page>("config");
  const [refreshed, setRefreshed] = useState("尚未刷新");

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">配置控制台</div>
        <nav className="nav">
          {NAV.map((item) => (
            <button
              key={item.id}
              type="button"
              className={page === item.id ? "active" : undefined}
              onClick={() => setPage(item.id)}
            >
              {item.label}
            </button>
          ))}
        </nav>
        <div className="topbar-meta">
          <span>{refreshed}</span>
        </div>
      </header>
      {page === "config" ? <GameConfigPage onRefreshed={setRefreshed} /> : null}
      {page === "gacha" ? <GachaPage onRefreshed={setRefreshed} /> : null}
      {page === "cards" ? <CardsPage onRefreshed={setRefreshed} /> : null}
      {page === "player" ? <PlayerPage onRefreshed={setRefreshed} /> : null}
    </div>
  );
}
