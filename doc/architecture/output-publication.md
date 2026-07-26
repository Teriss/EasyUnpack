# Output Publication

Every job extracts into a private staging directory, completes nested-archive processing, normalizes the resulting directory tree, and only then moves the finished tree atomically to an archive-named directory beside the source archive. Existing targets receive a stable numeric suffix such as ` (2)`.

The published archive-named root is always retained. Inside that root, a directory chain is redundant when the current directory contains no files and exactly one ordinary child directory. EasyUnpack lifts that child's entries into the current directory and repeats the check. The same rule is applied recursively inside meaningful branches, so a branch that exists alongside other content remains named while redundant layers below it are removed.

Directories with files, mixed file-and-directory content, or multiple child directories are meaningful and remain unchanged. Reparse points are never traversed or collapsed. Normalization happens only in staging, so an error or cancellation cannot partially rewrite an already published output directory.
