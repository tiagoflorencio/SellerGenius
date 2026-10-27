import { reactive } from 'vue'

// Estado de atividade compartilhado entre telas, para o escritório refletir o que os agentes estão fazendo.
export const activity = reactive({
  adsAnalyzing: false, // o agente de Ads está rodando uma análise
  adsPending: 0, // recomendações do agente de Ads aguardando decisão
})
