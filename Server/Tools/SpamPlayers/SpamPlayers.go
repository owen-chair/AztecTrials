package main

import (
	"encoding/base64"
	"encoding/json"
	"flag"
	"fmt"
	"math/rand"
	"net/http"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type SubmitTimeRequest struct {
	ClientKey         string  `json:"clientkey"`
	PlayerName        string  `json:"playername"`
	CompletionSeconds float64 `json:"completionseconds"`
}

func main() {
	baseURL := flag.String("base", "http://localhost:8080", "Server base URL (e.g. http://localhost:8080)")
	clientKey := flag.String("clientkey", "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000", "Client key")
	count := flag.Int("count", 10000, "Number of players to submit")
	concurrency := flag.Int("concurrency", 0, "Concurrent workers (default: GOMAXPROCS*4)")
	minSeconds := flag.Float64("min", 30, "Min completion seconds")
	maxSeconds := flag.Float64("max", 900, "Max completion seconds")
	namePrefix := flag.String("prefix", "Bot_", "Player name prefix")
	timeout := flag.Duration("timeout", 10*time.Second, "HTTP client timeout")
	logEvery := flag.Int("logEvery", 250, "Progress log frequency")
	seed := flag.Int64("seed", time.Now().UnixNano(), "RNG seed")
	flag.Parse()

	b := strings.TrimRight(strings.TrimSpace(*baseURL), "/")
	if b == "" {
		panic("-base is required")
	}
	if *count <= 0 {
		fmt.Println("Nothing to do (-count <= 0)")
		return
	}
	if *minSeconds <= 0 || *maxSeconds <= 0 || *maxSeconds < *minSeconds {
		panic("invalid -min/-max")
	}

	w := *concurrency
	if w <= 0 {
		w = runtime.GOMAXPROCS(0) * 4
		if w < 8 {
			w = 8
		}
	}

	client := &http.Client{Timeout: *timeout}

	var nextIndex int64
	var ok int64
	var fail int64

	start := time.Now()

	var wg sync.WaitGroup
	wg.Add(w)
	for workerID := 0; workerID < w; workerID++ {
		rng := rand.New(rand.NewSource(*seed + int64(workerID)*99991))
		go func(id int, r *rand.Rand) {
			defer wg.Done()

			for {
				i := int(atomic.AddInt64(&nextIndex, 1) - 1)
				if i >= *count {
					return
				}

				name := randomName(*namePrefix, i, r)
				secs := *minSeconds + r.Float64()*(*maxSeconds-*minSeconds)

				payload := SubmitTimeRequest{
					ClientKey:         *clientKey,
					PlayerName:        name,
					CompletionSeconds: secs,
				}

				body, _ := json.Marshal(payload)
				// Standard base64 + submit marker (server requires 'a' at index 15).
				b64 := base64.StdEncoding.EncodeToString(body)
				if len(b64) <= 15 {
					atomic.AddInt64(&fail, 1)
					continue
				}
				b64 = b64[:15] + "a" + b64[15:]
				url := b + "/time/submit/" + b64

				resp, err := client.Get(url)
				if err != nil {
					atomic.AddInt64(&fail, 1)
				} else {
					_ = resp.Body.Close()
					if resp.StatusCode >= 200 && resp.StatusCode < 300 {
						atomic.AddInt64(&ok, 1)
					} else {
						atomic.AddInt64(&fail, 1)
					}
				}

				done := atomic.LoadInt64(&ok) + atomic.LoadInt64(&fail)
				if *logEvery > 0 && done%int64(*logEvery) == 0 {
					elapsed := time.Since(start).Seconds()
					fmt.Printf("progress: %d/%d (ok=%d fail=%d) %.1f req/s\n", done, *count, atomic.LoadInt64(&ok), atomic.LoadInt64(&fail), float64(done)/elapsed)
				}
			}
		}(workerID, rng)
	}

	wg.Wait()

	done := atomic.LoadInt64(&ok) + atomic.LoadInt64(&fail)
	elapsed := time.Since(start).Seconds()
	fmt.Printf("DONE: %d submitted (ok=%d fail=%d) in %.2fs (%.1f req/s)\n", done, atomic.LoadInt64(&ok), atomic.LoadInt64(&fail), elapsed, float64(done)/elapsed)
}

func randomName(prefix string, i int, r *rand.Rand) string {
	// Keep it <= 64 chars to satisfy Server.go validation.
	suffix := fmt.Sprintf("%d_%08x", i, r.Uint32())
	name := prefix + suffix
	if len(name) > 64 {
		name = name[:64]
	}
	return name
}
