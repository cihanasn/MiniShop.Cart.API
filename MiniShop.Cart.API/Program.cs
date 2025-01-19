using Microsoft.EntityFrameworkCore;
using MiniShop.Cart.API.Context;
using MiniShop.Cart.API.Dtos;
using MiniShop.Cart.API.Models;
using System;

var builder = WebApplication.CreateBuilder(args);

// Connection string tanýmý (appsettings.json'dan okunabilir)
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
    // Yeni bir Cart nesnesi oluþturuluyor
    var cart = new Cart
    {
        UserId = createCartDto.UserId,
        CartItems = new List<CartItem>()
    };

    foreach (var cartItemDto in createCartDto.CartItems)
    {
        // CartItem oluþturuluyor
        var cartItem = new CartItem
        {
            ProductId = cartItemDto.ProductId,
            Quantity = cartItemDto.Quantity
        };
        cart.CartItems.Add(cartItem);
    }

    // Cart nesnesi veritabanýna ekleniyor
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
                    // Ürün bulunamazsa, varsayýlan bir yanýt dönebilirsiniz
                    cartItemsDto.Add(new GetCartItemDto(cartItem.Id, cartItem.ProductId, "Bilinmiyor", 0m, cartItem.Quantity));
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ürün bilgisi alýnýrken bir hata oluþtu: {ex.Message}");
            }
        }

        result.Add(new GetAllCartsDto(cart.Id, cart.UserId, cartItemsDto));
    }

    return Results.Ok(result);
});

app.MapGet("/api/carts/create-order", async (CartDbContext dbContext, IHttpClientFactory httpClientFactory, Guid userId,
                                       CancellationToken cancellationToken) =>
{
    // Kullanýcýnýn sepetini al
    var cart = await dbContext.Carts
        .Include(c => c.CartItems)
        .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    if (cart == null || !cart.CartItems.Any())
    {
        return Results.NotFound("Sepet bulunamadý veya sepet boþ.");
    }

    // Order servisi için HTTP istemcisi oluþtur
    var httpClient = httpClientFactory.CreateClient("OrderService");
    var productHttpClient = httpClientFactory.CreateClient("ProductService");

    // Sipariþ için DTO'yu oluþtur
    var orderDtoList = new List<object>
    {
        new
        {
            Items = cart.CartItems.Select(cartItem => new
            {
                ProductId = cartItem.ProductId,
                Quantity = cartItem.Quantity,
                Price = 0 // Ürün fiyatýný burada belirleyebilirsiniz
            }).ToList()
        }
    };

    // Sipariþ servisine istek at
    try
    {
        var response = await httpClient.PostAsJsonAsync("/create-order", orderDtoList, cancellationToken);

        if (response.IsSuccessStatusCode)
        {

            // Stok düþme iþlemi
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
                    return Results.Problem($"Stok güncellenirken bir hata oluþtu: {stockResponse.ReasonPhrase}");
                }
            }

            // Sipariþ baþarýlý, sepeti temizle
            dbContext.Carts.Remove(cart);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok("Sipariþ oluþturuldu ve sepet temizlendi.");
        }

        return Results.Problem("Sipariþ oluþturulamadý: " + response.ReasonPhrase);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Sipariþ oluþturulurken bir hata oluþtu: {ex.Message}");
    }
});


// Migration'larý otomatik olarak uygula
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CartDbContext>();
    await dbContext.Database.MigrateAsync(); // Migration'larý uygula
}

app.Run();
