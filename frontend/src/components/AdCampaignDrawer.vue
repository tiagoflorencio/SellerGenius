<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '../api'
import type { AdCampaign, AdCampaignDetail, CampaignDiagnosis } from '../types'
import {
  formatDateTime,
  formatMoney,
  formatNumber,
  formatPct,
  formatRoas,
  formatShortDate,
  strategyLabel,
} from '../format'
import FindingList from './FindingList.vue'
import LineChart from './LineChart.vue'
import StateChip from './StateChip.vue'

const props = defineProps<{
  campaign: AdCampaign
  days: number
  diagnosis: CampaignDiagnosis | null
}>()

const emit = defineEmits<{ close: [] }>()

const detail = ref<AdCampaignDetail | null>(null)
const error = ref<string | null>(null)

async function load() {
  error.value = null
  try {
    detail.value = await api.adsCampaign(props.campaign.id, props.days)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Erro ao carregar a campanha.'
  }
}

onMounted(load)
watch(() => [props.campaign.id, props.days], load)

const labels = computed(() => detail.value?.daily.map((d) => formatShortDate(d.date)) ?? [])

const moneySeries = computed(() => [
  { name: 'Receita (ads)', color: '#2b8a3e', values: detail.value?.daily.map((d) => d.metrics.revenue) ?? [] },
  { name: 'Investimento', color: '#c92a2a', values: detail.value?.daily.map((d) => d.metrics.cost) ?? [] },
])

const roasSeries = computed(() => [
  { name: 'ROAS', color: '#3b5bdb', values: detail.value?.daily.map((d) => d.metrics.roas) ?? [] },
])

const shareSeries = computed(() => [
  { name: 'Impressões ganhas', color: '#3b5bdb', values: detail.value?.daily.map((d) => d.impressionShare) ?? [] },
  { name: 'Perdidas por orçamento', color: '#e8890c', values: detail.value?.daily.map((d) => d.lostByBudget) ?? [] },
  { name: 'Perdidas por ranking', color: '#868e96', values: detail.value?.daily.map((d) => d.lostByRank) ?? [] },
])

const typeLabel: Record<string, string> = { ITEM: 'Item', FAMILY: 'Família', CATALOG: 'Catálogo' }
const m = computed(() => props.campaign.metrics)

// Diagnóstico de cada anúncio da campanha, pelo id.
const groupDiag = computed(() => new Map((props.diagnosis?.groups ?? []).map((g) => [g.adGroupId, g.diagnosis])))

const fieldLabel: Record<string, string> = {
  Budget: 'Orçamento',
  RoasTarget: 'Meta de ROAS',
  Status: 'Status',
  Strategy: 'Estratégia',
  AutomaticBudget: 'Orçamento automático',
}
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <aside class="drawer">
      <header>
        <div>
          <h2>{{ campaign.name }}</h2>
          <small>
            {{ campaign.status === 'active' ? 'Ativa' : 'Pausada' }} ·
            {{ strategyLabel[campaign.strategy ?? ''] ?? campaign.strategy ?? '–' }} · orçamento
            {{ formatMoney(campaign.budget) }}/dia
          </small>
        </div>
        <button @click="emit('close')">Fechar</button>
      </header>

      <dl class="kpis">
        <div><dt>Investimento</dt><dd>{{ formatMoney(m.cost) }}</dd></div>
        <div><dt>Receita (ads)</dt><dd>{{ formatMoney(m.revenue) }}</dd></div>
        <div>
          <dt>ROAS</dt>
          <dd>{{ formatRoas(m.roas) }}<small v-if="campaign.roasTarget"> / meta {{ formatRoas(campaign.roasTarget) }}</small></dd>
        </div>
        <div><dt>ACOS</dt><dd>{{ formatPct(m.acos) }}</dd></div>
        <div><dt>TACOS</dt><dd>{{ formatPct(m.tacos) }}</dd></div>
        <div><dt>Conversão</dt><dd>{{ formatPct(m.cvr) }}</dd></div>
      </dl>

      <p v-if="error" class="error">{{ error }}</p>
      <p v-else-if="!detail" class="muted">Carregando...</p>

      <template v-else>
        <h3>Diagnóstico <StateChip v-if="diagnosis" :state="diagnosis.diagnosis.state" /></h3>
        <FindingList v-if="diagnosis && diagnosis.diagnosis.findings.length" :findings="diagnosis.diagnosis.findings" />
        <p v-else class="muted">Nenhum achado para esta campanha com os parâmetros atuais.</p>

        <h3>Investimento x receita por dia</h3>
        <LineChart :labels="labels" :series="moneySeries" :format="(v) => `R$ ${Math.round(v)}`" />

        <h3>ROAS por dia</h3>
        <LineChart
          :labels="labels"
          :series="roasSeries"
          :hline="campaign.roasTarget ? { value: campaign.roasTarget, label: 'Meta de ROAS', color: '#e8890c' } : null"
          :format="(v) => `${v.toFixed(1)}x`"
        />

        <h3>Competitividade (impressões)</h3>
        <LineChart :labels="labels" :series="shareSeries" :format="(v) => `${Math.round(v * 100)}%`" />
        <p class="hint">
          Impressões ganhas, perdidas por orçamento e perdidas por ranking somam 100%. Muita perda por orçamento indica
          orçamento baixo; muita perda por ranking indica meta de ROAS alta ou anúncios com pouca qualidade.
        </p>

        <h3>Anúncios da campanha</h3>
        <p v-if="detail.adGroupsPeriod" class="hint">
          Métricas de {{ formatShortDate(detail.adGroupsPeriod.from) }} a {{ formatShortDate(detail.adGroupsPeriod.to) }}
          (período da última sincronização).
        </p>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Anúncio</th>
                <th class="num">Invest.</th>
                <th class="num">Receita</th>
                <th class="num">ROAS</th>
                <th class="num">ACOS</th>
                <th class="num">Cliques</th>
                <th class="num">CTR</th>
                <th class="num">Conv.</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="g in detail.adGroups" :key="g.id" :class="{ inactive: g.status !== 'ACTIVE' }">
                <td class="title-cell">
                  <img v-if="g.thumbnail" :src="g.thumbnail" alt="" loading="lazy" />
                  <div>
                    <strong>{{ g.title }}</strong>
                    <small>
                      {{ typeLabel[g.type] ?? g.type }} · {{ g.externalId }}
                      <template v-if="g.status !== 'ACTIVE'"> · {{ g.status }}</template>
                    </small>
                    <span v-for="t in g.tags" :key="t" class="tag">{{ t }}</span>
                    <small v-if="g.listings.length" class="linked">
                      Publicações: {{ g.listings.slice(0, 2).map((l) => l.itemId).join(', ') }}<template v-if="g.listings.length > 2"> +{{ g.listings.length - 2 }}</template>
                    </small>
                    <small v-else class="linked">Sem publicação ligada no painel</small>
                    <div v-if="groupDiag.get(g.id)" class="group-diag">
                      <StateChip :state="groupDiag.get(g.id)!.state" />
                      <FindingList :findings="groupDiag.get(g.id)!.findings.filter((f) => f.severity !== 'info')" compact />
                    </div>
                  </div>
                </td>
                <td class="num">{{ formatMoney(g.metrics.cost) }}</td>
                <td class="num">{{ formatMoney(g.metrics.revenue) }}</td>
                <td class="num">{{ formatRoas(g.metrics.roas) }}</td>
                <td class="num">{{ formatPct(g.metrics.acos) }}</td>
                <td class="num">{{ formatNumber(g.metrics.clicks) }}</td>
                <td class="num">{{ formatPct(g.metrics.ctr, 2) }}</td>
                <td class="num">{{ formatPct(g.metrics.cvr) }}</td>
              </tr>
              <tr v-if="detail.adGroups.length === 0">
                <td colspan="8" class="muted">Nenhum anúncio nesta campanha.</td>
              </tr>
            </tbody>
          </table>
        </div>

        <h3>Alterações detectadas na campanha</h3>
        <p class="hint">
          Mudanças de orçamento, meta de ROAS, status ou estratégia percebidas entre sincronizações. A data é a da
          sincronização que notou; servem de base para comparar o antes e o depois.
        </p>
        <ul v-if="detail.changes.length" class="changes">
          <li v-for="c in detail.changes" :key="c.id">
            <strong>{{ fieldLabel[c.field] ?? c.field }}</strong>: {{ c.oldValue || '–' }} → {{ c.newValue || '–' }}
            <small>({{ formatDateTime(c.detectedAt) }})</small>
          </li>
        </ul>
        <p v-else class="muted">Nenhuma alteração detectada desde que o histórico começou a ser guardado.</p>
      </template>
    </aside>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(20, 25, 40, 0.35);
  display: flex;
  justify-content: flex-end;
  z-index: 10;
}
.drawer {
  width: min(820px, 100%);
  height: 100%;
  background: var(--card);
  padding: 20px 24px;
  overflow-y: auto;
}
header {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: flex-start;
}
h2 {
  margin: 0;
}
h3 {
  margin: 22px 0 6px;
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
}
small,
.muted,
.hint {
  color: var(--muted);
}
.hint {
  font-size: 12px;
  margin: 4px 0 8px;
}
.error {
  color: var(--bad);
}
.kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 10px;
  margin: 16px 0 0;
}
.kpis > div {
  background: var(--bg);
  border-radius: 10px;
  padding: 8px 12px;
}
.kpis dt {
  font-size: 11px;
  text-transform: uppercase;
  color: var(--muted);
}
.kpis dd {
  margin: 0;
  font-size: 18px;
  font-weight: 700;
}
.kpis dd small {
  font-size: 11px;
  font-weight: 400;
}
.table-wrap {
  border: 1px solid var(--border);
  border-radius: 10px;
  overflow-x: auto;
}
table {
  width: 100%;
  border-collapse: collapse;
}
th,
td {
  padding: 8px 10px;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}
th {
  font-size: 11px;
  text-transform: uppercase;
  color: var(--muted);
  white-space: nowrap;
}
.num {
  text-align: right;
  white-space: nowrap;
}
.title-cell {
  display: flex;
  gap: 10px;
  min-width: 260px;
}
.title-cell img {
  width: 40px;
  height: 40px;
  object-fit: cover;
  border-radius: 6px;
  flex: none;
}
.title-cell small {
  display: block;
}
.tag {
  display: inline-block;
  background: var(--info-soft);
  color: var(--muted);
  border-radius: 999px;
  padding: 0 7px;
  font-size: 11px;
  margin: 2px 4px 0 0;
}
tr.inactive {
  opacity: 0.55;
}
.group-diag {
  margin-top: 6px;
}
.group-diag :deep(.findings) {
  margin-top: 4px;
}
.changes {
  padding-left: 18px;
}
</style>
