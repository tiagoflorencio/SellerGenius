<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '../api'
import type { AdCampaign, AdsDiagnosisResponse, AdsOverview, CampaignDiagnosis } from '../types'
import {
  formatDateTime,
  formatMoney,
  formatNumber,
  formatPct,
  formatRoas,
  formatShortDate,
  strategyLabel,
} from '../format'
import AdCampaignDrawer from './AdCampaignDrawer.vue'
import AdsSettingsDrawer from './AdsSettingsDrawer.vue'
import FindingList from './FindingList.vue'
import StateChip from './StateChip.vue'

const days = ref(30)
const overview = ref<AdsOverview | null>(null)
const diagnosis = ref<AdsDiagnosisResponse | null>(null)
const showSettings = ref(false)
const showAllTop = ref(false)
const loading = ref(true)
const syncing = ref(false)
const error = ref<string | null>(null)
const onlyActive = ref(true)
const selectedId = ref<number | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    overview.value = await api.adsCampaigns(days.value)
    // O diagnóstico é complementar: se falhar, a tela segue com as métricas.
    diagnosis.value = await api.adsDiagnosis(days.value).catch(() => null)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Não foi possível carregar os dados de Ads.'
  } finally {
    loading.value = false
  }
}

// Busca no Mercado Livre (somente leitura) e salva no banco local; depois recarrega a tela.
async function sync() {
  syncing.value = true
  error.value = null
  try {
    await api.adsSync(days.value)
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Falha ao sincronizar os dados de Ads.'
  } finally {
    syncing.value = false
  }
}

onMounted(load)
watch(days, load)

const campaigns = computed(() =>
  (overview.value?.campaigns ?? []).filter((c) => !onlyActive.value || c.status === 'active' || c.metrics.cost > 0),
)
const selected = computed(() => overview.value?.campaigns.find((c) => c.id === selectedId.value) ?? null)
const diagByCampaign = computed(
  () => new Map<number, CampaignDiagnosis>((diagnosis.value?.campaigns ?? []).map((c) => [c.campaignId, c])),
)

async function settingsSaved() {
  showSettings.value = false
  await load()
}

// Cor do ROAS em relação à meta da campanha: verde = atingiu, amarelo = perto, vermelho = abaixo.
function roasTone(c: AdCampaign): 'ok' | 'warn' | 'bad' | '' {
  if (c.metrics.roas === null || !c.roasTarget) return ''
  if (c.metrics.roas >= c.roasTarget) return 'ok'
  return c.metrics.roas >= c.roasTarget * 0.8 ? 'warn' : 'bad'
}

const periods = [7, 14, 30, 60, 90]
const t = computed(() => overview.value?.totals ?? null)
</script>

<template>
  <section>
    <div class="toolbar">
      <label>
        Período
        <select v-model.number="days">
          <option v-for="p in periods" :key="p" :value="p">Últimos {{ p }} dias</option>
        </select>
      </label>
      <label class="check"><input v-model="onlyActive" type="checkbox" /> Ocultar campanhas pausadas sem gasto</label>
      <span v-if="overview" class="muted">
        {{ formatShortDate(overview.from) }} a {{ formatShortDate(overview.to) }}
        <template v-if="overview.lastSync"> · sincronizado em {{ formatDateTime(overview.lastSync) }}</template>
      </span>
      <button class="push" @click="showSettings = true">Parâmetros</button>
      <button class="primary" :disabled="syncing" @click="sync">
        {{ syncing ? 'Sincronizando...' : 'Sincronizar Ads' }}
      </button>
    </div>

    <p v-if="error" class="banner bad">{{ error }}</p>
    <p v-if="loading && !overview" class="muted">Carregando...</p>

    <template v-else-if="overview">
      <p v-if="overview.campaigns.length === 0" class="muted">
        Nenhuma campanha salva ainda. Clique em "Sincronizar Ads".
      </p>

      <template v-else>
        <div v-if="t" class="cards">
          <div class="card"><span>Investimento</span><strong>{{ formatMoney(t.cost) }}</strong></div>
          <div class="card"><span>Receita (ads)</span><strong>{{ formatMoney(t.revenue) }}</strong></div>
          <div class="card"><span>ROAS</span><strong>{{ formatRoas(t.roas) }}</strong></div>
          <div class="card"><span>ACOS</span><strong>{{ formatPct(t.acos) }}</strong></div>
          <div class="card"><span>TACOS</span><strong>{{ formatPct(t.tacos) }}</strong></div>
          <div class="card"><span>Cliques</span><strong>{{ formatNumber(t.clicks) }}</strong></div>
          <div class="card"><span>Impressões</span><strong>{{ formatNumber(t.prints) }}</strong></div>
          <div class="card"><span>CTR</span><strong>{{ formatPct(t.ctr, 2) }}</strong></div>
          <div class="card"><span>CPC</span><strong>{{ formatMoney(t.cpc) }}</strong></div>
          <div class="card"><span>Conversão</span><strong>{{ formatPct(t.cvr) }}</strong></div>
          <div class="card"><span>Unidades (ads)</span><strong>{{ formatNumber(t.units) }}</strong></div>
          <div class="card"><span>Vendas orgânicas</span><strong>{{ formatMoney(t.organicRevenue) }}</strong></div>
        </div>

        <section v-if="diagnosis && diagnosis.top.length" class="top">
          <h3>Principais achados</h3>
          <p class="hint">
            Diagnóstico por regras (sem IA). Referência de conversão da conta:
            <template v-if="diagnosis.medianCvr !== null">{{ formatPct(diagnosis.medianCvr) }} (mediana de {{ diagnosis.cvrSampleGroups }} anúncios)</template>
            <template v-else>ainda sem volume suficiente</template>.
          </p>
          <div v-for="t in showAllTop ? diagnosis.top : diagnosis.top.slice(0, 5)" :key="t.campaignId + '-' + (t.adGroupId ?? 0) + t.finding.code" class="top-item" @click="selectedId = t.campaignId">
            <small>{{ t.campaignName }}<template v-if="t.adGroupTitle"> › {{ t.adGroupTitle }}</template></small>
            <FindingList :findings="[t.finding]" />
          </div>
          <button v-if="diagnosis.top.length > 5" @click="showAllTop = !showAllTop">
            {{ showAllTop ? 'Mostrar menos' : `Mostrar todos (${diagnosis.top.length})` }}
          </button>
        </section>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Campanha</th>
                <th>Diagnóstico</th>
                <th class="num">Orçamento/dia</th>
                <th class="num">Uso do orç.</th>
                <th class="num">Invest.</th>
                <th class="num">Receita</th>
                <th class="num">ROAS (meta)</th>
                <th class="num">ACOS</th>
                <th class="num">TACOS</th>
                <th class="num">Cliques</th>
                <th class="num">CTR</th>
                <th class="num">CPC</th>
                <th class="num">Conv.</th>
                <th class="num" title="Impressões perdidas por orçamento / por ranking">Perdas orç. / rank</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="c in campaigns" :key="c.id" class="clickable" @click="selectedId = c.id">
                <td class="name-cell">
                  <strong>{{ c.name }}</strong>
                  <small>
                    <span class="status" :class="c.status">{{ c.status === 'active' ? 'Ativa' : 'Pausada' }}</span>
                    {{ strategyLabel[c.strategy ?? ''] ?? c.strategy ?? '' }}
                  </small>
                </td>
                <td>
                  <template v-if="diagByCampaign.get(c.id)">
                    <StateChip :state="diagByCampaign.get(c.id)!.diagnosis.state" />
                    <small v-if="diagByCampaign.get(c.id)!.diagnosis.findings.length" class="count">
                      {{ diagByCampaign.get(c.id)!.diagnosis.findings.length }} achado(s)
                    </small>
                  </template>
                </td>
                <td class="num">{{ formatMoney(c.budget) }}</td>
                <td class="num">{{ formatPct(c.budgetUsagePct, 0) }}</td>
                <td class="num">{{ formatMoney(c.metrics.cost) }}</td>
                <td class="num">{{ formatMoney(c.metrics.revenue) }}</td>
                <td class="num">
                  <span class="roas" :class="roasTone(c)">{{ formatRoas(c.metrics.roas) }}</span>
                  <small v-if="c.roasTarget"> ({{ formatRoas(c.roasTarget) }})</small>
                </td>
                <td class="num">{{ formatPct(c.metrics.acos) }}</td>
                <td class="num">{{ formatPct(c.metrics.tacos) }}</td>
                <td class="num">{{ formatNumber(c.metrics.clicks) }}</td>
                <td class="num">{{ formatPct(c.metrics.ctr, 2) }}</td>
                <td class="num">{{ formatMoney(c.metrics.cpc) }}</td>
                <td class="num">{{ formatPct(c.metrics.cvr) }}</td>
                <td class="num">
                  <template v-if="c.lostByBudget !== null && c.lostByRank !== null">
                    {{ formatPct(c.lostByBudget * 100, 0) }} / {{ formatPct(c.lostByRank * 100, 0) }}
                  </template>
                  <template v-else>–</template>
                </td>
              </tr>
              <tr v-if="campaigns.length === 0">
                <td colspan="14" class="empty">Nenhuma campanha com esses filtros.</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="note">
          Dados atualizados pelo Mercado Livre às 10h (horário de Brasília) e referentes até o dia anterior. A venda é
          atribuída ao anúncio em até 14 dias após o clique. Clique em uma campanha para ver o detalhe diário.
        </p>
      </template>
    </template>

    <AdCampaignDrawer
      v-if="selected"
      :campaign="selected"
      :days="days"
      :diagnosis="diagByCampaign.get(selected.id) ?? null"
      @close="selectedId = null"
    />
    <AdsSettingsDrawer v-if="showSettings && diagnosis" :settings="diagnosis.settings" @close="showSettings = false" @saved="settingsSaved" />
  </section>
</template>

<style scoped>
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  margin-bottom: 14px;
}
.toolbar label {
  display: flex;
  align-items: center;
  gap: 8px;
}
.push {
  margin-left: auto;
}
.muted {
  color: var(--muted);
}
.banner {
  padding: 10px 14px;
  border-radius: 8px;
}
.banner.bad {
  background: var(--bad-soft);
  color: var(--bad);
}
.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 10px;
  margin-bottom: 16px;
}
.card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 12px 14px;
}
.card span {
  display: block;
  color: var(--muted);
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.card strong {
  font-size: 22px;
}
.table-wrap {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  overflow-x: auto;
}
table {
  width: 100%;
  border-collapse: collapse;
}
th,
td {
  padding: 10px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}
th {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
  white-space: nowrap;
}
.num {
  text-align: right;
  white-space: nowrap;
}
tr.clickable {
  cursor: pointer;
}
tr.clickable:hover {
  background: var(--bg);
}
.name-cell {
  min-width: 220px;
}
.name-cell small {
  display: block;
  color: var(--muted);
  margin-top: 2px;
}
small {
  color: var(--muted);
}
.status {
  display: inline-block;
  padding: 0 8px;
  border-radius: 999px;
  font-size: 11px;
  margin-right: 6px;
}
.status.active {
  background: var(--ok-soft);
  color: var(--ok);
}
.status.paused {
  background: var(--info-soft);
  color: var(--muted);
}
.roas {
  font-weight: 700;
}
.roas.ok {
  color: var(--ok);
}
.roas.warn {
  color: var(--warn);
}
.roas.bad {
  color: var(--bad);
}
.empty {
  text-align: center;
  color: var(--muted);
  padding: 28px;
}
.note {
  color: var(--muted);
  font-size: 12px;
  margin-top: 10px;
}
.top {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 12px 16px;
  margin-bottom: 16px;
}
.top h3 {
  margin: 0;
}
.hint {
  color: var(--muted);
  font-size: 12px;
  margin: 2px 0 10px;
}
.top-item {
  cursor: pointer;
  margin-bottom: 6px;
}
.top-item small {
  display: block;
  margin-bottom: 2px;
}
.count {
  display: block;
  margin-top: 2px;
}
</style>
