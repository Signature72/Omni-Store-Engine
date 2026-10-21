using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniStore.InventoryService.Data;
using OmniStore.InventoryService.Entities;

namespace OmniStore.InventoryService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(InventoryDbContext dbContext, ILogger<InventoryController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("products")]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("products/{id:guid}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null)
        {
            return NotFound(new { message = $"Product with ID '{id}' was not found." });
        }

        return Ok(product);
    }

    [HttpPost("seed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SeedProducts(CancellationToken cancellationToken)
    {
        if (await _dbContext.Products.AnyAsync(cancellationToken))
        {
            return Ok(new { message = "Catalog already seeded.", count = await _dbContext.Products.CountAsync(cancellationToken) });
        }

        var sampleProducts = new List<Product>
        {
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Sku = "MBP-16-M3MAX",
                Name = "MacBook Pro 16\" (M3 Max, 64GB, 2TB SSD)",
                Description = "High-performance laptop engineered for extreme microservice compiling, architecture modeling, and distributed computing.",
                Price = 3499.00m,
                AvailableStock = 50,
                ReservedStock = 0,
                ImageUrl = "https://images.unsplash.com/photo-1517336714731-489689fd1ca8?auto=format&fit=crop&w=800&q=80"
            },
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Sku = "SONY-WH1000XM5",
                Name = "Sony WH-1000XM5 Wireless Headphones",
                Description = "Industry-leading noise canceling with two processors and 8 microphones for total immersion during deep coding sessions.",
                Price = 399.99m,
                AvailableStock = 120,
                ReservedStock = 0,
                ImageUrl = "https://images.unsplash.com/photo-1505740420928-5e560c06d30e?auto=format&fit=crop&w=800&q=80"
            },
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Sku = "KEYCHRON-Q1-PRO",
                Name = "Keychron Q1 Pro Wireless Custom Mechanical Keyboard",
                Description = "Full metal QMK/VIA wireless mechanical keyboard with Gateron Jupiter Banana tactile switches and double-gasket design.",
                Price = 199.00m,
                AvailableStock = 75,
                ReservedStock = 0,
                ImageUrl = "https://images.unsplash.com/photo-1587829741301-dc798b83add3?auto=format&fit=crop&w=800&q=80"
            },
            new()
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Sku = "LOGI-MX-MASTER-3S",
                Name = "Logitech MX Master 3S Performance Mouse",
                Description = "Quiet clicks and 8K DPI track-on-glass sensor designed for software architects and multi-monitor power users.",
                Price = 99.99m,
                AvailableStock = 200,
                ReservedStock = 0,
                ImageUrl = "https://images.unsplash.com/photo-1615663245857-ac93bb7c39e7?auto=format&fit=crop&w=800&q=80"
            },
            new()
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Sku = "DELL-U3423WE",
                Name = "Dell UltraSharp 34\" Curved USB-C Hub Monitor",
                Description = "IPS Black technology with 2000:1 contrast ratio, integrated KVM switch and 90W USB-C power delivery.",
                Price = 849.50m,
                AvailableStock = 30,
                ReservedStock = 0,
                ImageUrl = "https://images.unsplash.com/photo-1527443224154-c4a3942d3acf?auto=format&fit=crop&w=800&q=80"
            }
        };

        _dbContext.Products.AddRange(sampleProducts);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded {Count} demo products into InventoryDB.", sampleProducts.Count);
        return Ok(new { message = "Seeded successfully.", count = sampleProducts.Count });
    }
}
