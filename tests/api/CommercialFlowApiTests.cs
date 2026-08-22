using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Tests;

public sealed class CommercialFlowApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public CommercialFlowApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CompleteCommercialFlowKeepsStockAndCashConsistent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var auth = await Register(client, cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var branches = await client.GetFromJsonAsync<List<BranchDto>>("/api/v1/branches", cancellationToken);
        var branchId = Assert.Single(branches!).Id;

        var createProduct = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            "Playera integración", null, null, "Mediana", $"SKU-{Guid.NewGuid():N}", null,
            Cost: 40, Price: 100, MinimumStock: 2, InitialStock: 10, BranchId: branchId), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createProduct.StatusCode);

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/v1/products", cancellationToken);
        var product = Assert.Single(products!, x => x.Name == "Playera integración");
        Assert.Equal(10, product.Stock);

        var open = await client.PostAsJsonAsync("/api/v1/cash/open", new OpenCashSessionRequest(branchId, 100), cancellationToken);
        open.EnsureSuccessStatusCode();
        var cash = (await open.Content.ReadFromJsonAsync<CashDto>(cancellationToken))!;

        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales", new CreateSaleRequest(
            branchId, cash.Id, [new CreateSaleItemRequest(product.VariantId, 2)],
            PaymentMethod.Cash, ReceivedAmount: 200), cancellationToken);
        saleResponse.EnsureSuccessStatusCode();
        var sale = (await saleResponse.Content.ReadFromJsonAsync<SaleDto>(cancellationToken))!;
        Assert.Equal(200, sale.Total);
        Assert.Equal(8, await Stock(client, product.VariantId, cancellationToken));

        var detail = await client.GetFromJsonAsync<SaleDetailDto>($"/api/v1/sales/{sale.Id}", cancellationToken);
        var soldItem = Assert.Single(detail!.Items);
        var returned = await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/return",
            new ReturnSaleRequest("Cambio de talla", [new ReturnSaleItemRequest(soldItem.Id, 1)]), cancellationToken);
        returned.EnsureSuccessStatusCode();
        var returnResult = (await returned.Content.ReadFromJsonAsync<ReturnDto>(cancellationToken))!;
        Assert.Equal(100, returnResult.RefundAmount);
        Assert.False(returnResult.FullyReturned);
        Assert.Equal(9, await Stock(client, product.VariantId, cancellationToken));

        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Cliente apartado", "5551234567", null, null), cancellationToken);
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CreatedIdDto>(cancellationToken))!;

        var layawayResponse = await client.PostAsJsonAsync("/api/v1/layaways", new CreateLayawayRequest(
            branchId, customer.Id, 30, 50, PaymentMethod.Cash, "Prueba HTTP",
            [new CreateLayawayItemRequest(product.VariantId, 1)]), cancellationToken);
        layawayResponse.EnsureSuccessStatusCode();
        var layaway = (await layawayResponse.Content.ReadFromJsonAsync<LayawayDto>(cancellationToken))!;
        Assert.Equal(50, layaway.Balance);
        Assert.Equal(8, await Stock(client, product.VariantId, cancellationToken));

        var cancelLayaway = await client.PostAsync($"/api/v1/layaways/{layaway.Id}/cancel", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, cancelLayaway.StatusCode);
        Assert.Equal(9, await Stock(client, product.VariantId, cancellationToken));

        var close = await client.PostAsJsonAsync($"/api/v1/cash/{cash.Id}/close",
            new CloseCashSessionRequest(250), cancellationToken);
        close.EnsureSuccessStatusCode();
        var closeResult = (await close.Content.ReadFromJsonAsync<CloseDto>(cancellationToken))!;
        Assert.Equal(250, closeResult.ExpectedAmount);
        Assert.Equal(0, closeResult.DifferenceAmount);

        var current = await client.GetAsync("/api/v1/cash/current", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    private static async Task<decimal> Stock(HttpClient client, Guid variantId, CancellationToken cancellationToken)
    {
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/v1/products", cancellationToken);
        return Assert.Single(products!, x => x.VariantId == variantId).Stock;
    }

    private static async Task<AuthResponse> Register(HttpClient client, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterBusinessRequest($"Flujo {id}", "Propietario", $"flow-{id}@example.com", "Password123!", "negocio", null, null, null, null),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken))!;
    }

    private sealed record BranchDto(Guid Id);
    private sealed record CashDto(Guid Id);
    private sealed record SaleDto(Guid Id, decimal Total);
    private sealed record SaleDetailDto(IReadOnlyList<SaleItemDto> Items);
    private sealed record SaleItemDto(Guid Id);
    private sealed record ReturnDto(decimal RefundAmount, bool FullyReturned);
    private sealed record CreatedIdDto(Guid Id);
    private sealed record LayawayDto(Guid Id, decimal Balance);
    private sealed record CloseDto(decimal ExpectedAmount, decimal DifferenceAmount);
}
