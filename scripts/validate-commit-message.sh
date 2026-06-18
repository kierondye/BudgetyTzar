#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <commit-message-file>" >&2
  exit 2
fi

message_file=$1

if [ ! -f "$message_file" ]; then
  echo "commit message file not found: $message_file" >&2
  exit 2
fi

subject=$(
  sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d; q' "$message_file"
)

if [ -z "$subject" ]; then
  echo "commit message subject is empty" >&2
  exit 1
fi

case "$subject" in
  Merge\ *|Revert\ \"*|revert:\ *|fixup!\ *|squash!\ *)
    exit 0
    ;;
esac

if printf '%s\n' "$subject" | grep -Eq '^(feat|fix|perf|refactor|docs|test|build|ci|chore|style|revert)(\([a-z0-9][a-z0-9._/-]*\))?!?: .+$'; then
  exit 0
fi

cat >&2 <<'EOF'
Commit message must follow Conventional Commits.

Valid examples:
  feat: add budget export
  fix(api): preserve version endpoint metadata
  feat(events)!: rename transaction event

Allowed types:
  feat, fix, perf, refactor, docs, test, build, ci, chore, style, revert
EOF
exit 1
