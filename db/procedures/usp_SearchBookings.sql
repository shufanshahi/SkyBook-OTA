CREATE OR ALTER PROCEDURE dbo.usp_SearchBookings
    @AgentId INT = NULL, @Status VARCHAR(20) = NULL, @Pnr CHAR(6) = NULL,
    @FromDate DATETIME2 = NULL, @ToDate DATETIME2 = NULL,
    @Page INT = 1, @PageSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Id, b.Pnr, a.AgencyName, b.Supplier, b.Status, b.TotalFare, b.CreatedAt,
           COUNT(*) OVER() AS TotalCount
    FROM dbo.Bookings b
    JOIN dbo.Agents a ON a.Id = b.AgentId
    WHERE (@AgentId IS NULL OR b.AgentId = @AgentId)
      AND (@Status  IS NULL OR b.Status  = @Status)
      AND (@Pnr     IS NULL OR b.Pnr     = @Pnr)
      AND (@FromDate IS NULL OR b.CreatedAt >= @FromDate)
      AND (@ToDate   IS NULL OR b.CreatedAt <  @ToDate)
    ORDER BY b.CreatedAt DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);
END
