package main

import (
	"database/sql"
	"errors"
	"fmt"
	"strings"
	"time"
)

var ErrPlayerAlreadyExists = errors.New("player already exists")

type PlayerStore struct {
	db *sql.DB
}

type PlayerQuery struct {
	Search   string
	ClientIP string
	Order    string
	Page     int
	PageSize int
}

type PlayerQueryResult struct {
	Total   int64        `json:"total"`
	Page    int          `json:"page"`
	Size    int          `json:"pagesize"`
	Players []PlayerData `json:"players"`
}

func OpenPlayerStore(db *sql.DB) (*PlayerStore, error) {
	if db == nil {
		return nil, fmt.Errorf("db is nil")
	}

	schema := `
CREATE TABLE IF NOT EXISTS players (
  playername TEXT PRIMARY KEY,
  completion_seconds DOUBLE PRECISION NOT NULL,
	added_time_ns BIGINT NOT NULL,
	client_ip TEXT,
	geo_country TEXT,
	geo_city TEXT
);
ALTER TABLE players ADD COLUMN IF NOT EXISTS client_ip TEXT;
ALTER TABLE players ADD COLUMN IF NOT EXISTS geo_country TEXT;
ALTER TABLE players ADD COLUMN IF NOT EXISTS geo_city TEXT;
CREATE INDEX IF NOT EXISTS idx_players_sort ON players(completion_seconds, added_time_ns, playername);
CREATE INDEX IF NOT EXISTS idx_players_added_time ON players(added_time_ns);
CREATE INDEX IF NOT EXISTS idx_players_client_ip ON players(client_ip);
`
	if _, err := db.Exec(schema); err != nil {
		return nil, err
	}

	return &PlayerStore{db: db}, nil
}

func (s *PlayerStore) Close() error {
	// The shared DB pool is owned by main.
	return nil
}

// SubmitTime inserts a new time for playerName or improves it if completionSeconds is faster.
// Returns one of: "Time added", "Time improved", "Time not improved".
func (s *PlayerStore) SubmitTime(playerName string, completionSeconds float64, now time.Time, clientIP string, geoCountry string, geoCity string) (string, error) {
	if s == nil || s.db == nil {
		return "", fmt.Errorf("player store not initialized")
	}
	if playerName == "" {
		return "", fmt.Errorf("missing playername")
	}
	if !(completionSeconds > 0) {
		return "", fmt.Errorf("invalid completionseconds")
	}
	clientIP = strings.TrimSpace(clientIP)
	geoCountry = strings.TrimSpace(geoCountry)
	geoCity = strings.TrimSpace(geoCity)

	// Concurrency-safe flow:
	// 1) Try insert (ignored if already exists)
	// 2) If not inserted, conditionally update only when the new time is faster.
	// This avoids a SELECT-before-INSERT race that can produce UNIQUE constraint errors.

	tx, err := s.db.Begin()
	if err != nil {
		return "", err
	}
	defer func() { _ = tx.Rollback() }()

	insertRes, err := tx.Exec(
		postgresifyPlaceholders("INSERT INTO players(playername, completion_seconds, added_time_ns, client_ip, geo_country, geo_city) VALUES (?,?,?,?,?,?) ON CONFLICT (playername) DO NOTHING"),
		playerName,
		completionSeconds,
		now.UTC().UnixNano(),
		clientIP,
		geoCountry,
		geoCity,
	)
	if err != nil {
		return "", err
	}
	inserted, _ := insertRes.RowsAffected()
	if inserted > 0 {
		if err := tx.Commit(); err != nil {
			return "", err
		}
		return "Time added", nil
	}

	updateRes, err := tx.Exec(
		postgresifyPlaceholders("UPDATE players SET completion_seconds = ?, added_time_ns = ?, client_ip = ?, geo_country = ?, geo_city = ? WHERE playername = ? AND ? < completion_seconds"),
		completionSeconds,
		now.UTC().UnixNano(),
		clientIP,
		geoCountry,
		geoCity,
		playerName,
		completionSeconds,
	)
	if err != nil {
		return "", err
	}
	updated, _ := updateRes.RowsAffected()
	if err := tx.Commit(); err != nil {
		return "", err
	}
	if updated > 0 {
		return "Time improved", nil
	}
	return "Time not improved", nil
}

// SetPlayerTime creates a new player row.
// If the player already exists, returns ErrPlayerAlreadyExists.
// This is intended for admin tooling.
func (s *PlayerStore) SetPlayerTime(playerName string, completionSeconds float64, now time.Time, clientIP string, geoCountry string, geoCity string) (created bool, err error) {
	if s == nil || s.db == nil {
		return false, fmt.Errorf("player store not initialized")
	}
	playerName = strings.TrimSpace(playerName)
	if playerName == "" {
		return false, nil
	}
	if completionSeconds < 0 {
		return false, fmt.Errorf("invalid completionseconds")
	}
	clientIP = strings.TrimSpace(clientIP)
	geoCountry = strings.TrimSpace(geoCountry)
	geoCity = strings.TrimSpace(geoCity)

	res, err := s.db.Exec(
		postgresifyPlaceholders("INSERT INTO players(playername, completion_seconds, added_time_ns, client_ip, geo_country, geo_city) VALUES (?,?,?,?,?,?) ON CONFLICT (playername) DO NOTHING"),
		playerName,
		completionSeconds,
		now.UTC().UnixNano(),
		clientIP,
		geoCountry,
		geoCity,
	)
	if err != nil {
		return false, err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return false, ErrPlayerAlreadyExists
	}
	return true, nil
}

func (s *PlayerStore) GetTop(offset, limit int) ([]PublicPlayerData, error) {
	if s == nil || s.db == nil {
		return nil, fmt.Errorf("player store not initialized")
	}
	if offset < 0 {
		offset = 0
	}
	if limit <= 0 {
		limit = 10
	}
	if limit > 500 {
		limit = 500
	}

	rows, err := s.db.Query(
		postgresifyPlaceholders("SELECT playername, completion_seconds FROM players ORDER BY completion_seconds ASC, added_time_ns ASC, playername ASC LIMIT ? OFFSET ?"),
		limit,
		offset,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	out := make([]PublicPlayerData, 0, limit)
	for rows.Next() {
		var name string
		var secs float64
		if err := rows.Scan(&name, &secs); err != nil {
			return nil, err
		}
		out = append(out, PublicPlayerData{PlayerName: name, CompletionSeconds: secs})
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	return out, nil
}

func (s *PlayerStore) GetPersonalRank(playerName string) (rank int, completionSeconds float64, found bool, err error) {
	if s == nil || s.db == nil {
		return 0, 0, false, fmt.Errorf("player store not initialized")
	}
	if playerName == "" {
		return 0, 0, false, nil
	}

	// Use a window function to compute rank in the sorted leaderboard.
	row := s.db.QueryRow(postgresifyPlaceholders(`
SELECT playername, completion_seconds, r FROM (
	SELECT playername, completion_seconds,
		ROW_NUMBER() OVER (ORDER BY completion_seconds ASC, added_time_ns ASC, playername ASC) AS r
	FROM players
) WHERE playername = ?`), playerName)

	var name string
	var secs float64
	var rnk int
	if err := row.Scan(&name, &secs, &rnk); err != nil {
		if err == sql.ErrNoRows {
			return 0, 0, false, nil
		}
		return 0, 0, false, err
	}
	return rnk, secs, true, nil
}

// QueryPlayers returns full PlayerData rows (includes AddedTime) for admin usage.
func (s *PlayerStore) QueryPlayers(q PlayerQuery) (PlayerQueryResult, error) {
	res := PlayerQueryResult{}
	if s == nil || s.db == nil {
		return res, fmt.Errorf("player store not initialized")
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

	whereParts := make([]string, 0, 2)
	args := make([]any, 0, 4)
	if sTerm := strings.TrimSpace(q.Search); sTerm != "" {
		whereParts = append(whereParts, "playername LIKE ?")
		args = append(args, "%"+sTerm+"%")
	}
	if ipTerm := strings.TrimSpace(q.ClientIP); ipTerm != "" {
		whereParts = append(whereParts, "client_ip = ?")
		args = append(args, ipTerm)
	}
	whereSQL := ""
	if len(whereParts) > 0 {
		whereSQL = "WHERE " + strings.Join(whereParts, " AND ")
	}

	// Whitelisted ordering to avoid SQL injection.
	order := strings.ToLower(strings.TrimSpace(q.Order))
	orderBy := "completion_seconds ASC, added_time_ns ASC, playername ASC" // leaderboard default
	switch order {
	case "", "leaderboard", "time_asc":
		orderBy = "completion_seconds ASC, added_time_ns ASC, playername ASC"
	case "time_desc":
		orderBy = "completion_seconds DESC, added_time_ns ASC, playername ASC"
	case "added_asc", "addedtime_asc", "added_time_asc":
		orderBy = "added_time_ns ASC, playername ASC"
	case "added_desc", "addedtime_desc", "added_time_desc":
		orderBy = "added_time_ns DESC, playername ASC"
	case "name_asc", "playername_asc":
		orderBy = "playername ASC"
	case "name_desc", "playername_desc":
		orderBy = "playername DESC"
	default:
		return res, fmt.Errorf("invalid order")
	}

	var total int64
	if err := s.db.QueryRow(postgresifyPlaceholders("SELECT COUNT(1) FROM players "+whereSQL), args...).Scan(&total); err != nil {
		return res, err
	}

	rows, err := s.db.Query(
		postgresifyPlaceholders("SELECT playername, completion_seconds, added_time_ns, COALESCE(client_ip,''), COALESCE(geo_country,''), COALESCE(geo_city,'') FROM players "+whereSQL+" ORDER BY "+orderBy+" LIMIT ? OFFSET ?"),
		append(args, size, offset)...,
	)
	if err != nil {
		return res, err
	}
	defer rows.Close()

	players := make([]PlayerData, 0, size)
	for rows.Next() {
		var name string
		var secs float64
		var ns int64
		var clientIP string
		var geoCountry string
		var geoCity string
		if err := rows.Scan(&name, &secs, &ns, &clientIP, &geoCountry, &geoCity); err != nil {
			return res, err
		}
		players = append(players, PlayerData{
			PlayerName:        name,
			CompletionSeconds: secs,
			AddedTime:         time.Unix(0, ns).UTC(),
			ClientIP:          clientIP,
			GeoCountry:        geoCountry,
			GeoCity:           geoCity,
		})
	}
	if err := rows.Err(); err != nil {
		return res, err
	}

	res.Total = total
	res.Page = page
	res.Size = size
	res.Players = players
	return res, nil
}

func (s *PlayerStore) GetPlayer(playerName string) (*PlayerData, bool, error) {
	if s == nil || s.db == nil {
		return nil, false, fmt.Errorf("player store not initialized")
	}
	playerName = strings.TrimSpace(playerName)
	if playerName == "" {
		return nil, false, nil
	}

	var name string
	var secs float64
	var ns int64
	var clientIP string
	var geoCountry string
	var geoCity string
	err := s.db.QueryRow(
		postgresifyPlaceholders("SELECT playername, completion_seconds, added_time_ns, COALESCE(client_ip,''), COALESCE(geo_country,''), COALESCE(geo_city,'') FROM players WHERE playername = ?"),
		playerName,
	).Scan(&name, &secs, &ns, &clientIP, &geoCountry, &geoCity)
	if err != nil {
		if err == sql.ErrNoRows {
			return nil, false, nil
		}
		return nil, false, err
	}

	p := &PlayerData{PlayerName: name, CompletionSeconds: secs, AddedTime: time.Unix(0, ns).UTC(), ClientIP: clientIP, GeoCountry: geoCountry, GeoCity: geoCity}
	return p, true, nil
}

func (s *PlayerStore) DeletePlayer(playerName string) (deleted bool, err error) {
	if s == nil || s.db == nil {
		return false, fmt.Errorf("player store not initialized")
	}
	playerName = strings.TrimSpace(playerName)
	if playerName == "" {
		return false, nil
	}

	res, err := s.db.Exec(postgresifyPlaceholders("DELETE FROM players WHERE playername = ?"), playerName)
	if err != nil {
		return false, err
	}
	n, _ := res.RowsAffected()
	return n > 0, nil
}

func (s *PlayerStore) UpdatePlayer(oldName, newName string, completionSeconds float64) (updated bool, err error) {
	if s == nil || s.db == nil {
		return false, fmt.Errorf("player store not initialized")
	}
	oldName = strings.TrimSpace(oldName)
	newName = strings.TrimSpace(newName)
	if oldName == "" || newName == "" {
		return false, nil
	}

	tx, err := s.db.Begin()
	if err != nil {
		return false, err
	}
	defer func() {
		if err != nil {
			_ = tx.Rollback()
		}
	}()

	var one int
	if err := tx.QueryRow(
		postgresifyPlaceholders("SELECT 1 FROM players WHERE playername = ?"),
		oldName,
	).Scan(&one); err != nil {
		if err == sql.ErrNoRows {
			return false, nil
		}
		return false, err
	}

	if oldName != newName {
		err := tx.QueryRow(
			postgresifyPlaceholders("SELECT 1 FROM players WHERE playername = ?"),
			newName,
		).Scan(&one)
		if err == nil {
			return false, ErrPlayerAlreadyExists
		}
		if err != sql.ErrNoRows {
			return false, err
		}
	}

	if oldName == newName {
		res, err := tx.Exec(
			postgresifyPlaceholders("UPDATE players SET completion_seconds = ? WHERE playername = ?"),
			completionSeconds,
			oldName,
		)
		if err != nil {
			return false, err
		}
		n, _ := res.RowsAffected()
		updated = n > 0
	} else {
		res, err := tx.Exec(
			postgresifyPlaceholders("UPDATE players SET playername = ?, completion_seconds = ? WHERE playername = ?"),
			newName,
			completionSeconds,
			oldName,
		)
		if err != nil {
			return false, err
		}
		n, _ := res.RowsAffected()
		updated = n > 0
	}

	if err := tx.Commit(); err != nil {
		return false, err
	}
	return updated, nil
}

func (s *PlayerStore) ClearAll() (deleted int64, err error) {
	if s == nil || s.db == nil {
		return 0, fmt.Errorf("player store not initialized")
	}

	res, err := s.db.Exec("TRUNCATE players")
	if err != nil {
		return 0, err
	}
	deleted, _ = res.RowsAffected()
	return deleted, nil
}
