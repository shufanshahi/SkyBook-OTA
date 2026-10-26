CREATE OR ALTER PROCEDURE dbo.usp_IssueTicket
    @BookingId BIGINT,
    @AgentId   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @Fare DECIMAL(18,2), @Status VARCHAR(20), @Pnr CHAR(6);

    SELECT @Fare = TotalFare, @Status = Status, @Pnr = Pnr
    FROM dbo.Bookings WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @BookingId AND AgentId = @AgentId;

    IF @Status IS NULL      THROW 50001, 'Booking not found.', 1;
    IF @Status <> 'Held'    THROW 50002, 'Booking is not in Held status.', 1;

    DECLARE @Available DECIMAL(18,2);
    SELECT @Available = WalletBalance + CreditLimit
    FROM dbo.Agents WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @AgentId;

    IF @Available < @Fare   THROW 50003, 'Insufficient wallet balance.', 1;

    UPDATE dbo.Agents SET WalletBalance = WalletBalance - @Fare WHERE Id = @AgentId;

    INSERT dbo.WalletTransactions (AgentId, Amount, Type, Reference, CreatedAt)
    VALUES (@AgentId, -@Fare, 'TicketDebit', @Pnr, SYSUTCDATETIME());

    UPDATE dbo.Bookings SET Status = 'Ticketed' WHERE Id = @BookingId;

    COMMIT;
END
