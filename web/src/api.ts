export let csrf = "";
export async function api<T = any>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  const res = await fetch("/api" + path, {
    method,
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      ...(csrf ? { "X-CSRF-TOKEN": csrf } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let data;
    try {
      data = await res.json();
    } catch {}
    throw new Error(
      res.status === 401
        ? "请连接工作空间"
        : data?.detail || `请求失败 (${res.status})`,
    );
  }
  return res.status === 204 || res.headers.get("content-length") === "0"
    ? (undefined as T)
    : res.json().catch(() => undefined as T);
}
export async function session() {
  const r = await api("/session");
  csrf = r.csrfToken;
  return true;
}
export type EventItem = {
  author?: string;
  language?: string;
  originalSummary?: string;
  publisher?: string;
  evidenceKey?: string;
  relation?: string;
  publishedEstimated?: boolean;
  id: string;
  source: string;
  title: string;
  body: string;
  url: string;
  category: string;
  published_at: number;
  collected_at: number;
  demo: boolean;
  groupId: string;
  analysisStatus: string;
};
export type Source = {
  category?: string;
  priority?: number;
  topic?: string;
  language?: string;
  region?: string;
  publisher?: string;
  validation?: string;
  checkedAt?: number;
  latestPublishedAt?: number;
  httpStatus?: number;
  consecutiveFailures?: number;
  suspendedUntil?: number;
  freshnessDays?: number;
  enableAfterValidation?: boolean;
  id: string;
  kind: string;
  name: string;
  address: string;
  enabled: boolean;
  intervalSeconds: number;
  status: string;
  lastSuccess?: number;
  error?: string;
};
export type Delivery = {
  id: string;
  channel: string;
  status: string;
  revision: number;
  remoteId?: string;
  error?: string;
};
export type Attachment = { id:string; alt:string };
export type Draft = {
  media?: Attachment[];
  id: string;
  content: string;
  channels: string[];
  revision: number;
  status: string;
  event_id?: string;
  quarantined: boolean;
  deliveries: Delivery[];
};
export type Rule = {
  id?: string;
  name: string;
  enabled: boolean;
  version?: number;
  sources: string[];
  keywords: string[];
  channels: string[];
  account: string;
  dailyLimit: number;
  cooldownMinutes: number;
  startHour: number;
  endHour: number;
};
