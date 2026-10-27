// Formato dos dados devolvidos pela API em C# (o ASP.NET serializa em camelCase).

export interface Listing {
  itemId: string
  title: string
  price: number
  currencyId: string
  status: string
  availableQuantity: number
  soldQuantity: number
  visits30d: number | null
  sales30d: number | null
  picturesCount: number
  freeShipping: boolean
  categoryId: string | null
  listingTypeId: string | null
  permalink: string | null
  health: number | null
  syncedAt: string
}

export interface Diagnosis {
  itemId: string
  title: string
  status: string
  visits30d: number | null
  sales30d: number | null
  conversion: number | null
  flags: string[]
}

// A sugestão da IA vem como texto JSON em contentJson; use parseSuggestion() para abrir.
export interface Suggestion {
  id: number
  itemId: string
  createdAt: string
  model: string
  status: 'Pending' | 'Approved' | 'Rejected'
  flags: string
  contentJson: string
}

export interface SuggestionContent {
  diagnostico: string
  titulos: string[]
  descricao?: string | null // ausente em sugestões geradas antes desse campo existir
  melhorias_descricao: string[]
  outras_acoes: string[]
}

// Rascunho de anúncio (formato do POST /items) e o que falta para ele poder ser criado.
export interface DraftReport {
  itemId: string
  ready: boolean
  missing: string[]
  warnings: string[]
  maxTitleLength: number
  maxDescriptionLength: number
  draft: { description?: { plain_text?: string } } & Record<string, unknown>
}

export interface MlUser {
  id: number
  nickname: string
  siteId: string
}

// Anúncio + diagnóstico + última sugestão + publicidade, já juntos para a tabela.
export interface Row extends Listing {
  conversion: number | null
  flags: string[]
  suggestion: Suggestion | null
  ads: ListingAds | null
}

export function parseSuggestion(s: Suggestion): SuggestionContent {
  return JSON.parse(s.contentJson) as SuggestionContent
}

// ---- Product Ads ----
// Taxas (ctr, acos, cvr, tacos) já vêm em porcentagem; roas é receita por real investido. Null = indefinido.
export interface AdMetrics {
  clicks: number
  prints: number
  cost: number
  revenue: number
  directRevenue: number
  indirectRevenue: number
  units: number
  advertisingSales: number
  organicUnits: number
  organicRevenue: number
  ctr: number | null
  cpc: number | null
  acos: number | null
  roas: number | null
  cvr: number | null
  tacos: number | null
}

export interface AdCampaign {
  id: number
  name: string
  status: string
  strategy: string | null
  budget: number
  dailyBudget: number
  roasTarget: number | null
  acosTarget: number | null
  avgDailyCost: number
  budgetUsagePct: number | null
  impressionShare: number | null // fração de 0 a 1
  lostByBudget: number | null
  lostByRank: number | null
  metrics: AdMetrics
}

export interface AdsOverview {
  from: string
  to: string
  lastSync: string | null
  totals: AdMetrics
  campaigns: AdCampaign[]
}

export interface AdDay {
  date: string
  metrics: AdMetrics
  impressionShare: number | null
  lostByBudget: number | null
  lostByRank: number | null
}

export interface AdGroup {
  id: number
  type: string // ITEM, FAMILY ou CATALOG
  externalId: string
  title: string
  status: string
  thumbnail: string | null
  tags: string[]
  sov: number | null
  metrics: AdMetrics
  listings: { itemId: string; title: string }[] // publicações do painel ligadas a este anúncio
}

// Anúncio de campanha ligado a uma publicação. sharedBy > 1 = as métricas são do grupo todo (família/catálogo).
export interface LinkedAdGroup {
  adGroupId: number
  type: string
  status: string
  campaignId: number
  campaignName: string
  campaignStatus: string
  sharedBy: number
  from: string
  to: string
  metrics: AdMetrics
}

export interface ListingAds {
  itemId: string
  metrics: AdMetrics
  shared: boolean
  adGroups: LinkedAdGroup[]
}

export interface AdCampaignDetail {
  from: string
  to: string
  campaign: AdCampaign
  daily: AdDay[]
  adGroups: AdGroup[]
  adGroupsPeriod: { from: string; to: string } | null
}

export interface AdsSyncResult {
  advertisers: number
  campaigns: number
  dailyRows: number
  adGroups: number
  from: string
  to: string
}
