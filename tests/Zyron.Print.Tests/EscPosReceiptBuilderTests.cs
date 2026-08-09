using System.Text;
using System.Text.Json;
using Zyron.Print.Printing;
using Zyron.Print.Services;
using Xunit;

namespace Zyron.Print.Tests;

public sealed class EscPosReceiptBuilderTests
{
    private static readonly byte[] InverseOn = [0x1D, 0x42, 0x01];
    private static readonly byte[] InverseOff = [0x1D, 0x42, 0x00];

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
        Assert.True(FindRasterCommand(result) >= 0);
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
        Assert.Contains("2x", text);
        Assert.Contains(" X-BURGER", text);
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
        Assert.Contains("1x", text);
        Assert.Contains(" X-BURGER", text);
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
    [InlineData(58, 48, 384)]
    [InlineData(80, 72, 576)]
    public void BuildFromPayload_AddsFullWidthZyronRasterWhenBrandingIsEnabled(
        int paperWidth, int expectedWidthBytes, int expectedPixelWidth)
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Brasa 47 Burger",
          "orderNumber": "123",
          "customerName": "Mariana",
          "items": [],
          "subtotal": 0,
          "total": 0,
          "receiptOptions": { "zyronBranding": true },
          "zyronBranding": {
            "tagline": "DELIVERY • PEDIDOS • GESTÃO",
            "website": "delivery.zyrondigital.com.br"
          }
        }
        """);

        var result = EscPosReceiptBuilder.BuildFromPayload(json.RootElement, paperWidth, false);
        var commandOffset = FindRasterCommand(result);

        Assert.True(commandOffset >= 0);
        Assert.Equal(expectedWidthBytes, result[commandOffset + 4] | result[commandOffset + 5] << 8);
        Assert.Equal(expectedPixelWidth, expectedWidthBytes * 8);
        var height = result[commandOffset + 6] | result[commandOffset + 7] << 8;
        Assert.True(height > 100);
        var raster = result.AsSpan(commandOffset + 8, expectedWidthBytes * height);
        var blackPixels = raster.ToArray().Sum(value => System.Numerics.BitOperations.PopCount(value));
        Assert.InRange(blackPixels, expectedPixelWidth * 3, expectedPixelWidth * height / 2);
    }

    [Fact]
    public void BuildFromPayload_DoesNotAddRasterWhenBrandingIsDisabled()
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Loja",
          "orderNumber": "123",
          "customerName": "Cliente",
          "items": [],
          "subtotal": 0,
          "total": 0,
          "receiptOptions": { "zyronBranding": false }
        }
        """);

        var result = EscPosReceiptBuilder.BuildFromPayload(json.RootElement, 58, false);

        Assert.Equal(-1, FindRasterCommand(result));
    }

    [Fact]
    public void BuildTest_DemonstratesFourBalancedInverseHighlights()
    {
        var result = EscPosReceiptBuilder.BuildTest("ZYRON", 58, true);
        var commands = FindInverseCommands(result);

        Assert.Equal(9, commands.Count);
        Assert.Equal([true, false, true, false, true, false, true, false, false], commands.Select(command => command.Enabled));
        Assert.True(commands[^1].Offset < result.Length - 3);
        Assert.True(result.TakeLast(3).SequenceEqual(new byte[] { 0x1D, 0x56, 0x00 }));
    }

    [Fact]
    public void BuildFromPayload_UsesInverseForQuantityRemovalNoteAndChangeInCorrectOrder()
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Loja",
          "orderNumber": "321",
          "source": "SITE",
          "customerName": "Maria",
          "items": [{
            "quantity": 2,
            "name": "X-Burger Especial",
            "details": "+ Bacon\n- Cebola\nSEM PICLES",
            "note": "Carne bem passada",
            "total": 49.80
          }],
          "subtotal": 49.80,
          "total": 49.80,
          "payment": {
            "method": "Dinheiro",
            "received": 100.00,
            "change": 50.20
          }
        }
        """);

        var result = EscPosReceiptBuilder.BuildFromPayload(json.RootElement, 58, true);
        var commands = FindInverseCommands(result);
        var text = Encoding.GetEncoding(850).GetString(result);

        Assert.Equal(commands.Count(command => command.Enabled), commands.Count(command => !command.Enabled) - 1);
        AssertAlternatesUntilFinalSafetyOff(commands);
        Assert.Contains("2x", TextBetween(result, commands[0].Offset + 3, commands[1].Offset));
        Assert.Contains("- Cebola", text);
        Assert.Contains("SEM PICLES", text);
        Assert.Contains("OBS: Carne bem passada", text);
        Assert.Contains("Origem: ZYRON", text);
        Assert.Contains("Valor recebido: R$ 100,00", text);
        Assert.Contains("Troco: R$ 50,20", text);
        Assert.True(commands[^1].Offset < result.Length - 3);
        Assert.True(result.TakeLast(3).SequenceEqual(new byte[] { 0x1D, 0x56, 0x00 }));
    }

    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 48)]
    public void BuildFromPayload_DoesNotWriteVisibleLinesBeyondPaperWidth(int paperWidth, int columns)
    {
        using var json = JsonDocument.Parse("""
        {
          "storeName": "Loja",
          "orderNumber": "99",
          "customerName": "Cliente",
          "items": [{
            "quantity": 3,
            "name": "Produto com um nome muito comprido que precisa quebrar sem ultrapassar o papel",
            "details": "- Remover cebola tomate picles maionese mostarda e todos os complementos",
            "note": "Observação muito importante e extensa para conferir a quebra segura da linha",
            "total": 10
          }],
          "subtotal": 10,
          "total": 10,
          "payment": { "changeFor": "R$ 100,00" }
        }
        """);

        var bytes = EscPosReceiptBuilder.BuildFromPayload(json.RootElement, paperWidth, false);
        var printableText = RemoveEscPosCommands(bytes);

        Assert.All(
            printableText.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.True(line.Length <= columns, $"Linha de {line.Length} colunas excedeu {columns}: {line}"));
    }

    [Theory]
    [InlineData(" ab-cd 12 ", "ABCD12")]
    [InlineData("zyr.123", "ZYR123")]
    public void PairingCode_IsNormalized(string input, string expected) =>
        Assert.Equal(expected, SupabaseDeviceClient.NormalizePairingCode(input));

    private static List<(int Offset, bool Enabled)> FindInverseCommands(byte[] bytes)
    {
        var commands = new List<(int, bool)>();
        for (var index = 0; index <= bytes.Length - 3; index++)
        {
            if (bytes[index] == 0x1D && bytes[index + 1] == 0x42
                && bytes[index + 2] is 0x00 or 0x01)
                commands.Add((index, bytes[index + 2] == 0x01));
        }
        return commands;
    }

    private static int FindRasterCommand(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - 8; index++)
        {
            if (bytes[index] == 0x1D && bytes[index + 1] == 0x76
                && bytes[index + 2] == 0x30 && bytes[index + 3] == 0x00)
                return index;
        }
        return -1;
    }

    private static void AssertAlternatesUntilFinalSafetyOff(List<(int Offset, bool Enabled)> commands)
    {
        Assert.NotEmpty(commands);
        for (var index = 0; index < commands.Count - 1; index++)
            Assert.Equal(index % 2 == 0, commands[index].Enabled);
        Assert.False(commands[^1].Enabled);
    }

    private static string TextBetween(byte[] bytes, int start, int end) =>
        Encoding.GetEncoding(850).GetString(bytes[start..end]);

    private static string RemoveEscPosCommands(byte[] bytes)
    {
        using var stream = new MemoryStream();
        for (var index = 0; index < bytes.Length;)
        {
            if (index + 2 < bytes.Length
                && ((bytes[index] == 0x1D && bytes[index + 1] is 0x42 or 0x56)
                    || (bytes[index] == 0x1B && bytes[index + 1] is 0x40 or 0x74 or 0x61 or 0x45)))
            {
                index += 3;
                continue;
            }
            stream.WriteByte(bytes[index++]);
        }
        return Encoding.GetEncoding(850).GetString(stream.ToArray());
    }
}
