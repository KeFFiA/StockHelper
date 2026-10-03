using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockHelper.Data;

/// <summary>Used only by <c>dotnet ef</c> to create migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockHelperDbContext>
{
    public StockHelperDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<StockHelperDbContext>();
        DataServiceCollectionExtensions.Configure(builder, new DatabaseOptions { ConnectionString = "Data Source=design-time.db" });
        return new StockHelperDbContext(builder.Options);
    }
}
