import { reactive } from 'vue'

// Estado do acesso ao sistema. "required" = o servidor exige senha (produção); em desenvolvimento fica livre.
export const session = reactive({
  checked: false,
  required: false,
  authenticated: true,
})
