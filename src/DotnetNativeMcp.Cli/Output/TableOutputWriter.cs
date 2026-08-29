using System.Collections;
using System.Reflection;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;

namespace DotnetNativeMcp.Cli.Output;

public sealed class TableOutputWriter(TextWriter writer) : IOutputWriter
{
    public async ValueTask WriteAsync<T>(NativeResult<T> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await writer.WriteLineAsync(result.Summary.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (result.Error is not null)
        {
            await WriteRowAsync("kind", result.Error.Kind, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync("message", result.Error.Message, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.Error.Detail))
            {
                await WriteRowAsync("detail", result.Error.Detail, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (result.Data is null)
        {
            return;
        }

        if (result.Data is ITableRenderable renderable)
        {
            await renderable.WriteTableAsync(writer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.Data is IEnumerable<InstructionView> instructions)
        {
            await WriteInstructionTableAsync(instructions, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var property in result.Data.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(result.Data);
            await WriteRowAsync(property.Name, FormatValue(value), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteInstructionTableAsync(
        IEnumerable<InstructionView> instructions,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{"Address",-18} {"Bytes",-20} {"Mnemonic",-12} Operands".AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var instruction in instructions)
        {
            await writer.WriteLineAsync(
                    $"{instruction.AddressHex,-18} {instruction.Bytes,-20} {instruction.Mnemonic,-12} {instruction.Operands}".AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask WriteRowAsync(string name, string value, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{name,-20} {value}".AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable sequence)
        {
            var items = new List<string>();
            foreach (var item in sequence)
            {
                items.Add(item?.ToString() ?? string.Empty);
            }

            return string.Join(", ", items);
        }

        return value.ToString() ?? string.Empty;
    }
}
