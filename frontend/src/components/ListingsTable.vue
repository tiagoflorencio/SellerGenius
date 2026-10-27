<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Row } from '../types'
import { flagInfo, flagLabel, flagTone, formatMoney, formatNumber, formatPercent, formatPrice, formatRoas } from '../format'

const props = defineProps<{
  rows: Row[]
  suggestingId: string | null // anúncio com sugestão sendo gerada agora
}>()

const emit = defineEmits<{
  suggest: [itemId: string]
  open: [row: Row]
}>()

// Filtros e ordenação ficam só nesta tela (não precisam ir para a API).
const search = ref('')
const statusFilter = ref<'all' | 'active' | 'paused'>('active')
const flagFilter = ref('')
const onlyWithAlerts = ref(false)
const adsFilter = ref<'all' | 'with' | 'without'>('all')

type SortKey = 'title' | 'price' | 'visits30d' | 'sales30d' | 'conversion' | 'flags' | 'adsCost' | 'adsRoas'
const sortKey = ref<SortKey>('visits30d')
const sortDesc = ref(true)

function sortBy(key: SortKey) {
  if (sortKey.value === key) sortDesc.value = !sortDesc.value
  else {
    sortKey.value = key
    sortDesc.value = true
  }
}

function sortValue(row: Row, key: SortKey): number | string {
  if (key === 'flags') return row.flags.length
  if (key === 'adsCost') return row.ads?.metrics.cost ?? -1
  if (key === 'adsRoas') return row.ads?.metrics.roas ?? -1
  return row[key] ?? -1 // valores nulos vão para o fim
}

// computed: recalcula sozinho quando rows, filtros ou ordenação mudam.
const visibleRows = computed(() => {
  const term = search.value.trim().toLowerCase()

  return props.rows
    .filter((r) => statusFilter.value === 'all' || r.status === statusFilter.value)
    .filter((r) => !term || r.title.toLowerCase().includes(term) || r.itemId.toLowerCase().includes(term))
    .filter((r) => !flagFilter.value || r.flags.includes(flagFilter.value))
    .filter((r) => !onlyWithAlerts.value || r.flags.length > 0)
    .filter((r) => adsFilter.value === 'all' || (adsFilter.value === 'with') === (r.ads !== null))
    .sort((a, b) => {
      const va = sortValue(a, sortKey.value)
      const vb = sortValue(b, sortKey.value)
      const result = typeof va === 'string' ? va.localeCompare(vb as string) : (va as number) - (vb as number)
      return sortDesc.value ? -result : result
    })
})

const arrow = (key: SortKey) => (sortKey.value === key ? (sortDesc.value ? ' ▼' : ' ▲') : '')

const statusText: Record<string, string> = { Pending: 'Pendente', Approved: 'Aprovada', Rejected: 'Rejeitada' }
</script>

<template>
  <div class="filters">
    <input v-model="search" type="search" placeholder="Buscar por título ou MLB..." />
    <select v-model="statusFilter">
      <option value="active">Ativos</option>
      <option value="paused">Pausados</option>
      <option value="all">Todos</option>
    </select>
    <select v-model="flagFilter">
      <option value="">Todos os alertas</option>
      <option v-for="(info, code) in flagInfo" :key="code" :value="code">{{ info.label }}</option>
    </select>
    <label class="check"><input v-model="onlyWithAlerts" type="checkbox" /> Só com alertas</label>
    <select v-model="adsFilter">
      <option value="all">Com e sem Ads</option>
      <option value="with">Com anúncio pago</option>
      <option value="without">Sem anúncio pago</option>
    </select>
    <span class="count">{{ visibleRows.length }} anúncio(s)</span>
  </div>

  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th class="sortable" @click="sortBy('title')">Anúncio{{ arrow('title') }}</th>
          <th class="sortable num" @click="sortBy('price')">Preço{{ arrow('price') }}</th>
          <th class="sortable num" @click="sortBy('visits30d')">Visitas 30d{{ arrow('visits30d') }}</th>
          <th class="sortable num" @click="sortBy('sales30d')">Vendas 30d{{ arrow('sales30d') }}</th>
          <th class="sortable num" @click="sortBy('conversion')">Conversão{{ arrow('conversion') }}</th>
          <th class="sortable num" title="Investimento em Product Ads (período da última sincronização de Ads)" @click="sortBy('adsCost')">Ads: invest.{{ arrow('adsCost') }}</th>
          <th class="sortable num" title="Receita por real investido em Product Ads" @click="sortBy('adsRoas')">Ads: ROAS{{ arrow('adsRoas') }}</th>
          <th class="sortable" @click="sortBy('flags')">Alertas{{ arrow('flags') }}</th>
          <th>Sugestão (IA)</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in visibleRows" :key="row.itemId">
          <td class="title-cell">
            <a v-if="row.permalink" :href="row.permalink" target="_blank" rel="noopener noreferrer">{{ row.title }}</a>
            <span v-else>{{ row.title }}</span>
            <small>{{ row.itemId }} · {{ row.picturesCount }} fotos<template v-if="row.freeShipping"> · frete grátis</template></small>
          </td>
          <td class="num">{{ formatPrice(row.price) }}</td>
          <td class="num">{{ formatNumber(row.visits30d) }}</td>
          <td class="num">{{ formatNumber(row.sales30d) }}</td>
          <td class="num">{{ formatPercent(row.conversion) }}</td>
          <td class="num">
            <template v-if="row.ads">
              {{ formatMoney(row.ads.metrics.cost) }}
              <small v-if="row.ads.shared" class="shared" title="Métricas do grupo (família/catálogo), compartilhadas com outras publicações">grupo</small>
            </template>
            <span v-else class="muted">–</span>
          </td>
          <td class="num">{{ row.ads ? formatRoas(row.ads.metrics.roas) : '–' }}</td>
          <td>
            <span v-for="flag in row.flags" :key="flag" class="chip" :class="flagTone(flag)">{{ flagLabel(flag) }}</span>
            <span v-if="row.flags.length === 0" class="muted">–</span>
          </td>
          <td class="actions">
            <button v-if="row.suggestion" @click="emit('open', row)">
              Ver ({{ statusText[row.suggestion.status] }})
            </button>
            <button
              v-else
              :disabled="suggestingId !== null || row.status !== 'active'"
              title="Gera uma sugestão com a IA (consome créditos da OpenAI)"
              @click="emit('suggest', row.itemId)"
            >
              {{ suggestingId === row.itemId ? 'Gerando...' : 'Gerar' }}
            </button>
          </td>
        </tr>
        <tr v-if="visibleRows.length === 0">
          <td colspan="9" class="empty">Nenhum anúncio encontrado com esses filtros.</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  margin-bottom: 12px;
}
.filters input[type='search'] {
  min-width: 260px;
}
.check {
  display: flex;
  gap: 6px;
  align-items: center;
}
.count {
  margin-left: auto;
  color: var(--muted);
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
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
  white-space: nowrap;
}
th.sortable {
  cursor: pointer;
  user-select: none;
}
.num {
  text-align: right;
  white-space: nowrap;
}
.title-cell {
  max-width: 380px;
}
.title-cell a {
  color: var(--text);
  text-decoration: none;
  font-weight: 600;
}
.title-cell a:hover {
  color: var(--primary);
}
.title-cell small {
  display: block;
  color: var(--muted);
  margin-top: 2px;
}
.chip {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 12px;
  margin: 0 4px 4px 0;
  white-space: nowrap;
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
.muted,
.empty {
  color: var(--muted);
}
.shared {
  display: block;
  font-size: 11px;
  color: var(--muted);
}
.empty {
  text-align: center;
  padding: 30px;
}
.actions {
  white-space: nowrap;
}
</style>
