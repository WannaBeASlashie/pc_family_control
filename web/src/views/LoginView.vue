<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import WbLogo from '@/components/WbLogo.vue'
import LocaleSwitch from '@/components/LocaleSwitch.vue'
import { useAuth } from '@/composables/useAuth'

const router = useRouter()
const { t } = useI18n()
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
    error.value = e instanceof Error && e.message === 'Wrong password' ? t('login.wrongPassword') : t('login.failed')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-screen">
    <el-card class="wb-card login-card" shadow="never">
      <div style="display: flex; justify-content: flex-end; margin-bottom: -8px">
        <LocaleSwitch />
      </div>
      <div class="login-brand">
        <WbLogo :size="46" />
        <div class="side-name" style="font-weight: 800; color: var(--wb-ink); line-height: 1.15">
          willBeyond
          <small style="display: block; font-size: 10.5px; letter-spacing: 0.22em; color: #8aa4bd">TIMEBANK</small>
        </div>
      </div>
      <div class="login-title">{{ t('login.title') }}</div>
      <div class="login-sub">{{ t('login.subtitle') }}</div>

      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item :label="t('common.password')">
          <el-input v-model="password" type="password" show-password autofocus @keyup.enter="submit" />
        </el-form-item>
        <el-button type="primary" style="width: 100%" :loading="busy" @click="submit">{{ t('common.signIn') }}</el-button>
      </el-form>
      <div class="login-err">{{ error }}</div>
    </el-card>
  </div>
</template>
