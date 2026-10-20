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
        var previous = Enumerable.Range(0, candidate.Length + 1).ToArray();
        for (var i = 1; i <= query.Length; i++)
        {
            var current = new int[candidate.Length + 1];
            current[0] = i;
            for (var j = 1; j <= candidate.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (query[i - 1] == candidate[j - 1] ? 0 : 1));
            previous = current;
        }
        var distance = previous[^1];
        return distance <= (query.Length >= 8 ? 2 : 1)
            ? 1 - (double)distance / Math.Max(query.Length, candidate.Length) : 0;
    }
}
