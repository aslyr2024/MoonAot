import { computed, ref } from 'vue'
import { darkTheme } from 'naive-ui'
import type { AppTheme } from './types'

export interface Palette {
  name: AppTheme
  label: string
  dark: boolean
  bg: string
  bgSoft: string
  panel: string
  panel2: string
  panel3: string
  border: string
  borderStrong: string
  text: string
  text2: string
  text3: string
  accent: string
  accent2: string
  accentSoft: string
  accentRing: string
  shadow: string
  danger: string
  success: string
  warning: string
  coverBg: string
}

export const PALETTES: Record<AppTheme, Palette> = {
  dark: {
    name: 'dark', label: '深空黑', dark: true,
    bg: '#0a0c10', bgSoft: '#0e1116', panel: '#141820', panel2: '#1a2029', panel3: '#232b37',
    border: 'rgba(255,255,255,0.075)', borderStrong: 'rgba(255,255,255,0.16)',
    text: '#e9ecf3', text2: '#aab2c2', text3: '#6f7a8d',
    accent: '#6ea8fe', accent2: '#93c1ff', accentSoft: 'rgba(110,168,254,0.14)', accentRing: 'rgba(110,168,254,0.32)',
    shadow: '0 18px 55px rgba(0,0,0,0.55)', danger: '#ff6b81', success: '#3ddc97', warning: '#ffc857',
    coverBg: '#1b212b',
  },
  light: {
    name: 'light', label: '月光白', dark: false,
    bg: '#eef1f6', bgSoft: '#f6f8fb', panel: '#ffffff', panel2: '#f2f4f9', panel3: '#e7ebf3',
    border: 'rgba(15,23,42,0.09)', borderStrong: 'rgba(15,23,42,0.18)',
    text: '#171c26', text2: '#4a5568', text3: '#8a93a5',
    accent: '#3b6ef6', accent2: '#2b5fd9', accentSoft: 'rgba(59,110,246,0.10)', accentRing: 'rgba(59,110,246,0.22)',
    shadow: '0 18px 45px rgba(15,23,42,0.14)', danger: '#e11d48', success: '#0f9d69', warning: '#c27c0e',
    coverBg: '#e3e7ef',
  },
  forest: {
    name: 'forest', label: '森林绿', dark: true,
    bg: '#07110d', bgSoft: '#0b1712', panel: '#101f18', panel2: '#15291f', panel3: '#1d3529',
    border: 'rgba(160,255,214,0.08)', borderStrong: 'rgba(160,255,214,0.18)',
    text: '#e7f3ec', text2: '#a9c4b7', text3: '#6c8a7b',
    accent: '#35c98b', accent2: '#62e3ab', accentSoft: 'rgba(53,201,139,0.14)', accentRing: 'rgba(53,201,139,0.30)',
    shadow: '0 18px 55px rgba(0,0,0,0.5)', danger: '#ff6b81', success: '#35c98b', warning: '#ffc857',
    coverBg: '#15291f',
  },
  violet: {
    name: 'violet', label: '紫罗兰', dark: true,
    bg: '#0d0917', bgSoft: '#120e1f', panel: '#181127', panel2: '#201733', panel3: '#2a1f42',
    border: 'rgba(214,190,255,0.09)', borderStrong: 'rgba(214,190,255,0.18)',
    text: '#efe9ff', text2: '#b9aed6', text3: '#7f739b',
    accent: '#a879ff', accent2: '#c3a2ff', accentSoft: 'rgba(168,121,255,0.15)', accentRing: 'rgba(168,121,255,0.32)',
    shadow: '0 18px 55px rgba(0,0,0,0.55)', danger: '#ff6b81', success: '#3ddc97', warning: '#ffc857',
    coverBg: '#201733',
  },
  warm: {
    name: 'warm', label: '琥珀橙', dark: true,
    bg: '#130e0a', bgSoft: '#1a1310', panel: '#211711', panel2: '#2b1e15', panel3: '#37271b',
    border: 'rgba(255,214,170,0.08)', borderStrong: 'rgba(255,214,170,0.18)',
    text: '#f6ece2', text2: '#cfb9a5', text3: '#907a66',
    accent: '#f0913f', accent2: '#ffb066', accentSoft: 'rgba(240,145,63,0.15)', accentRing: 'rgba(240,145,63,0.32)',
    shadow: '0 18px 55px rgba(0,0,0,0.5)', danger: '#ff6b81', success: '#3ddc97', warning: '#ffc857',
    coverBg: '#2b1e15',
  },
}

export const themeNames: AppTheme[] = ['dark', 'light', 'forest', 'violet', 'warm']

export const themeName = ref<AppTheme>('dark')
export const palette = computed<Palette>(() => PALETTES[themeName.value] ?? PALETTES.dark)

const FONT = '"Microsoft YaHei UI", "PingFang SC", "HarmonyOS Sans SC", "Segoe UI", system-ui, -apple-system, sans-serif'

export function applyTheme(name: AppTheme) {
  const p = PALETTES[name] ?? PALETTES.dark
  themeName.value = p.name
  const root = document.documentElement
  root.dataset.theme = p.name
  root.style.colorScheme = p.dark ? 'dark' : 'light'
  const vars: Record<string, string> = {
    '--bg': p.bg, '--bg-soft': p.bgSoft, '--panel': p.panel, '--panel-2': p.panel2, '--panel-3': p.panel3,
    '--border': p.border, '--border-strong': p.borderStrong, '--text': p.text, '--text-2': p.text2,
    '--text-3': p.text3, '--accent': p.accent, '--accent-2': p.accent2, '--accent-soft': p.accentSoft,
    '--accent-ring': p.accentRing, '--shadow': p.shadow, '--danger': p.danger, '--success': p.success,
    '--warning': p.warning, '--cover-bg': p.coverBg, '--font-ui': FONT,
  }
  for (const [k, v] of Object.entries(vars)) root.style.setProperty(k, v)
  try { localStorage.setItem('moonaot-theme', p.name) } catch { }
}

export function initThemeFromStorage() {
  let name: AppTheme = 'dark'
  try {
    const saved = localStorage.getItem('moonaot-theme') as AppTheme | null
    if (saved && PALETTES[saved]) name = saved
  } catch { }
  applyTheme(name)
}

export const naiveTheme = computed(() => (palette.value.dark ? darkTheme : null))

export const themeOverrides = computed(() => {
  const p = palette.value
  return {
    common: {
      primaryColor: p.accent,
      primaryColorHover: p.accent2,
      primaryColorPressed: p.accent2,
      primaryColorSuppl: p.accent2,
      bodyColor: p.bg,
      cardColor: p.panel,
      modalColor: p.panel,
      popoverColor: p.panel2,
      tableColor: p.panel,
      tableHeaderColor: p.panel2,
      borderColor: p.border,
      dividerColor: p.border,
      inputColor: p.panel2,
      actionColor: p.panel2,
      actionColorHover: p.panel3,
      hoverColor: p.panel3,
      textColorBase: p.text,
      textColor1: p.text,
      textColor2: p.text2,
      textColor3: p.text3,
      placeholderColor: p.text3,
      borderRadius: '10px',
      borderRadiusSmall: '8px',
      fontFamily: FONT,
      fontSize: '13px',
      heightSmall: '28px',
      heightMedium: '34px',
      heightLarge: '40px',
    },
    Button: {
      textColor: p.text2,
      textColorHover: p.text,
      textColorPressed: p.text,
      textColorFocus: p.text,
      border: `1px solid ${p.border}`,
      borderHover: `1px solid ${p.accent}`,
      borderPressed: `1px solid ${p.accent}`,
      color: 'transparent',
      colorHover: p.panel3,
      colorPressed: p.panel3,
      colorFocus: p.panel3,
      textColorPrimary: '#ffffff',
      colorPrimary: p.accent,
      colorHoverPrimary: p.accent2,
      colorPressedPrimary: p.accent2,
      colorFocusPrimary: p.accent2,
      borderPrimary: '1px solid transparent',
      borderHoverPrimary: '1px solid transparent',
      borderFocusPrimary: '1px solid transparent',
      borderPressedPrimary: '1px solid transparent',
      textColorPrimaryHover: '#ffffff',
    },
    Input: {
      color: p.panel2,
      colorFocus: p.panel2,
      border: `1px solid ${p.border}`,
      borderHover: `1px solid ${p.borderStrong}`,
      borderFocus: `1px solid ${p.accent}`,
      boxShadowFocus: `0 0 0 3px ${p.accentRing}`,
      textColor: p.text,
      placeholderColor: p.text3,
      caretColor: p.accent,
    },
    Select: {
      peers: {
        InternalSelection: {
          color: p.panel2,
          colorActive: p.panel2,
          border: `1px solid ${p.border}`,
          borderHover: `1px solid ${p.borderStrong}`,
          borderActive: `1px solid ${p.accent}`,
          boxShadowActive: `0 0 0 3px ${p.accentRing}`,
          textColor: p.text,
          placeholderColor: p.text3,
          arrowColor: p.text3,
        },
        InternalSelectMenu: {
          color: p.panel2,
          optionTextColor: p.text2,
          optionTextColorActive: p.accent,
          optionColorPending: p.panel3,
          optionColorActive: p.accentSoft,
        },
      },
    },
    Slider: {
      railColor: p.panel3,
      railColorHover: p.panel3,
      fillColor: p.accent,
      fillColorHover: p.accent2,
      handleColor: p.accent,
      handleColorHover: p.accent2,
    },
    Switch: { railColorActive: p.accent, buttonColor: '#ffffff' },
    Tag: { color: p.panel3, textColor: p.text2, border: '1px solid transparent' },
    Dropdown: {
      color: p.panel2,
      optionTextColor: p.text2,
      optionTextColorHover: p.text,
      optionTextColorActive: p.accent,
      optionColorHover: p.panel3,
      optionColorActive: p.accentSoft,
      borderColor: p.border,
      dividerColor: p.border,
    },
    Drawer: {
      color: p.panel,
      headerBorderBottom: `1px solid ${p.border}`,
      footerBorderTop: `1px solid ${p.border}`,
      titleTextColor: p.text,
      bodyPadding: '20px',
      closeIconColor: p.text3,
      closeIconColorHover: p.text,
    },
    Modal: { color: p.panel, borderColor: p.border },
    Tabs: {
      tabTextColorActive: p.accent,
      tabTextColorHover: p.text,
      tabTextColor: p.text3,
      barColor: p.accent,
      tabBorderColor: p.border,
    },
    Progress: { railColor: p.panel3, fillColor: p.accent },
    Skeleton: { color: p.panel2, colorEnd: p.panel3 },
    Empty: { iconColor: p.text3, textColor: p.text3 },
    Popover: { color: p.panel2, borderColor: p.border },
    Tooltip: { color: p.panel3, textColor: p.text },
    Message: { color: p.panel2, textColor: p.text },
    Dialog: { color: p.panel, titleTextColor: p.text, textColor: p.text2 },
    Checkbox: { checkMarkColor: '#ffffff' },
    Radio: { buttonTextColorActive: '#ffffff' },
    Pagination: { itemColor: p.panel2, itemColorActive: p.accentSoft, itemTextColorActive: p.accent },
  }
})
