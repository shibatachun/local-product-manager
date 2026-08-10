export type LabelSettings = {
  labelMode: 'product' | 'bin'; widthMm: number; heightMm: number; columns: 1 | 2;
  columnGapMm: number; rowGapMm: number; sideMarginMm: number; fontSize: number;
  barcodeHeight: number; showInv: boolean; showCategoryId: boolean; showSku: boolean;
  showName: boolean; showQuantity: boolean; binDescription: string;
};

export type SavedLabelPreset = { name: string; settings: LabelSettings | null };

export const labelPresetStorageKey = 'lpm-label-maker-custom-presets-v1';

export const emptyLabelPresetSlots = (): SavedLabelPreset[] => [
  { name: 'Preset 1', settings: null },
  { name: 'Preset 2', settings: null },
  { name: 'Preset 3', settings: null },
];

export const defaultProductLabelPresets = (): SavedLabelPreset[] => [
  {
    name: '70 × 20 mm · Single column',
    settings: {
      labelMode: 'product', widthMm: 70, heightMm: 20, columns: 1,
      columnGapMm: 0, rowGapMm: 0, sideMarginMm: 0, fontSize: 11,
      barcodeHeight: 48, showInv: true, showCategoryId: true, showSku: true,
      showName: true, showQuantity: false, binDescription: '',
    },
  },
  {
    name: '50 × 30 mm · Double column',
    settings: {
      labelMode: 'product', widthMm: 50, heightMm: 30, columns: 2,
      columnGapMm: 0, rowGapMm: 0, sideMarginMm: 0, fontSize: 11,
      barcodeHeight: 48, showInv: true, showCategoryId: true, showSku: true,
      showName: true, showQuantity: false, binDescription: '',
    },
  },
];

export function loadSavedLabelPresets(): SavedLabelPreset[] {
  const fallback = emptyLabelPresetSlots();
  try {
    const saved = JSON.parse(localStorage.getItem(labelPresetStorageKey) || 'null');
    if (!Array.isArray(saved)) return fallback;
    return fallback.map((slot, index) => {
      const entry = saved[index];
      return entry && typeof entry === 'object'
        ? {
            name: typeof entry.name === 'string' && entry.name.trim() ? entry.name.slice(0, 32) : slot.name,
            settings: entry.settings && typeof entry.settings === 'object' ? entry.settings as LabelSettings : null,
          }
        : slot;
    });
  } catch {
    return fallback;
  }
}

export function saveLabelPresets(presets: SavedLabelPreset[]): void {
  try { localStorage.setItem(labelPresetStorageKey, JSON.stringify(presets)); }
  catch { /* Label printing remains available when browser storage is unavailable. */ }
}
