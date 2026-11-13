using System.Text.Json.Nodes;

namespace Gamexid.Api.AI;

public static class GeminiGeneration
{
    public static JsonObject Create(IConfiguration config, string model, int maxTokens)
    {
        var generation = new JsonObject { ["temperature"] = 0.1, ["maxOutputTokens"] = maxTokens };
        var level = config["Gemini:ThinkingLevel"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(level)) return generation;
        if (level is not ("minimal" or "low" or "medium" or "high"))
            throw new InvalidOperationException("Gemini:ThinkingLevel debe ser minimal, low, medium o high.");
        if (!model.StartsWith("gemini-3", StringComparison.Ordinal))
            throw new InvalidOperationException("ThinkingLevel requiere un modelo Gemini 3 compatible; dejalo vacío para usar el valor predeterminado del modelo.");
        generation["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = level };
        return generation;
    }
}
