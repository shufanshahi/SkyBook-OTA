# Booking state machine

```
 Search ─► Price ─► [Book] ─► HELD ──[Issue ticket]──► TICKETED ──[Cancel]──► CANCELLED ──[Admin approves refund]──► REFUNDED
                                │                                                ▲
                                ├──[Cancel: nothing was charged, no refund]──────┘
                                └──[hold expires, background job]──► EXPIRED
```

| From | Allowed next states |
|---|---|
| Held | Ticketed, Cancelled, Expired |
| Ticketed | Cancelled |
| Cancelled | Refunded |
| Refunded, Expired | none (terminal) |

Single source of truth: `BookingTransitions.CanMove` in `SkyBook.Domain`. `Booking.MoveTo` throws a
`DomainException` on an illegal move, and the booking details view only shows the *Issue ticket* /
*Cancel booking* buttons when `CanMove` allows them.

## Money rules
- **Hold**: 30 minutes, nothing is charged. `HoldExpiryService` expires overdue holds and cancels them at the supplier.
- **Ticket**: `usp_IssueTicket` debits the wallet and sets `Ticketed` in one transaction (`UPDLOCK` on the agent row), so parallel requests cannot overspend. Wallet + credit limit must cover the fare.
- **Cancel a ticketed booking**: `usp_CancelBooking` creates a *pending* refund. Penalty = 10% of the fare, minimum BDT 500, never more than the fare.
- **Refund**: an admin approves it; only then is the wallet credited and the booking set to `Refunded`.

## Supplier ordering trade-off
Ticketing calls the supplier first, then the database. If the database step fails after the airline ticketed,
a `RECONCILE` critical log is written (and the user is told to quote the PNR) instead of silently losing the ticket.
