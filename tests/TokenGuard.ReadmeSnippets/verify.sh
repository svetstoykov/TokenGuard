#!/usr/bin/env bash
# Builds the README's OpenAI and Anthropic quick starts and the fragments of the OpenAI package readme against packages
# packed from the working tree, then checks that the READMEs still carry the compiled text.
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$project_dir/../.." && pwd)"
work_dir="$repo_root/artifacts/readme-snippets"
program="$project_dir/Program.cs"
readmes=("README.md" "src/TokenGuard.Core/PackageReadme.md")
blocks=("QuickStart")
anthropic_program="$project_dir/AnthropicQuickStart.cs"
anthropic_package_readme="src/TokenGuard.Extensions.Anthropic/PackageReadme.md"
anthropic_blocks=("AnthropicUsings" "AnthropicSetup" "AnthropicLoop")
openai_fragments="$project_dir/OpenAIPackageReadme.cs"
openai_package_readme="src/TokenGuard.Extensions.OpenAI/PackageReadme.md"

rm -rf "$work_dir" "$project_dir/bin" "$project_dir/obj"

for package in TokenGuard.Core TokenGuard.Extensions.OpenAI TokenGuard.Extensions.Anthropic; do
  dotnet pack "$repo_root/src/$package/$package.csproj" --configuration Release --nologo --output "$work_dir/feed"
done

# A private packages folder keeps a TokenGuard package of the same version in the global cache out of the build.
dotnet build "$project_dir/TokenGuard.ReadmeSnippets.csproj" --configuration Release --nologo --packages "$work_dir/packages"

custom_summarizer="$project_dir/CustomSummarizer.cs"

failed=0

# check_blocks <source file> <readme> <block>...
# Fails when the readme does not carry a tagged block of the source file verbatim.
# A block is compared without the indentation of its opening tag, so a block inside a method matches an unindented code fence.
check_blocks() {
  local source="$1" readme="$2" block text
  shift 2

  for block in "$@"; do
    text="$(awk -v first="// <$block>" -v last="// </$block>" '
      { line = $0; sub(/^[ \t]+/, "", line) }
      line == last { on = 0 }
      on { print substr($0, indent + 1) }
      line == first { on = 1; indent = length($0) - length(line) }' "$source")"
    if [ -z "$text" ]; then
      echo "Block <$block> is missing from $source" >&2
      failed=1
      continue
    fi

    if [[ "$(cat "$repo_root/$readme")" != *"$text"* ]]; then
      echo "$readme does not carry block <$block> from ${source#"$repo_root/"} verbatim" >&2
      failed=1
    fi
  done
}

check_blocks "$custom_summarizer" "README.md" "CustomSummarizerExample"

for readme in "${readmes[@]}"; do
  check_blocks "$program" "$readme" "${blocks[@]}"
done

check_blocks "$anthropic_program" "README.md" "${anthropic_blocks[@]}"
check_blocks "$anthropic_program" "$anthropic_package_readme" "${anthropic_blocks[@]}"
check_blocks "$openai_fragments" "$openai_package_readme" "OpenAIUsings" "OpenAIOverview" "OpenAIRegistration" "OpenAIPrepare" "OpenAISend"

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "README snippets build against the packed packages and match the READMEs."
