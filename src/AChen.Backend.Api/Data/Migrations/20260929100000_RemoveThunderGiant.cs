using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929100000_RemoveThunderGiant")]
public sealed class RemoveThunderGiant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE PlayerProfiles SET OwnedCards = (
                SELECT json_group_array(json(value))
                FROM json_each(PlayerProfiles.OwnedCards)
                WHERE json_extract(value, '$.CardId') <> '19222426'
            ), OwnedArtIds = (
                SELECT json_group_array(value)
                FROM json_each(PlayerProfiles.OwnedArtIds)
                WHERE value <> '19222426'
            );
            """);

        foreach (string column in new[] { "MainDeck", "ExtraDeck" })
        {
            migrationBuilder.Sql($$"""
                UPDATE PlayerDecks SET {{column}} = (
                    SELECT json_group_array(json(value))
                    FROM json_each(PlayerDecks.{{column}})
                    WHERE json_extract(value, '$.CardId') <> '19222426'
                );
                """);
        }

        migrationBuilder.Sql("""
            UPDATE Gifts SET Cards = (
                SELECT json_group_array(json(value))
                FROM json_each(Gifts.Cards)
                WHERE json_extract(value, '$.CardId') <> '19222426'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted player possessions cannot be reconstructed.
    }
}
