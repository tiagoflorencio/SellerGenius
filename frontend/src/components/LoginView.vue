<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'

const emit = defineEmits<{ done: [] }>()

const password = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

async function submit() {
  busy.value = true
  error.value = null
  try {
    await api.login(password.value)
    password.value = ''
    emit('done')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Não foi possível entrar.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="login">
    <form class="card" @submit.prevent="submit">
      <div class="brand">
        <span class="brand-mark">A</span>
        <div>
          <strong>AgentOffice</strong>
          <small>Gestão com IA</small>
        </div>
      </div>

      <h1>Entrar</h1>
      <p class="muted">Digite a senha de acesso para continuar.</p>

      <label>
        Senha
        <input v-model="password" type="password" autocomplete="current-password" autofocus required />
      </label>

      <p v-if="error" class="error">{{ error }}</p>

      <button class="primary" type="submit" :disabled="busy || !password">{{ busy ? 'Entrando...' : 'Entrar' }}</button>
    </form>
  </main>
</template>

<style scoped>
.login {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: linear-gradient(160deg, #0f172a, #1e1b4b);
}
.card {
  width: 100%;
  max-width: 380px;
  display: grid;
  gap: 14px;
  background: var(--card);
  border-radius: 16px;
  padding: 28px;
  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
}
.brand {
  display: flex;
  align-items: center;
  gap: 12px;
}
.brand-mark {
  width: 40px;
  height: 40px;
  border-radius: 11px;
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 18px;
  color: #fff;
  background: linear-gradient(135deg, #6366f1, #4338ca);
}
.brand strong {
  display: block;
}
.brand small,
.muted {
  color: var(--muted);
}
h1 {
  margin: 8px 0 0;
  font-size: 22px;
}
.muted {
  margin: 0;
}
label {
  display: grid;
  gap: 5px;
  font-weight: 600;
  font-size: 13px;
}
input {
  width: 100%;
  font-weight: 400;
}
.error {
  margin: 0;
  padding: 9px 12px;
  border-radius: 10px;
  background: var(--bad-soft);
  color: var(--bad);
}
</style>
