using System.Text.RegularExpressions;

namespace Gamexid.Api.PcBuilder;

public record PcCoolingInfo(string Status, string Detail);

public static class PcCooling
{
    public static PcCoolingInfo Read(GamingCityProduct product)
    {
        var name = product.Name.ToUpperInvariant();
        var codes = (product.SupplierCode + " " + product.Sku).ToUpperInvariant();
        bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        const string negative = @"\b(?:SIN|NO INCLUYE|WITHOUT)\s+(?:COOLER|DISIPADOR|REFRIGERACI[ÓO]N)\b";
        const string positive = @"\b(?:CON\s+(?:COOLER|DISIPADOR)|(?:COOLER|DISIPADOR)\s+INCLUIDO|INCLUYE\s+(?:COOLER|DISIPADOR))\b";
        if (Has(name, negative)) return new("excluded", "Esta presentación se vende sin cooler. Elegí una refrigeración aparte.");
        if (Has(name, positive)) return new("included", "El catálogo confirma que esta presentación incluye cooler.");
        if (Has(name + " " + codes, @"\b(?:BULK|TRAY|OEM)\b|\b[\w-]+WOF\b"))
            return new("excluded", "Presentación bulk, tray, OEM o WOF sin cooler incluido confirmado.");
        var description = product.Description.ToUpperInvariant();
        if (Has(description, negative)) return new("excluded", "La ficha indica que no incluye cooler.");
        if (Has(description, positive)) return new("included", "La ficha del catálogo indica que incluye cooler.");
        if (name.Contains("INTEL") && name.Contains("CORE")) {
            if (Has(name, @"\b\d{3,5}(?:KF|KS|K|XE|X)\b"))
                return new("excluded", "Esta variante Intel se entrega sin cooler de fábrica.");
            if (Has(name + " " + codes, @"\bBOX(?:ED)?\b|\bBX\d+[A-Z0-9]*\b"))
                return new("included", "Presentación Intel Core en caja identificada; incluye cooler de fábrica.");
        }
        if (name.Contains("AMD") && Has(name + " " + codes, @"\bBOX(?:ED)?\b|\b100-[0-9]+BOX\b") &&
            Has(name, @"\bRYZEN\s+[357]\s+(?:9600|9500F|8700G|8700F|8600G|8500G|8400F|8300G|7700|7600|7500F|7400F?|5700G?|5700X|5600GT|5600XT|5600[GXTF]?|5500GT|5500)\b"))
            return new("included", "Versión AMD en caja identificada y listada con refrigeración incluida.");
        return new("unknown", "La ficha no confirma si esta unidad incluye cooler. Revisá su presentación antes de elegir esta opción.");
    }

    public static PcCheck[] Check(PcSelection[] items, PcLine[] lines)
    {
        var cpu = items.SingleOrDefault(i => i.Slot == "cpu" && i.UseIncludedCooler);
        if (cpu == null) return [];
        if (items.Any(i => i.Slot == "cooler"))
            return [new("incompatible", "Refrigeración", "Elegí el cooler incluido o uno adicional, no ambos.")];
        var info = Read(lines.Single(l => l.Slot == "cpu").Product);
        if (info.Status == "excluded" || info.Status == "unknown" && !cpu.CoolerConfirmed)
            return [new("incompatible", "Refrigeración", info.Detail)];
        return [new(info.Status == "included" ? "checked" : "pending", "Refrigeración",
            info.Status == "included" ? "Se utiliza el cooler incluido con el procesador, sin costo adicional."
                : "Cooler incluido confirmado por el vendedor; la ficha no lo verifica. Sin costo adicional.")];
    }
}
