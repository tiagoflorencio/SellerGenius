// Configuração do escritório: departamentos (salas) e o tipo de agente exibido.
// Por enquanto fica no front; quando houver mais agentes, isso pode vir da API.

export interface Department {
  id: string
  name: string
  subtitle: string
  x: number
  y: number
  w: number
  h: number
  tint: string // cor de fundo da sala
  seats: number // quantidade de mesas
}

export type AgentStatus = 'idle' | 'working' | 'waiting' | 'offline'

export interface OfficeAgent {
  id: string
  name: string
  role: string
  departmentId: string
  seat: number // índice da mesa (0 = primeira)
  color: string // cor da roupa do avatar
  status: AgentStatus
  message: string // texto do balão de fala
  details: { label: string; value: string }[]
}

export const statusInfo: Record<AgentStatus, { label: string; color: string }> = {
  idle: { label: 'Disponível', color: '#2b8a3e' },
  working: { label: 'Trabalhando', color: '#3b5bdb' },
  waiting: { label: 'Aguardando você', color: '#e8890c' },
  offline: { label: 'Offline', color: '#868e96' },
}

export const OFFICE_WIDTH = 980
export const OFFICE_HEIGHT = 590
export const DESK_WIDTH = 110

// Grade 2x2 de salas.
export const departments: Department[] = [
  { id: 'marketplaces', name: 'Marketplaces', subtitle: 'Anúncios e vendas', x: 30, y: 30, w: 450, h: 250, tint: '#e7ecff', seats: 3 },
  { id: 'financeiro', name: 'Financeiro', subtitle: 'Em breve', x: 500, y: 30, w: 450, h: 250, tint: '#e3f6e8', seats: 2 },
  { id: 'conteudo', name: 'Conteúdo', subtitle: 'Em breve', x: 30, y: 310, w: 450, h: 250, tint: '#fff0d6', seats: 2 },
  { id: 'atendimento', name: 'Atendimento', subtitle: 'Em breve', x: 500, y: 310, w: 450, h: 250, tint: '#fde7f1', seats: 2 },
]

// Canto superior esquerdo da mesa de um assento.
export function seatPosition(d: Department, index: number) {
  return { x: d.x + 30 + index * (DESK_WIDTH + 30), y: d.y + 172 }
}
