<script setup lang="ts">
import type { PillType } from '@/utils/format'

withDefaults(defineProps<{ type?: PillType }>(), { type: 'gray' })
</script>

<template>
  <span class="pill" :class="type"><slot /></span>
</template>
