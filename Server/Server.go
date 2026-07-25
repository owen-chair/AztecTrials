package main

import (
	"fmt"
	"log"
	"net/http"
	"os"
	"runtime"
	"strings"
	"time"
)

// Public client key.
const CLIENT_KEY = "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000"

// Separate admin key for admin endpoints.
// Prefer setting via env var BUGGYPYRAMID_ADMIN_KEY.
const ADMIN_KEY = "ADMIN_KEY_PLACEHOLDER_CHANGE_ME_000000000000"

var (
	logStore    *LogStore
	playerStore *PlayerStore
)

func main() {
	db, err := OpenPostgresFromEnv()
	if err != nil {
		log.Fatalf("failed to open postgres: %v", err)
	}
	defer func() { _ = db.Close() }()

	store, err := OpenLogStore(db)
	if err != nil {
		log.Fatalf("failed to init log store: %v", err)
	}
	logStore = store

	pStore, err := OpenPlayerStore(db)
	if err != nil {
		log.Fatalf("failed to init player store: %v", err)
	}
	playerStore = pStore

	// Background: refresh manual IP bans from DB.
	refreshManualBansFromDB(time.Now().UTC())
	go func() {
		t := time.NewTicker(30 * time.Second)
		defer t.Stop()
		for range t.C {
			refreshManualBansFromDB(time.Now().UTC())
		}
	}()

	// Background: snapshot DB size/rows and free disk every hour.
	// Disk path can be overridden via BUGGYPYRAMID_DISK_PATH.
	go func() {
		diskPath := strings.TrimSpace(os.Getenv("BUGGYPYRAMID_DISK_PATH"))
		if diskPath == "" {
			if runtime.GOOS == "windows" {
				diskPath = "C:\\"
			} else {
				diskPath = "/"
			}
		}

		_ = logStore.CaptureAndInsertDBMetrics(diskPath)
		t := time.NewTicker(1 * time.Hour)
		defer t.Stop()
		for range t.C {
			_ = logStore.CaptureAndInsertDBMetrics(diskPath)
		}
	}()

	// Register handlers
	http.HandleFunc("/", helloHandler)
	http.HandleFunc("/time/submit/", submitTimeHandler)
	http.HandleFunc("/metrics/checkpointUnlock/", checkpointUnlockHandler)
	http.HandleFunc("/metrics/genericMetric", genericMetricHandler)
	http.HandleFunc("/metrics/genericMetric/", genericMetricHandler)
	http.HandleFunc("/data/top10/", top10Handler)
	http.HandleFunc("/data/top100/", top100Handler)
	http.HandleFunc("/data/page/", pageHandler)
	http.HandleFunc("/data/personal/", personalRankHandler)

	// Admin
	http.HandleFunc("/admin/logs", adminLogsHandler)
	http.HandleFunc("/admin/logs/", adminLogsSubHandler)
	http.HandleFunc("/admin/players", adminPlayersHandler)
	http.HandleFunc("/admin/players/", adminPlayersSubHandler)
	http.HandleFunc("/admin/ratelimits", adminRateLimitsHandler)
	http.HandleFunc("/admin/ratelimits/", adminRateLimitsSubHandler)
	http.HandleFunc("/admin/manualbans", adminManualBansHandler)
	http.HandleFunc("/admin/manualbans/", adminManualBansSubHandler)
	http.HandleFunc("/admin/dbmetrics/", adminDBMetricsSubHandler)
	http.HandleFunc("/admin/checkpointunlocks", adminCheckpointUnlocksHandler)
	http.HandleFunc("/admin/checkpointunlocks/", adminCheckpointUnlocksSubHandler)

	// Start server
	port := "8080"
	fmt.Printf("Server starting on http://localhost:%s\n", port)
	fmt.Printf("Client Key: %s\n", CLIENT_KEY)
	fmt.Printf("Admin Key: (set via BUGGYPYRAMID_ADMIN_KEY)\n")
	fmt.Println("Endpoints (base64 encoded JSON in URL path):")
	fmt.Println("  GET / - Intentionally returns forbidden / closes connection")
	fmt.Println("  GET /time/submit/{base64_json} - Submit completion time")
	fmt.Println("  GET /metrics/checkpointUnlock/{checkpoint}/{base64_json} - Record checkpoint unlock metric")
	fmt.Println("  GET|POST /metrics/genericMetric/{base64_json} - Accept arbitrary JSON and only log request")
	fmt.Println("  GET /data/top10/{base64_json} - Leaderboard (top 10, fastest first)")
	fmt.Println("  GET /data/top100/{base64_json} - Leaderboard (top 100, fastest first)")
	fmt.Println("  GET /data/page/{base64_json} - Leaderboard page (100 per page, requires page)")
	fmt.Println("  GET /data/personal/{base64_json} - Personal rank lookup (requires playername)")
	fmt.Println("  GET /admin/logs - Admin log query (requires X-Admin-Key or adminkey=)")
	fmt.Println("  GET /admin/logs/{id} - Admin log fetch by id")
	fmt.Println("  POST /admin/logs/clear - Admin clear logs")
	fmt.Println("  GET /admin/players - Admin player query (q, order, page, pageSize)")
	fmt.Println("  GET /admin/players/{playername} - Admin player fetch by name")
	fmt.Println("  DELETE /admin/players/{playername} - Admin delete player by name")
	fmt.Println("  POST /admin/players/clear - Admin clear players")
	fmt.Println("  GET /admin/ratelimits - Admin rate-limit log query (ip, endpoint, event, q, page, pageSize)")
	fmt.Println("  POST /admin/ratelimits/clear - Admin clear rate-limit logs")
	fmt.Println("  GET /admin/checkpointunlocks - Admin checkpoint unlock query (ip, checkpoint, q, page, pageSize)")
	fmt.Println("  POST /admin/checkpointunlocks/clear - Admin clear checkpoint unlocks")
	fmt.Println("  GET /admin/dbmetrics/stats - Admin DB/disk metrics stats (bucket, start, end, metric=db_bytes|rows|free_bytes)")
	log.Fatal(http.ListenAndServe(":"+port, nil))
}
