var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
    ?? "Server=localhost;Database=ProductsDb;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddSingleton<IProductsRepository>(_ => new ProductsRepository(defaultConnection));

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ProductsApi");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();

var repository = app.Services.GetRequiredService<IProductsRepository>();
repository.InitializeDatabase();

app.MapGet("/products", async (IProductsRepository repo, int page = 1, int pageSize = 6) =>
{
    var normalizedPage = Math.Max(1, page);
    var normalizedPageSize = Math.Clamp(pageSize <= 0 ? 6 : pageSize, 1, 50);

    logger.LogInformation("Fetching products page {Page} with page size {PageSize}", normalizedPage, normalizedPageSize);
    var result = await repo.GetPageAsync(normalizedPage, normalizedPageSize);
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
    logger.LogInformation("Creating product with SKU {Sku}", request.Code);
    var created = await repo.CreateAsync(new Product
    {
        Code = request.Code,
        Name = request.Name,
        Description = request.Description,
        SaleStartDate = request.SaleStartDate,
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

public class PagedResult<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = new();
}

public record CreateProductRequest(
    string Code,
    string Name,
    string Description,
    DateTime SaleStartDate,
    string Image);

public record UpdateProductRequest(
    string Code,
    string Name,
    string Description,
    DateTime SaleStartDate,
    string Image);