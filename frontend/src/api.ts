import type {
  AdCampaignDetail,
  AdsOverview,
  AdsSyncResult,
  AdsDiagnosisResponse,
  AdsSettings,
  DiaryResponse,
  IntegrationsStatus,
  RecommendationsResponse,
  Diagnosis,
  DraftReport,
  Listing,
  ListingAds,
  MlUser,
  Suggestion,
} from './types'

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// Todas as chamadas vão para /api/..., que o Vite repassa para a API em C# (ver vite.config.ts).
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`/api${path}`, init)

  if (!res.ok) {
    let message = `Erro ${res.status}`
    try {
      const body = await res.json()
      message = body.error ?? message
    } catch {
      // resposta sem JSON: mantém a mensagem padrão
    }
    throw new ApiError(res.status, message)
  }

  return res.json() as Promise<T>
}

const json = { 'Content-Type': 'application/json' }

// Para rotas que respondem 204 (sem corpo).
async function requestEmpty(path: string, method: string): Promise<void> {
  const res = await fetch(`/api${path}`, { method })
  if (!res.ok) throw new ApiError(res.status, `Erro ${res.status}`)
}

export const api = {
  me: () => request<MlUser>('/ml/me'),
  integrations: () => request<IntegrationsStatus>(`/settings/integrations?origin=${encodeURIComponent(window.location.origin)}`),
  saveMlCredentials: (appId: string, clientSecret: string, redirectUri: string) =>
    request<{ saved: boolean }>('/settings/mercadolivre/credentials', {
      method: 'PUT',
      headers: json,
      // redirectUri em branco = o servidor gera a URL a partir do endereço em que o painel foi aberto.
      body: JSON.stringify({ appId, clientSecret, redirectUri, panelOrigin: window.location.origin }),
    }),
  mlConnect: () => request<{ url: string }>('/settings/mercadolivre/connect', { method: 'POST' }),
  mlDisconnect: () => requestEmpty('/settings/mercadolivre/disconnect', 'POST'),
  deleteMlCredentials: () => requestEmpty('/settings/mercadolivre/credentials', 'DELETE'),
  listings: () => request<Listing[]>('/listings'),
  draft: (itemId: string) => request<DraftReport>(`/listings/${itemId}/draft`),
  diagnosis: () => request<Diagnosis[]>('/agent/diagnosis'),
  suggestions: () => request<Suggestion[]>('/agent/suggestions'),
  sync: () => request<{ synced: number }>('/ml/sync', { method: 'POST' }),
  suggest: (itemId: string) => request<Suggestion>(`/agent/suggest/${itemId}`, { method: 'POST' }),
  decide: (id: number, decision: 'approve' | 'reject') =>
    request<Suggestion>(`/agent/suggestions/${id}/${decision}`, { method: 'POST' }),
  adsCampaigns: (days: number) => request<AdsOverview>(`/ads/campaigns?days=${days}`),
  adsCampaign: (id: number, days: number) => request<AdCampaignDetail>(`/ads/campaigns/${id}?days=${days}`),
  adsSync: (days: number) => request<AdsSyncResult>(`/ads/sync?days=${days}`, { method: 'POST' }),
  adsByListing: () => request<ListingAds[]>('/ads/by-listing'),
  adsDiagnosis: (days: number) => request<AdsDiagnosisResponse>(`/ads/diagnosis?days=${days}`),
  adsSettings: () => request<AdsSettings>('/ads/settings'),
  adsAnalyze: (days: number) =>
    request<{ runId: number; recommendations: number }>(`/ads/analyze?days=${days}`, { method: 'POST' }),
  adsRecommendations: (runId?: number) =>
    request<RecommendationsResponse>(`/ads/recommendations${runId ? `?runId=${runId}` : ''}`),
  decideAdsRecommendation: (id: number, decision: 'approve' | 'reject', note: string) =>
    request<{ id: number; status: string }>(`/ads/recommendations/${id}/decision`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ decision, note }),
    }),
  adsDiary: () => request<DiaryResponse>('/ads/diary'),
  saveChangeNote: (changeId: number, note: string) =>
    request<{ id: number }>(`/ads/changes/${changeId}/note`, { method: 'PUT', headers: json, body: JSON.stringify({ note }) }),
  addDiaryNote: (date: string, campaignId: number | null, text: string) =>
    request<{ id: number }>('/ads/diary/notes', { method: 'POST', headers: json, body: JSON.stringify({ date, campaignId, text }) }),
  deleteDiaryNote: async (id: number) => {
    const res = await fetch(`/api/ads/diary/notes/${id}`, { method: 'DELETE' })
    if (!res.ok) throw new ApiError(res.status, `Erro ${res.status}`)
  },
  markApplied: (recommendationId: number, date: string | null) =>
    request<{ id: number }>(`/ads/recommendations/${recommendationId}/applied`, { method: 'PUT', headers: json, body: JSON.stringify({ date }) }),
  saveAdsSettings: (settings: AdsSettings) =>
    request<AdsSettings>('/ads/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    }),
}
