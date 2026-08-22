using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class PlanCatalogTests
{
    [Theory]
    [InlineData("esencial", "esencial")]
    [InlineData("NEGOCIO", "negocio")]
    [InlineData("pro", "pro")]
    public void Get_ReturnsRequestedPlanIgnoringCase(string input, string expected)
    {
        Assert.Equal(expected, PlanCatalog.Get(input).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desconocido")]
    public void Get_FallsBackToEssentialForUnknownPlan(string? input)
    {
        Assert.Same(PlanCatalog.Essential, PlanCatalog.Get(input));
    }

    [Fact]
    public void HigherPlansIncreaseOperationalLimits()
    {
        Assert.True(PlanCatalog.Business.MaxUsers > PlanCatalog.Essential.MaxUsers);
        Assert.True(PlanCatalog.Pro.MaxUsers > PlanCatalog.Business.MaxUsers);
        Assert.True(PlanCatalog.Pro.MaxBranches > PlanCatalog.Business.MaxBranches);
    }
}
