using System.Text.RegularExpressions;

namespace Gamexid.Api.PcBuilder;

public static class PcCompatibility
{
    public static readonly string[] Slots = ["cpu", "mother", "ram", "gpu", "storage", "psu", "case", "cooler"];
    public static bool Valid(PcSelection[]? items) => items is { Length: <= 8 } && items.All(i => i != null &&
        Slots.Contains(i.Slot) && (!i.UseIncludedCooler || i.Slot == "cpu") && (!i.CoolerConfirmed || i.UseIncludedCooler) && i.ProductId != null && Regex.IsMatch(i.ProductId, @"^[0-9]{1,20}$") &&
        i.Quantity >= 1 && i.Quantity <= (i.Slot is "ram" or "storage" ? 4 : 1) &&
        (i.OfferId == null || Guid.TryParse(i.OfferId, out _)) && i.ConfirmedCurrency is null or "ARS" or "USD") &&
        items.Select(i => i.Slot).Distinct().Count() == items.Length;

    public static string? Category(string name)
    {
        var value = name.ToUpperInvariant();
        return value.StartsWith("PROCESADOR") || value.StartsWith("CPU ") ? "cpu" :
            value.StartsWith("MOTHER") || value.StartsWith("PLACA MADRE") ? "mother" :
            value.StartsWith("MEMORIA DDR") || value.StartsWith("MEMORIA RAM") || value.StartsWith("RAM ") ? "ram" :
            value.StartsWith("GABINETE") ? "case" : value.StartsWith("FUENTE") ? "psu" :
            value.StartsWith("PLACA DE VIDEO") || value.StartsWith("PLACA VIDEO") ? "gpu" :
            value.StartsWith("DISCO") || value.StartsWith("SSD ") ? "storage" :
            value.StartsWith("COOLER CPU") || value.StartsWith("CPU COOLER") || value.StartsWith("WATER COOLER") ||
            value.StartsWith("REFRIGERACION") ? "cooler" : null;
    }
    public static string SearchTerm(string slot) => slot switch {
        "cpu" => "PROCESADOR", "mother" => "MOTHER", "ram" => "MEMORIA", "storage" => "DISCO",
        "gpu" => "PLACA", "psu" => "FUENTE", "case" => "GABINETE", "cooler" => "COOLER",
        _ => throw new ArgumentException("Paso inválido.")
    };
    public static string[] Prerequisites(string slot) => slot switch {
        "cpu" => [], "mother" => ["cpu"], "ram" => ["cpu", "mother"],
        _ => ["cpu", "mother", "ram"]
    };

    public static PcCheck[] Check(PcLine[] lines)
    {
        var checks = new List<PcCheck>();
        string Text(string slot) {
            var p = lines.FirstOrDefault(l => l.Slot == slot)?.Product;
            return p == null ? "" : (p.Name + " " + p.Description).ToUpperInvariant();
        }
        string? Spec(string text, string pattern) {
            var matches = Regex.Matches(text, pattern).Select(m => m.Value.Replace(" ", "")).Distinct().ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        void Compare(string a, string b, string pattern, string title) {
            if (Text(a) == "" || Text(b) == "") return;
            var first = Spec(Text(a), pattern); var second = Spec(Text(b), pattern);
            checks.Add(first == null || second == null
                ? new("pending", title, "La ficha no informa un valor único. Revisar especificaciones del fabricante.")
                : first != second ? new("incompatible", title, $"No coinciden: {first} y {second}.")
                : new("checked", title, $"Coincide {first} en las fichas. Este control no confirma el resto del armado."));
        }
        foreach (var line in lines) {
            var category = Category(line.Product.Name);
            if (category != null && category != line.Slot)
                checks.Add(new("incompatible", "Tipo de componente", $"{line.Product.Name} no corresponde al espacio {line.Slot}."));
            if (category == null) checks.Add(new("pending", "Tipo de componente", $"Confirmar que {line.Product.Name} corresponde al espacio elegido ({line.Slot})."));
            if (line.Product.Stock is { } stock && stock < line.Quantity)
                checks.Add(new("pending", "Disponibilidad", $"{line.Product.Name}: stock del portal menor a la cantidad solicitada. No hay reserva."));
        }
        foreach (var slot in new[] { "cpu", "mother", "ram", "storage", "psu", "case" })
            if (Text(slot) == "") checks.Add(new("pending", "Componente pendiente", slot switch {
                "cpu" => "Procesador", "mother" => "Motherboard", "ram" => "Memoria RAM", "storage" => "Almacenamiento",
                "psu" => "Fuente", _ => "Gabinete" }));
        Compare("cpu", "mother", @"\b(?:AM[45]|LGA\s?(?:1151|1200|1700|1851))\b", "Socket del procesador y motherboard");
        string? Memory(string slot) {
            var product = lines.FirstOrDefault(l => l.Slot == slot)?.Product;
            if (product == null) return null;
            string? Read(string text) => Spec(Regex.Replace(text.ToUpperInvariant(), @"DDR[\s-]*", "DDR"), @"\bDDR[345](?!\d)");
            var memory = Read(product.Name) ?? Read(product.Description);
            if (slot == "ram") return memory;
            var socket = Spec(Text(slot), @"\bAM[45]\b");
            var platformMemory = socket == "AM4" ? "DDR4" : socket == "AM5" ? "DDR5" : null;
            if (platformMemory != null && memory != null && memory != platformMemory)
                checks.Add(new("incompatible", "Generación de memoria", $"La ficha de {product.Name} contradice su plataforma: {socket} requiere {platformMemory}, no {memory}."));
            return platformMemory ?? memory;
        }
        if (Text("ram") != "") {
            var ram = Memory("ram");
            foreach (var slot in new[] { "mother", "cpu" }) {
                if (Text(slot) == "") continue;
                var required = Memory(slot);
                if (slot == "cpu" && required == null) continue;
                var component = slot == "mother" ? "motherboard" : "procesador";
                checks.Add(required == null || ram == null
                    ? new("pending", "Generación de memoria", $"Falta confirmar la generación de RAM admitida por el {component}. No se confirmó compatibilidad.")
                    : required != ram
                        ? new("incompatible", "Generación de memoria", $"El {component} requiere {required}; la memoria elegida es {ram}. Elegí una memoria {required}.")
                        : new("checked", "Generación de memoria", $"La memoria {ram} coincide con la generación admitida por el {component}."));
            }
        }
        if (Text("cpu") != "" && Text("mother") != "")
            checks.Add(new("pending", "BIOS", "Confirmar el modelo exacto de CPU en la lista de soporte del fabricante y la versión de BIOS instalada."));
        if (Text("ram") != "") checks.Add(new("pending", "Memoria y ranuras", "Revisar DIMM/SO-DIMM, capacidad, cantidad de módulos por kit y ranuras disponibles."));
        if (Text("case") != "") checks.Add(new("pending", "Espacio físico", "Revisar formato de motherboard, largo de GPU, altura del cooler y espacio para la fuente."));
        if (Text("psu") != "") checks.Add(new("pending", "Alimentación", "Verificar consumo del equipo, potencia recomendada y conectores de CPU y GPU; los watts solos no alcanzan."));
        if (Text("storage") != "") checks.Add(new("pending", "Almacenamiento", "Verificar interfaz SATA/NVMe, tamaño y puertos o ranuras libres de la motherboard."));
        checks.Add(new("pending", "Gráficos y refrigeración", Text("gpu") == ""
            ? "No hay GPU seleccionada: confirmar gráficos integrados y salida de video. Revisar si el procesador incluye cooler compatible."
            : "Confirmar refrigeración del CPU y conectores de la placa de video antes de cerrar el presupuesto."));
        return checks.ToArray();
    }
}
