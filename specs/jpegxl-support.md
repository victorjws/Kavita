## JPEG XL read support
Goal: Recognize `.jxl` pages and serve them to browsers that decode JPEG XL natively, without a server-side decoder.
API: no new endpoints. Existing reader/image/OPDS page responses return `Content-Type: image/jxl` for `.jxl` files.
Models: none changed. `EncodeFormat` is unchanged (no JXL output).
Rules:
- `Parser.ImageFileExtensions` includes `.jxl`; `Parser.IsImage` is true for it (case-insensitive).
- `Parser.IsJxl(path)` is an extension check.
- Page dimensions for `.jxl` are read from the file header (bare codestream or ISOBMFF container), with EXIF orientation 5-8 swapping width/height.
- Cover selection from archives skips `.jxl` entries and picks the next image; all-JXL archives yield no cover.
- `Parser.IsCoverImage` is unchanged so `cover.jxl` is not turned into a chapter by parsers.
- Reader page-preview thumbnails copy `.jxl` pages through unchanged.
- Cover upload (`UploadController`, cover chooser) does not accept JXL.
Errors:
- Malformed/truncated JXL header → dimensions unavailable (no exception), page keeps its index.
Criteria:
- [ ] `IsImage("test.jxl")` and `IsImage("TEST.JXL")` are true
- [ ] `IsJxl` true only for `.jxl` (any case)
- [ ] `JxlHeaderReader` returns correct dimensions for bare, container, non-multiple-of-8 and non-1:1 fixtures
- [ ] `JxlHeaderReader` swaps dimensions for orientation 5-8
- [ ] `JxlHeaderReader` returns false for garbage, truncated, and PNG input
- [ ] `FindCoverImageFilename` prefers `002.png` over `001.jxl`; returns null for all-JXL input
- [ ] `GetCachedFileDimensions` returns a dimension for every page in a mixed JXL/PNG folder with correct page numbers
- [ ] `MimeTypeHelper.GetMimeType(".jxl")` is `image/jxl`; other extensions delegate to `MimeTypeMap`
