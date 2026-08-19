using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

public class ProductsRepository : IProductsRepository
{
    private string _connectionString;
    private readonly ILogger<ProductsRepository> _logger;

    public ProductsRepository(string connectionString,ILogger<ProductsRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public void InitializeDatabase()
    {

        EnsureDatabaseFileExists();
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        _logger.LogDebug("Initalizing databaes");   

        using var command = new SqlCommand(@"
            IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Products (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Code NVARCHAR(100) NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    SaleStartDate DATE NOT NULL,
                    InStock BIT NOT NULL DEFAULT 1,
                    Image NVARCHAR(500) NULL
                );
            END;

            IF OBJECT_ID(N'dbo.sp_Products_GetPageJson', N'P') IS NULL
            BEGIN
                EXEC('CREATE PROCEDURE dbo.sp_Products_GetPageJson
                    @Page INT = 1,
                    @PageSize INT = 5,
                    @SortBy NVARCHAR(50) = ''Id'',
                    @SortOrder NVARCHAR(4) = ''ASC''
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF @SortBy NOT IN (''Id'', ''Code'', ''Name'', ''SaleStartDate'') SET @SortBy = ''Id'';
                    IF @SortOrder NOT IN (''ASC'', ''DESC'') SET @SortOrder = ''ASC'';

                    DECLARE @TotalCount INT = (SELECT COUNT(*) FROM dbo.Products);
                    DECLARE @TotalPages INT = CASE WHEN @TotalCount = 0 THEN 1 ELSE CEILING(CAST(@TotalCount AS DECIMAL(18,2)) / @PageSize) END;
                    DECLARE @NormalizedPage INT = CASE WHEN @Page < 1 THEN 1 WHEN @Page > @TotalPages THEN @TotalPages ELSE @Page END;
                    DECLARE @Offset INT = (@NormalizedPage - 1) * @PageSize;

                    DECLARE @sql NVARCHAR(MAX) = N''
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
                                        saleStartDate = p.SaleStartDate,
                                        inStock = p.InStock,
                                        p.Image
                                    FROM dbo.Products p
                                    ORDER BY '' + QUOTENAME(@SortBy) + N'' '' + @SortOrder + N''
                                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                                    FOR JSON PATH
                                )
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        ) AS ResultJson;'';

                    EXEC sp_executesql @sql,
                        N''@NormalizedPage INT, @PageSize INT, @TotalCount INT, @Offset INT'',
                        @NormalizedPage, @PageSize, @TotalCount, @Offset;
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
                            saleStartDate = SaleStartDate,
                            inStock = InStock,
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
                        InStock BIT,
                        Image NVARCHAR(500)
                    );

                    INSERT INTO @Payload (Code, Name, Description, SaleStartDate, InStock, Image)
                    SELECT
                        j.Code,
                        j.Name,
                        j.Description,
                        j.SaleStartDate,
                        j.InStock,
                        j.Image
                    FROM OPENJSON(@ProductJson)
                    WITH (
                        Code NVARCHAR(100) ''$.code'',
                        Name NVARCHAR(200) ''$.name'',
                        Description NVARCHAR(MAX) ''$.description'',
                        SaleStartDate DATE ''$.saleStartDate'',
                        InStock BIT ''$.inStock'',
                        Image NVARCHAR(500) ''$.image''
                    ) AS j;

                    INSERT INTO dbo.Products (Code, Name, Description, SaleStartDate, InStock, Image)
                    SELECT Code, Name, Description, SaleStartDate, InStock, Image
                    FROM @Payload;

                    SELECT TOP(1)
                        Id,
                        Code,
                        Name,
                        Description,
                        saleStartDate = SaleStartDate,
                        inStock = InStock,
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
                        InStock BIT,
                        Image NVARCHAR(500)
                    );

                    INSERT INTO @Payload (Code, Name, Description, SaleStartDate, InStock, Image)
                    SELECT
                        j.Code,
                        j.Name,
                        j.Description,
                        j.SaleStartDate,
                        j.InStock,
                        j.Image
                    FROM OPENJSON(@ProductJson)
                    WITH (
                        Code NVARCHAR(100) ''$.code'',
                        Name NVARCHAR(200) ''$.name'',
                        Description NVARCHAR(MAX) ''$.description'',
                        SaleStartDate DATE ''$.saleStartDate'',
                        InStock BIT ''$.inStock'',
                        Image NVARCHAR(500) ''$.image''
                    ) AS j;

                    UPDATE p
                    SET
                        p.Code = payload.Code,
                        p.Name = payload.Name,
                        p.Description = payload.Description,
                        p.SaleStartDate = payload.SaleStartDate,
                        p.InStock = payload.InStock,
                        p.Image = payload.Image
                    FROM dbo.Products p
                    CROSS JOIN @Payload AS payload
                    WHERE p.Id = @Id;

                    SELECT TOP(1)
                        Id,
                        Code,
                        Name,
                        Description,
                        saleStartDate = SaleStartDate,
                        inStock = InStock,
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
                INSERT INTO dbo.Products (Code, Name, Description, SaleStartDate, InStock, Image)
                VALUES
                    (N'P-1001', N'AeroFlex Smartwatch', N'Lightweight fitness smartwatch with heart-rate tracking and GPS support.', '2025-01-14', 1, N'/images/products/aeroflex-smartwatch.svg'),
                    (N'P-1002', N'LumaDesk Lamp', N'Minimal desk lighting with adjustable brightness and warm-to-cool tones.', '2025-02-03', 1, N'/images/products/lumadesk-lamp.svg'),
                    (N'P-1003', N'Terra Bottle', N'Insulated stainless-steel bottle designed for travel and daily hydration.', '2025-03-11', 0, N'/images/products/terra-bottle.svg'),
                    (N'P-1004', N'Orbit Headphones', N'Noise-canceling over-ear headphones with studio-quality sound output.', '2025-04-09', 1, N'/images/products/orbit-headphones.svg');
            ", connection);

            seed.ExecuteNonQuery();
        }
        else
        {
            using var normalizeSeed = new SqlCommand(@"
                UPDATE dbo.Products
                SET Image = CASE Code
                    WHEN N'P-1001' THEN N'/images/products/aeroflex-smartwatch.svg'
                    WHEN N'P-1002' THEN N'/images/products/lumadesk-lamp.svg'
                    WHEN N'P-1003' THEN N'/images/products/terra-bottle.svg'
                    WHEN N'P-1004' THEN N'/images/products/orbit-headphones.svg'
                    ELSE Image
                END
                WHERE Code IN (N'P-1001', N'P-1002', N'P-1003', N'P-1004');
            ", connection);

            normalizeSeed.ExecuteNonQuery();
        }
    }

private void EnsureDatabaseFileExists()
{
    // Path to the .mdf inside App_Data
    var dataDir = AppDomain.CurrentDomain.GetData("DataDirectory") as string
                  ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
    Directory.CreateDirectory(dataDir);   // make sure App_Data exists
    var mdfPath = Path.Combine(dataDir, "ProductsDb.mdf");

    if (File.Exists(mdfPath))
        return;   // already created, nothing to do

    // Connect to master (not AttachDbFilename) to create the database
    var masterConnStr = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=True";
    using var conn = new SqlConnection(masterConnStr);
    conn.Open();

    var ldfPath = Path.Combine(dataDir, "ProductsDb_log.ldf");
    // Drop any stale registration, then create fresh with explicit file paths
    var sql = $@"
        IF DB_ID('ProductsDb') IS NOT NULL
        BEGIN
            ALTER DATABASE [ProductsDb] SET OFFLINE WITH ROLLBACK IMMEDIATE;
            EXEC sp_detach_db 'ProductsDb';
        END
        CREATE DATABASE [ProductsDb] ON (NAME='ProductsDb', FILENAME='{mdfPath}')
            LOG ON (NAME='ProductsDb_log', FILENAME='{ldfPath}');
        EXEC sp_detach_db 'ProductsDb';";

    using var cmd = new SqlCommand(sql, conn);
    cmd.ExecuteNonQuery();
}

    public async Task<List<Product>> GetAllAsync()
    {
        var pageResult = await GetPageAsync(1, int.MaxValue);
        return pageResult.Items;
    }

    public async Task<PagedResult<Product>> GetPageAsync(int page, int pageSize, string sortBy ="Id", string sortOrder = "ASC")
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("dbo.sp_Products_GetPageJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@Page", SqlDbType.Int).Value = page;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        command.Parameters.Add("@SortBy",SqlDbType.VarChar).Value = sortBy;
        command.Parameters.Add("@SortOrder",SqlDbType.VarChar).Value = sortOrder;

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
            Items = []
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
                    InStock = item.TryGetProperty("InStock", out var inStockElement) && inStockElement.ValueKind != JsonValueKind.Null ? inStockElement.GetBoolean()
                        : item.TryGetProperty("inStock", out var lowerInStockElement) && lowerInStockElement.ValueKind != JsonValueKind.Null ? lowerInStockElement.GetBoolean()
                        : true,
                    Image = item.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : string.Empty
                });
                _logger.LogDebug("Fetched product: {ProductId} - {ProductName}, with in-stock status: {InStock}", result.Items.Last().Id, result.Items.Last().Name, result.Items.Last().InStock);
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
            InStock = item.TryGetProperty("InStock", out var inStockElement) && inStockElement.ValueKind != JsonValueKind.Null ? inStockElement.GetBoolean()
                : item.TryGetProperty("inStock", out var lowerInStockElement) && lowerInStockElement.ValueKind != JsonValueKind.Null ? lowerInStockElement.GetBoolean()
                : true,
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
            image = product.Image,
            inStock = product.InStock
        };
        
        var jsonPayload = JsonSerializer.Serialize(payload);
        _logger.LogInformation("Creating product with SKU {Sku} and in-stock status {InStock}. JSON payload: {JsonPayload}", payload.code, payload.inStock, jsonPayload);

        using var command = new SqlCommand("dbo.sp_Products_CreateJson", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@ProductJson", SqlDbType.NVarChar, -1).Value = jsonPayload;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return product;
        }

        var json = reader.GetString(0);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;

        item.TryGetProperty("Id", out var test);
        _logger.LogInformation($"Created product with ID: {test}");
        return new Product
        {
            Id = item.TryGetProperty("Id", out var idElement) ? idElement.GetInt32() : 0,
            Code = item.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() ?? string.Empty : product.Code,
            Name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : product.Name,
            Description = item.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : product.Description,
            SaleStartDate = item.TryGetProperty("SaleStartDate", out var dateElement) ? DateTime.Parse(dateElement.GetString() ?? product.SaleStartDate.ToString("yyyy-MM-dd")) : product.SaleStartDate,
            InStock = item.TryGetProperty("InStock", out var inStockElement) && inStockElement.ValueKind != JsonValueKind.Null ? inStockElement.GetBoolean()
                : item.TryGetProperty("inStock", out var lowerInStockElement) && lowerInStockElement.ValueKind != JsonValueKind.Null ? lowerInStockElement.GetBoolean()
                : product.InStock,
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
            image = product.Image,
            inStock = product.InStock
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
            InStock = item.TryGetProperty("InStock", out var inStockElement) && inStockElement.ValueKind != JsonValueKind.Null ? inStockElement.GetBoolean()
                : item.TryGetProperty("inStock", out var lowerInStockElement) && lowerInStockElement.ValueKind != JsonValueKind.Null ? lowerInStockElement.GetBoolean()
                : product.InStock,
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
