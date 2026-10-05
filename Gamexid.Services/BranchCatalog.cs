using Gamexid.Models;
using Microsoft.EntityFrameworkCore;
namespace Gamexid.Services;
public static class BranchCatalog
{
    public static async Task SeedAsync(GamexidDbContext db)
    {
        var codes = await db.Branches.Select(b => b.Code).ToListAsync();
        var official = new Branch[] {
            new() { Code = "DEP-CENTRAL", Name = "Morón (Depósito Central)", Address = "Av. Rivadavia 17939, Morón, PBA" },
            new() { Code = "GX-UNICENTER", Name = "Unicenter Shopping", Address = "Paraná 3745, Martínez, PBA" },
            new() { Code = "GX-LAPLATA", Name = "La Plata", Address = "Cno. Gral. Manuel Belgrano 1550 (Local 12), Gonnet, PBA" },
            new() { Code = "GX-SANMIGUEL", Name = "San Miguel", Address = "Paunero 1373, San Miguel, PBA" },
            new() { Code = "GX-SANISIDRO", Name = "San Isidro", Address = "Av. Centenario 239, San Isidro, PBA" },
            new() { Code = "GX-MERLO", Name = "Merlo", Address = "Suipacha 509, Merlo, PBA" },
            new() { Code = "GX-CATAN1", Name = "González Catán I", Address = "Av. José Equiza 4247, González Catán, PBA" },
            new() { Code = "GX-CATAN2", Name = "González Catán II", Address = "Dr. Enrique Simón Pérez 4429, González Catán, PBA" },
            new() { Code = "GX-BELGRANO", Name = "Belgrano", Address = "José Hernández 2438, CABA" },
            new() { Code = "GX-ABASTO", Name = "Abasto Shopping", Address = "Av. Corrientes 3247 (Nivel 1 - Local 2045), CABA" },
            new() { Code = "GX-PALERMO", Name = "Palermo", Address = "Serrano 1406, CABA" },
            new() { Code = "GX-CABALLITO", Name = "Caballito", Address = "Av. Rivadavia 5040 (Local 14, Galería Vía Cavour), CABA" },
            new() { Code = "GX-LINIERS", Name = "Liniers", Address = "Av. Rivadavia 11063, CABA" },
            new() { Code = "GX-MARDELPLATA", Name = "Mar del Plata", Address = "San Martín 3160, Mar del Plata, PBA" },
            new() { Code = "GX-MENDOZA", Name = "Mendoza", Address = "Lavalle 334, Mendoza" },
            new() { Code = "GX-CORRIENTES", Name = "Corrientes", Address = "Av. Raúl Alfonsín 3525 (Local 108, Shopping Centenario), Corrientes" }
        };
        if (codes.Count == 0)
            for (var index = 0; index < official.Length; index++) official[index].Id = index + 2;
        db.Branches.AddRange(official.Where(b => !codes.Contains(b.Code, StringComparer.OrdinalIgnoreCase)));
        await db.SaveChangesAsync();
    }
}
