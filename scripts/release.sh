#!/usr/bin/env sh
set -eu

repo=$(git rev-parse --show-toplevel)
calculator="$repo/scripts/calculate-version.sh"
notes_dir="$repo/obj/release-notes"
mkdir -p "$notes_dir"

semver_re='^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$'
cc_types='feat|fix|perf|refactor|docs|test|build|ci|chore|style|revert'

latest_tag() {
  git -C "$repo" tag --merged HEAD --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname |
    while IFS= read -r tag; do
      version=${tag#v}
      if printf '%s\n' "$version" | grep -Eq "$semver_re"; then
        printf '%s\n' "$tag"
        break
      fi
    done
}

exact_tag=$(git -C "$repo" tag --points-at HEAD --list 'v[0-9]*.[0-9]*.[0-9]*' | head -n 1)
if [ -n "$exact_tag" ]; then
  echo "HEAD is already tagged with $exact_tag"
  exit 0
fi

tag=$(latest_tag)
if [ -n "$tag" ]; then
  range="$tag..HEAD"
else
  range="HEAD"
fi

calculated=$("$calculator" --print "$repo")
product_version=$(printf '%s\n' "$calculated" | awk -F= '$1 == "ProductVersion" { print $2 }')
release_version=$(printf '%s\n' "$product_version" | cut -d- -f1)
release_tag="v$release_version"

if git -C "$repo" rev-parse "$release_tag" >/dev/null 2>&1; then
  echo "$release_tag already exists"
  exit 1
fi

release_commits=$(git -C "$repo" log --format='- %s (%h)' "$range" |
  grep -E "^- ($cc_types)(\([a-z0-9][a-z0-9._/-]*\))?!?: " || true)

if [ -z "$release_commits" ]; then
  release_commits="- No release-notable Conventional Commits."
fi

notes_file="$notes_dir/$release_tag.md"
{
  printf '# %s\n\n' "$release_tag"
  printf '%s\n' "$release_commits"
} > "$notes_file"

git -C "$repo" tag -a "$release_tag" -F "$notes_file"

cat <<EOF
Created $release_tag.

Release notes:
$(cat "$notes_file")

To publish this release when GitHub CLI is available:
gh release create $release_tag --title "$release_tag" --notes-file "$notes_file"
EOF
