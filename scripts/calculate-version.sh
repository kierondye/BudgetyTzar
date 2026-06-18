#!/usr/bin/env sh
set -eu

semver_re='^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$'
cc_types='feat|fix|perf|refactor|docs|test|build|ci|chore|style|revert'

die() {
  echo "$*" >&2
  exit 1
}

repo_root() {
  if [ "$#" -gt 0 ]; then
    printf '%s\n' "$1"
  else
    git rev-parse --show-toplevel
  fi
}

short_sha() {
  git -C "$1" rev-parse --short=7 HEAD
}

latest_tag() {
  git -C "$1" tag --merged HEAD --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname |
    while IFS= read -r tag; do
      version=${tag#v}
      if printf '%s\n' "$version" | grep -Eq "$semver_re"; then
        printf '%s\n' "$tag"
        break
      fi
    done
}

exact_tag() {
  git -C "$1" tag --points-at HEAD --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname |
    while IFS= read -r tag; do
      version=${tag#v}
      if printf '%s\n' "$version" | grep -Eq "$semver_re"; then
        printf '%s\n' "$tag"
        break
      fi
    done
}

commits_range() {
  repo=$1
  tag=$2

  if [ -n "$tag" ]; then
    printf '%s..HEAD\n' "$tag"
  else
    printf 'HEAD\n'
  fi
}

commit_count() {
  repo=$1
  range=$2

  if [ -n "$range" ]; then
    git -C "$repo" rev-list --count "$range"
  else
    printf '0\n'
  fi
}

bump_version() {
  version=$1
  bump=$2

  major=$(printf '%s\n' "$version" | cut -d. -f1)
  minor=$(printf '%s\n' "$version" | cut -d. -f2)
  patch=$(printf '%s\n' "$version" | cut -d. -f3)

  case "$bump" in
    major)
      major=$((major + 1))
      minor=0
      patch=0
      ;;
    minor)
      minor=$((minor + 1))
      patch=0
      ;;
    patch)
      patch=$((patch + 1))
      ;;
    none)
      ;;
    *)
      die "unknown bump type: $bump"
      ;;
  esac

  printf '%s.%s.%s\n' "$major" "$minor" "$patch"
}

required_bump() {
  repo=$1
  range=$2
  base_major=$3

  if [ -z "$range" ]; then
    printf 'none\n'
    return
  fi

  log_file=$(mktemp)
  git -C "$repo" log --format='%s%n%b%n' "$range" > "$log_file"

  if grep -Eq "^($cc_types)(\([a-z0-9][a-z0-9._/-]*\))?!: |^BREAKING CHANGE: .+" "$log_file"; then
    rm -f "$log_file"
    if [ "$base_major" -ge 1 ]; then
      printf 'major\n'
    else
      printf 'minor\n'
    fi
    return
  fi

  if grep -Eq '^feat(\([a-z0-9][a-z0-9._/-]*\))?: ' "$log_file"; then
    rm -f "$log_file"
    printf 'minor\n'
    return
  fi

  if grep -Eq '^(fix|perf)(\([a-z0-9][a-z0-9._/-]*\))?: ' "$log_file"; then
    rm -f "$log_file"
    printf 'patch\n'
    return
  fi

  rm -f "$log_file"
  printf 'none\n'
}

calculate() {
  repo=$(repo_root "$@")
  tag=$(exact_tag "$repo")
  sha=$(short_sha "$repo")

  if [ -n "$tag" ]; then
    version=${tag#v}
    printf 'ProductVersion=%s\n' "$version"
    printf 'InformationalVersion=%s+%s\n' "$version" "$sha"
    printf 'AssemblyVersion=%s.0\n' "$version"
    printf 'FileVersion=%s.0\n' "$version"
    return
  fi

  tag=$(latest_tag "$repo")
  if [ -n "$tag" ]; then
    base=${tag#v}
    range=$(commits_range "$repo" "$tag")
    has_tag=true
  else
    base=0.1.0
    range=$(commits_range "$repo" "")
    has_tag=false
  fi

  major=$(printf '%s\n' "$base" | cut -d. -f1)
  count=$(commit_count "$repo" "$range")

  if [ "$has_tag" = false ]; then
    product_version="$base-preview.$count"
  else
    bump=$(required_bump "$repo" "$range" "$major")
    if [ "$bump" = "none" ]; then
      product_version=$base
    else
    target=$(bump_version "$base" "$bump")
    product_version="$target-preview.$count"
    fi
  fi

  assembly_base=$(printf '%s\n' "$product_version" | cut -d- -f1)

  printf 'ProductVersion=%s\n' "$product_version"
  printf 'InformationalVersion=%s+%s\n' "$product_version" "$sha"
  printf 'AssemblyVersion=%s.0\n' "$assembly_base"
  printf 'FileVersion=%s.0\n' "$assembly_base"
}

xml_escape() {
  sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e "s/'/\&apos;/g" -e 's/"/\&quot;/g'
}

write_msbuild_props() {
  repo=$1
  output=$2
  mkdir -p "$(dirname "$output")"

  values=$(calculate "$repo")
  product_version=$(printf '%s\n' "$values" | awk -F= '$1 == "ProductVersion" { print $2 }')
  informational_version=$(printf '%s\n' "$values" | awk -F= '$1 == "InformationalVersion" { print $2 }')
  assembly_version=$(printf '%s\n' "$values" | awk -F= '$1 == "AssemblyVersion" { print $2 }')
  file_version=$(printf '%s\n' "$values" | awk -F= '$1 == "FileVersion" { print $2 }')

  {
    printf '<Project>\n'
    printf '  <PropertyGroup>\n'
    printf '    <ProductVersion>%s</ProductVersion>\n' "$(printf '%s' "$product_version" | xml_escape)"
    printf '    <Version>%s</Version>\n' "$(printf '%s' "$product_version" | xml_escape)"
    printf '    <PackageVersion>%s</PackageVersion>\n' "$(printf '%s' "$product_version" | xml_escape)"
    printf '    <InformationalVersion>%s</InformationalVersion>\n' "$(printf '%s' "$informational_version" | xml_escape)"
    printf '    <AssemblyVersion>%s</AssemblyVersion>\n' "$(printf '%s' "$assembly_version" | xml_escape)"
    printf '    <FileVersion>%s</FileVersion>\n' "$(printf '%s' "$file_version" | xml_escape)"
    printf '  </PropertyGroup>\n'
    printf '</Project>\n'
  } > "$output"
}

self_test_commit() {
  repo=$1
  message=$2
  git -C "$repo" add file.txt
  git -C "$repo" -c user.name='BudgetyTzar Tests' -c user.email='tests@example.com' commit -m "$message" >/dev/null
}

self_test_repo() {
  repo=$(mktemp -d)
  git -C "$repo" init -q
  printf 'initial\n' > "$repo/file.txt"
  self_test_commit "$repo" "chore: initial commit"
  printf '%s\n' "$repo"
}

assert_product_version() {
  repo=$1
  expected=$2
  actual=$(calculate "$repo" | awk -F= '$1 == "ProductVersion" { print $2 }')
  if [ "$actual" != "$expected" ]; then
    echo "expected $expected but got $actual in $repo" >&2
    exit 1
  fi
}

self_test() {
  repo=$(self_test_repo)
  assert_product_version "$repo" "0.1.0-preview.1"

  git -C "$repo" tag -a v0.1.0 -m "Release v0.1.0"
  assert_product_version "$repo" "0.1.0"

  printf 'fix\n' >> "$repo/file.txt"
  self_test_commit "$repo" "fix: correct budget balance"
  assert_product_version "$repo" "0.1.1-preview.1"

  repo=$(self_test_repo)
  git -C "$repo" tag -a v0.1.0 -m "Release v0.1.0"
  printf 'feat\n' >> "$repo/file.txt"
  self_test_commit "$repo" "feat: add budget export"
  assert_product_version "$repo" "0.2.0-preview.1"

  repo=$(self_test_repo)
  git -C "$repo" tag -a v1.0.0 -m "Release v1.0.0"
  printf 'breaking\n' >> "$repo/file.txt"
  self_test_commit "$repo" "feat(api)!: rename transaction route"
  assert_product_version "$repo" "2.0.0-preview.1"

  echo "version calculation self-test passed"
}

case "${1:-}" in
  --print)
    shift
    calculate "$@"
    ;;
  --msbuild-props)
    [ "$#" -eq 3 ] || die "usage: $0 --msbuild-props <repo-root> <output-file>"
    write_msbuild_props "$2" "$3"
    ;;
  --self-test)
    self_test
    ;;
  *)
    die "usage: $0 --print [repo-root] | --msbuild-props <repo-root> <output-file> | --self-test"
    ;;
esac
