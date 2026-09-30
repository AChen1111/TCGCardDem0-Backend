import { useState } from "react";
import { PlayerPage } from "./pages/PlayerPage";
import { ActivityPage } from "./pages/ActivityPage";

export function App() {
  const [refreshed, setRefreshed] = useState("尚未刷新");
  const [page, setPage] = useState("players");
  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">游戏控制台</div>
        <nav className="nav"><button className={page === "players" ? "active" : ""} onClick={() => setPage("players")}>玩家</button><button className={page === "activities" ? "active" : ""} onClick={() => setPage("activities")}>活动</button></nav>
        <div className="topbar-meta"><span>{refreshed}</span></div>
      </header>
      {page === "players" ? <PlayerPage onRefreshed={setRefreshed} /> : <ActivityPage />}
    </div>
  );
}
