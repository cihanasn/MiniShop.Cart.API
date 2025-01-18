using Microsoft.EntityFrameworkCore;

namespace MiniShop.Cart.API.Context;

public sealed class CartDbContext : DbContext
{
    public CartDbContext(DbContextOptions<CartDbContext> options) : base(options) { }

    public DbSet<MiniShop.Cart.API.Models.Cart> Carts { get; set; }
    public DbSet<MiniShop.Cart.API.Models.CartItem> CartItems { get; set; }
}
