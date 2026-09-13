import { useCallback, useEffect, useState } from "react";
import { type AllCardRow, getAllCards, putAllCardsCsv } from "../api";
import { cardsToCsv } from "../csv";
import { DataTable, type Column } from "../ui/DataTable";
import { usePoll } from "../usePoll";

const columns: Column<AllCardRow>[] = [
  { key: "cardId", title: "卡牌 Id" },
  { key: "sourcePool", title: "来源卡池" }
];

function patchRow<T>(rows: T[], index: number, key: keyof T, value: string | number | boolean): T[] {
  return rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [key]: value } : row));
}

export function CardsPage({ onRefreshed }: { onRefreshed: (text: string) => void }) {
  const [live, setLive] = useState<AllCardRow[]>([]);
  const [cards, setCards] = useState<AllCardRow[]>([]);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const apply = useCallback(
    (next: AllCardRow[], overwriteEdit: boolean) => {
      setLive(next);
      if (overwriteEdit) {
        setCards(next);
        setDirty(false);
      }
      onRefreshed(`卡牌 ${next.length}`);
    },
    [onRefreshed]
  );

  const load = useCallback(
    async (overwriteEdit: boolean) => {
      const config = await getAllCards();
      apply(config.cards, overwriteEdit);
    },
    [apply]
  );

  useEffect(() => {
    void load(true).catch((reason) => setError(reason instanceof Error ? reason.message : "加载失败"));
  }, [load]);

  const poll = useCallback(() => {
    void load(!dirty).catch(() => undefined);
  }, [dirty, load]);
  usePoll(true, 5000, poll);

  const runSave = async (csv: string) => {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const config = await putAllCardsCsv(csv);
      apply(config.cards, true);
      setMessage("全部卡牌已生效");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "保存失败");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page">
      <h1>全部卡牌</h1>
      <div className="metrics">
        <div>
          线上 <strong>{live.length}</strong>
        </div>
        {dirty ? <div>有未保存改动，轮询不会覆盖编辑表</div> : null}
      </div>
      {error ? <p className="notice">{error}</p> : null}
      {message ? <p className="notice ok">{message}</p> : null}

      <section className="section">
        <h2>线上</h2>
        <DataTable rows={live} columns={columns} readOnly />
      </section>

      <section className="section">
        <h2>编辑并立即生效</h2>
        <div className="toolbar">
          <button
            className="primary"
            type="button"
            disabled={busy}
            onClick={() => void runSave(cardsToCsv(cards))}
          >
            保存
          </button>
          <label className="file-button">
            导入 CSV
            <input
              type="file"
              accept=".csv,text/csv"
              onChange={(event) => {
                const file = event.target.files?.[0];
                event.target.value = "";
                if (!file) {
                  return;
                }
                void file.text().then((text) => runSave(text));
              }}
            />
          </label>
          <button
            type="button"
            onClick={() => {
              setCards([...cards, { cardId: "", sourcePool: "" }]);
              setDirty(true);
            }}
          >
            加一行
          </button>
        </div>
        <DataTable
          rows={cards}
          columns={columns}
          onChange={(index, key, value) => {
            setCards(patchRow(cards, index, key, value));
            setDirty(true);
          }}
          onRemove={(index) => {
            setCards(cards.filter((_, rowIndex) => rowIndex !== index));
            setDirty(true);
          }}
        />
      </section>
    </div>
  );
}
