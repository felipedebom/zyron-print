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
        if (Option(payload, "orderInfo"))
        {
            OptionalLine(stream, payload, "dateTime", "Data: ");
            OptionalLine(stream, payload, "source", "Origem: ");
            OptionalLine(stream, payload, "fulfillmentType", "Tipo: ");
            OptionalLine(stream, payload, "estimate", "Previsão: ");
        }
        Write(stream, Left);
        Separator(stream, columns);

        Write(stream, BoldOn);
        WriteText(stream, "CLIENTE E ENTREGA\n");
        Write(stream, BoldOff);
        WriteText(stream, $"{Text(payload, "customerName", "Cliente")}\n");
        if (Option(payload, "customerPhone"))
            OptionalLine(stream, payload, "customerPhone", "Telefone: ");

        if (Option(payload, "deliveryAddress")
            && payload.TryGetProperty("address", out var address)
            && address.ValueKind == JsonValueKind.Object)
        {
            var street = Text(address, "street");
            var number = Text(address, "number");
            if (!string.IsNullOrWhiteSpace(street))
                WriteText(stream, string.IsNullOrWhiteSpace(number) ? $"{street}\n" : $"{street}, {number}\n");
            OptionalLine(stream, address, "neighborhood", "Bairro: ");
            OptionalLine(stream, address, "complement", "Compl.: ");
            if (Option(payload, "deliveryReference"))
                OptionalLine(stream, address, "reference", "Ref.: ");
        }
        if (Option(payload, "deliveryNote"))
            OptionalLine(stream, payload, "deliveryNote", "Obs.: ");

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
                if (Option(payload, "itemDetails"))
                    WriteMultiline(stream, Text(item, "details"), "   ");
                if (Option(payload, "itemNotes"))
                    OptionalLine(stream, item, "note", "   OBS: ");
                if (Option(payload, "unitPrices") && item.TryGetProperty("unitPrice", out var unitPrice)
                    && unitPrice.ValueKind != JsonValueKind.Null)
                    WriteText(stream, Align("   Unitário:", Money(MoneyValue(item, "unitPrice", 0)), columns));
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
        if (Option(payload, "discount") && discount > 0)
        {
            var coupon = Text(payload, "coupon");
            WriteText(stream, Align(string.IsNullOrWhiteSpace(coupon) ? "Desconto" : $"Cupom {coupon}", $"- {Money(discount)}", columns));
        }
        var delivery = MoneyValue(payload, "deliveryFee", 0);
        if (Option(payload, "deliveryFee") && delivery > 0)
            WriteText(stream, Align("Taxa de entrega", Money(delivery), columns));
        Write(stream, BoldOn);
        WriteText(stream, Align("TOTAL", Money(MoneyValue(payload, "total", 0)), columns));
        Write(stream, BoldOff);
        Separator(stream, columns);
        if (Option(payload, "payment")
            && payload.TryGetProperty("payment", out var payment)
            && payment.ValueKind == JsonValueKind.Object)
        {
            Write(stream, BoldOn);
            WriteText(stream, "PAGAMENTO\n");
            Write(stream, BoldOff);
            OptionalLine(stream, payment, "method", "");
            OptionalLine(stream, payment, "status", "");
            OptionalLine(stream, payment, "received", "Valor recebido: ");
            OptionalLine(stream, payment, "change", "Troco: ");
            Separator(stream, columns);
        }
        if (Option(payload, "driver"))
            OptionalLine(stream, payload, "driver", "Entregador: ");
        if (Option(payload, "operationalNote"))
            OptionalLine(stream, payload, "orderNotes", "Obs. geral: ");
        if (Option(payload, "orderId"))
            OptionalLine(stream, payload, "printId", "ID: ");
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

    private static bool Option(JsonElement payload, string property, bool fallback = true)
    {
        if (!payload.TryGetProperty("receiptOptions", out var options)
            || options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty(property, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return fallback;
        return value.GetBoolean();
    }

    private static void WriteMultiline(Stream stream, string value, string prefix)
    {
        foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            WriteText(stream, $"{prefix}{line}\n");
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
