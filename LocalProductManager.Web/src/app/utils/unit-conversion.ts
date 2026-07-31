export interface UnitOption {
  value: string;
  label: string;
  factorToBase: number;
}

export const LENGTH_UNITS: readonly UnitOption[] = [
  { value: 'in', label: 'Inch (in)', factorToBase: 0.0254 },
  { value: 'ft', label: 'Foot (ft)', factorToBase: 0.3048 },
  { value: 'yd', label: 'Yard (yd)', factorToBase: 0.9144 },
  { value: 'mm', label: 'Millimeter (mm)', factorToBase: 0.001 },
  { value: 'cm', label: 'Centimeter (cm)', factorToBase: 0.01 },
  { value: 'm', label: 'Meter (m)', factorToBase: 1 },
];

export const WEIGHT_UNITS: readonly UnitOption[] = [
  { value: 'oz', label: 'Ounce (oz)', factorToBase: 28.349523125 },
  { value: 'lb', label: 'Pound (lb)', factorToBase: 453.59237 },
  { value: 'g', label: 'Gram (g)', factorToBase: 1 },
  { value: 'kg', label: 'Kilogram (kg)', factorToBase: 1000 },
];

export function safeNumber(value: number | null | undefined): number {
  const numeric = Number(value ?? 0);
  return Number.isFinite(numeric) ? numeric : 0;
}

export function convertUnit(
  amount: number | null | undefined,
  from: string,
  to: string,
  units: readonly UnitOption[],
): number {
  const fromUnit = units.find((unit) => unit.value === from) ?? units[0];
  const toUnit = units.find((unit) => unit.value === to) ?? units[0];
  if (!fromUnit || !toUnit) return 0;
  return safeNumber(amount) * fromUnit.factorToBase / toUnit.factorToBase;
}

export function convertCurrency(
  amount: number | null | undefined,
  from: string,
  to: string,
  baseToTargetRate: number | null | undefined,
  defaultRate: number,
): number {
  const normalizedAmount = safeNumber(amount);
  const rate = safeNumber(baseToTargetRate) || defaultRate;
  if (from === to) return normalizedAmount;
  return from === 'rmb' ? normalizedAmount * rate : normalizedAmount / rate;
}

export function swapValues<T extends { from: string; to: string }>(value: T): void {
  [value.from, value.to] = [value.to, value.from];
}

export function formatNumber(value: number, maximumFractionDigits: number): string {
  return safeNumber(value).toLocaleString(undefined, { maximumFractionDigits });
}
