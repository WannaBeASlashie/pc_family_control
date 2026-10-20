<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { SwitchButton } from '@element-plus/icons-vue'
import LocaleSwitch from '@/components/LocaleSwitch.vue'
import { useAuth } from '@/composables/useAuth'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const { logout } = useAuth()
const title = computed(() => {
  const key = route.meta.titleKey as string | undefined
  return key ? t(key) : ''
})

function signOut(): void {
  logout()
  router.replace('/login')
}
</script>

<template>
  <div class="crumb">
    <span class="product">TimeBank</span>
    <span class="sep">/</span>
    <span class="page">{{ title }}</span>
  </div>

  <div class="user-area">
    <LocaleSwitch />
    <el-avatar :size="30" style="background: var(--wb-gradient); font-weight: 700">A</el-avatar>
    <span style="color: #475569; font-size: 14px">admin</span>
    <el-button :icon="SwitchButton" text type="danger" @click="signOut">{{ t('common.signOut') }}</el-button>
  </div>
</template>

<style scoped>
.user-area {
  display: flex;
  align-items: center;
  gap: 10px;
}
</style>
