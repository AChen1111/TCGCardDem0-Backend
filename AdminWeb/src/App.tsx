import { useState } from "react";
import { PlayerPage } from "./pages/PlayerPage";

export function App() {
  const [refreshed, setRefreshed] = useState("尚未刷新");
  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">玩家控制台</div>
        <div className="topbar-meta"><span>{refreshed}</span></div>
      </header>
      <PlayerPage onRefreshed={setRefreshed} />
    </div>
  );
}
