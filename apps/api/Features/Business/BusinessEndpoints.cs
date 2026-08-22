using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Business;

public static class BusinessEndpoints
{
    public static RouteGroupBuilder MapBusinessEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/branches", GetBranches);
        api.MapGet("/customers", GetCustomers);
        api.MapPost("/customers", CreateCustomer);
        api.MapPut("/customers/{customerId:guid}", UpdateCustomer);
        return api;
    }

    private static async Task<IResult> GetBranches(VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        Results.Ok(await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderByDescending(x => x.IsMain)
            .Select(x => new { x.Id, x.Name, x.IsMain }).ToListAsync(cancellationToken));

    private static async Task<IResult> GetCustomers(VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        Results.Ok(await db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.Notes, x.IsActive, x.WalletBalance, Purchases = db.Sales.Count(s => s.CustomerId == x.Id && s.Status != SaleStatus.Cancelled), TotalSpent = db.Sales.Where(s => s.CustomerId == x.Id && s.Status != SaleStatus.Cancelled).Sum(s => (decimal?)s.Total) ?? 0 })
            .ToListAsync(cancellationToken));

    private static async Task<IResult> CreateCustomer(SaveCustomerRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["El nombre es obligatorio."] });
        var customer = new Customer { Name = request.Name.Trim(), Phone = Normalize(request.Phone), Email = Normalize(request.Email)?.ToLowerInvariant(), Notes = Normalize(request.Notes) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/customers/{customer.Id}", new { customer.Id });
    }

    private static async Task<IResult> UpdateCustomer(Guid customerId, SaveCustomerRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        if (customer is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
        customer.Name = request.Name.Trim();
        customer.Phone = Normalize(request.Phone);
        customer.Email = Normalize(request.Email)?.ToLowerInvariant();
        customer.Notes = Normalize(request.Notes);
        customer.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
