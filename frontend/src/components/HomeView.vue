<script setup lang="ts">
import { computed } from 'vue'
import type { IntegrationsStatus, Page } from '../types'
import Icon from './Icon.vue'

const props = defineProps<{
  status: IntegrationsStatus | null
  listingsCount: number
  activeListings: number
  withAlerts: number
  suggestionsPending: number
  adsPending: number
  adsSynced: boolean
  adsAnalyzed: boolean
}>()
const emit = defineEmits<{ go: [page: Page] }>()

const steps = computed(() => {
  const ml = props.status?.mercadoLivre
  return [
    { title: 'Informar as credenciais do aplicativo', hint: 'App ID, Secret Key e URL de redirecionamento.', done: !!ml?.configured, page: 'settings' as Page },
    { title: 'Conectar a conta do Mercado Livre', hint: 'Autorize o acesso somente leitura.', done: !!ml?.connected, page: 'settings' as Page },
    { title: 'Sincronizar os anúncios', hint: 'Traz seus anúncios e métricas para análise.', done: props.listingsCount > 0, page: 'listings' as Page },
    { title: 'Sincronizar as campanhas de Ads', hint: 'Traz campanhas e métricas de Product Ads.', done: props.adsSynced, page: 'ads' as Page },
    { title: 'Pedir a primeira análise com IA', hint: 'Receba recomendações priorizadas para decidir.', done: props.adsAnalyzed, page: 'ads' as Page },
  ]
})

const doneCount = computed(() => steps.value.filter((s) => s.done).length)
const nextStep = computed(() => steps.value.find((s) => !s.done) ?? null)
</script>

<template>
  <div class="home">
    <section class="kpis">
      <button class="kpi" @click="emit('go', 'listings')">
        <span>Anúncios ativos</span>
        <strong>{{ activeListings }}</strong>
        <small>{{ listingsCount }} no total</small>
      </button>
      <button class="kpi" :class="{ alert: withAlerts > 0 }" @click="emit('go', 'listings')">
        <span>Anúncios com alertas</span>
        <strong>{{ withAlerts }}</strong>
        <small>precisam de atenção</small>
      </button>
      <button class="kpi" :class="{ alert: suggestionsPending > 0 }" @click="emit('go', 'listings')">
        <span>Sugestões de anúncios</span>
        <strong>{{ suggestionsPending }}</strong>
        <small>aguardando sua decisão</small>
      </button>
      <button class="kpi" :class="{ alert: adsPending > 0 }" @click="emit('go', 'ads')">
        <span>Recomendações de Ads</span>
        <strong>{{ adsPending }}</strong>
        <small>aguardando sua decisão</small>
      </button>
    </section>

    <section class="panel">
      <header>
        <h2>Primeiros passos</h2>
        <span class="progress">{{ doneCount }} de {{ steps.length }} concluídos</span>
      </header>
      <div class="bar"><i :style="{ width: (doneCount / steps.length) * 100 + '%' }"></i></div>

      <ul>
        <li v-for="(s, i) in steps" :key="s.title" :class="{ done: s.done, next: s === nextStep }">
          <span class="mark"><Icon v-if="s.done" name="check" /><template v-else>{{ i + 1 }}</template></span>
          <div>
            <strong>{{ s.title }}</strong>
            <small>{{ s.hint }}</small>
          </div>
          <button v-if="!s.done && s === nextStep" class="primary" @click="emit('go', s.page)">Continuar</button>
          <button v-else-if="!s.done" @click="emit('go', s.page)">Abrir</button>
        </li>
      </ul>
    </section>

    <p class="note">
      Nada é alterado no Mercado Livre pelo sistema: toda recomendação é aplicada por você, manualmente, depois de aprovar.
    </p>
  </div>
</template>

<style scoped>
.home {
  display: grid;
  gap: 20px;
  max-width: 980px;
}
.kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 14px;
}
.kpi {
  text-align: left;
  display: grid;
  gap: 2px;
  padding: 16px 18px;
  border-radius: 14px;
  box-shadow: var(--shadow);
}
.kpi span {
  color: var(--muted);
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.kpi strong {
  font-size: 30px;
  line-height: 1.2;
}
.kpi small {
  color: var(--muted);
}
.kpi.alert strong {
  color: var(--warn);
}
.panel {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: var(--shadow);
  padding: 20px 24px;
}
.panel header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.panel h2 {
  margin: 0;
  font-size: 17px;
}
.progress {
  color: var(--muted);
  font-size: 13px;
}
.bar {
  height: 6px;
  background: var(--info-soft);
  border-radius: 999px;
  margin: 12px 0 8px;
  overflow: hidden;
}
.bar i {
  display: block;
  height: 100%;
  background: var(--primary);
  border-radius: 999px;
  transition: width 0.3s;
}
ul {
  list-style: none;
  margin: 0;
  padding: 0;
}
li {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 0;
  border-bottom: 1px solid var(--border);
}
li:last-child {
  border-bottom: none;
}
li > div {
  flex: 1;
  display: grid;
}
li small {
  color: var(--muted);
}
.mark {
  flex: none;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-weight: 700;
  background: var(--info-soft);
  color: var(--muted);
}
li.next .mark {
  background: var(--primary-soft);
  color: var(--primary);
}
li.done .mark {
  background: var(--ok-soft);
  color: var(--ok);
}
li.done strong {
  color: var(--muted);
  text-decoration: line-through;
}
.note {
  margin: 0;
  color: var(--muted);
  font-size: 13px;
}
</style>
