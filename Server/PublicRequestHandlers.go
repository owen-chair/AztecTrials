package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"strconv"
	"strings"
	"sync"
	"time"
)

const publicCacheTTL = 20 * time.Second

const (
	publicRateLimitPerSecond = 20
	publicRateLimitBan       = 15 * time.Minute
	publicRateLimitMaxIPs    = 500
)

type cachedJSON struct {
	At   time.Time
	Body []byte
}

var (
	cacheMu sync.Mutex

	top10Cache  cachedJSON
	top100Cache cachedJSON

	pageCache     = map[int]cachedJSON{}
	personalCache = map[string]cachedJSON{}

	rateMu sync.Mutex
	// ip -> limiter state
	rateLimiters = map[string]*ipLimiter{}
)

type ipLimiter struct {
	WindowStart time.Time
	Count       int
	BannedUntil time.Time
	LastSeen    time.Time
}

func evictOldestIPIfNeeded() {
	if len(rateLimiters) <= publicRateLimitMaxIPs {
		return
	}
	oldestKey := ""
	oldestAt := time.Time{}
	first := true
	for k, v := range rateLimiters {
		if v == nil {
			continue
		}
		if first || v.LastSeen.Before(oldestAt) {
			first = false
			oldestKey = k
			oldestAt = v.LastSeen
		}
	}
	if oldestKey != "" {
		delete(rateLimiters, oldestKey)
	}
}

// checkPublicRateLimit returns true if request should be allowed.
// If blocked, it writes the status code and returns false.
func checkPublicRateLimit(rw http.ResponseWriter, r *http.Request, endpoint string, reqLog *RequestLog) bool {
	clientIP := getClientIP(r)
	if clientIP == "" {
		// If we can't determine an IP, don't rate limit.
		return true
	}

	now := time.Now().UTC()
	if banned, until := isManuallyBanned(clientIP, now); banned {
		if reqLog != nil {
			reqLog.Error = "Manually banned"
		}
		rw.Header().Set("Retry-After", strconv.FormatInt(int64(time.Until(until).Seconds()), 10))
		rw.WriteHeader(http.StatusTooManyRequests)
		return false
	}
	secStart := now.Truncate(time.Second)

	rateMu.Lock()
	st := rateLimiters[clientIP]
	if st == nil {
		if len(rateLimiters) >= publicRateLimitMaxIPs {
			evictOldestIPIfNeeded()
		}
		st = &ipLimiter{WindowStart: secStart, Count: 0}
		rateLimiters[clientIP] = st
	}
	st.LastSeen = now

	// If currently banned.
	if !st.BannedUntil.IsZero() && now.Before(st.BannedUntil) {
		bannedUntil := st.BannedUntil
		count := st.Count
		windowStart := st.WindowStart
		rateMu.Unlock()

		if reqLog != nil {
			reqLog.Error = "Rate limited"
		}
		logStore.InsertRateLimit(&RateLimitLog{
			TimeUTC:        now,
			Endpoint:       endpoint,
			Method:         r.Method,
			Path:           r.URL.Path,
			RemoteAddr:     r.RemoteAddr,
			ClientIP:       clientIP,
			UserAgent:      r.UserAgent(),
			Event:          "blocked",
			LimitPerSecond: publicRateLimitPerSecond,
			CountThisSec:   count,
			WindowStartUTC: windowStart,
			BannedUntilUTC: bannedUntil,
		})
		rw.WriteHeader(http.StatusTooManyRequests)
		return false
	}
	if !st.BannedUntil.IsZero() && !now.Before(st.BannedUntil) {
		// Ban expired.
		st.BannedUntil = time.Time{}
		st.WindowStart = secStart
		st.Count = 0
	}

	if st.WindowStart.Before(secStart) {
		st.WindowStart = secStart
		st.Count = 0
	}
	st.Count++
	count := st.Count
	windowStart := st.WindowStart

	if count > publicRateLimitPerSecond {
		st.BannedUntil = now.Add(publicRateLimitBan)
		bannedUntil := st.BannedUntil
		rateMu.Unlock()

		if reqLog != nil {
			reqLog.Error = "Rate limited"
		}
		logStore.InsertRateLimit(&RateLimitLog{
			TimeUTC:        now,
			Endpoint:       endpoint,
			Method:         r.Method,
			Path:           r.URL.Path,
			RemoteAddr:     r.RemoteAddr,
			ClientIP:       clientIP,
			UserAgent:      r.UserAgent(),
			Event:          "banned",
			LimitPerSecond: publicRateLimitPerSecond,
			CountThisSec:   count,
			WindowStartUTC: windowStart,
			BannedUntilUTC: bannedUntil,
		})
		rw.WriteHeader(http.StatusTooManyRequests)
		return false
	}

	rateMu.Unlock()
	return true
}

func cacheFresh(e cachedJSON, now time.Time) bool {
	if len(e.Body) == 0 {
		return false
	}
	if e.At.IsZero() {
		return false
	}
	return now.Sub(e.At) < publicCacheTTL
}

func evictOldestPageCacheIfNeeded() {
	if len(pageCache) <= 100 {
		return
	}
	oldestKey := 0
	oldestAt := time.Time{}
	first := true
	for k, v := range pageCache {
		if first || v.At.Before(oldestAt) {
			first = false
			oldestKey = k
			oldestAt = v.At
		}
	}
	delete(pageCache, oldestKey)
}

func evictOldestPersonalCacheIfNeeded() {
	if len(personalCache) <= 100 {
		return
	}
	oldestKey := ""
	oldestAt := time.Time{}
	first := true
	for k, v := range personalCache {
		if first || v.At.Before(oldestAt) {
			first = false
			oldestKey = k
			oldestAt = v.At
		}
	}
	delete(personalCache, oldestKey)
}

type statusRecordingResponseWriter struct {
	http.ResponseWriter
	status int
}

func (w *statusRecordingResponseWriter) WriteHeader(statusCode int) {
	w.status = statusCode
	w.ResponseWriter.WriteHeader(statusCode)
}

func parseIP(s string) net.IP {
	v := strings.TrimSpace(strings.Trim(s, "\""))
	if v == "" {
		return nil
	}
	if ip := net.ParseIP(v); ip != nil {
		return ip
	}
	// Handle host:port (IPv4) or [IPv6]:port.
	if host, _, err := net.SplitHostPort(v); err == nil {
		if ip := net.ParseIP(host); ip != nil {
			return ip
		}
	}
	// Handle IPv6 zone identifiers like fe80::1%eth0.
	if i := strings.IndexByte(v, '%'); i > 0 {
		if ip := net.ParseIP(v[:i]); ip != nil {
			return ip
		}
	}
	return nil
}

func remoteHost(r *http.Request) string {
	if r.RemoteAddr == "" {
		return ""
	}
	if host, _, err := net.SplitHostPort(r.RemoteAddr); err == nil {
		return host
	}
	// In practice RemoteAddr is almost always host:port, but fall back just in case.
	return r.RemoteAddr
}

func isTrustedProxyHost(host string) bool {
	ip := parseIP(host)
	if ip == nil {
		return false
	}
	// In this deployment the Go server is not publicly exposed; only internal Docker
	// containers (nginx reverse proxies) can reach it.
	return ip.IsLoopback() || ip.IsPrivate()
}

func getClientIP(r *http.Request) string {
	remote := remoteHost(r)
	if remote == "" {
		return ""
	}

	// Only trust proxy headers when the immediate peer is a trusted proxy.
	if isTrustedProxyHost(remote) {
		if v := strings.TrimSpace(r.Header.Get("X-Real-IP")); v != "" {
			if ip := parseIP(v); ip != nil {
				return ip.String()
			}
		}
		if v := strings.TrimSpace(r.Header.Get("X-Forwarded-For")); v != "" {
			parts := strings.Split(v, ",")
			for _, p := range parts {
				if ip := parseIP(p); ip != nil {
					return ip.String()
				}
			}
		}
	}

	// Fall back to the immediate peer.
	if ip := parseIP(remote); ip != nil {
		return ip.String()
	}
	return remote
}

func truncateForLog(s string, max int) string {
	if max <= 0 {
		return ""
	}
	if len(s) <= max {
		return s
	}
	return s[:max] + "...(truncated," + strconv.Itoa(len(s)) + ")"
}

func decodeAndValidate(path string, target interface{}, reqLog *RequestLog) error {
	// Standard base64 only.
	decodedBytes, err := base64.StdEncoding.DecodeString(path)
	if err != nil {
		if reqLog != nil {
			reqLog.Error = "invalid base64 encoding"
		}
		return fmt.Errorf("invalid base64 encoding")
	}
	if reqLog != nil {
		reqLog.PayloadJSON = truncateForLog(string(decodedBytes), 2048)
	}

	if err := json.Unmarshal(decodedBytes, target); err != nil {
		if reqLog != nil {
			reqLog.Error = "invalid JSON"
		}
		return fmt.Errorf("invalid JSON")
	}
	return nil
}

func stripSubmitMarkerAt15(path string, reqLog *RequestLog) (string, error) {
	if reqLog != nil {
		reqLog.PayloadPathSegmentRaw = path
	}
	if len(path) <= 15 {
		return "", fmt.Errorf("invalid request data")
	}
	if path[15] != 'a' {
		return "", fmt.Errorf("invalid request data")
	}
	stripped := path[:15] + path[16:]
	if reqLog != nil {
		reqLog.PayloadPathSegmentStripped = stripped
	}
	return stripped, nil
}

func toPublicPlayerData(player *PlayerData) PublicPlayerData {
	return PublicPlayerData{PlayerName: player.PlayerName, CompletionSeconds: player.CompletionSeconds}
}

func helloHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	// Note: http.HandleFunc("/", ...) is a catch-all in net/http. Ensure unknown routes
	// return 404 instead of a friendly banner.
	if r.URL.Path != "/" {
		http.NotFound(rw, r)
		return
	}

	if !checkPublicRateLimit(rw, r, "/", &reqLog) {
		return
	}

	// Don't advertise service presence on the root route.
	// Best-effort: hijack and close the connection without any HTTP response.
	if hj, ok := w.(http.Hijacker); ok {
		conn, _, err := hj.Hijack()
		if err == nil {
			rw.status = http.StatusForbidden
			_ = conn.Close()
			return
		}
	}

	rw.WriteHeader(http.StatusForbidden)
}

func submitTimeHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/time/submit/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if !checkPublicRateLimit(rw, r, "/time/submit/", &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	path := strings.TrimPrefix(r.URL.Path, "/time/submit/")
	if path == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}

	path, err := stripSubmitMarkerAt15(path, &reqLog)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	var request SubmitTimeRequest
	if err := decodeAndValidate(path, &request, &reqLog); err != nil {
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	if request.ClientKey != CLIENT_KEY {
		reqLog.Error = "Invalid client key"
		http.Error(rw, "Invalid client key", http.StatusUnauthorized)
		return
	}

	playerName := strings.TrimSpace(request.PlayerName)
	if playerName == "" {
		reqLog.Error = "Missing playername"
		http.Error(rw, "Missing playername", http.StatusBadRequest)
		return
	}
	if len(playerName) > 64 {
		reqLog.Error = "playername too long"
		http.Error(rw, "playername too long", http.StatusBadRequest)
		return
	}

	completion := request.CompletionSeconds
	if !(completion > 0) {
		reqLog.Error = "Invalid completionseconds"
		http.Error(rw, "Invalid completionseconds", http.StatusBadRequest)
		return
	}
	if completion > 86400 {
		reqLog.Error = "completionseconds too large"
		http.Error(rw, "completionseconds too large", http.StatusBadRequest)
		return
	}

	now := time.Now().UTC()

	clientIP := getClientIP(r)
	geoCountry, geoCity := logStore.LookupGeoForIP(clientIP)
	msg, err := playerStore.SubmitTime(playerName, completion, now, clientIP, geoCountry, geoCity)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}
	_ = json.NewEncoder(rw).Encode(Response{Message: msg})
}

func topNHandler(w http.ResponseWriter, r *http.Request, limit int) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	ep := fmt.Sprintf("/data/top%d/", limit)
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   ep,
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if !checkPublicRateLimit(rw, r, ep, &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	if limit <= 0 {
		limit = 10
	}
	if limit > 200 {
		limit = 200
	}

	path := strings.TrimPrefix(r.URL.Path, fmt.Sprintf("/data/top%d/", limit))
	if path == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}
	reqLog.PayloadPathSegmentRaw = path

	var request LeaderboardRequest
	if err := decodeAndValidate(path, &request, &reqLog); err != nil {
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	if request.ClientKey != CLIENT_KEY {
		reqLog.Error = "Invalid client key"
		http.Error(rw, "Invalid client key", http.StatusUnauthorized)
		return
	}

	// Simple in-memory cache for top10/top100.
	if r.Method == http.MethodGet && (limit == 10 || limit == 100) {
		now := time.Now()
		cacheMu.Lock()
		var entry cachedJSON
		if limit == 10 {
			entry = top10Cache
		} else {
			entry = top100Cache
		}
		if cacheFresh(entry, now) {
			body := entry.Body
			cacheMu.Unlock()
			_, _ = rw.Write(body)
			return
		}
		cacheMu.Unlock()
	}

	publicPlayers, err := playerStore.GetTop(0, limit)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}

	body, err := json.Marshal(LeaderboardResponse{Players: publicPlayers})
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}
	_, _ = rw.Write(body)

	// Store cache only for successful responses.
	if r.Method == http.MethodGet && (limit == 10 || limit == 100) {
		now := time.Now()
		cacheMu.Lock()
		if limit == 10 {
			top10Cache = cachedJSON{At: now, Body: body}
		} else {
			top100Cache = cachedJSON{At: now, Body: body}
		}
		cacheMu.Unlock()
	}
}

func top10Handler(w http.ResponseWriter, r *http.Request)  { topNHandler(w, r, 10) }
func top100Handler(w http.ResponseWriter, r *http.Request) { topNHandler(w, r, 100) }

func pageHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/data/page/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if !checkPublicRateLimit(rw, r, "/data/page/", &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	path := strings.TrimPrefix(r.URL.Path, "/data/page/")
	if path == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}
	reqLog.PayloadPathSegmentRaw = path

	var request PagedLeaderboardRequest
	if err := decodeAndValidate(path, &request, &reqLog); err != nil {
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	if request.ClientKey != CLIENT_KEY {
		reqLog.Error = "Invalid client key"
		http.Error(rw, "Invalid client key", http.StatusUnauthorized)
		return
	}

	if request.Page < 0 {
		reqLog.Error = "Invalid page"
		http.Error(rw, "Invalid page", http.StatusBadRequest)
		return
	}

	const pageSize = 100
	offset := request.Page * pageSize

	// Simple in-memory cache: up to 100 pages, 20s TTL.
	if r.Method == http.MethodGet {
		now := time.Now()
		cacheMu.Lock()
		entry, ok := pageCache[request.Page]
		if ok && cacheFresh(entry, now) {
			body := entry.Body
			cacheMu.Unlock()
			_, _ = rw.Write(body)
			return
		}
		cacheMu.Unlock()
	}

	publicPlayers, err := playerStore.GetTop(offset, pageSize)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}

	body, err := json.Marshal(LeaderboardResponse{Players: publicPlayers})
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}
	_, _ = rw.Write(body)

	if r.Method == http.MethodGet {
		now := time.Now()
		cacheMu.Lock()
		pageCache[request.Page] = cachedJSON{At: now, Body: body}
		evictOldestPageCacheIfNeeded()
		cacheMu.Unlock()
	}
}

func personalRankHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/data/personal/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if !checkPublicRateLimit(rw, r, "/data/personal/", &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	path := strings.TrimPrefix(r.URL.Path, "/data/personal/")
	if path == "" {
		reqLog.Error = "Missing request data"
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: "Missing request data"})
		return
	}
	reqLog.PayloadPathSegmentRaw = path

	var request PersonalRankRequest
	if err := decodeAndValidate(path, &request, &reqLog); err != nil {
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: err.Error()})
		return
	}

	if request.ClientKey != CLIENT_KEY {
		reqLog.Error = "Invalid client key"
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: "Invalid client key"})
		return
	}

	playerName := strings.TrimSpace(request.PlayerName)
	if playerName == "" {
		reqLog.Error = "Missing playername"
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: "Missing playername"})
		return
	}
	if len(playerName) > 64 {
		reqLog.Error = "playername too long"
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: "playername too long"})
		return
	}

	// Simple in-memory cache: up to 100 players, 20s TTL.
	if r.Method == http.MethodGet {
		now := time.Now()
		cacheMu.Lock()
		entry, ok := personalCache[playerName]
		if ok && cacheFresh(entry, now) {
			body := entry.Body
			cacheMu.Unlock()
			_, _ = rw.Write(body)
			return
		}
		cacheMu.Unlock()
	}

	rank, seconds, found, err := playerStore.GetPersonalRank(playerName)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}
	if !found {
		reqLog.Error = "Player not found"
		_ = json.NewEncoder(rw).Encode(PersonalRankResponse{Message: "Player not found"})
		return
	}

	body, err := json.Marshal(PersonalRankResponse{
		PlayerName:        playerName,
		CompletionSeconds: seconds,
		Rank:              rank,
	})
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, "Internal server error", http.StatusInternalServerError)
		return
	}
	_, _ = rw.Write(body)

	if r.Method == http.MethodGet {
		now := time.Now()
		cacheMu.Lock()
		personalCache[playerName] = cachedJSON{At: now, Body: body}
		evictOldestPersonalCacheIfNeeded()
		cacheMu.Unlock()
	}
}

type CheckpointUnlockRequest struct {
	ClientKey string `json:"clientkey"`
}

func checkpointUnlockHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/metrics/checkpointUnlock/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if r.Method != http.MethodGet {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	// /metrics/checkpointUnlock/{checkpoint}/{base64_json}
	sub := strings.TrimPrefix(r.URL.Path, "/metrics/checkpointUnlock/")
	sub = strings.TrimSpace(sub)
	if sub == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}

	parts := strings.SplitN(sub, "/", 2)
	if len(parts) != 2 {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}

	checkpoint := strings.TrimSpace(parts[0])
	if !isAllowedCheckpointUnlock(checkpoint) {
		reqLog.Error = "Invalid checkpoint"
		http.Error(rw, "Invalid checkpoint", http.StatusBadRequest)
		return
	}

	ep := "/metrics/checkpointUnlock/" + checkpoint + "/"
	reqLog.Endpoint = ep
	if !checkPublicRateLimit(rw, r, ep, &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	path := strings.TrimSpace(parts[1])
	if path == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}
	reqLog.PayloadPathSegmentRaw = path

	var request CheckpointUnlockRequest
	if err := decodeAndValidate(path, &request, &reqLog); err != nil {
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	if request.ClientKey != CLIENT_KEY {
		reqLog.Error = "Invalid client key"
		http.Error(rw, "Invalid client key", http.StatusUnauthorized)
		return
	}

	now := time.Now().UTC()
	clientIP := getClientIP(r)
	logStore.InsertCheckpointUnlock(now, clientIP, checkpoint)
	_ = json.NewEncoder(rw).Encode(Response{Message: "ok"})
}

func genericMetricHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/metrics/genericMetric/",
		Method:     r.Method,
		Path:       r.URL.Path,
		RemoteAddr: r.RemoteAddr,
		ClientIP:   getClientIP(r),
		UserAgent:  r.UserAgent(),
		Headers:    r.Header.Clone(),
	}
	defer func() {
		reqLog.DurationMs = time.Since(start).Milliseconds()
		reqLog.Status = rw.status
		logStore.Insert(&reqLog)
	}()

	if r.Method != http.MethodGet && r.Method != http.MethodPost {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	if !checkPublicRateLimit(rw, r, "/metrics/genericMetric/", &reqLog) {
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	// /metrics/genericMetric/{base64_json}
	path := strings.TrimPrefix(r.URL.Path, "/metrics/genericMetric")
	path = strings.TrimPrefix(path, "/")
	if path == "" {
		reqLog.Error = "Missing request data"
		http.Error(rw, "Missing request data", http.StatusBadRequest)
		return
	}
	reqLog.PayloadPathSegmentRaw = path

	stripped, err := stripSubmitMarkerAt15(path, &reqLog)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}
	path = stripped

	var payload json.RawMessage
	if err := decodeAndValidate(path, &payload, &reqLog); err != nil {
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	_ = json.NewEncoder(rw).Encode(Response{Message: "ok"})
}
