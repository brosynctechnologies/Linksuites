using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School24CbtAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_key_at",
                table: "cbt_papers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cbt_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_marks = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false, defaultValue: 0m),
                    auto_marked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    released_key_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cbt_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_cbt_attempts_cbt_papers_paper_id",
                        column: x => x.paper_id,
                        principalTable: "cbt_papers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_attempts_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_attempts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cbt_attempt_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    stem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    option_order = table.Column<string>(type: "jsonb", nullable: false),
                    correct_shown_index = table.Column<int>(type: "integer", nullable: false),
                    marks = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    chosen_index = table.Column<int>(type: "integer", nullable: true),
                    marks_awarded = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cbt_attempt_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_cbt_attempt_items_bank_questions_bank_question_id",
                        column: x => x.bank_question_id,
                        principalTable: "bank_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cbt_attempt_items_cbt_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "cbt_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_attempt_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempt_items_attempt_id_bank_question_id",
                table: "cbt_attempt_items",
                columns: new[] { "attempt_id", "bank_question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempt_items_attempt_id_order",
                table: "cbt_attempt_items",
                columns: new[] { "attempt_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempt_items_bank_question_id",
                table: "cbt_attempt_items",
                column: "bank_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempt_items_tenant_id",
                table: "cbt_attempt_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempts_enrolment_id",
                table: "cbt_attempts",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempts_paper_id_enrolment_id",
                table: "cbt_attempts",
                columns: new[] { "paper_id", "enrolment_id" },
                unique: true,
                filter: "submitted_at IS NULL AND reopened_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_attempts_tenant_id",
                table: "cbt_attempts",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cbt_attempt_items");

            migrationBuilder.DropTable(
                name: "cbt_attempts");

            migrationBuilder.DropColumn(
                name: "released_key_at",
                table: "cbt_papers");
        }
    }
}
