#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# bump-version.sh — Bump the project version (manual or auto from commits)
#
# Usage:
#   ./scripts/bump-version.sh              # auto-detect from conventional commits
#   ./scripts/bump-version.sh major        # force major bump  (0.1.0 → 1.0.0)
#   ./scripts/bump-version.sh minor        # force minor bump  (0.1.0 → 0.2.0)
#   ./scripts/bump-version.sh patch        # force patch bump  (0.1.0 → 0.1.1)
#   ./scripts/bump-version.sh --dry-run    # show what would happen, don't change anything
#
# Auto-detection scans commits since the last version tag (vX.Y.Z):
#   - feat!: or BREAKING CHANGE in body → major
#   - feat:                             → minor
#   - fix:, chore:, anything else       → patch
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="${REPO_ROOT}/VERSION"
DRY_RUN=false
BUMP_LEVEL=""

for arg in "$@"; do
  case "$arg" in
    major|minor|patch) BUMP_LEVEL="$arg" ;;
    --dry-run)         DRY_RUN=true ;;
    --help|-h)
      sed -n '2,/^# ---/p' "$0" | head -n -1 | sed 's/^# //'
      exit 0
      ;;
    *) echo "Unknown argument: $arg"; exit 1 ;;
  esac
done

# ── Read current version ─────────────────────────────────────────────────────
CURRENT="$(cat "$VERSION_FILE" | tr -d '[:space:]')"
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"
echo "Current version: ${CURRENT}"

# ── Auto-detect bump level from conventional commits ─────────────────────────
if [[ -z "$BUMP_LEVEL" ]]; then
  LAST_TAG="$(git -C "$REPO_ROOT" tag -l 'v*' --sort=-v:refname | head -1)" || true

  if [[ -n "$LAST_TAG" ]]; then
    RANGE="${LAST_TAG}..HEAD"
    echo "Scanning commits since ${LAST_TAG}..."
  else
    RANGE="HEAD"
    echo "No version tags found. Scanning all commits..."
  fi

  COMMITS="$(git -C "$REPO_ROOT" log "$RANGE" --pretty=format:"%s%n%b" 2>/dev/null)" || true

  if echo "$COMMITS" | grep -qiE '^feat.*!:|BREAKING CHANGE'; then
    BUMP_LEVEL="major"
    echo "Detected: BREAKING CHANGE → major bump"
  elif echo "$COMMITS" | grep -qiE '^feat(\(|:)'; then
    BUMP_LEVEL="minor"
    echo "Detected: feat → minor bump"
  else
    BUMP_LEVEL="patch"
    echo "Detected: fix/chore → patch bump"
  fi
fi

# ── Compute new version ──────────────────────────────────────────────────────
case "$BUMP_LEVEL" in
  major) NEW_VERSION="$((MAJOR + 1)).0.0" ;;
  minor) NEW_VERSION="${MAJOR}.$((MINOR + 1)).0" ;;
  patch) NEW_VERSION="${MAJOR}.${MINOR}.$((PATCH + 1))" ;;
esac

echo ""
echo "  ${CURRENT} → ${NEW_VERSION}  (${BUMP_LEVEL} bump)"
echo ""

if [[ "$DRY_RUN" == true ]]; then
  echo "(dry run — no changes made)"
  exit 0
fi

# ── Apply ─────────────────────────────────────────────────────────────────────
echo "$NEW_VERSION" > "$VERSION_FILE"
echo "Updated ${VERSION_FILE}"

git -C "$REPO_ROOT" add "$VERSION_FILE"
git -C "$REPO_ROOT" commit -m "chore: bump version to ${NEW_VERSION}"
git -C "$REPO_ROOT" tag -a "v${NEW_VERSION}" -m "v${NEW_VERSION}"

echo ""
echo "Created tag v${NEW_VERSION}"
echo "Run 'git push && git push --tags' to publish."
