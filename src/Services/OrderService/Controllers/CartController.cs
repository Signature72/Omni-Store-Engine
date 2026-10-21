using Microsoft.AspNetCore.Mvc;
using OmniStore.OrderService.Models;
using OmniStore.OrderService.Services;

namespace OmniStore.OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet("{cartId}")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(string cartId)
    {
        var cart = await _cartService.GetCartAsync(cartId);
        return Ok(cart);
    }

    [HttpPost("{cartId}/items")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem(string cartId, [FromBody] AddCartItemRequest request)
    {
        var cartItem = new CartItem
        {
            ProductId = request.ProductId,
            ProductName = request.ProductName,
            UnitPrice = request.UnitPrice,
            Quantity = request.Quantity > 0 ? request.Quantity : 1,
            ImageUrl = request.ImageUrl ?? string.Empty
        };

        var updatedCart = await _cartService.AddItemAsync(cartId, cartItem);
        return Ok(updatedCart);
    }

    [HttpPut("{cartId}/items/{productId:guid}")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateQuantity(string cartId, Guid productId, [FromBody] UpdateCartItemRequest request)
    {
        var updatedCart = await _cartService.UpdateQuantityAsync(cartId, productId, request.Quantity);
        return Ok(updatedCart);
    }

    [HttpDelete("{cartId}/items/{productId:guid}")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(string cartId, Guid productId)
    {
        var updatedCart = await _cartService.RemoveItemAsync(cartId, productId);
        return Ok(updatedCart);
    }

    [HttpDelete("{cartId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearCart(string cartId)
    {
        await _cartService.ClearCartAsync(cartId);
        return NoContent();
    }
}
