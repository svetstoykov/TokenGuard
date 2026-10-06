#!/usr/bin/env bash
# Builds the README's OpenAI quick start against packages packed from the working tree,
# then checks that the READMEs still carry the compiled text.
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$project_dir/../.." && pwd)"
work_dir="$repo_root/artifacts/readme-snippets"
program="$project_dir/Program.cs"
readmes=("README.md" "src/TokenGuard.Core/PackageReadme.md")
blocks=("LoopUsings" "Register" "CreateContext" "Loop")

rm -rf "$work_dir" "$project_dir/bin" "$project_dir/obj"

for package in TokenGuard.Core TokenGuard.Extensions.OpenAI; do
  dotnet pack "$repo_root/src/$package/$package.csproj" --configuration Release --nologo --output "$work_dir/feed"
done

# A private packages folder keeps a TokenGuard package of the same version in the global cache out of the build.
dotnet build "$project_dir/TokenGuard.ReadmeSnippets.csproj" --configuration Release --nologo --packages "$work_dir/packages"

failed=0
for block in "${blocks[@]}"; do
  text="$(awk -v first="// <$block>" -v last="// </$block>" '$0 == last { on = 0 } on { print } $0 == first { on = 1 }' "$program")"
  if [ -z "$text" ]; then
    echo "Block <$block> is missing from $program" >&2
    failed=1
    continue
  fi

  for readme in "${readmes[@]}"; do
    if [[ "$(cat "$repo_root/$readme")" != *"$text"* ]]; then
      echo "$readme does not carry block <$block> from tests/TokenGuard.ReadmeSnippets/Program.cs verbatim" >&2
      failed=1
    fi
  done
done

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "README snippets build against the packed packages and match the READMEs."
