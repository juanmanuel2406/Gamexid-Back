using System.ComponentModel.DataAnnotations;

namespace Gamexid.Models;

public class Order
{
    public Guid Id { get; set; }
    [MaxLength(160)] public string Supplier { get; set; } = "";
    [MaxLength(100)] public string Reference { get; set; } = "";
    public int BranchId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime Created { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(255)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string RequestHash { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<OrderItem> Lines { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    [MaxLength(20)] public string Ean { get; set; } = "";
    [MaxLength(40)] public string Sku { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";
    public int Expected { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Total { get; set; }
    [MaxLength(3)] public string? Currency { get; set; }
    public ICollection<SerialNumber> Serials { get; set; } = new List<SerialNumber>();
}

public class SerialNumber
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public int SerializedUnitId { get; set; }
    [MaxLength(100)] public string Serial { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Received";
    public DateTime ScannedDate { get; set; } = DateTime.UtcNow;
}

public record CreateOrderDto(Guid Id, string Supplier, string Reference, int BranchId, DateTime Created,
    string? FileName, CreateOrderLineDto[] Lines);
public record CreateOrderLineDto(string Ean, string? Sku, string Name, int Expected,
    decimal? UnitPrice, decimal? Total, string? Currency);
public record SaveSerialDto(int OrderItemId, string Serial);
public record SerialResponseDto(int Id, string Serial, string Status, DateTime ScannedDate);
public record OrderLineResponseDto(int Id, int ProductId, string Ean, string Sku, string Name, int Expected,
    int Received, decimal? UnitPrice, decimal? Total, string? Currency, SerialResponseDto[] Serials);
public record OrderResponseDto(Guid Id, string Supplier, string Reference, int BranchId, DateTime Created,
    string FileName, string Status, OrderLineResponseDto[] Lines);
public record OrderPageDto(OrderResponseDto[] Items, int Total, int Page, int PageSize);
