package main

import (
	"encoding/base64"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net/http"
	"strings"
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
	seconds := flag.Float64("seconds", 123.45, "Completion time in seconds")
	timeout := flag.Duration("timeout", 10*time.Second, "HTTP client timeout")
	flag.Parse()

	b := strings.TrimRight(strings.TrimSpace(*baseURL), "/")
	if b == "" {
		panic("-base is required")
	}

	payload := SubmitTimeRequest{
		ClientKey:         *clientKey,
		PlayerName:        "TestPlayer",
		CompletionSeconds: *seconds,
	}

	body, _ := json.Marshal(payload)
	// Standard base64 + submit marker (server requires 'a' at index 15).
	b64 := base64.StdEncoding.EncodeToString(body)
	if len(b64) <= 15 {
		panic("payload base64 unexpectedly short")
	}
	b64 = b64[:15] + "a" + b64[15:]
	url := b + "/time/submit/" + b64

	client := &http.Client{Timeout: *timeout}
	resp, err := client.Get(url)
	if err != nil {
		panic(err)
	}
	defer resp.Body.Close()

	respBody, _ := io.ReadAll(resp.Body)
	fmt.Printf("HTTP %d\n%s\n", resp.StatusCode, string(respBody))
}
