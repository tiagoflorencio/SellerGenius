<script setup lang="ts">
import type { AgentStatus } from '../office'

// Desenha o boneco (visto de frente, da cintura para cima). A origem (0,0) fica na linha da mesa.
// O estado muda a animação: parado respira, trabalhando digita, aguardando acena, offline fica cinza.
defineProps<{
  color: string
  status: AgentStatus
  skin?: string
  hair?: string
}>()
</script>

<template>
  <g class="avatar" :class="status">
    <g class="figure">
      <!-- braços: ficam atrás do corpo -->
      <g transform="translate(-17,-30)">
        <rect class="arm arm-left" x="-4" y="0" width="8" height="22" rx="4" :fill="status === 'offline' ? '#adb5bd' : color" />
      </g>
      <g transform="translate(17,-30)">
        <rect class="arm arm-right" x="-4" y="0" width="8" height="22" rx="4" :fill="status === 'offline' ? '#adb5bd' : color" />
      </g>

      <!-- corpo -->
      <rect x="-16" y="-36" width="32" height="40" rx="11" :fill="status === 'offline' ? '#ced4da' : color" />

      <!-- cabeça -->
      <circle cx="0" cy="-52" r="15" :fill="skin ?? '#f3c9a5'" />
      <path d="M-15 -54 A15 15 0 0 1 15 -54 Q0 -63 -15 -54 Z" :fill="hair ?? '#3d2b1f'" />

      <!-- rosto: offline dorme (olhos fechados), os demais ficam com olhos abertos -->
      <template v-if="status === 'offline'">
        <path d="M-9 -50 h6 M3 -50 h6" stroke="#495057" stroke-width="2" stroke-linecap="round" />
      </template>
      <template v-else>
        <circle class="eye" cx="-5" cy="-50" r="2" fill="#212529" />
        <circle class="eye" cx="5" cy="-50" r="2" fill="#212529" />
        <path v-if="status === 'waiting'" d="M-5 -43 Q0 -40 5 -43" stroke="#212529" stroke-width="1.6" fill="none" stroke-linecap="round" />
        <path v-else d="M-4 -43 Q0 -41 4 -43" stroke="#212529" stroke-width="1.6" fill="none" stroke-linecap="round" />
      </template>
    </g>
  </g>
</template>

<style scoped>
/* Em SVG, transform-box faz a rotação usar o próprio elemento como referência. */
.arm {
  transform-box: fill-box;
  transform-origin: 50% 0%;
}

.idle .figure {
  animation: breathe 3.2s ease-in-out infinite;
}

.working .arm-left {
  animation: type 0.28s ease-in-out infinite alternate;
}
.working .arm-right {
  animation: type 0.28s ease-in-out 0.14s infinite alternate;
}

.waiting .arm-right {
  animation: wave 0.7s ease-in-out infinite alternate;
}
.waiting .figure {
  animation: breathe 2s ease-in-out infinite;
}

.eye {
  transform-box: fill-box;
  transform-origin: center;
  animation: blink 5s infinite;
}

@keyframes breathe {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-2px);
  }
}
@keyframes type {
  from {
    transform: rotate(8deg) translateY(0);
  }
  to {
    transform: rotate(-4deg) translateY(-4px);
  }
}
@keyframes wave {
  from {
    transform: rotate(150deg);
  }
  to {
    transform: rotate(190deg);
  }
}
@keyframes blink {
  0%,
  94%,
  100% {
    transform: scaleY(1);
  }
  97% {
    transform: scaleY(0.1);
  }
}

@media (prefers-reduced-motion: reduce) {
  .figure,
  .arm,
  .eye {
    animation: none !important;
  }
}
</style>
