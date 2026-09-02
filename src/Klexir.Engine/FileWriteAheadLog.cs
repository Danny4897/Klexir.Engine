using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// File-backed redo log. Each record is <c>[pageId:4][length:4][bytes:length]</c>, appended and fsynced
/// immediately so a record is durable before the caller's write is acknowledged.
/// </summary>
public sealed class FileWriteAheadLog : IWriteAheadLog
{
    private readonly FileStream _file;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<Result<FileWriteAheadLog>> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        await Try.ExecuteAsync(async () =>
        {
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, useAsync: true);
            return new FileWriteAheadLog(file);
        }).ConfigureAwait(false);

    private FileWriteAheadLog(FileStream file) => _file = file;

    public async Task<Result<Unit>> AppendAsync(PageId pageId, ReadOnlyMemory<byte> pageBytes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Try.ExecuteAsync(async () =>
            {
                var header = new byte[8];
                BitConverter.TryWriteBytes(header.AsSpan(0, 4), pageId.Value);
                BitConverter.TryWriteBytes(header.AsSpan(4, 4), pageBytes.Length);

                _file.Seek(0, SeekOrigin.End);
                await _file.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await _file.WriteAsync(pageBytes, cancellationToken).ConfigureAwait(false);
                await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<(PageId PageId, byte[] Bytes)>>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Try.ExecuteAsync(async () =>
            {
                var records = new List<(PageId, byte[])>();
                _file.Seek(0, SeekOrigin.Begin);
                var header = new byte[8];

                while (true)
                {
                    var headerRead = await ReadExactAsync(header, cancellationToken).ConfigureAwait(false);
                    if (headerRead < header.Length)
                    {
                        break;
                    }

                    var pageId = new PageId(BitConverter.ToUInt32(header, 0));
                    var length = BitConverter.ToInt32(header, 4);
                    var bytes = new byte[length];
                    var bodyRead = await ReadExactAsync(bytes, cancellationToken).ConfigureAwait(false);
                    if (bodyRead < length)
                    {
                        break;
                    }

                    records.Add((pageId, bytes));
                }

                return (IReadOnlyList<(PageId, byte[])>)records;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> TruncateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Try.ExecuteAsync(async () =>
            {
                _file.SetLength(0);
                await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        await _file.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<int> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await _file.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
