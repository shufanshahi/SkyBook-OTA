CREATE OR ALTER PROCEDURE dbo.usp_CancelBooking
    @BookingId BIGINT,
    @AgentId   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @Fare DECIMAL(18,2), @Status VARCHAR(20);

    SELECT @Fare = TotalFare, @Status = Status
    FROM dbo.Bookings WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @BookingId AND AgentId = @AgentId;

    IF @Status IS NULL                          THROW 50001, 'Booking not found.', 1;
    IF @Status NOT IN ('Held', 'Ticketed')      THROW 50004, 'Booking cannot be cancelled from its current status.', 1;

    IF @Status = 'Ticketed'
    BEGIN
        -- penalty: 10% of the fare, minimum BDT 500, never more than the fare
        DECLARE @Penalty DECIMAL(18,2) = CASE WHEN @Fare * 0.10 < 500 THEN 500 ELSE @Fare * 0.10 END;
        IF @Penalty > @Fare SET @Penalty = @Fare;

        INSERT dbo.Refunds (BookingId, RefundAmount, Penalty, Status, ProcessedAt)
        VALUES (@BookingId, @Fare - @Penalty, @Penalty, 'Pending', NULL);
    END

    UPDATE dbo.Bookings SET Status = 'Cancelled' WHERE Id = @BookingId;

    COMMIT;
END
