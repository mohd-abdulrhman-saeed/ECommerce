using ECommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ICachingService _cachingService;

    public ProductsController(ICachingService cachingService)
    {
        _cachingService = cachingService;
    }

    // 1. جلب المنتج (Read-through Caching + Single Flight + Circuit Breaker)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProductById(int id, CancellationToken cancellationToken)
    {
        string cacheKey = $"product-{id}";

        // استدعاء الكاش سيرفيس مع تمرير الـ Factory (قراءة من الداتابيز الوهمية في حالة الـ Miss)
        var product = await _cachingService.GetOrSetAsync(cacheKey, async (token) =>
        {
            // محاكاة جلب البيانات من قاعدة البيانات (Database Call)
            await Task.Delay(500, token);
            return new ProductDto(id, $"Product Name {id}", 99.99m);

        }, TimeSpan.FromMinutes(5), cancellationToken);

        if (product is null)
            return NotFound();

        return Ok(product);
    }

    // 2. تحديث المنتج (Command-Based Invalidation)
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] UpdateProductDto updateDto, CancellationToken cancellationToken)
    {
        // محاكاة تحديث البيانات في الداتابيز
        await Task.Delay(200, cancellationToken);

        string cacheKey = $"product-{id}";

        // إبطال الكاش فوراً عند التعديل (Command-Based Invalidation)
        await _cachingService.RemoveAsync(cacheKey, cancellationToken);

        return Ok(new { Message = "Product updated and cache invalidated successfully!" });
    }
}

public record ProductDto(int Id, string Name, decimal Price);
public record UpdateProductDto(string Name, decimal Price);