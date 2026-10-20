<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { House, User, Checked, Operation, Monitor, Clock } from '@element-plus/icons-vue'
import WbLogo from '@/components/WbLogo.vue'
import { useDashboard } from '@/composables/useDashboard'

const route = useRoute()
const { t } = useI18n()
const { overview } = useDashboard()
const active = computed(() => route.path)
const pending = computed(() => overview.value?.pendingRequests ?? 0)

const menus = [
  { path: '/overview', key: 'nav.overview', icon: House },
  { path: '/accounts', key: 'nav.accounts', icon: User },
  { path: '/requests', key: 'nav.requests', icon: Checked },
  { path: '/policies', key: 'nav.policies', icon: Operation },
  { path: '/machines', key: 'nav.machines', icon: Monitor },
  { path: '/records', key: 'nav.records', icon: Clock }
]
</script>

<template>
  <div class="side-brand">
    <WbLogo :size="38" />
    <div class="side-name">
      willBeyond
      <small>TIMEBANK</small>
    </div>
  </div>

  <el-menu :default-active="active" router>
    <el-menu-item v-for="m in menus" :key="m.path" :index="m.path">
      <el-icon><component :is="m.icon" /></el-icon>
      <span>{{ t(m.key) }}</span>
      <el-tag
        v-if="m.path === '/requests' && pending > 0"
        type="warning"
        size="small"
        round
        style="margin-left: auto"
      >{{ pending }}</el-tag>
    </el-menu-item>
  </el-menu>

  <div class="side-foot">
    <div class="eco">WillBeyond Ecosystem</div>
    <div>{{ t('app.footer') }}</div>
  </div>
</template>
