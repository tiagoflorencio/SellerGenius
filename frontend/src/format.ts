// Códigos de alerta da API -> texto exibido e cor.
export const flagInfo: Record<string, { label: string; tone: 'bad' | 'warn' | 'info' }> = {
  VISITAS_SEM_VENDAS: { label: 'Visitas sem vendas', tone: 'bad' },
  CONVERSAO_BAIXA: { label: 'Conversão baixa', tone: 'bad' },
  POUCAS_VISITAS: { label: 'Poucas visitas', tone: 'warn' },
  POUCAS_FOTOS: { label: 'Poucas fotos', tone: 'warn' },
  TITULO_CURTO: { label: 'Título curto', tone: 'warn' },
  PRECO_ACIMA_DOS_SIMILARES: { label: 'Preço acima dos similares', tone: 'warn' },
  SEM_FRETE_GRATIS: { label: 'Sem frete grátis', tone: 'warn' },
  PAUSADO: { label: 'Pausado', tone: 'info' },
}

export function flagLabel(code: string): string {
  return flagInfo[code]?.label ?? code
}

export function flagTone(code: string): 'bad' | 'warn' | 'info' {
  return flagInfo[code]?.tone ?? 'info'
}

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export const formatPrice = (value: number) => brl.format(value)

export const formatPercent = (value: number | null) =>
  value === null ? '–' : `${(value * 100).toFixed(1).replace('.', ',')}%`

// Para valores que já vêm em porcentagem (ex.: 17,15 = 17,15%).
export const formatPct = (value: number | null, digits = 1) =>
  value === null ? '–' : `${value.toFixed(digits).replace('.', ',')}%`

export const formatRoas = (value: number | null) => (value === null ? '–' : `${value.toFixed(2).replace('.', ',')}x`)

export const formatMoney = (value: number | null) => (value === null ? '–' : brl.format(value))

export const formatShortDate = (iso: string) => {
  const [, month, day] = iso.split('-')
  return `${day}/${month}`
}

export const strategyLabel: Record<string, string> = {
  PROFITABILITY: 'Rentabilidade',
  INCREASE: 'Crescimento',
  VISIBILITY: 'Visibilidade',
}

export const formatNumber = (value: number | null) => (value === null ? '–' : value.toLocaleString('pt-BR'))

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
