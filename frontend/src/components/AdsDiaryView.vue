<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import type { AdCampaign, DiaryEvent, DiaryResponse, EvaluationVerdict, MetricDelta } from '../types'
import { formatDay, formatMoney, formatNumber, formatPct, formatRoas, strategyLabel, todayIso } from '../format'

const data = ref<DiaryResponse | null>(null)
const campaigns = ref<AdCampaign[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const openKey = ref<string | null>(null)
const noteDrafts = ref<Record<string, string>>({})
const saving = ref(false)

const newNote = ref({ date: todayIso(), campaignId: '' as string | number, text: '' })

async function load() {
  try {
    data.value = await api.adsDiary()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Não foi possível carregar o diário.'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  load()
  api.adsCampaigns(30).then((o) => (campaigns.value = o.campaigns)).catch(() => {})
})

async function run(action: () => Promise<unknown>, failure: string) {
  saving.value = true
  error.value = null
  try {
    await action()
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : failure
  } finally {
    saving.value = false
  }
}

const saveNote = (ev: DiaryEvent) =>
  run(() => api.saveChangeNote(ev.noteChangeId!, noteDrafts.value[ev.key] ?? ev.note ?? ''), 'Falha ao salvar o motivo.')

const addNote = () =>
  run(async () => {
    const id = newNote.value.campaignId === '' ? null : Number(newNote.value.campaignId)
    await api.addDiaryNote(newNote.value.date, id, newNote.value.text)
    newNote.value.text = ''
  }, 'Falha ao salvar a anotação.')

const removeNote = (id: number) => run(() => api.deleteDiaryNote(id), 'Falha ao apagar a anotação.')

// ---- Textos ----
const fieldLabel: Record<string, string> = {
  Budget: 'Orçamento',
  RoasTarget: 'Meta de ROAS',
  Status: 'Status',
  Strategy: 'Estratégia',
  AutomaticBudget: 'Orçamento automático',
}

function fieldValue(field: string, value: string): string {
  if (value === '') return '–'
  switch (field) {
    case 'Budget': return formatMoney(Number(value))
    case 'RoasTarget': return formatRoas(Number(value))
    case 'Status': return value === 'active' ? 'Ativa' : value === 'paused' ? 'Pausada' : value
    case 'Strategy': return strategyLabel[value] ?? value
    case 'AutomaticBudget': return value === 'True' ? 'Sim' : 'Não'
    default: return value
  }
}

const actionLabel: Record<string, string> = {
  increase_budget: 'Aumentar orçamento',
  decrease_budget: 'Reduzir orçamento',
  adjust_roas_target: 'Ajustar meta de ROAS',
  increase_roas_target: 'Aumentar meta de ROAS',
  decrease_roas_target: 'Reduzir meta de ROAS',
  pause_ad_group: 'Pausar anúncio na campanha',
  move_ad_group_to_growth_campaign: 'Mover para campanha de crescimento',
  move_ad_group_to_profit_campaign: 'Mover para campanha de rentabilidade',
  improve_listing: 'Melhorar publicação',
  review_offer: 'Revisar oferta',
  review_costs: 'Revisar custos e margem',
}

function title(ev: DiaryEvent): string {
  if (ev.changes.length > 0) {
    return ev.changes.map((c) => `${fieldLabel[c.field] ?? c.field}: ${fieldValue(c.field, c.oldValue)} → ${fieldValue(c.field, c.newValue)}`).join(' · ')
  }
  return ev.recommendationTitle ?? 'Mudança aplicada'
}

const verdictLabel: Record<EvaluationVerdict, string> = {
  melhorou: 'Melhorou',
  piorou: 'Piorou',
  misto: 'Misto',
  neutro: 'Sem variação',
  inconclusivo: 'Inconclusivo',
  aguardando: 'Aguardando dados',
}
const statusLabel = { aguardando: 'em andamento', provisorio: 'provisório', final: 'definitivo', indisponivel: 'indisponível' } as const
const confidenceLabel = { alta: 'Confiança alta', media: 'Confiança média', baixa: 'Confiança baixa' } as const

function fmt(d: MetricDelta, value: number | null): string {
  if (value === null) return '–'
  switch (d.unit) {
    case 'x': return formatRoas(value)
    case 'brl': return formatMoney(value)
    case 'pct': return formatPct(value)
    default: return value.toLocaleString('pt-BR', { maximumFractionDigits: 1 })
  }
}

function tone(d: MetricDelta): string {
  if (d.direction === 'up' || d.direction === 'down') {
    if (d.better === 'higher') return d.direction === 'up' ? 'good' : 'bad'
    if (d.better === 'lower') return d.direction === 'down' ? 'good' : 'bad'
  }
  return ''
}

const arrow = { up: '▲', down: '▼', flat: '≈', na: '–' } as const
const signed = (v: number | null) => (v === null ? '–' : `${v > 0 ? '+' : ''}${v.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`)

const events = computed(() => data.value?.events ?? [])
</script>

<template>
  <section>
    <p class="intro">
      Linha do tempo das mudanças nas campanhas (detectadas a cada sincronização ou marcadas por você nas recomendações
      aprovadas) e o que aconteceu com os resultados depois. A comparação é feita pelo sistema, sem IA, e mostra
      <strong>correlação, não prova</strong>: promoções, preço, estoque e outras mudanças no período também influenciam.
    </p>

    <p v-if="error" class="banner bad">{{ error }}</p>
    <p v-if="loading" class="muted">Carregando...</p>

    <template v-else-if="data">
      <div v-if="data.awaiting.length" class="box">
        <h3>Aprovadas, aguardando você informar a aplicação ({{ data.awaiting.length }})</h3>
        <ul>
          <li v-for="a in data.awaiting" :key="a.recommendationId">
            <strong>{{ actionLabel[a.action] ?? a.action }}</strong>: {{ a.title }}
            <small v-if="a.campaignName"> · {{ a.campaignName }}</small>
          </li>
        </ul>
        <small>
          Em "Agente de Ads", abra a recomendação e use "Marcar como aplicada". Mudanças de orçamento e meta de ROAS
          também são detectadas sozinhas na próxima sincronização.
        </small>
      </div>

      <details class="box" :open="data.notes.length > 0">
        <summary>Anotações do diário ({{ data.notes.length }})</summary>
        <p class="muted">
          Registre fatos que explicam resultados: promoção, mudança de preço, falta de estoque, mudança de foto/título.
          Elas entram na avaliação como outros fatores e são enviadas ao agente.
        </p>
        <form class="note-form" @submit.prevent="addNote">
          <input v-model="newNote.date" type="date" :max="todayIso()" required />
          <select v-model="newNote.campaignId">
            <option value="">Conta toda</option>
            <option v-for="c in campaigns" :key="c.id" :value="c.id">{{ c.name }}</option>
          </select>
          <input v-model="newNote.text" class="grow" maxlength="1000" placeholder="Ex.: promoção relâmpago do kit barba, estoque zerado..." required />
          <button class="primary" :disabled="saving">Adicionar</button>
        </form>
        <ul v-if="data.notes.length" class="notes">
          <li v-for="n in data.notes" :key="n.id">
            <b>{{ formatDay(n.date) }}</b> · {{ n.campaignName ?? 'Conta toda' }}: {{ n.text }}
            <button class="link" :disabled="saving" @click="removeNote(n.id)">apagar</button>
          </li>
        </ul>
      </details>

      <p v-if="events.length === 0" class="muted">
        Nenhuma mudança registrada ainda. Elas aparecem quando uma sincronização detecta alteração de orçamento, meta,
        estratégia ou status de uma campanha, ou quando você marca uma recomendação como aplicada.
      </p>

      <article v-for="ev in events" :key="ev.key" class="event" :class="ev.evaluation.verdict">
        <header @click="openKey = openKey === ev.key ? null : ev.key">
          <div class="date">{{ formatDay(ev.date).slice(0, 5) }}<small>{{ ev.date.slice(0, 4) }}</small></div>
          <div class="head-text">
            <div class="chips">
              <span class="chip verdict" :class="ev.evaluation.verdict">{{ verdictLabel[ev.evaluation.verdict] }}</span>
              <span class="chip">{{ statusLabel[ev.evaluation.status] }}</span>
              <span v-if="ev.evaluation.verdict !== 'aguardando'" class="chip" :class="ev.evaluation.confidence">
                {{ confidenceLabel[ev.evaluation.confidence] }}
              </span>
              <span v-if="ev.recommendationId" class="chip">recomendação do agente</span>
              <span v-else-if="ev.kind === 'change'" class="chip">mudança feita por você</span>
            </div>
            <strong>{{ title(ev) }}</strong>
            <small v-if="ev.campaignName">{{ ev.campaignName }}</small>
          </div>
          <span class="toggle">{{ openKey === ev.key ? '▲' : '▼' }}</span>
        </header>

        <div v-if="openKey === ev.key" class="body">
          <p class="reason">{{ ev.evaluation.reason }}</p>

          <template v-if="ev.recommendationTitle">
            <h4>Recomendação que originou</h4>
            <p>
              <strong>{{ actionLabel[ev.recommendationAction ?? ''] ?? ev.recommendationAction }}</strong>:
              {{ ev.recommendationTitle }}
            </p>
            <p v-if="ev.successMetric || ev.successCriterion" class="muted">
              <template v-if="ev.successMetric">Métrica de sucesso sugerida: {{ ev.successMetric }}. </template>
              <template v-if="ev.successCriterion">Critério: {{ ev.successCriterion }}.</template>
            </p>
          </template>

          <template v-if="ev.evaluation.before && ev.evaluation.after">
            <h4>Antes x depois</h4>
            <p class="muted">
              Antes: {{ formatDay(ev.evaluation.before.from) }} a {{ formatDay(ev.evaluation.before.to) }}
              ({{ ev.evaluation.before.metrics.clicks.toLocaleString('pt-BR') }} cliques) · Depois:
              {{ formatDay(ev.evaluation.after.from) }} a {{ formatDay(ev.evaluation.after.to) }}
              ({{ ev.evaluation.after.metrics.clicks.toLocaleString('pt-BR') }} cliques). O dia da mudança fica de fora.
            </p>
            <table class="deltas">
              <thead>
                <tr><th>Métrica</th><th class="num">Antes</th><th class="num">Depois</th><th class="num">Variação</th></tr>
              </thead>
              <tbody>
                <tr v-for="d in ev.evaluation.deltas" :key="d.key">
                  <td>{{ d.label }}</td>
                  <td class="num">{{ fmt(d, d.before) }}</td>
                  <td class="num">{{ fmt(d, d.after) }}</td>
                  <td class="num" :class="tone(d)">{{ arrow[d.direction] }} {{ signed(d.changePct) }}</td>
                </tr>
              </tbody>
            </table>
            <p v-if="ev.evaluation.incrementalRoas !== null" class="muted">
              Retorno incremental: cada R$ 1 a mais investido trouxe cerca de {{ formatRoas(ev.evaluation.incrementalRoas) }} de receita a mais.
            </p>
          </template>

          <template v-if="ev.evaluation.notes.length">
            <h4>Ressalvas</h4>
            <ul><li v-for="n in ev.evaluation.notes" :key="n">{{ n }}</li></ul>
          </template>

          <template v-if="ev.noteChangeId !== null">
            <h4>Por que você mudou? (ajuda o agente a aprender)</h4>
            <div class="note-edit">
              <input
                :value="noteDrafts[ev.key] ?? ev.note ?? ''"
                maxlength="1000"
                placeholder="Ex.: queria testar mais volume antes da data comemorativa"
                @input="noteDrafts[ev.key] = ($event.target as HTMLInputElement).value"
              />
              <button :disabled="saving" @click="saveNote(ev)">Salvar</button>
            </div>
          </template>
          <p v-if="ev.evaluation.status !== 'indisponivel' && ev.evaluation.verdict === 'aguardando'" class="muted">
            Dias já com dados: {{ formatNumber(ev.evaluation.elapsedDays) }} de {{ ev.evaluation.windowDays }}.
          </p>
        </div>
      </article>
    </template>
  </section>
</template>

<style scoped>
.intro,
.muted,
small {
  color: var(--muted);
}
.intro {
  margin: 0 0 14px;
  max-width: 900px;
}
.banner.bad {
  background: var(--bad-soft);
  color: var(--bad);
  padding: 10px 14px;
  border-radius: 8px;
}
.box {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 12px 16px;
  margin-bottom: 14px;
}
.box h3 {
  margin: 0 0 6px;
  font-size: 15px;
}
.box summary {
  cursor: pointer;
  font-weight: 600;
}
.box ul {
  margin: 6px 0;
  padding-left: 18px;
}
.note-form {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.note-form .grow {
  flex: 1;
  min-width: 220px;
}
.notes {
  list-style: none;
  padding: 0;
}
.notes li {
  padding: 3px 0;
}
.link {
  border: none;
  background: none;
  color: var(--primary);
  cursor: pointer;
  padding: 0 4px;
}
.event {
  background: var(--card);
  border: 1px solid var(--border);
  border-left: 5px solid var(--muted);
  border-radius: 12px;
  margin-bottom: 10px;
}
.event.melhorou {
  border-left-color: var(--ok);
}
.event.piorou {
  border-left-color: var(--bad);
}
.event.misto {
  border-left-color: var(--warn);
}
.event header {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  cursor: pointer;
}
.date {
  flex: none;
  width: 52px;
  text-align: center;
  font-weight: 800;
  font-size: 17px;
  color: var(--primary);
}
.date small {
  display: block;
  font-weight: 400;
  font-size: 11px;
}
.head-text {
  flex: 1;
  display: grid;
  gap: 3px;
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
.chip.verdict {
  font-weight: 700;
}
.chip.melhorou,
.chip.alta {
  background: var(--ok-soft);
  color: var(--ok);
}
.chip.piorou,
.chip.baixa {
  background: var(--bad-soft);
  color: var(--bad);
}
.chip.misto,
.chip.media {
  background: var(--warn-soft);
  color: var(--warn);
}
.body {
  padding: 0 18px 16px 82px;
}
.body h4 {
  margin: 14px 0 4px;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
}
.body p {
  margin: 0 0 4px;
}
.reason {
  font-weight: 600;
}
.body ul {
  margin: 0;
  padding-left: 18px;
}
.deltas {
  border-collapse: collapse;
  min-width: 420px;
}
.deltas th,
.deltas td {
  padding: 4px 12px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}
.deltas .num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.deltas .good {
  color: var(--ok);
  font-weight: 600;
}
.deltas .bad {
  color: var(--bad);
  font-weight: 600;
}
.note-edit {
  display: flex;
  gap: 8px;
}
.note-edit input {
  flex: 1;
}
</style>
