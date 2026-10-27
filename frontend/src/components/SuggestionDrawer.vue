<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import type { DraftReport, Row } from '../types'
import { parseSuggestion } from '../types'
import { flagLabel, flagTone, formatDateTime, formatMoney, formatPct, formatRoas } from '../format'

const props = defineProps<{
  row: Row
  deciding: boolean
}>()

const emit = defineEmits<{
  close: []
  decide: [id: number, decision: 'approve' | 'reject']
}>()

const suggestion = computed(() => props.row.suggestion!)
const content = computed(() => parseSuggestion(suggestion.value))

const statusText = { Pending: 'Pendente', Approved: 'Aprovada', Rejected: 'Rejeitada' }

// Prontidão: o que falta para criar uma variação deste anúncio (e a descrição atual).
const draft = ref<DraftReport | null>(null)
onMounted(async () => {
  try {
    draft.value = await api.draft(props.row.itemId)
  } catch {
    draft.value = null // sem dados completos ainda (falta sincronizar)
  }
})

const currentDescription = computed(() => draft.value?.draft.description?.plain_text ?? null)

// Copiar facilita colar no Mercado Livre (a publicação ainda é manual).
const copied = ref<string | null>(null)
async function copy(text: string) {
  await navigator.clipboard.writeText(text)
  copied.value = text
  setTimeout(() => (copied.value = null), 1500)
}
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <aside class="drawer">
      <header>
        <div>
          <h2>Sugestão da IA</h2>
          <small>{{ row.itemId }} · {{ formatDateTime(suggestion.createdAt) }} · {{ suggestion.model }}</small>
        </div>
        <button @click="emit('close')">Fechar</button>
      </header>

      <p class="current-title"><strong>Título atual:</strong> {{ row.title }}</p>
      <div>
        <span v-for="flag in row.flags" :key="flag" class="chip" :class="flagTone(flag)">{{ flagLabel(flag) }}</span>
      </div>

      <h3>Diagnóstico</h3>
      <p>{{ content.diagnostico }}</p>

      <h3>Títulos sugeridos</h3>
      <ul class="titles">
        <li v-for="t in content.titulos" :key="t">
          <span>{{ t }} <small>({{ t.length }}/60)</small></span>
          <button @click="copy(t)">{{ copied === t ? 'Copiado!' : 'Copiar' }}</button>
        </li>
        <li v-if="content.titulos.length === 0" class="muted">Nenhum título válido (até 60 caracteres).</li>
      </ul>

      <h3>Melhorias na descrição</h3>
      <ul>
        <li v-for="t in content.melhorias_descricao" :key="t">{{ t }}</li>
      </ul>

      <h3>Descrição sugerida</h3>
      <template v-if="content.descricao">
        <pre class="description">{{ content.descricao }}</pre>
        <div class="desc-actions">
          <button @click="copy(content.descricao)">{{ copied === content.descricao ? 'Copiado!' : 'Copiar descrição' }}</button>
          <small>{{ content.descricao.length }}<template v-if="draft?.maxDescriptionLength"> / {{ draft.maxDescriptionLength }}</template> caracteres</small>
        </div>
      </template>
      <p v-else class="muted">Esta sugestão foi gerada antes da coleta da descrição. Gere uma nova sugestão para receber um texto.</p>

      <details v-if="currentDescription" class="current-desc">
        <summary>Ver descrição atual do anúncio</summary>
        <pre class="description">{{ currentDescription }}</pre>
      </details>

      <h3>Publicidade (Product Ads)</h3>
      <template v-if="row.ads">
        <ul class="ads">
          <li v-for="g in row.ads.adGroups" :key="g.adGroupId">
            <strong>{{ g.campaignName }}</strong>
            <small> · {{ g.type === 'FAMILY' ? 'família' : g.type === 'CATALOG' ? 'catálogo' : 'item' }}</small>
            <div>
              Invest. {{ formatMoney(g.metrics.cost) }} · receita {{ formatMoney(g.metrics.revenue) }} · ROAS
              {{ formatRoas(g.metrics.roas) }} · ACOS {{ formatPct(g.metrics.acos) }} · conversão {{ formatPct(g.metrics.cvr) }}
            </div>
          </li>
        </ul>
        <p v-if="row.ads.shared" class="hint">
          Este anúncio divide o grupo de Ads com outras publicações (mesma família ou catálogo); os números são do grupo todo.
        </p>
      </template>
      <p v-else class="muted">Sem anúncio pago ligado a esta publicação (ou Ads ainda não sincronizado).</p>

      <h3>Dados para criar a variação</h3>
      <p v-if="!draft" class="muted">Dados completos ainda não coletados. Clique em "Sincronizar anúncios".</p>
      <template v-else>
        <p v-if="draft.ready" class="ready">Todos os campos obrigatórios estão disponíveis.</p>
        <template v-else>
          <p class="not-ready">Faltam campos obrigatórios:</p>
          <ul>
            <li v-for="m in draft.missing" :key="m">{{ m }}</li>
          </ul>
        </template>
        <ul v-if="draft.warnings.length" class="warnings">
          <li v-for="w in draft.warnings" :key="w">{{ w }}</li>
        </ul>
      </template>

      <h3>Outras ações</h3>
      <ul>
        <li v-for="t in content.outras_acoes" :key="t">{{ t }}</li>
      </ul>

      <footer>
        <div v-if="suggestion.status === 'Pending'" class="buttons">
          <button class="primary" :disabled="deciding" @click="emit('decide', suggestion.id, 'approve')">Aprovar</button>
          <button :disabled="deciding" @click="emit('decide', suggestion.id, 'reject')">Rejeitar</button>
        </div>
        <p v-else class="status">Sugestão {{ statusText[suggestion.status].toLowerCase() }}.</p>
        <small class="note">
          Aprovar só registra a sua decisão. Nada é publicado ou alterado no Mercado Livre por este painel.
        </small>
      </footer>
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
  width: min(560px, 100%);
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
  margin: 20px 0 6px;
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
}
small,
.muted {
  color: var(--muted);
}
.current-title {
  margin: 14px 0 8px;
}
.titles {
  list-style: none;
  padding: 0;
  margin: 0;
}
.titles li {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 8px;
  margin-bottom: 6px;
}
ul {
  padding-left: 18px;
}
.description {
  white-space: pre-wrap;
  font: inherit;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 10px 12px;
  margin: 0 0 8px;
  max-height: 260px;
  overflow-y: auto;
}
.desc-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.current-desc {
  margin-top: 10px;
}
.current-desc summary {
  cursor: pointer;
  color: var(--muted);
  margin-bottom: 8px;
}
.ready {
  color: var(--ok);
  font-weight: 600;
}
.not-ready {
  color: var(--bad);
  font-weight: 600;
  margin-bottom: 0;
}
.warnings {
  color: var(--warn);
}
.ads {
  padding-left: 18px;
}
.hint {
  font-size: 12px;
  color: var(--muted);
}
.chip {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 12px;
  margin: 0 4px 4px 0;
}
.chip.bad {
  background: var(--bad-soft);
  color: var(--bad);
}
.chip.warn {
  background: var(--warn-soft);
  color: var(--warn);
}
.chip.info {
  background: var(--info-soft);
  color: var(--muted);
}
footer {
  margin-top: 24px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
}
.buttons {
  display: flex;
  gap: 10px;
  margin-bottom: 10px;
}
.status {
  font-weight: 600;
}
.note {
  display: block;
}
</style>
