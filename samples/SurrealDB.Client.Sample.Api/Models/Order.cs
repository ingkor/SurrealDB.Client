namespace SurrealDB.Client.Sample.Api.Models;

public class Order
{
    public string? Id { get; set; }
    public string UserId { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class OrderItem
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}

public class CreateOrderRequest
{
    public string UserId { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}
