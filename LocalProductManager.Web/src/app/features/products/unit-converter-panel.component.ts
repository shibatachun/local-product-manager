import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface UnitOption {
  value: string;
  label: string;
  factorToBase: number;
}

@Component({
  selector: 'app-unit-converter-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './unit-converter-panel.component.html',
  styleUrl: './unit-converter-panel.component.css',
})
export class UnitConverterPanelComponent {
  readonly lengthConverter = {
    amount: 1,
    from: 'in',
    to: 'cm',
  };
  readonly weightConverter = {
    amount: 16,
    from: 'oz',
    to: 'lb',
  };
  readonly currencyConverter = {
    amount: 100,
    from: 'rmb',
    to: 'usd',
    rmbToUsdRate: 0.14,
  };
  readonly lengthUnits: UnitOption[] = [
    { value: 'in', label: 'Inch (in)', factorToBase: 0.0254 },
    { value: 'ft', label: 'Foot (ft)', factorToBase: 0.3048 },
    { value: 'yd', label: 'Yard (yd)', factorToBase: 0.9144 },
    { value: 'mm', label: 'Millimeter (mm)', factorToBase: 0.001 },
    { value: 'cm', label: 'Centimeter (cm)', factorToBase: 0.01 },
    { value: 'm', label: 'Meter (m)', factorToBase: 1 },
  ];
  readonly weightUnits: UnitOption[] = [
    { value: 'oz', label: 'Ounce (oz)', factorToBase: 28.349523125 },
    { value: 'lb', label: 'Pound (lb)', factorToBase: 453.59237 },
    { value: 'g', label: 'Gram (g)', factorToBase: 1 },
    { value: 'kg', label: 'Kilogram (kg)', factorToBase: 1000 },
  ];

  lengthConversion(): number {
    return this.convertUnit(this.lengthConverter.amount, this.lengthConverter.from, this.lengthConverter.to, this.lengthUnits);
  }

  weightConversion(): number {
    return this.convertUnit(this.weightConverter.amount, this.weightConverter.from, this.weightConverter.to, this.weightUnits);
  }

  currencyConversion(): number {
    const amount = this.safeNumber(this.currencyConverter.amount);
    const rate = this.safeNumber(this.currencyConverter.rmbToUsdRate) || 0.14;
    if (this.currencyConverter.from === this.currencyConverter.to) return amount;
    return this.currencyConverter.from === 'rmb' ? amount * rate : amount / rate;
  }

  swapLengthUnits(): void {
    const from = this.lengthConverter.from;
    this.lengthConverter.from = this.lengthConverter.to;
    this.lengthConverter.to = from;
  }

  swapWeightUnits(): void {
    const from = this.weightConverter.from;
    this.weightConverter.from = this.weightConverter.to;
    this.weightConverter.to = from;
  }

  swapCurrencyUnits(): void {
    const from = this.currencyConverter.from;
    this.currencyConverter.from = this.currencyConverter.to;
    this.currencyConverter.to = from;
  }

  number(value: number): string {
    return this.safeNumber(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
  }

  unit(value: number): string {
    return this.safeNumber(value).toLocaleString(undefined, { maximumFractionDigits: 4 });
  }

  private convertUnit(amount: number | null | undefined, from: string, to: string, units: UnitOption[]): number {
    const fromUnit = units.find((unit) => unit.value === from) ?? units[0];
    const toUnit = units.find((unit) => unit.value === to) ?? units[0];
    return this.safeNumber(amount) * fromUnit.factorToBase / toUnit.factorToBase;
  }

  private safeNumber(value: number | null | undefined): number {
    const numeric = Number(value ?? 0);
    return Number.isFinite(numeric) ? numeric : 0;
  }
}
