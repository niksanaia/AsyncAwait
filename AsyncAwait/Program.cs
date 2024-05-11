// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

AsyncLocal<int> asyncValue = new();

for (int i = 0; i < 1000; i++)
{
    asyncValue.Value = i;
    ThreadPool.QueueUserWorkItem(delegate
    {
        Console.WriteLine(asyncValue.Value);
        Thread.Sleep(1000);
    });
}

Console.ReadLine();


public static class MyThreadPool
{
    private static readonly BlockingCollection<(Action, ExecutionContext?)> SWorkItems = new();

    public static void QueueUserWorkItem(Action action) => SWorkItems.Add((action, ExecutionContext.Capture()));

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() =>
            {
                while (true)
                {
                    (Action workItem, ExecutionContext? executionContext) = SWorkItems.Take();
                    if (executionContext == null)
                    {
                        workItem();
                    }
                    else
                    {
                        ExecutionContext.Run(executionContext, state => ((Action?)state!).Invoke(), workItem);
                    }
                }
            }).Start();
        }
    }

    class MyTask
    {
        private bool _completed;
        private Exception? _exception;
        private Action? _action;
        private ExecutionContext? _executionContext;

        public bool IsCompleted
        {
            get
            {
                lock (this)
                {
                    return _completed;
                }
            }
        }

        public void SetResult() => Complete(null);

        public void SetException(Exception exception) => Complete(exception);

        public void Wait()
        {
            ManualResetEventSlim? mres = null;
            lock (this)
            {
                if (!_completed)
                {
                    mres = new ManualResetEventSlim();
                    ContinueWith(mres.Set);
                }
            }

            mres?.Wait();
            if (_exception is not null)
            {
                ExceptionDispatchInfo.Throw(_exception);
            }
        }

        public static MyTask Run(Action action)
        {
            MyTask t = new MyTask();

            MyThreadPool.QueueUserWorkItem(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    t.SetException(e);
                }

                t.SetResult();
            });
            return t;
        }

        public static MyTask WhenAll(List<MyTask> tasks)
        {
            MyTask t = new MyTask();

            if (tasks.Count == 0)
            {
                t.SetResult();
            }
            else
            {
                int remaining = tasks.Count;
                Action continuation = () =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        t.SetResult();
                    }
                };
                foreach (var t1 in tasks)
                {
                    t1.ContinueWith(continuation);
                }
            }

            return t;
        }

        public static MyTask Delay(int timeout)
        {
            MyTask t = new MyTask();
            new Timer(_ => t.SetResult()).Change(timeout, -1);
            return t;
        }
        private void Complete(Exception? exception)
        {
            lock (this)
            {
                if (_completed)
                {
                    throw new InvalidOperationException("something has wrong");
                }

                _completed = true;
                _exception = exception;
                if (_action is not null)
                {
                    QueueUserWorkItem(delegate
                    {
                        if (_executionContext is null)
                        {
                            _action();
                        }
                        else
                        {
                            ExecutionContext.Run(_executionContext, state => ((Action)state!).Invoke(), _action);
                        }
                    });
                }
            }
        }

        public void ContinueWith(Action action)
        {
            lock (this)
            {
                if (_completed)
                {
                    QueueUserWorkItem(action);
                }
                else
                {
                    _action = action;
                    _executionContext = ExecutionContext.Capture();
                }
            }
        }
    }
}