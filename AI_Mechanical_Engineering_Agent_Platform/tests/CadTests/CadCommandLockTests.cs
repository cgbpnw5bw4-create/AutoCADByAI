using System;
using System.IO;
using System.Threading;
using CadCli;
using Xunit;

namespace CadTests
{
    /// <summary>只验证系统互斥, 不创建 SolidWorks 实例或平台状态文件。</summary>
    public class CadCommandLockTests
    {
        static string Name() { return @"Local\CadCommandLockTests_" + Guid.NewGuid().ToString("N"); }

        static Exception OnOtherThread(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            return error;
        }

        [Fact]
        public void SecondDriverIsRejectedUntilWholeCommandCompletes()
        {
            string name = Name();
            using (CadCommandLock.Acquire(name))
            {
                var error = Assert.IsType<IOException>(OnOtherThread(() => { using var other = CadCommandLock.Acquire(name); }));
                Assert.Contains("另一个 cad 命令", error.Message);
            }
            Assert.Null(OnOtherThread(() => { using var next = CadCommandLock.Acquire(name); }));
        }

        [Fact]
        public void ExceptionInCommandReleasesLock()
        {
            string name = Name();
            Action command = () =>
            {
                using var lease = CadCommandLock.Acquire(name);
                throw new InvalidOperationException("命令失败");
            };
            Assert.Throws<InvalidOperationException>(command);
            Assert.Null(OnOtherThread(() => { using var next = CadCommandLock.Acquire(name); }));
        }

        [Fact]
        public void AbandonedOwnerDoesNotBlockNextCommand()
        {
            string name = Name();
            Mutex abandoned = null;
            var thread = new Thread(() =>
            {
                abandoned = new Mutex(false, name);
                abandoned.WaitOne(); // 模拟持有线程意外退出, 不调用 ReleaseMutex。
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            try { using var lease = CadCommandLock.Acquire(name); }
            finally { abandoned?.Dispose(); }
        }
    }
}
