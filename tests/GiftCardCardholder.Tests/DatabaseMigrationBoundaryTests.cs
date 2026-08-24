using GiftCardCardholder.Web.Sessions;
using Microsoft.Extensions.Configuration;

namespace GiftCardCardholder.Tests;

public sealed class DatabaseMigrationBoundaryTests
{
    [Fact]
    public void MigrationModeRequiresTheExactSwitch()
    {
        Assert.True(CardholderDatabaseMigrator.IsRequested(["--migrate"]));
        Assert.False(CardholderDatabaseMigrator.IsRequested([]));
        Assert.False(CardholderDatabaseMigrator.IsRequested(["--MIGRATE"]));
        Assert.False(CardholderDatabaseMigrator.IsRequested(["migrate"]));
    }

    [Fact]
    public async Task MigrationModeRequiresASeparateOwnerConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Cardholder"] =
                    "Host=localhost;Database=cardholder;Username=cardholder_app",
            })
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CardholderDatabaseMigrator.RunAsync(configuration, CancellationToken.None));

        Assert.Contains("CardholderMigrations", exception.Message, StringComparison.Ordinal);
    }
}
