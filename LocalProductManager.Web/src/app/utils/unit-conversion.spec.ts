import { describe, expect, it } from 'vitest';
import {
  LENGTH_UNITS,
  WEIGHT_UNITS,
  convertCurrency,
  convertUnit,
  safeNumber,
  swapValues,
} from './unit-conversion';

describe('unit conversion utilities', () => {
  it('converts length and weight through their base units', () => {
    expect(convertUnit(1, 'in', 'cm', LENGTH_UNITS)).toBeCloseTo(2.54);
    expect(convertUnit(16, 'oz', 'lb', WEIGHT_UNITS)).toBeCloseTo(1);
  });

  it('converts currencies in both directions', () => {
    expect(convertCurrency(100, 'rmb', 'usd', 0.14, 0.14)).toBeCloseTo(14);
    expect(convertCurrency(14, 'usd', 'rmb', 0.14, 0.14)).toBeCloseTo(100);
  });

  it('normalizes invalid numbers and swaps selections', () => {
    expect(safeNumber(Number.NaN)).toBe(0);
    const selection = { from: 'lb', to: 'kg' };
    swapValues(selection);
    expect(selection).toEqual({ from: 'kg', to: 'lb' });
  });
});
