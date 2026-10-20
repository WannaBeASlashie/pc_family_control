<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { House, User, Checked, Operation, Monitor, Clock } from '@element-plus/icons-vue'
import WbLogo from '@/components/WbLogo.vue'
import { useDashboard } from '@/composables/useDashboard'

const route = useRoute()
const { overview } = useDashboard()
const active = computed(() => route.path)
const pending = computed(() => overview.value?.pendingRequests ?? 0)

const menus = [
  { path: '/overview', title: 'Overview', icon: House },
  { path: '/accounts', title: 'Accounts', icon: User },
  { path: '/requests', title: 'Requests', icon: Checked },
  { path: '/policies', title: 'Policies', icon: Operation },
  { path: '/machines', title: 'Machines', icon: Monitor },
  { path: '/records', title: 'Records', icon: Clock }
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
      <span>{{ m.title }}</span>
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
    <div>Time, well spent · LAN only</div>
  </div>
</template>
