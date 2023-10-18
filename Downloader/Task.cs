using LiteDB;
using System.Collections.Concurrent;
using System.Net;

namespace Downloader
{
    public struct Range
    {
        public long Start { get; set; }
        public long End { get; set; }
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
        public ConcurrentDictionary<int,Range> Ranges { get; private set; }

        public Progress(Task task)
        {
            TaskHandler = task;
            Ranges = new ConcurrentDictionary<int, Range>(Enumerable.Range(0, TaskHandler.Option.ParallelCount).ToDictionary(i => i, i => new Range()));
            Length = TaskHandler.GetFileLength();
        }
        public double Value
        {
            get => (double)Ranges.Sum(item => item.Value.End - item.Value.Start) / Length;
        }
        public void Set(int id, Range range)
        {
            Ranges.AddOrUpdate(id, range, (key, oldValue) => range);
            TaskHandler.OnTaskProgressChanged(Value);
        }
        public Range Get(int id)
        {
            if (!Ranges.TryGetValue(id, out Range range))
            {
                range = Ranges.AddOrUpdate(id, new Range(), (key, oldValue) => new Range());
                TaskHandler.OnTaskProgressChanged(Value);
            }
            return range;
        }
    }


    public class Task : IDisposable
    {
        private bool disposedValue;

        /// <summary>
        /// ID
        /// </summary>
        public ObjectId ID
        {
            get
            {
                id ??= ObjectId.NewObjectId();
                return id;
            }
            set
            {
                if (value != id)
                {
                    id = value;
                }
            }
        }
        private ObjectId? id;

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
        public long GetFileLength(HttpRequestMessage? httpRequest = null)
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
                if (httpResponse.Content.Headers.ContentRange != null && httpResponse.Content.Headers.ContentRange.HasRange && httpResponse.Content.Headers.ContentRange.HasLength)
                {
                    return httpResponse.Content.Headers.ContentRange.Length ?? -1;
                }
                if (httpResponse.Content.Headers.ContentLength.HasValue)
                {
                    return httpResponse.Content.Headers.ContentLength.Value;
                }
                return -1;
            }
            catch (HttpRequestException)
            {
                httpRequest.Method = HttpMethod.Get;
                httpRequest.Headers.Add("Range", "bytes=0-1");
                return GetFileLength(httpRequest);
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                httpResponse?.Dispose();
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
