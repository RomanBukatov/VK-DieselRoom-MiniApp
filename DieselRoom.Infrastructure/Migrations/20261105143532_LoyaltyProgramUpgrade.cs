using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DieselRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyProgramUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdultConfirmedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAcceptedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentVersion",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastApproachAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastApproachTier",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileCompletedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AgeRestricted",
                table: "ShopOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AgeRestricted",
                table: "ShopItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "CodeWindow",
                table: "PrivilegeRedemptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventTitle",
                table: "Bookings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "AfishaEvents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EventTime",
                table: "AfishaEvents",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductLabel",
                table: "AfishaEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                });

            // Подарки только для совершеннолетних: алкоголь и кальян. Названия — из официального каталога,
            // выданные и действующие купоны получают тот же признак, чтобы официант увидел напоминание о паспорте.
            const string ageRestrictedTitles =
                "'Бокал пива', 'Бокал вина', 'Сет из 5 шотов водка-редбул', '1 л коктейля виски-кола', " +
                "'Вин Дизель Бургер + Бокал пива', 'Кальян на фрукте', 'Бутылка водочки «Хаски»', 'Сет «БИГ» + 1 литр пива'";
            migrationBuilder.Sql($"UPDATE \"ShopItems\" SET \"AgeRestricted\" = TRUE WHERE \"Title\" IN ({ageRestrictedTitles});");
            migrationBuilder.Sql($"UPDATE \"ShopOrders\" SET \"AgeRestricted\" = TRUE WHERE \"Title\" IN ({ageRestrictedTitles}, 'Бутылка Хаски 0.5');");

            // Карточки афиши, созданные прежним сидом: дата Нового года и ссылка на меню бизнес-ланча
            migrationBuilder.Sql("UPDATE \"AfishaEvents\" SET \"EventDate\" = DATE '2026-12-31' WHERE \"Title\" = 'НОВЫЙ ГОД В DIESEL ROOM' AND \"EventDate\" IS NULL;");
            migrationBuilder.Sql(
                "UPDATE \"AfishaEvents\" SET \"ProductUrl\" = 'https://vk.ru/album-34613849_258959228', \"ProductLabel\" = 'Меню бизнес-ланча' " +
                "WHERE \"Title\" = 'БИЗНЕС-ЛАНЧИ В DIESEL ROOM' AND \"ProductUrl\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "AdultConfirmedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConsentAcceptedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConsentVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastApproachAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastApproachTier",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfileCompletedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AgeRestricted",
                table: "ShopOrders");

            migrationBuilder.DropColumn(
                name: "AgeRestricted",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "CodeWindow",
                table: "PrivilegeRedemptions");

            migrationBuilder.DropColumn(
                name: "EventTitle",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "AfishaEvents");

            migrationBuilder.DropColumn(
                name: "EventTime",
                table: "AfishaEvents");

            migrationBuilder.DropColumn(
                name: "ProductLabel",
                table: "AfishaEvents");
        }
    }
}
