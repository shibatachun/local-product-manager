namespace LocalProductManager.Api.Services;

public static class WhatnotSafePriceCalculator
{
    public static decimal? Calculate(decimal? totalCostRmb, decimal rmbToUsdRate)
    {
        if (!totalCostRmb.HasValue) return null;
        const decimal commissionRate = 0.08m;
        const decimal processingRate = 0.029m;
        const decimal feeTaxReserveRate = 0.01m;
        const decimal fixedProcessingFee = 0.30m;
        const decimal assumedBuyerShippingAndTax = 10.00m;
        const decimal packagingReserve = 0.25m;
        const decimal hostLaborCostPerPackage = 25m / 60m;
        const decimal costSafetyBufferRate = 0.10m;
        var protectedCost = totalCostRmb.Value * rmbToUsdRate * (1 + costSafetyBufferRate)
            + packagingReserve + hostLaborCostPerPackage;
        var fixedAndAncillaryFees = fixedProcessingFee + assumedBuyerShippingAndTax * processingRate;
        var retainedSaleRate = 1 - commissionRate - processingRate - feeTaxReserveRate;
        return decimal.Ceiling((protectedCost + fixedAndAncillaryFees) / retainedSaleRate);
    }

    public static string? ToItemCode(decimal? suggestedPrice)
    {
        if (!suggestedPrice.HasValue || suggestedPrice.Value < 1) return null;
        var value = decimal.ToInt32(decimal.Round(suggestedPrice.Value, 0, MidpointRounding.AwayFromZero));
        var code = "";
        while (value > 0)
        {
            value--;
            code = (char)('A' + value % 26) + code;
            value /= 26;
        }
        return code;
    }
}
