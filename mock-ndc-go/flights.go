package main

import (
	"fmt"
	"hash/fnv"
	"time"
)

// Domestic Bangladesh airports; anything else counts as international.
var domestic = map[string]bool{
	"DAC": true, "CGP": true, "CXB": true, "ZYL": true, "JSR": true, "SPD": true, "BZL": true,
}

const offerTTL = 15 * time.Minute

func isInternational(origin, destination string) bool {
	return !domestic[origin] || !domestic[destination]
}

func hash32(s string) uint32 {
	h := fnv.New32a()
	h.Write([]byte(s))
	return h.Sum32()
}

// generateOffers returns deterministic fake offers: the same route/date/cabin always yields
// the same flights and fares, which makes tests and demos repeatable.
// Destination "EXP" returns offers that are already expired (to test OFFER_EXPIRED).
func generateOffers(req SearchReq, now time.Time) ([]Offer, error) {
	day, err := time.Parse("2006-01-02", req.Date)
	if err != nil {
		return nil, fmt.Errorf("date must be yyyy-MM-dd")
	}
	seed := hash32(req.Origin + "|" + req.Destination + "|" + req.Date + "|" + req.Cabin)
	intl := isInternational(req.Origin, req.Destination)

	cabinFactor := 1.0
	if req.Cabin == "Business" {
		cabinFactor = 2.4
	}

	expires := now.Add(offerTTL)
	if req.Destination == "EXP" {
		expires = now.Add(-time.Minute)
	}

	count := 3 + int(seed%3) // 3..5 offers
	offers := make([]Offer, 0, count)
	for i := 0; i < count; i++ {
		s := hash32(fmt.Sprintf("%d|%d", seed, i))

		var base float64
		var duration time.Duration
		if intl {
			base = 12000 + float64(s%28000)
			duration = time.Duration(120+s%240) * time.Minute
		} else {
			base = 3000 + float64(s%4000)
			duration = time.Duration(45+s%60) * time.Minute
		}
		base = float64(int(base*cabinFactor/50) * 50)
		taxes := float64(int(base * 0.12))

		dep := day.Add(time.Duration(6+i*3)*time.Hour + time.Duration(s%4)*15*time.Minute)
		offers = append(offers, Offer{
			OfferID:     fmt.Sprintf("NDC-%08x-%d", seed, i),
			Carrier:     "AN",
			FlightNo:    fmt.Sprintf("AN%d", 300+int(s%400)),
			Origin:      req.Origin,
			Destination: req.Destination,
			Departure:   dep,
			Arrival:     dep.Add(duration),
			BaseFare:    base,
			Taxes:       taxes,
			Currency:    "BDT",
			Cabin:       req.Cabin,
			Seats:       1 + int(s%9),
			Baggage:     map[bool]string{true: "30kg", false: "20kg"}[intl],
			Refundable:  s%2 == 0,
			ExpiresAt:   expires,
		})
	}
	return offers, nil
}
