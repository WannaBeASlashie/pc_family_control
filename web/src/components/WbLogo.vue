<script setup lang="ts">
/**
 * WillBeyond 品牌标记：天空蓝渐变圆角方块 + 白色 ∞（"Beyond / 无限"意象）。
 * 与 willBeyondEverything 共用同一组件实现。
 */
withDefaults(defineProps<{ size?: number }>(), { size: 36 })

const uid = 'wbg' + Math.random().toString(36).slice(2, 8)
const fill = `url(#${uid})`
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 64 64"
    xmlns="http://www.w3.org/2000/svg"
    class="wb-mark"
    aria-label="WillBeyond"
  >
    <defs>
      <linearGradient :id="uid" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="#38BDF8" />
        <stop offset="0.55" stop-color="#0EA5E9" />
        <stop offset="1" stop-color="#0284C7" />
      </linearGradient>
    </defs>
    <rect width="64" height="64" rx="18" :fill="fill" />
    <text
      x="32"
      y="46"
      text-anchor="middle"
      font-family="Arial, 'Microsoft YaHei', sans-serif"
      font-size="40"
      font-weight="700"
      fill="#ffffff"
    >∞</text>
  </svg>
</template>
