<script setup lang="ts">
/**
 * WillBeyond 生态通用登录页外壳。
 *
 * 只负责品牌区、生态产品列表、语言切换与布局；登录表单由使用方通过 #form 插槽提供。
 * 无 vue-i18n 依赖：文案全部由 props 传入，语言切换通过 locale-change 事件交给使用方处理。
 *
 * 来源：willBeyondEverything/web/src/components/WbAuthShell.vue
 * 用法见 docs/06-生态组件复用指引.md
 */
import WbLogo from './WbLogo.vue'
import type { WbEcosystemItem } from '@/brand.config'

withDefaults(
  defineProps<{
    /** 品牌主名，如 willBeyond */
    name: string
    /** 品牌强调名（加粗），如 TimeBank */
    accent: string
    /** 主标语 */
    slogan: string
    /** 副标语（可选） */
    sloganSub?: string
    /** 底部文案（可选） */
    footer?: string
    /** 生态区标题（可选） */
    ecosystemTitle?: string
    /** 生态产品列表 */
    ecosystem?: WbEcosystemItem[]
    /** 当前产品标签文案（如 "当前" / "Current"） */
    currentTag?: string
    /** 当前语言（'zh-CN' | 'en-US'），仅用于切换器显示 */
    locale?: string
    /** 是否显示语言切换器 */
    showLangSwitch?: boolean
    /** 语言切换器按钮文案 */
    langLabels?: { zh: string; en: string }
  }>(),
  {
    sloganSub: '',
    footer: '',
    ecosystemTitle: '',
    ecosystem: () => [],
    currentTag: '',
    locale: 'zh-CN',
    showLangSwitch: true,
    langLabels: () => ({ zh: '中文', en: 'EN' }),
  },
)

const emit = defineEmits<{ 'locale-change': [locale: string] }>()

function onLang(cmd: string) {
  emit('locale-change', cmd)
}
</script>

<template>
  <div class="wb-auth-shell">
    <!-- 左：品牌区 -->
    <aside class="brand-side wb-soft">
      <div class="brand-top">
        <slot name="logo"><WbLogo :size="52" /></slot>
        <div class="brand-name">{{ name }}<b>{{ accent }}</b></div>
      </div>

      <div class="brand-slogan">
        <h1>{{ slogan }}</h1>
        <p v-if="sloganSub">{{ sloganSub }}</p>
      </div>

      <div v-if="ecosystem.length" class="brand-eco">
        <div v-if="ecosystemTitle" class="eco-title">{{ ecosystemTitle }}</div>
        <div
          v-for="item in ecosystem"
          :key="item.key"
          class="eco-item"
          :class="{ current: item.active }"
        >
          <span class="dot" :class="{ dim: item.dim }" />
          <span class="name">{{ item.label }}</span>
          <el-tag v-if="item.active && currentTag" size="small" round>{{ currentTag }}</el-tag>
        </div>
      </div>

      <div v-if="footer" class="brand-foot">{{ footer }}</div>
    </aside>

    <!-- 右：表单区（由使用方提供） -->
    <main class="form-side">
      <el-dropdown v-if="showLangSwitch" class="lang-switch" @command="onLang">
        <el-button text>{{ locale === 'zh-CN' ? langLabels.zh : langLabels.en }}</el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="zh-CN" :disabled="locale === 'zh-CN'">
              简体中文
            </el-dropdown-item>
            <el-dropdown-item command="en-US" :disabled="locale === 'en-US'">
              English
            </el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>

      <slot name="form" />
    </main>
  </div>
</template>

<style scoped>
.wb-auth-shell {
  height: 100%;
  display: flex;
}

.lang-switch {
  position: absolute;
  top: 18px;
  right: 22px;
  z-index: 10;
}

.brand-side {
  flex: 1.4;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 40px 52px;
  min-width: 0;
}

.brand-top {
  display: flex;
  align-items: center;
  gap: 14px;
}

.brand-name {
  font-size: 20px;
  font-weight: 300;
  color: var(--wb-ink);
}

.brand-name b {
  font-weight: 800;
  margin-left: 2px;
}

.brand-slogan {
  margin: auto 0;
}

.brand-slogan h1 {
  font-size: 46px;
  margin: 0 0 10px;
  font-weight: 800;
  letter-spacing: 0.04em;
  background: linear-gradient(120deg, #0284c7 0%, #38bdf8 100%);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}

.brand-slogan p {
  margin: 0;
  color: var(--wb-on-soft-muted);
  font-size: 15px;
  letter-spacing: 0.08em;
}

.brand-eco {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 28px;
}

.eco-title {
  font-size: 11px;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: #7c9cb8;
  font-weight: 700;
  margin-bottom: 2px;
}

.eco-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 14px;
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.75);
  border: 1px solid rgba(2, 132, 199, 0.1);
  color: #46617c;
  font-size: 14px;
  backdrop-filter: blur(4px);
}

.eco-item.current {
  color: var(--wb-primary-strong);
  border-color: rgba(14, 165, 233, 0.35);
  background: rgba(255, 255, 255, 0.9);
  box-shadow: 0 6px 18px -10px rgba(14, 116, 233, 0.25);
}

.eco-item .name {
  flex: 1;
}

.dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--wb-gradient);
  flex-shrink: 0;
}

.dot.dim {
  background: #bfd4e6;
}

.brand-foot {
  font-size: 12px;
  color: #9db4c9;
  letter-spacing: 0.12em;
}

.form-side {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--wb-bg);
  position: relative;
}

@media (max-width: 860px) {
  .brand-side {
    display: none;
  }
}
</style>
