using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School8ResultRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subject_results_class_subjects_class_subject_id",
                table: "subject_results");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_results_school_classes_class_id",
                table: "subject_results");

            migrationBuilder.DropForeignKey(
                name: "fk_term_results_school_classes_class_id",
                table: "term_results");

            migrationBuilder.AddForeignKey(
                name: "fk_subject_results_class_subjects_class_subject_id",
                table: "subject_results",
                column: "class_subject_id",
                principalTable: "class_subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_subject_results_school_classes_class_id",
                table: "subject_results",
                column: "class_id",
                principalTable: "school_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_term_results_school_classes_class_id",
                table: "term_results",
                column: "class_id",
                principalTable: "school_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subject_results_class_subjects_class_subject_id",
                table: "subject_results");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_results_school_classes_class_id",
                table: "subject_results");

            migrationBuilder.DropForeignKey(
                name: "fk_term_results_school_classes_class_id",
                table: "term_results");

            migrationBuilder.AddForeignKey(
                name: "fk_subject_results_class_subjects_class_subject_id",
                table: "subject_results",
                column: "class_subject_id",
                principalTable: "class_subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subject_results_school_classes_class_id",
                table: "subject_results",
                column: "class_id",
                principalTable: "school_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_term_results_school_classes_class_id",
                table: "term_results",
                column: "class_id",
                principalTable: "school_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
