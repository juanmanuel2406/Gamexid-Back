using System.ComponentModel.DataAnnotations;

namespace Gamexid.Models;

public enum MovementType { Receipt = 1, Transfer, Sale, Return, Adjustment }
public enum UserRole { Administrator = 1, Manager, Operator }
public class Branch { public int Id { get; set; } [MaxLength(20)] public required string Code { get; set; } [MaxLength(120)] public required string Name { get; set; } [MaxLength(200)] public string? Address { get; set; } public bool IsActive { get; set; } = true; }
public class Product { public int Id { get; set; } [MaxLength(40)] public required string Sku { get; set; } [MaxLength(180)] public required string Name { get; set; } [MaxLength(20)] public required string Ean { get; set; } public bool RequiresSerialNumber { get; set; } public bool IsActive { get; set; } = true; }
public class SerializedUnit { public int Id { get; set; } public int ProductId { get; set; } [MaxLength(100)] public required string SerialNumber { get; set; } public int? CurrentBranchId { get; set; } [MaxLength(30)] public string Status { get; set; } = "Available"; }
public class InventoryMovement { public int Id { get; set; } public MovementType Type { get; set; } public int? SourceBranchId { get; set; } public int? DestinationBranchId { get; set; } public int? RegisteredByUserId { get; set; } public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow; public string? Notes { get; set; } public ICollection<InventoryMovementItem> Items { get; set; } = new List<InventoryMovementItem>(); }
public class InventoryMovementItem { public int Id { get; set; } public int InventoryMovementId { get; set; } public int ProductId { get; set; } public int? SerializedUnitId { get; set; } public int Quantity { get; set; } = 1; }
public class User { public int Id { get; set; } [MaxLength(120)] public required string FullName { get; set; } [MaxLength(160)] public required string Email { get; set; } [MaxLength(100)] public required string PasswordHash { get; set; } public UserRole Role { get; set; } = UserRole.Operator; public int? BranchId { get; set; } public bool IsActive { get; set; } = true; }
