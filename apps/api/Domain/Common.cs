namespace VendemeFacil.Api.Domain;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
}

public enum OperationMode { Simple = 1, Complete = 2 }
public enum InventoryMovementType { InitialStock = 1, Entry = 2, Sale = 3, Return = 4, Adjustment = 5, Layaway = 6 }
public enum UserRole { Owner = 1, Administrator = 2, Cashier = 3 }
public enum CashSessionStatus { Open = 1, Closed = 2 }
public enum PaymentMethod { Cash = 1, Card = 2, Transfer = 3, Wallet = 4 }
public enum SaleStatus { Completed = 1, Cancelled = 2, PartiallyReturned = 3 }
public enum LayawayStatus { Active = 1, Completed = 2, Cancelled = 3 }
