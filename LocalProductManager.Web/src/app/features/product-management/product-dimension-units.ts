// Product.Length/Width/Height are stored as inches for existing pricing calculations.
// The inventory product creation form accepts millimeters.
export function productMillimetersToInches(value: number | null): number | null {
  return value === null || !Number.isFinite(value) ? null : Math.round((value / 25.4) * 100) / 100;
}

export function productInchesToMillimeters(value: number | null): number | null {
  return value === null || !Number.isFinite(value) ? null : Math.round(value * 25.4 * 100) / 100;
}
