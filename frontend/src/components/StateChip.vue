<script setup lang="ts">
import type { DiagnosisStateValue } from '../types'

defineProps<{ state: DiagnosisStateValue }>()

const labels: Record<DiagnosisStateValue, string> = {
  saudavel: 'Saudável',
  atencao: 'Atenção',
  critico: 'Crítico',
  inconclusivo: 'Inconclusivo',
  sem_dados: 'Sem dados',
}

const titles: Record<DiagnosisStateValue, string> = {
  saudavel: 'Sem alertas com o volume de dados atual',
  atencao: 'Há desvios que merecem investigação',
  critico: 'Problema grave com amostra suficiente',
  inconclusivo: 'Poucos cliques/impressões para concluir',
  sem_dados: 'Sem impressões no período',
}
</script>

<template>
  <span class="chip" :class="state" :title="titles[state]">{{ labels[state] }}</span>
</template>

<style scoped>
.chip {
  display: inline-block;
  padding: 1px 9px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
}
.saudavel {
  background: var(--ok-soft);
  color: var(--ok);
}
.atencao {
  background: var(--warn-soft);
  color: var(--warn);
}
.critico {
  background: var(--bad-soft);
  color: var(--bad);
}
.inconclusivo,
.sem_dados {
  background: var(--info-soft);
  color: var(--muted);
}
</style>
