namespace Contracts;

// Contrato compartilhado entre OrderService (produtor) e NotificationService (consumidor).
// Em produção: publicado como pacote NuGet interno. Aqui: ProjectReference para simplicidade.
public class OrderCreatedMessage
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
