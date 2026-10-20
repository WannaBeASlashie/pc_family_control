<script setup lang="ts">
withDefaults(defineProps<{
  label: string
  value: string | number
  danger?: boolean
  clickable?: boolean
}>(), { danger: false, clickable: false })
</script>

<template>
  <div class="stat" :class="{ click: clickable }">
    <div class="lbl">{{ label }}</div>
    <div class="num" :class="{ red: danger }">{{ value }}</div>
  </div>
</template>
