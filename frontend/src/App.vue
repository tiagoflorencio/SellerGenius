<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, ApiError } from './api'
import type { Diagnosis, Listing, ListingAds, MlUser, Row, Suggestion } from './types'
import { formatDateTime, formatNumber, formatPercent } from './format'
import type { OfficeAgent } from './office'
import ListingsTable from './components/ListingsTable.vue'
import SuggestionDrawer from './components/SuggestionDrawer.vue'
import OfficeView from './components/OfficeView.vue'
import AdsView from './components/AdsView.vue'

// Tela atual: o escritório visual, o painel de anúncios ou o Product Ads.
const view = ref<'office' | 'dashboard' | 'ads'>('office')

// ref(): variável reativa. Quando o valor muda, a tela é atualizada sozinha.
const listings = ref<Listing[]>([])
const diagnoses = ref<Diagnosis[]>([])
const suggestions = ref<Suggestion[]>([])
const adsByListing = ref<ListingAds[]>([])
const me = ref<MlUser | null>(null)
const connected = ref<boolean | null>(null) // null = ainda verificando

const error = ref<string | null>(null)
const loading = ref(true)
const syncing = ref(false)
const suggestingId = ref<string | null>(null)
const deciding = ref(false)
const selectedId = ref<string | null>(null)

async function loadData() {
  try {
    ;[listings.value, diagnoses.value, suggestions.value, adsByListing.value] = await Promise.all([
      api.listings(),
      api.diagnosis(),
      api.suggestions(),
      // Ads é opcional: se ainda não foi sincronizado (ou a API antiga não tem a rota), a tabela segue sem essas colunas.
      api.adsByListing().catch(() => [] as ListingAds[]),
    ])
  } catch {
    error.value = 'Não foi possível falar com a API. Ela está rodando em http://localhost:5172?'
  }
}

async function loadConnection() {
  try {
    me.value = await api.me()
    connected.value = true
  } catch (e) {
    connected.value = e instanceof ApiError && e.status === 401 ? false : null
  }
}

// Envolve uma ação: mostra erro na tela se algo falhar.
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

// O estado do agente no escritório é derivado dos dados reais (não é só enfeite).
const pendingCount = computed(() => suggestions.value.filter((s) => s.status === 'Pending').length)

const officeAgents = computed<OfficeAgent[]>(() => {
  let status: OfficeAgent['status'] = 'idle'
  let message = 'Pronto para trabalhar'

  if (connected.value === false) {
    status = 'offline'
    message = 'Conecte o Mercado Livre'
  } else if (syncing.value) {
    status = 'working'
    message = 'Sincronizando anúncios...'
  } else if (suggestingId.value) {
    status = 'working'
    message = 'Analisando um anúncio...'
  } else if (pendingCount.value > 0) {
    status = 'waiting'
    message = `${pendingCount.value} sugestão(ões) para você`
  }

  return [
    {
      id: 'anuncios',
      name: 'Ana',
      role: 'Agente de anúncios',
      departmentId: 'marketplaces',
      seat: 0,
      color: '#3b5bdb',
      status,
      message,
      details: [
        { label: 'Anúncios ativos', value: String(stats.value.active) },
        { label: 'Com alertas', value: String(stats.value.withAlerts) },
        { label: 'Sugestões pendentes', value: String(pendingCount.value) },
      ],
    },
  ]
})

onMounted(async () => {
  await Promise.all([loadData(), loadConnection()])
  loading.value = false
})
</script>

<template>
  <div class="page">
    <header class="top">
      <div>
        <h1>AgentOffice</h1>
        <p class="subtitle">
          Agente de anúncios
          <template v-if="me"> · conta {{ me.nickname }}</template>
          <template v-if="lastSync"> · última sincronização {{ formatDateTime(lastSync) }}</template>
        </p>
      </div>
      <div class="top-actions">
        <span class="badge ok">Somente leitura no Mercado Livre</span>
        <button class="primary" :disabled="syncing || connected === false" @click="sync">
          {{ syncing ? 'Sincronizando...' : 'Sincronizar anúncios' }}
        </button>
      </div>
    </header>

    <div v-if="connected === false" class="banner warn">
      Você ainda não conectou sua conta do Mercado Livre.
      <a href="/api/auth/login">Conectar agora</a>
    </div>
    <div v-if="error" class="banner bad">{{ error }}</div>

    <nav class="tabs">
      <button :class="{ active: view === 'office' }" @click="view = 'office'">Escritório</button>
      <button :class="{ active: view === 'dashboard' }" @click="view = 'dashboard'">
        Painel de anúncios
        <span v-if="pendingCount > 0" class="pill">{{ pendingCount }}</span>
      </button>
      <button :class="{ active: view === 'ads' }" @click="view = 'ads'">Product Ads</button>
    </nav>

    <AdsView v-if="view === 'ads'" />

    <p v-else-if="loading" class="muted">Carregando...</p>

    <OfficeView
      v-else-if="view === 'office'"
      :agents="officeAgents"
      @open-dashboard="view = 'dashboard'"
      @sync="sync"
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
.page {
  max-width: 1280px;
  margin: 0 auto;
  padding: 24px;
}
.top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  margin-bottom: 18px;
}
h1 {
  margin: 0;
}
.tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 16px;
}
.tabs button.active {
  background: var(--primary-soft);
  border-color: var(--primary);
  color: var(--primary);
  font-weight: 600;
}
.pill {
  background: var(--warn);
  color: #fff;
  border-radius: 999px;
  padding: 1px 7px;
  font-size: 12px;
  margin-left: 4px;
}
.subtitle,
.muted {
  margin: 2px 0 0;
  color: var(--muted);
}
.top-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.badge {
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
}
.badge.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.banner {
  padding: 10px 14px;
  border-radius: 8px;
  margin-bottom: 14px;
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
  gap: 12px;
  margin-bottom: 18px;
}
.card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 14px 16px;
}
.card span {
  display: block;
  color: var(--muted);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.card strong {
  font-size: 26px;
}
</style>
