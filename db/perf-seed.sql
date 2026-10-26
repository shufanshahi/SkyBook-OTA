-- Seeds ~100k bookings spread across the existing agents (for the index experiment).
-- Run with: sqlcmd -I ... -i db/perf-seed.sql   (-I = QUOTED_IDENTIFIER ON, required by the filtered index)
SET NOCOUNT ON;
DECLARE @agents TABLE (rn INT IDENTITY(1,1), Id INT);
INSERT @agents(Id) SELECT Id FROM dbo.Agents;
DECLARE @n INT = (SELECT COUNT(*) FROM @agents);

;WITH nums AS (
    SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.Bookings (Pnr, AgentId, Supplier, SupplierRef, Status, TotalFare, Currency, OfferId, IdempotencyKey, CreatedAt)
SELECT CONVERT(CHAR(6), 100000 + n),
       (SELECT Id FROM @agents WHERE rn = (n % @n) + 1),
       CASE WHEN n % 2 = 0 THEN 'SkyGDS' ELSE 'AeroNDC' END,
       'REF' + CONVERT(VARCHAR(10), n),
       CASE n % 5 WHEN 0 THEN 'Held' WHEN 1 THEN 'Ticketed' WHEN 2 THEN 'Cancelled' WHEN 3 THEN 'Refunded' ELSE 'Expired' END,
       2000 + (n % 50) * 100, 'BDT', 'perf', NEWID(),
       DATEADD(MINUTE, -n, SYSUTCDATETIME())
FROM nums;
