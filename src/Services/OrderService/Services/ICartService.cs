using System.Text.Json;
using OmniStore.OrderService.Models;
using StackExchange.Redis;

namespace OmniStore.OrderService.Services;

public interface ICartService
{
    Task<Cart> GetCartAsync(string cartId);
    Task<Cart> AddItemAsync(string cartId, CartItem item);
    Task<Cart> UpdateQuantityAsync(string cartId, Guid productId, int quantity);
    Task<Cart> RemoveItemAsync(string cartId, Guid productId);
    Task<bool> ClearCartAsync(string cartId);
}

public class RedisCartService : ICartService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCartService> _logger;
    private static readonly TimeSpan CartTtl = TimeSpan.FromDays(7);

    public RedisCartService(IConnectionMultiplexer redis, ILogger<RedisCartService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private string GetKey(string cartId) => $"cart:{cartId}";

    public async Task<Cart> GetCartAsync(string cartId)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync(GetKey(cartId));

        if (data.IsNullOrEmpty)
        {
            return new Cart { CartId = cartId };
        }

        try
        {
            var cart = JsonSerializer.Deserialize<Cart>(data.ToString());
            return cart ?? new Cart { CartId = cartId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize cart for CartId: {CartId}", cartId);
            return new Cart { CartId = cartId };
        }
    }

    public async Task<Cart> AddItemAsync(string cartId, CartItem item)
    {
        var cart = await GetCartAsync(cartId);
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == item.ProductId);

        if (existingItem != null)
        {
            existingItem.Quantity += item.Quantity;
        }
        else
        {
            cart.Items.Add(item);
        }

        cart.UpdatedAtUtc = DateTime.UtcNow;

        var db = _redis.GetDatabase();
        await db.StringSetAsync(GetKey(cartId), JsonSerializer.Serialize(cart), CartTtl);

        _logger.LogInformation("Added item {ProductId} (qty: {Qty}) to Cart {CartId}", item.ProductId, item.Quantity, cartId);
        return cart;
    }

    public async Task<Cart> UpdateQuantityAsync(string cartId, Guid productId, int quantity)
    {
        var cart = await GetCartAsync(cartId);
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (existingItem != null)
        {
            if (quantity <= 0)
            {
                cart.Items.Remove(existingItem);
            }
            else
            {
                existingItem.Quantity = quantity;
            }

            cart.UpdatedAtUtc = DateTime.UtcNow;
            var db = _redis.GetDatabase();
            await db.StringSetAsync(GetKey(cartId), JsonSerializer.Serialize(cart), CartTtl);
        }

        return cart;
    }

    public async Task<Cart> RemoveItemAsync(string cartId, Guid productId)
    {
        var cart = await GetCartAsync(cartId);
        var itemToRemove = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (itemToRemove != null)
        {
            cart.Items.Remove(itemToRemove);
            cart.UpdatedAtUtc = DateTime.UtcNow;

            var db = _redis.GetDatabase();
            await db.StringSetAsync(GetKey(cartId), JsonSerializer.Serialize(cart), CartTtl);
            _logger.LogInformation("Removed product {ProductId} from Cart {CartId}", productId, cartId);
        }

        return cart;
    }

    public async Task<bool> ClearCartAsync(string cartId)
    {
        var db = _redis.GetDatabase();
        var deleted = await db.KeyDeleteAsync(GetKey(cartId));
        _logger.LogInformation("Cleared Cart {CartId}", cartId);
        return deleted;
    }
}
