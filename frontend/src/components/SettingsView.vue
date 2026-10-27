<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import type { IntegrationsStatus } from '../types'
import Icon from './Icon.vue'

const props = defineProps<{
  status: IntegrationsStatus | null
  flash: { kind: 'ok' | 'bad'; text: string } | null
}>()
const emit = defineEmits<{ changed: [] }>()

const appId = ref('')
const clientSecret = ref('')
const redirectUri = ref('')
const busy = ref<'save' | 'connect' | 'disconnect' | 'remove' | null>(null)
const message = ref<{ kind: 'ok' | 'bad'; text: string } | null>(null)
const dirty = ref(false)

const ml = computed(() => props.status?.mercadoLivre ?? null)

// Preenche o formulário com o que já está salvo (sem sobrescrever o que o usuário está digitando).
watch(
  ml,
  (value) => {
    if (!value || dirty.value) return
    appId.value = value.appId
    redirectUri.value = value.redirectUri
  },
  { immediate: true },
)

const stateLabel = computed(() => {
  if (!ml.value) return { text: 'Verificando...', tone: 'muted' }
  if (ml.value.connected) return { text: 'Conectado', tone: 'ok' }
  if (ml.value.configured) return { text: 'Pronto para conectar', tone: 'warn' }
  return { text: 'Não configurado', tone: 'muted' }
})

async function act(kind: NonNullable<typeof busy.value>, action: () => Promise<void>, success?: string) {
  busy.value = kind
  message.value = null
  try {
    await action()
    if (success) message.value = { kind: 'ok', text: success }
    emit('changed')
  } catch (e) {
    message.value = { kind: 'bad', text: e instanceof Error ? e.message : 'Algo deu errado.' }
  } finally {
    busy.value = null
  }
}

const save = () =>
  act(
    'save',
    async () => {
      await api.saveMlCredentials(appId.value.trim(), clientSecret.value.trim(), redirectUri.value.trim())
      clientSecret.value = ''
      dirty.value = false
    },
    'Credenciais salvas. Agora clique em "Conectar conta".',
  )

// Leva o navegador à tela de autorização do Mercado Livre; ao terminar, ele volta para cá.
const connect = () =>
  act('connect', async () => {
    const { url } = await api.mlConnect()
    window.location.href = url
  })

const disconnect = () => act('disconnect', () => api.mlDisconnect(), 'Conta desconectada.')

const removeCredentials = () => {
  if (!window.confirm('Remover as credenciais salvas e desconectar a conta?')) return Promise.resolve()
  return act(
    'remove',
    async () => {
      await api.deleteMlCredentials()
      appId.value = ''
      redirectUri.value = ''
      clientSecret.value = ''
      dirty.value = false
    },
    'Credenciais removidas.',
  )
}

const canSave = computed(() => appId.value.trim() && redirectUri.value.trim() && (clientSecret.value.trim() || ml.value?.hasSecret))
</script>

<template>
  <div class="settings">
    <p v-if="flash" class="notice" :class="flash.kind">{{ flash.text }}</p>

    <section class="panel">
      <header class="panel-head">
        <div class="logo ml">ML</div>
        <div class="grow">
          <h2>Mercado Livre</h2>
          <p class="muted">Conecte a conta do vendedor para sincronizar anúncios e campanhas de Product Ads.</p>
        </div>
        <span class="chip" :class="stateLabel.tone">{{ stateLabel.text }}</span>
      </header>

      <div v-if="ml?.connected" class="connected">
        <Icon name="check" />
        <div>
          <strong v-if="ml.account">Conta {{ ml.account.nickname }}</strong>
          <strong v-else>Conta conectada</strong>
          <small v-if="ml.account"> · ID {{ ml.account.id }} · site {{ ml.account.siteId }}</small>
          <p v-if="ml.accountError" class="muted">{{ ml.accountError }}</p>
        </div>
        <button :disabled="busy !== null" @click="disconnect">{{ busy === 'disconnect' ? 'Desconectando...' : 'Desconectar' }}</button>
      </div>

      <ol class="steps">
        <li>
          <strong>Crie um aplicativo no Mercado Livre.</strong>
          Acesse o painel de desenvolvedores (<a href="https://developers.mercadolivre.com.br/" target="_blank" rel="noopener noreferrer">developers.mercadolivre.com.br</a>),
          crie um aplicativo e cadastre nele a mesma <em>URL de redirecionamento</em> informada abaixo.
        </li>
        <li><strong>Copie o App ID e a Secret Key</strong> do aplicativo e cole nos campos abaixo.</li>
        <li><strong>Clique em "Conectar conta"</strong> e autorize o acesso na tela do Mercado Livre.</li>
      </ol>

      <form class="form" @submit.prevent="save">
        <label>
          App ID
          <input v-model="appId" inputmode="numeric" autocomplete="off" placeholder="Ex.: 1234567890123456" @input="dirty = true" />
        </label>
        <label>
          Secret Key
          <input
            v-model="clientSecret"
            type="password"
            autocomplete="new-password"
            :placeholder="ml?.hasSecret ? '•••••••••••• (salva; deixe em branco para manter)' : 'Cole a chave secreta do aplicativo'"
            @input="dirty = true"
          />
        </label>
        <label class="wide">
          URL de redirecionamento (HTTPS)
          <input v-model="redirectUri" autocomplete="off" placeholder="https://seu-endereco-publico/auth/callback" @input="dirty = true" />
          <small>
            O Mercado Livre não aceita <code>http://localhost</code>. Use um endereço HTTPS público que encaminhe para a API
            desta aplicação (domínio próprio ou um túnel, como o devtunnel) terminando em <code>/auth/callback</code>.
          </small>
        </label>

        <p v-if="ml?.source === 'config'" class="muted wide">
          No momento as credenciais vêm da configuração do servidor (user-secrets ou variáveis de ambiente). Ao salvar aqui, elas passam a ser usadas no lugar.
        </p>

        <div class="actions wide">
          <button class="primary" type="submit" :disabled="!canSave || busy !== null">{{ busy === 'save' ? 'Salvando...' : 'Salvar credenciais' }}</button>
          <button type="button" class="primary solid-ok" :disabled="!ml?.configured || busy !== null || dirty" @click="connect">
            <Icon name="link" />
            {{ busy === 'connect' ? 'Abrindo...' : ml?.connected ? 'Reconectar conta' : 'Conectar conta' }}
          </button>
          <button
            v-if="ml?.source === 'ui'"
            type="button"
            class="danger"
            :disabled="busy !== null"
            @click="removeCredentials"
          >
            Remover credenciais
          </button>
        </div>
        <p v-if="dirty && ml?.configured" class="muted wide">Salve as alterações antes de conectar.</p>
        <p v-if="message" class="notice wide" :class="message.kind">{{ message.text }}</p>
      </form>

      <p class="safe">
        <Icon name="check" />
        Acesso somente leitura: o sistema consulta seus dados, mas nunca altera anúncios ou campanhas no Mercado Livre.
        As credenciais ficam criptografadas neste computador e a chave secreta nunca é exibida novamente.
      </p>
    </section>
  </div>
</template>

<style scoped>
.settings {
  display: grid;
  gap: 18px;
  max-width: 880px;
}
.panel {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: var(--shadow);
  padding: 22px 24px;
}
.panel-head {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 14px;
}
.panel-head h2 {
  margin: 0;
  font-size: 17px;
}
.panel-head p {
  margin: 2px 0 0;
}
.grow {
  flex: 1;
}
.logo {
  flex: none;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 14px;
}
.logo.ml {
  background: #ffe600;
  color: #2d3277;
}
.muted,
small {
  color: var(--muted);
}
.chip {
  font-size: 12px;
  font-weight: 600;
  padding: 4px 12px;
  border-radius: 999px;
  background: var(--info-soft);
  color: var(--muted);
}
.chip.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.chip.warn {
  background: var(--warn-soft);
  color: var(--warn);
}
.connected {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--ok-soft);
  color: var(--ok);
  border-radius: 10px;
  padding: 12px 14px;
  margin-bottom: 14px;
}
.connected > div {
  flex: 1;
  color: var(--text);
}
.connected p {
  margin: 2px 0 0;
}
.steps {
  margin: 0 0 18px;
  padding-left: 20px;
  display: grid;
  gap: 6px;
  color: var(--muted);
  line-height: 1.5;
}
.steps strong {
  color: var(--text);
}
.form {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px 16px;
}
.form label {
  display: grid;
  gap: 5px;
  font-weight: 600;
  font-size: 13px;
}
.form label small {
  font-weight: 400;
  line-height: 1.45;
}
.form input {
  font-weight: 400;
  width: 100%;
}
.wide {
  grid-column: 1 / -1;
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
.actions button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.actions .solid-ok {
  background: var(--ok);
  border-color: var(--ok);
}
.danger {
  margin-left: auto;
  color: var(--bad);
}
.notice {
  margin: 0;
  padding: 10px 14px;
  border-radius: 10px;
  font-weight: 500;
}
.notice.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.notice.bad {
  background: var(--bad-soft);
  color: var(--bad);
}
.safe {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  margin: 18px 0 0;
  padding-top: 14px;
  border-top: 1px solid var(--border);
  color: var(--muted);
  font-size: 13px;
  line-height: 1.5;
}
code {
  font-size: 12.5px;
}
@media (max-width: 720px) {
  .form {
    grid-template-columns: 1fr;
  }
}
</style>
