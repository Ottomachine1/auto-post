import React, { useCallback, useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  QueryClient,
  QueryClientProvider,
  useInfiniteQuery,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import * as Dialog from "@radix-ui/react-dialog";
import {
  Activity,
  ArrowUpRight,
  Check,
  ChevronRight,
  Clock,
  Database,
  FileText,
  Globe,
  Layers,
  Pause,
  Play,
  Plus,
  Radio,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Sparkles,
  Star,
  X,
  Zap,
} from "lucide-react";
import {
  api,
  session,
  type EventItem,
  type Draft,
  type Rule,
  type Source,
} from "./api";
import { useRealtime } from "./realtime";
import "./style.css";
import {Composer,MediaGrid} from "./Composer";
import {AgentPanel} from "./AgentPanel";
import type {Attachment} from "./api";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
});
const statusName: Record<string, string> = {
  draft: "待审核",
  approved: "已审核",
  published: "已发布",
  partial: "需处理",
  queued: "排队中",
  sending: "发送中",
  unknown: "待核验",
  failed: "失败",
  blocked: "已拦截",
  manual_required: "人工导出",
  pending: "待处理",
  running: "执行中",
  done: "完成",
  completed: "分析完成",
  connected: "在线",
  error: "异常",
};
const time = (stamp: number) =>
  new Date(stamp * 1000).toLocaleString("zh-CN", {
    timeZone: "Asia/Shanghai",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
const safeUrl = (url: string) => /^https?:\/\//i.test(url);
const demo: EventItem[] = [
  ["全球宏观", "美联储政策观察：风险资产如何响应利率预期？", "宏观"],
  ["加密市场", "比特币生态观察：网络活动与资金流向", "加密"],
  ["X 社交", "AI 与加密基础设施：追踪关键叙事的变化", "社交"],
  ["全球能源", "能源供需变化与市场传导路径", "宏观"],
].map(([source, title, category], i) => ({
  id: "preview-" + i,
  source,
  title,
  category,
  body: "这是布局演示样例，不代表当前新闻。连接工作空间并配置授权数据源后，将显示真实事件、来源与采集时间。",
  url: "",
  published_at: Math.floor(Date.now() / 1000) - i * 180,
  collected_at: Math.floor(Date.now() / 1000),
  demo: true,
  groupId: "preview-" + i,
  analysisStatus: "pending",
}));
function Modal({
  open,
  onOpen,
  title,
  description,
  children,
}: {
  open: boolean;
  onOpen: (v: boolean) => void;
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpen}>
      <Dialog.Portal>
        <Dialog.Overlay className="overlay" />
        <Dialog.Content className="modal">
          <div className="row between">
            <Dialog.Title>{title}</Dialog.Title>
            <Dialog.Close className="icon" aria-label="关闭">
              <X size={18} />
            </Dialog.Close>
          </div>
          <Dialog.Description className="muted">
            {description}
          </Dialog.Description>
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
function App() {
  const qc = useQueryClient();
  const [connected, setConnected] = useState(false),
    [page, setPage] = useState("feed"),
    [selected, setSelected] = useState<EventItem | null>(null),
    [search, setSearch] = useState(""),
    [source, setSource] = useState(""),
    [category, setCategory] = useState(""),
    [paused, setPaused] = useState(false),
    [news, setNews] = useState(false),
    [error, setError] = useState(""),
    [login, setLogin] = useState(false),
    [token, setToken] = useState(""),
    [busy, setBusy] = useState(false),
    [editor, setEditor] = useState(false),
    [editing, setEditing] = useState<Draft | null>(null),
    [content, setContent] = useState(""),
    [channels, setChannels] = useState(["telegram"]),
    [preview, setPreview] = useState<any[]>([]),
    [ruleEditor, setRuleEditor] = useState(false),
    [rule, setRule] = useState<Rule>(blankRule()),
    [sourceEditor, setSourceEditor] = useState(false),
    [sourceInput, setSourceInput] = useState<Partial<Source>>({
      kind: "rss",
      name: "",
      address: "",
      enabled: false,
      intervalSeconds: 60,
    }),
    [watch, setWatch] = useState<string[]>(() => {
      try {
        return JSON.parse(localStorage.getItem("atlas-watch-v2") || "[]");
      } catch {
        return [];
      }
    }),
    [now, setNow] = useState(new Date());
  useEffect(() => {
    session()
      .then(() => setConnected(true))
      .catch(() => {});
    const timer = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);
  const refresh = useCallback(() => {
    if (paused) setNews(true);
    else void qc.invalidateQueries({ queryKey: ["events"] });
    void qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== "events" });
  }, [paused, qc]);
  const realtime = useRealtime(connected, refresh);
  const status = useQuery({
    queryKey: ["status"],
    queryFn: () => api("/status"),
    enabled: connected,
    refetchInterval: 15000,
  });
  const sources: Source[] = status.data?.sources || [];
  const feed = useInfiniteQuery({
    queryKey: ["events", source, search, category],
    queryFn: ({ pageParam }) =>
      api<{ items: EventItem[]; nextCursor: string | null }>(
        "/events/page?" +
          new URLSearchParams({
            source,
            q: search,
            category,
            ...(pageParam ? { cursor: pageParam } : {}),
          }),
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (p) => p.nextCursor,
    enabled: connected,
  });
  const [media,setMedia]=useState<Attachment[]>([]),[uploading,setUploading]=useState(false);
  const drafts = useQuery({
    queryKey: ["drafts"],
    queryFn: () => api<Draft[]>("/drafts"),
    enabled: connected,
  });
  const rules = useQuery({
    queryKey: ["rules"],
    queryFn: () => api<Rule[]>("/rules"),
    enabled: connected && page === "rules",
  });
  const detail = useQuery({
    queryKey: ["detail", selected?.id],
    queryFn: () => api("/events/" + selected!.id),
    enabled: connected && !!selected && !selected.demo,
  });
  const tasks = useQuery({
    queryKey: ["tasks"],
    queryFn: () => api<any[]>("/tasks"),
    enabled: connected && page === "system",
  });
  const audit = useQuery({
    queryKey: ["audit"],
    queryFn: () => api<any[]>("/audit"),
    enabled: connected && page === "system",
  });
  const rows = (
    connected
      ? feed.data?.pages.flatMap((p) => p.items) || []
      : demo.filter(
          (e) =>
            (!search || (e.title + e.body).includes(search)) &&
            (!category || e.category === category),
        )
  ).filter((e) => page !== "watch" || watch.includes(e.id));
  const scroll = useRef<HTMLDivElement>(null);
  const virtual = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroll.current,
    estimateSize: () => 156,
    overscan: 5,
  });
  async function action(work: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try {
      await work();
      refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  function compose(d?: Draft, text = "", event?: EventItem) {
    setMedia(d?.media || []); setUploading(false);
    setEditing(d || null);
    setContent(d?.content || text);
    setChannels(d?.channels || ["x"]);
    setPreview([]);
    if (event) setSelected(event);
    setEditor(true);
  }
  function toggleWatch(id: string) {
    const list = watch.includes(id)
      ? watch.filter((x) => x !== id)
      : [...watch, id];
    setWatch(list);
    localStorage.setItem("atlas-watch-v2", JSON.stringify(list));
  }
  const analysis = detail.data?.analysis
    ? JSON.parse(detail.data.analysis.result)
    : null;
  const navigate = (p: string) => {
    setPage(p);
    setSelected(null);
  };
  return (
    <div className="app">
      <aside className="sidebar">
        <a href="#" className="brand" onClick={() => navigate("feed")}>
          <span className="brand-icon">
            <Layers size={23} />
          </span>
          <div>
            SIGNAL ATLAS<small>GLOBAL INTELLIGENCE</small>
          </div>
        </a>
        <div className="workspace">
          <span className="avatar">O</span>
          <div>
            Ottomachine<small>个人情报工作空间</small>
          </div>
          <ChevronRight size={14} />
        </div>
        <span className="nav-label">WORKSPACE</span>
        <nav>
          {[
            ["feed", Radio, "实时情报"],
            ["watch", Star, "我的关注"],
            ["drafts", FileText, "发布中心"],
            ["rules", Zap, "自动化规则"],
            ["sources", Database, "数据源与渠道"],
            ["system", Activity, "系统状态"],
          ].map(([id, Icon, label]) => (
            <button
              key={id as string}
              onClick={() => navigate(id as string)}
              className={page === id ? "active" : ""}
            >
              {React.createElement(Icon as typeof Radio, { size: 18 })}
              <span>{label as string}</span>
              {id === "feed" && <span className="live-mini">LIVE</span>}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <ShieldCheck size={20} />
          <strong>让每次发布都有依据</strong>
          <p>原始来源 · 版本审核 · 发布追踪</p>
          <button className="primary" onClick={() => compose()}>
            <Plus size={15} />
            人工创作
          </button>
        </div>
        <button className="connection" onClick={() => setLogin(true)}>
          <span className={"dot " + (connected ? "green" : "amber")} />
          {connected ? "工作空间已连接" : "连接工作空间"}
          <Settings2 size={16} />
        </button>
      </aside>
      <div className="shell">
        <header>
          <div className="breadcrumb">
            工作空间 <ChevronRight size={13} />
            <b>
              {
                {
                  feed: "实时情报",
                  watch: "我的关注",
                  drafts: "发布中心",
                  rules: "自动化规则",
                  sources: "数据源与渠道",
                  system: "系统状态",
                }[page]
              }
            </b>
          </div>
          <div className="row">
            <time>
              {now.toLocaleTimeString("zh-CN", {
                hour12: false,
                timeZone: "Asia/Shanghai",
              })}{" "}
              CST
            </time>
            <span className="badge">
              {status.data?.mode === "live" ? "LIVE" : "DEMO"}
            </span>
            <button
              className="avatar small"
              aria-label="账户连接"
              onClick={() => setLogin(true)}
            >
              O
            </button>
          </div>
        </header>
        <main>
          <div className="title-row">
            <div>
              <div className="eyebrow">INTELLIGENCE / SIGNAL ATLAS</div>
              <h1>
                {
                  {
                    feed: "全球信号，实时汇聚",
                    watch: "追踪值得关注的变化",
                    drafts: "从情报到表达",
                    rules: "让规则驱动发布",
                    sources: "连接你的信息网络",
                    system: "每个环节，清晰可见",
                  }[page]
                }
              </h1>
              <p className="muted">
                全球时事 · 加密叙事 · 社交脉搏 <span className="title-line" />{" "}
                来源可追溯，发布可掌控
              </p>
            </div>
            <button className="primary" onClick={() => compose()}>
              <Plus size={16} />
              创建内容
            </button>
          </div>
          {error && (
            <div role="alert" className="notice error">
              {error}
              <button
                className="icon"
                onClick={() => setError("")}
                aria-label="关闭提示"
              >
                <X size={14} />
              </button>
            </div>
          )}
          {(!connected || status.data?.mode === "demo") && (
            <div className="notice">
              <Globe size={17} />
              <span>演示工作空间 · 样例不代表真实新闻，对外发布已禁用</span>
              <button onClick={() => setLogin(true)}>
                连接后端 <ArrowUpRight size={14} />
              </button>
            </div>
          )}
          {(feed.error ||
            status.error ||
            drafts.error ||
            rules.error ||
            detail.error) && (
            <div className="notice error">
              {
                (
                  feed.error ||
                  status.error ||
                  drafts.error ||
                  rules.error ||
                  detail.error
                )?.message
              }
            </div>
          )}
          {["feed", "watch"].includes(page) && (
            <>
              <section className="stats">
                <Stat
                  label="情报事件"
                  value={connected ? status.data?.events || 0 : demo.length}
                  note="工作空间累计"
                  icon={<Globe size={17} />}
                />
                <Stat
                  label="关注信号"
                  value={watch.length}
                  note="人工收藏"
                  icon={<Star size={17} />}
                />
                <Stat
                  label="待审核草稿"
                  value={
                    drafts.data?.filter((d) => d.status === "draft").length || 0
                  }
                  note="审核后发布"
                  icon={<FileText size={17} />}
                />
                <Stat
                  label="数据源在线"
                  value={sources.filter((s) => s.status === "connected").length}
                  note={realtime}
                  icon={<Radio size={17} />}
                />
              </section>
              <section
                className={"terminal " + (selected ? "detail-open" : "")}
              >
                <div className="feed panel">
                  <div className="panel-title">
                    <h2>
                      <span className="dot green" />
                      实时事件流{" "}
                      <span className="muted">
                        {rows.length.toString().padStart(2, "0")}
                      </span>
                    </h2>
                    <div className="row">
                      <button
                        className="icon"
                        aria-label="刷新"
                        onClick={() => refresh()}
                      >
                        <RefreshCw size={15} />
                      </button>
                      <button
                        onClick={() => {
                          setPaused(!paused);
                          if (paused) {
                            setNews(false);
                            void qc.invalidateQueries({ queryKey: ["events"] });
                          }
                        }}
                      >
                        {paused ? <Play size={14} /> : <Pause size={14} />}{" "}
                        {paused ? "恢复" : "暂停"}
                      </button>
                    </div>
                  </div>
                  <div className="filter-row">
                    <div className="tabs">
                      {["", "宏观", "加密", "社交"].map((c) => (
                        <button
                          className={category === c ? "selected" : ""}
                          onClick={() => setCategory(c)}
                          key={c}
                        >
                          {c || "全部事件"}
                        </button>
                      ))}
                    </div>
                    <select
                      aria-label="筛选来源"
                      value={source}
                      onChange={(e) => setSource(e.target.value)}
                    >
                      <option value="">全部来源</option>
                      {sources.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                  </div>
                  <label className="search">
                    <Search size={16} />
                    <input
                      aria-label="搜索事件"
                      placeholder="搜索事件、人物、关键词…"
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                    <kbd>⌕</kbd>
                  </label>
                  {news && (
                    <button
                      className="new-events"
                      onClick={() => {
                        setNews(false);
                        void qc.invalidateQueries({ queryKey: ["events"] });
                      }}
                    >
                      有新变化 · 点击更新事件流
                    </button>
                  )}
                  <div className="column-label">
                    <span>事件 / EVENT</span>
                    <span>来源 · 时间</span>
                  </div>
                  <div ref={scroll} className="event-scroll">
                    {feed.isLoading ? (
                      <Empty title="正在同步事件…" />
                    ) : rows.length === 0 ? (
                      <Empty
                        title="没有匹配的事件"
                        note="配置来源或调整筛选条件。"
                      />
                    ) : (
                      <div
                        style={{
                          height: virtual.getTotalSize(),
                          position: "relative",
                        }}
                      >
                        {virtual.getVirtualItems().map((v) => {
                          const e = rows[v.index];
                          return (
                            <article
                              key={e.id}
                              data-index={v.index}
                              ref={virtual.measureElement}
                              className={
                                "event " +
                                (selected?.id === e.id ? "chosen" : "")
                              }
                              style={{
                                position: "absolute",
                                top: 0,
                                left: 0,
                                width: "100%",
                                transform: `translateY(${v.start}px)`,
                              }}
                            >
                              <button
                                className="event-main"
                                onClick={() => setSelected(e)}
                              >
                                <span
                                  className={
                                    "source-icon " +
                                    (e.category === "加密" ? "crypto" : "")
                                  }
                                >
                                  {e.category === "加密"
                                    ? "₿"
                                    : e.category === "社交"
                                      ? "𝕏"
                                      : "◎"}
                                </span>
                                <div>
                                  <div className="event-meta">
                                    <span>
                                      {sources.find((s) => s.id === e.source)
                                        ?.name || e.source}
                                    </span>
                                    <span>·</span>
                                    <span>{time(e.published_at)}</span>
                                    <span className="tag">{e.category}</span>
                                    {e.demo && (
                                      <span className="sample">DEMO</span>
                                    )}
                                  </div>
                                  <h3>{e.title}</h3>
                                  <p>{e.body}</p>
                                  <div className="event-footer">
                                    <span>
                                      {statusName[e.analysisStatus] || "待分析"}
                                    </span>
                                    <span>
                                      原始来源 <ArrowUpRight size={11} />
                                    </span>
                                  </div>
                                </div>
                              </button>
                              <button
                                className={
                                  "watch " +
                                  (watch.includes(e.id) ? "saved" : "")
                                }
                                aria-label="关注事件"
                                onClick={() => toggleWatch(e.id)}
                              >
                                <Star
                                  size={16}
                                  fill={
                                    watch.includes(e.id)
                                      ? "currentColor"
                                      : "none"
                                  }
                                />
                              </button>
                            </article>
                          );
                        })}
                      </div>
                    )}
                  </div>
                  {feed.hasNextPage && (
                    <button
                      className="load-more"
                      disabled={feed.isFetchingNextPage}
                      onClick={() => void feed.fetchNextPage()}
                    >
                      加载更早事件
                    </button>
                  )}
                  <div className="feed-footer">
                    <span>
                      <span className="dot green" />
                      {realtime}
                    </span>
                    <span>保留来源与时间戳</span>
                  </div>
                </div>
                <aside className="insights">
                  <div className="panel">
                    <div className="panel-title">
                      <h2>
                        <Sparkles size={17} className="accent" />
                        情报解读
                      </h2>
                      {selected && (
                        <button
                          className="icon"
                          onClick={() => setSelected(null)}
                          aria-label="返回事件列表"
                        >
                          <X size={16} />
                        </button>
                      )}
                      <span className="tiny">COPILOT</span>
                    </div>
                    <div className="insight-content">
                      {!selected ? (
                        <>
                          <div className="splash">
                            <Sparkles size={34} />
                          </div>
                          <span className="eyebrow">
                            CONTEXT BEFORE CONTENT
                          </span>
                          <h3>每个信号，都值得追问</h3>
                          <p className="muted">
                            选择事件查看原始来源、事实依据与影响路径，再将洞察转化为内容。
                          </p>
                          <div className="chips">
                            <span>来源溯源</span>
                            <span>影响推演</span>
                            <span>发布追踪</span>
                          </div>
                        </>
                      ) : (
                        <>
                          <span className="eyebrow">SOURCE CONTEXT</span>
                          <h3>{selected.title}</h3>
                          <p className="muted">{selected.body}</p>
                          <p className="meta">
                            {selected.publisher ||
                              sources.find((s) => s.id === selected.source)
                                ?.name ||
                              selected.source}{" "}
                            · {selected.language || "und"} ·{" "}
                            {selected.author || "作者未标注"}
                            <br />
                            发布时间 {time(selected.published_at)}{" "}
                            {selected.publishedEstimated ? "（估计）" : ""}
                            <br />
                            采集时间 {time(selected.collected_at)}
                          </p>
                          {safeUrl(selected.url) && (
                            <a
                              className="accent row"
                              href={selected.url}
                              target="_blank"
                              rel="noreferrer"
                            >
                              查看原始来源 <ArrowUpRight size={14} />
                            </a>
                          )}
                          <h4>关联来源 · 独立性待人工核验</h4>
                          <p className="meta">
                            相似报道与转载归组仅用于去重，不作为已核实证据。
                          </p>
                          {detail.data?.related?.map((r: EventItem) => (
                            <p key={r.id} className="meta">
                              {safeUrl(r.url) ? (
                                <a
                                  href={r.url}
                                  target="_blank"
                                  rel="noreferrer"
                                >
                                  {r.publisher || r.source} · {r.title}
                                </a>
                              ) : (
                                r.title
                              )}{" "}
                              · {r.relation || "unverified_report"}
                            </p>
                          ))}
                          {analysis ? (
                            <>
                              <h4>
                                核心摘要 · 重要性 {analysis.importance ?? "—"}
                                /100
                              </h4>
                              <p className="meta">
                                类型 {analysis.claimType || "media_report"} ·
                                核验 {analysis.verification || "unverified"}
                              </p>
                              <p>{analysis.summary}</p>
                              {[
                                ["来源声明的事实", analysis.facts],
                                ["媒体报道", analysis.reports],
                                ["预测", analysis.predictions],
                                ["市场传闻", analysis.rumors],
                                ["事实依据", analysis.evidence],
                                ["影响路径 · 推测", analysis.implications],
                                ["不确定性", analysis.uncertainties],
                              ].map(([title, list]) => (
                                <div key={title}>
                                  <h4>{title}</h4>
                                  <ul>
                                    {list?.map((text: string, i: number) => (
                                      <li key={i}>{text}</li>
                                    ))}
                                  </ul>
                                </div>
                              ))}
                              <p className="meta">
                                {detail.data.analysis.model} ·{" "}
                                {detail.data.analysis.durationMs}ms ·{" "}
                                {detail.data.analysis.promptVersion}
                              </p>
                              <button
                                className="primary"
                                onClick={() =>
                                  compose(undefined, analysis.draft, selected)
                                }
                              >
                                编辑候选草稿 <ChevronRight size={15} />
                              </button>
                            </>
                          ) : (
                            <button
                              disabled={busy || !connected || selected.demo}
                              onClick={() =>
                                void action(() =>
                                  api(
                                    "/events/" + selected.id + "/analysis",
                                    "POST",
                                  ),
                                )
                              }
                            >
                              <Sparkles size={15} />
                              加入分析队列
                            </button>
                          )}
                          <button
                            className="wide"
                            onClick={() =>
                              compose(
                                undefined,
                                selected.title + "\n\n" + selected.body,
                                selected,
                              )
                            }
                          >
                            基于事件人工创作
                          </button>
                          {detail.data?.related?.length > 1 && (
                            <div className="related">
                              <h4>关联来源 · {detail.data.related.length}</h4>
                              {detail.data.related.map((e: EventItem) => (
                                <p key={e.id}>{e.title}</p>
                              ))}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                  <div className="panel radar">
                    <div className="panel-title">
                      <h2>工作流水线</h2>
                      <span className="tiny">PIPELINE</span>
                    </div>
                    {[
                      ["01", "采集与归组", "保留原始依据"],
                      ["02", "模型分析", "区分事实与推测"],
                      ["03", "审核与规则", "由你控制授权"],
                      ["04", "渠道投递", "核验实际结果"],
                    ].map(([n, t, d]) => (
                      <div className="radar-row" key={n}>
                        <span>{n}</span>
                        <div>
                          {t}
                          <small>{d}</small>
                        </div>
                        <ChevronRight size={14} />
                      </div>
                    ))}
                  </div>
                </aside>
              </section>
            </>
          )}
          {page === "drafts" && (
            <div className="cards">
              {!drafts.data?.length && (
                <Empty
                  title="还没有草稿"
                  note="点击创建内容，或等待真实事件完成模型分析。"
                />
              )}
              {drafts.data?.map((d) => (
                <div className="panel content-card" key={d.id}>
                  <div className="row between">
                    <span className={"pill " + d.status}>
                      {d.quarantined ? "隔离区" : statusName[d.status]}
                    </span>
                    <span className="meta">
                      版本 {d.revision} · {d.channels.join(" / ")}
                    </span>
                  </div>
                  <p className="draft-text">{d.content}</p><MediaGrid media={d.media||[]}/>
                  {d.deliveries.map((del) => (
                    <div className="delivery" key={del.id}>
                      <span>
                        {del.channel} · v{del.revision}
                      </span>
                      <span>{statusName[del.status]}</span>
                      <small>{del.error || del.remoteId}</small>
                      {["failed", "blocked"].includes(del.status) && (
                        <button
                          disabled={busy}
                          onClick={() =>
                            void action(() =>
                              api("/deliveries/" + del.id + "/retry", "POST"),
                            )
                          }
                        >
                          人工重试
                        </button>
                      )}
                      {del.status === "unknown" && (
                        <Resolve delivery={del} action={action} />
                      )}
                    </div>
                  ))}
                  <div className="actions">
                    <button onClick={() => compose(d)}>编辑</button>
                    <button
                      onClick={() => {
                        const a = document.createElement("a");
                        a.href = URL.createObjectURL(
                          new Blob([d.content], {
                            type: "text/plain;charset=utf-8",
                          }),
                        );
                        a.download = "auto-post-" + d.id + ".txt";
                        a.click();
                        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
                      }}
                    >
                      导出内容
                    </button>
                    {d.status === "draft" && !d.quarantined && (
                      <button
                        disabled={busy}
                        onClick={() =>
                          void action(() =>
                            api(
                              "/drafts/" +
                                d.id +
                                "/approve?revision=" +
                                d.revision,
                              "POST",
                            ),
                          )
                        }
                      >
                        <Check size={14} />
                        审核当前版本
                      </button>
                    )}
                    {["approved", "partial"].includes(d.status) &&
                      !d.quarantined && (
                        <button
                          className="primary"
                          disabled={busy}
                          onClick={() =>
                            void action(() =>
                              api("/drafts/" + d.id + "/publish", "POST"),
                            )
                          }
                        >
                          提交发布
                        </button>
                      )}
                  </div>
                </div>
              ))}
            </div>
          )}
          {page === "rules" && (
            <>
              <div className="panel content-card">
                <div className="row between">
                  <div>
                    <h2>自动发布总开关</h2>
                    <p className="muted">
                      规则与渠道都通过检查后才可发送。新规则默认关闭。
                    </p>
                  </div>
                  <button
                    className={status.data?.settings.autoPaused ? "" : "danger"}
                    disabled={!connected || busy}
                    onClick={() =>
                      void action(() =>
                        api("/settings", "PUT", {
                          ...status.data.settings,
                          autoPaused: !status.data.settings.autoPaused,
                        }),
                      )
                    }
                  >
                    {status.data?.settings.autoPaused ? (
                      <Play size={15} />
                    ) : (
                      <Pause size={15} />
                    )}{" "}
                    {status.data?.settings.autoPaused
                      ? "解除全局暂停"
                      : "暂停自动发布"}
                  </button>
                </div>
              </div>
              <div className="section-title">
                <h2>白名单规则</h2>
                <button
                  disabled={!connected}
                  onClick={() => {
                    setRule(blankRule());
                    setRuleEditor(true);
                  }}
                >
                  <Plus size={15} />
                  新建规则
                </button>
              </div>
              <div className="cards">
                {!rules.data?.length && (
                  <Empty
                    title="规则尚未配置"
                    note="设置来源、关键词、目标渠道和发布窗口，试运行后再启用。"
                  />
                )}
                {rules.data?.map((r) => (
                  <div className="panel content-card" key={r.id}>
                    <div className="row between">
                      <h3>{r.name}</h3>
                      <span className="pill">
                        {r.enabled ? "已启用" : "已关闭"} · v{r.version}
                      </span>
                    </div>
                    <p className="muted">
                      {r.sources
                        .map(
                          (id) => sources.find((s) => s.id === id)?.name || id,
                        )
                        .join(" / ")}{" "}
                      → {r.channels.join(" / ")}
                    </p>
                    <div className="chips">
                      <span>{r.keywords.join("、") || "所有关键词"}</span>
                      <span>每日 {r.dailyLimit} 条 / 渠道</span>
                      <span>间隔 {r.cooldownMinutes} 分钟</span>
                      <span>
                        {r.startHour}:00–{r.endHour}:00 CST
                      </span>
                    </div>
                    <div className="actions">
                      <button
                        onClick={() => {
                          setRule(r);
                          setRuleEditor(true);
                        }}
                      >
                        编辑
                      </button>
                      <button
                        disabled={busy}
                        onClick={() =>
                          void action(async () => {
                            const results = await api(
                              "/rules/" + r.id + "/test",
                              "POST",
                            );
                            setError(
                              results
                                .slice(0, 5)
                                .map((x: any) => x.title + "：" + x.reason)
                                .join("；") || "暂无事件可试运行",
                            );
                          })
                        }
                      >
                        试运行
                      </button>
                      <button
                        disabled={busy}
                        onClick={() =>
                          void action(() =>
                            api("/rules/" + r.id, "PUT", {
                              ...r,
                              enabled: !r.enabled,
                            }),
                          )
                        }
                      >
                        {r.enabled ? "关闭规则" : "启用规则"}
                      </button>
                      <button
                        disabled={busy}
                        onClick={() =>
                          void action(() => api("/rules/" + r.id, "DELETE"))
                        }
                      >
                        删除
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
          {page === "sources" && (
            <>
              <div className="section-title">
                <h2>多源情报网络 · {sources.length} 个配置</h2>
                <button
                  disabled={!connected || busy}
                  onClick={() =>
                    void action(async () => {
                      await api("/sources/catalog", "POST");
                    })
                  }
                >
                  导入 76 个候选源
                </button>
                <button
                  disabled={!connected || busy}
                  onClick={() =>
                    void action(async () => {
                      await api("/sources/validate-all", "POST");
                    })
                  }
                >
                  检测未启用来源
                </button>
                <button
                  disabled={!connected}
                  onClick={() => {
                    setSourceInput({
                      kind: "rss",
                      name: "",
                      address: "",
                      enabled: false,
                      intervalSeconds: 60,
                    });
                    setSourceEditor(true);
                  }}
                >
                  <Plus size={15} />
                  添加来源
                </button>
              </div>
              <div className="cards">
                {sources.length === 0 && (
                  <Empty
                    title="尚无真实数据源"
                    note="添加获准使用的 RSS 地址或 X 查询；X 还需要服务器配置读取凭据。"
                  />
                )}
                {sources.map((s) => (
                  <div className="panel content-card" key={s.id}>
                    <div className="row between">
                      <h3>{s.name}</h3>
                      <span className="pill">
                        {s.enabled
                          ? statusName[s.status] || s.status
                          : "已停用"}
                      </span>
                    </div>
                    <p className="muted break">{s.address}</p>
                    <p className="meta">
                      {s.category || "world"} · 优先级 {s.priority ?? 50} ·{" "}
                      {s.kind.toUpperCase()} · 间隔 {s.intervalSeconds}s · 检测{" "}
                      {s.validation || "unchecked"} / HTTP {s.httpStatus || "—"}{" "}
                      · 连续失败 {s.consecutiveFailures || 0} · 上次成功{" "}
                      {s.lastSuccess ? time(s.lastSuccess) : "尚无"} {s.error}
                    </p>
                    <button
                      onClick={() => {
                        setSourceInput(s);
                        setSourceEditor(true);
                      }}
                    >
                      编辑来源
                    </button>
                    <button
                      disabled={busy || s.kind !== "rss"}
                      onClick={() =>
                        void action(async () => {
                          await api(`/sources/${s.id}/validate`, "POST");
                        })
                      }
                    >
                      检测可用性
                    </button>
                    <button
                      disabled={busy}
                      onClick={() =>
                        void action(async () => {
                          await api(`/sources/${s.id}`, "PUT", {
                            ...s,
                            enabled: !s.enabled,
                          });
                        })
                      }
                    >
                      {s.enabled
                        ? "停用"
                        : s.enableAfterValidation
                          ? "检测后启用中"
                          : "检测并启用"}
                    </button>
                    <button
                      disabled={busy}
                      onClick={() =>
                        void action(async () => {
                          await api(`/sources/${s.id}`, "DELETE");
                        })
                      }
                    >
                      删除来源
                    </button>
                  </div>
                ))}
              </div>
              <div className="section-title">
                <h2>模型与发布渠道</h2>
              </div>
              <div className="channel-grid">
                {status.data?.channels.map((c: any) => (
                  <div className="panel content-card" key={c.id}>
                    <h3>{c.label}</h3>
                    <p className="muted">
                      {c.mode === "manual_export"
                        ? "人工导出 · 待授权内容 API"
                        : c.configured
                          ? "凭据已配置 · 实际调用结果见投递记录"
                          : "待配置凭据"}
                    </p>
                  </div>
                ))}
                <div className="panel content-card">
                  <h3>SiliconFlow / 模型</h3>
                  <p className="muted">
                    {status.data?.ai_configured
                      ? "凭据已配置 · 分析成功后才代表调用通过"
                      : "等待 API Key 与模型权限"}
                  </p>
                </div>
              </div>
            </>
          )}
          {page === "system" && (
            <>
              <div className="panel content-card">
                <h2>运行状态与预算</h2>
                <p className="muted">{status.data?.mode==="live"?"真实数据生产模式":"演示模式"} · {status.data?.cloudDatabaseStatus==="connected"?"云数据库已连接":"云数据库待证书核验，当前数据库正常运行"}</p>
                <p className="muted">
                  Worker 心跳：
                  {status.data?.settings.workerHeartbeat
                    ? time(status.data.settings.workerHeartbeat)
                    : "未启动"}{" "}
                  ·{" "}
                  {status.data?.settings.workerHeartbeat &&
                  Date.now() / 1000 - status.data.settings.workerHeartbeat < 60
                    ? "运行中"
                    : "待连接或已离线"}
                </p>
                {connected && <Budget status={status.data} action={action} />}
                <div className="chips">
                  {status.data?.usage.map((b: any) => (
                    <span key={b.id}>
                      {b.id}：{b.used}
                    </span>
                  ))}
                </div>
              </div>
              <div className="panel content-card">
                <h2>后台任务</h2>
                {tasks.data?.slice(0, 30).map((t) => (
                  <div className="log-row" key={t.id}>
                    <span>{t.kind}</span>
                    <span>{statusName[t.status]}</span>
                    <span>尝试 {t.attempts}</span>
                    <small>{t.error}</small>
                  </div>
                ))}
              </div>
              <div className="panel content-card">
                <h2>操作审计</h2>
                {audit.data?.slice(0, 30).map((a) => (
                  <div className="log-row" key={a.id}>
                    <span>{a.action}</span>
                    <small>{a.target}</small>
                    <span>{time(a.createdAt)}</span>
                  </div>
                ))}
              </div>
            </>
          )}
          <footer>
            <span>◈ SIGNAL ATLAS</span>
            <span>Capture context. Publish with confidence.</span>
            <span>v0.5 / .NET 10</span>
          </footer>
        </main>
      </div>
      <AgentPanel connected={connected} event={selected} onLogin={()=>setLogin(true)} onDraft={()=>{setPage("drafts");void qc.invalidateQueries({queryKey:["drafts"]})}}/>
      <Modal
        open={login}
        onOpen={setLogin}
        title="连接工作空间"
        description="管理令牌只用于登录。会话使用安全 Cookie，页面不会保存令牌。"
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action(async () => {
              await api("/session", "POST", { token });
              await session();
              setToken("");
              setConnected(true);
              setLogin(false);
            });
          }}
        >
          <label>
            管理访问令牌
            <input
              type="password"
              autoComplete="off"
              required
              value={token}
              onChange={(e) => setToken(e.target.value)}
            />
          </label>
          <button className="primary" disabled={busy}>
            验证并连接
          </button>
          {connected && (
            <button
              type="button"
              onClick={() =>
                void action(async () => {
                  await api("/session", "DELETE");
                  setConnected(false);
                  qc.clear();
                  setLogin(false);
                })
              }
            >
              退出登录
            </button>
          )}
        </form>
      </Modal>
      <Modal
        open={editor}
        onOpen={v=>{if(!uploading)setEditor(v);}}
        title={editing ? "编辑草稿 · v" + editing.revision : "人工创作"}
        description="修改内容会创建新版本并取消旧审核。平台限制以预览结果为准。"
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action(async () => {
              if (!connected) throw new Error("请先连接工作空间");
              await api(
                "/drafts" + (editing ? "/" + editing.id : ""),
                editing ? "PUT" : "POST",
                {
                  content,
                  channels,
                  media,
                  eventId: editing?.event_id || selected?.id || null,
                  revision: editing?.revision,
                },
              );
              setEditor(false);
              setPage("drafts");
            });
          }}
        >
          <Composer content={content} onContent={v=>{setContent(v);setPreview([]);}} media={media} onMedia={v=>{setMedia(v);setPreview([]);}} channels={channels} onChannels={v=>{setChannels(v);setPreview([]);}} disabled={busy||!connected} onUploading={setUploading}/>
          <div className="actions">
            <button
              type="button"
              disabled={!connected || busy || uploading}
              onClick={() =>
                void action(async () =>
                  setPreview(
                    await api("/drafts/preview", "POST", { content, channels, media }),
                  ),
                )
              }
            >
              分渠道预览
            </button>
            <span className="meta">{content.length} 字符</span>
            <button className="primary" disabled={busy||uploading||!connected||channels.length===0}>
              保存待审核草稿
            </button>
          </div>
          {preview.map((p) => (
            <div className="preview" key={p.channel}>
              <b>
                {p.channel} · 长度 {p.length}
              </b>
              <p>{p.content}</p>
              <span className={p.error ? "accent-error" : "accent"}>
                {p.error || "通过内容校验"}
              </span>
            </div>
          ))}
        </form>
      </Modal>
      <Modal
        open={ruleEditor}
        onOpen={setRuleEditor}
        title={rule.id ? "编辑白名单规则" : "新建白名单规则"}
        description="时间窗口按北京时间。每渠道使用服务器配置的 default 账号，新建后默认关闭。"
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action(async () => {
              await api(
                "/rules" + (rule.id ? "/" + rule.id : ""),
                rule.id ? "PUT" : "POST",
                rule,
              );
              setRuleEditor(false);
            });
          }}
        >
          <label>
            规则名称
            <input
              required
              value={rule.name}
              onChange={(e) => setRule({ ...rule, name: e.target.value })}
            />
          </label>
          <label>来源白名单</label>
          <div className="checks">
            {sources.map((s) => (
              <label key={s.id}>
                <input
                  type="checkbox"
                  checked={rule.sources.includes(s.id)}
                  onChange={() =>
                    setRule({
                      ...rule,
                      sources: rule.sources.includes(s.id)
                        ? rule.sources.filter((x) => x !== s.id)
                        : [...rule.sources, s.id],
                    })
                  }
                />
                {s.name}
              </label>
            ))}
          </div>
          <label>
            关键词（逗号分隔，留空匹配来源内全部内容）
            <input
              value={rule.keywords.join(",")}
              onChange={(e) =>
                setRule({
                  ...rule,
                  keywords: e.target.value
                    .split(/[,，]/)
                    .map((x) => x.trim())
                    .filter(Boolean),
                })
              }
            />
          </label>
          <div className="checks">
            {["x", "telegram"].map((c) => (
              <label key={c}>
                <input
                  type="checkbox"
                  checked={rule.channels.includes(c)}
                  onChange={() =>
                    setRule({
                      ...rule,
                      channels: rule.channels.includes(c)
                        ? rule.channels.filter((x) => x !== c)
                        : [...rule.channels, c],
                    })
                  }
                />
                {c}
              </label>
            ))}
          </div>
          <div className="form-grid">
            {[
              ["dailyLimit", "每日限额", 1, 1000],
              ["cooldownMinutes", "冷却分钟", 1, 1440],
              ["startHour", "开始小时", 0, 23],
              ["endHour", "结束小时", 1, 24],
            ].map(([key, label, min, max]) => (
              <label key={key}>
                {label}
                <input
                  type="number"
                  min={min}
                  max={max}
                  value={(rule as any)[key]}
                  onChange={(e) =>
                    setRule({ ...rule, [key]: Number(e.target.value) })
                  }
                />
              </label>
            ))}
          </div>
          <button className="primary" disabled={busy}>
            保存规则
          </button>
        </form>
      </Modal>
      <Modal
        open={sourceEditor}
        onOpen={setSourceEditor}
        title="配置数据源"
        description="RSS 仅支持公共 HTTPS 地址。X 使用授权的最近搜索接口，实际延迟取决于配额。"
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action(async () => {
              await api(
                "/sources" + (sourceInput.id ? "/" + sourceInput.id : ""),
                sourceInput.id ? "PUT" : "POST",
                sourceInput,
              );
              setSourceEditor(false);
            });
          }}
        >
          <label>
            类型
            <select
              value={sourceInput.kind}
              onChange={(e) =>
                setSourceInput({ ...sourceInput, kind: e.target.value })
              }
            >
              <option value="rss">RSS</option>
              <option value="x">X 最近搜索</option>
            </select>
          </label>
          <label>
            名称
            <input
              required
              value={sourceInput.name}
              onChange={(e) =>
                setSourceInput({ ...sourceInput, name: e.target.value })
              }
            />
          </label>
          <label>
            {sourceInput.kind === "rss" ? "HTTPS RSS 地址" : "X 查询词"}
            <input
              required
              value={sourceInput.address}
              onChange={(e) =>
                setSourceInput({ ...sourceInput, address: e.target.value })
              }
            />
          </label>
          {sourceInput.kind === "rss" && (
            <>
              <label>
                Google News 关键词（留空使用直接地址）
                <input
                  value={sourceInput.topic || ""}
                  onChange={(e) => {
                    const topic = e.target.value;
                    setSourceInput({
                      ...sourceInput,
                      topic,
                      address: topic
                        ? "https://news.google.com/rss/search?q=" +
                          encodeURIComponent(topic) +
                          "&hl=" +
                          (sourceInput.language || "en") +
                          "&gl=" +
                          (sourceInput.region || "US") +
                          "&ceid=" +
                          (sourceInput.region || "US") +
                          ":" +
                          (sourceInput.language === "zh-CN" ? "zh-Hans" : "en")
                        : sourceInput.address,
                    });
                  }}
                />
              </label>
              <label>
                分类
                <select
                  value={sourceInput.category || "world"}
                  onChange={(e) =>
                    setSourceInput({ ...sourceInput, category: e.target.value })
                  }
                >
                  <option value="world">全球时事</option>
                  <option value="crypto">加密 Web3</option>
                  <option value="technology">AI 科技</option>
                  <option value="macro">宏观金融</option>
                  <option value="regulation">政策监管</option>
                </select>
              </label>
              <label>
                优先级（0–100）
                <input
                  type="number"
                  min={0}
                  max={100}
                  value={sourceInput.priority ?? 50}
                  onChange={(e) =>
                    setSourceInput({
                      ...sourceInput,
                      priority: Number(e.target.value),
                    })
                  }
                />
              </label>
              <label>
                语言
                <select
                  value={sourceInput.language || "en"}
                  onChange={(e) =>
                    setSourceInput({ ...sourceInput, language: e.target.value })
                  }
                >
                  <option value="en">English</option>
                  <option value="zh-CN">中文</option>
                </select>
              </label>
              <label>
                地区
                <select
                  value={sourceInput.region || "US"}
                  onChange={(e) =>
                    setSourceInput({ ...sourceInput, region: e.target.value })
                  }
                >
                  <option value="US">美国</option>
                  <option value="CN">中国</option>
                  <option value="GB">英国</option>
                </select>
              </label>
              <label>
                最大发布年龄（天）
                <input
                  type="number"
                  min={1}
                  max={365}
                  value={sourceInput.freshnessDays ?? 30}
                  onChange={(e) =>
                    setSourceInput({
                      ...sourceInput,
                      freshnessDays: Number(e.target.value),
                    })
                  }
                />
              </label>
            </>
          )}
          <label>
            采集间隔（秒）
            <input
              type="number"
              min={30}
              max={86400}
              value={sourceInput.intervalSeconds}
              onChange={(e) =>
                setSourceInput({
                  ...sourceInput,
                  intervalSeconds: Number(e.target.value),
                })
              }
            />
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={sourceInput.enabled}
              onChange={(e) =>
                setSourceInput({ ...sourceInput, enabled: e.target.checked })
              }
            />
            启用采集
          </label>
          <button className="primary" disabled={busy}>
            保存来源
          </button>
        </form>
      </Modal>
    </div>
  );
}
function Stat({
  label,
  value,
  note,
  icon,
}: {
  label: string;
  value: number;
  note: string;
  icon: React.ReactNode;
}) {
  return (
    <div className="stat panel">
      <div className="row between">
        <span>{label}</span>
        {icon}
      </div>
      <strong>{String(value).padStart(2, "0")}</strong>
      <small>{note}</small>
      <div className="stat-line" />
    </div>
  );
}
function Empty({ title, note }: { title: string; note?: string }) {
  return (
    <div className="empty">
      <Radio size={24} />
      <h3>{title}</h3>
      <p>{note}</p>
    </div>
  );
}
function blankRule(): Rule {
  return {
    name: "",
    enabled: false,
    sources: [],
    keywords: [],
    channels: ["telegram"],
    account: "default",
    dailyLimit: 10,
    cooldownMinutes: 15,
    startHour: 0,
    endHour: 24,
  };
}
function Resolve({
  delivery,
  action,
}: {
  delivery: any;
  action: (fn: () => Promise<unknown>) => Promise<void>;
}) {
  const [open, setOpen] = useState(false),
    [state, setState] = useState("published"),
    [remote, setRemote] = useState(""),
    [note, setNote] = useState("");
  return (
    <>
      <button onClick={() => setOpen(true)}>人工核验</button>
      <Modal
        open={open}
        onOpen={setOpen}
        title="核验平台实际结果"
        description="请先打开目标平台确认。标记未发布后才能人工重试。"
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action(async () => {
              await api("/deliveries/" + delivery.id + "/resolve", "POST", {
                status: state,
                remoteId: remote,
                note,
              });
              setOpen(false);
            });
          }}
        >
          <label>
            核验结果
            <select value={state} onChange={(e) => setState(e.target.value)}>
              <option value="published">确认已发布</option>
              <option value="failed">确认未发布</option>
            </select>
          </label>
          <label>
            平台内容 ID
            <input
              required={state === "published"}
              value={remote}
              onChange={(e) => setRemote(e.target.value)}
            />
          </label>
          <label>
            核验说明
            <input
              required
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </label>
          <button className="primary">记录核验结果</button>
        </form>
      </Modal>
    </>
  );
}
function Budget({
  status,
  action,
}: {
  status: any;
  action: (fn: () => Promise<unknown>) => Promise<void>;
}) {
  const [analysis, setAnalysis] = useState(
      status?.settings.analysisDailyLimit || 100,
    ),
    [channel, setChannel] = useState(status?.settings.channelDailyLimit || 20);
  return (
    <form
      className="budget"
      onSubmit={(e) => {
        e.preventDefault();
        void action(() =>
          api("/settings", "PUT", {
            ...status.settings,
            analysisDailyLimit: analysis,
            channelDailyLimit: channel,
          }),
        );
      }}
    >
      <label>
        每日模型调用上限
        <input
          type="number"
          min={0}
          max={10000}
          value={analysis}
          onChange={(e) => setAnalysis(Number(e.target.value))}
        />
      </label>
      <label>
        每渠道每日投递上限
        <input
          type="number"
          min={0}
          max={10000}
          value={channel}
          onChange={(e) => setChannel(Number(e.target.value))}
        />
      </label>
      <button>保存预算</button>
    </form>
  );
}
createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={queryClient}>
    <App />
  </QueryClientProvider>,
);
