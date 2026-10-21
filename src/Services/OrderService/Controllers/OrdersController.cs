using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniStore.OrderService.Data;
using OmniStore.OrderService.Entities;
using OmniStore.OrderService.Models;
using OmniStore.OrderService.Services;

namespace OmniStore.OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _dbContext;
    private readonly ICheckoutService _checkoutService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderDbContext dbContext,
        ICheckoutService checkoutService,
        ILogger<OrdersController> logger)
    {
        _dbContext = dbContext;
        _checkoutService = checkoutService;
        _logger = logger;
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _checkoutService.CheckoutAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Checkout failed: {Message}", ex.Message);

            if (ex.Message.Contains("Stock reservation failed", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status409Conflict, new { error = "StockReservationFailed", message = ex.Message });
            }

            return BadRequest(new { error = "CheckoutFailed", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during checkout");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "ServerError", message = "An unexpected error occurred during checkout." });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders([FromQuery] Guid? userId, CancellationToken cancellationToken)
    {
        var query = _dbContext.Orders
            .Include(o => o.Items)
            .AsNoTracking();

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(orders);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
        {
            return NotFound(new { message = $"Order '{id}' not found." });
        }

        return Ok(order);
    }
}
