import { useI18n } from 'vue-i18n'

export function useReasonLabel() {
  const { t, te } = useI18n()
  return (reason: string | null | undefined): string => {
    if (!reason) return '-'
    const key = `reason.${reason}`
    return te(key) ? t(key) : reason
  }
}
