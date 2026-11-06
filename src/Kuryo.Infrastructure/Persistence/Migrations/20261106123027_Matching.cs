using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kuryo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Matching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "search_started_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Orders already in the table started searching when they were created.
            migrationBuilder.Sql("UPDATE orders SET search_started_at = created_at;");

            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    offered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    distance_km = table.Column<double>(type: "double precision", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_offers_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_offers_users_courier_id",
                        column: x => x.courier_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_positions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offers_one_pending_per_courier",
                table: "offers",
                column: "courier_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_offers_one_pending_per_order",
                table: "offers",
                column: "order_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_offers_order_id_courier_id",
                table: "offers",
                columns: new[] { "order_id", "courier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_status_expires_at",
                table: "offers",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_company_id",
                table: "positions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_courier_id_at",
                table: "positions",
                columns: new[] { "courier_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_order_id_at",
                table: "positions",
                columns: new[] { "order_id", "at" },
                filter: "order_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offers");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropColumn(
                name: "search_started_at",
                table: "orders");
        }
    }
}
