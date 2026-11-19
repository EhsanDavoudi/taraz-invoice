using System;

namespace TarazInvoice.Models
{
    public class StorageItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = "عدد";
        public decimal DefaultPrice { get; set; } = 0;
        public decimal StockQuantity { get; set; } = 0;
        public string Category { get; set; } = "عمومی";
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public override string ToString() => !string.IsNullOrEmpty(Name) ? Name : Code;

        public StorageItem Clone()
        {
            return new StorageItem
            {
                Id = this.Id,
                Code = this.Code,
                Name = this.Name,
                Unit = this.Unit,
                DefaultPrice = this.DefaultPrice,
                StockQuantity = this.StockQuantity,
                Category = this.Category,
                Description = this.Description,
                CreatedAt = this.CreatedAt,
                UpdatedAt = this.UpdatedAt
            };
        }
    }
}
