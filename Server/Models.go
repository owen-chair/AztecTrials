package main

import (
	"net/http"
	"time"
)

type RequestLog struct {
	ID         int64     `json:"id"`
	TimeUTC    time.Time `json:"timeutc"`
	DurationMs int64     `json:"durationms"`

	Endpoint string `json:"endpoint"`
	Method   string `json:"method"`
	Path     string `json:"path"`

	RemoteAddr string `json:"remoteaddr"`
	ClientIP   string `json:"clientip"`
	GeoCountry string `json:"geocountry"`
	GeoCity    string `json:"geocity"`
	UserAgent  string `json:"useragent"`

	Headers http.Header `json:"headers"`

	PayloadPathSegmentRaw       string `json:"payloadraw"`
	PayloadPathSegmentUnescaped string `json:"payloadunescaped"`
	PayloadPathSegmentStripped  string `json:"payloadstripped"`
	PayloadJSON                 string `json:"payloadjson"`

	Status int    `json:"status"`
	Error  string `json:"error"`
}

type RateLimitLog struct {
	ID int64 `json:"id"`

	TimeUTC time.Time `json:"timeutc"`

	Endpoint string `json:"endpoint"`
	Method   string `json:"method"`
	Path     string `json:"path"`

	RemoteAddr string `json:"remoteaddr"`
	ClientIP   string `json:"clientip"`
	UserAgent  string `json:"useragent"`

	// Event is typically "banned" (threshold exceeded) or "blocked" (request during ban).
	Event string `json:"event"`

	LimitPerSecond int       `json:"limitpersecond"`
	CountThisSec   int       `json:"countthissec"`
	WindowStartUTC time.Time `json:"windowstartutc"`
	BannedUntilUTC time.Time `json:"banneduntilutc"`
}

type ManualIPBan struct {
	ID             int64     `json:"id"`
	CreatedUTC     time.Time `json:"createdutc"`
	IP             string    `json:"ip"`
	BannedUntilUTC time.Time `json:"banneduntilutc"`
	Reason         string    `json:"reason"`
}

type ManualIPBanQuery struct {
	Search     string
	ActiveOnly bool
	Page       int
	PageSize   int
}

type ManualIPBanQueryResult struct {
	Total int64         `json:"total"`
	Page  int           `json:"page"`
	Size  int           `json:"pagesize"`
	Bans  []ManualIPBan `json:"bans"`
}

type PlayerData struct {
	PlayerName        string    `json:"playername"`
	CompletionSeconds float64   `json:"completionseconds"`
	AddedTime         time.Time `json:"addedtime"`
	ClientIP          string    `json:"clientip"`
	GeoCountry        string    `json:"geocountry"`
	GeoCity           string    `json:"geocity"`
}

type PublicPlayerData struct {
	PlayerName        string  `json:"playername"`
	CompletionSeconds float64 `json:"completionseconds"`
}

type Response struct {
	Message string `json:"message"`
}

type LeaderboardResponse struct {
	Players []PublicPlayerData `json:"players"`
}

type SubmitTimeRequest struct {
	ClientKey         string  `json:"clientkey"`
	PlayerName        string  `json:"playername"`
	CompletionSeconds float64 `json:"completionseconds"`
}

type LeaderboardRequest struct {
	ClientKey string `json:"clientkey"`
}

type PagedLeaderboardRequest struct {
	ClientKey string `json:"clientkey"`
	Page      int    `json:"page"`
}

type PersonalRankRequest struct {
	ClientKey  string `json:"clientkey"`
	PlayerName string `json:"playername"`
}

type PersonalRankResponse struct {
	Message           string  `json:"message,omitempty"`
	PlayerName        string  `json:"playername,omitempty"`
	CompletionSeconds float64 `json:"completionseconds,omitempty"`
	Rank              int     `json:"rank,omitempty"`
}
