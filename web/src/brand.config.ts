/**
 * WillBeyond 生态品牌配置
 *
 * 复制自 willBeyondEverything/web/src/brand.config.ts，按项目修改 accent。
 */

export interface WbBrandConfig {
  /** 品牌主名，如 willBeyond */
  name: string
  /** 品牌强调名（加粗显示），如 Everything / TimeBank */
  accent: string
}

export interface WbEcosystemItem {
  /** 唯一标识 */
  key: string
  /** 显示名（可带中文副名），如 "Everything · 万物管家" */
  label: string
  /** 是否为当前产品（高亮 + 显示"当前"标签） */
  active?: boolean
  /** 弱化显示（如"更多产品规划中"） */
  dim?: boolean
}

/** 当前项目品牌配置 */
export const wbBrand: WbBrandConfig = {
  name: 'willBeyond',
  accent: 'TimeBank',
}
