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
    public void Read_ReusedNumberOnDifferentOrders_KeepsBothFullNames()
    {
        var path = WriteReport(
            "order-1,buyer-one,Blue beads #1,1,2026-09-22 13:39:23.316,,shipment-1",
            "order-2,buyer-two,Red charms #1,1,2026-09-22 16:38:43.551,,shipment-2");

        try
        {
            var report = WhatnotShowReportImporter.Read(path);

            Assert.Equal(["1", "1"], report.Sales.Select(sale => sale.ProductNumber));
            Assert.Equal(["Blue beads #1", "Red charms #1"], report.Sales.Select(sale => sale.PrintProductName));
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
            Assert.Equal(["Lots of Charms #1", "Lots of Charms #2"], report.Sales.Select(sale => sale.PrintProductName));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrintProductName_OldShowReportUsesTitle_OldLiveSaleUsesNumber()
    {
        Assert.Equal("Blue beads #1", new SaleEvent
        {
            ProductNumber = "1", SourceText = "Blue beads #1", ReportRowNumber = 2
        }.PrintProductName);
        Assert.Equal("1", new SaleEvent
        {
            ProductNumber = "1", SourceText = "buyer won Blue beads #1"
        }.PrintProductName);
    }

    [Fact]
    public void Read_DuplicateOrderId_StillStopsImport()
    {
        var path = WriteReport(
            "order-1,buyer-one,Blue beads #1,1,2026-09-22 13:39:23.316,,shipment-1",
            "order-1,buyer-two,Red charms #1,1,2026-09-22 16:38:43.551,,shipment-2");

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => WhatnotShowReportImporter.Read(path));
            Assert.Contains("duplicate order_id order-1", error.Message);
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
