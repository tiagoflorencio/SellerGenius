import { reactive } from 'vue'

// Estado de atividade compartilhado entre telas (contador do menu e indicador de análise em andamento).
export const activity = reactive({
  adsAnalyzing: false, // o agente de Ads está rodando uma análise
  adsPending: 0, // recomendações do agente de Ads aguardando decisão
})
