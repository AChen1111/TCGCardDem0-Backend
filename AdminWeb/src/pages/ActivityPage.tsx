import { useState } from "react";
import { request, rarityLabel } from "../api";

type Reward = { rewardType: number; rewardId: string; amount: number; cardVariant: number };
type Gift = { id: string; nameKey: string; description: string; rewards: Reward[] };
type GiftRow = { id: string; revision: number; frozen: boolean; definitionJson: string };
type Entry = { id: string; giftId: string; nameKey: string; description: string; periodKind: number; limitPerPeriod: number; totalLimit: number | null; sortOrder: number; dayIndex: number; threshold: number; costGold: number; rewards: Reward[] };
type Condition = { type: string; value: number; activityId: string; time: string | null };
type Definition = {
  id: string; nameKey: string; description: string; descriptionKey: string; type: number;
  scheduleMode: number; startsAt: string | null; endsAt: string | null; displayMode: number; sortOrder: number;
  bannerResourceKey: string; showBeforeStart: boolean; hideWhenCompleted: boolean; showLocked: boolean; definitionVersion: number;
  openConditions: { allOf: Condition[] };
  popup: { trigger: string; frequency: string; priority: number; policyVersion: number; stopWhenCompleted: boolean; shouldShow: boolean };
  notice: { content: string; imageResourceKey: string; actionKind: string; actionTarget: string };
  requiredDays: number; allowCatchUpClaims: boolean; metricType: string; packIds: number[]; entries: Entry[];
};
type Row = { id: string; target: string; configHash: string; draftJson: string; revision: number; activeVersion: number; publishStatus: number };
type Catalog = { target: string; configHash: string; entries: { key: string; chinese: string; english: string }[]; cards: string[]; packs: { id: number; title: string }[]; resources: string[] };
type History = { version: number; action: string; actor: string; publishedAt: string };
const TYPES = ["公告", "固定礼包", "累计登录", "金币兑换", "抽卡里程碑"];
const MODES = ["活动页面", "自动弹窗", "页面与弹窗"];
const STATUS = ["草稿", "已发布", "已下架"];
const gold = (): Reward => ({ rewardType: 1, rewardId: "gold", amount: 200, cardVariant: 0 });
const entry = (i: number): Entry => ({ id: "reward_" + i, giftId: "", nameKey: "", description: "", periodKind: 0, limitPerPeriod: 1, totalLimit: 1, sortOrder: i, dayIndex: i, threshold: i * 10, costGold: 800, rewards: [] });
function fresh(type = 1): Definition {
  const names = ["notice_national_day_2026", "national_day_gift_2026", "login_three_days_2026", "daily_card_exchange", "draw_ten_reward_2026"];
  return { id: names[type], nameKey: "activity." + names[type] + ".name", description: "", descriptionKey: "", type, scheduleMode: 0, startsAt: null, endsAt: null, displayMode: type === 0 ? 1 : 0, sortOrder: 0, bannerResourceKey: "", showBeforeStart: false, hideWhenCompleted: false, showLocked: true, definitionVersion: 0, openConditions: { allOf: [] }, popup: { trigger: "lobbyReady", frequency: "oncePerActivity", priority: 0, policyVersion: 1, stopWhenCompleted: true, shouldShow: false }, notice: { content: "", imageResourceKey: "", actionKind: "close", actionTarget: "" }, requiredDays: 3, allowCatchUpClaims: true, metricType: "gachaDrawCount", packIds: [], entries: type === 0 ? [] : [{ ...entry(1), costGold: type === 3 ? 800 : 0 }] };
}
const defaultGift = (): Gift => ({ id: "gift_gold_200_v1", nameKey: "gift.gold_200.name", description: "", rewards: [gold()] });

export function ActivityPage() {
  const [key, setKey] = useState(() => sessionStorage.getItem("activityPublishKey") ?? "");
  const [target, setTarget] = useState("Editor");
  const [catalog, setCatalog] = useState<Catalog>();
  const [rows, setRows] = useState<Row[]>([]);
  const [gifts, setGifts] = useState<GiftRow[]>([]);
  const [active, setActive] = useState<Row>();
  const [draft, setDraft] = useState<Definition>(fresh());
  const [giftRow, setGiftRow] = useState<GiftRow>();
  const [gift, setGift] = useState<Gift>(defaultGift());
  const [history, setHistory] = useState<History[]>([]);
  const [tab, setTab] = useState("activities");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [filterType, setFilterType] = useState(-1);
  const [filterStatus, setFilterStatus] = useState(-1);
  const [filterMode, setFilterMode] = useState(-1);
  const [filterTime, setFilterTime] = useState("all");
  const [copyId, setCopyId] = useState("");
  const dirty = active !== undefined && JSON.stringify(draft) !== JSON.stringify(JSON.parse(active.draftJson));
  const api = <T,>(path: string, method = "GET", data?: unknown) => request<T>("/api/admin/activities" + path,
    { method, headers: { "X-Content-Publish-Key": key }, ...(data === undefined ? {} : { body: JSON.stringify(data) }) });
  async function run(action: () => Promise<void>) {
    setBusy(true); setMessage("");
    try { await action(); } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  }
  async function load() {
    sessionStorage.setItem("activityPublishKey", key);
    const [list, giftList, published] = await Promise.all([api<Row[]>("/"), api<GiftRow[]>("/gifts"), api<Catalog>("/translations/" + target)]);
    setRows(list); setGifts(giftList); setCatalog(published);
  }
  async function createExamples() {
    if (!catalog) throw new Error("请先载入已发布文案目录");
    const response = await fetch("/activity-examples.json");
    if (!response.ok) throw new Error("示例配置读取失败");
    const examples = await response.json() as { gifts: Gift[]; activities: Definition[] };
    for (const sample of examples.gifts) if (!gifts.some(x => x.id === sample.id))
      await api<GiftRow>("/gifts/" + sample.id, "PUT", { definition: sample, expectedVersion: 0 });
    for (const definition of examples.activities) if (!rows.some(x => x.id === definition.id))
      await api<Row>("/", "POST", { target, configHash: catalog.configHash, expectedVersion: 0, definition });
    await load(); setMessage("计划书示例草稿已创建。先发布国庆见面礼，再发布跳转公告；其他活动可分别发布。");
  }
  const patch = (value: Partial<Definition>) => setDraft({ ...draft, ...value });
  const patchEntry = (i: number, value: Partial<Entry>) => patch({ entries: draft.entries.map((e, n) => n === i ? { ...e, ...value } : e) });
  const selectRow = (row: Row) => { setActive(row); setDraft(JSON.parse(row.draftJson) as Definition); setHistory([]); setCopyId(row.id + "_copy"); };
  function keyField(label: string, value: string, change: (value: string) => void, optional = false) {
    const preview = catalog?.entries.find(x => x.key === value);
    return <label className="activity-key">{label}<input list="activity-name-keys" value={value} onChange={e => change(e.target.value)} placeholder={optional ? "可选文案 key" : "选择已发布名称 key"} />
      {preview ? <span className="name-preview">中文：{preview.chinese}<br />English: {preview.english}</span> : value ? <span className="notice">该 key 尚未发布到当前内容目标</span> : null}</label>;
  }
  function resourceField(label: string, value: string, change: (v: string) => void) {
    return <label>{label}<select value={value} onChange={e => change(e.target.value)}><option value="">默认类型图标</option>{catalog?.resources.map(x => <option key={x}>{x}</option>)}</select></label>;
  }
  function changeType(type: number) {
    patch({ type, entries: type === 0 ? [] : type === 2 ? [entry(1), entry(2), entry(3)].map(e => ({ ...e, costGold: 0 })) : [{ ...entry(1), costGold: type === 3 ? 800 : 0 }], displayMode: type === 0 ? 1 : 0 });
  }
  async function save() {
    if (!catalog) throw new Error("请先载入目标内容的已发布文案目录");
    const row = await api<Row>(active ? "/" + active.id : "/", active ? "PUT" : "POST", { target, configHash: catalog.configHash, expectedVersion: active?.revision ?? 0, definition: draft });
    selectRow(row); await load(); setMessage("完整草稿已保存；玩家继续读取已发布版本。");
  }
  async function operate(action: string) {
    if (!active) throw new Error("请先保存草稿");
    const row = await api<Row>(`/${active.id}/${action}`, "POST", { expectedVersion: active.revision });
    selectRow(row); await load(); setMessage(action === "publish" ? "活动新版本已发布。" : "活动已下架，玩家记录保留。");
  }
  function rewardEditor(reward: Reward, i: number) {
    const update = (v: Partial<Reward>) => setGift({ ...gift, rewards: gift.rewards.map((r, n) => n === i ? { ...r, ...v } : r) });
    return <div className="reward-editor" key={i}>
      <label>奖励类型<select value={reward.rewardType} onChange={e => update({ rewardType: +e.target.value, rewardId: +e.target.value === 1 ? "gold" : "" })}><option value={1}>金币</option><option value={2}>卡牌</option></select></label>
      {reward.rewardType === 2 && <><label>已发布卡牌<input list="activity-card-ids" value={reward.rewardId} onChange={e => update({ rewardId: e.target.value })} /></label><label>卡牌版本<select value={reward.cardVariant} onChange={e => update({ cardVariant: +e.target.value })}>{[0,1,2,3,4].map(x => <option key={x} value={x}>{rarityLabel(x)}</option>)}</select></label></>}
      <label>数量<input type="number" min={1} value={reward.amount} onChange={e => update({ amount: +e.target.value })} /></label>
      <button onClick={() => setGift({ ...gift, rewards: gift.rewards.filter((_, n) => n !== i) })}>删除奖励</button>
    </div>;
  }
  return <main className="page activity-page">
    <h1>活动管理</h1><p className="muted">名称与图片从目标内容目录选择；保存草稿后发布完整版本。领取记录产生后，奖励、成本和档位身份冻结。</p>
    <div className="toolbar"><label>发布密钥 <input type="password" value={key} onChange={e => setKey(e.target.value)} autoComplete="off" /></label>
      <select value={target} onChange={e => { setTarget(e.target.value); setCatalog(undefined); setActive(undefined); }}>{["Editor", "Android", "StandaloneWindows64", "iOS"].map(x => <option key={x}>{x}</option>)}</select>
      <button onClick={() => void run(load)} disabled={busy}>载入活动与已发布文案</button><button disabled={busy || !catalog} onClick={() => void run(createExamples)}>创建计划书示例草稿</button></div>
    {message && <p className="notice" role="status">{message}</p>}
    <nav className="nav"><button className={tab === "activities" ? "active" : ""} onClick={() => setTab("activities")}>活动列表与编辑</button><button className={tab === "gifts" ? "active" : ""} onClick={() => setTab("gifts")}>礼包定义</button></nav>
    <datalist id="activity-name-keys">{catalog?.entries.map(x => <option key={x.key} value={x.key}>{x.chinese} / {x.english}</option>)}</datalist>
    <datalist id="activity-card-ids">{catalog?.cards.map(x => <option key={x} value={x} />)}</datalist>
    {tab === "gifts" ? <div className="activity-columns"><aside>
      <button onClick={() => { setGiftRow(undefined); setGift(defaultGift()); }}>新建礼包</button>
      {gifts.map(row => <button key={row.id} className="activity-row" onClick={() => { setGiftRow(row); setGift(JSON.parse(row.definitionJson) as Gift); }}>{row.id}<small>{row.frozen ? "已冻结" : "可编辑"} · 修订 {row.revision}</small></button>)}
    </aside><section className="activity-editor"><h2>礼包奖励</h2>
      <label>礼包 ID<input disabled={!!giftRow} value={gift.id} onChange={e => setGift({ ...gift, id: e.target.value })} /></label>
      {keyField("礼包 NameKey", gift.nameKey, nameKey => setGift({ ...gift, nameKey }))}
      <label>说明<textarea value={gift.description} onChange={e => setGift({ ...gift, description: e.target.value })} /></label>
      <fieldset disabled={giftRow?.frozen || busy}>{gift.rewards.map(rewardEditor)}<button onClick={() => setGift({ ...gift, rewards: [...gift.rewards, gold()] })}>增加奖励</button></fieldset>
      <div className="toolbar"><button disabled={busy || giftRow?.frozen} onClick={() => void run(async () => { const row = await api<GiftRow>("/gifts/" + gift.id, "PUT", { definition: gift, expectedVersion: giftRow?.revision ?? 0 }); setGiftRow(row); await load(); setMessage("礼包定义已保存。"); })}>保存礼包</button>
        <button onClick={() => { setGiftRow(undefined); setGift({ ...gift, id: gift.id + "_v2" }); }}>复制为新礼包</button></div>
    </section></div> : <>
      <div className="toolbar activity-filters"><button onClick={() => { setActive(undefined); setDraft(fresh()); setHistory([]); }}>新建活动</button>
        <select value={filterType} onChange={e => setFilterType(+e.target.value)}><option value={-1}>所有类型</option>{TYPES.map((x, i) => <option key={x} value={i}>{x}</option>)}</select>
        <select value={filterStatus} onChange={e => setFilterStatus(+e.target.value)}><option value={-1}>所有发布状态</option>{STATUS.map((x, i) => <option key={x} value={i}>{x}</option>)}</select>
        <select value={filterMode} onChange={e => setFilterMode(+e.target.value)}><option value={-1}>所有展示方式</option>{MODES.map((x, i) => <option key={x} value={i}>{x}</option>)}</select>
        <select value={filterTime} onChange={e => setFilterTime(e.target.value)}><option value="all">所有排期</option><option value="running">正在开放</option><option value="upcoming">尚未开始</option><option value="ended">已经结束</option></select>
      </div><div className="activity-columns"><aside>
        {rows.filter(row => row.target === target).map(row => ({ row, data: JSON.parse(row.draftJson) as Definition })).filter(({ row, data }) => (filterType < 0 || data.type === filterType) && (filterStatus < 0 || row.publishStatus === filterStatus) && (filterMode < 0 || data.displayMode === filterMode) && (filterTime === "all" || (data.scheduleMode === 0 ? "running" : Date.now() < Date.parse(data.startsAt!) ? "upcoming" : Date.now() >= Date.parse(data.endsAt!) ? "ended" : "running") === filterTime)).map(({ row, data }) => <button key={row.id} className={"activity-row " + (active?.id === row.id ? "selected" : "")} onClick={() => selectRow(row)}>{catalog?.entries.find(x => x.key === data.nameKey)?.chinese ?? data.nameKey}<small>{row.id}<br />{TYPES[data.type]} · {MODES[data.displayMode]}<br />{STATUS[row.publishStatus]} · 发布 v{row.activeVersion} / 草稿 r{row.revision}<br />{data.scheduleMode === 0 ? "长期开放" : `${data.startsAt} ～ ${data.endsAt}`}</small></button>)}
      </aside><section className="activity-editor"><fieldset disabled={busy}>
        <h2>基本信息</h2><div className="form-grid"><label>活动 ID<input value={draft.id} disabled={!!active} onChange={e => patch({ id: e.target.value })} /></label><label>类型<select value={draft.type} disabled={(active?.activeVersion ?? 0) > 0} onChange={e => changeType(+e.target.value)}>{TYPES.map((x, i) => <option key={x} value={i}>{x}</option>)}</select></label></div>
        {keyField("活动 NameKey", draft.nameKey, nameKey => patch({ nameKey }))}
        {keyField("说明文案 key", draft.descriptionKey, descriptionKey => patch({ descriptionKey }), true)}
        <label>活动说明<textarea value={draft.description} onChange={e => patch({ description: e.target.value })} /></label>
        <h2>排期与参与条件</h2><label>排期<select value={draft.scheduleMode} onChange={e => patch(+e.target.value === 0 ? { scheduleMode: 0, startsAt: null, endsAt: null } : { scheduleMode: 1, startsAt: "2026-10-01T00:00:00+08:00", endsAt: "2026-10-08T00:00:00+08:00" })}><option value={0}>长期开放</option><option value={1}>定时活动</option></select></label>
        {draft.scheduleMode === 1 && <div className="form-grid"><label>开始时间（ISO 8601，含时区）<input value={draft.startsAt ?? ""} onChange={e => patch({ startsAt: e.target.value })} /></label><label>结束时间（不含该时刻）<input value={draft.endsAt ?? ""} onChange={e => patch({ endsAt: e.target.value })} /></label></div>}
        {draft.openConditions.allOf.map((c, i) => { const update = (v: Partial<Condition>) => patch({ openConditions: { allOf: draft.openConditions.allOf.map((x, n) => n === i ? { ...x, ...v } : x) } }); return <div className="reward-editor" key={i}><select value={c.type} onChange={e => update({ type: e.target.value })}><option value="ownedCardKindsAtLeast">至少持有卡牌种类</option><option value="activityCompleted">前置活动完成</option><option value="playerCreatedBefore">注册早于时间</option><option value="playerCreatedAfter">注册不早于时间</option></select>
          {c.type === "ownedCardKindsAtLeast" ? <input type="number" min={0} value={c.value} onChange={e => update({ value: +e.target.value })} /> : c.type === "activityCompleted" ? <select value={c.activityId} onChange={e => update({ activityId: e.target.value })}><option value="">选择前置活动</option>{rows.filter(x => x.publishStatus === 1 && x.id !== draft.id && x.target === target).map(x => <option key={x.id}>{x.id}</option>)}</select> : <input value={c.time ?? ""} placeholder="2026-10-01T00:00:00+08:00" onChange={e => update({ time: e.target.value })} />}
          <button onClick={() => patch({ openConditions: { allOf: draft.openConditions.allOf.filter((_, n) => n !== i) } })}>删除条件</button></div>; })}
        <button onClick={() => patch({ openConditions: { allOf: [...draft.openConditions.allOf, { type: "ownedCardKindsAtLeast", value: 1, activityId: "", time: null }] } })}>增加条件（全部满足）</button>
        <h2>展示设置</h2><div className="form-grid"><label>展示方式<select value={draft.displayMode} onChange={e => patch({ displayMode: +e.target.value })}>{MODES.map((x, i) => <option key={x} value={i}>{x}</option>)}</select></label><label>页面排序<input type="number" value={draft.sortOrder} onChange={e => patch({ sortOrder: +e.target.value })} /></label>{resourceField("活动图片", draft.bannerResourceKey, bannerResourceKey => patch({ bannerResourceKey }))}</div>
        <div className="activity-checks">{([['showBeforeStart','开始前展示'],['hideWhenCompleted','完成后隐藏'],['showLocked','条件未满足时展示']] as const).map(([field,label]) => <label key={field}><input type="checkbox" checked={draft[field]} onChange={e => patch({ [field]: e.target.checked })} />{label}</label>)}</div>
        {draft.displayMode !== 0 && <div className="form-grid"><label>弹窗触发<select value={draft.popup.trigger} onChange={e => patch({ popup: { ...draft.popup, trigger: e.target.value } })}><option value="lobbyReady">大厅加载完成</option><option value="rewardClaimable">有可领取奖励</option></select></label><label>频率<select value={draft.popup.frequency} onChange={e => patch({ popup: { ...draft.popup, frequency: e.target.value } })}><option value="oncePerActivity">每活动一次</option><option value="oncePerDay">每天一次（北京时间）</option></select></label><label>优先级<input type="number" value={draft.popup.priority} onChange={e => patch({ popup: { ...draft.popup, priority: +e.target.value } })} /></label><label>提醒策略版本<input type="number" min={1} value={draft.popup.policyVersion} onChange={e => patch({ popup: { ...draft.popup, policyVersion: +e.target.value } })} /></label><label><input type="checkbox" checked={draft.popup.stopWhenCompleted} onChange={e => patch({ popup: { ...draft.popup, stopWhenCompleted: e.target.checked } })} />完成后停止提醒</label></div>}
        <h2>玩法配置</h2>
        {draft.type === 0 ? <><label>公告内容<textarea rows={6} value={draft.notice.content} onChange={e => patch({ notice: { ...draft.notice, content: e.target.value } })} /></label>{resourceField("公告图片", draft.notice.imageResourceKey, imageResourceKey => patch({ notice: { ...draft.notice, imageResourceKey } }))}<label>公告操作<select value={draft.notice.actionKind} onChange={e => patch({ notice: { ...draft.notice, actionKind: e.target.value } })}><option value="close">关闭</option><option value="activity">前往活动页面</option></select></label>{draft.notice.actionKind === "activity" && <label>跳转活动<select value={draft.notice.actionTarget} onChange={e => patch({ notice: { ...draft.notice, actionTarget: e.target.value } })}><option value="">请选择</option>{rows.filter(x => x.target === target && x.publishStatus === 1).map(x => <option key={x.id}>{x.id}</option>)}</select></label>}</> : <>
          {draft.type === 2 && <div className="form-grid"><label>累计登录天数<input type="number" min={1} value={draft.requiredDays} onChange={e => { const n = +e.target.value; patch({ requiredDays: n, entries: Array.from({ length: Math.max(1, Math.min(60,n)) }, (_, i) => draft.entries[i] ?? { ...entry(i+1), costGold: 0 }) }); }} /></label><label><input type="checkbox" checked={draft.allowCatchUpClaims} onChange={e => patch({ allowCatchUpClaims: e.target.checked })} />允许补领已解锁档位</label></div>}
          {draft.type === 4 && <div className="activity-checks"><span>累计成功抽卡，包含以下卡包：</span>{catalog?.packs.map(p => <label key={p.id}><input type="checkbox" checked={draft.packIds.includes(p.id)} onChange={e => patch({ packIds: e.target.checked ? [...draft.packIds,p.id] : draft.packIds.filter(x => x !== p.id) })} />{p.title} ({p.id})</label>)}</div>}
          {draft.entries.map((e, i) => { const g = gifts.find(x => x.id === e.giftId); const reward = g ? JSON.parse(g.definitionJson) as Gift : undefined; return <div className="entry-editor" key={i}><div className="form-grid"><label>领取项 ID<input value={e.id} onChange={v => patchEntry(i,{id:v.target.value})} /></label><label>引用礼包<select value={e.giftId} onChange={v => { if (v.target.value === '') { patchEntry(i,{giftId:'',nameKey:''}); return; } const giftData = JSON.parse(gifts.find(x => x.id === v.target.value)!.definitionJson) as Gift; patchEntry(i,{giftId:v.target.value,nameKey:giftData.nameKey}); }}><option value="">先在礼包定义中创建奖励</option>{gifts.map(x => <option key={x.id}>{x.id}</option>)}</select></label></div>
            {draft.type === 3 && keyField("商品 NameKey",e.nameKey,nameKey => patchEntry(i,{nameKey}))}
            <div className="reward-preview">{reward ? reward.rewards.map((r,n) => <span key={n}>{r.rewardType === 1 ? "金币" : `卡牌 ${r.rewardId} · ${rarityLabel(r.cardVariant)}`} × {r.amount}</span>) : "请选择礼包以预览奖励"}</div>
            <div className="form-grid">{draft.type === 2 && <label>解锁登录日<input type="number" min={1} value={e.dayIndex} onChange={v => patchEntry(i,{dayIndex:+v.target.value})} /></label>}{draft.type === 4 && <label>抽卡次数门槛<input type="number" min={1} value={e.threshold} onChange={v => patchEntry(i,{threshold:+v.target.value})} /></label>}{draft.type === 3 && <label>兑换金币成本<input type="number" min={1} value={e.costGold} onChange={v => patchEntry(i,{costGold:+v.target.value})} /></label>}
              {(draft.type === 1 || draft.type === 3) && <><label>计数周期<select value={e.periodKind} onChange={v => patchEntry(i,{periodKind:+v.target.value,totalLimit:+v.target.value === 1 ? null : 1})}><option value={0}>整个活动</option><option value={1}>每日（北京时间）</option></select></label><label>每周期上限<input type="number" min={1} value={e.limitPerPeriod} onChange={v => patchEntry(i,{limitPerPeriod:+v.target.value})} /></label><label>总上限（空表示不限）<input type="number" min={1} value={e.totalLimit ?? ""} onChange={v => patchEntry(i,{totalLimit:v.target.value === "" ? null : +v.target.value})} /></label></>}
              <label>排序<input type="number" value={e.sortOrder} onChange={v => patchEntry(i,{sortOrder:+v.target.value})} /></label></div>
            <label>领取项说明<input value={e.description} onChange={v => patchEntry(i,{description:v.target.value})} /></label>
            {draft.type !== 2 && <button onClick={() => patch({entries:draft.entries.filter((_,n) => n !== i)})}>删除领取项</button>}</div>; })}
          {draft.type !== 2 && <button onClick={() => patch({entries:[...draft.entries,{...entry(draft.entries.length+1),costGold:draft.type === 3 ? 800 : 0}]})}>增加领取项</button>}
        </>}
      </fieldset><div className="toolbar"><button className="primary" disabled={busy || !catalog} onClick={() => void run(save)}>保存完整草稿</button><button disabled={busy || !active || dirty} onClick={() => void run(() => operate("publish"))}>发布新版本</button><button disabled={busy || active?.publishStatus !== 1} onClick={() => void run(() => operate("disable"))}>下架</button><button disabled={busy || !active} onClick={() => void run(async () => setHistory(await api<History[]>(`/${active!.id}/history`)))}>发布历史</button></div>
      {dirty && <p className="muted">有未保存的修改，请先保存完整草稿再发布。</p>}{active && <div className="toolbar"><input aria-label="复制后的活动 ID" value={copyId} onChange={e => setCopyId(e.target.value)} /><button disabled={busy} onClick={() => void run(async () => { const row = await api<Row>(`/${active.id}/copy`, "POST", {newId:copyId}); selectRow(row); await load(); })}>复制为新活动</button></div>}
      {history.length > 0 && <table><thead><tr><th>版本</th><th>操作</th><th>操作者</th><th>UTC 时间</th></tr></thead><tbody>{history.map(x => <tr key={x.version}><td>{x.version}</td><td>{x.action}</td><td>{x.actor}</td><td>{x.publishedAt}</td></tr>)}</tbody></table>}
      </section></div></>}
  </main>;
}
