using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;

namespace StockTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;
    private readonly ProductInspectService _inspectService;
    private readonly IStoreAdapterRegistry _registry;
    private readonly IUsageLimitService? _usageLimitService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        ProductService productService,
        ProductInspectService inspectService,
        IStoreAdapterRegistry registry,
        ILogger<ProductsController> logger,
        IUsageLimitService? usageLimitService = null)
    {
        _productService = productService;
        _inspectService = inspectService;
        _registry = registry;
        _usageLimitService = usageLimitService;
        _logger = logger;
    }

    private int? GetCurrentUserId()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idStr, out var id) && id > 0) return id;
        }
        return null;
    }

    /// <summary>
    /// Gets a list of all currently supported stores and their domains.
    /// </summary>
    [HttpGet("stores")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSupportedStores()
    {
        var stores = _registry.GetSupportedStores();
        return Ok(stores);
    }

    /// <summary>
    /// Fetches product information and size variants from the given store URL (legacy endpoint).
    /// </summary>
    [HttpPost("fetch")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> FetchProduct(
        [FromBody] FetchProductRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { success = false, message = "URL boş olamaz." });

        var product = await _productService.FetchProductAsync(request.Url, cancellationToken);

        if (product is null)
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "UNSUPPORTED_STORE",
                message = "Bu mağaza henüz desteklenmiyor."
            });

        return Ok(product);
    }

    /// <summary>
    /// Universal endpoint: Inspects a product URL from any supported store (Zara, Mango, etc.)
    /// and returns normalized product details, primary image, and variant stock availability.
    /// </summary>
    /// <remarks>
    /// Example request:
    ///
    ///     POST /api/products/inspect
    ///     {
    ///        "url": "https://shop.mango.com/tr/tr/kadin/..."
    ///     }
    ///
    /// </remarks>
    [HttpPost("inspect")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("InspectRateLimit")]
    [ProducesResponseType(typeof(ProductInspectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(object), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(object), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> InspectProduct(
        [FromBody] FetchProductRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { success = false, message = "URL boş olamaz." });

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { success = false, message = "Geçersiz URL formatı." });

        var userId = GetCurrentUserId();

        // 1. Check daily inspect limit for authenticated user
        if (_usageLimitService != null)
        {
            var (allowed, errorCode, message) = await _usageLimitService.CanInspectProductAsync(userId, cancellationToken);
            if (!allowed)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = message, code = errorCode });
            }
        }

        try
        {
            var result = await _inspectService.InspectAsync(request.Url, cancellationToken);

            if (_usageLimitService != null && userId.HasValue)
            {
                await _usageLimitService.RecordInspectUsageAsync(userId.Value, cancellationToken);
            }

            return Ok(result);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning("Unsupported store URL: {Url}", request.Url);
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "UNSUPPORTED_STORE",
                error = ex.Message,
                message = "Bu mağaza henüz desteklenmiyor."
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout inspecting URL: {Url}", request.Url);
            return StatusCode(StatusCodes.Status408RequestTimeout,
                new { success = false, error = "Sayfa yüklenirken zaman aşımına uğrandı. Lütfen tekrar deneyin." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Inspection failed for URL: {Url}", request.Url);
            return UnprocessableEntity(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error inspecting URL: {Url}", request.Url);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, error = "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin." });
        }
    }
}
