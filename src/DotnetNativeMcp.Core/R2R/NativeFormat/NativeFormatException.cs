namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// Thrown by the low-level NativeFormat decoders (<see cref="NativeReader"/>,
/// <see cref="NativePrimitiveDecoder"/>, <see cref="NativeParser"/>) when the
/// underlying blob is truncated, out of range, or otherwise malformed.
/// </summary>
/// <remarks>
/// The decoders are a faithful port of the runtime's
/// <c>Internal.NativeFormat</c> reader, which throws <see cref="BadImageFormatException"/>
/// on malformed input. To honour the "tools never throw to the caller"
/// convention, the tool-facing R2R section readers catch this sentinel and
/// convert it into a <c>NativeResult.Fail(InvalidArgument)</c> envelope.
/// </remarks>
internal sealed class NativeFormatException : Exception
{
    public NativeFormatException(string message) : base(message)
    {
    }

    public NativeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
