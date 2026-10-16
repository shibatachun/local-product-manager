import { productInchesToMillimeters, productMillimetersToInches } from './product-dimension-units';

describe('inventory product creation dimensions', () => {
  it('converts millimeter inputs to the existing inch-based product fields', () => {
    expect(productMillimetersToInches(25.4)).toBe(1);
    expect(productMillimetersToInches(50.8)).toBe(2);
    expect(productMillimetersToInches(null)).toBeNull();
  });

  it('migrates legacy inch-based creation drafts to millimeter inputs', () => {
    expect(productInchesToMillimeters(1)).toBe(25.4);
    expect(productInchesToMillimeters(2.5)).toBe(63.5);
    expect(productInchesToMillimeters(null)).toBeNull();
  });
});
