import { Platform } from '../../core/models';

export type ViewMode = 'products' | 'domesticProducts' | 'autoListing' | 'bidProducts' | 'inventory' | 'broochFinder' | 'barcodeScanner' | 'labelMaker' | 'pricing' | 'unitConverter' | 'marketing' | 'storage' | 'mediaLibrary' | 'videoTrend' | 'sceneEditor' | 'apiDocs';
export type MarketingProductFilter = 'all' | 'standalone' | 'family-one';
export type PricingPlatform = Platform;
export type ProductCostCurrency = 'rmb' | 'usd';
export type PricingWeightUnit = 'lb' | 'oz' | 'g' | 'kg';

export type TauriInternals = {
  invoke<T>(command: string, args?: Record<string, unknown>): Promise<T>;
};

export type AsinRelationshipRow = {
  parentAsin: string;
  childAsin: string;
  sku: string;
  amazonAttributeDone: boolean;
  source: 'current' | 'child';
};

export interface PricingStep {
  label: string;
  value: string;
  note: string;
}

export interface PricingEstimate {
  platform: PricingPlatform;
  logistics: string;
  category: string;
  categoryRate: number;
  quantity: number;
  unitCost: number;
  productCostCurrency: ProductCostCurrency;
  unitProductCostRmb: number;
  unitProductCostUsd: number;
  unitFirstMileRmb: number;
  unitFirstMileUsd: number;
  unitLandedCostRmb: number;
  totalLandedCostRmb: number;
  firstMileRate: number;
  unitWeightInput: number;
  unitWeightUnit: PricingWeightUnit;
  packagingWeightInput: number;
  packagingWeightUnit: PricingWeightUnit;
  unitWeightGrams: number;
  unitProductWeight: number;
  packagingWeight: number;
  totalProductWeight: number;
  totalPackageWeight: number;
  rmbToUsdRate: number;
  exchangeRateDate: string;
  exchangeRateSource: string;
  totalCost: number;
  cubicInches: number;
  dimensionalWeight: number;
  billableWeight: number;
  shippingCost: number;
  breakEvenPrice: number;
  rawSuggestedPrice: number;
  suggestedPrice: number;
  estimatedProfit: number;
  targetProfit: number;
  targetProfitEnabled: boolean;
  steps: PricingStep[];
}

export interface LogisticsOption {
  value: string;
  label: string;
  note: string;
  dimDivisor: number;
  tiers: { max: number; cost: number; baseAbove?: number; perLb?: number }[];
  overflowPerLb: number;
  weightPaddingLb?: number;
  includePackagingReserve?: boolean;
}

export interface PricingPlatformConfig {
  label: string;
  defaultLogistics: string;
  defaultCategory: string;
  processingRate: number;
  fixedFee: number;
  listingFee: number;
  marginPercent: number;
  bufferPercent: number;
  note: string;
  categories: PricingCategory[];
  logistics: LogisticsOption[];
}

export interface PricingCategory {
  value: string;
  label: string;
  feeRate: number;
  note: string;
}

export interface PricingInput {
  platform: PricingPlatform;
  category: string;
  logistics: string;
  quantity: number;
  unitCost: number | null;
  productCostCurrency: ProductCostCurrency;
  unitWeight: number | null;
  unitWeightUnit: PricingWeightUnit;
  packagingWeight: number | null;
  packagingWeightUnit: PricingWeightUnit;
  firstMileRate: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  packagingCost: number | null;
  targetProfit: number | null;
  targetMarginPercent: number | null;
  bufferPercent: number | null;
}

export interface PlatformChoice {
  platform: Platform;
  label: string;
  selected: boolean;
  url: string;
}
