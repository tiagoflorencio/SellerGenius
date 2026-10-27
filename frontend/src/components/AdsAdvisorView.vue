<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { activity } from '../activity'
import { api } from '../api'
import type { AdRecommendation, RecommendationStatus, RecommendationsResponse } from '../types'
import { formatDateTime, formatMoney, formatNumber } from '../format'
import FindingList from './FindingList.vue'

const props = defineProps<{ days: number }>()

const data = ref<RecommendationsResponse | null>(null)
const loading = ref(true)
const analyzing = ref(false)
const error = ref<string | null>(null)
const filter = ref<'Pending' | 'Approved' | 'Rejected' | 'all'>('Pending')
const openId = ref<number | null>(null)
const notes = ref<Record<number, string>>({})
const deciding = ref<number | null>(null)

async function load(runId?: number) {
  try {
    data.value = await api.adsRecommendations(runId)
    activity.adsPending = data.value.pendingTotal
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Não foi possível carregar as recomendações.'
  } finally {
    loading.value = false
  }
}

onMounted(() => load())

// Roda o agente (consome créditos da OpenAI) e carrega o resultado.
async function analyze() {
  analyzing.value = true
  activity.adsAnalyzing = true
  error.value = null
  try {
    await api.adsAnalyze(props.days)
    filter.value = 'Pending'
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Falha ao analisar.'
  } finally {
    analyzing.value = false
    activity.adsAnalyzing = false
  }
}

async function decide(rec: AdRecommendation, decision: 'approve' | 'reject') {
  deciding.value = rec.id
  error.value = null
  try {
    await api.decideAdsRecommendation(rec.id, decision, notes.value[rec.id] ?? '')
    await load(data.value?.run?.id)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Falha ao registrar a decisão.'
  } finally {
    deciding.value = null
  }
}

const visible = computed(() =>
  (data.value?.recommendations ?? []).filter((r) => filter.value === 'all' || r.status === filter.value),
)
const countBy = (status: RecommendationStatus) => (data.value?.recommendations ?? []).filter((r) => r.status === status).length

const actionLabel: Record<string, string> = {
  increase_budget: 'Aumentar orçamento',
  decrease_budget: 'Reduzir orçamento',
  adjust_roas_target: 'Ajustar meta de ROAS',
  pause_ad_group: 'Pausar anúncio na campanha',
  move_ad_group_to_growth_campaign: 'Mover para campanha de crescimento',
  move_ad_group_to_profit_campaign: 'Mover para campanha de rentabilidade',
  improve_listing: 'Melhorar publicação',
  review_offer: 'Revisar oferta',
  review_costs: 'Revisar custos e margem',
  collect_more_data: 'Coletar mais dados',
  no_action: 'Manter como está',
}

const confidenceLabel: Record<string, string> = { alta: 'Confiança alta', media: 'Confiança média', baixa: 'Confiança baixa' }
const statusLabel: Record<RecommendationStatus, string> = { Pending: 'Pendente', Approved: 'Aprovada', Rejected: 'Rejeitada' }
const scoreLabels = [
  ['impact', 'Impacto'],
  ['confidence', 'Confiança'],
  ['urgency', 'Urgência'],
  ['ease', 'Facilidade'],
  ['reversibility', 'Reversibilidade'],
] as const

const run = computed(() => data.value?.run ?? null)
</script>

<template>
  <section>
    <div class="toolbar">
      <button class="primary" :disabled="analyzing" @click="analyze">
        {{ analyzing ? 'Analisando... (pode levar alguns segundos)' : 'Analisar com IA' }}
      </button>
      <span class="muted">
        Usa os dados já sincronizados (últimos {{ days }} dias) e a OpenAI (centavos por análise). Nada é alterado no
        Mercado Livre.
      </span>
      <select
        v-if="data && data.runs.length > 1"
        class="push"
        :value="run?.id"
        @change="load(Number(($event.target as HTMLSelectElement).value))"
      >
        <option v-for="r in data.runs" :key="r.id" :value="r.id">
          Análise de {{ formatDateTime(r.createdAt) }} ({{ r.recommendations }})
        </option>
      </select>
    </div>

    <p v-if="error" class="banner bad">{{ error }}</p>
    <p v-if="loading" class="muted">Carregando...</p>

    <p v-else-if="!run" class="muted">
      Nenhuma análise ainda. Sincronize os dados de Ads e clique em "Analisar com IA".
    </p>

    <template v-else>
      <div class="summary">
        <h3>Resumo do agente</h3>
        <p>{{ run.summary || 'Sem resumo.' }}</p>
        <p class="meta">
          {{ formatDateTime(run.createdAt) }} · {{ run.model }} · manual {{ run.manualHash }}
          <template v-if="run.promptTokens"> · {{ formatNumber(run.promptTokens) }} + {{ formatNumber(run.completionTokens) }} tokens</template>
        </p>
        <p v-if="run.dataQuality.estado" class="meta">
          Qualidade dos dados: <strong>{{ run.dataQuality.estado }}</strong>
          <template v-if="run.dataQuality.faltantes?.length"> · faltam: {{ run.dataQuality.faltantes.join('; ') }}</template>
        </p>
        <ul v-if="run.dataQuality.alertas?.length" class="alerts">
          <li v-for="a in run.dataQuality.alertas" :key="a">{{ a }}</li>
        </ul>
      </div>

      <div class="filters">
        <button :class="{ active: filter === 'Pending' }" @click="filter = 'Pending'">Pendentes ({{ countBy('Pending') }})</button>
        <button :class="{ active: filter === 'Approved' }" @click="filter = 'Approved'">Aprovadas ({{ countBy('Approved') }})</button>
        <button :class="{ active: filter === 'Rejected' }" @click="filter = 'Rejected'">Rejeitadas ({{ countBy('Rejected') }})</button>
        <button :class="{ active: filter === 'all' }" @click="filter = 'all'">Todas</button>
      </div>

      <p v-if="visible.length === 0" class="muted">Nenhuma recomendação nesta lista.</p>

      <article v-for="r in visible" :key="r.id" class="rec" :class="r.status.toLowerCase()">
        <header @click="openId = openId === r.id ? null : r.id">
          <div class="score" :title="'Prioridade calculada pelo código (0 a 100)'">{{ Math.round(r.priority) }}</div>
          <div class="head-text">
            <div class="chips">
              <span class="chip action">{{ actionLabel[r.action] ?? r.action }}</span>
              <span class="chip">{{ r.where }}</span>
              <span class="chip" :class="r.confidence">{{ confidenceLabel[r.confidence] ?? r.confidence }}</span>
              <span v-if="r.impactBrl" class="chip impact">{{ formatMoney(r.impactBrl) }} em jogo</span>
              <span v-else-if="r.impactUnknown" class="chip" title="Sem dados para estimar o impacto em reais">impacto desconhecido</span>
              <span v-if="r.status !== 'Pending'" class="chip status">{{ statusLabel[r.status] }}</span>
            </div>
            <strong>{{ r.title }}</strong>
            <small v-if="r.campaignName">
              {{ r.campaignName }}<template v-if="r.adGroupTitle"> › {{ r.adGroupTitle }}</template>
            </small>
          </div>
          <span class="toggle">{{ openId === r.id ? '▲' : '▼' }}</span>
        </header>

        <div v-if="openId === r.id" class="body">
          <h4>Diagnóstico</h4>
          <p>{{ r.content.diagnostico }}</p>

          <template v-if="r.content.evidencias.length">
            <h4>Evidências</h4>
            <ul><li v-for="e in r.content.evidencias" :key="e">{{ e }}</li></ul>
          </template>

          <template v-if="r.content.hipoteses.length">
            <h4>Hipóteses (não confirmadas)</h4>
            <ul><li v-for="h in r.content.hipoteses" :key="h">{{ h }}</li></ul>
          </template>

          <template v-if="r.content.teste_sugerido">
            <h4>Teste sugerido</h4>
            <dl class="test">
              <template v-if="r.content.teste_sugerido.mudanca"><dt>Mudança</dt><dd>{{ r.content.teste_sugerido.mudanca }}</dd></template>
              <template v-if="r.content.teste_sugerido.magnitude"><dt>Magnitude</dt><dd>{{ r.content.teste_sugerido.magnitude }}</dd></template>
              <template v-if="r.content.teste_sugerido.janela_avaliacao_dias"><dt>Janela de avaliação</dt><dd>{{ r.content.teste_sugerido.janela_avaliacao_dias }} dias</dd></template>
              <template v-if="r.content.teste_sugerido.metrica_sucesso"><dt>Métrica</dt><dd>{{ r.content.teste_sugerido.metrica_sucesso }}</dd></template>
              <template v-if="r.content.teste_sugerido.criterio"><dt>Critério de sucesso</dt><dd>{{ r.content.teste_sugerido.criterio }}</dd></template>
            </dl>
          </template>

          <template v-if="r.content.efeito_esperado">
            <h4>Efeito esperado</h4>
            <p>{{ r.content.efeito_esperado }}</p>
          </template>

          <template v-if="r.content.riscos.length">
            <h4>Riscos</h4>
            <ul><li v-for="x in r.content.riscos" :key="x">{{ x }}</li></ul>
          </template>

          <template v-if="r.content.achados.length">
            <h4>Achados por regras que sustentam</h4>
            <FindingList :findings="r.content.achados" compact />
          </template>

          <h4>Prioridade</h4>
          <div class="bars">
            <div v-for="[key, label] in scoreLabels" :key="key" class="bar" :title="`${label}: ${r.content.pontuacao[key]}`">
              <span>{{ label }}</span>
              <div><i :style="{ width: r.content.pontuacao[key] + '%' }"></i></div>
              <b>{{ Math.round(r.content.pontuacao[key]) }}</b>
            </div>
          </div>

          <div class="decision">
            <template v-if="r.status === 'Pending'">
              <textarea v-model="notes[r.id]" rows="2" placeholder="Motivo da decisão (opcional, mas ajuda o agente a aprender)"></textarea>
              <div class="buttons">
                <button class="primary" :disabled="deciding === r.id" @click="decide(r, 'approve')">Aprovar</button>
                <button :disabled="deciding === r.id" @click="decide(r, 'reject')">Rejeitar</button>
              </div>
              <small>
                Aprovar só registra a sua decisão. Se concordar, aplique a mudança manualmente no Mercado Livre; este
                sistema não altera a conta.
              </small>
            </template>
            <template v-else>
              <p>
                <strong>{{ statusLabel[r.status] }}</strong>
                <template v-if="r.decidedAt"> em {{ formatDateTime(r.decidedAt) }}</template>
                <template v-if="r.decisionNote">: "{{ r.decisionNote }}"</template>
              </p>
            </template>
          </div>
        </div>
      </article>

      <details v-if="run.discarded.length || run.dataQuality.naoRecomendado?.length" class="discarded">
        <summary>Itens descartados e não recomendados</summary>
        <template v-if="run.discarded.length">
          <h4>Descartados pelas travas do sistema</h4>
          <ul><li v-for="d in run.discarded" :key="d.title + d.reason"><strong>{{ d.title }}</strong>: {{ d.reason }}</li></ul>
        </template>
        <template v-if="run.dataQuality.naoRecomendado?.length">
          <h4>O agente decidiu não recomendar</h4>
          <ul><li v-for="n in run.dataQuality.naoRecomendado" :key="(n.tema ?? '') + (n.motivo ?? '')"><strong>{{ n.tema }}</strong>: {{ n.motivo }}</li></ul>
        </template>
      </details>
    </template>
  </section>
</template>

<style scoped>
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}
.push {
  margin-left: auto;
}
.muted,
.meta,
small {
  color: var(--muted);
}
.meta {
  font-size: 12px;
  margin: 4px 0 0;
}
.banner.bad {
  background: var(--bad-soft);
  color: var(--bad);
  padding: 10px 14px;
  border-radius: 8px;
}
.summary {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 12px 16px;
  margin-bottom: 14px;
}
.summary h3 {
  margin: 0 0 4px;
}
.alerts {
  color: var(--warn);
  margin: 6px 0 0;
}
.filters {
  display: flex;
  gap: 6px;
  margin-bottom: 12px;
}
.filters button.active {
  background: var(--primary-soft);
  border-color: var(--primary);
  color: var(--primary);
  font-weight: 600;
}
.rec {
  background: var(--card);
  border: 1px solid var(--border);
  border-left: 5px solid var(--primary);
  border-radius: 12px;
  margin-bottom: 10px;
}
.rec.approved {
  border-left-color: var(--ok);
}
.rec.rejected {
  border-left-color: var(--muted);
  opacity: 0.8;
}
.rec header {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  cursor: pointer;
}
.score {
  flex: none;
  width: 46px;
  height: 46px;
  border-radius: 50%;
  background: var(--primary-soft);
  color: var(--primary);
  font-weight: 800;
  font-size: 18px;
  display: grid;
  place-items: center;
}
.head-text {
  flex: 1;
  display: grid;
  gap: 3px;
}
.head-text small {
  display: block;
}
.toggle {
  color: var(--muted);
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.chip {
  font-size: 11px;
  background: var(--info-soft);
  color: var(--muted);
  border-radius: 999px;
  padding: 1px 9px;
}
.chip.action {
  background: var(--primary-soft);
  color: var(--primary);
  font-weight: 600;
}
.chip.alta {
  background: var(--ok-soft);
  color: var(--ok);
}
.chip.media {
  background: var(--warn-soft);
  color: var(--warn);
}
.chip.baixa {
  background: var(--bad-soft);
  color: var(--bad);
}
.chip.impact {
  background: var(--bad-soft);
  color: var(--bad);
}
.chip.status {
  font-weight: 600;
}
.body {
  padding: 0 18px 16px 76px;
}
.body h4 {
  margin: 14px 0 4px;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
}
.body p {
  margin: 0;
}
.body ul {
  margin: 0;
  padding-left: 18px;
}
.test {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 12px;
  background: var(--bg);
  border-radius: 8px;
  padding: 10px 12px;
  margin: 0;
}
.test dt {
  color: var(--muted);
  font-size: 12px;
}
.test dd {
  margin: 0;
}
.bars {
  display: grid;
  gap: 4px;
  max-width: 420px;
}
.bar {
  display: grid;
  grid-template-columns: 110px 1fr 30px;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.bar > div {
  height: 8px;
  background: var(--info-soft);
  border-radius: 999px;
  overflow: hidden;
}
.bar i {
  display: block;
  height: 100%;
  background: var(--primary);
}
.decision {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
  display: grid;
  gap: 8px;
}
.decision textarea {
  font: inherit;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 8px;
  resize: vertical;
}
.buttons {
  display: flex;
  gap: 10px;
}
.discarded {
  margin-top: 16px;
  color: var(--muted);
}
.discarded summary {
  cursor: pointer;
}
</style>
