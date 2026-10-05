using Gamexid.Models;

namespace Gamexid.Services;

public record ReceiptLine(int ProductId, int Quantity, string[] Serials, bool RequiresSerialNumber);
public record ReceiptRequest(int DestinationBranchId, string? Notes, ReceiptLine[] Items);

public static class ReceiptValidation
{
    public static string? Validate(ReceiptRequest request, IReadOnlyList<Product> products, IEnumerable<string> existingSerials)
    {
        if (request.Items is null || request.Items.Length is < 1 or > 100 || request.Items.Any(i => i is null) || request.Notes?.Length > 1000)
            return "Agregá entre 1 y 100 productos. Las notas admiten hasta 1000 caracteres.";
        if (request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Length)
            return "Unificá los productos repetidos en una sola línea.";
        var serials = new HashSet<string>(existingSerials.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items)
        {
            var product = products.SingleOrDefault(p => p.Id == item.ProductId && p.IsActive);
            if (product is null || item.Quantity is < 1 or > 10000 || item.RequiresSerialNumber != product.RequiresSerialNumber)
                return "Revisá producto, cantidad y requerimiento de seriales.";
            if (item.Serials is null || (product.RequiresSerialNumber ? item.Serials.Length != item.Quantity : item.Serials.Length != 0))
                return "La cantidad de seriales debe coincidir con las unidades del producto.";
            foreach (var serial in item.Serials)
            {
                if (string.IsNullOrWhiteSpace(serial) || serial.Trim().Length > 100 || serial.Any(char.IsControl) || !serials.Add(serial.Trim()))
                    return "Hay seriales vacíos, repetidos o ya registrados en el inventario.";
            }
        }
        return null;
    }
}
