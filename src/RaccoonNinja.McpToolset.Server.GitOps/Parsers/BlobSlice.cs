namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>The outcome of slicing a text blob by line range, byte offset, and byte cap.</summary>
/// <param name="Bytes">The bytes to decode and return.</param>
/// <param name="TotalLines">The line count of the whole blob.</param>
/// <param name="Truncated">Whether the byte cap cut the content.</param>
/// <param name="NextByteOffset">The offset of the first byte the cap left out, in the frame <c>byteOffset</c> uses; <c>null</c> when nothing was cut.</param>
public sealed record BlobSlice(byte[] Bytes, int TotalLines, bool Truncated, int? NextByteOffset);