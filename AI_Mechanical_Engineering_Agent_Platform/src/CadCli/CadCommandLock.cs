using System;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace CadCli
{
    /// <summary>同一用户的 CAD 命令串行执行; 不写状态文件, 进程退出后系统释放互斥。</summary>
    internal sealed class CadCommandLock : IDisposable
    {
        readonly Mutex mutex;

        CadCommandLock(Mutex mutex) { this.mutex = mutex; }

        internal static CadCommandLock Acquire(string name = null)
        {
            // 不按 checkout 区分: 不同目录仍会临时修改同一用户的 SolidWorks 选项。
            if (name == null)
            {
                using var identity = WindowsIdentity.GetCurrent();
                name = @"Global\AutoCADByAI.SolidWorks." + identity.User.Value;
            }
            var mutex = new Mutex(false, name);
            bool acquired;
            try
            {
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("另一个 cad 命令正在执行, 本次未执行。请等待其结束后重试。");
                return new CadCommandLock(mutex);
            }
            catch { mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }
}
