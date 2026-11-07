using System.Text.RegularExpressions;

namespace Gamexid.Api.AI;

public static class ProductSearchTerms
{
    public static string Normalize(string query)
    {
        var terms = new Dictionary<string, string> {
            ["gabiente|gabinet|gabinetes|gabo|gabos|case|cases"] = "gabinete",
            ["mother|mothers|motherboard|motherboards|placa madre|placas madre"] = "mother",
            ["ram|rams|memorias"] = "memoria",
            ["micro|micros|cpu|procesadores"] = "procesador",
            ["headset|headsets|auriculares|audifonos|audífonos"] = "auricular",
            ["adaptadores"] = "adaptador",
            ["psu|fuentes"] = "fuente",
            ["gpu|gpus|placas de video|tarjeta grafica|tarjeta gráfica"] = "placa de video",
            ["discos"] = "disco"
        };
        foreach (var (pattern, replacement) in terms)
            query = Regex.Replace(query, @"\b(" + pattern + @")\b", replacement, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return Regex.Replace(query.Trim(), @"\s+", " ");
    }
}
