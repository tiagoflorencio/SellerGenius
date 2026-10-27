<script setup lang="ts">
import type { AdFinding, Severity } from '../types'
import { formatMoney } from '../format'

defineProps<{ findings: AdFinding[]; compact?: boolean }>()

const severityLabel: Record<Severity, string> = {
  critical: 'Crítico',
  attention: 'Atenção',
  opportunity: 'Oportunidade',
  info: 'Info',
}

// Ordem de exibição: mais grave primeiro.
const order: Record<Severity, number> = { critical: 0, attention: 1, opportunity: 2, info: 3 }
const sorted = (list: AdFinding[]) => [...list].sort((a, b) => order[a.severity] - order[b.severity])
</script>

<template>
  <ul class="findings" :class="{ compact }">
    <li v-for="f in sorted(findings)" :key="f.code + f.title" :class="f.severity">
      <div class="head">
        <span class="badge">{{ severityLabel[f.severity] }}</span>
        <strong>{{ f.title }}</strong>
        <span class="where">{{ f.where }}</span>
        <span v-if="f.impactBrl" class="impact">{{ formatMoney(f.impactBrl) }}</span>
      </div>
      <p v-if="!compact">{{ f.evidence }}</p>
    </li>
  </ul>
</template>

<style scoped>
.findings {
  list-style: none;
  padding: 0;
  margin: 0;
}
li {
  border-left: 4px solid var(--border);
  padding: 6px 10px;
  margin-bottom: 8px;
  background: var(--bg);
  border-radius: 0 8px 8px 0;
}
li.critical {
  border-left-color: var(--bad);
}
li.attention {
  border-left-color: var(--warn);
}
li.opportunity {
  border-left-color: var(--ok);
}
.head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.badge {
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.critical .badge {
  color: var(--bad);
}
.attention .badge {
  color: var(--warn);
}
.opportunity .badge {
  color: var(--ok);
}
.info .badge {
  color: var(--muted);
}
.where {
  font-size: 11px;
  background: var(--info-soft);
  color: var(--muted);
  border-radius: 999px;
  padding: 0 8px;
}
.impact {
  margin-left: auto;
  font-size: 12px;
  color: var(--bad);
  font-weight: 600;
}
p {
  margin: 4px 0 0;
  color: var(--text);
  font-size: 13px;
}
.compact li {
  margin-bottom: 4px;
  padding: 3px 8px;
}
</style>
