public record CreateProductRequest(
    string Code,
    string Name,
    string Description,
    DateTime SaleStartDate,
    bool InStock,
    string Image);
