import { useCallback, useEffect, useMemo, useState } from "react";
import {
  type GachaPoolEntry,
  type GachaRarityWeight,
  getGacha,
  putGachaCsv,
  rarityLabel
} from "../api";
import { gachaToCsv } from "../csv";
import { DataTable, type Column } from "../ui/DataTable";
import { usePoll } from "../usePoll";

const rarityColumns: Column<GachaRarityWeight>[] = [
  { key: "rarity", title: "稀有度", kind: "number", width: "80px" },
  { key: "weight", title: "权重", kind: "number", width: "80px" }
];

const entryColumns: Column<GachaPoolEntry>[] = [
  { key: "poolKey", title: "卡池" },
  { key: "cardId", title: "卡牌 Id" },
  { key: "weight", title: "权重", kind: "number", width: "80px" }
];

function patchRow<T>(rows: T[], index: number, key: keyof T, value: string | number | boolean): T[] {
  return rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [key]: value } : row));
}

export function GachaPage({ onRefreshed }: { onRefreshed: (text: string) => void }) {
  const [liveEntries, setLiveEntries] = useState<GachaPoolEntry[]>([]);
  const [liveWeights, setLiveWeights] = useState<GachaRarityWeight[]>([]);
  const [entries, setEntries] = useState<GachaPoolEntry[]>([]);
  const [weights, setWeights] = useState<GachaRarityWeight[]>([]);
  const [dirty, setDirty] = useState(false);
  const [poolFilter, setPoolFilter] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const applyLive = useCallback(
    (nextEntries: GachaPoolEntry[], nextWeights: GachaRarityWeight[], overwriteEdit: boolean) => {
      setLiveEntries(nextEntries);
      setLiveWeights(nextWeights);
      if (overwriteEdit) {
        setEntries(nextEntries);
        setWeights(nextWeights);
        setDirty(false);
      }
      onRefreshed(`卡池 ${nextEntries.length} / 稀有度 ${nextWeights.length}`);
    },
    [onRefreshed]
  );

  const load = useCallback(
    async (overwriteEdit: boolean) => {
      const config = await getGacha();
      applyLive(config.poolEntries, config.rarityWeights, overwriteEdit);
    },
    [applyLive]
  );

  useEffect(() => {
    void load(true).catch((reason) => setError(reason instanceof Error ? reason.message : "加载失败"));
  }, [load]);

  const poll = useCallback(() => {
    void load(!dirty).catch(() => undefined);
  }, [dirty, load]);
  usePoll(true, 5000, poll);

  const visibleEntries = useMemo(
    () =>
      poolFilter.trim()
        ? entries.filter((entry) => entry.poolKey.toLowerCase().includes(poolFilter.trim().toLowerCase()))
        : entries,
    [entries, poolFilter]
  );

  const runSave = async (csv: string) => {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const config = await putGachaCsv(csv);
      applyLive(config.poolEntries, config.rarityWeights, true);
      setMessage("抽卡配置已生效");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "保存失败");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page">
      <h1>抽卡卡池</h1>
      <div className="metrics">
        <div>
          线上卡池 <strong>{liveEntries.length}</strong>
        </div>
        <div>
          稀有度 <strong>{liveWeights.length}</strong>
        </div>
        {dirty ? <div>有未保存改动，轮询不会覆盖编辑表</div> : null}
      </div>
      {error ? <p className="notice">{error}</p> : null}
      {message ? <p className="notice ok">{message}</p> : null}

      <section className="section">
        <h2>线上稀有度</h2>
        <DataTable
          rows={liveWeights.map((row) => ({ label: rarityLabel(row.rarity), weight: row.weight }))}
          columns={[
            { key: "label", title: "稀有度" },
            { key: "weight", title: "权重", kind: "number" }
          ]}
          readOnly
        />
        <h2>线上卡池</h2>
        <DataTable rows={liveEntries} columns={entryColumns} readOnly />
      </section>

      <section className="section">
        <h2>编辑并立即生效</h2>
        <div className="toolbar">
          <button
            className="primary"
            type="button"
            disabled={busy}
            onClick={() => void runSave(gachaToCsv(entries, weights))}
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
          <input
            type="text"
            placeholder="筛选卡池"
            value={poolFilter}
            onChange={(event) => setPoolFilter(event.target.value)}
          />
          <button
            type="button"
            onClick={() => {
              setWeights([...weights, { rarity: 0, weight: 1 }]);
              setDirty(true);
            }}
          >
            加稀有度
          </button>
          <button
            type="button"
            onClick={() => {
              setEntries([...entries, { poolKey: "", cardId: "", weight: 100 }]);
              setDirty(true);
            }}
          >
            加卡池行
          </button>
        </div>

        <h2>稀有度权重</h2>
        <p className="muted">0 普通 / 1 炫彩 / 2 镜碎 / 3 描边 / 4 Gold</p>
        <DataTable
          rows={weights}
          columns={rarityColumns}
          onChange={(index, key, value) => {
            setWeights(patchRow(weights, index, key, value));
            setDirty(true);
          }}
          onRemove={(index) => {
            setWeights(weights.filter((_, rowIndex) => rowIndex !== index));
            setDirty(true);
          }}
        />

        <h2>卡池 {visibleEntries.length}</h2>
        <DataTable
          rows={visibleEntries}
          columns={entryColumns}
          onChange={(index, key, value) => {
            const target = visibleEntries[index];
            const realIndex = entries.indexOf(target);
            setEntries(patchRow(entries, realIndex, key, value));
            setDirty(true);
          }}
          onRemove={(index) => {
            const target = visibleEntries[index];
            setEntries(entries.filter((entry) => entry !== target));
            setDirty(true);
          }}
        />
      </section>
    </div>
  );
}
