using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderLinkWave4 : Migration
    {
        /// <summary>
        /// Plans are seeded insert-only, so the existing OrderLink plans get the wave 4 limits here (when missing): <c>inventory</c> and
        /// <c>online_payments</c> follow the plan's remove-branding limit (paid plans have it), <c>team_seats</c> is 1 on free plans and 3 on
        /// paid ones; Pro gets the three feature lines. Subscriptions keep a snapshot of their plan's limits, so a snapshot without the keys
        /// gets them the same way, so paying stores get the new features. Safe to run more than once.
        /// </summary>
        public const string PlansSql = """
            UPDATE plans SET limits = limits
                || jsonb_build_object('inventory', CASE WHEN COALESCE((limits->>'remove_branding')::bigint, 0) <> 0 THEN 1 ELSE 0 END)
                || jsonb_build_object('online_payments', CASE WHEN COALESCE((limits->>'remove_branding')::bigint, 0) <> 0 THEN 1 ELSE 0 END)
                || jsonb_build_object('team_seats', CASE WHEN COALESCE((limits->>'remove_branding')::bigint, 0) <> 0 THEN 3 ELSE 1 END)
            WHERE product = 'whatsapp-orders' AND NOT (limits ? 'inventory' AND limits ? 'online_payments' AND limits ? 'team_seats');
            UPDATE plans SET features = array_append(features, 'Stock tracking and low-stock alerts')
            WHERE product = 'whatsapp-orders' AND COALESCE((limits->>'inventory')::bigint, 0) <> 0 AND NOT ('Stock tracking and low-stock alerts' = ANY (features));
            UPDATE plans SET features = array_append(features, 'Online payments to your Paystack')
            WHERE product = 'whatsapp-orders' AND COALESCE((limits->>'online_payments')::bigint, 0) <> 0 AND NOT ('Online payments to your Paystack' = ANY (features));
            UPDATE plans SET features = array_append(features, 'Team of up to 3')
            WHERE product = 'whatsapp-orders' AND COALESCE((limits->>'team_seats')::bigint, 0) = 3 AND NOT ('Team of up to 3' = ANY (features));
            UPDATE subscriptions s SET limits = s.limits
                || jsonb_build_object('inventory', CASE WHEN COALESCE((s.limits->>'remove_branding')::bigint, 0) <> 0 THEN 1 ELSE 0 END)
                || jsonb_build_object('online_payments', CASE WHEN COALESCE((s.limits->>'remove_branding')::bigint, 0) <> 0 THEN 1 ELSE 0 END)
                || jsonb_build_object('team_seats', CASE WHEN COALESCE((s.limits->>'remove_branding')::bigint, 0) <> 0 THEN 3 ELSE 1 END)
            FROM plans p
            WHERE p.id = s.plan_id AND p.product = 'whatsapp-orders' AND s.limits IS NOT NULL
              AND NOT (s.limits ? 'inventory' AND s.limits ? 'online_payments' AND s.limits ? 'team_seats');
            """;

        /// <summary>
        /// Orders placed before this release took stock from tracked products at placement; mark those lines so a later cancel gives it
        /// back (the product is taken as tracked then if it is tracked now).
        /// </summary>
        public const string OrderItemsSql = """
            UPDATE order_items i SET product_stock_taken = TRUE
            FROM orders o, products p
            WHERE o.id = i.order_id AND p.id = i.product_id AND o.status <> 'Cancelled' AND p.stock_qty IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "low_stock_threshold",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "paystack_previous_key_protected",
                table: "stores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paystack_secret_key_protected",
                table: "stores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_key_until",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "low_stock_alerted",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "low_stock_alerted",
                table: "product_variants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "stock_qty",
                table: "product_variants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "product_stock_taken",
                table: "order_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "variant_stock_taken",
                table: "order_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "order_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    charged_amount_minor = table.Column<long>(type: "bigint", nullable: true),
                    charged_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rechecked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    variant_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    quantity_after = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_order_id",
                table: "order_payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_reference",
                table: "order_payments",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_status_created_at",
                table: "order_payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_tenant_id_order_id",
                table: "order_payments",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_created_at",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_product_id_created_at",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "product_id", "created_at" });

            migrationBuilder.Sql(PlansSql);
            migrationBuilder.Sql(OrderItemsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_payments");

            migrationBuilder.DropTable(
                name: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "low_stock_threshold",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "paystack_previous_key_protected",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "paystack_secret_key_protected",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "previous_key_until",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "low_stock_alerted",
                table: "products");

            migrationBuilder.DropColumn(
                name: "low_stock_alerted",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "stock_qty",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "product_stock_taken",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "variant_stock_taken",
                table: "order_items");
        }
    }
}
