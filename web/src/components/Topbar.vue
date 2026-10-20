<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { SwitchButton } from '@element-plus/icons-vue'
import { useAuth } from '@/composables/useAuth'

const route = useRoute()
const router = useRouter()
const { logout } = useAuth()
const title = computed(() => (route.meta.title as string) || '')

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
    <el-avatar :size="30" style="background: var(--wb-gradient); font-weight: 700">A</el-avatar>
    <span style="color: #475569; font-size: 14px">admin</span>
    <el-button :icon="SwitchButton" text type="danger" @click="signOut">Sign out</el-button>
  </div>
</template>

<style scoped>
.user-area {
  display: flex;
  align-items: center;
  gap: 10px;
}
</style>
