<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Sidebar from '@/components/Sidebar.vue'
import Topbar from '@/components/Topbar.vue'
import { useDashboard } from '@/composables/useDashboard'
import { getToken } from '@/api/client'

const route = useRoute()
const router = useRouter()
const isLogin = computed(() => route.path === '/login')
const { refresh } = useDashboard()

let timer: number | undefined

function onUnauthorized(): void {
  if (route.path !== '/login') router.replace('/login')
}

onMounted(() => {
  if (getToken()) refresh()
  timer = window.setInterval(() => {
    if (getToken() && !isLogin.value) refresh()
  }, 5000)
  window.addEventListener('tb:unauthorized', onUnauthorized)
})

onUnmounted(() => {
  if (timer) window.clearInterval(timer)
  window.removeEventListener('tb:unauthorized', onUnauthorized)
})

watch(
  () => route.path,
  () => {
    if (getToken() && !isLogin.value) refresh()
  }
)
</script>

<template>
  <router-view v-if="isLogin" />
  <el-container v-else style="height: 100%">
    <el-aside width="228px" class="wb-sidebar">
      <Sidebar />
    </el-aside>
    <el-container>
      <el-header height="60px" class="wb-header">
        <Topbar />
      </el-header>
      <el-main class="wb-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>
