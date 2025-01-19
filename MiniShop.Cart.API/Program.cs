using Microsoft.EntityFrameworkCore;
using MiniShop.Cart.API.Context;
using MiniShop.Cart.API.Dtos;
using MiniShop.Cart.API.Models;
using System;

var builder = WebApplication.CreateBuilder(args);

// Connection string tan�m� (appsettings.json'dan okunabilir)
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                          ?? "Server=localhost,1433;Database=MiniShopCartDb;User Id=sa;Password=StrongPassword123!;Encrypt=True;TrustServerCertificate=True;";

// DbContext ekleniyor
builder.Services.AddDbContext<CartDbContext>(options =>
    options.UseSqlServer(connectionString));

//builder.Services.AddHttpClient("ProductService", client =>
//{
//    client.BaseAddress = new Uri("http://localhost:6001"); // Docker'daki ProductService URL
//});
builder.Services.AddHttpClient("ProductService", client =>
{
    var productServiceBaseAddress = builder.Configuration["ProductService:BaseAddress"];
    client.BaseAddress = new Uri(productServiceBaseAddress!);
});

builder.Services.AddHttpClient("OrderService", client =>
{
    var orderServiceBaseAddress = builder.Configuration["OrderService:BaseAddress"];
    client.BaseAddress = new Uri(orderServiceBaseAddress!);
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/api/carts", async (CartDbContext dbContext, CreateCartDto createCartDto, CancellationToken cancellationToken) =>
{
    // Yeni bir Cart nesnesi olu�turuluyor
    var cart = new Cart
    {
        UserId = createCartDto.UserId,
        CartItems = new List<CartItem>()
    };

    foreach (var cartItemDto in createCartDto.CartItems)
    {
        // CartItem olu�turuluyor
        var cartItem = new CartItem
        {
            ProductId = cartItemDto.ProductId,
            Quantity = cartItemDto.Quantity
        };
        cart.CartItems.Add(cartItem);
    }

    // Cart nesnesi veritaban�na ekleniyor
    await dbContext.Carts.AddAsync(cart, cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/carts/{cart.Id}", cart.Id);
});


app.MapGet("/api/carts", async (CartDbContext dbContext, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var httpClient = httpClientFactory.CreateClient("ProductService");

    var carts = await dbContext.Carts
        .Include(c => c.CartItems)
        .ToListAsync(cancellationToken);

    var result = new List<GetAllCartsDto>();

    foreach (var cart in carts)
    {
        var cartItemsDto = new List<GetCartItemDto>();

        foreach (var cartItem in cart.CartItems)
        {
            try
            {
                var response = await httpClient.GetAsync($"/api/products/{cartItem.ProductId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var product = await response.Content.ReadFromJsonAsync<ProductDto>(cancellationToken: cancellationToken);
                    if (product != null)
                    {
                        cartItemsDto.Add(new GetCartItemDto(cartItem.Id, cartItem.ProductId, product.Name, product.Price, cartItem.Quantity));
                    }
                }
                else
                {
                    // �r�n bulunamazsa, varsay�lan bir yan�t d�nebilirsiniz
                    cartItemsDto.Add(new GetCartItemDto(cartItem.Id, cartItem.ProductId, "Bilinmiyor", 0m, cartItem.Quantity));
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"�r�n bilgisi al�n�rken bir hata olu�tu: {ex.Message}");
            }
        }

        result.Add(new GetAllCartsDto(cart.Id, cart.UserId, cartItemsDto));
    }

    return Results.Ok(result);
});

app.MapGet("/api/carts/create-order", async (CartDbContext dbContext, IHttpClientFactory httpClientFactory, Guid userId,
                                       CancellationToken cancellationToken) =>
{
    // Kullan�c�n�n sepetini al
    var cart = await dbContext.Carts
        .Include(c => c.CartItems)
        .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    if (cart == null || !cart.CartItems.Any())
    {
        return Results.NotFound("Sepet bulunamad� veya sepet bo�.");
    }

    // Order servisi i�in HTTP istemcisi olu�tur
    var httpClient = httpClientFactory.CreateClient("OrderService");
    var productHttpClient = httpClientFactory.CreateClient("ProductService");

    // Sipari� i�in DTO'yu olu�tur
    var orderDtoList = new List<object>
    {
        new
        {
            Items = cart.CartItems.Select(cartItem => new
            {
                ProductId = cartItem.ProductId,
                Quantity = cartItem.Quantity,
                Price = 0 // �r�n fiyat�n� burada belirleyebilirsiniz
            }).ToList()
        }
    };

    // Sipari� servisine istek at
    try
    {
        var response = await httpClient.PostAsJsonAsync("/create-order", orderDtoList, cancellationToken);

        if (response.IsSuccessStatusCode)
        {

            // Stok d��me i�lemi
            foreach (var cartItem in cart.CartItems)
            {
                var stockUpdateDto = new
                {
                    ProductId = cartItem.ProductId,
                    Quantity = cartItem.Quantity
                };

                var stockResponse = await productHttpClient.PostAsJsonAsync("/api/products/update-stock", stockUpdateDto, cancellationToken);

                if (!stockResponse.IsSuccessStatusCode)
                {
                    return Results.Problem($"Stok g�ncellenirken bir hata olu�tu: {stockResponse.ReasonPhrase}");
                }
            }

            // Sipari� ba�ar�l�, sepeti temizle
            dbContext.Carts.Remove(cart);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok("Sipari� olu�turuldu ve sepet temizlendi.");
        }

        return Results.Problem("Sipari� olu�turulamad�: " + response.ReasonPhrase);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Sipari� olu�turulurken bir hata olu�tu: {ex.Message}");
    }
});


// Migration'lar� otomatik olarak uygula
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CartDbContext>();
    await dbContext.Database.MigrateAsync(); // Migration'lar� uygula
}

app.Run();
