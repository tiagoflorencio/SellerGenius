<script setup lang="ts">
import { computed } from 'vue'

// Gráfico de linhas simples em SVG (sem biblioteca). Cada série é uma linha; "hline" desenha uma meta.
const props = defineProps<{
  labels: string[]
  series: { name: string; color: string; values: (number | null)[] }[]
  hline?: { value: number; label: string; color: string } | null
  format?: (value: number) => string
  height?: number
}>()

const W = 640
const H = computed(() => props.height ?? 200)
const PAD = { left: 52, right: 26, top: 12, bottom: 24 }

const fmt = (v: number) => (props.format ? props.format(v) : String(Math.round(v * 100) / 100))

// Arredonda o topo do eixo para um número "redondo" (1, 2, 2.5, 5 ou 10 vezes uma potência de 10).
function niceMax(value: number) {
  const exp = Math.pow(10, Math.floor(Math.log10(value)))
  const f = value / exp
  const nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10
  return nice * exp
}

const maxValue = computed(() => {
  const all = props.series.flatMap((s) => s.values).filter((v): v is number => v !== null)
  if (props.hline) all.push(props.hline.value)
  const max = Math.max(...all, 0)
  return max === 0 ? 1 : niceMax(max)
})

const innerW = W - PAD.left - PAD.right
const innerH = computed(() => H.value - PAD.top - PAD.bottom)

const x = (i: number) => PAD.left + (props.labels.length <= 1 ? innerW / 2 : (i / (props.labels.length - 1)) * innerW)
const y = (v: number) => PAD.top + innerH.value - (v / maxValue.value) * innerH.value

// Segmentos contínuos: valores nulos interrompem a linha.
function path(values: (number | null)[]) {
  let d = ''
  let open = false
  values.forEach((v, i) => {
    if (v === null) {
      open = false
      return
    }
    d += `${open ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)} `
    open = true
  })
  return d
}

const ticks = computed(() => [0, 0.25, 0.5, 0.75, 1].map((f) => f * maxValue.value))

// Mostra só alguns rótulos do eixo X para não sobrepor.
const labelIndexes = computed(() => {
  const n = props.labels.length
  if (n <= 6) return props.labels.map((_, i) => i)
  const step = Math.ceil(n / 6)
  return props.labels.map((_, i) => i).filter((i) => i % step === 0 || i === n - 1)
})
</script>

<template>
  <div>
    <div class="legend">
      <span v-for="s in series" :key="s.name"><i :style="{ background: s.color }"></i>{{ s.name }}</span>
      <span v-if="hline"><i class="dash" :style="{ borderColor: hline.color }"></i>{{ hline.label }}</span>
    </div>
    <svg :viewBox="`0 0 ${W} ${H}`" class="chart" role="img">
      <g v-for="t in ticks" :key="t">
        <line :x1="PAD.left" :x2="W - PAD.right" :y1="y(t)" :y2="y(t)" stroke="#e2e6ef" />
        <text :x="PAD.left - 6" :y="y(t) + 4" text-anchor="end" class="tick">{{ fmt(t) }}</text>
      </g>

      <line
        v-if="hline"
        :x1="PAD.left"
        :x2="W - PAD.right"
        :y1="y(hline.value)"
        :y2="y(hline.value)"
        :stroke="hline.color"
        stroke-dasharray="6 4"
        stroke-width="1.5"
      />

      <text v-for="i in labelIndexes" :key="i" :x="x(i)" :y="H - 6" text-anchor="middle" class="tick">{{ labels[i] }}</text>

      <g v-for="s in series" :key="s.name">
        <path :d="path(s.values)" fill="none" :stroke="s.color" stroke-width="2" stroke-linejoin="round" />
        <template v-for="(v, i) in s.values" :key="i">
          <circle v-if="v !== null" :cx="x(i)" :cy="y(v)" r="3" :fill="s.color" class="point">
            <title>{{ labels[i] }} · {{ s.name }}: {{ fmt(v) }}</title>
          </circle>
        </template>
      </g>
    </svg>
  </div>
</template>

<style scoped>
.chart {
  width: 100%;
  height: auto;
  display: block;
}
.tick {
  font-size: 11px;
  fill: #6b7489;
}
.point {
  opacity: 0;
}
.point:hover {
  opacity: 1;
}
.legend {
  display: flex;
  gap: 14px;
  font-size: 12px;
  color: var(--muted);
  margin-bottom: 4px;
}
.legend i {
  display: inline-block;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  margin-right: 5px;
}
.legend i.dash {
  border-radius: 0;
  height: 0;
  border-top: 2px dashed;
  vertical-align: middle;
}
</style>
