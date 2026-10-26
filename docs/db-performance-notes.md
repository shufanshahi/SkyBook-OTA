# Database performance notes

## Experiment: agent booking history

**Query** (what "My Bookings" runs): latest 20 bookings of one agent.

```sql
SELECT Pnr, Status, TotalFare, CreatedAt
FROM dbo.Bookings
WHERE AgentId = @AgentId
ORDER BY CreatedAt DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
```

**Data:** 100,000 bookings spread over the agents, seeded by `db/perf-seed.sql`.
Measured with `SET STATISTICS IO, TIME ON` on SQL Server 2022 (Docker).

| | Logical reads | CPU time | Plan |
|---|---|---|---|
| Without `IX_Bookings_Agent_Created` | **1,673** | 119 ms (first run, cold plan) | Clustered index scan of the whole table, then sort |
| With `IX_Bookings_Agent_Created (AgentId, CreatedAt DESC) INCLUDE (Pnr, Status, TotalFare)` | **3** | 1 ms | Index seek, rows already in the right order, no sort |

## What I learned

- Without an index on `AgentId` the query reads every page of `Bookings` (a scan) no matter how few rows the agent has, so cost grows with table size.
- Putting `CreatedAt DESC` in the index key means the rows come out already sorted, so SQL Server stops after 20 rows and does no sort step.
- `INCLUDE (Pnr, Status, TotalFare)` makes the index *covering*: no key lookups back into the clustered index.
- The filtered index `IX_Bookings_Status_HoldExpires` (`WHERE Status = 'Held'`) stays tiny because most bookings are not `Held`; the hold-expiry job only needs those rows.
- `usp_SearchBookings` is a "catch-all" query with optional parameters. `OPTION (RECOMPILE)` stops SQL Server reusing one cached plan that is wrong for other parameter combinations (parameter sniffing), at the cost of compiling each time. Fine for an admin screen, not for a hot path.
- Filtered indexes require `SET QUOTED_IDENTIFIER ON`. `sqlcmd` needs `-I` or inserts into `Bookings` fail with error 1934.

## How to reproduce

```bash
docker cp db/perf-seed.sql skybook-sql:/tmp/perf-seed.sql
docker exec skybook-sql /opt/mssql-tools18/bin/sqlcmd -I -C -S localhost -U sa -P '<password>' -d SkyBook -i /tmp/perf-seed.sql
# then DROP / CREATE INDEX IX_Bookings_Agent_Created and run the query with STATISTICS IO ON
# clean up afterwards:  DELETE FROM dbo.Bookings WHERE OfferId = 'perf';
```
