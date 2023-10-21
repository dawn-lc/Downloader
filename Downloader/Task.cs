using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

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
        public readonly long Length 
        {
             get => End - Start;
        }
    }
    public struct Chunk
    {
        public readonly bool IsCompleted 
        {
            get => Completed == Range.Length;
        }
        public Range Range { get; set; }
        public long Completed { get; set; }
    }
    public struct Option
    {
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
        public Task TaskHandler { get; }
        public long Length { get; }
        public ConcurrentDictionary<long, Chunk> Chunks { get; }

        public Progress(Task task)
        {
            TaskHandler = task;
            Chunks = new ConcurrentDictionary<long, Chunk>();
            Length = TaskHandler.GetFileLength();
            long chunks = TaskHandler.Option.ParallelCount < 2 || Length < 0 ? 1 : (Length % TaskHandler.Option.ChunkSize) != 0 ? (Length / TaskHandler.Option.ChunkSize) + 1 : Length / TaskHandler.Option.ChunkSize;
            for (long i = 0; i < chunks; i++)
            {
                long start = i * TaskHandler.Option.ChunkSize;
                long end = (i + 1) < chunks ? start + TaskHandler.Option.ChunkSize : Length;
                Chunk chunk = new() { Range = new() { Start = start, End = end }, Completed = 0 };
                Chunks.AddOrUpdate(i, chunk, (key, oldValue) => chunk);
            }
        }

        public double Value
        {
            get => (double)Chunks.Sum(item => item.Value.Completed) / Length;
        }

        public void Set(long id, Chunk chunk)
        {
            Chunks.TryUpdate(id, chunk, chunk);
            TaskHandler.OnTaskProgressChanged(Value);
        }
        public Chunk Get(long id)
        {
            if (!Chunks.TryGetValue(id, out Chunk chunk))
            {
                throw new InvalidOperationException();
            }
            return chunk;
        }
    }


    public class Task : IDisposable
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
        public Progress Progress { get; set; }
        public Option Option { get; }

        public Task(Option option)
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

        public async void Chunk(long id, int tryCount)
        {
            Chunk chunk = Progress.Get(id);
            Uri uri = new(Option.URL);
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Add("Range", $"bytes={chunk.Range.Start}-{chunk.Range.End}");
            long chunkSeek = chunk.Range.Start;
            try
            {
                Stream ResponseStream = await (await ClientHandler.SendAsync(request)).Content.ReadAsStreamAsync();
                byte[] buffer = new byte[Option.ChunkSize];

                int bytesRead;
                using (FileStream destination = new(Option.Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write))
                {
                    while ((bytesRead = await ResponseStream.ReadAsync(buffer)) != 0)
                    {
                        destination.Seek(chunkSeek, SeekOrigin.Begin);
                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
                        buffer.Initialize();
                        chunkSeek = destination.Position;
                        chunk.Completed += bytesRead;
                        Progress.Set(id, chunk);
                    }
                };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
            {
                if (tryCount < 5)
                {
                    tryCount++;
                    await System.Threading.Tasks.Task.Delay(1000 * 5);
                    Chunk(id, tryCount);
                }
                else
                {
                    throw;
                }
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
