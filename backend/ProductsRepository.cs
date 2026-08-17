using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

public class ProductsRepository : IProductsRepository
{
    private string _connectionString;

    public ProductsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void InitializeDatabase()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(@"
            IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Products (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Code NVARCHAR(100) NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    SaleStartDate DATE NOT NULL,
                    Image NVARCHAR(500) NULL
                );
            END;

            IF OBJECT_ID(N'dbo.sp_Products_GetPageJson', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_GetPageJson
                    @Page INT = 1,
                    @PageSize INT = 6
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @TotalCount INT = (SELECT COUNT(*) FROM dbo.Products);
                    DECLARE @TotalPages INT = CASE WHEN @TotalCount = 0 THEN 1 ELSE CEILING(CAST(@TotalCount AS DECIMAL(18,2)) / @PageSize) END;
                    DECLARE @NormalizedPage INT = CASE WHEN @Page < 1 THEN 1 WHEN @Page > @TotalPages THEN @TotalPages ELSE @Page END;
                    DECLARE @Offset INT = (@NormalizedPage - 1) * @PageSize;

                    SELECT (
                        SELECT
                            Page = @NormalizedPage,
                            PageSize = @PageSize,
                            TotalCount = @TotalCount,
                            Items = (
                                SELECT
                                    p.Id,
                                    p.Code,
                                    p.Name,
                                    p.Description,
                                    p.SaleStartDate,
                                    p.Image
                                FROM dbo.Products p
                                ORDER BY p.Id
                                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                                FOR JSON PATH
                            )
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                    ) AS ResultJson;
                END');
            END;

            IF OBJECT_ID(N'dbo.sp_Products_GetByIdJson', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_GetByIdJson
                    @Id INT
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT (
                        SELECT
                            Id,
                            Code,
                            Name,
                            Description,
                            SaleStartDate,
                            Image
                        FROM dbo.Products
                        WHERE Id = @Id
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                    ) AS ResultJson;
                END');
            END;

            IF OBJECT_ID(N'dbo.sp_Products_CreateJson', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_CreateJson
                    @ProductJson NVARCHAR(MAX)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @Payload TABLE (
                        Code NVARCHAR(100),
                        Name NVARCHAR(200),
                        Description NVARCHAR(MAX),
                        SaleStartDate DATE,
                        Image NVARCHAR(500)
                    );

                    INSERT INTO @Payload (Code, Name, Description, SaleStartDate, Image)
                    SELECT
                        j.Code,
                        j.Name,
                        j.Description,
                        j.SaleStartDate,
                        j.Image
                    FROM OPENJSON(@ProductJson)
                    WITH (
                        Code NVARCHAR(100) ''$.code'',
                        Name NVARCHAR(200) ''$.name'',
                        Description NVARCHAR(MAX) ''$.description'',
                        SaleStartDate DATE ''$.saleStartDate'',
                        Image NVARCHAR(500) ''$.image''
                    ) AS j;

                    INSERT INTO dbo.Products (Code, Name, Description, SaleStartDate, Image)
                    SELECT Code, Name, Description, SaleStartDate, Image
                    FROM @Payload;

                    SELECT TOP(1)
                        Id,
                        Code,
                        Name,
                        Description,
                        SaleStartDate,
                        Image
                    FROM dbo.Products
                    ORDER BY Id DESC
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
                END');
            END;

            IF OBJECT_ID(N'dbo.sp_Products_UpdateJson', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_UpdateJson
                    @Id INT,
                    @ProductJson NVARCHAR(MAX)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @Payload TABLE (
                        Code NVARCHAR(100),
                        Name NVARCHAR(200),
                        Description NVARCHAR(MAX),
                        SaleStartDate DATE,
                        Image NVARCHAR(500)
                    );

                    INSERT INTO @Payload (Code, Name, Description, SaleStartDate, Image)
                    SELECT
                        j.Code,
                        j.Name,
                        j.Description,
                        j.SaleStartDate,
                        j.Image
                    FROM OPENJSON(@ProductJson)
                    WITH (
                        Code NVARCHAR(100) ''$.code'',
                        Name NVARCHAR(200) ''$.name'',
                        Description NVARCHAR(MAX) ''$.description'',
                        SaleStartDate DATE ''$.saleStartDate'',
                        Image NVARCHAR(500) ''$.image''
                    ) AS j;

                    UPDATE p
                    SET
                        p.Code = payload.Code,
                        p.Name = payload.Name,
                        p.Description = payload.Description,
                        p.SaleStartDate = payload.SaleStartDate,
                        p.Image = payload.Image
                    FROM dbo.Products p
                    CROSS JOIN @Payload AS payload
                    WHERE p.Id = @Id;

                    SELECT TOP(1)
                        Id,
                        Code,
                        Name,
                        Description,
                        SaleStartDate,
                        Image
                    FROM dbo.Products
                    WHERE Id = @Id
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
                END');
            END;

            IF OBJECT_ID(N'dbo.sp_Products_Delete', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_Delete
                    @Id INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DELETE FROM dbo.Products WHERE Id = @Id;
                END');
            END;
        ", connection);

        command.ExecuteNonQuery();

        using var seedCheck = new SqlCommand("SELECT COUNT(*) FROM dbo.Products", connection);
        var count = Convert.ToInt32(seedCheck.ExecuteScalar());

        if (count == 0)
        {
            using var seed = new SqlCommand(@"
                INSERT INTO dbo.Products (Code, Name, Description, SaleStartDate, Image)
                VALUES
                    (N'P-1001', N'AeroFlex Smartwatch', N'Lightweight fitness smartwatch with heart-rate tracking and GPS support.', '2025-01-14', N'https://images.unsplash.com/photo-1546868871-7041f2a55e12?auto=format&fit=crop&w=800&q=80'),
                    (N'P-1002', N'LumaDesk Lamp', N'Minimal desk lighting with adjustable brightness and warm-to-cool tones.', '2025-02-03', N'https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=800&q=80'),
                    (N'P-1003', N'Terra Bottle', N'Insulated stainless-steel bottle designed for travel and daily hydration.', '2025-03-11', N'https://images.unsplash.com/photo-1602143407151-7111542de6e8?auto=format&fit=crop&w=800&q=80'),
                    (N'P-1004', N'Orbit Headphones', N'Noise-canceling over-ear headphones with studio-quality sound output.', '2025-04-09', N'https://images.unsplash.com/photo-1546435770-a3e426bf472b?auto=format&fit=crop&w=800&q=80');
            ", connection);

            seed.ExecuteNonQuery();
        }
    }

    public async Task<List<Product>> GetAllAsync()
    {
        var pageResult = await GetPageAsync(1, int.MaxValue);
        return pageResult.Items;
    }

    public async Task<PagedResult<Product>> GetPageAsync(int page, int pageSize)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("dbo.sp_Products_GetPageJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@Page", SqlDbType.Int).Value = page;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new PagedResult<Product> { Page = page, PageSize = pageSize, TotalCount = 0, Items = new List<Product>() };
        }

        var json = reader.GetString(0);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var result = new PagedResult<Product>
        {
            Page = root.TryGetProperty("Page", out var pageElement) ? pageElement.GetInt32() : page,
            PageSize = root.TryGetProperty("PageSize", out var sizeElement) ? sizeElement.GetInt32() : pageSize,
            TotalCount = root.TryGetProperty("TotalCount", out var totalElement) ? totalElement.GetInt32() : 0,
            Items = new List<Product>()
        };

        if (root.TryGetProperty("Items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                result.Items.Add(new Product
                {
                    Id = item.TryGetProperty("Id", out var idElement) ? idElement.GetInt32() : 0,
                    Code = item.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty,
                    Name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
                    Description = item.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : string.Empty,
                    SaleStartDate = item.TryGetProperty("SaleStartDate", out var dateElement) ? DateTime.Parse(dateElement.GetString() ?? DateTime.Today.ToString("yyyy-MM-dd")) : DateTime.Today,
                    Image = item.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : string.Empty
                });
            }
        }

        return result;
    }

    public async Task<string> GetNextSkuAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("SELECT Code FROM dbo.Products", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var highestNumber = 0;
        while (await reader.ReadAsync())
        {
            var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var parsed = ParseSkuNumber(code);
            if (parsed.HasValue)
            {
                highestNumber = Math.Max(highestNumber, parsed.Value);
            }
        }

        return $"SKU-{(highestNumber + 1).ToString("D4")}";
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("dbo.sp_Products_GetByIdJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var json = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;
        return new Product
        {
            Id = item.TryGetProperty("Id", out var idElement) ? idElement.GetInt32() : id,
            Code = item.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty,
            Name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
            Description = item.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : string.Empty,
            SaleStartDate = item.TryGetProperty("SaleStartDate", out var dateElement) ? DateTime.Parse(dateElement.GetString() ?? DateTime.Today.ToString("yyyy-MM-dd")) : DateTime.Today,
            Image = item.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : string.Empty
        };
    }

    private static int? ParseSkuNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(\d+)(?!.*\d)");
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    public async Task<Product> CreateAsync(Product product)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var payload = new
        {
            code = product.Code,
            name = product.Name,
            description = product.Description,
            saleStartDate = product.SaleStartDate.ToString("yyyy-MM-dd"),
            image = product.Image
        };

        using var command = new SqlCommand("dbo.sp_Products_CreateJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@ProductJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(payload);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return product;
        }

        var json = reader.GetString(0);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;

        return new Product
        {
            Id = item.TryGetProperty("Id", out var idElement) ? idElement.GetInt32() : 0,
            Code = item.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() ?? string.Empty : product.Code,
            Name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : product.Name,
            Description = item.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : product.Description,
            SaleStartDate = item.TryGetProperty("SaleStartDate", out var dateElement) ? DateTime.Parse(dateElement.GetString() ?? product.SaleStartDate.ToString("yyyy-MM-dd")) : product.SaleStartDate,
            Image = item.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : product.Image
        };
    }

    public async Task<Product?> UpdateAsync(int id, Product product)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var payload = new
        {
            code = product.Code,
            name = product.Name,
            description = product.Description,
            saleStartDate = product.SaleStartDate.ToString("yyyy-MM-dd"),
            image = product.Image
        };

        using var command = new SqlCommand("dbo.sp_Products_UpdateJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        command.Parameters.Add("@ProductJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(payload);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var json = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;

        return new Product
        {
            Id = id,
            Code = item.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() ?? string.Empty : product.Code,
            Name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : product.Name,
            Description = item.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : product.Description,
            SaleStartDate = item.TryGetProperty("SaleStartDate", out var dateElement) ? DateTime.Parse(dateElement.GetString() ?? product.SaleStartDate.ToString("yyyy-MM-dd")) : product.SaleStartDate,
            Image = item.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : product.Image
        };
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("dbo.sp_Products_Delete", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
