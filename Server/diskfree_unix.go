//go:build !windows

package main

import (
	"fmt"
	"syscall"
)

func diskFreeBytes(path string) (uint64, error) {
	var st syscall.Statfs_t
	if err := syscall.Statfs(path, &st); err != nil {
		return 0, fmt.Errorf("statfs %q: %w", path, err)
	}
	// Bavail * Bsize is the free bytes available to unprivileged user.
	return st.Bavail * uint64(st.Bsize), nil
}
