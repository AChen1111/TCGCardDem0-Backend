import { useCallback, useEffect, useState } from "react";
import {
  ApiError,
  type AvatarRow,
  type CardPackRow,
  type GameConfigBootstrap,
  type GameConfigDraft,
  type WallpaperRow,
  getBootstrap,
  getDraft,
  importDraftCsv,
  publishDraft,
  saveDraft
} from "../api";
import { downloadText, gameConfigToCsv } from "../csv";
import { DataTable, type Column } from "../ui/DataTable";
import { usePoll } from "../usePoll";

const avatarColumns: Column<AvatarRow>[] = [
  { key: "id", title: "Id", kind: "number", width: "64px" },
  { key: "name", title: "名称" },
  { key: "resourceKey", title: "资源" },
  { key: "priceGold", title: "金币", kind: "number", width: "80px" },
  { key: "sortOrder", title: "排序", kind: "number", width: "64px" },
  { key: "isEnabled", title: "启用", kind: "bool", width: "56px" },
  { key: "startsAt", title: "开始" },
  { key: "endsAt", title: "结束" }
];

const packColumns: Column<CardPackRow>[] = [
  { key: "id", title: "Id", kind: "number", width: "64px" },
  { key: "title", title: "标题" },
  { key: "coverResourceKey", title: "封面" },
  { key: "poolKey", title: "卡池" },
  { key: "priceGold", title: "金币", kind: "number", width: "80px" },
  { key: "sortOrder", title: "排序", kind: "number", width: "64px" },
  { key: "isEnabled", title: "启用", kind: "bool", width: "56px" },
  { key: "startsAt", title: "开始" },
  { key: "endsAt", title: "结束" }
];

function nextId(rows: Array<{ id: number }>): number {
  return rows.reduce((max, row) => Math.max(max, row.id), 0) + 1;
}

function patchRow<T>(rows: T[], index: number, key: keyof T, value: string | number | boolean): T[] {
  return rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [key]: value } : row));
}

export function GameConfigPage({ onRefreshed }: { onRefreshed: (text: string) => void }) {
  const [live, setLive] = useState<GameConfigBootstrap | null>(null);
  const [draft, setDraft] = useState<GameConfigDraft | null>(null);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const loadDraft = useCallback(async () => {
    const next = await getDraft();
    setDraft(next);
    setDirty(false);
  }, []);

  const loadLive = useCallback(async () => {
    try {
      const next = await getBootstrap();
      setLive(next);
      onRefreshed(`线上 r${next.revision}`);
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === "GAME_CONFIG_NOT_PUBLISHED") {
        setLive(null);
        onRefreshed("尚未发布");
        return;
      }
      throw reason;
    }
  }, [onRefreshed]);

  useEffect(() => {
    void Promise.all([loadDraft(), loadLive()]).catch((reason) =>
      setError(reason instanceof Error ? reason.message : "加载失败")
    );
  }, [loadDraft, loadLive]);

  const pollLive = useCallback(() => {
    void loadLive().catch(() => undefined);
  }, [loadLive]);
  usePoll(true, 5000, pollLive);

  const updateDraft = (next: GameConfigDraft) => {
    setDraft(next);
    setDirty(true);
  };

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "操作失败");
    } finally {
      setBusy(false);
    }
  };

  const onImport = (file: File | undefined) => {
    if (!file || !draft) {
      return;
    }
    void run(async () => {
      const saved = await importDraftCsv(draft.editRevision, file);
      setDraft(saved);
      setDirty(false);
      setMessage("CSV 已写入草稿");
    });
  };

  return (
    <div className="page">
      <h1>游戏配置</h1>
      <div className="metrics">
        <div>
          线上 <strong>{live ? `r${live.revision}` : "未发布"}</strong>
        </div>
        <div>
          草稿 <strong>{draft ? `r${draft.draftRevision}` : "—"}</strong>
        </div>
        <div>
          EditRevision <strong>{draft?.editRevision ?? "—"}</strong>
        </div>
        {dirty ? <div>有未保存改动</div> : null}
      </div>
      {error ? <p className="notice">{error}</p> : null}
      {message ? <p className="notice ok">{message}</p> : null}

      <section className="section">
        <h2>线上（只读）</h2>
        <p className="muted">约 5 秒刷新一次，这是客户端正在用的配置。</p>
        <h2>头像 {live?.avatars.length ?? 0}</h2>
        <DataTable rows={live?.avatars ?? []} columns={avatarColumns} readOnly />
        <h2>壁纸 {live?.wallpapers.length ?? 0}</h2>
        <DataTable rows={live?.wallpapers ?? []} columns={avatarColumns} readOnly />
        <h2>卡包 {live?.cardPacks.length ?? 0}</h2>
        <DataTable rows={live?.cardPacks ?? []} columns={packColumns} readOnly />
      </section>

      <section className="section">
        <h2>草稿</h2>
        <div className="toolbar">
          <button
            className="primary"
            type="button"
            disabled={busy || !draft}
            onClick={() =>
              void run(async () => {
                if (!draft) {
                  return;
                }
                const saved = await saveDraft(draft);
                setDraft(saved);
                setDirty(false);
                setMessage("草稿已保存");
              })
            }
          >
            保存草稿
          </button>
          <button
            type="button"
            disabled={busy || !draft || dirty}
            onClick={() =>
              void run(async () => {
                if (!draft) {
                  return;
                }
                await publishDraft(draft.editRevision);
                await loadDraft();
                await loadLive();
                setMessage("已发布");
              })
            }
          >
            发布
          </button>
          <label className="file-button">
            导入 CSV
            <input
              type="file"
              accept=".csv,text/csv"
              onChange={(event) => {
                onImport(event.target.files?.[0]);
                event.target.value = "";
              }}
            />
          </label>
          <button
            type="button"
            disabled={!draft}
            onClick={() => {
              if (!draft) {
                return;
              }
              downloadText(
                `game-config-draft-r${draft.draftRevision}.csv`,
                gameConfigToCsv(draft.avatars, draft.wallpapers, draft.cardPacks)
              );
            }}
          >
            导出 CSV
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() =>
              void run(async () => {
                await loadDraft();
                setMessage("草稿已重新加载");
              })
            }
          >
            刷新草稿
          </button>
        </div>

        {draft ? (
          <>
            <h2>头像 {draft.avatars.length}</h2>
            <div className="toolbar">
              <button
                type="button"
                onClick={() =>
                  updateDraft({
                    ...draft,
                    avatars: [
                      ...draft.avatars,
                      {
                        id: nextId(draft.avatars),
                        name: "",
                        resourceKey: "",
                        priceGold: 0,
                        sortOrder: draft.avatars.length,
                        isEnabled: true
                      }
                    ]
                  })
                }
              >
                加一行
              </button>
            </div>
            <DataTable
              rows={draft.avatars}
              columns={avatarColumns}
              onChange={(index, key, value) =>
                updateDraft({ ...draft, avatars: patchRow(draft.avatars, index, key, value) })
              }
              onRemove={(index) =>
                updateDraft({
                  ...draft,
                  avatars: draft.avatars.filter((_, rowIndex) => rowIndex !== index)
                })
              }
            />

            <h2>壁纸 {draft.wallpapers.length}</h2>
            <div className="toolbar">
              <button
                type="button"
                onClick={() =>
                  updateDraft({
                    ...draft,
                    wallpapers: [
                      ...draft.wallpapers,
                      {
                        id: nextId(draft.wallpapers),
                        name: "",
                        resourceKey: "",
                        priceGold: 0,
                        sortOrder: draft.wallpapers.length,
                        isEnabled: true
                      }
                    ]
                  })
                }
              >
                加一行
              </button>
            </div>
            <DataTable
              rows={draft.wallpapers}
              columns={avatarColumns}
              onChange={(index, key, value) =>
                updateDraft({
                  ...draft,
                  wallpapers: patchRow(draft.wallpapers, index, key, value)
                })
              }
              onRemove={(index) =>
                updateDraft({
                  ...draft,
                  wallpapers: draft.wallpapers.filter((_, rowIndex) => rowIndex !== index)
                })
              }
            />

            <h2>卡包 {draft.cardPacks.length}</h2>
            <div className="toolbar">
              <button
                type="button"
                onClick={() =>
                  updateDraft({
                    ...draft,
                    cardPacks: [
                      ...draft.cardPacks,
                      {
                        id: nextId(draft.cardPacks),
                        title: "",
                        coverResourceKey: "",
                        poolKey: "",
                        priceGold: 0,
                        sortOrder: draft.cardPacks.length,
                        isEnabled: true
                      }
                    ]
                  })
                }
              >
                加一行
              </button>
            </div>
            <DataTable
              rows={draft.cardPacks}
              columns={packColumns}
              onChange={(index, key, value) =>
                updateDraft({
                  ...draft,
                  cardPacks: patchRow(draft.cardPacks, index, key, value)
                })
              }
              onRemove={(index) =>
                updateDraft({
                  ...draft,
                  cardPacks: draft.cardPacks.filter((_, rowIndex) => rowIndex !== index)
                })
              }
            />
          </>
        ) : null}
      </section>
    </div>
  );
}
