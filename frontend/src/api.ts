import type {
  AdCampaignDetail,
  AdsOverview,
  AdsSyncResult,
  AdsDiagnosisResponse,
  AdsSettings,
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

export const api = {
  me: () => request<MlUser>('/ml/me'),
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
  saveAdsSettings: (settings: AdsSettings) =>
    request<AdsSettings>('/ads/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    }),
}
