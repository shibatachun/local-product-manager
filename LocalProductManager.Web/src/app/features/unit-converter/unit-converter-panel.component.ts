import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  LENGTH_UNITS,
  WEIGHT_UNITS,
  convertCurrency,
  convertUnit,
  formatNumber,
  swapValues,
} from '../../utils/unit-conversion';

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
  readonly lengthUnits = LENGTH_UNITS;
  readonly weightUnits = WEIGHT_UNITS;

  lengthConversion(): number {
    return convertUnit(this.lengthConverter.amount, this.lengthConverter.from, this.lengthConverter.to, this.lengthUnits);
  }

  weightConversion(): number {
    return convertUnit(this.weightConverter.amount, this.weightConverter.from, this.weightConverter.to, this.weightUnits);
  }

  currencyConversion(): number {
    return convertCurrency(
      this.currencyConverter.amount,
      this.currencyConverter.from,
      this.currencyConverter.to,
      this.currencyConverter.rmbToUsdRate,
      0.14,
    );
  }

  swapLengthUnits(): void {
    swapValues(this.lengthConverter);
  }

  swapWeightUnits(): void {
    swapValues(this.weightConverter);
  }

  swapCurrencyUnits(): void {
    swapValues(this.currencyConverter);
  }

  number(value: number): string {
    return formatNumber(value, 2);
  }

  unit(value: number): string {
    return formatNumber(value, 4);
  }
}
