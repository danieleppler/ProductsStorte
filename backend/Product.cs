public class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime SaleStartDate { get; set; }
    public string Image { get; set; } = string.Empty;
}
