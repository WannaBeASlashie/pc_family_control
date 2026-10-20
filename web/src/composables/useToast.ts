import { ref } from 'vue'

const message = ref('')
let timer: number | undefined

export function showToast(text: string): void {
  message.value = text
  if (timer) window.clearTimeout(timer)
  timer = window.setTimeout(() => {
    message.value = ''
  }, 2500)
}

export function useToast() {
  return { message }
}
