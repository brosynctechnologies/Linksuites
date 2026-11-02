using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School23CbtBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false),
                    marks = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_questions_levels_level_id",
                        column: x => x.level_id,
                        principalTable: "levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bank_questions_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_questions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cbt_papers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    exam_sitting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    open_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    close_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assessment_component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    draw_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cbt_papers", x => x.id);
                    table.ForeignKey(
                        name: "fk_cbt_papers_assessment_components_assessment_component_id",
                        column: x => x.assessment_component_id,
                        principalTable: "assessment_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cbt_papers_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_papers_exam_sittings_exam_sitting_id",
                        column: x => x.exam_sitting_id,
                        principalTable: "exam_sittings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cbt_papers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_papers_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cbt_paper_picks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cbt_paper_picks", x => x.id);
                    table.ForeignKey(
                        name: "fk_cbt_paper_picks_bank_questions_bank_question_id",
                        column: x => x.bank_question_id,
                        principalTable: "bank_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cbt_paper_picks_cbt_papers_paper_id",
                        column: x => x.paper_id,
                        principalTable: "cbt_papers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cbt_paper_picks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_questions_level_id",
                table: "bank_questions",
                column: "level_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_questions_subject_id_level_id",
                table: "bank_questions",
                columns: new[] { "subject_id", "level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_questions_tenant_id",
                table: "bank_questions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_paper_picks_bank_question_id",
                table: "cbt_paper_picks",
                column: "bank_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_paper_picks_paper_id_bank_question_id",
                table: "cbt_paper_picks",
                columns: new[] { "paper_id", "bank_question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cbt_paper_picks_tenant_id",
                table: "cbt_paper_picks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_papers_assessment_component_id",
                table: "cbt_papers",
                column: "assessment_component_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_papers_class_subject_id_term_id",
                table: "cbt_papers",
                columns: new[] { "class_subject_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cbt_papers_exam_sitting_id",
                table: "cbt_papers",
                column: "exam_sitting_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_papers_tenant_id",
                table: "cbt_papers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cbt_papers_term_id",
                table: "cbt_papers",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cbt_paper_picks");

            migrationBuilder.DropTable(
                name: "bank_questions");

            migrationBuilder.DropTable(
                name: "cbt_papers");
        }
    }
}
