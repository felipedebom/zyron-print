using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;

namespace Zyron.Print.Printing;

public static class EscPosReceiptBuilder
{
    private const string DefaultBrandTagline = "DELIVERY • PEDIDOS • GESTÃO";
    private const string LegacyBrandTagline = "Sistema de delivery";
    private const string DefaultBrandWebsite = "delivery.zyrondigital.com.br";
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] CodePage850 = [0x1B, 0x74, 0x02];
    private static readonly byte[] Center = [0x1B, 0x61, 0x01];
    private static readonly byte[] Left = [0x1B, 0x61, 0x00];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] InverseOn = [0x1D, 0x42, 0x01];
    private static readonly byte[] InverseOff = [0x1D, 0x42, 0x00];
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
        Write(stream, BuildZyronRaster(paperWidth, DefaultBrandTagline, DefaultBrandWebsite));
        Write(stream, Left);
        Separator(stream, columns);
        Write(stream, Center);
        WriteWrapped(stream, Clean(storeName), columns);
        Write(stream, BoldOn);
        WriteText(stream, "IMPRESSÃO DE TESTE\n");
        Write(stream, BoldOff);
        Write(stream, Left);
        WriteText(stream, $"{new string('-', columns)}\n");
        WriteQuantityAndName(stream, "2x", "X-BURGER ESPECIAL", columns);
        WriteInverseFullLine(stream, "- Cebola", columns);
        WriteInverseFullLine(stream, "OBS: Carne bem passada", columns);
        WriteText(stream, Align("Total item:", "R$ 49,80", columns));
        WriteInverseFullLine(stream, "Troco para: R$ 100,00", columns);
        WriteText(stream, "Acentos: á é í ó ú ã õ ç\n");
        WriteText(stream, Align("Papel", $"{paperWidth} mm", columns));
        WriteText(stream, $"{new string('-', columns)}\n\n\n");
        Write(stream, InverseOff);
        if (cut) Write(stream, Cut);
        return stream.ToArray();
    }

    public static byte[] BuildFromPayload(JsonElement payload, int paperWidth, bool cut)
    {
        var columns = Columns(paperWidth);
        using var stream = new MemoryStream();
        Write(stream, Initialize);
        Write(stream, CodePage850);
        if (Option(payload, "zyronBranding", false))
            WriteZyronBranding(stream, payload, paperWidth, columns);
        Write(stream, Center);
        WriteWrapped(stream, Text(payload, "storeName", "ZYRON Delivery"), columns);
        Write(stream, BoldOn);
        WriteWrapped(stream, $"PEDIDO #{Text(payload, "orderNumber", "SEM NÚMERO")}", columns);
        Write(stream, BoldOff);
        if (Option(payload, "orderInfo"))
        {
            OptionalLine(stream, payload, "dateTime", "Data: ", columns);
            OptionalLine(stream, payload, "source", "Origem: ", columns);
            OptionalLine(stream, payload, "fulfillmentType", "Tipo: ", columns);
            OptionalLine(stream, payload, "estimate", "Previsão: ", columns);
        }
        Write(stream, Left);
        Separator(stream, columns);

        Write(stream, BoldOn);
        WriteText(stream, "CLIENTE E ENTREGA\n");
        Write(stream, BoldOff);
        WriteWrapped(stream, Text(payload, "customerName", "Cliente"), columns);
        if (Option(payload, "customerPhone"))
            OptionalLine(stream, payload, "customerPhone", "Telefone: ", columns);

        if (Option(payload, "deliveryAddress")
            && payload.TryGetProperty("address", out var address)
            && address.ValueKind == JsonValueKind.Object)
        {
            var street = Text(address, "street");
            var number = Text(address, "number");
            if (!string.IsNullOrWhiteSpace(street))
                WriteWrapped(stream, string.IsNullOrWhiteSpace(number) ? street : $"{street}, {number}", columns);
            OptionalLine(stream, address, "neighborhood", "Bairro: ", columns);
            OptionalLine(stream, address, "complement", "Compl.: ", columns);
            if (Option(payload, "deliveryReference"))
                OptionalLine(stream, address, "reference", "Ref.: ", columns);
        }
        if (Option(payload, "deliveryNote"))
            OptionalLine(stream, payload, "deliveryNote", "Obs.: ", columns);

        Separator(stream, columns);
        Write(stream, BoldOn);
        WriteText(stream, "ITENS\n");
        Write(stream, BoldOff);
        if (payload.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var quantity = Number(item, "quantity", 1);
                WriteQuantityAndName(
                    stream,
                    $"{quantity:0.##}x",
                    Text(item, "name", "ITEM").ToUpperInvariant(),
                    columns);
                if (Option(payload, "itemDetails"))
                    WriteItemDetails(stream, Text(item, "details"), columns);
                if (Option(payload, "itemNotes"))
                {
                    var note = Text(item, "note");
                    if (!string.IsNullOrWhiteSpace(note))
                        WriteInverseFullLine(stream, $"OBS: {note}", columns);
                }
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
            OptionalLine(stream, payment, "method", "", columns);
            OptionalLine(stream, payment, "status", "", columns);
            OptionalLine(stream, payment, "received", "Valor recebido: ", columns);
            var changeFor = Text(payment, "changeFor");
            if (!string.IsNullOrWhiteSpace(changeFor))
                WriteInverseFullLine(stream, $"Troco para: {changeFor}", columns);
            var change = Text(payment, "change");
            if (!string.IsNullOrWhiteSpace(change))
                WriteInverseFullLine(stream, $"Troco: {change}", columns);
            Separator(stream, columns);
        }
        if (Option(payload, "driver"))
            OptionalLine(stream, payload, "driver", "Entregador: ", columns);
        if (Option(payload, "operationalNote"))
            OptionalLine(stream, payload, "orderNotes", "Obs. geral: ", columns);
        if (Option(payload, "orderId"))
            OptionalLine(stream, payload, "printId", "ID: ", columns);
        Write(stream, InverseOff);
        WriteText(stream, "\n\n\n");
        if (cut) Write(stream, Cut);
        return stream.ToArray();
    }

    public static int Columns(int paperWidth) => paperWidth == 80 ? 48 : 32;

    private static void WriteZyronBranding(Stream stream, JsonElement payload, int paperWidth, int columns)
    {
        var tagline = NormalizeBrandingTagline(BrandingText(payload, "tagline", DefaultBrandTagline));
        var website = BrandingText(payload, "website", DefaultBrandWebsite);
        try
        {
            Write(stream, BuildZyronRaster(paperWidth, tagline, website));
            Write(stream, Left);
            Separator(stream, columns);
        }
        catch
        {
            Write(stream, Center);
            Write(stream, BoldOn);
            WriteText(stream, "ZYRON\n");
            Write(stream, BoldOff);
            WriteWrapped(stream, tagline, columns);
            WriteWrapped(stream, website, columns);
            Write(stream, Left);
            Separator(stream, columns);
        }
    }

    private static string BrandingText(JsonElement payload, string property, string fallback)
    {
        if (payload.TryGetProperty("zyronBranding", out var branding)
            && branding.ValueKind == JsonValueKind.Object)
            return Text(branding, property, fallback);
        return fallback;
    }

    private static string NormalizeBrandingTagline(string tagline) =>
        string.IsNullOrWhiteSpace(tagline)
        || tagline.Equals(LegacyBrandTagline, StringComparison.OrdinalIgnoreCase)
            ? DefaultBrandTagline
            : tagline;

    internal static byte[] BuildZyronRaster(int paperWidth, string tagline, string website)
    {
        _ = tagline;
        _ = website;

        const string resourceName = "Zyron.Print.Assets.zyron-receipt-logo.png";
        using var logoStream = typeof(EscPosReceiptBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso de marca não encontrado: {resourceName}");
        using var logo = new Bitmap(logoStream);

        var width = paperWidth == 80 ? 576 : 384;
        var horizontalMargin = paperWidth == 80 ? 18 : 12;
        var verticalMargin = paperWidth == 80 ? 6 : 4;
        var drawWidth = width - (horizontalMargin * 2);
        var drawHeight = (int)Math.Round(drawWidth * (logo.Height / (float)logo.Width));
        using var bitmap = new Bitmap(width, drawHeight + (verticalMargin * 2));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            logo,
            new Rectangle(horizontalMargin, verticalMargin, drawWidth, drawHeight));

        return ToEscPosRaster(bitmap);
    }

    private static byte[] ToEscPosRaster(Bitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;
        using var stream = new MemoryStream();
        Write(stream,
        [
            0x1D, 0x76, 0x30, 0x00,
            (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
            (byte)(bitmap.Height & 0xFF), (byte)((bitmap.Height >> 8) & 0xFF)
        ]);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var byteIndex = 0; byteIndex < widthBytes; byteIndex++)
            {
                byte value = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = byteIndex * 8 + bit;
                    if (x >= bitmap.Width) continue;
                    var color = bitmap.GetPixel(x, y);
                    var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    if (luminance < 205) value |= (byte)(0x80 >> bit);
                }
                stream.WriteByte(value);
            }
        }
        WriteText(stream, "\n");
        return stream.ToArray();
    }

    private static void OptionalLine(Stream stream, JsonElement element, string property, string prefix, int columns)
    {
        var value = Text(element, property);
        if (!string.IsNullOrWhiteSpace(value)) WriteWrapped(stream, $"{prefix}{value}", columns);
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

    private static void WriteQuantityAndName(Stream stream, string quantity, string name, int columns)
    {
        quantity = Clean(quantity);
        var firstWidth = Math.Max(1, columns - quantity.Length - 1);
        var nameLines = Wrap(name, firstWidth).ToList();
        if (nameLines.Count == 0) nameLines.Add("ITEM");

        Write(stream, InverseOn);
        WriteText(stream, quantity);
        Write(stream, InverseOff);
        WriteText(stream, $" {nameLines[0]}\n");

        var remaining = string.Join(" ", nameLines.Skip(1));
        foreach (var line in Wrap(remaining, columns))
            WriteText(stream, $"{line}\n");
    }

    private static void WriteItemDetails(Stream stream, string value, int columns)
    {
        foreach (var rawLine in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Clean(rawLine).Trim();
            if (IsRemoval(line))
            {
                WriteInverseFullLine(stream, line, columns);
                continue;
            }
            foreach (var wrapped in Wrap(line, Math.Max(1, columns - 3)))
                WriteText(stream, $"   {wrapped}\n");
        }
    }

    private static bool IsRemoval(string value)
    {
        var normalized = value.TrimStart();
        return normalized.StartsWith("-", StringComparison.Ordinal)
               || normalized.StartsWith("SEM ", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteInverseFullLine(Stream stream, string value, int columns)
    {
        foreach (var line in Wrap(value, columns))
        {
            Write(stream, InverseOn);
            WriteText(stream, line.PadRight(columns));
            WriteText(stream, "\n");
            Write(stream, InverseOff);
        }
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        value = Clean(value).Trim();
        if (value.Length == 0) yield break;
        width = Math.Max(1, width);

        var current = "";
        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > width)
            {
                if (current.Length > 0)
                {
                    yield return current;
                    current = "";
                }
                for (var index = 0; index < word.Length; index += width)
                    yield return word.Substring(index, Math.Min(width, word.Length - index));
                continue;
            }

            if (current.Length == 0)
            {
                current = word;
            }
            else if (current.Length + 1 + word.Length <= width)
            {
                current += $" {word}";
            }
            else
            {
                yield return current;
                current = word;
            }
        }
        if (current.Length > 0) yield return current;
    }

    private static string Money(decimal value) =>
        value.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    private static string Align(string label, string value, int columns)
    {
        label = Clean(label);
        value = Clean(value);
        if (value.Length >= columns)
            value = value[^Math.Max(1, columns - 1)..];
        var maximumLabelLength = Math.Max(0, columns - value.Length - 1);
        if (label.Length > maximumLabelLength)
            label = label[..maximumLabelLength];
        var spaces = Math.Max(1, columns - label.Length - value.Length);
        return $"{label}{new string(' ', spaces)}{value}\n";
    }

    private static string Clean(string? value) =>
        new((value ?? "").Where(character => !char.IsControl(character)).ToArray());

    private static void Separator(Stream stream, int columns) =>
        WriteText(stream, $"{new string('-', columns)}\n");

    private static void WriteWrapped(Stream stream, string value, int columns)
    {
        foreach (var line in Wrap(value, columns))
            WriteText(stream, $"{line}\n");
    }

    private static void WriteText(Stream stream, string text) =>
        Write(stream, Encoding.GetEncoding(850, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetBytes(text));

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}
