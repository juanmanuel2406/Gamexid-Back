namespace Gamexid.Services;

public record AiQuestion(string Mensaje, string ContextoPantalla, int SucursalId,
    string[]? PreguntasAnteriores = null, string[]? ProductosAnteriores = null);
public record AiScope(int UserId, int BranchId, string BranchName);
public record AiProductCard(string Id, string Name, string Code, string Description,
    string? ImageUrl, string? WebUrl);
public record AiWebSource(int Numero, string Titulo, string Url);
public record AiWebResearch(string ProductoId, string Nombre, string Texto, AiWebSource[] Fuentes, string SugerenciasHtml);
public record AiAnswer(string Respuesta, AiProductCard[] Productos, string[] Fuentes, DateTime ConsultadoUtc,
    AiWebResearch[]? Investigaciones = null, string[]? Avisos = null);

public interface IGamexidAiService
{
    bool IsConfigured { get; }
    Task<AiAnswer> AskAsync(AiQuestion question, AiScope scope, CancellationToken ct);
}
