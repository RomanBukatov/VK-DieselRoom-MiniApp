using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieselRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfficialCatalogAndIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Плитки меню: колонка переименовывается, а не пересоздаётся — данные сохраняются
            // (символы эмодзи заменяет на ключи иконок DbInitializer при старте)
            migrationBuilder.RenameColumn(
                name: "Emoji",
                table: "MenuLinks",
                newName: "Icon");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "MenuLinks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "ShopOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ShopOrders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "ShopItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ShopItems",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ShopOrders");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ShopItems");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "MenuLinks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.RenameColumn(
                name: "Icon",
                table: "MenuLinks",
                newName: "Emoji");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "ShopOrders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "ShopItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

        }
    }
}
