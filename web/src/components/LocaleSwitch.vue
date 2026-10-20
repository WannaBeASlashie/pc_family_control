<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ArrowDown } from '@element-plus/icons-vue'
import { setLocale, type AppLocale } from '@/i18n'

const { locale, t } = useI18n()

function change(value: unknown): void {
  setLocale(String(value) as AppLocale)
}
</script>

<template>
  <el-dropdown trigger="click" @command="change">
    <el-button text>
      {{ locale === 'zh-CN' ? t('locale.zh') : t('locale.en') }}
      <el-icon class="el-icon--right"><ArrowDown /></el-icon>
    </el-button>
    <template #dropdown>
      <el-dropdown-menu>
        <el-dropdown-item command="en">English</el-dropdown-item>
        <el-dropdown-item command="zh-CN">中文</el-dropdown-item>
      </el-dropdown-menu>
    </template>
  </el-dropdown>
</template>
