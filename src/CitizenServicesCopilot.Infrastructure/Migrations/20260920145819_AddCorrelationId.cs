using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CitizenServicesCopilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "WorkflowRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "AgentSteps",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_CorrelationId",
                table: "AgentSteps",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentSteps_CorrelationId",
                table: "AgentSteps");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AgentSteps");
        }
    }
}
