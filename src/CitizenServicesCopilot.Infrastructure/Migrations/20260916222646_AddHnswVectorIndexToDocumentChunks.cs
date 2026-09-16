using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CitizenServicesCopilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHnswVectorIndexToDocumentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks");
        }
    }
}
