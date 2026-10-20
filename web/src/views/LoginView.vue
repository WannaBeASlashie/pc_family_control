<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuth } from '@/composables/useAuth'

const router = useRouter()
const { login } = useAuth()
const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit(): Promise<void> {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await login(password.value)
    router.replace('/overview')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Sign in failed'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-screen">
    <div class="login-box">
      <div class="brand">
        <div class="mark">∞</div>
        <div class="wordmark"><b>willBeyond</b><span>TIMEBANK</span></div>
      </div>
      <h2>Admin Console</h2>
      <div class="sub">Sign in to manage family time</div>
      <form @submit.prevent="submit">
        <label>Password</label>
        <input v-model="password" type="password" autofocus />
        <div class="form-actions">
          <button class="btn primary" style="width:100%" :disabled="busy">Sign in</button>
        </div>
      </form>
      <p class="login-err">{{ error }}</p>
    </div>
  </div>
</template>
