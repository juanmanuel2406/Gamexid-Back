namespace Gamexid.Services;

public static class ProductMatching
{
    public static double Score(string query, string candidate)
    {
        query = query.Trim().ToUpperInvariant();
        candidate = candidate.Trim().ToUpperInvariant();
        if (query.Length < 3 || candidate.Length == 0) return 0;
        if (query == candidate) return 1;
        if (query.Length >= 4 && candidate.Contains(query)) return .9;
        if (Math.Abs(query.Length - candidate.Length) > 2) return 0;
        var distancia = CalcularDistancia(query, candidate);
        var erroresPermitidos = 1;
        if (query.Length >= 8)
        {
            erroresPermitidos = 2;
        }
        if (distancia > erroresPermitidos)
        {
            return 0;
        }
        return 1 - (double)distancia / Math.Max(query.Length, candidate.Length);
    }

    private static int CalcularDistancia(string busqueda, string candidato)
    {
        var filaAnterior = new int[candidato.Length + 1];
        for (var columna = 0; columna <= candidato.Length; columna++)
        {
            filaAnterior[columna] = columna;
        }
        for (var fila = 1; fila <= busqueda.Length; fila++)
        {
            var filaActual = new int[candidato.Length + 1];
            filaActual[0] = fila;
            for (var columna = 1; columna <= candidato.Length; columna++)
            {
                var costoReemplazo = 1;
                if (busqueda[fila - 1] == candidato[columna - 1])
                {
                    costoReemplazo = 0;
                }
                var insertar = filaActual[columna - 1] + 1;
                var eliminar = filaAnterior[columna] + 1;
                var reemplazar = filaAnterior[columna - 1] + costoReemplazo;
                filaActual[columna] = Math.Min(Math.Min(insertar, eliminar), reemplazar);
            }
            filaAnterior = filaActual;
        }
        return filaAnterior[candidato.Length];
    }
}
