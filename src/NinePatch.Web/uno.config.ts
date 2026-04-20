import { defineConfig, presetUno, presetIcons } from 'unocss'

export default defineConfig({
  presets: [
    presetUno(),
    presetIcons(),
  ],
  shortcuts: {
    'btn': 'px-4 py-2 rounded-lg bg-blue-500 text-white cursor-pointer hover:bg-blue-600 active:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors',
    'card': 'bg-white rounded-xl border border-gray-200 shadow-sm p-4',
    'label': 'text-sm font-medium text-gray-700',
    'input-range': 'w-full accent-blue-500',
  },
})
