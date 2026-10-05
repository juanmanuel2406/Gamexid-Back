using Gamexid.Models;
using Gamexid.Services;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Services;

public static class AdminProvisioning
{
    public static async Task CreateAsync(GamexidDbContext db)
    {
        if (await db.Users.AnyAsync()) throw new InvalidOperationException("Ya existen usuarios. No se sobrescriben cuentas.");
        Console.Write("Correo del administrador: ");
        var email = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        Console.Write("Nombre: ");
        var name = (Console.ReadLine() ?? "").Trim();
        Console.Write("Contraseña (entrada oculta): ");
        var password = "";
        if (Console.IsInputRedirected) password = Console.ReadLine() ?? "";
        else
        {
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
                if (key.Key == ConsoleKey.Backspace) password = password.Length > 0 ? password[..^1] : "";
                else if (!char.IsControl(key.KeyChar)) password += key.KeyChar;
            Console.WriteLine();
        }
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) ||
            email.Length > 160 || name.Length is < 1 or > 120 || password.Length is < 8 or > 72)
            throw new InvalidOperationException("Revisá correo, nombre y contraseña (8 a 72 caracteres).");
        db.Users.Add(new User { Email = email, FullName = name, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), Role = UserRole.Administrator });
        await db.SaveChangesAsync();
        Console.WriteLine("Administrador creado. La contraseña no se guardó en texto plano.");
    }
}
