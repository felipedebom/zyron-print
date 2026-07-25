using System.Text;
using System.Text.Json;
using Zyron.Print.Printing;
using Zyron.Print.Services;
using Xunit;

namespace Zyron.Print.Tests;

public sealed class EscPosReceiptBuilderTests
{
    static EscPosReceiptBuilderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 48)]
    [InlineData(99, 32)]
    public void Columns_UsesSupportedPaperWidths(int width, int expected) =>
        Assert.Equal(expected, EscPosReceiptBuilder.Columns(width));

    [Fact]
    public void BuildTest_EncodesPortugueseInCp850AndAddsCut()
    {
        var result = EscPosReceiptBuilder.BuildTest("Loja Coração", 58, true);
        var text = Encoding.GetEncoding(850).GetString(result);

        Assert.Contains("Coração", text);
        Assert.True(result.TakeLast(3).SequenceEqual(new byte[] { 0x1D, 0x56, 0x00 }));
    }

    [Fact]
    public void BuildFromPayload_PrintsCoreOrderData()
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Lanchonete São João",
          "orderNumber": 123,
          "customerName": "José",
          "items": [{ "quantity": 2, "name": "X-Burger", "total": 49.80 }],
          "subtotal": 49.80,
          "deliveryFee": 5.00,
          "total": 54.80
        }
        """);

        var result = EscPosReceiptBuilder.BuildFromPayload(json.RootElement, 58, false);
        var text = Encoding.GetEncoding(850).GetString(result);

        Assert.Contains("PEDIDO #123", text);
        Assert.Contains("José", text);
        Assert.Contains("2x X-BURGER", text);
        Assert.False(result.TakeLast(3).SequenceEqual(new byte[] { 0x1D, 0x56, 0x00 }));
    }

    [Fact]
    public void BuildFromPayload_HonorsReceiptOptions()
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Brasa 47 Burger",
          "orderNumber": "000123",
          "dateTime": "24/07/2026 19:42",
          "customerName": "Mariana",
          "customerPhone": "(11) 99999-8877",
          "address": { "street": "Rua das Flores, 245" },
          "items": [{
            "quantity": 1, "name": "X-Burger", "details": "+ Bacon",
            "note": "Bem passado", "unitPrice": 24.90, "total": 24.90
          }],
          "subtotal": 24.90,
          "discountAmount": 5,
          "deliveryFee": 6,
          "total": 25.90,
          "payment": { "method": "Dinheiro" },
          "receiptOptions": {
            "orderInfo": false, "customerPhone": false, "deliveryAddress": false,
            "itemDetails": false, "itemNotes": false, "unitPrices": false,
            "discount": false, "deliveryFee": false, "payment": false
          }
        }
        """);

        var text = Encoding.GetEncoding(850).GetString(
            EscPosReceiptBuilder.BuildFromPayload(json.RootElement, 58, false));

        Assert.Contains("PEDIDO #000123", text);
        Assert.Contains("1x X-BURGER", text);
        Assert.Contains("TOTAL", text);
        Assert.DoesNotContain("24/07/2026", text);
        Assert.DoesNotContain("(11) 99999-8877", text);
        Assert.DoesNotContain("Rua das Flores", text);
        Assert.DoesNotContain("+ Bacon", text);
        Assert.DoesNotContain("Bem passado", text);
        Assert.DoesNotContain("Unitário", text);
        Assert.DoesNotContain("Desconto", text);
        Assert.DoesNotContain("Taxa de entrega", text);
        Assert.DoesNotContain("PAGAMENTO", text);
    }

    [Theory]
    [InlineData(" ab-cd 12 ", "ABCD12")]
    [InlineData("zyr.123", "ZYR123")]
    public void PairingCode_IsNormalized(string input, string expected) =>
        Assert.Equal(expected, SupabaseDeviceClient.NormalizePairingCode(input));
}
