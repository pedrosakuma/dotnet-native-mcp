namespace DotnetNativeMcp.Cli.Output;

internal sealed record TableColumn<T>(string Header, Func<T, string> Selector);

internal static class TableRenderer
{
    public static async ValueTask WriteKeyValueRowsAsync(
        TextWriter writer,
        IReadOnlyList<(string Name, string Value)> rows,
        CancellationToken cancellationToken)
    {
        foreach (var (name, value) in rows)
        {
            await writer.WriteLineAsync($"{name,-20} {value}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    public static ValueTask WriteBlankLineAsync(TextWriter writer, CancellationToken cancellationToken) =>
        new(writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken));

    public static async ValueTask WriteGridAsync<T>(
        TextWriter writer,
        string title,
        IReadOnlyList<T> rows,
        IReadOnlyList<TableColumn<T>> columns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        await writer.WriteLineAsync(title.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            await writer.WriteLineAsync("(none)".AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var widths = columns.Select(column => column.Header.Length).ToArray();
        var renderedRows = new string[rows.Count][];

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var values = new string[columns.Count];
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var value = columns[columnIndex].Selector(rows[rowIndex]) ?? string.Empty;
                values[columnIndex] = value;
                widths[columnIndex] = Math.Max(widths[columnIndex], value.Length);
            }

            renderedRows[rowIndex] = values;
        }

        await writer.WriteLineAsync(RenderRow(columns.Select(column => column.Header).ToArray(), widths).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await writer.WriteLineAsync(RenderRow(widths.Select(width => new string('-', width)).ToArray(), widths).AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in renderedRows)
        {
            await writer.WriteLineAsync(RenderRow(row, widths).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string RenderRow(IReadOnlyList<string> values, int[] widths) =>
        string.Join("  ", values.Select((value, index) => value.PadRight(widths[index], ' ')));
}
