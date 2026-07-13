#!/usr/bin/env sh
set -eu

cc_subject_re='^(feat|fix|perf|refactor|docs|test|build|ci|chore|style|revert)(\([a-z0-9][a-z0-9._/-]*\))?!?: .+$'

usage() {
  echo "usage: $0 <commit-message-file> | --range <git-range> | --self-test" >&2
}

print_guidance() {
  cat >&2 <<'EOF'
Commit message must follow Conventional Commits.

Expected format:
  type(scope): description

Valid examples:
  feat: add budget export
  fix(api): preserve version endpoint metadata
  feat(events)!: rename transaction event

Allowed types:
  feat, fix, perf, refactor, docs, test, build, ci, chore, style, revert
EOF
}

is_conventional_subject() {
  subject=$1
  printf '%s\n' "$subject" | grep -Eq "$cc_subject_re"
}

is_git_generated_subject() {
  subject=$1

  case "$subject" in
    Merge\ *|Revert\ \"*|revert:\ *)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

is_local_workflow_subject() {
  subject=$1

  case "$subject" in
    fixup!\ *|squash!\ *)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

validate_subject() {
  subject=$1
  mode=${2:-ci}

  if [ -z "$subject" ]; then
    echo "commit message subject is empty" >&2
    return 1
  fi

  if is_conventional_subject "$subject" || is_git_generated_subject "$subject"; then
    return 0
  fi

  if [ "$mode" = "local" ] && is_local_workflow_subject "$subject"; then
    return 0
  fi

  return 1
}

subject_from_message_file() {
  sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d; q' "$1"
}

validate_message_file() {
  message_file=$1

  if [ ! -f "$message_file" ]; then
    echo "commit message file not found: $message_file" >&2
    return 2
  fi

  subject=$(subject_from_message_file "$message_file")
  if validate_subject "$subject" local; then
    return 0
  fi

  print_guidance
  return 1
}

validate_range() {
  range=$1
  log_file=$(mktemp)
  tab=$(printf '\t')
  failures=0

  git log --format='%H%x09%s' "$range" > "$log_file"

  while IFS="$tab" read -r sha subject; do
    [ -n "$sha" ] || continue

    if ! validate_subject "$subject" ci; then
      short_sha=$(printf '%s\n' "$sha" | cut -c1-12)
      echo "Invalid commit subject in $short_sha: $subject" >&2
      failures=$((failures + 1))
    fi
  done < "$log_file"

  rm -f "$log_file"

  if [ "$failures" -ne 0 ]; then
    echo >&2
    print_guidance
    return 1
  fi

  return 0
}

self_test() {
  repo=$(mktemp -d)
  original_dir=$(pwd)
  git -C "$repo" init -q
  git -C "$repo" config user.name 'BudgetyTzar Tests'
  git -C "$repo" config user.email 'tests@example.com'

  printf 'initial\n' > "$repo/file.txt"
  git -C "$repo" add file.txt
  git -C "$repo" commit -m 'chore: initial commit' >/dev/null

  printf 'valid\n' >> "$repo/file.txt"
  git -C "$repo" add file.txt
  git -C "$repo" commit -m 'fix(api): validate commit subjects' >/dev/null

  cd "$repo"
  validate_range 'HEAD~1..HEAD'

  printf 'invalid\n' >> "$repo/file.txt"
  git -C "$repo" add file.txt
  git -C "$repo" commit -m 'update commit validation' >/dev/null

  if validate_range 'HEAD~1..HEAD' >/dev/null 2>&1; then
    echo "expected invalid commit subject to fail" >&2
    exit 1
  fi

  printf 'fixup\n' >> "$repo/file.txt"
  git -C "$repo" add file.txt
  git -C "$repo" commit -m 'fixup! fix(api): validate commit subjects' >/dev/null

  if validate_range 'HEAD~1..HEAD' >/dev/null 2>&1; then
    echo "expected fixup commit subject to fail in CI range validation" >&2
    exit 1
  fi

  message_file=$(mktemp)
  printf 'fixup! fix(api): validate commit subjects\n' > "$message_file"
  validate_message_file "$message_file"
  cd "$original_dir"

  echo "commit message validation self-test passed"
}

case "${1:-}" in
  --range)
    [ "$#" -eq 2 ] || {
      usage
      exit 2
    }
    validate_range "$2"
    ;;
  --self-test)
    [ "$#" -eq 1 ] || {
      usage
      exit 2
    }
    self_test
    ;;
  -*)
    usage
    exit 2
    ;;
  *)
    [ "$#" -eq 1 ] || {
      usage
      exit 2
    }
    validate_message_file "$1"
    ;;
esac
