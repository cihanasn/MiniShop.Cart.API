namespace MiniShop.Cart.API.Models;

public sealed class Cart
{
    public Cart()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}

public sealed class CartItem
{
    public CartItem()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Cart Cart { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
