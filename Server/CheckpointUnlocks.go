package main

import (
	"database/sql"
	"fmt"
	"strings"
	"time"
)

const (
	CheckpointZipline       = "ZiplineCheckpointUnlocked"
	CheckpointBoulder       = "BoulderTunnelCheckpointUnlocked"
	CheckpointCrushingWalls = "crushingWallsCheckpointUnlocked"
	JumpRoom                = "jumpRoomCheckpointUnlocked"
)

func isAllowedCheckpointUnlock(checkpoint string) bool {
	switch strings.TrimSpace(checkpoint) {
	case CheckpointZipline,
		CheckpointBoulder,
		CheckpointCrushingWalls,
		JumpRoom:
		return true
	default:
		return false
	}
}

type CheckpointUnlock struct {
	ID         int64     `json:"id"`
	TimeUTC    time.Time `json:"timeutc"`
	ClientIP   string    `json:"clientip"`
	GeoCountry string    `json:"geocountry"`
	GeoCity    string    `json:"geocity"`
	Checkpoint string    `json:"checkpoint"`
}

type CheckpointUnlockQuery struct {
	ClientIP   string
	Checkpoint string
	Search     string
	StartUTC   time.Time
	EndUTC     time.Time
	Page       int
	PageSize   int
}

type CheckpointUnlockQueryResult struct {
	Total   int64              `json:"total"`
	Page    int                `json:"page"`
	Size    int                `json:"pagesize"`
	Unlocks []CheckpointUnlock `json:"unlocks"`
}

func (s *LogStore) InsertCheckpointUnlock(nowUTC time.Time, clientIP string, checkpoint string) {
	if s == nil || s.db == nil {
		return
	}
	clientIP = strings.TrimSpace(clientIP)
	checkpoint = strings.TrimSpace(checkpoint)
	if clientIP == "" || checkpoint == "" {
		return
	}
	if !isAllowedCheckpointUnlock(checkpoint) {
		return
	}

	_, _ = s.db.Exec(
		postgresifyPlaceholders(`INSERT INTO checkpoint_unlocks (time_utc, client_ip, checkpoint) VALUES (?,?,?)`),
		nowUTC.UTC(),
		clientIP,
		checkpoint,
	)
}

func (s *LogStore) ClearCheckpointUnlocks() (int64, error) {
	if s == nil || s.db == nil {
		return 0, fmt.Errorf("log store is nil")
	}
	res, err := s.db.Exec("DELETE FROM checkpoint_unlocks")
	if err != nil {
		return 0, err
	}
	n, _ := res.RowsAffected()
	return n, nil
}

func (s *LogStore) GetCheckpointUnlockByID(id int64) (*CheckpointUnlock, bool, error) {
	if s == nil || s.db == nil {
		return nil, false, fmt.Errorf("log store is nil")
	}
	if id <= 0 {
		return nil, false, fmt.Errorf("invalid id")
	}

	row := s.db.QueryRow(postgresifyPlaceholders(`SELECT id, time_utc, client_ip, checkpoint FROM checkpoint_unlocks WHERE id = ?`), id)
	var e CheckpointUnlock
	if err := row.Scan(&e.ID, &e.TimeUTC, &e.ClientIP, &e.Checkpoint); err != nil {
		if err == sql.ErrNoRows {
			return nil, false, nil
		}
		return nil, false, err
	}
	e.TimeUTC = e.TimeUTC.UTC()
	e.ClientIP = strings.TrimSpace(e.ClientIP)
	e.GeoCountry, e.GeoCity = s.LookupGeoForIP(e.ClientIP)
	e.Checkpoint = strings.TrimSpace(e.Checkpoint)
	return &e, true, nil
}

func (s *LogStore) CreateCheckpointUnlock(timeUTC time.Time, clientIP string, checkpoint string) (*CheckpointUnlock, error) {
	if s == nil || s.db == nil {
		return nil, fmt.Errorf("log store is nil")
	}
	clientIP = strings.TrimSpace(clientIP)
	checkpoint = strings.TrimSpace(checkpoint)
	if clientIP == "" {
		return nil, fmt.Errorf("clientip is empty")
	}
	if checkpoint == "" {
		return nil, fmt.Errorf("checkpoint is empty")
	}
	if !isAllowedCheckpointUnlock(checkpoint) {
		return nil, fmt.Errorf("invalid checkpoint")
	}

	var id int64
	if err := s.db.QueryRow(
		postgresifyPlaceholders(`INSERT INTO checkpoint_unlocks (time_utc, client_ip, checkpoint) VALUES (?,?,?) RETURNING id`),
		timeUTC.UTC(),
		clientIP,
		checkpoint,
	).Scan(&id); err != nil {
		return nil, err
	}
	geoCountry, geoCity := s.LookupGeoForIP(clientIP)
	return &CheckpointUnlock{ID: id, TimeUTC: timeUTC.UTC(), ClientIP: clientIP, GeoCountry: geoCountry, GeoCity: geoCity, Checkpoint: checkpoint}, nil
}

func (s *LogStore) UpdateCheckpointUnlockByID(id int64, timeUTC time.Time, clientIP string, checkpoint string) (*CheckpointUnlock, bool, error) {
	if s == nil || s.db == nil {
		return nil, false, fmt.Errorf("log store is nil")
	}
	if id <= 0 {
		return nil, false, fmt.Errorf("invalid id")
	}
	clientIP = strings.TrimSpace(clientIP)
	checkpoint = strings.TrimSpace(checkpoint)
	if clientIP == "" {
		return nil, false, fmt.Errorf("clientip is empty")
	}
	if checkpoint == "" {
		return nil, false, fmt.Errorf("checkpoint is empty")
	}
	if !isAllowedCheckpointUnlock(checkpoint) {
		return nil, false, fmt.Errorf("invalid checkpoint")
	}

	row := s.db.QueryRow(
		postgresifyPlaceholders(`UPDATE checkpoint_unlocks SET time_utc = ?, client_ip = ?, checkpoint = ? WHERE id = ? RETURNING id, time_utc, client_ip, checkpoint`),
		timeUTC.UTC(),
		clientIP,
		checkpoint,
		id,
	)
	var out CheckpointUnlock
	if err := row.Scan(&out.ID, &out.TimeUTC, &out.ClientIP, &out.Checkpoint); err != nil {
		if err == sql.ErrNoRows {
			return nil, false, nil
		}
		return nil, false, err
	}
	out.TimeUTC = out.TimeUTC.UTC()
	out.ClientIP = strings.TrimSpace(out.ClientIP)
	out.GeoCountry, out.GeoCity = s.LookupGeoForIP(out.ClientIP)
	out.Checkpoint = strings.TrimSpace(out.Checkpoint)
	return &out, true, nil
}

func (s *LogStore) DeleteCheckpointUnlockByID(id int64) (bool, error) {
	if s == nil || s.db == nil {
		return false, fmt.Errorf("log store is nil")
	}
	if id <= 0 {
		return false, fmt.Errorf("invalid id")
	}
	res, err := s.db.Exec(postgresifyPlaceholders(`DELETE FROM checkpoint_unlocks WHERE id = ?`), id)
	if err != nil {
		return false, err
	}
	n, _ := res.RowsAffected()
	return n > 0, nil
}

func (s *LogStore) QueryCheckpointUnlocks(q CheckpointUnlockQuery) (CheckpointUnlockQueryResult, error) {
	if s == nil || s.db == nil {
		return CheckpointUnlockQueryResult{}, fmt.Errorf("log store is nil")
	}

	clientIP := strings.TrimSpace(q.ClientIP)
	checkpoint := strings.TrimSpace(q.Checkpoint)
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

	where := "WHERE 1=1"
	args := []any{}

	if clientIP != "" {
		where += " AND client_ip = ?"
		args = append(args, clientIP)
	}
	if checkpoint != "" {
		if !isAllowedCheckpointUnlock(checkpoint) {
			return CheckpointUnlockQueryResult{}, fmt.Errorf("invalid checkpoint")
		}
		where += " AND checkpoint = ?"
		args = append(args, checkpoint)
	}
	if search != "" {
		where += " AND (client_ip ILIKE CONCAT('%', ?, '%') OR checkpoint ILIKE CONCAT('%', ?, '%'))"
		args = append(args, search, search)
	}
	if !q.StartUTC.IsZero() {
		where += " AND time_utc >= ?"
		args = append(args, q.StartUTC.UTC())
	}
	if !q.EndUTC.IsZero() {
		where += " AND time_utc <= ?"
		args = append(args, q.EndUTC.UTC())
	}

	// Count
	var total int64
	if err := s.db.QueryRow(postgresifyPlaceholders("SELECT COUNT(1) FROM checkpoint_unlocks "+where), args...).Scan(&total); err != nil {
		return CheckpointUnlockQueryResult{}, err
	}

	// Page
	pageSQL := "SELECT id, time_utc, client_ip, checkpoint FROM checkpoint_unlocks " + where + " ORDER BY time_utc DESC, id DESC LIMIT ? OFFSET ?"
	pageArgs := append(append([]any{}, args...), pageSize, offset)
	rows, err := s.db.Query(postgresifyPlaceholders(pageSQL), pageArgs...)
	if err != nil {
		return CheckpointUnlockQueryResult{}, err
	}
	defer rows.Close()

	unlocks := make([]CheckpointUnlock, 0, pageSize)
	for rows.Next() {
		var e CheckpointUnlock
		if err := rows.Scan(&e.ID, &e.TimeUTC, &e.ClientIP, &e.Checkpoint); err != nil {
			return CheckpointUnlockQueryResult{}, err
		}
		e.TimeUTC = e.TimeUTC.UTC()
		e.ClientIP = strings.TrimSpace(e.ClientIP)
		e.GeoCountry, e.GeoCity = s.LookupGeoForIP(e.ClientIP)
		e.Checkpoint = strings.TrimSpace(e.Checkpoint)
		unlocks = append(unlocks, e)
	}
	if err := rows.Err(); err != nil {
		return CheckpointUnlockQueryResult{}, err
	}

	return CheckpointUnlockQueryResult{Total: total, Page: page, Size: pageSize, Unlocks: unlocks}, nil
}
