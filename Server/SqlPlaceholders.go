package main

import "strings"

// postgresifyPlaceholders converts a SQL string that uses '?' placeholders
// into a Postgres-compatible form using $1, $2, ... placeholders.
//
// This is intentionally simple because this codebase only uses '?' for
// parameter placeholders (not inside string literals).
func postgresifyPlaceholders(sql string) string {
	if !strings.Contains(sql, "?") {
		return sql
	}
	var b strings.Builder
	b.Grow(len(sql) + 16)
	arg := 1
	for i := 0; i < len(sql); i++ {
		if sql[i] == '?' {
			b.WriteByte('$')
			b.WriteString(itoaSmall(arg))
			arg++
			continue
		}
		b.WriteByte(sql[i])
	}
	return b.String()
}

func itoaSmall(n int) string {
	// Fast path for small ints; avoids fmt for hot paths.
	if n < 10 {
		return string(rune('0' + n))
	}
	// Fallback; still tiny (n will be small in practice here).
	var buf [16]byte
	pos := len(buf)
	for n > 0 {
		pos--
		buf[pos] = byte('0' + (n % 10))
		n /= 10
	}
	return string(buf[pos:])
}
