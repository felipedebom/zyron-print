using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zyron.Print.Printing;

public static class EscPosReceiptBuilder
{
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] CodePage850 = [0x1B, 0x74, 0x02];
    private static readonly byte[] Center = [0x1B, 0x61, 0x01];
    private static readonly byte[] Left = [0x1B, 0x61, 0x00];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x00];

    static EscPosReceiptBuilder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static byte[] BuildTest(string storeName, int paperWidth, bool cut)
    {
        var columns = Columns(paperWidth);
        using var stream = new MemoryStream();
        Write(stream, Initialize);
        Write(stream, CodePage850);
        Write(stream, Center);
        WriteText(stream, $"{Clean(storeName)}\n");
        Write(stream, BoldOn);
        WriteText(stream, "ZYRON PRINT\n");
        Write(stream, BoldOff);
        WriteText(stream, "IMPRESSÃO DE TESTE\n");
        Write(stream, Left);
        WriteText(stream, $"{new string('-', columns)}\n");
        WriteText(stream, "Acentos: á é í ó ú ã õ ç\n");
        WriteText(stream, "Impressora configurada com sucesso.\n");
        WriteText(stream, Align("Papel", $"{paperWidth} mm", columns));
        WriteText(stream, $"{new string('-', columns)}\n\n\n");
        if (cut) Write(stream, Cut);
        return stream.ToArray();
    }

    public static byte[] BuildFromPayload(JsonElement payload, int paperWidth, bool cut)
    {
        var columns = Columns(paperWidth);
        using var stream = new MemoryStream();
        Write(stream, Initialize);
        Write(stream, CodePage850);
        Write(stream, Center);
        WriteText(stream, $"{Text(payload, "storeName", "ZYRON Delivery")}\n");
        Write(stream, BoldOn);
        WriteText(stream, $"PEDIDO #{Text(payload, "orderNumber", "SEM NÚMERO")}\n");
        Write(stream, BoldOff);
        Write(stream, Left);
        Separator(stream, columns);

        Write(stream, BoldOn);
        WriteText(stream, "CLIENTE E ENTREGA\n");
        Write(stream, BoldOff);
        WriteText(stream, $"{Text(payload, "customerName", "Cliente")}\n");
        OptionalLine(stream, payload, "customerPhone", "Telefone: ");

        if (payload.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
        {
            var street = Text(address, "street");
            var number = Text(address, "number");
            if (!string.IsNullOrWhiteSpace(street)) WriteText(stream, $"{street}, {number}\n");
            OptionalLine(stream, address, "neighborhood", "Bairro: ");
            OptionalLine(stream, address, "complement", "Compl.: ");
            OptionalLine(stream, address, "reference", "Ref.: ");
        }

        Separator(stream, columns);
        Write(stream, BoldOn);
        WriteText(stream, "ITENS\n");
        Write(stream, BoldOff);
        if (payload.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var quantity = Number(item, "quantity", 1);
                WriteText(stream, $"{quantity:0.##}x {Text(item, "name", "ITEM").ToUpperInvariant()}\n");
                OptionalLine(stream, item, "note", "   OBS: ");
                var total = MoneyValue(item, "total", MoneyValue(item, "totalAmount", 0));
                if (total == 0)
                {
                    total = MoneyValue(item, "unitTotal", MoneyValue(item, "unitPrice", 0)) * quantity;
                }
                WriteText(stream, Align("   Total item:", Money(total), columns));
            }
        }

        Separator(stream, columns);
        WriteText(stream, Align("Subtotal", Money(MoneyValue(payload, "subtotal", 0)), columns));
        var discount = MoneyValue(payload, "discountAmount", 0);
        if (discount > 0) WriteText(stream, Align("Desconto", $"- {Money(discount)}", columns));
        var delivery = MoneyValue(payload, "deliveryFee", 0);
        if (delivery > 0) WriteText(stream, Align("Taxa de entrega", Money(delivery), columns));
        Write(stream, BoldOn);
        WriteText(stream, Align("TOTAL", Money(MoneyValue(payload, "total", 0)), columns));
        Write(stream, BoldOff);
        Separator(stream, columns);
        OptionalLine(stream, payload, "orderNotes", "Obs. geral: ");
        WriteText(stream, "\n\n\n");
        if (cut) Write(stream, Cut);
        return stream.ToArray();
    }

    public static int Columns(int paperWidth) => paperWidth == 80 ? 48 : 32;

    private static void OptionalLine(Stream stream, JsonElement element, string property, string prefix)
    {
        var value = Text(element, property);
        if (!string.IsNullOrWhiteSpace(value)) WriteText(stream, $"{prefix}{value}\n");
    }

    private static string Text(JsonElement element, string property, string fallback = "")
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return Clean(value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString());
    }

    private static decimal Number(JsonElement element, string property, decimal fallback)
    {
        if (!element.TryGetProperty(property, out var value)) return fallback;
        return value.TryGetDecimal(out var number) ? number : fallback;
    }

    private static decimal MoneyValue(JsonElement element, string property, decimal fallback)
    {
        var value = Number(element, property, fallback);
        return value;
    }

    private static string Money(decimal value) =>
        value.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    private static string Align(string label, string value, int columns)
    {
        label = Clean(label);
        value = Clean(value);
        var spaces = Math.Max(1, columns - label.Length - value.Length);
        return $"{label}{new string(' ', spaces)}{value}\n";
    }

    private static string Clean(string? value) =>
        new((value ?? "").Where(character => !char.IsControl(character)).ToArray());

    private static void Separator(Stream stream, int columns) =>
        WriteText(stream, $"{new string('-', columns)}\n");

    private static void WriteText(Stream stream, string text) =>
        Write(stream, Encoding.GetEncoding(850, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetBytes(text));

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}

