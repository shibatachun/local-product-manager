using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class LiveStreamBidShowCostImportTests
{
    [Fact]
    public async Task SettledShow_AllowsEditingExistingManualAndScannedCostAmounts()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        const string csv = "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed\r\n" +
                           "O-M,alice,Manual item,1,2026-09-25T19:00:00Z,\r\n" +
                           "O-S,alice,Scanned item,1,2026-09-25T19:01:00Z,\r\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "Editable Cost Show.csv");
        var importResponse = await client.PostAsync("/api/live-stream-bid-products/show-sessions/import", form);
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(imported);
        var orders = Assert.Single(imported.Show.Customers).Orders;
        var manualOrder = orders.Single(item => item.ExternalOrderId == "O-M");
        var scannedOrder = orders.Single(item => item.ExternalOrderId == "O-S");

        var manualResponse = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-orders/{manualOrder.Id}/manual-costs", new { quantity = 1, unitCost = 3m });
        manualResponse.EnsureSuccessStatusCode();
        var manualEntry = await manualResponse.Content.ReadFromJsonAsync<IdResult>();
        Assert.NotNull(manualEntry);
        int productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new LiveStreamBidProduct
            {
                Uuid = Guid.NewGuid().ToString(), Title = "Scanned item", Quantity = 1,
                Inventory = 1, ActualInventory = 1, UnitPrice = 2
            };
            db.LiveStreamBidProducts.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }
        var scanResponse = await client.PostAsJsonAsync($"/api/live-stream-bid-products/{productId}/confirm-sale",
            new { actualSalePrice = (decimal?)null, showSessionId = imported.Show.Id, showOrderId = scannedOrder.Id, quantity = 1 });
        scanResponse.EnsureSuccessStatusCode();
        var scannedEntry = await scanResponse.Content.ReadFromJsonAsync<IdResult>();
        Assert.NotNull(scannedEntry);
        var settledResponse = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{imported.Show.Id}/stop", new { });
        settledResponse.EnsureSuccessStatusCode();

        var manualEdit = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/manual-costs/{manualEntry.Id}", new { unitCost = 5m });
        manualEdit.EnsureSuccessStatusCode();
        var scanEdit = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/sales/{scannedEntry.Id}/cost", new { cost = 7m });
        scanEdit.EnsureSuccessStatusCode();
        var negativeEdit = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/manual-costs/{manualEntry.Id}", new { unitCost = -1m });
        Assert.Equal(HttpStatusCode.BadRequest, negativeEdit.StatusCode);
        var afterSettlementQuantity = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/show-orders/{manualOrder.Id}/quantity", new { quantity = 2 });
        Assert.Equal(HttpStatusCode.Conflict, afterSettlementQuantity.StatusCode);

        var day = await client.GetFromJsonAsync<DailySummary>($"/api/live-stream-bid-products/sales/by-date?date={imported.Show.Date:yyyy-MM-dd}");
        Assert.NotNull(day);
        var show = Assert.Single(day.Shows);
        Assert.Equal(12m, show.TotalCostRmb);
        Assert.Equal(2, show.RecordedCostQuantity);
        var customer = Assert.Single(show.Customers);
        Assert.Equal(5m, customer.Orders.Single(item => item.ExternalOrderId == "O-M").TotalCostRmb);
        Assert.Equal(7m, customer.Orders.Single(item => item.ExternalOrderId == "O-S").TotalCostRmb);
    }

    [Fact]
    public async Task RawPriceEvidenceUpload_SavesPhotoWithoutMultipartForm()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        const string csv = "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed\r\n" +
                           "O-RAW,alice,Mixed lot,1,2026-09-25T19:00:00Z,\r\n";
        using var importForm = new MultipartFormDataContent();
        importForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "Raw Evidence Show.csv");
        var importResponse = await client.PostAsync("/api/live-stream-bid-products/show-sessions/import", importForm);
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(imported);
        var order = Assert.Single(Assert.Single(imported.Show.Customers).Orders);
        var photoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };

        using var photoContent = new ByteArrayContent(photoBytes);
        photoContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var response = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/orders/{order.Id}/price-evidence/raw", photoContent);
        response.EnsureSuccessStatusCode();

        var summary = await client.GetFromJsonAsync<ShowSummary>("/api/live-stream-bid-products/show-sessions/active");
        Assert.NotNull(summary);
        var evidence = Assert.Single(Assert.Single(Assert.Single(summary.Customers).Orders).PriceEvidence);
        Assert.StartsWith("price-evidence-", evidence.OriginalFileName);
        Assert.EndsWith(".jpg", evidence.OriginalFileName);
        var savedPhoto = await client.GetByteArrayAsync($"/api/live-stream-bid-products/show-attachments/files/{evidence.Id}");
        Assert.Equal(photoBytes, savedPhoto);

        using var emptyContent = new ByteArrayContent([]);
        emptyContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var emptyResponse = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/orders/{order.Id}/price-evidence/raw", emptyContent);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        Assert.Contains("server received an empty photo upload", await emptyResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ShowAttachments_KeepOrderPricePhotosAndBuyerShippingDocuments()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        const string csv = "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed\r\n" +
                           "O-1,alice,Mixed lot,1,2026-09-25T19:00:00Z,\r\n";
        using var importForm = new MultipartFormDataContent();
        importForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "Evidence Show.csv");
        var importResponse = await client.PostAsync("/api/live-stream-bid-products/show-sessions/import", importForm);
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(imported);
        var order = Assert.Single(Assert.Single(imported.Show.Customers).Orders);

        static MultipartFormDataContent AttachmentForm(byte[] bytes, string fileName, string contentType)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);
            return form;
        }

        var photoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        using (var form = AttachmentForm(photoBytes, "price.jpg", "image/jpeg"))
        {
            var response = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/orders/{order.Id}/price-evidence", form);
            response.EnsureSuccessStatusCode();
        }
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("not a photo"), "other");
            var response = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/orders/{order.Id}/price-evidence", form);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Choose one photo to upload.", await response.Content.ReadAsStringAsync());
        }
        using (var form = AttachmentForm(Encoding.ASCII.GetBytes("%PDF-1.4\n"), "slip.pdf", "application/pdf"))
        {
            form.Add(new StringContent("alice"), "customerName");
            form.Add(new StringContent("shipping-slip"), "kind");
            var response = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/shows/{imported.Show.Id}/shipment-documents", form);
            response.EnsureSuccessStatusCode();
        }
        using (var form = AttachmentForm(photoBytes, "label.jpg", "image/jpeg"))
        {
            form.Add(new StringContent("alice"), "customerName");
            form.Add(new StringContent("shipping-label"), "kind");
            var response = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/shows/{imported.Show.Id}/shipment-documents", form);
            response.EnsureSuccessStatusCode();
        }
        var summary = await client.GetFromJsonAsync<ShowSummary>("/api/live-stream-bid-products/show-sessions/active");
        Assert.NotNull(summary);
        var customer = Assert.Single(summary.Customers);
        var evidence = Assert.Single(Assert.Single(customer.Orders).PriceEvidence);
        Assert.Equal("price.jpg", evidence.OriginalFileName);
        Assert.Equal(["shipping-slip", "shipping-label"], customer.ShipmentDocuments.Select(item => item.Kind).ToArray());
        var savedPhoto = await client.GetByteArrayAsync($"/api/live-stream-bid-products/show-attachments/files/{evidence.Id}");
        Assert.Equal(photoBytes, savedPhoto);

        using var wrongBuyerForm = AttachmentForm(photoBytes, "wrong.jpg", "image/jpeg");
        wrongBuyerForm.Add(new StringContent("not-this-buyer"), "customerName");
        wrongBuyerForm.Add(new StringContent("shipping-label"), "kind");
        var wrongBuyer = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/shows/{imported.Show.Id}/shipment-documents", wrongBuyerForm);
        Assert.Equal(HttpStatusCode.NotFound, wrongBuyer.StatusCode);

        var manual = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-orders/{order.Id}/manual-costs", new { quantity = 1, unitCost = 3m });
        manual.EnsureSuccessStatusCode();
        var settled = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{imported.Show.Id}/stop", new { });
        settled.EnsureSuccessStatusCode();
        using var lateLabelForm = AttachmentForm(photoBytes, "replacement-label.jpg", "image/jpeg");
        lateLabelForm.Add(new StringContent("alice"), "customerName");
        lateLabelForm.Add(new StringContent("shipping-label"), "kind");
        var lateLabel = await client.PostAsync($"/api/live-stream-bid-products/show-attachments/shows/{imported.Show.Id}/shipment-documents", lateLabelForm);
        lateLabel.EnsureSuccessStatusCode();
        var day = await client.GetFromJsonAsync<DailySummary>($"/api/live-stream-bid-products/sales/by-date?date={imported.Show.Date:yyyy-MM-dd}");
        Assert.NotNull(day);
        var historicalBuyer = Assert.Single(Assert.Single(day.Shows).Customers);
        Assert.Equal(3, historicalBuyer.ShipmentDocuments.Count);
        Assert.Single(Assert.Single(historicalBuyer.Orders).PriceEvidence);
    }

    [Fact]
    public async Task ImportedOrder_AllowsMoreInventoryItemsThanCsvQuantity()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        const string csv = "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed\r\n" +
                           "O-1,alice,Mixed lot,1,2026-09-25T19:00:00Z,\r\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "Mixed Show.csv");
        var response = await client.PostAsync("/api/live-stream-bid-products/show-sessions/import", form);
        response.EnsureSuccessStatusCode();
        var imported = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(imported);
        var order = Assert.Single(Assert.Single(imported.Show.Customers).Orders);
        Assert.Equal(1, order.Quantity);

        var increased = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/show-orders/{order.Id}/quantity", new { quantity = 2 });
        Assert.Equal(HttpStatusCode.NoContent, increased.StatusCode);
        int[] productIds;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var products = new[] { "First inventory item", "Second inventory item" }
                .Select(title => new LiveStreamBidProduct
                {
                    Uuid = Guid.NewGuid().ToString(), Title = title, Quantity = 1,
                    Inventory = 1, ActualInventory = 1, UnitPrice = 3
                }).ToArray();
            db.LiveStreamBidProducts.AddRange(products);
            await db.SaveChangesAsync();
            productIds = products.Select(item => item.Id).ToArray();
        }
        foreach (var productId in productIds)
        {
            var scanned = await client.PostAsJsonAsync($"/api/live-stream-bid-products/{productId}/confirm-sale",
                new { actualSalePrice = (decimal?)null, showSessionId = imported.Show.Id, showOrderId = order.Id, quantity = 1 });
            scanned.EnsureSuccessStatusCode();
        }

        var tooLow = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/show-orders/{order.Id}/quantity", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        var settled = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{imported.Show.Id}/stop", new { });
        settled.EnsureSuccessStatusCode();
        var settledSummary = await settled.Content.ReadFromJsonAsync<ShowSummary>();
        Assert.NotNull(settledSummary);
        Assert.Equal(2, settledSummary.OrderedQuantity);
        Assert.Equal(2, settledSummary.RecordedCostQuantity);
        var afterSettlement = await client.PatchAsJsonAsync($"/api/live-stream-bid-products/show-orders/{order.Id}/quantity", new { quantity = 3 });
        Assert.Equal(HttpStatusCode.Conflict, afterSettlement.StatusCode);
    }

    [Fact]
    public async Task ImportedShow_GroupsCustomersAndRequiresExactCostQuantityBeforeSettlement()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        const string csv = "order_id,buyer_username,product_name,product_quantity,placed_at,cancelled_or_failed\r\n" +
                           "O-1,alice,Blue widget,2,2026-09-25T19:00:00Z,\r\n" +
                           "O-2,alice,Red gadget,3,2026-09-25T19:02:00Z,\r\n" +
                           "O-3,bob,Cancelled item,1,2026-09-25T19:03:00Z,cancelled\r\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "September Show.csv");
        var importedResponse = await client.PostAsync("/api/live-stream-bid-products/show-sessions/import", form);
        importedResponse.EnsureSuccessStatusCode();
        var imported = await importedResponse.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(imported);
        Assert.Equal(2, imported.ImportedOrders);
        Assert.Equal(1, imported.ImportedCustomers);
        Assert.Equal(1, imported.IgnoredRows);
        Assert.Equal(5, imported.Show.OrderedQuantity);
        var orders = Assert.Single(imported.Show.Customers).Orders;
        Assert.Equal(2, orders.Count);

        var earlyStop = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{imported.Show.Id}/stop", new { });
        Assert.Equal(HttpStatusCode.Conflict, earlyStop.StatusCode);

        var firstOrder = orders.Single(item => item.ExternalOrderId == "O-1");
        var overflow = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-orders/{firstOrder.Id}/manual-costs", new { quantity = 3, unitCost = 12.50m });
        Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);
        var manual = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-orders/{firstOrder.Id}/manual-costs", new { quantity = 2, unitCost = 12.50m });
        manual.EnsureSuccessStatusCode();

        int productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new LiveStreamBidProduct
            {
                Uuid = Guid.NewGuid().ToString(), Title = "Scanned product", Quantity = 1,
                Inventory = 3, ActualInventory = 3, UnitPrice = 1
            };
            db.LiveStreamBidProducts.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }
        var secondOrder = orders.Single(item => item.ExternalOrderId == "O-2");
        var noOrder = await client.PostAsJsonAsync($"/api/live-stream-bid-products/{productId}/confirm-sale",
            new { actualSalePrice = (decimal?)null, showSessionId = imported.Show.Id });
        Assert.Equal(HttpStatusCode.Conflict, noOrder.StatusCode);
        var scanned = await client.PostAsJsonAsync($"/api/live-stream-bid-products/{productId}/confirm-sale",
            new { actualSalePrice = (decimal?)null, showSessionId = imported.Show.Id, showOrderId = secondOrder.Id, quantity = 3 });
        scanned.EnsureSuccessStatusCode();
        var overAllocated = await client.PostAsJsonAsync($"/api/live-stream-bid-products/{productId}/confirm-sale",
            new { actualSalePrice = (decimal?)null, showSessionId = imported.Show.Id, showOrderId = secondOrder.Id, quantity = 1 });
        Assert.Equal(HttpStatusCode.Conflict, overAllocated.StatusCode);

        var settledResponse = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{imported.Show.Id}/stop", new { });
        settledResponse.EnsureSuccessStatusCode();
        var settled = await settledResponse.Content.ReadFromJsonAsync<ShowSummary>();
        Assert.NotNull(settled);
        Assert.Equal(5, settled.RecordedCostQuantity);
        Assert.Equal(0, settled.RemainingCostQuantity);
        Assert.Equal(28m, settled.TotalCostRmb);
        Assert.All(settled.Customers.SelectMany(item => item.Orders), order => Assert.Equal(0, order.RemainingQuantity));
    }

    private sealed record ImportResult(ShowSummary Show, int ImportedOrders, int ImportedCustomers, int IgnoredRows);
    private sealed record ShowSummary(int Id, DateOnly Date, int OrderedQuantity, int RecordedCostQuantity, int RemainingCostQuantity,
        decimal TotalCostRmb, IReadOnlyList<CustomerSummary> Customers);
    private sealed record DailySummary(IReadOnlyList<ShowSummary> Shows);
    private sealed record CustomerSummary(string Name, IReadOnlyList<OrderSummary> Orders, IReadOnlyList<AttachmentSummary> ShipmentDocuments);
    private sealed record OrderSummary(int Id, string ExternalOrderId, int Quantity, int RemainingQuantity, decimal TotalCostRmb, IReadOnlyList<AttachmentSummary> PriceEvidence);
    private sealed record AttachmentSummary(int Id, string Kind, string OriginalFileName);
    private sealed record IdResult(int Id);
}
