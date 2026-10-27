<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import type { AdsSettings } from '../types'

const props = defineProps<{ settings: AdsSettings }>()
const emit = defineEmits<{ close: []; saved: [] }>()

// Cópia editável: só vale depois de salvar.
const form = ref<AdsSettings>({ ...props.settings })
const saving = ref(false)
const error = ref<string | null>(null)

async function save() {
  saving.value = true
  error.value = null
  try {
    await api.saveAdsSettings(form.value)
    emit('saved')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Não foi possível salvar.'
  } finally {
    saving.value = false
  }
}

// Campos numéricos agrupados, com explicação do que cada um controla.
const groups = [
  {
    title: 'Meta de custo de Ads (enquanto não há custo dos produtos)',
    fields: [
      { key: 'costTargetMinPct', label: 'Faixa ideal: mínimo (%)', hint: 'Abaixo disso há espaço para investir mais.' },
      { key: 'costTargetMaxPct', label: 'Faixa ideal: teto (%)', hint: 'Acima disso o custo é sinalizado.' },
      { key: 'growthCostMaxPct', label: 'Teto para anúncios em crescimento (%)', hint: 'Produtos com poucas vendas toleram custo maior para ganhar tração.' },
      { key: 'criticalCostFactor', label: 'Fator crítico (x o teto)', hint: 'Acima de teto × fator o problema é crítico.' },
      { key: 'growthMaxSoldUnits', label: 'Vendas totais para sair do crescimento', hint: 'Abaixo disso o produto é considerado em fase de crescimento.' },
    ],
  },
  {
    title: 'Volume mínimo para julgar',
    fields: [
      { key: 'minImpressionsForCtr', label: 'Impressões mínimas (CTR)', hint: 'Com menos impressões o CTR não é avaliado.' },
      { key: 'minClicksForCvr', label: 'Cliques mínimos (custo e conversão)', hint: 'Com menos cliques o resultado é inconclusivo.' },
    ],
  },
  {
    title: 'Limites de atenção',
    fields: [
      { key: 'ctrLowPct', label: 'CTR baixo (%)', hint: 'Heurística inicial; não é referência oficial.' },
      { key: 'cvrLowFactor', label: 'Conversão baixa (x a mediana da conta)', hint: 'Compara com a conversão típica dos seus anúncios.' },
      { key: 'zeroSalesProbability', label: 'Chance máxima de "sem vendas" por acaso', hint: 'Cliques sem venda só são anormais se essa chance for menor que isso.' },
      { key: 'roasTargetTolerance', label: 'Tolerância da meta de ROAS (x)', hint: 'ROAS abaixo de meta × tolerância é sinalizado.' },
      { key: 'lostByBudgetHighPct', label: 'Perda por orçamento alta (%)', hint: '' },
      { key: 'lostByRankHighPct', label: 'Perda por ranking alta (%)', hint: '' },
      { key: 'budgetUnderusePct', label: 'Orçamento subutilizado abaixo de (%)', hint: '' },
      { key: 'trendChangePct', label: 'Variação semanal relevante (%)', hint: 'Últimos 7 dias contra os 7 anteriores.' },
    ],
  },
  {
    title: 'Agente de recomendações',
    fields: [
      { key: 'maxRecommendations', label: 'Máximo de recomendações por análise', hint: '' },
      { key: 'recentChangeDays', label: 'Dias de espera após mudar uma campanha', hint: 'Campanha alterada há menos que isso não recebe nova mudança de configuração: aguarda a avaliação.' },
      { key: 'weightImpact', label: 'Peso: impacto', hint: 'Pesos da prioridade (relativos entre si). Padrão do manual: 35 / 25 / 20 / 10 / 10.' },
      { key: 'weightConfidence', label: 'Peso: confiança', hint: '' },
      { key: 'weightUrgency', label: 'Peso: urgência', hint: '' },
      { key: 'weightEase', label: 'Peso: facilidade', hint: '' },
      { key: 'weightReversibility', label: 'Peso: reversibilidade', hint: '' },
      { key: 'evaluationWindowDays', label: 'Janela de avaliação antes/depois (dias)', hint: 'Dias comparados antes e depois de uma mudança, quando a recomendação não sugerir outra janela (7 a 30).' },
      { key: 'evaluationFlatBandPct', label: 'Faixa de estabilidade na avaliação (%)', hint: 'Variações até este valor são tratadas como "sem mudança".' },
    ],
  },
] as const
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <aside class="drawer">
      <header>
        <div>
          <h2>Parâmetros do diagnóstico</h2>
          <small>Valores iniciais baseados no manual e nas suas metas. Ajuste quando os dados mostrarem outra realidade.</small>
        </div>
        <button @click="emit('close')">Fechar</button>
      </header>

      <label class="field">
        <span>Base do percentual de custo</span>
        <select v-model="form.costBasis">
          <option value="tacos">TACOS: gasto / vendas totais do produto (com e sem Ads)</option>
          <option value="acos">ACOS: gasto / vendas feitas por Ads</option>
        </select>
      </label>

      <section v-for="g in groups" :key="g.title">
        <h3>{{ g.title }}</h3>
        <label v-for="f in g.fields" :key="f.key" class="field">
          <span>{{ f.label }}</span>
          <input v-model.number="form[f.key]" type="number" step="any" />
          <small v-if="f.hint">{{ f.hint }}</small>
        </label>
      </section>

      <p v-if="error" class="error">{{ error }}</p>
      <footer>
        <button class="primary" :disabled="saving" @click="save">{{ saving ? 'Salvando...' : 'Salvar' }}</button>
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
  z-index: 20;
}
.drawer {
  width: min(520px, 100%);
  height: 100%;
  background: var(--card);
  padding: 20px 24px;
  overflow-y: auto;
}
header {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}
h2 {
  margin: 0;
}
h3 {
  margin: 22px 0 8px;
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted);
}
small {
  color: var(--muted);
}
.field {
  display: grid;
  gap: 3px;
  margin: 10px 0;
}
.field span {
  font-weight: 600;
}
.error {
  color: var(--bad);
}
footer {
  margin-top: 20px;
}
</style>
