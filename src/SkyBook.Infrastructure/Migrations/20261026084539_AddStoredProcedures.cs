using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyBook.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "procedures");
            foreach (var file in Directory.GetFiles(dir, "*.sql").OrderBy(f => f))
                migrationBuilder.Sql(File.ReadAllText(file));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sp in new[] { "usp_IssueTicket", "usp_CancelBooking", "usp_SearchBookings", "usp_AgentSalesReport" })
                migrationBuilder.Sql($"DROP PROCEDURE IF EXISTS dbo.{sp}");
        }
    }
}
