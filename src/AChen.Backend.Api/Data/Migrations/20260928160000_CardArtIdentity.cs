using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928160000_CardArtIdentity")]
public sealed class CardArtIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OwnedArtIds", table: "PlayerProfiles", type: "TEXT", nullable: false, defaultValue: "[]");

        // Capture the exact artwork IDs before folding alternate IDs into rule IDs.
        migrationBuilder.Sql("""
            UPDATE PlayerProfiles SET OwnedArtIds = COALESCE((
                SELECT json_group_array(ArtId) FROM (
                    SELECT DISTINCT json_extract(value, '$.CardId') AS ArtId
                    FROM json_each(PlayerProfiles.OwnedCards)
                )
            ), '[]');
            """);
        migrationBuilder.Sql("""
            UPDATE PlayerProfiles SET OwnedCards = COALESCE((
                SELECT json_group_array(json_object('CardId', CardId, 'Rarity', Rarity, 'Count', Count))
                FROM (
                    SELECT CASE json_extract(value, '$.CardId')
                        WHEN '14558128' THEN '14558127'
                        WHEN '94145022' THEN '94145021'
                        ELSE json_extract(value, '$.CardId') END AS CardId,
                        CAST(json_extract(value, '$.Rarity') AS INTEGER) AS Rarity,
                        SUM(CAST(json_extract(value, '$.Count') AS INTEGER)) AS Count
                    FROM json_each(PlayerProfiles.OwnedCards)
                    GROUP BY 1, 2 ORDER BY 1, 2
                )
            ), '[]');
            """);
        foreach (string column in new[] { "MainDeck", "ExtraDeck" })
        {
            migrationBuilder.Sql($$"""
                UPDATE PlayerDecks SET {{column}} = COALESCE((
                    SELECT json_group_array(json_object('CardId', CardId, 'Rarity', Rarity, 'Count', Count))
                    FROM (
                        SELECT CASE json_extract(value, '$.CardId')
                            WHEN '14558128' THEN '14558127'
                            WHEN '94145022' THEN '94145021'
                            ELSE json_extract(value, '$.CardId') END AS CardId,
                            CAST(json_extract(value, '$.Rarity') AS INTEGER) AS Rarity,
                            SUM(CAST(json_extract(value, '$.Count') AS INTEGER)) AS Count
                        FROM json_each(PlayerDecks.{{column}})
                        GROUP BY 1, 2 ORDER BY 1, 2
                    )
                ), '[]');
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "OwnedArtIds", table: "PlayerProfiles");
}
