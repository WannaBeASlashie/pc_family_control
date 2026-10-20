<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import WbLogo from '@/components/WbLogo.vue'
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
    <el-card class="wb-card login-card" shadow="never">
      <div class="login-brand">
        <WbLogo :size="46" />
        <div class="side-name" style="font-weight: 800; color: var(--wb-ink); line-height: 1.15">
          willBeyond
          <small style="display: block; font-size: 10.5px; letter-spacing: 0.22em; color: #8aa4bd">TIMEBANK</small>
        </div>
      </div>
      <div class="login-title">Admin Console</div>
      <div class="login-sub">Sign in to manage family time</div>

      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item label="Password">
          <el-input v-model="password" type="password" show-password autofocus @keyup.enter="submit" />
        </el-form-item>
        <el-button type="primary" style="width: 100%" :loading="busy" @click="submit">Sign in</el-button>
      </el-form>
      <div class="login-err">{{ error }}</div>
    </el-card>
  </div>
</template>
