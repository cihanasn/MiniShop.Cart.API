namespace MiniShop.Cart.API.Dtos;

public sealed record ProductDto
(
    string Name,
    string Description,
    decimal Price,
    int Stock
);
