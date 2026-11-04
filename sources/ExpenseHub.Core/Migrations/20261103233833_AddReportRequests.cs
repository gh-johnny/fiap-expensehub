using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseHub.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddReportRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRequests", x => x.Id);
                    table.CheckConstraint("CK_ReportRequests_Period", "FromDate <= ToDate");
                    table.CheckConstraint("CK_ReportRequests_Scope", "Scope IN ('Mine', 'Global')");
                    table.CheckConstraint("CK_ReportRequests_Status", "Status IN ('Pending', 'Processing', 'Completed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_ReportRequests_AspNetUsers_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_RequesterId_CreatedAtUtc",
                table: "ReportRequests",
                columns: new[] { "RequesterId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRequests");
        }
    }
}
