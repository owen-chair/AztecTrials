package main

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"
)

// OpenPostgresFromEnv opens a shared Postgres connection pool.
//
// Expected env vars (either):
// - BUGGYPYRAMID_DATABASE_URL=postgres://user:pass@host:5432/dbname?sslmode=disable
// - or BUGGYPYRAMID_DB_HOST/PORT/USER/PASSWORD/NAME
func OpenPostgresFromEnv() (*sql.DB, error) {
	dsn := os.Getenv("BUGGYPYRAMID_DATABASE_URL")
	if dsn == "" {
		host := envOr("BUGGYPYRAMID_DB_HOST", "db")
		port := envOr("BUGGYPYRAMID_DB_PORT", "5432")
		user := envOr("BUGGYPYRAMID_DB_USER", "buggypyramid")
		pass := envOr("BUGGYPYRAMID_DB_PASSWORD", "change_me")
		name := envOr("BUGGYPYRAMID_DB_NAME", "buggypyramid")
		dsn = fmt.Sprintf("postgres://%s:%s@%s:%s/%s?sslmode=disable", user, pass, host, port, name)
	}

	db, err := sql.Open("pgx", dsn)
	if err != nil {
		return nil, err
	}

	// Keep the pool small for a low-resource VPS.
	db.SetMaxOpenConns(5)
	db.SetMaxIdleConns(5)
	db.SetConnMaxLifetime(30 * time.Minute)
	db.SetConnMaxIdleTime(5 * time.Minute)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := db.PingContext(ctx); err != nil {
		_ = db.Close()
		return nil, err
	}

	return db, nil
}

func envOr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
