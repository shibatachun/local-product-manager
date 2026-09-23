using System.IO;
using AuctionProductMonitor.Wpf;
using Xunit;

namespace AuctionProductMonitor.Wpf.Tests;

public sealed class WhatnotShowReportImporterTests
{
    [Fact]
    public void Read_FreeGiftNumber_CanReuseAuctionNumberAndKeepsPrintableLabel()
    {
        var path = WriteReport(
            "order-1,buyer-one,Lots of Charms #1,1,2026-09-22 13:39:23.316,,shipment-1",
            "order-2,buyer-two,Free Gift #1,1,2026-09-22 16:38:43.551,,shipment-2");

        try
        {
            var report = WhatnotShowReportImporter.Read(path);

            Assert.Equal(["1", "Free Gift #1"], report.Sales.Select(sale => sale.ProductNumber));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_DuplicateFreeGiftLabel_ReportsBothRows()
    {
        var path = WriteReport(
            "order-1,buyer-one,Free Gift #1,1,2026-09-22 13:39:23.316,,shipment-1",
            "order-2,buyer-two,Free Gift #1,1,2026-09-22 16:38:43.551,,shipment-2");

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => WhatnotShowReportImporter.Read(path));

            Assert.Contains("row 3: duplicate product label Free Gift #1", error.Message);
            Assert.Contains("first used on row 2 by buyer-one (Free Gift #1)", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_UniqueProductNumbers_ReturnsSalesInReportOrder()
    {
        var path = WriteReport(
            "order-1,buyer-one,Lots of Charms #1,1,2026-09-22 13:39:23.316,,shipment-1",
            "order-2,buyer-two,Lots of Charms #2,2,2026-09-22 13:40:59.056,,shipment-2");

        try
        {
            var report = WhatnotShowReportImporter.Read(path);

            Assert.Equal("2026-09-22", report.ShowDate);
            Assert.Equal(["1", "2"], report.Sales.Select(sale => sale.ProductNumber));
            Assert.Equal([1, 2], report.Sales.Select(sale => sale.Quantity));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteReport(params string[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"auction-monitor-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path,
        [
            "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed,shipment_id",
            .. rows
        ]);
        return path;
    }
}
