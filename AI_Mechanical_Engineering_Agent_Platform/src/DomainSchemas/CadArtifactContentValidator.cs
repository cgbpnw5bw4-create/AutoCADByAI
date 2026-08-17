namespace DomainSchemas;

/// <summary>
/// Validates physical CAD exchange-file content independently from a file name
/// or a CAD API return value.
/// </summary>
public static class CadArtifactContentValidator
{
    private static readonly byte[] StepHeader = "ISO-10303-21;"u8.ToArray();
    private static readonly byte[] StepSectionEnd = "ENDSEC;"u8.ToArray();
    private static readonly byte[] StepFileEnd = "END-ISO-10303-21;"u8.ToArray();

    public static bool TryValidateStepFile(string path, out string issue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(path));
            if (stream.Length == 0)
            {
                issue = "STEP file is empty.";
                return false;
            }

            var header = new byte[Math.Min(256, checked((int)Math.Min(stream.Length, 256L)))];
            var headerBytesRead = stream.Read(header, 0, header.Length);
            var offset = SkipUtf8BomAndAsciiWhitespace(header.AsSpan(0, headerBytesRead));
            if (!header.AsSpan(offset, headerBytesRead - offset).StartsWith(StepHeader))
            {
                issue = "STEP file does not start with ISO-10303-21; after an optional UTF-8 BOM and whitespace.";
                return false;
            }

            stream.Position = 0;
            var hasSectionEnd = ContainsAsciiToken(stream, StepSectionEnd);
            stream.Position = 0;
            var hasFileEnd = ContainsAsciiToken(stream, StepFileEnd);
            if (!hasSectionEnd || !hasFileEnd)
            {
                issue = "STEP file is missing ENDSEC; or END-ISO-10303-21;.";
                return false;
            }

            issue = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issue = $"STEP file could not be read: {exception.Message}";
            return false;
        }
    }

    private static int SkipUtf8BomAndAsciiWhitespace(ReadOnlySpan<byte> value)
    {
        var offset = value.Length >= 3 &&
                     value[0] == 0xEF &&
                     value[1] == 0xBB &&
                     value[2] == 0xBF
            ? 3
            : 0;
        while (offset < value.Length && value[offset] is 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
        {
            offset++;
        }

        return offset;
    }

    private static bool ContainsAsciiToken(Stream stream, ReadOnlySpan<byte> token)
    {
        Span<byte> buffer = stackalloc byte[4096];
        var matched = 0;
        int bytesRead;
        while ((bytesRead = stream.Read(buffer)) > 0)
        {
            foreach (var value in buffer[..bytesRead])
            {
                if (value == token[matched])
                {
                    matched++;
                    if (matched == token.Length)
                    {
                        return true;
                    }
                }
                else
                {
                    matched = value == token[0] ? 1 : 0;
                }
            }
        }

        return false;
    }
}
