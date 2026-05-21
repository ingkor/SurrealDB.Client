namespace SurrealDB.Client.Sample.Api.Migrations;

using SurrealDB.Client.Migrations;

public class AddProductCategoryIndex : Migration
{
    public override string Name => "20260101_add_product_category_index";

    public override string Description => "Creates an index on products.category for faster category lookups";

    public override async Task Up(IMigrationExecutor executor, CancellationToken cancellationToken = default)
    {
        await executor.CreateIndexAsync("products", "idx_product_category", ["category"], cancellationToken: cancellationToken);
    }

    public override async Task Down(IMigrationExecutor executor, CancellationToken cancellationToken = default)
    {
        await executor.DropIndexAsync("products", "idx_product_category", cancellationToken);
    }
}
