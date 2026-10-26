package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"sync"
	"time"
)

type Pax struct {
	Adt int `json:"adt"`
	Chd int `json:"chd"`
	Inf int `json:"inf"`
}

type SearchReq struct {
	Origin      string `json:"origin"`
	Destination string `json:"destination"`
	Date        string `json:"date"`
	Cabin       string `json:"cabin"`
	Pax         Pax    `json:"pax"`
}

type Offer struct {
	OfferID     string    `json:"offerId"`
	Carrier     string    `json:"carrier"`
	FlightNo    string    `json:"flightNo"`
	Origin      string    `json:"origin"`
	Destination string    `json:"destination"`
	Departure   time.Time `json:"departure"`
	Arrival     time.Time `json:"arrival"`
	BaseFare    float64   `json:"baseFare"`
	Taxes       float64   `json:"taxes"`
	Currency    string    `json:"currency"`
	Cabin       string    `json:"cabin"`
	Seats       int       `json:"seats"`
	Baggage     string    `json:"baggage"`
	Refundable  bool      `json:"refundable"`
	ExpiresAt   time.Time `json:"expiresAt"`
}

type Passenger struct {
	Type      string `json:"type"`
	FirstName string `json:"firstName"`
	LastName  string `json:"lastName"`
}

type OrderReq struct {
	OfferID    string      `json:"offerId"`
	Passengers []Passenger `json:"passengers"`
}

type Order struct {
	OrderID       string   `json:"orderId"`
	OfferID       string   `json:"offerId"`
	Pnr           string   `json:"pnr"`
	Status        string   `json:"status"` // HELD | TICKETED | CANCELLED
	Total         float64  `json:"total"`
	Currency      string   `json:"currency"`
	TicketNumbers []string `json:"ticketNumbers,omitempty"`
	PassengerN    int      `json:"-"`
}

// offers and orders are shared by concurrent request goroutines, so each map has its own lock.
var (
	offerMu sync.RWMutex
	offers  = map[string]Offer{}

	orderMu sync.RWMutex
	orders  = map[string]*Order{}
	nextSeq = 1000
)

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(v)
}

// NDC-style error body: {"errors":[{"code":"...","message":"..."}]}
func writeErr(w http.ResponseWriter, status int, code, msg string) {
	writeJSON(w, status, map[string]any{"errors": []map[string]string{{"code": code, "message": msg}}})
}

func search(w http.ResponseWriter, r *http.Request) {
	var req SearchReq
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeErr(w, http.StatusBadRequest, "INVALID_BODY", "request body is not valid JSON")
		return
	}
	if req.Origin == "" || req.Destination == "" || req.Date == "" {
		writeErr(w, http.StatusBadRequest, "INVALID_REQUEST", "origin, destination and date are required")
		return
	}
	if req.Cabin == "" {
		req.Cabin = "Economy"
	}

	// Test triggers
	if req.Origin == "ERR" {
		writeErr(w, http.StatusInternalServerError, "SUPPLIER_ERROR", "simulated upstream failure")
		return
	}
	if req.Destination == "SLW" {
		time.Sleep(8 * time.Second)
	}

	list, err := generateOffers(req, time.Now().UTC())
	if err != nil {
		writeErr(w, http.StatusBadRequest, "INVALID_DATE", err.Error())
		return
	}

	offerMu.Lock()
	for _, o := range list {
		offers[o.OfferID] = o
	}
	offerMu.Unlock()

	writeJSON(w, http.StatusOK, map[string]any{"offers": list})
}

// lookupOffer returns a stored, non-expired offer or writes the NDC error response itself.
func lookupOffer(w http.ResponseWriter, id string) (Offer, bool) {
	offerMu.RLock()
	o, ok := offers[id]
	offerMu.RUnlock()
	if !ok {
		writeErr(w, http.StatusNotFound, "OFFER_NOT_FOUND", "unknown offer "+id)
		return Offer{}, false
	}
	if time.Now().UTC().After(o.ExpiresAt) {
		writeErr(w, http.StatusConflict, "OFFER_EXPIRED", "offer "+id+" has expired, search again")
		return Offer{}, false
	}
	return o, true
}

func price(w http.ResponseWriter, r *http.Request) {
	o, ok := lookupOffer(w, r.PathValue("id"))
	if !ok {
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"offer": o})
}

func createOrder(w http.ResponseWriter, r *http.Request) {
	var req OrderReq
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil || req.OfferID == "" || len(req.Passengers) == 0 {
		writeErr(w, http.StatusBadRequest, "INVALID_REQUEST", "offerId and at least one passenger are required")
		return
	}
	o, ok := lookupOffer(w, req.OfferID)
	if !ok {
		return
	}

	orderMu.Lock()
	nextSeq++
	id := fmt.Sprintf("ORD-%d", nextSeq)
	ord := &Order{
		OrderID:    id,
		OfferID:    o.OfferID,
		Pnr:        pnrFor(nextSeq),
		Status:     "HELD",
		Total:      (o.BaseFare + o.Taxes) * float64(len(req.Passengers)),
		Currency:   o.Currency,
		PassengerN: len(req.Passengers),
	}
	orders[id] = ord
	orderMu.Unlock()

	writeJSON(w, http.StatusCreated, ord)
}

func pnrFor(seq int) string {
	const letters = "ABCDEFGHJKLMNPQRSTUVWXYZ"
	h := hash32(fmt.Sprintf("pnr-%d", seq))
	b := make([]byte, 6)
	for i := range b {
		b[i] = letters[h%uint32(len(letters))]
		h = h/uint32(len(letters)) + uint32(seq)*7919 + uint32(i)
	}
	return string(b)
}

func ticketOrder(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	orderMu.Lock()
	defer orderMu.Unlock()
	ord, ok := orders[id]
	if !ok {
		writeErr(w, http.StatusNotFound, "ORDER_NOT_FOUND", "unknown order "+id)
		return
	}
	switch ord.Status {
	case "TICKETED":
		writeErr(w, http.StatusConflict, "ALREADY_TICKETED", "order "+id+" is already ticketed")
		return
	case "CANCELLED":
		writeErr(w, http.StatusConflict, "ORDER_CANCELLED", "order "+id+" is cancelled")
		return
	}
	ord.Status = "TICKETED"
	ord.TicketNumbers = nil
	for i := 0; i < ord.PassengerN; i++ {
		ord.TicketNumbers = append(ord.TicketNumbers, fmt.Sprintf("998%010d", int(hash32(fmt.Sprintf("%s-%d", id, i)))))
	}
	writeJSON(w, http.StatusOK, ord)
}

func cancelOrder(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	orderMu.Lock()
	defer orderMu.Unlock()
	ord, ok := orders[id]
	if !ok {
		writeErr(w, http.StatusNotFound, "ORDER_NOT_FOUND", "unknown order "+id)
		return
	}
	if ord.Status == "CANCELLED" {
		writeErr(w, http.StatusConflict, "ALREADY_CANCELLED", "order "+id+" is already cancelled")
		return
	}
	ord.Status = "CANCELLED"
	writeJSON(w, http.StatusOK, ord)
}

func newMux() *http.ServeMux {
	mux := http.NewServeMux()
	mux.HandleFunc("POST /v1/offers/search", search)
	mux.HandleFunc("POST /v1/offers/{id}/price", price)
	mux.HandleFunc("POST /v1/orders", createOrder)
	mux.HandleFunc("POST /v1/orders/{id}/ticket", ticketOrder)
	mux.HandleFunc("POST /v1/orders/{id}/cancel", cancelOrder)
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, _ *http.Request) { w.Write([]byte("ok")) })
	return mux
}

func main() {
	log.Println("AeroNDC mock listening on :8081")
	log.Fatal(http.ListenAndServe(":8081", newMux()))
}
