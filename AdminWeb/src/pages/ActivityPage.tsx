import { useState } from "react";
import { request } from "../api";

type Master = { activityId: string; type: number; detailTable: string; isEnabled: boolean; scheduleMode: number; startsAt: string | null; endsAt: string | null; nameKey: string };
type Row = { id: string; masterJson: string; activeVersion: number; detailHash: string };
type Release = { releaseId: string; revision: number; actor: string; publishedAt: string; masterJson: string; manifestJson: string };
type Current = { current: Release | null; activities: Row[] };
const TYPES = ["公告", "礼包", "签到", "兑换", "里程碑"];
export function ActivityPage() {
  const [key, setKey] = useState("");
  const [current, setCurrent] = useState<Current>();
  const [history, setHistory] = useState<Release[]>([]);
  const [selected, setSelected] = useState<Release>();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function load() {
    setBusy(true); setError("");
    try {
      const headers = { "X-Content-Publish-Key": key };
      const [data, releases] = await Promise.all([request<Current>("/api/admin/activities", { headers }), request<Release[]>("/api/admin/activities/history", { headers })]);
      setCurrent(data); setHistory(releases); setSelected(undefined);
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  const masters: Master[] = selected ? JSON.parse(selected.masterJson) : current?.current ? JSON.parse(current.current.masterJson) : [];
  return <section className="panel">
    <h2>活动配置（只读）</h2>
    <p>CSV 是唯一编辑来源：修改 ActivityTableData，执行 Tools/AddToActBytes，再通过 Tools/活动配置/发布活动 上传整套版本。礼包奖励也在活动子表中配置。</p>
    <label>发布密钥 <input type="password" value={key} onChange={e => setKey(e.target.value)} autoComplete="off" /></label>
    <button disabled={busy} onClick={load}>查询当前版本与发布历史</button>
    {error && <p role="alert">{error}</p>}
    <p>当前全平台版本：{current?.current ? `${current.current.revision} / ${current.current.releaseId}` : "尚未发布"}</p>
    <label>查看总表 <select value={selected?.releaseId ?? ""} onChange={e => setSelected(history.find(x => x.releaseId === e.target.value))}>
      <option value="">当前版本</option>{history.map(x => <option key={x.releaseId} value={x.releaseId}>{x.revision} · {x.publishedAt} · {x.actor}</option>)}
    </select></label>
    <table><thead><tr><th>活动 ID</th><th>玩法</th><th>子表</th><th>启用</th><th>排期</th><th>名称键</th><th>定义版本</th></tr></thead>
      <tbody>{masters.map(m => <tr key={m.activityId}><td>{m.activityId}</td><td>{TYPES[m.type]}</td><td>{m.detailTable}.bytes</td><td>{m.isEnabled ? "是" : "否"}</td><td>{m.scheduleMode === 0 ? "长期" : `${m.startsAt} ～ ${m.endsAt}`}</td><td>{m.nameKey}</td><td>{selected ? "见发布清单" : current?.activities.find(x => x.id === m.activityId)?.activeVersion}</td></tr>)}</tbody>
    </table>
    <h3>发布历史</h3><table><thead><tr><th>版本</th><th>发布时间</th><th>发布者</th><th>版本 ID</th></tr></thead><tbody>{history.map(x => <tr key={x.releaseId}><td>{x.revision}</td><td>{x.publishedAt}</td><td>{x.actor}</td><td>{x.releaseId}</td></tr>)}</tbody></table>
    {selected && <details><summary>文件清单与哈希</summary><pre>{JSON.stringify(JSON.parse(selected.manifestJson), null, 2)}</pre></details>}
  </section>;
}
