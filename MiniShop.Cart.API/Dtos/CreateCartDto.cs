namespace MiniShop.Cart.API.Dtos;

public sealed record CreateCartDto(Guid UserId, ICollection<CreateCartItemDto> CartItems);

public sealed record CreateCartItemDto(Guid ProductId, int Quantity);
