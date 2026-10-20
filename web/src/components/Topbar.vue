<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
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
  <header class="topbar">
    <div class="crumb">TimeBank<span class="sep">/</span><span class="cur">{{ title }}</span></div>
    <div class="spacer"></div>
    <div class="who"><span class="avatar">A</span>admin</div>
    <button class="signout" @click="signOut">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v8"/><path d="M6.6 6.6a8 8 0 1 0 10.8 0"/></svg>
      Sign out
    </button>
  </header>
</template>
