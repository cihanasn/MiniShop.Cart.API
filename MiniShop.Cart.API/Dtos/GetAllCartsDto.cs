namespace MiniShop.Cart.API.Dtos;

public record GetAllCartsDto(Guid Id, Guid UserId, ICollection<GetCartItemDto> CartItems);

public record GetCartItemDto(Guid Id, Guid ProductId, string ProductName, decimal Price, int Quantity);
