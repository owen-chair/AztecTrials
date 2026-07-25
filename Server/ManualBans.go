package main

import (
	"sync"
	"time"
)

var (
	manualBanMu sync.RWMutex
	manualBans  = map[string]time.Time{}
)

func setManualBan(ip string, bannedUntilUTC time.Time) {
	manualBanMu.Lock()
	manualBans[ip] = bannedUntilUTC.UTC()
	manualBanMu.Unlock()
}

func replaceManualBans(next map[string]time.Time) {
	manualBanMu.Lock()
	manualBans = next
	manualBanMu.Unlock()
}

func clearManualBan(ip string) {
	manualBanMu.Lock()
	delete(manualBans, ip)
	manualBanMu.Unlock()
}

func isManuallyBanned(ip string, nowUTC time.Time) (bool, time.Time) {
	nowUTC = nowUTC.UTC()
	manualBanMu.RLock()
	until, ok := manualBans[ip]
	manualBanMu.RUnlock()
	if !ok {
		return false, time.Time{}
	}
	until = until.UTC()
	if nowUTC.Before(until) {
		return true, until
	}

	// Expired; clean up.
	manualBanMu.Lock()
	if cur, ok := manualBans[ip]; ok && !nowUTC.Before(cur.UTC()) {
		delete(manualBans, ip)
	}
	manualBanMu.Unlock()
	return false, time.Time{}
}

func refreshManualBansFromDB(nowUTC time.Time) {
	if logStore == nil {
		return
	}
	next, err := logStore.ListActiveManualIPBans(nowUTC.UTC())
	if err != nil {
		return
	}
	replaceManualBans(next)
}
