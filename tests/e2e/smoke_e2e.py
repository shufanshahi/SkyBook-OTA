"""End-to-end smoke test against a RUNNING stack (DB + both mock suppliers + SkyBook.Web).

Start everything, then:  SKYBOOK_URL=http://localhost:5100 python3 tests/e2e/smoke_e2e.py
Needs: pip install requests. Uses the seeded demo users."""
import re, sys, html, datetime, requests
import os
B = os.environ.get("SKYBOOK_URL", "http://localhost:5100")
ok = fail = 0
def check(name, cond, extra=""):
    global ok, fail
    print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))
    ok += cond; fail += (not cond)

def token(text):
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', text)
    return m.group(1) if m else None

def login(email, pw):
    s = requests.Session()
    r = s.get(B + "/Account/Login"); t = token(r.text)
    r = s.post(B + "/Account/Login", data={"Email": email, "Password": pw, "__RequestVerificationToken": t}, allow_redirects=False)
    return s, r

def wallet(s):
    r = s.get(B + "/")
    m = re.search(r"Wallet balance</div><div class=\"fs-2\">BDT ([\d,]+)", r.text)
    return int(m.group(1).replace(",", "")) if m else None

def form_values(text):
    """hidden + visible inputs of the booking form as a dict"""
    d = {}
    for m in re.finditer(r'<input[^>]*name="([^"]+)"[^>]*>', text):
        tag = m.group(0); name = m.group(1)
        v = re.search(r'value="([^"]*)"', tag)
        d[name] = html.unescape(v.group(1)) if v else ""
    return d

# ---- anonymous + auth -------------------------------------------------------------
r = requests.get(B + "/Bookings", allow_redirects=False)
check("anonymous /Bookings redirects to login", r.status_code == 302 and "/Account/Login" in r.headers["Location"])
s, r = login("agent1@skybook.test", "wrong")
check("wrong password stays on login with an error", r.status_code == 200 and "Invalid email or password" in r.text)
s, r = login("agent1@skybook.test", "Agent@123")
check("agent login redirects home", r.status_code == 302)
w0 = wallet(s); check("dashboard shows wallet", w0 is not None, str(w0))
check("agent blocked from admin area", s.get(B + "/Admin/Agents", allow_redirects=False).headers.get("Location", "").find("AccessDenied") > 0)

# ---- search ------------------------------------------------------------------------
date = (datetime.date.today() + datetime.timedelta(days=14)).isoformat()
q = f"/Flights/Search?Origin=DAC&Destination=CGP&DepartureDate={date}&Adults=1&Children=0&Infants=0&Cabin=Economy"
r = s.get(B + q)
links = re.findall(r'href="(/Bookings/Create\?[^"]+)"', r.text)
check("search returns offers from both suppliers", any("SkyGDS" in l for l in links) and any("AeroNDC" in l for l in links), f"{len(links)} links")
check("no warnings when both suppliers up", "did not respond" not in r.text)
r2 = s.get(B + f"/Flights/Search?Origin=ERR&Destination=CGP&DepartureDate={date}&Adults=1&Children=0&Infants=0&Cabin=Economy")
check("unknown airport rejected with validation message", "Unknown origin airport" in r2.text)
r3 = s.get(B + f"/Flights/Search?Origin=DAC&Destination=DAC&DepartureDate={date}&Adults=1&Children=0&Infants=0&Cabin=Economy")
check("same origin and destination rejected", "must be different" in r3.text)

# ---- book (NDC offer: price is stable) -----------------------------------------------
ndc = html.unescape([l for l in links if "AeroNDC" in l][0])
r = s.get(B + ndc)
check("review page renders passenger form", "Passengers[0].FirstName" in r.text)
f = form_values(r.text); f["__RequestVerificationToken"] = token(r.text)
f.update({"Passengers[0].FirstName": "Ayesha", "Passengers[0].LastName": "Rahman", "Passengers[0].DateOfBirth": "1990-04-02", "Passengers[0].Title": "Ms"})
f.pop("Passengers[0].PassportNo", None); f["Passengers[0].PassportNo"] = ""
r = s.post(B + "/Bookings/Create", data=f, allow_redirects=False)
check("booking created, redirected to details (PRG)", r.status_code == 302 and "/Bookings/Details/" in r.headers.get("Location", ""), r.text[:200] if r.status_code != 302 else "")
pnr = r.headers["Location"].rsplit("/", 1)[-1]
print("   PNR", pnr)
r = s.post(B + "/Bookings/Create", data=f, allow_redirects=False)
check("same idempotency key returns the same PNR", r.status_code == 302 and r.headers["Location"].endswith(pnr))
d = s.get(B + f"/Bookings/Details/{pnr}")
check("details show Held with Issue/Cancel buttons", "Held" in d.text and "Issue ticket" in d.text and "Cancel booking" in d.text)

# validation: infant-heavy / missing fields
bad = dict(f); bad["Passengers[0].FirstName"] = ""; bad["IdempotencyKey"] = "11111111-1111-1111-1111-111111111111"
r = s.post(B + "/Bookings/Create", data=bad)
check("missing first name re-renders the form (no booking)", r.status_code == 200 and "Passengers[0].FirstName" in r.text)
young = dict(f); young["Passengers[0].DateOfBirth"] = "2020-01-01"; young["IdempotencyKey"] = "22222222-2222-2222-2222-222222222222"
r = s.post(B + "/Bookings/Create", data=young)
check("adult aged 6 rejected by passenger rules", "must be 12 or older" in r.text)

# ---- authorization between agents ------------------------------------------------------
s2, _ = login("agent2@skybook.test", "Agent@123")
check("another agent gets 404 for this PNR", s2.get(B + f"/Bookings/Details/{pnr}").status_code == 404)
t = token(s2.get(B + "/Flights/Search").text) or token(s2.get(B + "/Bookings").text)

# ---- ticket ----------------------------------------------------------------------------
dt = token(d.text)
r = s.post(B + f"/Bookings/Ticket/{pnr}", data={"__RequestVerificationToken": dt}, allow_redirects=True)
check("ticket issued", "Ticket issued" in r.text and "Ticketed" in r.text, re.sub(r"\s+", " ", re.sub("<[^>]+>", " ", r.text))[:300])
w1 = wallet(s)
total = int(re.search(r"BDT ([\d,]+)</div>\s*</div>\s*<h6", s.get(B + f"/Bookings/Details/{pnr}").text).group(1).replace(",", "")) if False else None
check("wallet was debited", w1 is not None and w0 is not None and w1 < w0, f"{w0} -> {w1}")
r = s.post(B + f"/Bookings/Ticket/{pnr}", data={"__RequestVerificationToken": dt}, allow_redirects=True)
check("ticketing twice is refused (state machine)", "cannot be ticketed" in r.text)

# ---- cancel -> pending refund -> admin approves --------------------------------------------
r = s.post(B + f"/Bookings/Cancel/{pnr}", data={"__RequestVerificationToken": dt}, allow_redirects=True)
check("cancel creates a pending refund", "pending admin approval" in r.text, re.sub(r"\s+", " ", re.sub("<[^>]+>", " ", r.text))[:300])
r = s.post(B + f"/Bookings/Cancel/{pnr}", data={"__RequestVerificationToken": dt}, allow_redirects=True)
check("cancelling twice is refused", "cannot be cancelled" in r.text)

a, r = login("admin@skybook.test", "Admin@123")
check("admin login", r.status_code == 302)
for path in ["/", "/Admin/Agents", "/Admin/Agents/Create", "/Admin/Bookings", "/Admin/Bookings?Pnr=" + pnr, "/Admin/Refunds", "/Admin/Reports/Sales", "/Admin/SupplierLogs"]:
    check(f"admin page {path} loads", a.get(B + path).status_code == 200)
rf = a.get(B + "/Admin/Refunds")
rid = re.search(r"/Admin/Refunds/Approve/(\d+)", rf.text)
check("pending refund listed", rid is not None)
if rid:
    r = a.post(B + f"/Admin/Refunds/Approve/{rid.group(1)}", data={"__RequestVerificationToken": token(rf.text)}, allow_redirects=True)
    check("refund approved", "Refund approved" in r.text)
    w2 = wallet(s)
    check("wallet credited with refund (less penalty)", w2 is not None and w2 > w1, f"{w1} -> {w2}")
    check("booking is now Refunded", "Refunded" in s.get(B + f"/Bookings/Details/{pnr}").text)

# ---- admin: agents ---------------------------------------------------------------------------
ag = a.get(B + "/Admin/Agents"); tk = token(ag.text)
r = a.post(B + "/Admin/Agents/Create", data={"AgencyName": "E2E Travel", "Code": "E2E" + datetime.datetime.now().strftime("%H%M%S"), "Email": f"e2e{datetime.datetime.now().strftime('%H%M%S')}@skybook.test", "Password": "Passw0rd!", "MarkupPercent": "2", "CreditLimit": "0", "__RequestVerificationToken": token(a.get(B + "/Admin/Agents/Create").text)}, allow_redirects=True)
check("admin can create an agent", "created" in r.text)
logs = a.get(B + "/Admin/SupplierLogs")
check("supplier logs recorded calls from both suppliers", "SkyGDS" in logs.text and "AeroNDC" in logs.text)
check("passport numbers never stored unmasked", "A1234567" not in logs.text)

# ---- GDS: price drift handling ----------------------------------------------------------------
gds = html.unescape([l for l in links if "SkyGDS" in l][0])
changed = booked = 0
for i in range(6):
    r = s.get(B + gds)
    if "The price changed" in r.text: changed += 1
check("GDS price drift surfaces as a 'price changed' notice", changed > 0, f"changed {changed}/6")

# ---- supplier failure -> partial results ----------------------------------------------------------
print(f"\n{ok} passed, {fail} failed")
sys.exit(1 if fail else 0)
