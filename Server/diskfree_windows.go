//go:build windows

package main

func diskFreeBytes(path string) (uint64, error) {
	// This project is deployed on Linux; Windows support is not required.
	// Returning 0 keeps local builds working if someone runs on Windows.
	return 0, nil
}
