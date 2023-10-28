using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;

namespace Downloader
{
    public struct Range
    {
        public long Start
        {
            readonly get => start;
            set
            {
                if (value > end)
                {
                    throw new ArgumentException("The start is greater than the end", nameof(Start));
                }
                start = value;
            }
        }
        private long start;
        public long End
        {
            readonly get => end;
            set
            {
                if (value < start)
                {
                    throw new ArgumentException("The end is smaller than the start", nameof(End));
                }
                end = value;
            }
        }
        private long end;
        public readonly int Length 
        {
            get
            {
                try
                {
                    return checked((int)(End - Start));
                }
                catch (OverflowException)
                {
                    return int.MaxValue;
                }
            }
        }
        
    }

    public class ChunkQueue
    {
        public int Length { get => Chunks.Length; }
        public Chunk[] Chunks { get; private set; }
        private object ChunksLock = new();
        public ChunkQueue(Chunk[] chunks)
        {
            Chunks = chunks;
        }
        public ChunkQueue(long fileLength, long chunkSize)
        {
            long length = (fileLength < 0 ? 1 : (fileLength % chunkSize) != 0 ? (fileLength / chunkSize) + 1 : fileLength / chunkSize);
            Chunks = new Chunk[length];
            for (int i = 0; i < length; i++)
            {
                long start = i * chunkSize;
                long end = (i + 1) < length ? start + chunkSize : fileLength;
                Chunks[i] = new(new() { Start = start, End = end });
            }
        }
        public bool Dequeue(out Chunk? chunk)
        {
            lock (ChunksLock)
            {
                int index = Array.FindIndex(Chunks, i => i.State == State.Waiting);
                chunk = index >= 0 ? Chunks[index] : null;
                return chunk != null;
            }
        }
    }
    public enum State
    {
        Waiting,
        Downloading,
        Completed
    }
    public class Chunk
    {
        public event Action<double>? ChunkProgressChanged;
        public event Action<State>? ChunkStateChanged;

        private long completed;
        public long Completed
        {
            get => completed;
            set
            {
                completed = value;
                ChunkProgressChanged?.Invoke((double)completed / Range.Length);
                State = completed >= Range.Length ? State.Completed : State.Downloading;
            }
        }

        private State state;
        public State State
        {
            get => state;
            set
            {
                if (state != value)
                {
                    state = value;
                    ChunkStateChanged?.Invoke(state);
                }
            }
        }

        public Range Range { get; set; }

        public Chunk(Range range, long completed = 0)
        {
            Range = range;
            Completed = completed;
            State = State.Waiting;
        }
    }

    public struct Option
    { 
        public int Attempts { get; set; }
        public int Delay { get; set; }
        public string Path { get; set; }
        public string URL { get; set; }
        public int ParallelCount { get; set; }
        public int ChunkSize { get; set; }
        public string? UserAgent { get; set; }
        public string? Cookies { get; set; }
        public string? Proxy { get; set; }
    }

    public class Progress
    {
        public DownloadTask TaskHandler { get; }
        public long Length { get; }
        public ChunkQueue ChunkQueue { get; }

        public Progress(DownloadTask task)
        {
            TaskHandler = task;
            Length = TaskHandler.GetFileLength();
            ChunkQueue = new(Length, TaskHandler.Option.ChunkSize);
        }

        public double Value
        {
            get => (double)ChunkQueue.Chunks.Sum(item => item.Completed) / Length;
        }
    }


    public class DownloadTask : IDisposable
    {
        private bool disposedValue;

        private ITaskState? state;
        public ITaskState State
        {
            get => state ?? Stopped;
            set => state = value;
        }

        private ITaskState started;
        public ITaskState Started
        {
            get => started;
            set => started = value;
        }
        private ITaskState stopped;
        public ITaskState Stopped
        {
            get => stopped;
            set => stopped = value;
        }
        private ITaskState completed;
        public ITaskState Completed
        {
            get => completed;
            set => completed = value;
        }
        private ITaskState failed;
        public ITaskState Failed
        {
            get => failed;
            set => failed = value;
        }
        public void Start()
        {
            State.Start();
        }
        public void Stop()
        {
            State.Stop();
        }
        public void Complete()
        {
            State.Complete();
        }
        public void Fail()
        {
            State.Fail();
        }
        public ITaskState GetState()
        {
            return State;
        }


        public event Action<double>? TaskProgressChanged;
        public event Action<ITaskState>? TaskStateChanged;
        public void OnTaskProgressChanged(double data)
        {
            TaskProgressChanged?.Invoke(data);
        }
        public void OnTaskStateChanged()
        {
            TaskStateChanged?.Invoke(State);
        }

        
        private SocketsHttpHandler SocketsHandler { get; set; }
        private HttpClient ClientHandler { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public Progress Progress { get; set; }
        public Option Option { get; }

        public DownloadTask(Option option)
        {
            Option = option;
            started = new TaskStarted(this);
            stopped = new TaskStoped(this);
            completed = new TaskCompleted(this);
            failed = new TaskFailed(this);
            SocketsHandler = new()
            {
                AutomaticDecompression = DecompressionMethods.GZip,
                PooledConnectionLifetime = TimeSpan.FromSeconds(60),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
                MaxConnectionsPerServer = 256,
                AllowAutoRedirect = true
            };
            if (Option.Proxy != null && Uri.TryCreate(Option.Proxy, UriKind.Absolute, out Uri? Proxy))
            {
                SocketsHandler.Proxy = new WebProxy(Proxy);
            }
            ClientHandler = new(SocketsHandler);
            ClientHandler.DefaultRequestHeaders.Add("User-Agent", Option.UserAgent ?? $"{Environment.Name} {string.Join(".", Environment.Version)}");
            if (Option.Cookies != null)
            {
                ClientHandler.DefaultRequestHeaders.Add("cookie", Option.Cookies);
            }
            Progress = new(this);
            CancellationTokenSource = new();
            using (FileStream destination = new(Option.Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write))
            {

            }
        }
        public static bool IsSupportRange(HttpResponseMessage response)
        {
            return response.Headers.AcceptRanges.Contains("bytes") || response.Content.Headers.ContentRange != null;
        }
        public long GetFileLength(HttpRequestMessage? httpRequest = null, int tryCount = 0)
        {
            Uri uri = new(Option.URL);
            HttpResponseMessage? httpResponse = null;
            httpRequest ??= new(HttpMethod.Head, uri);
            try
            {
                httpResponse = ClientHandler.Send(httpRequest);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException();
                }
                if (!IsSupportRange(httpResponse))
                {
                    throw new NotSupportedException("Not supported range");
                }
                if (httpResponse.Content.Headers.ContentRange != null && httpResponse.Content.Headers.ContentRange.HasRange && httpResponse.Content.Headers.ContentRange.HasLength)
                {
                    return httpResponse.Content.Headers.ContentRange.Length ?? throw new Exception("Unable to get file length");
                }
                if (httpResponse.Content.Headers.ContentLength.HasValue)
                {
                    return httpResponse.Content.Headers.ContentLength.Value;
                }
                throw new Exception("Unable to get file length");
            }
            catch (HttpRequestException)
            {
                if (tryCount < 1)
                {
                    tryCount++;
                    httpRequest = new(HttpMethod.Get, uri);
                    httpRequest.Headers.Add("Range", "bytes=0-1");
                    return GetFileLength(httpRequest, tryCount);
                }
                throw;
            }
            catch (Exception)
            {
                tryCount++;
                if (tryCount < 5)
                {
                    HttpRequestHeaders Headers = httpRequest.Headers;
                    httpRequest = new(httpRequest.Method, uri);
                    foreach (var item in Headers)
                    {
                        httpRequest.Headers.Add(item.Key, item.Value);
                    }
                    return GetFileLength(httpRequest, tryCount);
                }
                throw;
            }
            finally
            {
                httpResponse?.Dispose();
            }
        }
        public async Task Download(Chunk chunk, CancellationToken? cancellationToken = null)
        {
            CancellationToken ct = cancellationToken ?? CancellationToken.None;
            using (IMemoryOwner<byte> bufferOwner = MemoryPool<byte>.Shared.Rent(chunk.Range.Length))
            {
                for (int tryCount = 0; tryCount < Option.Attempts; tryCount++)
                {
                    try
                    {
                        HttpRequestMessage request = new(HttpMethod.Get, Option.URL);
                        request.Headers.Add("Range", $"bytes={chunk.Range.Start}-{chunk.Range.End}");
                        using var response = await ClientHandler.SendAsync(request, ct);
                        using var responseStream = await response.Content.ReadAsStreamAsync(ct);

                        var buffer = bufferOwner.Memory;
                        int bytesRead;
                        int offset = 0;

                        while ((bytesRead = await responseStream.ReadAsync(buffer.Slice(offset, chunk.Range.Length - offset), ct)) != 0)
                        {
                            offset += bytesRead;
                            chunk.Completed += bytesRead;
                            ct.ThrowIfCancellationRequested();
                        }
                        using (FileStream destination = new(Option.Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write))
                        {
                            await destination.WriteAsync(buffer.Slice(0, offset));
                        }
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (tryCount >= (Option.Attempts - 1))
                        {
                            throw new Exception($"Failed to download chunk after {tryCount} attempts", ex);
                        }
                        await Task.Delay(Option.Delay);
                    }
                }
                throw new Exception($"Failed to download chunk after {Option.Attempts} attempts");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                    ClientHandler.Dispose();
                    SocketsHandler.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
