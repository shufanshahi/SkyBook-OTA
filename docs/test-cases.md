# Manual test cases

Run against `docker compose up --build` (http://localhost:5000). Demo logins: `agent1@skybook.test / Agent@123`
(Sky Travels, 3% markup), `agent2@skybook.test / Agent@123` (Blue Horizon, wallet BDT 5,000), `admin@skybook.test / Admin@123`.
Mock triggers: origin `ERR` → GDS SOAP fault, destination `SLW` → GDS 8 s delay (times out), GDS re-price drifts 50% of the time.
"Automated" names the test that also covers the case (`I` = `tests/SkyBook.IntegrationTests`, `U` = `tests/SkyBook.UnitTests`, `E` = `tests/e2e/smoke_e2e.py`).

| ID | Scenario | Steps | Expected | Result | Automated |
|---|---|---|---|---|---|
| TC-01 | One-way search DAC→CGP | Log in as agent1, search DAC→CGP in 14 days | Offers from AeroNDC and SkyGDS, sorted by price, no warning | Pass | E |
| TC-02 | Markup applied | Same search as agent1 (3%) and agent2 (5%) | Same flight is more expensive for agent2 | Pass | U, I |
| TC-03 | Same origin and destination | Search DAC→DAC | Validation message "must be different", no results | Pass | E |
| TC-04 | Unknown airport | Origin `ERR` typed in URL | "Unknown origin airport" | Pass | E |
| TC-05 | Past date | Search with yesterday's date | "Departure date cannot be in the past" | Not run | |
| TC-06 | More infants than adults | 1 adult, 2 infants | "cannot be more infants than adults" | Not run | |
| TC-07 | Supplier timeout | Search DAC→SLW | AeroNDC offers only + SkyGDS warning banner, page < 7 s | Pass | U |
| TC-08 | Supplier fault | Search from a GDS-failing origin | Other supplier's offers still shown + warning | Pass | U |
| TC-09 | Book one adult | Select an AeroNDC offer, fill passenger, Book | Redirect to details, status Held, PNR shown, 30-min hold | Pass | I, E |
| TC-10 | Missing passenger name | Book with empty first name | Form re-renders with error, no booking | Pass | I, E |
| TC-11 | Adult too young | Adult with a 2020 date of birth | "must be 12 or older" | Pass | U, E |
| TC-12 | Price changed before booking | Repeat selecting a SkyGDS offer until it re-prices higher | Alert shows old/new fare; booking only after confirm | Pass | E |
| TC-13 | International needs passport | Book DAC→DEL without passport | Passenger rule error | Pass | U |
| TC-14 | Issue ticket | Details of a Held booking, Issue ticket | Status Ticketed, ticket number shown, wallet reduced by fare | Pass | I, E |
| TC-15 | Ticket with insufficient balance | agent2 books a fare above BDT 5,000, Issue ticket | Red alert "Insufficient wallet balance"; booking stays Held | Pass | I |
| TC-16 | Ticket twice | Resubmit the ticket form | Error, wallet charged once | Pass | I, E |
| TC-17 | Concurrent ticketing | 5 parallel ticket requests, wallet for 2 | Exactly 2 ticketed, wallet never negative | Pass | I |
| TC-18 | Double-click Book | Click Book twice quickly | Only one PNR created | Pass | I |
| TC-19 | Cancel a held booking | Cancel | Status Cancelled, no refund row | Pass | I |
| TC-20 | Cancel already-cancelled booking | Resubmit cancel | Error alert; no Cancel button shown | Pass | E |
| TC-21 | Cancel ticketed booking | Cancel a Ticketed booking | Pending refund (fare − max(10%, 500)) shown | Pass | I, E |
| TC-22 | Admin approves refund | Admin → Refunds → Approve | Wallet credited, booking Refunded | Pass | I, E |
| TC-23 | Hold expiry | Hold older than 30 min (set `HoldExpiresAt` in the past), wait one job tick | Status Expired, supplier booking cancelled | Not run | |
| TC-24 | Agent opens another agent's PNR | Edit the PNR in the URL as agent2 | 404 page | Pass | I, E |
| TC-25 | Anonymous access | Open /Bookings logged out | Redirect to login | Pass | I, E |
| TC-26 | Agent opens admin area | agent1 opens /Admin/Agents | Access denied page | Pass | I, E |
| TC-27 | Open redirect | Log in with `?ReturnUrl=https://evil.example` | Lands on dashboard, not external site | Pass | I, U |
| TC-28 | Create agent | Admin → Agents → New, valid data | Agent created, can sign in | Pass | E |
| TC-29 | Suspend agent | Admin suspends agent2, agent2 searches | "agency is suspended" message | Pass | U |
| TC-30 | Supplier logs | Admin → Supplier logs after a search | Rows for both suppliers; passport numbers masked | Pass | E |
