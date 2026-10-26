CREATE OR ALTER PROCEDURE dbo.usp_AgentSalesReport
    @FromDate DATETIME2 = NULL,
    @ToDate   DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.Id AS AgentId,
           a.AgencyName,
           DATEFROMPARTS(YEAR(w.CreatedAt), MONTH(w.CreatedAt), 1) AS [Month],
           SUM(CASE WHEN w.Type = 'TicketDebit'  THEN 1 ELSE 0 END)        AS BookingsTicketed,
           SUM(CASE WHEN w.Type = 'TicketDebit'  THEN -w.Amount ELSE 0 END) AS GrossSales,
           SUM(CASE WHEN w.Type = 'RefundCredit' THEN  w.Amount ELSE 0 END) AS Refunds,
           SUM(CASE WHEN w.Type = 'TicketDebit'  THEN -w.Amount
                    WHEN w.Type = 'RefundCredit' THEN -w.Amount ELSE 0 END) AS NetSales
    FROM dbo.WalletTransactions w
    JOIN dbo.Agents a ON a.Id = w.AgentId
    WHERE w.Type IN ('TicketDebit', 'RefundCredit')
      AND (@FromDate IS NULL OR w.CreatedAt >= @FromDate)
      AND (@ToDate   IS NULL OR w.CreatedAt <  @ToDate)
    GROUP BY a.Id, a.AgencyName, DATEFROMPARTS(YEAR(w.CreatedAt), MONTH(w.CreatedAt), 1)
    ORDER BY [Month] DESC, a.AgencyName;
END
