using System.Text.RegularExpressions;
using Gamexid.Services;

namespace Gamexid.Api.AI;

public static class WebResearchResults
{
    public static AiWebResearch Add(List<AiWebResearch> results, AiWebResearch result)
    {
        var offset = results.Sum(r => r.Fuentes.Length);
        var numbered = result with {
            Texto = Regex.Replace(result.Texto, @"\[(\d+)\]", m => int.TryParse(m.Groups[1].Value, out var number) && result.Fuentes.Any(f => f.Numero == number) ? "[" + (number + offset) + "]" : ""),
            Fuentes = result.Fuentes.Select(s => s with { Numero = s.Numero + offset }).ToArray()
        };
        results.Add(numbered);
        return numbered;
    }
    public static AiWebResearch[] Combine(List<AiWebResearch> results)
    {
        if (results.Count <= 1) return results.ToArray();
        return [new("", "Investigación de tecnología",
            string.Join("\n\n", results.Select(r => r.Nombre + "\n" + r.Texto)),
            results.SelectMany(r => r.Fuentes).ToArray(),
            string.Join("", results.Select(r => r.SugerenciasHtml)))];
    }
}
