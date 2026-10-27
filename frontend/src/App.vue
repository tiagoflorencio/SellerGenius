<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from './api'
import type { Diagnosis, IntegrationsStatus, Listing, ListingAds, Page, Row, Suggestion } from './types'
import { formatDateTime, formatNumber, formatPercent } from './format'
import { activity } from './activity'
import Icon from './components/Icon.vue'
import HomeView from './components/HomeView.vue'
import ListingsTable from './components/ListingsTable.vue'
import SuggestionDrawer from './components/SuggestionDrawer.vue'
import AdsView from './components/AdsView.vue'
import SettingsView from './components/SettingsView.vue'

const page = ref<Page>('home')

const listings = ref<Listing[]>([])
const diagnoses = ref<Diagnosis[]>([])
const suggestions = ref<Suggestion[]>([])
const adsByListing = ref<ListingAds[]>([])
const integrations = ref<IntegrationsStatus | null>(null)
const adsSynced = ref(false)
const adsAnalyzed = ref(false)
const flash = ref<{ kind: 'ok' | 'bad'; text: string } | null>(null)

const error = ref<string | null>(null)
const loading = ref(true)
const syncing = ref(false)
const suggestingId = ref<string | null>(null)
const deciding = ref(false)
const selectedId = ref<string | null>(null)

const ml = computed(() => integrations.value?.mercadoLivre ?? null)
const connected = computed(() => ml.value?.connected ?? null) // null = ainda verificando

async function loadData() {
  try {
    ;[listings.value, diagnoses.value, suggestions.value, adsByListing.value] = await Promise.all([
      api.listings(),
      api.diagnosis(),
      api.suggestions(),
      // Ads é opcional: se ainda não foi sincronizado, a tabela segue sem essas colunas.
      api.adsByListing().catch(() => [] as ListingAds[]),
    ])
  } catch {
    error.value = 'Não foi possível falar com a API. Ela está rodando em http://localhost:5172?'
  }
}

async function loadIntegrations() {
  try {
    integrations.value = await api.integrations()
  } catch {
    integrations.value = null
  }
}

// Resumo das campanhas e das análises de Ads, usado na visão geral e nos contadores do menu.
async function loadAdsSummary() {
  api.adsRecommendations()
    .then((r) => {
      activity.adsPending = r.pendingTotal
      adsAnalyzed.value = r.run !== null
    })
    .catch(() => {})
  api.adsCampaigns(30)
    .then((o) => (adsSynced.value = o.lastSync !== null))
    .catch(() => {})
}

// Configurações alteradas (credenciais, conexão): recarrega o status e, se conectou, os dados.
async function settingsChanged() {
  await loadIntegrations()
  if (connected.value) await loadData()
}

async function run(action: () => Promise<void>) {
  error.value = null
  try {
    await action()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Erro inesperado.'
  }
}

const sync = () =>
  run(async () => {
    syncing.value = true
    try {
      await api.sync()
      await loadData()
    } finally {
      syncing.value = false
    }
  })

const suggest = (itemId: string) =>
  run(async () => {
    suggestingId.value = itemId
    try {
      await api.suggest(itemId)
      suggestions.value = await api.suggestions()
      selectedId.value = itemId // abre a sugestão assim que ficar pronta
    } finally {
      suggestingId.value = null
    }
  })

const decide = (id: number, decision: 'approve' | 'reject') =>
  run(async () => {
    deciding.value = true
    try {
      await api.decide(id, decision)
      suggestions.value = await api.suggestions()
    } finally {
      deciding.value = false
    }
  })

// Junta anúncio + diagnóstico + sugestão mais recente numa só linha para a tabela.
const rows = computed<Row[]>(() => {
  const diagById = new Map(diagnoses.value.map((d) => [d.itemId, d]))
  const latestSuggestion = new Map<string, Suggestion>()
  for (const s of suggestions.value) {
    const current = latestSuggestion.get(s.itemId)
    if (!current || s.id > current.id) latestSuggestion.set(s.itemId, s)
  }

  const adsById = new Map(adsByListing.value.map((a) => [a.itemId, a]))

  return listings.value.map((l) => ({
    ...l,
    conversion: diagById.get(l.itemId)?.conversion ?? null,
    flags: diagById.get(l.itemId)?.flags ?? [],
    suggestion: latestSuggestion.get(l.itemId) ?? null,
    ads: adsById.get(l.itemId) ?? null,
  }))
})

const selectedRow = computed(() => rows.value.find((r) => r.itemId === selectedId.value && r.suggestion) ?? null)

const stats = computed(() => {
  const active = rows.value.filter((r) => r.status === 'active')
  const visits = active.reduce((sum, r) => sum + (r.visits30d ?? 0), 0)
  const sales = active.reduce((sum, r) => sum + (r.sales30d ?? 0), 0)

  return {
    active: active.length,
    visits,
    sales,
    conversion: visits > 0 ? sales / visits : null,
    withAlerts: active.filter((r) => r.flags.length > 0).length,
  }
})

const lastSync = computed(() =>
  listings.value.length ? listings.value.map((l) => l.syncedAt).sort()[listings.value.length - 1] : null,
)

const pendingCount = computed(() => suggestions.value.filter((s) => s.status === 'Pending').length)

const nav = computed(() => [
  { page: 'home' as Page, label: 'Visão geral', icon: 'home' as const, badge: 0 },
  { page: 'listings' as Page, label: 'Anúncios', icon: 'tag' as const, badge: pendingCount.value },
  { page: 'ads' as Page, label: 'Campanhas de Ads', icon: 'megaphone' as const, badge: activity.adsPending },
  { page: 'settings' as Page, label: 'Configurações', icon: 'settings' as const, badge: 0 },
])

const heading = computed(() => {
  switch (page.value) {
    case 'listings':
      return {
        title: 'Anúncios',
        subtitle:
          'Diagnóstico e sugestões com IA' +
          (lastSync.value ? ` · última sincronização ${formatDateTime(lastSync.value)}` : ''),
      }
    case 'ads':
      return { title: 'Campanhas de Ads', subtitle: 'Métricas, diagnóstico, recomendações do agente e diário de mudanças' }
    case 'settings':
      return { title: 'Configurações', subtitle: 'Integrações e credenciais' }
    default:
      return { title: 'Visão geral', subtitle: 'O que precisa da sua atenção hoje' }
  }
})

onMounted(async () => {
  // Retorno do login do Mercado Livre (/?ml=connected ou /?ml=error&message=...): abre as Configurações com o resultado.
  const params = new URLSearchParams(window.location.search)
  const result = params.get('ml')
  if (result) {
    page.value = 'settings'
    flash.value =
      result === 'connected'
        ? { kind: 'ok', text: 'Conta do Mercado Livre conectada com sucesso.' }
        : { kind: 'bad', text: params.get('message') ?? 'Não foi possível conectar ao Mercado Livre.' }
    window.history.replaceState({}, '', window.location.pathname)
  }

  await Promise.all([loadData(), loadIntegrations()])
  loadAdsSummary()
  loading.value = false
})
</script>

<template>
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <span class="brand-mark">A</span>
        <div>
          <strong>AgentOffice</strong>
          <small>Gestão com IA</small>
        </div>
      </div>

      <nav>
        <button v-for="item in nav" :key="item.page" :class="{ active: page === item.page }" @click="page = item.page">
          <Icon :name="item.icon" />
          <span>{{ item.label }}</span>
          <b v-if="item.badge > 0" class="badge-count">{{ item.badge }}</b>
        </button>
      </nav>

      <div class="status">
        <div class="status-row">
          <i class="dot" :class="connected === null ? 'wait' : connected ? 'on' : 'off'"></i>
          <div>
            <strong>Mercado Livre</strong>
            <small v-if="connected && ml?.account">{{ ml.account.nickname }}</small>
            <small v-else-if="connected">Conectado</small>
            <small v-else-if="connected === false">Não conectado</small>
            <small v-else>Verificando...</small>
          </div>
        </div>
        <small class="ro">Acesso somente leitura</small>
      </div>
    </aside>

    <div class="main">
      <header class="topbar">
        <div>
          <h1>{{ heading.title }}</h1>
          <p>{{ heading.subtitle }}</p>
        </div>
        <button v-if="page === 'listings'" class="primary" :disabled="syncing || connected !== true" @click="sync">
          {{ syncing ? 'Sincronizando...' : 'Sincronizar anúncios' }}
        </button>
      </header>

      <div class="content">
        <div v-if="connected === false && (page === 'listings' || page === 'ads')" class="banner warn">
          <Icon name="alert" />
          <span>Conecte sua conta do Mercado Livre para sincronizar anúncios e campanhas.</span>
          <button @click="page = 'settings'">Ir para Configurações</button>
        </div>
        <div v-if="error && page !== 'settings'" class="banner bad">
          <Icon name="alert" />
          <span>{{ error }}</span>
        </div>

        <SettingsView v-if="page === 'settings'" :status="integrations" :flash="flash" @changed="settingsChanged" />

        <AdsView v-else-if="page === 'ads'" />

        <p v-else-if="loading" class="muted">Carregando...</p>

        <HomeView
          v-else-if="page === 'home'"
          :status="integrations"
          :listings-count="listings.length"
          :active-listings="stats.active"
          :with-alerts="stats.withAlerts"
          :suggestions-pending="pendingCount"
          :ads-pending="activity.adsPending"
          :ads-synced="adsSynced"
          :ads-analyzed="adsAnalyzed"
          @go="(p) => (page = p)"
        />

        <template v-else>
          <section class="cards">
            <div class="card"><span>Anúncios ativos</span><strong>{{ stats.active }}</strong></div>
            <div class="card"><span>Visitas (30 dias)</span><strong>{{ formatNumber(stats.visits) }}</strong></div>
            <div class="card"><span>Vendas (30 dias)</span><strong>{{ formatNumber(stats.sales) }}</strong></div>
            <div class="card"><span>Conversão geral</span><strong>{{ formatPercent(stats.conversion) }}</strong></div>
            <div class="card"><span>Com alertas</span><strong>{{ stats.withAlerts }}</strong></div>
          </section>

          <p v-if="listings.length === 0" class="muted">
            Nenhum anúncio salvo ainda. Clique em "Sincronizar anúncios".
          </p>

          <ListingsTable
            v-else
            :rows="rows"
            :suggesting-id="suggestingId"
            @suggest="suggest"
            @open="(row) => (selectedId = row.itemId)"
          />
        </template>
      </div>
    </div>

    <SuggestionDrawer
      v-if="selectedRow"
      :row="selectedRow"
      :deciding="deciding"
      @close="selectedId = null"
      @decide="decide"
    />
  </div>
</template>

<style scoped>
.shell {
  display: grid;
  grid-template-columns: 250px 1fr;
  min-height: 100vh;
}
.sidebar {
  position: sticky;
  top: 0;
  height: 100vh;
  display: flex;
  flex-direction: column;
  gap: 22px;
  padding: 20px 14px;
  background: var(--sidebar);
  color: #cbd5e1;
}
.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 4px 8px;
}
.brand-mark {
  width: 38px;
  height: 38px;
  border-radius: 11px;
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 18px;
  color: #fff;
  background: linear-gradient(135deg, #6366f1, #4338ca);
}
.brand strong {
  display: block;
  color: #fff;
  font-size: 16px;
}
.brand small {
  color: #94a3b8;
}
nav {
  display: grid;
  gap: 4px;
}
nav button {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  text-align: left;
  border: none;
  background: transparent;
  color: #cbd5e1;
  padding: 10px 12px;
  border-radius: 10px;
  font-weight: 500;
}
nav button:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.07);
}
nav button.active {
  background: rgba(99, 102, 241, 0.22);
  color: #fff;
}
nav button span {
  flex: 1;
}
.badge-count {
  background: #f59e0b;
  color: #1f2937;
  border-radius: 999px;
  font-size: 11px;
  padding: 1px 8px;
}
.status {
  margin-top: auto;
  padding: 12px;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.06);
  display: grid;
  gap: 8px;
}
.status-row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.status-row strong {
  display: block;
  color: #fff;
  font-size: 13px;
}
.status small,
.ro {
  color: #94a3b8;
}
.dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: #64748b;
}
.dot.on {
  background: #22c55e;
  box-shadow: 0 0 0 3px rgba(34, 197, 94, 0.25);
}
.dot.off {
  background: #f59e0b;
}
.main {
  min-width: 0;
}
.topbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  padding: 22px 32px;
  background: var(--card);
  border-bottom: 1px solid var(--border);
}
.topbar h1 {
  margin: 0;
  font-size: 22px;
}
.topbar p {
  margin: 2px 0 0;
  color: var(--muted);
}
.content {
  padding: 24px 32px 48px;
  max-width: 1360px;
}
.muted {
  color: var(--muted);
}
.banner {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 16px;
  border-radius: 10px;
  margin-bottom: 16px;
}
.banner span {
  flex: 1;
}
.banner.warn {
  background: var(--warn-soft);
  color: var(--warn);
}
.banner.bad {
  background: var(--bad-soft);
  color: var(--bad);
}
.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 14px;
  margin-bottom: 20px;
}
.card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: var(--shadow);
  padding: 14px 18px;
}
.card span {
  display: block;
  color: var(--muted);
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.card strong {
  font-size: 26px;
}
@media (max-width: 900px) {
  .shell {
    grid-template-columns: 1fr;
  }
  .sidebar {
    position: static;
    height: auto;
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px;
  }
  nav {
    display: flex;
    flex-wrap: wrap;
  }
  nav button {
    width: auto;
  }
  .status {
    margin: 0 0 0 auto;
  }
  .topbar,
  .content {
    padding-left: 18px;
    padding-right: 18px;
  }
}
</style>
