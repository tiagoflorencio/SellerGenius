<script setup lang="ts">
import { computed, ref } from 'vue'
import AgentAvatar from './AgentAvatar.vue'
import {
  DESK_WIDTH,
  OFFICE_HEIGHT,
  OFFICE_WIDTH,
  departments,
  seatPosition,
  statusInfo,
  type AgentStatus,
  type Department,
  type OfficeAgent,
} from '../office'

const props = defineProps<{
  agents: OfficeAgent[]
}>()

const emit = defineEmits<{
  open: [agent: OfficeAgent]
  sync: []
}>()

const selectedId = ref<string | null>(null)
const selected = computed(() => props.agents.find((a) => a.id === selectedId.value) ?? null)

function agentAt(d: Department, seat: number) {
  return props.agents.find((a) => a.departmentId === d.id && a.seat === seat) ?? null
}

// Largura aproximada do balão conforme o texto (SVG não quebra linha sozinho).
const bubbleWidth = (text: string) => Math.max(80, text.length * 6.4 + 24)

// Cor da tela do monitor conforme o estado do agente.
const screenColor: Record<AgentStatus, string> = {
  idle: '#4c6ef5',
  working: '#69db7c',
  waiting: '#ffc078',
  offline: '#495057',
}

const legend = Object.values(statusInfo)
</script>

<template>
  <section>
    <div class="legend">
      <span v-for="s in legend" :key="s.label"><i :style="{ background: s.color }"></i>{{ s.label }}</span>
    </div>

    <svg
      class="office"
      :viewBox="`0 0 ${OFFICE_WIDTH} ${OFFICE_HEIGHT}`"
      role="img"
      aria-label="Planta do escritório com os departamentos e agentes"
    >
      <rect width="100%" height="100%" rx="18" fill="#dde2ee" />

      <g v-for="d in departments" :key="d.id">
        <!-- sala -->
        <rect :x="d.x" :y="d.y" :width="d.w" :height="d.h" rx="16" :fill="d.tint" stroke="#b8c0d6" stroke-width="2" />
        <!-- piso listrado sutil -->
        <rect :x="d.x + 8" :y="d.y + 70" :width="d.w - 16" :height="d.h - 78" rx="10" fill="#ffffff" opacity="0.45" />

        <text :x="d.x + 24" :y="d.y + 38" class="room-name">{{ d.name }}</text>
        <text :x="d.x + 24" :y="d.y + 58" class="room-sub">{{ d.subtitle }}</text>

        <!-- planta decorativa -->
        <g :transform="`translate(${d.x + d.w - 40}, ${d.y + 62})`">
          <rect x="-9" y="-16" width="18" height="16" rx="3" fill="#a9724a" />
          <circle cx="-7" cy="-26" r="9" fill="#51a35a" />
          <circle cx="7" cy="-26" r="9" fill="#3f8f4b" />
          <circle cx="0" cy="-34" r="9" fill="#5db769" />
        </g>

        <!-- mesas -->
        <g
          v-for="n in d.seats"
          :key="n"
          :transform="`translate(${seatPosition(d, n - 1).x}, ${seatPosition(d, n - 1).y})`"
        >
          <template v-if="agentAt(d, n - 1)">
            <g
              class="agent-spot"
              :class="{ selected: selectedId === agentAt(d, n - 1)!.id }"
              tabindex="0"
              role="button"
              :aria-label="`${agentAt(d, n - 1)!.name}: ${agentAt(d, n - 1)!.message}`"
              @click="selectedId = agentAt(d, n - 1)!.id"
              @keydown.enter="selectedId = agentAt(d, n - 1)!.id"
            >
              <!-- área clicável maior que o desenho -->
              <rect x="-6" y="-104" :width="DESK_WIDTH + 12" height="170" fill="transparent" />

              <!-- balão de fala -->
              <g :transform="`translate(${DESK_WIDTH / 2}, -76)`">
                <rect
                  :x="-bubbleWidth(agentAt(d, n - 1)!.message) / 2"
                  y="-28"
                  :width="bubbleWidth(agentAt(d, n - 1)!.message)"
                  height="28"
                  rx="9"
                  fill="#fff"
                  stroke="#b8c0d6"
                />
                <path d="M-6 0 L0 8 L6 0 Z" fill="#fff" stroke="#b8c0d6" />
                <rect x="-7" y="-1.5" width="14" height="4" fill="#fff" />
                <text x="0" y="-10" text-anchor="middle" class="bubble-text">{{ agentAt(d, n - 1)!.message }}</text>
              </g>

              <g :transform="`translate(${DESK_WIDTH / 2}, 4)`">
                <AgentAvatar :color="agentAt(d, n - 1)!.color" :status="agentAt(d, n - 1)!.status" />
              </g>
            </g>

            <!-- mesa (desenhada na frente do boneco) -->
            <rect x="0" y="0" :width="DESK_WIDTH" height="28" rx="6" fill="#c8a97e" />
            <rect x="0" y="0" :width="DESK_WIDTH" height="7" rx="3" fill="#dcc29c" />
            <!-- monitor -->
            <rect x="72" y="-26" width="34" height="24" rx="3" fill="#2b2f3a" />
            <rect x="75" y="-23" width="28" height="18" rx="2" :fill="screenColor[agentAt(d, n - 1)!.status]" :class="['screen', agentAt(d, n - 1)!.status]" />
            <rect x="86" y="-2" width="6" height="4" fill="#2b2f3a" />

            <text :x="DESK_WIDTH / 2" y="48" text-anchor="middle" class="agent-name">{{ agentAt(d, n - 1)!.name }}</text>
            <text :x="DESK_WIDTH / 2" y="62" text-anchor="middle" class="agent-role">{{ agentAt(d, n - 1)!.role }}</text>
            <circle :cx="DESK_WIDTH / 2 - (agentAt(d, n - 1)!.name.length * 3.4 + 10)" cy="44" r="4" :fill="statusInfo[agentAt(d, n - 1)!.status].color" />
          </template>

          <!-- mesa vaga -->
          <template v-else>
            <rect x="0" y="0" :width="DESK_WIDTH" height="28" rx="6" fill="none" stroke="#98a2bd" stroke-width="2" stroke-dasharray="6 5" />
            <text :x="DESK_WIDTH / 2" y="18" text-anchor="middle" class="vacant">Vaga</text>
          </template>
        </g>
      </g>
    </svg>

    <div v-if="selected" class="card">
      <div class="card-head">
        <div>
          <h3>{{ selected.name }}</h3>
          <p>{{ selected.role }}</p>
        </div>
        <span class="status" :style="{ background: statusInfo[selected.status].color }">
          {{ statusInfo[selected.status].label }}
        </span>
      </div>
      <p class="message">"{{ selected.message }}"</p>
      <dl>
        <div v-for="d in selected.details" :key="d.label">
          <dt>{{ d.label }}</dt>
          <dd>{{ d.value }}</dd>
        </div>
      </dl>
      <div class="card-actions">
        <button class="primary" @click="emit('open', selected)">{{ selected.openLabel }}</button>
        <button
          v-if="selected.syncLabel"
          :disabled="selected.status === 'working' || selected.status === 'offline'"
          @click="emit('sync')"
        >
          {{ selected.syncLabel }}
        </button>
      </div>
    </div>
    <p v-else class="hint">Clique em um agente para ver detalhes e dar comandos.</p>
  </section>
</template>

<style scoped>
.legend {
  display: flex;
  gap: 16px;
  margin-bottom: 10px;
  color: var(--muted);
  font-size: 13px;
}
.legend i {
  display: inline-block;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  margin-right: 6px;
}
.office {
  width: 100%;
  height: auto;
  display: block;
}
.room-name {
  font-size: 20px;
  font-weight: 700;
  fill: #2b3550;
}
.room-sub {
  font-size: 13px;
  fill: #6b7489;
}
.agent-name {
  font-size: 13px;
  font-weight: 700;
  fill: #1c2333;
}
.agent-role {
  font-size: 11px;
  fill: #6b7489;
}
.vacant {
  font-size: 12px;
  fill: #8a94ad;
}
.bubble-text {
  font-size: 11.5px;
  fill: #1c2333;
}
.agent-spot {
  cursor: pointer;
  outline: none;
}
.agent-spot:hover,
.agent-spot:focus-visible,
.agent-spot.selected {
  filter: drop-shadow(0 0 6px rgba(59, 91, 219, 0.55));
}
.screen.working {
  animation: flicker 1.1s steps(2) infinite;
}
.screen.waiting {
  animation: pulse 1.2s ease-in-out infinite;
}
@keyframes flicker {
  50% {
    opacity: 0.55;
  }
}
@keyframes pulse {
  50% {
    opacity: 0.4;
  }
}
@media (prefers-reduced-motion: reduce) {
  .screen {
    animation: none !important;
  }
}
.hint {
  color: var(--muted);
  margin: 12px 0 0;
}
.card {
  margin-top: 14px;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 16px 18px;
}
.card-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}
.card h3 {
  margin: 0;
}
.card p {
  margin: 2px 0 0;
  color: var(--muted);
}
.status {
  color: #fff;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
}
.message {
  margin: 10px 0 !important;
  font-style: italic;
  color: var(--text) !important;
}
dl {
  display: flex;
  flex-wrap: wrap;
  gap: 24px;
  margin: 0 0 14px;
}
dt {
  font-size: 12px;
  color: var(--muted);
  text-transform: uppercase;
}
dd {
  margin: 0;
  font-weight: 700;
  font-size: 18px;
}
.card-actions {
  display: flex;
  gap: 10px;
}
</style>
