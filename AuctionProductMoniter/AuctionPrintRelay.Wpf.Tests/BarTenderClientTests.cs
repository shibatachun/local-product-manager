using Xunit;

namespace AuctionPrintRelay.Wpf.Tests;

public sealed class BarTenderClientTests
{
    [Fact]
    public void BuildCsv_PreservesRowOrderAndEscapesValues()
    {
        var csv = BarTenderClient.BuildCsv(
        [
            new PrintRequest { BuyerName = "buyer,one", ProductNumber = "Free Gift #1", SessionId = "show\"A" },
            new PrintRequest { BuyerName = "buyer-two", ProductNumber = "2", SessionId = "show-A" }
        ]);

        Assert.Equal(
            "action,BuyerName,ProductNumber,SessionId\r\n" +
            "print,\"buyer,one\",\"Free Gift #1\",\"show\"\"A\"\r\n" +
            "print,\"buyer-two\",\"2\",\"show-A\"\r\n",
            csv);
    }

    [Fact]
    public async Task SendAsync_RejectsConnectionTestWithoutPrinting()
    {
        var client = new BarTenderClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            "http://127.0.0.1:5164/Integration/auction-product-label/Execute",
            new PrintRequest { BuyerName = "connection_test", ProductNumber = "0", Test = true }));
    }
}
