public interface IProductsRepository
{
    void InitializeDatabase();
    Task<List<Product>> GetAllAsync();
    Task<PagedResult<Product>> GetPageAsync(int page, int pageSize);
    Task<string> GetNextSkuAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<Product> CreateAsync(Product product);
    Task<Product?> UpdateAsync(int id, Product product);
    Task<bool> DeleteAsync(int id);
}
