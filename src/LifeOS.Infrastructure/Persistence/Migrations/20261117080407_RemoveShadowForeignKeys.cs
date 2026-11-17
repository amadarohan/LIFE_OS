using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShadowForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiExecutionLogs_AiActions_AiActionId1",
                table: "AiExecutionLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_DecisionReviews_Decisions_DecisionId1",
                table: "DecisionReviews");

            migrationBuilder.DropIndex(
                name: "IX_DecisionReviews_DecisionId1",
                table: "DecisionReviews");

            migrationBuilder.DropIndex(
                name: "IX_AiExecutionLogs_AiActionId1",
                table: "AiExecutionLogs");

            migrationBuilder.DropColumn(
                name: "DecisionId1",
                table: "DecisionReviews");

            migrationBuilder.DropColumn(
                name: "AiActionId1",
                table: "AiExecutionLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DecisionId1",
                table: "DecisionReviews",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AiActionId1",
                table: "AiExecutionLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionReviews_DecisionId1",
                table: "DecisionReviews",
                column: "DecisionId1");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionLogs_AiActionId1",
                table: "AiExecutionLogs",
                column: "AiActionId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AiExecutionLogs_AiActions_AiActionId1",
                table: "AiExecutionLogs",
                column: "AiActionId1",
                principalTable: "AiActions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DecisionReviews_Decisions_DecisionId1",
                table: "DecisionReviews",
                column: "DecisionId1",
                principalTable: "Decisions",
                principalColumn: "Id");
        }
    }
}
