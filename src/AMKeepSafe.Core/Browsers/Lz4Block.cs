namespace AMKeepSafe.Core.Browsers;

/// <summary>
/// Firefox saves its search settings compressed with LZ4 in a "mozLz4" file: the bytes "mozLz40\0",
/// the uncompressed size as four bytes, then one LZ4 block. This reads that format.
/// </summary>
public static class Lz4Block
{
    private static readonly byte[] MozLz4Magic = "mozLz40\0"u8.ToArray();

    public static byte[] DecodeMozLz4(byte[] file)
    {
        if (file.Length < 12 || !file.AsSpan(0, 8).SequenceEqual(MozLz4Magic))
        {
            throw new InvalidDataException("Not a mozLz4 file.");
        }

        var size = BitConverter.ToInt32(file, 8);
        if (size < 0 || size > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("The mozLz4 file says it holds an unlikely amount of data.");
        }

        return Decode(file.AsSpan(12), size);
    }

    /// <summary>Decodes one LZ4 block into exactly <paramref name="outputSize"/> bytes.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> input, int outputSize)
    {
        var output = new byte[outputSize];
        var ip = 0;
        var op = 0;

        while (ip < input.Length)
        {
            var token = input[ip++];

            // Literals: bytes copied as they are.
            var literalLength = token >> 4;
            if (literalLength == 15)
            {
                literalLength += ReadLength(input, ref ip);
            }

            Check(ip + literalLength <= input.Length && op + literalLength <= outputSize);
            input.Slice(ip, literalLength).CopyTo(output.AsSpan(op));
            ip += literalLength;
            op += literalLength;

            // The last sequence has literals only.
            if (ip >= input.Length)
            {
                break;
            }

            // A match: copy bytes from earlier in the output.
            Check(ip + 2 <= input.Length);
            var offset = input[ip] | (input[ip + 1] << 8);
            ip += 2;
            Check(offset > 0 && offset <= op);

            var matchLength = token & 0x0F;
            if (matchLength == 15)
            {
                matchLength += ReadLength(input, ref ip);
            }

            matchLength += 4;
            Check(op + matchLength <= outputSize);

            // Byte by byte, because a match may overlap the bytes it is still writing.
            var from = op - offset;
            for (var i = 0; i < matchLength; i++)
            {
                output[op++] = output[from + i];
            }
        }

        Check(op == outputSize);
        return output;
    }

    private static int ReadLength(ReadOnlySpan<byte> input, ref int ip)
    {
        var length = 0;
        byte b;
        do
        {
            Check(ip < input.Length);
            b = input[ip++];
            length += b;
        }
        while (b == 255);

        return length;
    }

    private static void Check(bool ok)
    {
        if (!ok)
        {
            throw new InvalidDataException("The LZ4 data is damaged.");
        }
    }
}
