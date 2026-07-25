package main

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"os"
	"strings"
	"time"

	"github.com/oschwald/geoip2-golang"
)

type LogStore struct {
	db    *sql.DB
	geoDB *geoip2.Reader
}

type LogQuery struct {
	Endpoint  string
	Search    string
	Status    int
	HasStatus bool
	ErrorOnly bool
	StartUTC  time.Time
	EndUTC    time.Time
	Page      int
	PageSize  int
}

type LogQueryResult struct {
	Total int64        `json:"total"`
	Page  int          `json:"page"`
	Size  int          `json:"pagesize"`
	Logs  []RequestLog `json:"logs"`
}

type LogStatsQuery struct {
	Bucket    string
	StartUTC  time.Time
	EndUTC    time.Time
	Endpoint  string
	ErrorOnly bool
}

type LogStatsPoint struct {
	TimeUTC time.Time `json:"timeutc"`
	Count   int64     `json:"count"`
}

type LogStatsResult struct {
	Bucket   string          `json:"bucket"`
	StartUTC time.Time       `json:"startutc"`
	EndUTC   time.Time       `json:"endutc"`
	Points   []LogStatsPoint `json:"points"`
}

type DBMetricsPoint struct {
	TimeUTC time.Time `json:"timeutc"`
	Value   int64     `json:"value"`
}

type DBMetricsStatsResult struct {
	Bucket   string           `json:"bucket"`
	StartUTC time.Time        `json:"startutc"`
	EndUTC   time.Time        `json:"endutc"`
	Points   []DBMetricsPoint `json:"points"`
}

type RateLimitQuery struct {
	Endpoint string
	ClientIP string
	Event    string
	Search   string
	Page     int
	PageSize int
}

type RateLimitQueryResult struct {
	Total int64          `json:"total"`
	Page  int            `json:"page"`
	Size  int            `json:"pagesize"`
	Logs  []RateLimitLog `json:"logs"`
}

func OpenLogStore(db *sql.DB) (*LogStore, error) {
	if db == nil {
		return nil, fmt.Errorf("db is nil")
	}

	// Optional GeoIP DB. If not set (or file missing), logging proceeds without geo fields.
	var geoDB *geoip2.Reader
	if p := strings.TrimSpace(os.Getenv("BUGGYPYRAMID_GEOIP_DB_PATH")); p != "" {
		if _, err := os.Stat(p); err == nil {
			if r, err := geoip2.Open(p); err == nil {
				geoDB = r
			}
		}
	}

	schema := `
CREATE TABLE IF NOT EXISTS request_logs (
  id BIGSERIAL PRIMARY KEY,
  time_utc TIMESTAMPTZ NOT NULL,
  duration_ms INTEGER NOT NULL,
  endpoint TEXT NOT NULL,
  method TEXT NOT NULL,
  path TEXT NOT NULL,
  remote_addr TEXT,
  client_ip TEXT,
  user_agent TEXT,
  headers_json TEXT,
  payload_raw TEXT,
  payload_unescaped TEXT,
  payload_stripped TEXT,
  payload_json TEXT,
  status INTEGER NOT NULL,
	error TEXT,
	geo_country TEXT,
	geo_city TEXT
);
CREATE INDEX IF NOT EXISTS idx_request_logs_time ON request_logs(time_utc);
CREATE INDEX IF NOT EXISTS idx_request_logs_endpoint ON request_logs(endpoint);
CREATE INDEX IF NOT EXISTS idx_request_logs_status ON request_logs(status);

CREATE TABLE IF NOT EXISTS rate_limit_logs (
  id BIGSERIAL PRIMARY KEY,
  time_utc TIMESTAMPTZ NOT NULL,
  endpoint TEXT NOT NULL,
  method TEXT NOT NULL,
  path TEXT NOT NULL,
  remote_addr TEXT,
  client_ip TEXT,
  user_agent TEXT,
  event TEXT NOT NULL,
  limit_per_sec INTEGER NOT NULL,
  count_this_sec INTEGER NOT NULL,
  window_start_utc TIMESTAMPTZ NOT NULL,
  banned_until_utc TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_rate_limit_logs_time ON rate_limit_logs(time_utc);
CREATE INDEX IF NOT EXISTS idx_rate_limit_logs_ip ON rate_limit_logs(client_ip);

CREATE TABLE IF NOT EXISTS checkpoint_unlocks (
  id BIGSERIAL PRIMARY KEY,
  time_utc TIMESTAMPTZ NOT NULL,
  client_ip TEXT NOT NULL,
  checkpoint TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_checkpoint_unlocks_time ON checkpoint_unlocks(time_utc);
CREATE INDEX IF NOT EXISTS idx_checkpoint_unlocks_ip_time ON checkpoint_unlocks(client_ip, time_utc DESC);
CREATE INDEX IF NOT EXISTS idx_checkpoint_unlocks_checkpoint_time ON checkpoint_unlocks(checkpoint, time_utc DESC);

CREATE TABLE IF NOT EXISTS manual_ip_bans (
	id BIGSERIAL PRIMARY KEY,
	created_utc TIMESTAMPTZ NOT NULL,
	ip TEXT NOT NULL,
	banned_until_utc TIMESTAMPTZ NOT NULL,
	reason TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_manual_ip_bans_ip ON manual_ip_bans(ip);
CREATE INDEX IF NOT EXISTS idx_manual_ip_bans_until ON manual_ip_bans(banned_until_utc);

CREATE TABLE IF NOT EXISTS db_metrics (
	id BIGSERIAL PRIMARY KEY,
	time_utc TIMESTAMPTZ NOT NULL,
	db_size_bytes BIGINT NOT NULL,
	total_rows BIGINT NOT NULL,
	free_disk_bytes BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_db_metrics_time ON db_metrics(time_utc);
`
	if _, err := db.Exec(schema); err != nil {
		return nil, err
	}

	// Ensure columns exist if the table already existed before we added them.
	_, _ = db.Exec("ALTER TABLE request_logs ADD COLUMN IF NOT EXISTS geo_country TEXT")
	_, _ = db.Exec("ALTER TABLE request_logs ADD COLUMN IF NOT EXISTS geo_city TEXT")

	return &LogStore{db: db, geoDB: geoDB}, nil
}

func (s *LogStore) UpsertManualIPBan(ip string, bannedUntilUTC time.Time, reason string) error {
	if s == nil || s.db == nil {
		return fmt.Errorf("log store is nil")
	}
	ip = strings.TrimSpace(ip)
	if ip == "" {
		return fmt.Errorf("ip is empty")
	}

	_, err := s.db.Exec(
		postgresifyPlaceholders(`INSERT INTO manual_ip_bans (
			created_utc, ip, banned_until_utc, reason
		) VALUES (?,?,?,?)
		ON CONFLICT (ip) DO UPDATE SET
			banned_until_utc = EXCLUDED.banned_until_utc,
			reason = EXCLUDED.reason`),
		time.Now().UTC(),
		ip,
		bannedUntilUTC.UTC(),
		strings.TrimSpace(reason),
	)
	return err
}

func (s *LogStore) ListActiveManualIPBans(nowUTC time.Time) (map[string]time.Time, error) {
	if s == nil || s.db == nil {
		return nil, fmt.Errorf("log store is nil")
	}

	rows, err := s.db.Query(
		postgresifyPlaceholders(`SELECT ip, banned_until_utc FROM manual_ip_bans WHERE banned_until_utc > ?`),
		nowUTC.UTC(),
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	out := map[string]time.Time{}
	for rows.Next() {
		var ip string
		var until time.Time
		if err := rows.Scan(&ip, &until); err != nil {
			return nil, err
		}
		ip = strings.TrimSpace(ip)
		if ip == "" {
			continue
		}
		out[ip] = until.UTC()
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	return out, nil
}

func (s *LogStore) QueryManualIPBans(q ManualIPBanQuery) (ManualIPBanQueryResult, error) {
	if s == nil || s.db == nil {
		return ManualIPBanQueryResult{}, fmt.Errorf("log store is nil")
	}

	search := strings.TrimSpace(q.Search)
	page := q.Page
	pageSize := q.PageSize
	if page < 0 {
		page = 0
	}
	if pageSize <= 0 {
		pageSize = 100
	}
	if pageSize > 500 {
		pageSize = 500
	}
	offset := page * pageSize

	nowUTC := time.Now().UTC()

	where := "WHERE 1=1"
	args := []any{}
	if search != "" {
		where += " AND (ip ILIKE CONCAT('%', ?, '%') OR reason ILIKE CONCAT('%', ?, '%'))"
		args = append(args, search, search)
	}
	if q.ActiveOnly {
		where += " AND banned_until_utc > ?"
		args = append(args, nowUTC)
	}

	// Count
	countSQL := postgresifyPlaceholders("SELECT COUNT(*) FROM manual_ip_bans " + where)
	var total int64
	if err := s.db.QueryRow(countSQL, args...).Scan(&total); err != nil {
		return ManualIPBanQueryResult{}, err
	}

	// Page
	pageSQL := "SELECT id, created_utc, ip, banned_until_utc, COALESCE(reason, '') FROM manual_ip_bans " + where +
		" ORDER BY banned_until_utc DESC, ip ASC LIMIT ? OFFSET ?"
	pageArgs := append(append([]any{}, args...), pageSize, offset)
	rows, err := s.db.Query(postgresifyPlaceholders(pageSQL), pageArgs...)
	if err != nil {
		return ManualIPBanQueryResult{}, err
	}
	defer rows.Close()

	bans := []ManualIPBan{}
	for rows.Next() {
		var b ManualIPBan
		if err := rows.Scan(&b.ID, &b.CreatedUTC, &b.IP, &b.BannedUntilUTC, &b.Reason); err != nil {
			return ManualIPBanQueryResult{}, err
		}
		b.CreatedUTC = b.CreatedUTC.UTC()
		b.BannedUntilUTC = b.BannedUntilUTC.UTC()
		b.IP = strings.TrimSpace(b.IP)
		bans = append(bans, b)
	}
	if err := rows.Err(); err != nil {
		return ManualIPBanQueryResult{}, err
	}

	return ManualIPBanQueryResult{Total: total, Page: page, Size: pageSize, Bans: bans}, nil
}

func (s *LogStore) DeleteManualIPBan(ip string) (bool, error) {
	if s == nil || s.db == nil {
		return false, fmt.Errorf("log store is nil")
	}
	ip = strings.TrimSpace(ip)
	if ip == "" {
		return false, fmt.Errorf("ip is empty")
	}

	res, err := s.db.Exec(postgresifyPlaceholders("DELETE FROM manual_ip_bans WHERE ip = ?"), ip)
	if err != nil {
		return false, err
	}
	n, _ := res.RowsAffected()
	return n > 0, nil
}

func (s *LogStore) Close() error {
	if s != nil && s.geoDB != nil {
		_ = s.geoDB.Close()
		s.geoDB = nil
	}
	// The shared DB pool is owned by main.
	return nil
}

func (s *LogStore) enrichGeo(logEntry *RequestLog) {
	if s == nil || s.geoDB == nil || logEntry == nil {
		return
	}
	if strings.TrimSpace(logEntry.GeoCountry) != "" || strings.TrimSpace(logEntry.GeoCity) != "" {
		return
	}

	ip := net.ParseIP(strings.TrimSpace(logEntry.ClientIP))
	if ip == nil {
		return
	}

	rec, err := s.geoDB.City(ip)
	if err != nil {
		return
	}
	if rec == nil {
		return
	}

	if rec.Country.IsoCode != "" {
		logEntry.GeoCountry = rec.Country.IsoCode
	}
	if rec.City.Names != nil {
		if v := rec.City.Names["en"]; v != "" {
			logEntry.GeoCity = v
		}
	}
}

func (s *LogStore) LookupGeoForIP(ipStr string) (country string, city string) {
	if s == nil || s.geoDB == nil {
		return "", ""
	}

	ip := net.ParseIP(strings.TrimSpace(ipStr))
	if ip == nil {
		return "", ""
	}

	rec, err := s.geoDB.City(ip)
	if err != nil || rec == nil {
		return "", ""
	}

	if rec.Country.IsoCode != "" {
		country = rec.Country.IsoCode
	}
	if rec.City.Names != nil {
		if v := rec.City.Names["en"]; v != "" {
			city = v
		}
	}
	return country, city
}

func (s *LogStore) Insert(logEntry *RequestLog) {
	if s == nil || s.db == nil || logEntry == nil {
		return
	}

	s.enrichGeo(logEntry)

	headersJSON := ""
	if logEntry.Headers != nil {
		if b, err := json.Marshal(logEntry.Headers); err == nil {
			headersJSON = string(b)
		}
	}

	// Keep payloads bounded to avoid db bloat from accidents.
	payloadJSON := truncateForLog(logEntry.PayloadJSON, 8192)
	payloadRaw := truncateForLog(logEntry.PayloadPathSegmentRaw, 4096)
	payloadUnescaped := truncateForLog(logEntry.PayloadPathSegmentUnescaped, 4096)
	payloadStripped := truncateForLog(logEntry.PayloadPathSegmentStripped, 4096)
	errStr := truncateForLog(logEntry.Error, 2048)
	ua := truncateForLog(logEntry.UserAgent, 512)

	_, _ = s.db.Exec(
		postgresifyPlaceholders(`INSERT INTO request_logs (
			time_utc, duration_ms, endpoint, method, path,
			remote_addr, client_ip, user_agent,
			geo_country, geo_city,
			headers_json,
			payload_raw, payload_unescaped, payload_stripped, payload_json,
			status, error
		) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`),
		logEntry.TimeUTC.UTC(),
		logEntry.DurationMs,
		logEntry.Endpoint,
		logEntry.Method,
		logEntry.Path,
		logEntry.RemoteAddr,
		logEntry.ClientIP,
		ua,
		truncateForLog(logEntry.GeoCountry, 16),
		truncateForLog(logEntry.GeoCity, 128),
		headersJSON,
		payloadRaw,
		payloadUnescaped,
		payloadStripped,
		payloadJSON,
		logEntry.Status,
		errStr,
	)
}

func (s *LogStore) InsertRateLimit(entry *RateLimitLog) {
	if s == nil || s.db == nil || entry == nil {
		return
	}

	ua := truncateForLog(entry.UserAgent, 512)

	// Avoid inserting a new row for every request during an active ban.
	// Instead, aggregate repeated "blocked" hits by incrementing count_this_sec.
	if entry.Event == "blocked" {
		if res, err := s.db.Exec(
			postgresifyPlaceholders(`UPDATE rate_limit_logs
				SET time_utc = ?, method = ?, path = ?, remote_addr = ?, user_agent = ?,
					count_this_sec = count_this_sec + 1
				WHERE endpoint = ? AND client_ip = ? AND event = ?
					AND window_start_utc = ? AND banned_until_utc = ?`),
			entry.TimeUTC.UTC(),
			entry.Method,
			entry.Path,
			entry.RemoteAddr,
			ua,
			entry.Endpoint,
			entry.ClientIP,
			entry.Event,
			entry.WindowStartUTC.UTC(),
			entry.BannedUntilUTC.UTC(),
		); err == nil {
			if n, _ := res.RowsAffected(); n > 0 {
				return
			}
		}

		// First blocked request for this ban window.
		_, _ = s.db.Exec(
			postgresifyPlaceholders(`INSERT INTO rate_limit_logs (
				time_utc, endpoint, method, path,
				remote_addr, client_ip, user_agent,
				event, limit_per_sec, count_this_sec,
				window_start_utc, banned_until_utc
			) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`),
			entry.TimeUTC.UTC(),
			entry.Endpoint,
			entry.Method,
			entry.Path,
			entry.RemoteAddr,
			entry.ClientIP,
			ua,
			entry.Event,
			entry.LimitPerSecond,
			1,
			entry.WindowStartUTC.UTC(),
			entry.BannedUntilUTC.UTC(),
		)
		return
	}

	_, _ = s.db.Exec(
		postgresifyPlaceholders(`INSERT INTO rate_limit_logs (
			time_utc, endpoint, method, path,
			remote_addr, client_ip, user_agent,
			event, limit_per_sec, count_this_sec,
			window_start_utc, banned_until_utc
		) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`),
		entry.TimeUTC.UTC(),
		entry.Endpoint,
		entry.Method,
		entry.Path,
		entry.RemoteAddr,
		entry.ClientIP,
		ua,
		entry.Event,
		entry.LimitPerSecond,
		entry.CountThisSec,
		entry.WindowStartUTC.UTC(),
		entry.BannedUntilUTC.UTC(),
	)
}

func (s *LogStore) InsertDBMetricsSnapshot(now time.Time, dbSizeBytes, totalRows, freeDiskBytes int64) {
	if s == nil || s.db == nil {
		return
	}
	_, _ = s.db.Exec(
		postgresifyPlaceholders(`INSERT INTO db_metrics (time_utc, db_size_bytes, total_rows, free_disk_bytes)
			VALUES (?,?,?,?)`),
		now.UTC(),
		dbSizeBytes,
		totalRows,
		freeDiskBytes,
	)
}

func (s *LogStore) CaptureAndInsertDBMetrics(diskPath string) error {
	if s == nil || s.db == nil {
		return fmt.Errorf("log store not initialized")
	}
	now := time.Now().UTC()

	var dbSize int64
	if err := s.db.QueryRow("SELECT pg_database_size(current_database())").Scan(&dbSize); err != nil {
		return err
	}

	// Total rows across all user tables (estimate via stats; fast and broad).
	var totalRows int64
	if err := s.db.QueryRow("SELECT COALESCE(SUM(n_live_tup)::bigint, 0) FROM pg_stat_user_tables").Scan(&totalRows); err != nil {
		return err
	}

	freeBytesU64, err := diskFreeBytes(diskPath)
	if err != nil {
		return err
	}
	freeBytes := int64(freeBytesU64)

	s.InsertDBMetricsSnapshot(now, dbSize, totalRows, freeBytes)
	return nil
}

func (s *LogStore) QueryDBMetricsStats(bucket string, startUTC, endUTC time.Time, metric string) (DBMetricsStatsResult, error) {
	res := DBMetricsStatsResult{}
	if s == nil || s.db == nil {
		return res, fmt.Errorf("log store not initialized")
	}

	bucketNorm := ""
	truncUnit := ""
	{
		v := strings.TrimSpace(strings.ToLower(bucket))
		switch v {
		case "30m", "30min", "30mins", "30minute", "30minutes":
			bucketNorm = "30min"
		default:
			b, tu, err := normalizeStatsBucket(bucket)
			if err != nil {
				return res, err
			}
			bucketNorm = b
			truncUnit = tu
		}
	}

	start := startUTC.UTC()
	end := endUTC.UTC()
	if start.IsZero() {
		end = time.Now().UTC()
		start = end.Add(-1 * time.Hour)
	}
	if end.IsZero() {
		end = time.Now().UTC()
	}
	if start.After(end) {
		return res, fmt.Errorf("invalid range")
	}

	startBucket := truncateToBucketUTC(start, bucketNorm)
	endBucket := truncateToBucketUTC(end, bucketNorm)

	const maxPoints = 20000
	if n := estimateBucketCount(startBucket, endBucket, bucketNorm); n > maxPoints {
		return res, fmt.Errorf("range too large for bucket")
	}

	col := ""
	switch strings.TrimSpace(metric) {
	case "db_bytes":
		col = "db_size_bytes"
	case "rows":
		col = "total_rows"
	case "free_bytes":
		col = "free_disk_bytes"
	default:
		return res, fmt.Errorf("invalid metric")
	}

	bucketExpr := ""
	if bucketNorm == "30min" {
		// Bucket to 30-minute intervals without relying on date_bin.
		bucketExpr = "((date_trunc('hour', time_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC') + (floor(extract(minute from time_utc AT TIME ZONE 'UTC') / 30) * interval '30 minutes'))"
	} else {
		bucketExpr = "(date_trunc('" + truncUnit + "', time_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')"
	}
	rowsSQL := "SELECT " + bucketExpr + " AS bucket, AVG(" + col + ")::bigint FROM db_metrics WHERE time_utc >= ? AND time_utc <= ? GROUP BY bucket ORDER BY bucket ASC"

	rows, err := s.db.Query(postgresifyPlaceholders(rowsSQL), startBucket, end)
	if err != nil {
		return res, err
	}
	defer func() { _ = rows.Close() }()

	counts := make(map[int64]int64, 1024)
	for rows.Next() {
		var b time.Time
		var v int64
		if err := rows.Scan(&b, &v); err != nil {
			return res, err
		}
		b = truncateToBucketUTC(b, bucketNorm)
		counts[b.UnixNano()] = v
	}
	if err := rows.Err(); err != nil {
		return res, err
	}

	points := make([]DBMetricsPoint, 0, estimateBucketCount(startBucket, endBucket, bucketNorm))
	for t := startBucket; !t.After(endBucket); t = addBucketUTC(t, bucketNorm) {
		points = append(points, DBMetricsPoint{TimeUTC: t, Value: counts[t.UnixNano()]})
		if len(points) > maxPoints {
			return res, fmt.Errorf("range too large for bucket")
		}
	}

	res.Bucket = bucketNorm
	res.StartUTC = startBucket
	res.EndUTC = end
	res.Points = points
	return res, nil
}

func (s *LogStore) Query(q LogQuery) (LogQueryResult, error) {
	res := LogQueryResult{}
	if s == nil || s.db == nil {
		return res, fmt.Errorf("log store not initialized")
	}

	page := q.Page
	if page < 0 {
		page = 0
	}
	size := q.PageSize
	if size <= 0 {
		size = 100
	}
	if size > 500 {
		size = 500
	}
	offset := page * size

	where := make([]string, 0, 8)
	args := make([]any, 0, 8)

	if strings.TrimSpace(q.Endpoint) != "" {
		// Support filtering multiple endpoints via comma-separated list.
		// (Admin UI uses this for multi-select endpoint filters.)
		raw := strings.TrimSpace(q.Endpoint)
		parts := strings.Split(raw, ",")
		endpoints := make([]string, 0, len(parts))
		for _, p := range parts {
			p = strings.TrimSpace(p)
			if p == "" {
				continue
			}
			endpoints = append(endpoints, p)
		}
		if len(endpoints) == 1 {
			where = append(where, "endpoint = ?")
			args = append(args, endpoints[0])
		} else if len(endpoints) > 1 {
			if len(endpoints) > 50 {
				endpoints = endpoints[:50]
			}
			placeholders := make([]string, 0, len(endpoints))
			for i := 0; i < len(endpoints); i++ {
				placeholders = append(placeholders, "?")
			}
			where = append(where, "endpoint IN ("+strings.Join(placeholders, ",")+")")
			for _, ep := range endpoints {
				args = append(args, ep)
			}
		}
	}
	if q.HasStatus {
		where = append(where, "status = ?")
		args = append(args, q.Status)
	}
	if !q.StartUTC.IsZero() {
		where = append(where, "time_utc >= ?")
		args = append(args, q.StartUTC.UTC())
	}
	if !q.EndUTC.IsZero() {
		where = append(where, "time_utc <= ?")
		args = append(args, q.EndUTC.UTC())
	}
	if q.ErrorOnly {
		where = append(where, "error IS NOT NULL AND error != ''")
	}
	if sTerm := strings.TrimSpace(q.Search); sTerm != "" {
		like := "%" + sTerm + "%"
		where = append(where, "(path LIKE ? OR client_ip LIKE ? OR user_agent LIKE ? OR payload_json LIKE ? OR error LIKE ?)")
		args = append(args, like, like, like, like, like)
	}

	whereSQL := ""
	if len(where) > 0 {
		whereSQL = "WHERE " + strings.Join(where, " AND ")
	}

	countSQL := "SELECT COUNT(1) FROM request_logs " + whereSQL
	var total int64
	if err := s.db.QueryRow(postgresifyPlaceholders(countSQL), args...).Scan(&total); err != nil {
		return res, err
	}

	rowsSQL := `SELECT id, time_utc, duration_ms, endpoint, method, path,
		remote_addr, client_ip, geo_country, geo_city, user_agent, headers_json,
		payload_raw, payload_unescaped, payload_stripped, payload_json,
		status, error
		FROM request_logs ` + whereSQL + ` ORDER BY id DESC LIMIT ? OFFSET ?`

	rowArgs := append(args, size, offset)
	rows, err := s.db.Query(postgresifyPlaceholders(rowsSQL), rowArgs...)
	if err != nil {
		return res, err
	}
	defer rows.Close()

	logs := make([]RequestLog, 0, size)
	for rows.Next() {
		var (
			id                                                         int64
			tm                                                         time.Time
			dur                                                        int64
			endpoint, method, path                                     string
			remoteAddr, clientIP, geoCountry, geoCity, userAgent       sql.NullString
			headersJSON                                                sql.NullString
			payloadRaw, payloadUnescaped, payloadStripped, payloadJSON sql.NullString
			status                                                     int
			errStr                                                     sql.NullString
		)
		if err := rows.Scan(
			&id, &tm, &dur, &endpoint, &method, &path,
			&remoteAddr, &clientIP, &geoCountry, &geoCity, &userAgent, &headersJSON,
			&payloadRaw, &payloadUnescaped, &payloadStripped, &payloadJSON,
			&status, &errStr,
		); err != nil {
			return res, err
		}
		entry := RequestLog{
			ID:                          id,
			TimeUTC:                     tm.UTC(),
			DurationMs:                  dur,
			Endpoint:                    endpoint,
			Method:                      method,
			Path:                        path,
			RemoteAddr:                  remoteAddr.String,
			ClientIP:                    clientIP.String,
			GeoCountry:                  geoCountry.String,
			GeoCity:                     geoCity.String,
			UserAgent:                   userAgent.String,
			PayloadPathSegmentRaw:       payloadRaw.String,
			PayloadPathSegmentUnescaped: payloadUnescaped.String,
			PayloadPathSegmentStripped:  payloadStripped.String,
			PayloadJSON:                 payloadJSON.String,
			Status:                      status,
			Error:                       errStr.String,
		}
		if headersJSON.Valid && headersJSON.String != "" {
			var hdr http.Header
			if err := json.Unmarshal([]byte(headersJSON.String), &hdr); err == nil {
				entry.Headers = hdr
			}
		}

		logs = append(logs, entry)
	}
	if err := rows.Err(); err != nil {
		return res, err
	}

	res.Total = total
	res.Page = page
	res.Size = size
	res.Logs = logs
	return res, nil
}

func normalizeStatsBucket(raw string) (bucket string, truncUnit string, err error) {
	v := strings.TrimSpace(strings.ToLower(raw))
	if v == "" {
		v = "min"
	}
	switch v {
	case "s", "sec", "secs", "second", "seconds":
		return "sec", "second", nil
	case "m", "min", "mins", "minute", "minutes":
		return "min", "minute", nil
	case "h", "hr", "hour", "hours":
		return "hour", "hour", nil
	case "d", "day", "days":
		return "day", "day", nil
	default:
		return "", "", fmt.Errorf("invalid bucket")
	}
}

func truncateToBucketUTC(t time.Time, bucket string) time.Time {
	t = t.UTC()
	switch bucket {
	case "sec":
		return time.Date(t.Year(), t.Month(), t.Day(), t.Hour(), t.Minute(), t.Second(), 0, time.UTC)
	case "min":
		return time.Date(t.Year(), t.Month(), t.Day(), t.Hour(), t.Minute(), 0, 0, time.UTC)
	case "30min":
		m := (t.Minute() / 30) * 30
		return time.Date(t.Year(), t.Month(), t.Day(), t.Hour(), m, 0, 0, time.UTC)
	case "hour":
		return time.Date(t.Year(), t.Month(), t.Day(), t.Hour(), 0, 0, 0, time.UTC)
	case "day":
		return time.Date(t.Year(), t.Month(), t.Day(), 0, 0, 0, 0, time.UTC)
	default:
		return t
	}
}

func addBucketUTC(t time.Time, bucket string) time.Time {
	switch bucket {
	case "sec":
		return t.Add(1 * time.Second)
	case "min":
		return t.Add(1 * time.Minute)
	case "30min":
		return t.Add(30 * time.Minute)
	case "hour":
		return t.Add(1 * time.Hour)
	case "day":
		return t.AddDate(0, 0, 1)
	default:
		return t
	}
}

func estimateBucketCount(start, end time.Time, bucket string) int {
	if end.Before(start) {
		return 0
	}
	d := end.Sub(start)
	switch bucket {
	case "sec":
		return int(d/time.Second) + 1
	case "min":
		return int(d/time.Minute) + 1
	case "30min":
		return int(d/(30*time.Minute)) + 1
	case "hour":
		return int(d/time.Hour) + 1
	case "day":
		return int(d/(24*time.Hour)) + 1
	default:
		return 0
	}
}

func (s *LogStore) QueryStats(q LogStatsQuery) (LogStatsResult, error) {
	res := LogStatsResult{}
	if s == nil || s.db == nil {
		return res, fmt.Errorf("log store not initialized")
	}

	bucket, truncUnit, err := normalizeStatsBucket(q.Bucket)
	if err != nil {
		return res, err
	}

	start := q.StartUTC.UTC()
	end := q.EndUTC.UTC()
	if start.IsZero() {
		end = time.Now().UTC()
		start = end.Add(-1 * time.Hour)
	}
	if end.IsZero() {
		end = time.Now().UTC()
	}
	if start.After(end) {
		return res, fmt.Errorf("invalid range")
	}

	startBucket := truncateToBucketUTC(start, bucket)
	endBucket := truncateToBucketUTC(end, bucket)

	// Avoid returning huge payloads by accident.
	const maxPoints = 20000
	if n := estimateBucketCount(startBucket, endBucket, bucket); n > maxPoints {
		return res, fmt.Errorf("range too large for bucket")
	}

	where := make([]string, 0, 10)
	args := make([]any, 0, 10)

	where = append(where, "time_utc >= ? AND time_utc <= ?")
	args = append(args, startBucket, end)

	if strings.TrimSpace(q.Endpoint) != "" {
		raw := strings.TrimSpace(q.Endpoint)
		parts := strings.Split(raw, ",")
		endpoints := make([]string, 0, len(parts))
		for _, p := range parts {
			p = strings.TrimSpace(p)
			if p == "" {
				continue
			}
			endpoints = append(endpoints, p)
		}
		if len(endpoints) == 1 {
			where = append(where, "endpoint = ?")
			args = append(args, endpoints[0])
		} else if len(endpoints) > 1 {
			if len(endpoints) > 50 {
				endpoints = endpoints[:50]
			}
			placeholders := make([]string, 0, len(endpoints))
			for i := 0; i < len(endpoints); i++ {
				placeholders = append(placeholders, "?")
			}
			where = append(where, "endpoint IN ("+strings.Join(placeholders, ",")+")")
			for _, ep := range endpoints {
				args = append(args, ep)
			}
		}
	}
	if q.ErrorOnly {
		where = append(where, "error IS NOT NULL AND error != ''")
	}

	whereSQL := ""
	if len(where) > 0 {
		whereSQL = "WHERE " + strings.Join(where, " AND ")
	}

	bucketExpr := "(date_trunc('" + truncUnit + "', time_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')"
	rowsSQL := "SELECT " + bucketExpr + " AS bucket, COUNT(1) FROM request_logs " + whereSQL + " GROUP BY bucket ORDER BY bucket ASC"

	rows, err := s.db.Query(postgresifyPlaceholders(rowsSQL), args...)
	if err != nil {
		return res, err
	}
	defer func() { _ = rows.Close() }()

	counts := make(map[int64]int64, 1024)
	for rows.Next() {
		var b time.Time
		var c int64
		if err := rows.Scan(&b, &c); err != nil {
			return res, err
		}
		b = truncateToBucketUTC(b, bucket)
		counts[b.UnixNano()] = c
	}
	if err := rows.Err(); err != nil {
		return res, err
	}

	points := make([]LogStatsPoint, 0, estimateBucketCount(startBucket, endBucket, bucket))
	for t := startBucket; !t.After(endBucket); t = addBucketUTC(t, bucket) {
		points = append(points, LogStatsPoint{TimeUTC: t, Count: counts[t.UnixNano()]})
		if len(points) > maxPoints {
			return res, fmt.Errorf("range too large for bucket")
		}
	}

	res.Bucket = bucket
	res.StartUTC = startBucket
	res.EndUTC = end
	res.Points = points
	return res, nil
}

func (s *LogStore) QueryRateLimits(q RateLimitQuery) (RateLimitQueryResult, error) {
	res := RateLimitQueryResult{}
	if s == nil || s.db == nil {
		return res, fmt.Errorf("log store not initialized")
	}

	page := q.Page
	if page < 0 {
		page = 0
	}
	size := q.PageSize
	if size <= 0 {
		size = 100
	}
	if size > 500 {
		size = 500
	}
	offset := page * size

	where := make([]string, 0, 6)
	args := make([]any, 0, 6)

	if strings.TrimSpace(q.Endpoint) != "" {
		where = append(where, "endpoint = ?")
		args = append(args, q.Endpoint)
	}
	if strings.TrimSpace(q.ClientIP) != "" {
		where = append(where, "client_ip = ?")
		args = append(args, q.ClientIP)
	}
	if strings.TrimSpace(q.Event) != "" {
		where = append(where, "event = ?")
		args = append(args, q.Event)
	}
	if sTerm := strings.TrimSpace(q.Search); sTerm != "" {
		like := "%" + sTerm + "%"
		where = append(where, "(path LIKE ? OR client_ip LIKE ? OR user_agent LIKE ? OR endpoint LIKE ? OR event LIKE ?)")
		args = append(args, like, like, like, like, like)
	}

	whereSQL := ""
	if len(where) > 0 {
		whereSQL = "WHERE " + strings.Join(where, " AND ")
	}

	var total int64
	if err := s.db.QueryRow(postgresifyPlaceholders("SELECT COUNT(1) FROM rate_limit_logs "+whereSQL), args...).Scan(&total); err != nil {
		return res, err
	}

	rowsSQL := `SELECT id, time_utc, endpoint, method, path,
		remote_addr, client_ip, user_agent,
		event, limit_per_sec, count_this_sec,
		window_start_utc, banned_until_utc
		FROM rate_limit_logs ` + whereSQL + ` ORDER BY id DESC LIMIT ? OFFSET ?`

	rowArgs := append(args, size, offset)
	rows, err := s.db.Query(postgresifyPlaceholders(rowsSQL), rowArgs...)
	if err != nil {
		return res, err
	}
	defer rows.Close()

	logs := make([]RateLimitLog, 0, size)
	for rows.Next() {
		var (
			id                              int64
			tm                              time.Time
			endpoint, method, path          string
			remoteAddr, clientIP, userAgent sql.NullString
			event                           string
			limitPerSec, countThisSec       int
			windowStartUTC, bannedUntilUTC  time.Time
		)
		if err := rows.Scan(
			&id, &tm, &endpoint, &method, &path,
			&remoteAddr, &clientIP, &userAgent,
			&event, &limitPerSec, &countThisSec,
			&windowStartUTC, &bannedUntilUTC,
		); err != nil {
			return res, err
		}
		logs = append(logs, RateLimitLog{
			ID:             id,
			TimeUTC:        tm.UTC(),
			Endpoint:       endpoint,
			Method:         method,
			Path:           path,
			RemoteAddr:     remoteAddr.String,
			ClientIP:       clientIP.String,
			UserAgent:      userAgent.String,
			Event:          event,
			LimitPerSecond: limitPerSec,
			CountThisSec:   countThisSec,
			WindowStartUTC: windowStartUTC.UTC(),
			BannedUntilUTC: bannedUntilUTC.UTC(),
		})
	}
	if err := rows.Err(); err != nil {
		return res, err
	}

	res.Total = total
	res.Page = page
	res.Size = size
	res.Logs = logs
	return res, nil
}

func (s *LogStore) GetByID(id int64) (*RequestLog, bool, error) {
	if s == nil || s.db == nil {
		return nil, false, fmt.Errorf("log store not initialized")
	}
	if id <= 0 {
		return nil, false, nil
	}

	row := s.db.QueryRow(postgresifyPlaceholders(`SELECT id, time_utc, duration_ms, endpoint, method, path,
		remote_addr, client_ip, geo_country, geo_city, user_agent, headers_json,
		payload_raw, payload_unescaped, payload_stripped, payload_json,
		status, error
		FROM request_logs WHERE id = ?`), id)

	var (
		tm                                                         time.Time
		dur                                                        int64
		endpoint, method, path                                     string
		remoteAddr, clientIP, geoCountry, geoCity, userAgent       sql.NullString
		headersJSON                                                sql.NullString
		payloadRaw, payloadUnescaped, payloadStripped, payloadJSON sql.NullString
		status                                                     int
		errStr                                                     sql.NullString
	)

	var gotID int64
	if err := row.Scan(
		&gotID, &tm, &dur, &endpoint, &method, &path,
		&remoteAddr, &clientIP, &geoCountry, &geoCity, &userAgent, &headersJSON,
		&payloadRaw, &payloadUnescaped, &payloadStripped, &payloadJSON,
		&status, &errStr,
	); err != nil {
		if err == sql.ErrNoRows {
			return nil, false, nil
		}
		return nil, false, err
	}

	entry := &RequestLog{
		ID:                          gotID,
		TimeUTC:                     tm.UTC(),
		DurationMs:                  dur,
		Endpoint:                    endpoint,
		Method:                      method,
		Path:                        path,
		RemoteAddr:                  remoteAddr.String,
		ClientIP:                    clientIP.String,
		GeoCountry:                  geoCountry.String,
		GeoCity:                     geoCity.String,
		UserAgent:                   userAgent.String,
		PayloadPathSegmentRaw:       payloadRaw.String,
		PayloadPathSegmentUnescaped: payloadUnescaped.String,
		PayloadPathSegmentStripped:  payloadStripped.String,
		PayloadJSON:                 payloadJSON.String,
		Status:                      status,
		Error:                       errStr.String,
	}
	if headersJSON.Valid && headersJSON.String != "" {
		var hdr http.Header
		if err := json.Unmarshal([]byte(headersJSON.String), &hdr); err == nil {
			entry.Headers = hdr
		}
	}

	return entry, true, nil
}

func (s *LogStore) ClearAll() (int64, error) {
	if s == nil || s.db == nil {
		return 0, fmt.Errorf("log store not initialized")
	}
	res, err := s.db.Exec("TRUNCATE request_logs")
	if err != nil {
		return 0, err
	}
	n, _ := res.RowsAffected()
	return n, nil
}

func (s *LogStore) ClearRateLimits() (int64, error) {
	if s == nil || s.db == nil {
		return 0, fmt.Errorf("log store not initialized")
	}
	res, err := s.db.Exec("TRUNCATE rate_limit_logs")
	if err != nil {
		return 0, err
	}
	n, _ := res.RowsAffected()
	return n, nil
}
