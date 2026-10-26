package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func post(t *testing.T, path, body string) (*httptest.ResponseRecorder, map[string]any) {
	t.Helper()
	rec := httptest.NewRecorder()
	req := httptest.NewRequest("POST", path, strings.NewReader(body))
	newMux().ServeHTTP(rec, req)
	var out map[string]any
	json.Unmarshal(rec.Body.Bytes(), &out)
	return rec, out
}

const dacCgp = `{"origin":"DAC","destination":"CGP","date":"2026-11-14","cabin":"Economy"}`

func TestSearchIsDeterministic(t *testing.T) {
	_, a := post(t, "/v1/offers/search", dacCgp)
	_, b := post(t, "/v1/offers/search", dacCgp)
	oa, ob := a["offers"].([]any), b["offers"].([]any)
	if len(oa) < 3 || len(oa) != len(ob) {
		t.Fatalf("expected 3+ offers, same count both times; got %d and %d", len(oa), len(ob))
	}
	if oa[0].(map[string]any)["baseFare"] != ob[0].(map[string]any)["baseFare"] {
		t.Fatal("fares differ between identical searches")
	}
}

func TestErrTriggerReturnsErrorBody(t *testing.T) {
	rec, out := post(t, "/v1/offers/search", `{"origin":"ERR","destination":"CGP","date":"2026-11-14"}`)
	if rec.Code != http.StatusInternalServerError || out["errors"] == nil {
		t.Fatalf("expected 500 with errors body, got %d %v", rec.Code, out)
	}
}

func TestExpiredOfferReturns409(t *testing.T) {
	_, out := post(t, "/v1/offers/search", `{"origin":"DAC","destination":"EXP","date":"2026-11-14"}`)
	id := out["offers"].([]any)[0].(map[string]any)["offerId"].(string)
	rec, body := post(t, "/v1/offers/"+id+"/price", `{}`)
	code := body["errors"].([]any)[0].(map[string]any)["code"]
	if rec.Code != http.StatusConflict || code != "OFFER_EXPIRED" {
		t.Fatalf("expected 409 OFFER_EXPIRED, got %d %v", rec.Code, code)
	}
}

func TestBookTicketCancelLifecycle(t *testing.T) {
	_, out := post(t, "/v1/offers/search", dacCgp)
	id := out["offers"].([]any)[0].(map[string]any)["offerId"].(string)

	rec, ord := post(t, "/v1/orders", `{"offerId":"`+id+`","passengers":[{"type":"ADT","firstName":"A","lastName":"B"}]}`)
	if rec.Code != http.StatusCreated || ord["status"] != "HELD" {
		t.Fatalf("book failed: %d %v", rec.Code, ord)
	}
	oid := ord["orderId"].(string)

	if rec, o := post(t, "/v1/orders/"+oid+"/ticket", ``); rec.Code != 200 || o["status"] != "TICKETED" {
		t.Fatalf("ticket failed: %d %v", rec.Code, o)
	}
	if rec, _ := post(t, "/v1/orders/"+oid+"/ticket", ``); rec.Code != http.StatusConflict {
		t.Fatalf("second ticket should be 409, got %d", rec.Code)
	}
	if rec, o := post(t, "/v1/orders/"+oid+"/cancel", ``); rec.Code != 200 || o["status"] != "CANCELLED" {
		t.Fatalf("cancel failed: %d %v", rec.Code, o)
	}
}
