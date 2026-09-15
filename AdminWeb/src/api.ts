export const API_BASE = "http://127.0.0.1:5080";

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

export const RARITY_LABELS: Record<number, string> = {
  0: "普通",
  1: "炫彩",
  2: "镜碎",
  3: "描边",
  4: "Gold"
};

export function rarityLabel(rarity: number): string {
  return `${rarity} ${RARITY_LABELS[rarity] ?? "未知"}`;
}

type Problem = {
  title?: string;
  detail?: string;
  code?: string;
};

async function parseError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as Problem;
    return new ApiError(
      response.status,
      problem.code ?? "HTTP_ERROR",
      problem.detail || problem.title || `请求失败 ${response.status}`
    );
  } catch {
    return new ApiError(response.status, "HTTP_ERROR", `请求失败 ${response.status}`);
  }
}

export async function request<T>(
  path: string,
  init: RequestInit = {}
): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type") && !(init.body instanceof Blob)) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(API_BASE + path, { ...init, headers });
  if (!response.ok) {
    throw await parseError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export type OwnedCard = {
  cardId: string;
  rarity: number;
  count: number;
};

export type AdminPlayer = {
  id: string;
  username: string;
  nickname: string;
  gold: number;
  revision: number;
  avatarId?: number | null;
  ownedAvatarIds: number[];
  backgroundId?: number | null;
  ownedBackgroundIds: number[];
  ownedCards: OwnedCard[];
  createdAt: string;
  updatedAt: string;
};

export type GoldGrant = {
  id: string;
  username: string;
  previousGold: number;
  addedAmount: number;
  gold: number;
  revision: number;
};

export function getPlayer(username: string) {
  return request<AdminPlayer>(
    `/api/accounts/admin/player?username=${encodeURIComponent(username)}`
  );
}

export function addGold(username: string, amount: number) {
  return request<GoldGrant>("/api/accounts/admin/gold", {
    method: "POST",
    body: JSON.stringify({ username, amount })
  });
}
