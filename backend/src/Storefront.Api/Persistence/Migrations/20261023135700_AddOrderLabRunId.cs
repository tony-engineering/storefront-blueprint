using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLabRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LabRunId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_LabRunId",
                table: "Orders",
                column: "LabRunId",
                filter: "\"LabRunId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_LabRunId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LabRunId",
                table: "Orders");
        }
    }
}
