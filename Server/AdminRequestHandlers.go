package main

import (
	"encoding/json"
	"errors"
	"math"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"
)

func getAdminKey() string {
	if v := strings.TrimSpace(os.Getenv("BUGGYPYRAMID_ADMIN_KEY")); v != "" {
		return v
	}
	return ADMIN_KEY
}

func isAdminAuthorized(r *http.Request) bool {
	key := strings.TrimSpace(r.Header.Get("X-Admin-Key"))
	if key == "" {
		key = strings.TrimSpace(r.URL.Query().Get("adminkey"))
	}
	if key == "" {
		return false
	}
	return key == getAdminKey()
}

func adminLogsHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/logs",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}
	rw.Header().Set("Content-Type", "application/json")

	q := LogQuery{}
	q.Endpoint = strings.TrimSpace(r.URL.Query().Get("endpoint"))
	q.Search = strings.TrimSpace(r.URL.Query().Get("q"))

	if v := strings.TrimSpace(r.URL.Query().Get("start")); v != "" {
		if t, err := time.Parse(time.RFC3339, v); err == nil {
			q.StartUTC = t.UTC()
		} else {
			reqLog.Error = "Invalid start"
			http.Error(rw, "Invalid start", http.StatusBadRequest)
			return
		}
	}
	if v := strings.TrimSpace(r.URL.Query().Get("end")); v != "" {
		if t, err := time.Parse(time.RFC3339, v); err == nil {
			q.EndUTC = t.UTC()
		} else {
			reqLog.Error = "Invalid end"
			http.Error(rw, "Invalid end", http.StatusBadRequest)
			return
		}
	}
	if !q.StartUTC.IsZero() && !q.EndUTC.IsZero() && q.StartUTC.After(q.EndUTC) {
		reqLog.Error = "Invalid range"
		http.Error(rw, "Invalid range", http.StatusBadRequest)
		return
	}

	if v := strings.TrimSpace(r.URL.Query().Get("errorOnly")); v != "" {
		q.ErrorOnly = (v == "1" || strings.EqualFold(v, "true"))
	}

	if v := strings.TrimSpace(r.URL.Query().Get("status")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.HasStatus = true
			q.Status = n
		}
	}

	if v := strings.TrimSpace(r.URL.Query().Get("page")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.Page = n
		}
	}
	if v := strings.TrimSpace(r.URL.Query().Get("pageSize")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.PageSize = n
		}
	}

	result, err := logStore.Query(q)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusInternalServerError)
		return
	}
	_ = json.NewEncoder(rw).Encode(result)
}

// Handles subpaths under /admin/logs/
// - GET  /admin/logs/{id}
// - POST /admin/logs/clear
func adminLogsSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/logs/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/logs/")
	sub = strings.TrimSpace(sub)
	if sub == "" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}
	if sub == "clear" {
		reqLog.Endpoint = "/admin/logs/clear"
	} else if sub == "stats" {
		reqLog.Endpoint = "/admin/logs/stats"
	}

	// Clear all logs.
	if sub == "clear" {
		if r.Method != http.MethodPost {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}
		deleted, err := logStore.ClearAll()
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{
			"deleted": deleted,
		})
		return
	}

	// Aggregated stats for graphs.
	// GET /admin/logs/stats?bucket=sec|min|hour|day&start=RFC3339&end=RFC3339&endpoint=...&errorOnly=true
	if sub == "stats" {
		if r.Method != http.MethodGet {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}

		bucket := strings.TrimSpace(r.URL.Query().Get("bucket"))
		endpoint := strings.TrimSpace(r.URL.Query().Get("endpoint"))

		end := time.Now().UTC()
		if v := strings.TrimSpace(r.URL.Query().Get("end")); v != "" {
			if t, err := time.Parse(time.RFC3339, v); err == nil {
				end = t.UTC()
			} else {
				http.Error(rw, "Invalid end", http.StatusBadRequest)
				return
			}
		}

		start := end.Add(-1 * time.Hour)
		if v := strings.TrimSpace(r.URL.Query().Get("start")); v != "" {
			if t, err := time.Parse(time.RFC3339, v); err == nil {
				start = t.UTC()
			} else {
				http.Error(rw, "Invalid start", http.StatusBadRequest)
				return
			}
		}

		if start.After(end) {
			http.Error(rw, "Invalid range", http.StatusBadRequest)
			return
		}

		errorOnly := false
		if v := strings.TrimSpace(r.URL.Query().Get("errorOnly")); v != "" {
			errorOnly = (v == "1" || strings.EqualFold(v, "true"))
		}

		result, err := logStore.QueryStats(LogStatsQuery{
			Bucket:    bucket,
			StartUTC:  start,
			EndUTC:    end,
			Endpoint:  endpoint,
			ErrorOnly: errorOnly,
		})
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusBadRequest)
			return
		}

		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(result)
		return
	}

	// Fetch by id.
	if r.Method != http.MethodGet {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	id, err := strconv.ParseInt(sub, 10, 64)
	if err != nil || id <= 0 {
		http.Error(rw, "Invalid id", http.StatusBadRequest)
		return
	}

	entry, found, err := logStore.GetByID(id)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusInternalServerError)
		return
	}
	if !found {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}

	rw.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(rw).Encode(entry)
}

func adminCheckpointUnlocksHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/checkpointunlocks",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	rw.Header().Set("Content-Type", "application/json")

	if r.Method == http.MethodGet {
		q := CheckpointUnlockQuery{}
		q.ClientIP = strings.TrimSpace(r.URL.Query().Get("ip"))
		q.Checkpoint = strings.TrimSpace(r.URL.Query().Get("checkpoint"))
		q.Search = strings.TrimSpace(r.URL.Query().Get("q"))

		if v := strings.TrimSpace(r.URL.Query().Get("start")); v != "" {
			if t, err := time.Parse(time.RFC3339, v); err == nil {
				q.StartUTC = t.UTC()
			} else {
				reqLog.Error = "Invalid start"
				http.Error(rw, "Invalid start", http.StatusBadRequest)
				return
			}
		}
		if v := strings.TrimSpace(r.URL.Query().Get("end")); v != "" {
			if t, err := time.Parse(time.RFC3339, v); err == nil {
				q.EndUTC = t.UTC()
			} else {
				reqLog.Error = "Invalid end"
				http.Error(rw, "Invalid end", http.StatusBadRequest)
				return
			}
		}
		if !q.StartUTC.IsZero() && !q.EndUTC.IsZero() && q.StartUTC.After(q.EndUTC) {
			reqLog.Error = "Invalid range"
			http.Error(rw, "Invalid range", http.StatusBadRequest)
			return
		}

		if v := strings.TrimSpace(r.URL.Query().Get("page")); v != "" {
			if n, err := strconv.Atoi(v); err == nil {
				q.Page = n
			}
		}
		if v := strings.TrimSpace(r.URL.Query().Get("pageSize")); v != "" {
			if n, err := strconv.Atoi(v); err == nil {
				q.PageSize = n
			}
		}

		result, err := logStore.QueryCheckpointUnlocks(q)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusBadRequest)
			return
		}
		_ = json.NewEncoder(rw).Encode(result)
		return
	}

	// Create
	if r.Method == http.MethodPost {
		var body struct {
			TimeUTC    string `json:"timeutc"`
			ClientIP   string `json:"clientip"`
			Checkpoint string `json:"checkpoint"`
		}
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			reqLog.Error = "Invalid JSON"
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}

		clientIP := strings.TrimSpace(body.ClientIP)
		if clientIP == "" {
			reqLog.Error = "Missing clientip"
			http.Error(rw, "Missing clientip", http.StatusBadRequest)
			return
		}
		if ip := parseIP(clientIP); ip == nil {
			reqLog.Error = "Invalid clientip"
			http.Error(rw, "Invalid clientip", http.StatusBadRequest)
			return
		} else {
			clientIP = ip.String()
		}

		checkpoint := strings.TrimSpace(body.Checkpoint)
		if !isAllowedCheckpointUnlock(checkpoint) {
			reqLog.Error = "Invalid checkpoint"
			http.Error(rw, "Invalid checkpoint", http.StatusBadRequest)
			return
		}

		t := time.Now().UTC()
		if strings.TrimSpace(body.TimeUTC) != "" {
			if parsed, err := time.Parse(time.RFC3339, strings.TrimSpace(body.TimeUTC)); err == nil {
				t = parsed.UTC()
			} else {
				reqLog.Error = "Invalid timeutc"
				http.Error(rw, "Invalid timeutc", http.StatusBadRequest)
				return
			}
		}

		created, err := logStore.CreateCheckpointUnlock(t, clientIP, checkpoint)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusBadRequest)
			return
		}
		_ = json.NewEncoder(rw).Encode(created)
		return
	}

	http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
}

// Handles subpaths under /admin/checkpointunlocks/
// - GET    /admin/checkpointunlocks/{id}
// - PUT    /admin/checkpointunlocks/{id}
// - DELETE /admin/checkpointunlocks/{id}
// - POST   /admin/checkpointunlocks/clear
func adminCheckpointUnlocksSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/checkpointunlocks/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/checkpointunlocks/")
	sub = strings.TrimSpace(sub)
	if sub == "" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}
	if sub == "clear" {
		reqLog.Endpoint = "/admin/checkpointunlocks/clear"
	}

	if sub == "clear" {
		if r.Method != http.MethodPost {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}
		deleted, err := logStore.ClearCheckpointUnlocks()
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
		return
	}

	id, err := strconv.ParseInt(sub, 10, 64)
	if err != nil || id <= 0 {
		http.Error(rw, "Invalid id", http.StatusBadRequest)
		return
	}

	if r.Method == http.MethodGet {
		entry, found, err := logStore.GetCheckpointUnlockByID(id)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !found {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(entry)
		return
	}

	if r.Method == http.MethodDelete {
		deleted, err := logStore.DeleteCheckpointUnlockByID(id)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
		return
	}

	if r.Method == http.MethodPut {
		var body struct {
			TimeUTC    string `json:"timeutc"`
			ClientIP   string `json:"clientip"`
			Checkpoint string `json:"checkpoint"`
		}
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			reqLog.Error = "Invalid JSON"
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}

		clientIP := strings.TrimSpace(body.ClientIP)
		if clientIP == "" {
			reqLog.Error = "Missing clientip"
			http.Error(rw, "Missing clientip", http.StatusBadRequest)
			return
		}
		if ip := parseIP(clientIP); ip == nil {
			reqLog.Error = "Invalid clientip"
			http.Error(rw, "Invalid clientip", http.StatusBadRequest)
			return
		} else {
			clientIP = ip.String()
		}

		checkpoint := strings.TrimSpace(body.Checkpoint)
		if !isAllowedCheckpointUnlock(checkpoint) {
			reqLog.Error = "Invalid checkpoint"
			http.Error(rw, "Invalid checkpoint", http.StatusBadRequest)
			return
		}

		if strings.TrimSpace(body.TimeUTC) == "" {
			reqLog.Error = "Missing timeutc"
			http.Error(rw, "Missing timeutc", http.StatusBadRequest)
			return
		}
		parsed, err := time.Parse(time.RFC3339, strings.TrimSpace(body.TimeUTC))
		if err != nil {
			reqLog.Error = "Invalid timeutc"
			http.Error(rw, "Invalid timeutc", http.StatusBadRequest)
			return
		}

		updated, found, err := logStore.UpdateCheckpointUnlockByID(id, parsed.UTC(), clientIP, checkpoint)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !found {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(updated)
		return
	}

	http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
}

func adminPlayersHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/players",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}
	if r.Method == http.MethodPost {
		type addPlayerRequest struct {
			PlayerName        string  `json:"playername"`
			CompletionSeconds float64 `json:"completionseconds"`
		}
		var body addPlayerRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}
		body.PlayerName = strings.TrimSpace(body.PlayerName)
		if body.PlayerName == "" {
			http.Error(rw, "Invalid playername", http.StatusBadRequest)
			return
		}
		if len(body.PlayerName) > 64 {
			http.Error(rw, "playername too long", http.StatusBadRequest)
			return
		}
		if math.IsNaN(body.CompletionSeconds) || math.IsInf(body.CompletionSeconds, 0) || body.CompletionSeconds < 0 {
			http.Error(rw, "Invalid completionseconds", http.StatusBadRequest)
			return
		}
		if body.CompletionSeconds > 86400 {
			http.Error(rw, "completionseconds too large", http.StatusBadRequest)
			return
		}

		clientIP := getClientIP(r)
		geoCountry, geoCity := logStore.LookupGeoForIP(clientIP)
		_, err := playerStore.SetPlayerTime(body.PlayerName, body.CompletionSeconds, time.Now().UTC(), clientIP, geoCountry, geoCity)
		if err != nil {
			if errors.Is(err, ErrPlayerAlreadyExists) {
				http.Error(rw, "Player already exists", http.StatusConflict)
				return
			}
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		p, found, err := playerStore.GetPlayer(body.PlayerName)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !found {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(p)
		return
	}

	if r.Method != http.MethodGet {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	q := PlayerQuery{}
	q.Search = strings.TrimSpace(r.URL.Query().Get("q"))
	q.ClientIP = strings.TrimSpace(r.URL.Query().Get("ip"))
	q.Order = strings.TrimSpace(r.URL.Query().Get("order"))
	if v := strings.TrimSpace(r.URL.Query().Get("page")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.Page = n
		}
	}
	if v := strings.TrimSpace(r.URL.Query().Get("pageSize")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.PageSize = n
		}
	}

	result, err := playerStore.QueryPlayers(q)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusInternalServerError)
		return
	}
	rw.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(rw).Encode(result)
}

// Handles subpaths under /admin/players/
// - GET    /admin/players/{playername}
// - DELETE /admin/players/{playername}
// - POST   /admin/players/clear
func adminPlayersSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/players/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/players/")
	sub = strings.TrimSpace(sub)
	if sub == "" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}

	if sub == "clear" {
		if r.Method != http.MethodPost {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}
		deleted, err := playerStore.ClearAll()
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
		return
	}

	name, err := url.PathUnescape(sub)
	if err != nil {
		http.Error(rw, "Invalid playername", http.StatusBadRequest)
		return
	}
	name = strings.TrimSpace(name)
	if name == "" {
		http.Error(rw, "Invalid playername", http.StatusBadRequest)
		return
	}

	switch r.Method {
	case http.MethodGet:
		p, found, err := playerStore.GetPlayer(name)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !found {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(p)
		return
	case http.MethodDelete:
		deleted, err := playerStore.DeletePlayer(name)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
		return
	case http.MethodPut:
		type updatePlayerRequest struct {
			PlayerName        string  `json:"playername"`
			CompletionSeconds float64 `json:"completionseconds"`
		}
		var body updatePlayerRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}
		body.PlayerName = strings.TrimSpace(body.PlayerName)
		if body.PlayerName == "" {
			http.Error(rw, "Invalid playername", http.StatusBadRequest)
			return
		}
		if math.IsNaN(body.CompletionSeconds) || math.IsInf(body.CompletionSeconds, 0) || body.CompletionSeconds < 0 {
			http.Error(rw, "Invalid completionseconds", http.StatusBadRequest)
			return
		}

		updated, err := playerStore.UpdatePlayer(name, body.PlayerName, body.CompletionSeconds)
		if err != nil {
			if errors.Is(err, ErrPlayerAlreadyExists) {
				http.Error(rw, "Player already exists", http.StatusConflict)
				return
			}
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !updated {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}

		p, found, err := playerStore.GetPlayer(body.PlayerName)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		if !found {
			http.Error(rw, "Not found", http.StatusNotFound)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(p)
		return
	default:
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}
}

func adminRateLimitsHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/ratelimits",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}
	if r.Method != http.MethodGet {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	q := RateLimitQuery{}
	q.Endpoint = strings.TrimSpace(r.URL.Query().Get("endpoint"))
	q.ClientIP = strings.TrimSpace(r.URL.Query().Get("ip"))
	q.Event = strings.TrimSpace(r.URL.Query().Get("event"))
	q.Search = strings.TrimSpace(r.URL.Query().Get("q"))

	if v := strings.TrimSpace(r.URL.Query().Get("page")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.Page = n
		}
	}
	if v := strings.TrimSpace(r.URL.Query().Get("pageSize")); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			q.PageSize = n
		}
	}

	result, err := logStore.QueryRateLimits(q)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusInternalServerError)
		return
	}

	rw.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(rw).Encode(result)
}

func adminManualBansHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/manualbans",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	switch r.Method {
	case http.MethodGet:
		q := ManualIPBanQuery{}
		q.Search = strings.TrimSpace(r.URL.Query().Get("q"))
		if v := strings.TrimSpace(r.URL.Query().Get("activeOnly")); v != "" {
			q.ActiveOnly = (v == "1" || strings.EqualFold(v, "true"))
		}
		if v := strings.TrimSpace(r.URL.Query().Get("page")); v != "" {
			if n, err := strconv.Atoi(v); err == nil {
				q.Page = n
			}
		}
		if v := strings.TrimSpace(r.URL.Query().Get("pageSize")); v != "" {
			if n, err := strconv.Atoi(v); err == nil {
				q.PageSize = n
			}
		}

		result, err := logStore.QueryManualIPBans(q)
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(result)
		return

	case http.MethodPost:
		type upsertRequest struct {
			IP      string `json:"ip"`
			Minutes int    `json:"minutes"`
			Reason  string `json:"reason"`
		}
		var body upsertRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}
		body.IP = strings.TrimSpace(body.IP)
		ip := parseIP(body.IP)
		if ip == nil {
			http.Error(rw, "Invalid ip", http.StatusBadRequest)
			return
		}
		ipStr := ip.String()

		duration := 24 * time.Hour
		if body.Minutes > 0 {
			duration = time.Duration(body.Minutes) * time.Minute
		}
		now := time.Now().UTC()
		bannedUntil := now.Add(duration)

		if err := logStore.UpsertManualIPBan(ipStr, bannedUntil, body.Reason); err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		setManualBan(ipStr, bannedUntil)

		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(ManualIPBan{IP: ipStr, BannedUntilUTC: bannedUntil.UTC(), Reason: strings.TrimSpace(body.Reason), CreatedUTC: now.UTC()})
		return

	default:
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}
}

// Handles subpaths under /admin/manualbans/
// - DELETE /admin/manualbans/{ip}
func adminManualBansSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/manualbans/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/manualbans/")
	sub = strings.TrimSpace(sub)
	if sub == "" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}
	if strings.Contains(sub, "/") {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}

	if r.Method != http.MethodDelete {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	decoded, err := url.PathUnescape(sub)
	if err != nil {
		http.Error(rw, "Invalid ip", http.StatusBadRequest)
		return
	}
	decoded = strings.TrimSpace(decoded)
	ip := parseIP(decoded)
	if ip == nil {
		http.Error(rw, "Invalid ip", http.StatusBadRequest)
		return
	}
	ipStr := ip.String()

	deleted, err := logStore.DeleteManualIPBan(ipStr)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusInternalServerError)
		return
	}
	clearManualBan(ipStr)

	rw.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
}

// Handles subpaths under /admin/dbmetrics/
// - GET /admin/dbmetrics/stats?bucket=sec|min|30min|hour|day&start=RFC3339&end=RFC3339&metric=db_bytes|rows|free_bytes
func adminDBMetricsSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/dbmetrics/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/dbmetrics/")
	sub = strings.TrimSpace(sub)
	if sub != "stats" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}
	reqLog.Endpoint = "/admin/dbmetrics/stats"

	if r.Method != http.MethodGet {
		http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	bucket := strings.TrimSpace(r.URL.Query().Get("bucket"))
	metric := strings.TrimSpace(r.URL.Query().Get("metric"))

	end := time.Now().UTC()
	if v := strings.TrimSpace(r.URL.Query().Get("end")); v != "" {
		if t, err := time.Parse(time.RFC3339, v); err == nil {
			end = t.UTC()
		} else {
			http.Error(rw, "Invalid end", http.StatusBadRequest)
			return
		}
	}

	startUTC := end.Add(-1 * time.Hour)
	if v := strings.TrimSpace(r.URL.Query().Get("start")); v != "" {
		if t, err := time.Parse(time.RFC3339, v); err == nil {
			startUTC = t.UTC()
		} else {
			http.Error(rw, "Invalid start", http.StatusBadRequest)
			return
		}
	}

	if startUTC.After(end) {
		http.Error(rw, "Invalid range", http.StatusBadRequest)
		return
	}

	result, err := logStore.QueryDBMetricsStats(bucket, startUTC, end, metric)
	if err != nil {
		reqLog.Error = err.Error()
		http.Error(rw, err.Error(), http.StatusBadRequest)
		return
	}

	rw.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(rw).Encode(result)
}

// Handles subpaths under /admin/ratelimits/
// - POST /admin/ratelimits/clear
func adminRateLimitsSubHandler(w http.ResponseWriter, r *http.Request) {
	rw := &statusRecordingResponseWriter{ResponseWriter: w, status: http.StatusOK}
	start := time.Now()
	reqLog := RequestLog{
		TimeUTC:    start.UTC(),
		Endpoint:   "/admin/ratelimits/",
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

	if !isAdminAuthorized(r) {
		reqLog.Error = "Unauthorized"
		http.Error(rw, "Unauthorized", http.StatusUnauthorized)
		return
	}

	sub := strings.TrimPrefix(r.URL.Path, "/admin/ratelimits/")
	if sub == "" {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}
	if strings.Contains(sub, "/") {
		http.Error(rw, "Not found", http.StatusNotFound)
		return
	}

	if sub == "clear" {
		if r.Method != http.MethodPost {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}
		deleted, err := logStore.ClearRateLimits()
		if err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(map[string]any{"deleted": deleted})
		return
	}

	// Manually ban an IP (uses the same in-memory ban mechanism as rate limiting).
	// POST /admin/ratelimits/ban {"ip":"1.2.3.4"}
	if sub == "ban" {
		reqLog.Endpoint = "/admin/ratelimits/ban"
		if r.Method != http.MethodPost {
			http.Error(rw, "Method not allowed", http.StatusMethodNotAllowed)
			return
		}

		type banIPRequest struct {
			IP      string `json:"ip"`
			Minutes int    `json:"minutes"`
		}
		var body banIPRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			http.Error(rw, "Invalid JSON", http.StatusBadRequest)
			return
		}
		body.IP = strings.TrimSpace(body.IP)
		ip := parseIP(body.IP)
		if ip == nil {
			http.Error(rw, "Invalid ip", http.StatusBadRequest)
			return
		}
		ipStr := ip.String()

		duration := 24 * time.Hour
		if body.Minutes > 0 {
			duration = time.Duration(body.Minutes) * time.Minute
		}

		now := time.Now().UTC()
		bannedUntil := now.Add(duration)

		// Store manual bans in a dedicated DB table (separate from rate-limit logs).
		if err := logStore.UpsertManualIPBan(ipStr, bannedUntil, ""); err != nil {
			reqLog.Error = err.Error()
			http.Error(rw, err.Error(), http.StatusInternalServerError)
			return
		}
		setManualBan(ipStr, bannedUntil)

		type banIPResponse struct {
			IP             string    `json:"ip"`
			BannedUntilUTC time.Time `json:"banneduntilutc"`
		}
		rw.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(rw).Encode(banIPResponse{IP: ipStr, BannedUntilUTC: bannedUntil.UTC()})
		return
	}

	http.Error(rw, "Not found", http.StatusNotFound)
}
