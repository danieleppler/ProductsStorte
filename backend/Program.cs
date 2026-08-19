using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);


builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=ProductsDb;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddSingleton<IProductsRepository>(sp => 
new ProductsRepository(defaultConnection,sp.GetRequiredService<ILogger<ProductsRepository>>()));

var app = builder.Build();

AppDomain.CurrentDomain.SetData("DataDirectory",
    Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
    
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ProductsApi");


app.UseCors("AllowFrontend");
app.UseHttpsRedirection();
app.UseStaticFiles();

var repository = app.Services.GetRequiredService<IProductsRepository>();
try
{
    repository.InitializeDatabase();
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Database initialization failed. The API will continue running without a database connection.");
}

app.MapPost("/products/upload-image", async (IFormFile file, IWebHostEnvironment env) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Please choose an image file to upload." });
    }

    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg"
    };

    var extension = Path.GetExtension(file.FileName);
    var contentTypeLooksLikeImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    var extensionLooksLikeImage = !string.IsNullOrWhiteSpace(extension) && allowedExtensions.Contains(extension);

    if (!contentTypeLooksLikeImage && !extensionLooksLikeImage)
    {
        return Results.BadRequest(new
        {
            error = "Unsupported file type. Please upload a standard image like JPG, PNG, GIF, WEBP, SVG, or BMP."
        });
    }

    var uploadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "images", "products");
    Directory.CreateDirectory(uploadsFolder);

    var fileName = Path.GetFileNameWithoutExtension(file.FileName);
    fileName = Regex.Replace(fileName, @"[^a-zA-Z0-9._-]+", "-");
    var uniqueFileName = $"{fileName}-{Guid.NewGuid():N}{extension}";
    var destination = Path.Combine(uploadsFolder, uniqueFileName);

    await using (var stream = File.Create(destination))
    {
        await file.CopyToAsync(stream);
    }

    var relativePath = $"/images/products/{uniqueFileName}";
    return Results.Ok(new { path = relativePath, fileName = uniqueFileName });
}).DisableAntiforgery();

app.MapGet("/products", async (IProductsRepository repo, int page = 1, int pageSize = 5,string sortBy ="Id",int sortOrder = 1) =>
{
    var normalizedPage = Math.Max(1, page);
    var normalizedPageSize = Math.Clamp(pageSize <= 0 ? 6 : pageSize, 1, 50);

    var allowedSortingFields = typeof(Product).GetProperties().Select(p => p.Name).ToArray();
    var normalizedSortBy = allowedSortingFields.Contains(sortBy) ? sortBy : "Id" ;     
    var normalizedSortOrder = sortOrder == 1 ? "ASC" : "DESC";

    logger.LogInformation("Fetching products page {Page} with page size {PageSize}", normalizedPage, normalizedPageSize);
    var result = await repo.GetPageAsync(normalizedPage, normalizedPageSize,normalizedSortBy,normalizedSortOrder);
    return Results.Ok(result);
});

app.MapGet("/products/next-sku", async (IProductsRepository repo) =>
{
    logger.LogInformation("Fetching next product SKU");
    var sku = await repo.GetNextSkuAsync();
    return Results.Ok(new { sku });
});

app.MapGet("/products/{id:int}", async (int id, IProductsRepository repo) =>
{
    logger.LogInformation("Fetching product {ProductId}", id);
    var product = await repo.GetByIdAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.MapPost("/products", async (CreateProductRequest request, IProductsRepository repo) =>
{
    logger.LogInformation("Creating product with SKU {Sku} and in-stock status {InStock}", request.Code, request.InStock);
    var created = await repo.CreateAsync(new Product
    {
        Code = request.Code,
        Name = request.Name,
        Description = request.Description,
        SaleStartDate = request.SaleStartDate,
        InStock = request.InStock,
        Image = request.Image
    });

    return Results.Created($"/products/{created.Id}", created);
});

app.MapPut("/products/{id:int}", async (int id, UpdateProductRequest request, IProductsRepository repo) =>
{
    logger.LogInformation("Updating product {ProductId} with SKU {Sku}", id, request.Code);
    var updated = await repo.UpdateAsync(id, new Product
    {
        Code = request.Code,
        Name = request.Name,
        Description = request.Description,
        SaleStartDate = request.SaleStartDate,
        InStock = request.InStock,
        Image = request.Image
    });

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/products/{id:int}", async (int id, IProductsRepository repo) =>
{
    logger.LogInformation("Deleting product {ProductId}", id);
    var deleted = await repo.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.Run();
