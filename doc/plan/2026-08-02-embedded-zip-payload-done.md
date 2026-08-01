# Embedded ZIP Payload Support

## Problem

Some downloaded files are valid media containers with a ZIP archive embedded near the end. Their first bytes identify the media file, while media container data may appear both before and after the ZIP payload. EasyUnpack currently probes only the first 512 bytes, and 7-Zip cannot reliably open a large prefixed ZIP directly.

## Implementation

- Extend archive probing to recognize a structurally valid ZIP payload near end-of-file and report its byte range.
- Carry the embedded payload offset and length through archive candidate discovery.
- Materialize only the embedded archive range in the extraction job's temporary working directory before invoking an engine adapter.
- Preserve the original selected file until extraction and output publication have completed successfully.
- Document the embedded-payload boundary in the archive engine architecture decision.

## Tests

- Add focused probe tests for valid appended ZIP payloads and misleading ZIP markers.
- Add candidate discovery coverage for propagation of the embedded offset.
- Add a 7-Zip integration test proving that a prefixed ZIP is normalized and extracted through the adapter.

## Verification

- Verify the reported payload offset for the supplied MP4/ZIP file.
- Run `dotnet test EasyUnpack.slnx`.
- Run `dotnet build EasyUnpack.slnx --configuration Release`.
