<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch, watchEffect } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'
import Sidebar from '@/components/Sidebar.vue'
import Topbar from '@/components/Topbar.vue'
import { useDashboard } from '@/composables/useDashboard'
import { getToken } from '@/api/client'

const route = useRoute()
const router = useRouter()
const { locale, t } = useI18n()
const isLogin = computed(() => route.path === '/login')
const { refresh } = useDashboard()

const elLocale = computed(() => (locale.value === 'zh-CN' ? zhCn : en))
watchEffect(() => {
  document.title = t('app.docTitle')
})

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
  <el-config-provider :locale="elLocale">
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
          <router-view :key="locale" />
        </el-main>
      </el-container>
    </el-container>
  </el-config-provider>
</template>
