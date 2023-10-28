namespace Downloader
{
    public interface ITaskState
    {
        /// <summary>
        /// 开始
        /// </summary>
        void Start();
        /// <summary>
        /// 停止
        /// </summary>
        void Stop();

        /// <summary>
        /// 完成
        /// </summary>
        void Complete();

        /// <summary>
        /// 失败
        /// </summary>
        void Fail();
    }

    public class TaskStarted : ITaskState
    {
        private DownloadTask TaskHandler { get; set; }
        public TaskStarted(DownloadTask taskHandler)
        {
            TaskHandler = taskHandler;
        }
        public void Start()
        {
        }

        public void Stop()
        {

            TaskHandler.CancellationTokenSource.Cancel();
            TaskHandler.State = TaskHandler.Stopped;
            TaskHandler.OnTaskStateChanged();
        }

        public void Complete()
        {
            TaskHandler.State = TaskHandler.Completed;
            TaskHandler.OnTaskStateChanged();
        }

        public void Fail()
        {
            TaskHandler.State = TaskHandler.Failed;
            TaskHandler.OnTaskStateChanged();
        }
    }

    public class TaskStoped : ITaskState
    {
        private DownloadTask TaskHandler { get; set; }
        public TaskStoped(DownloadTask taskHandler)
        {
            TaskHandler = taskHandler;
        }
        public void Start()
        {
            
            TaskHandler.State = TaskHandler.Started;
        }

        public void Stop()
        {

        }

        public void Complete()
        {
            TaskHandler.State = TaskHandler.Completed;
        }

        public void Fail()
        {

            TaskHandler.State = TaskHandler.Failed;
        }
    }

    public class TaskCompleted : ITaskState
    {
        private DownloadTask TaskHandler { get; set; }
        public TaskCompleted(DownloadTask taskHandler)
        {
            TaskHandler = taskHandler;
        }
        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Complete()
        {
        }

        public void Fail()
        {
        }
    }

    public class TaskFailed : ITaskState
    {
        private DownloadTask TaskHandler { get; set; }
        public TaskFailed(DownloadTask taskHandler)
        {
            TaskHandler = taskHandler;
        }
        public void Start()
        {
            TaskHandler.State = TaskHandler.Started;
        }

        public void Stop()
        {
        }

        public void Complete()
        {
        }

        public void Fail()
        {
        }
    }

}
