<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const { message } = useToast()
</script>

<template>
  <Transition name="fade">
    <div v-if="message" class="toast">{{ message }}</div>
  </Transition>
</template>

<style scoped>
.fade-enter-active, .fade-leave-active { transition: opacity .15s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }
</style>
