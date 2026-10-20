<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { WbAuthShell } from '@/components'
import { wbBrand } from '@/brand.config'
import { setLocale, type AppLocale } from '@/i18n'
import { useAuth } from '@/composables/useAuth'

const router = useRouter()
const { t, locale } = useI18n()
const { login } = useAuth()
const password = ref('')
const error = ref('')
const busy = ref(false)

const brand = computed(() => ({
  name: wbBrand.name,
  accent: wbBrand.accent,
  slogan: t('login.slogan'),
  sloganSub: t('login.sloganSub'),
  footer: t('login.footer')
}))

const ecosystem = computed(() => [
  { key: 'everything', label: t('login.productEverything') },
  { key: 'timebank', label: t('login.productTimeBank'), active: true },
  { key: 'more', label: t('login.moreProducts'), dim: true }
])

const shellLocale = computed(() => (locale.value === 'zh-CN' ? 'zh-CN' : 'en-US'))

function onLocale(value: string): void {
  setLocale((value === 'zh-CN' ? 'zh-CN' : 'en') as AppLocale)
}

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
  <WbAuthShell
    v-bind="brand"
    :ecosystem-title="t('login.ecosystem')"
    :ecosystem="ecosystem"
    :current-tag="t('login.current')"
    :locale="shellLocale"
    :lang-labels="{ zh: '中文', en: 'EN' }"
    @locale-change="onLocale"
  >
    <template #form>
      <el-card class="wb-card login-card" shadow="never">
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
    </template>
  </WbAuthShell>
</template>
