#!/usr/bin/env bash
# Relative markdown links: the file or folder a link points to must exist.
#
# Why: the compiler does not read documents and no test runs them. When a document is moved or
# renamed, the links to it break together, and nothing else notices.
#
# Scope: .md files that git tracks, plus untracked files that are not ignored. The ignored
# local/, bin/ and obj/ are therefore left out.
#
# What it catches: inline links and images, `](target)`. Links inside fenced code blocks and
# inline code do not count; rule documents keep their examples of what not to write there.
# External schemes (http:, mailto: and so on) and bare anchors are skipped.
# Fences open and close by the CommonMark rules: a closing fence has no info string and is at
# least as long as the opening one. Toggling on any line that starts with ``` would treat a
# ```bash inside a fence as its end, and every later decision in the file would be off.
#
# A line suffix (`](path/Foo.cs:120)`) is the desktop app's clickable convention. It is removed
# before the file is checked; the line number itself is not checked.
#
# IMPORTANT: existence is checked against git's list of paths, not with `[[ -e ]]`. The macOS
# file system is case-insensitive, so `-e` accepts a link with the wrong case, which is a 404 on
# GitHub and on Linux CI. A side effect is that links to ignored files are reported too, which
# is correct: readers cannot open those files.
#
# Known limits:
# - Anchors are not checked. Heading slug rules for CJK text and punctuation differ between
#   renderers, and checking them would produce many false positives.
# - HTML <a href> / <img src> and reference-style definitions ([id]: path) are not caught.
# - A path in backticks (`docs/x.md`) is not a link, so it is not checked here.
#
# Expected output: nothing, exit 0. Each broken link is listed as "file:line: target" with exit 1.
set -uo pipefail
cd "$(dirname "$0")"

known_paths="$(mktemp)"
trap 'rm -f "$known_paths"' EXIT

git ls-files --cached --others --exclude-standard > "$known_paths"

git ls-files -z --cached --others --exclude-standard -- '*.md' \
  | LC_ALL=C xargs -0 awk -v known="$known_paths" '
    function normalize(p,    n, parts, out, i, m, r) {
      n = split(p, parts, "/")
      m = 0
      for (i = 1; i <= n; i++) {
        if (parts[i] == "" || parts[i] == ".") continue
        if (parts[i] == "..") {
          if (m == 0) return ""
          m--
          continue
        }
        out[++m] = parts[i]
      }
      if (m == 0) return "."
      r = out[1]
      for (i = 2; i <= m; i++) r = r "/" out[i]
      return r
    }

    function check(raw,    t, path, dir, resolved) {
      t = raw
      sub(/^[ \t]+/, "", t)
      if (substr(t, 1, 1) == "<") {
        sub(/^</, "", t)
        sub(/>.*$/, "", t)
      } else {
        sub(/[ \t].*$/, "", t)
      }
      path = t
      sub(/#.*$/, "", path)
      sub(/\?.*$/, "", path)
      # The line suffix goes first: `Foo.cs:120` would otherwise match the scheme pattern and be skipped.
      sub(/:[0-9]+(-[0-9]+)?$/, "", path)
      if (path == "") return
      if (path ~ /^[A-Za-z][A-Za-z0-9+.-]*:/) return
      gsub(/%20/, " ", path)

      if (substr(path, 1, 1) == "/") {
        resolved = normalize(substr(path, 2))
      } else {
        dir = FILENAME
        if (!sub(/\/[^\/]*$/, "", dir)) dir = "."
        resolved = normalize(dir "/" path)
      }

      # An empty result means the link climbs above the repo root, which cannot be verified here.
      if (resolved == "" || !(resolved in exists)) {
        printf "%s:%d: %s\n", FILENAME, FNR, raw
        bad = 1
      }
    }

    function run_length(s, ch,    n) {
      n = 0
      while (substr(s, n + 1, 1) == ch) n++
      return n
    }

    BEGIN {
      exists["."] = 1
      while ((getline p < known) > 0) {
        exists[p] = 1
        while (sub(/\/[^\/]*$/, "", p)) exists[p] = 1
      }
      close(known)
    }

    FNR == 1 { fence_char = "" }

    {
      line = $0
      lead = line
      sub(/^[ \t]+/, "", lead)
      if (fence_char != "") {
        n = run_length(lead, fence_char)
        if (n >= fence_size && substr(lead, n + 1) ~ /^[ \t]*$/) fence_char = ""
        next
      }
      c = substr(lead, 1, 1)
      if ((c == "`" || c == "~") && run_length(lead, c) >= 3) {
        fence_char = c
        fence_size = run_length(lead, c)
        next
      }

      gsub(/`[^`]*`/, "", line)
      while (match(line, /\]\([^)]*\)/)) {
        check(substr(line, RSTART + 2, RLENGTH - 3))
        line = substr(line, RSTART + RLENGTH)
      }
    }

    END { exit bad }
  ' || exit 1
