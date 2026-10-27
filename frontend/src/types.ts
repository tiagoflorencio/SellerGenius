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

// Telas do menu lateral.
export type Page = 'home' | 'listings' | 'ads' | 'settings'

// Situação das integrações (tela de Configurações). A chave secreta nunca é devolvida pela API.
export interface IntegrationsStatus {
  mercadoLivre: {
    configured: boolean
    source: 'ui' | 'config' | 'none'
    appId: string
    hasSecret: boolean
    redirectUri: string
    connected: boolean
    account: MlUser | null
    accountError: string | null
  }
  openAi: { configured: boolean; model: string }
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
  changes: AdChange[]
}

// Mudança de configuração detectada entre sincronizações (feita por você no Mercado Livre).
export interface AdChange {
  id: number
  campaignId: number
  detectedAt: string
  field: string // Budget, RoasTarget, Status, Strategy ou AutomaticBudget
  oldValue: string
  newValue: string
  note: string | null
}

// ---- Diagnóstico de Ads (por regras) ----
export type Severity = 'critical' | 'attention' | 'opportunity' | 'info'
export type DiagnosisStateValue = 'saudavel' | 'atencao' | 'critico' | 'inconclusivo' | 'sem_dados'

export interface AdFinding {
  code: string
  severity: Severity
  where: string // Campanha Ads, Publicação ou Oferta
  title: string
  evidence: string
  impactBrl: number | null
}

export interface AdDiagnosis {
  state: DiagnosisStateValue
  findings: AdFinding[]
}

export interface GroupDiagnosis {
  adGroupId: number
  title: string
  diagnosis: AdDiagnosis
}

export interface CampaignDiagnosis {
  campaignId: number
  name: string
  diagnosis: AdDiagnosis
  groups: GroupDiagnosis[]
}

export interface TopFinding {
  campaignId: number
  campaignName: string
  adGroupId: number | null
  adGroupTitle: string | null
  finding: AdFinding
}

// Parâmetros do diagnóstico (editáveis na tela).
export interface AdsSettings {
  costBasis: 'tacos' | 'acos'
  costTargetMinPct: number
  costTargetMaxPct: number
  growthCostMaxPct: number
  criticalCostFactor: number
  growthMaxSoldUnits: number
  minImpressionsForCtr: number
  minClicksForCvr: number
  ctrLowPct: number
  cvrLowFactor: number
  zeroSalesProbability: number
  roasTargetTolerance: number
  lostByBudgetHighPct: number
  lostByRankHighPct: number
  budgetUnderusePct: number
  trendChangePct: number
  maxRecommendations: number
  recentChangeDays: number
  weightImpact: number
  weightConfidence: number
  weightUrgency: number
  weightEase: number
  weightReversibility: number
  evaluationWindowDays: number
  evaluationFlatBandPct: number
}

export interface AdsDiagnosisResponse {
  from: string
  to: string
  settings: AdsSettings
  medianCvr: number | null
  cvrSampleGroups: number
  campaigns: CampaignDiagnosis[]
  top: TopFinding[]
}

// ---- Agente de Ads (IA) ----
export type RecommendationStatus = 'Pending' | 'Approved' | 'Rejected'

export interface RecommendationContent {
  titulo: string
  diagnostico: string
  evidencias: string[]
  hipoteses: string[]
  teste_sugerido: {
    mudanca: string | null
    magnitude: string | null
    janela_avaliacao_dias: number | null
    metrica_sucesso: string | null
    criterio: string | null
  } | null
  efeito_esperado: string | null
  riscos: string[]
  confianca: string
  // Notas (0 a 100) que formam a prioridade, calculadas pelo código.
  pontuacao: { impact: number; confidence: number; urgency: number; ease: number; reversibility: number }
  achados: AdFinding[]
}

export interface AdRecommendation {
  id: number
  status: RecommendationStatus
  scope: string
  campaignId: number | null
  campaignName: string | null
  adGroupId: number | null
  adGroupTitle: string | null
  action: string
  where: string
  title: string
  priority: number
  confidence: string
  impactBrl: number | null
  impactUnknown: boolean
  findingCodes: string[]
  content: RecommendationContent
  decidedAt: string | null
  decisionNote: string | null
  appliedOn: string | null
}

export interface AdAnalysisRunView {
  id: number
  createdAt: string
  model: string
  periodFrom: string
  periodTo: string
  manualHash: string
  summary: string
  dataQuality: {
    estado?: string | null
    faltantes?: string[]
    alertas?: string[]
    naoRecomendado?: { tema: string | null; motivo: string | null }[]
  }
  discarded: { title: string; reason: string }[]
  promptTokens: number | null
  completionTokens: number | null
}

export interface RunSummary {
  id: number
  createdAt: string
  recommendations: number
  pending: number
}

export interface RecommendationsResponse {
  run: AdAnalysisRunView | null
  recommendations: AdRecommendation[]
  runs: RunSummary[]
  pendingTotal: number
}

// ---- Diário de mudanças e avaliação antes/depois ----
export type EvaluationVerdict = 'melhorou' | 'piorou' | 'misto' | 'neutro' | 'inconclusivo' | 'aguardando'

export interface EvaluationWindow {
  from: string
  to: string
  daysWithDelivery: number
  metrics: { clicks: number; cost: number; revenue: number; roas: number | null }
}

export interface MetricDelta {
  key: string
  label: string
  unit: 'x' | 'brl' | 'pct' | 'num'
  before: number | null
  after: number | null
  changePct: number | null
  direction: 'up' | 'down' | 'flat' | 'na'
  better: 'higher' | 'lower' | 'neutral'
}

export interface Evaluation {
  status: 'aguardando' | 'provisorio' | 'final' | 'indisponivel'
  verdict: EvaluationVerdict
  reason: string
  confidence: 'alta' | 'media' | 'baixa'
  windowDays: number
  elapsedDays: number
  readyOn: string | null
  before: EvaluationWindow | null
  after: EvaluationWindow | null
  deltas: MetricDelta[]
  incrementalRoas: number | null
  notes: string[]
}

export interface DiaryEvent {
  key: string
  kind: 'change' | 'applied'
  date: string
  campaignId: number | null
  campaignName: string | null
  changes: { id: number; field: string; oldValue: string; newValue: string }[]
  recommendationId: number | null
  recommendationTitle: string | null
  recommendationAction: string | null
  note: string | null
  noteChangeId: number | null
  successMetric: string | null
  successCriterion: string | null
  confounders: string[]
  evaluation: Evaluation
}

export interface DiaryNote {
  id: number
  date: string
  campaignId: number | null
  campaignName: string | null
  text: string
}

export interface AwaitingApplication {
  recommendationId: number
  title: string
  action: string
  campaignId: number | null
  campaignName: string | null
  decidedAt: string | null
}

export interface DiaryResponse {
  events: DiaryEvent[]
  notes: DiaryNote[]
  awaiting: AwaitingApplication[]
}

export interface AdsSyncResult {
  advertisers: number
  campaigns: number
  dailyRows: number
  adGroups: number
  from: string
  to: string
}
