#!/bin/bash
# Runs run-pair.sh N times (default 4); results appended to $1 (default /tmp/pairs.out)
n=${2:-4}; out=${1:-/tmp/pairs.out}
for i in $(seq 1 $n); do "$(dirname "$0")/run-pair.sh" >> "$out" 2>&1; done
echo DONE >> "$out"
