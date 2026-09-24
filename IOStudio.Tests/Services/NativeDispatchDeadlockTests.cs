using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// native 派发 (--native-dispatch) 下的播放存活回归测试 —— 守住 AB-BA 死锁。
    ///
    /// <para>背景: 曾经 native 派发一开播就"瘫住"。根因是锁序倒置的死锁，而非求值/换算逻辑错误：
    /// 后台轮询线程 <c>IODeviceController::Update()</c> 持 Registry 再进 <c>MotionPlayer::Tick</c>
    /// (锁序 Registry → Motion)，而编辑器播放线程在 native 派发下先持 Motion 再进设备注册表
    /// (锁序 Motion → Registry)。两把 recursive_mutex 跨线程互等 → 两个驱动同时永久挂起，
    /// 表现为 tick/poll 计数同时冻结、CPU≈0、无异常。</para>
    ///
    /// <para>为什么跑"部署目录"的 exe 而不是测试输出目录: 死锁是竞态，能否命中取决于编辑线程
    /// 持 Motion 的窗口长度。派发到"真实存在的设备"时窗口更长，复现稳定 (实测 4/4 挂起)；
    /// 设备不存在时窗口极短，同一份代码时挂时通 (会漏报)。设备由部署目录的 Config 加载，测试输出
    /// 目录不带设备插件，所以这里显式指向 Binaries\Win64\{Debug|Release}\IOStudio.exe。
    /// 找不到部署产物时跳过 (本用例需要 native + 设备插件已部署)。</para>
    ///
    /// <para>为什么用子进程: 死锁一旦回归，进程内的线程会永久持锁无法回收，若在测试进程里直接跑
    /// 会污染后续所有 touch MotionPlayer 的用例。改为拉起 IOStudio CLI 子进程并等待其退出/读取状态：
    /// 卡死时超时判定为失败，不会挂起整个测试进程。</para>
    ///
    /// <para>阈值: 4 秒播放标称 ~250 次 tick / ~45 次 poll；修复前冻结在 8~43 tick，取 120 作门限。</para>
    /// </summary>
    [Trait("Category", "NativeE2E")]
    public class NativeDispatchDeadlockTests
    {
        private readonly ITestOutputHelper _out;

        public NativeDispatchDeadlockTests(ITestOutputHelper output) => _out = output;

        private const int PlayMs = 4000;
        private const int MinTicks = 120;   // 标称 ~250; 死锁回归时 < 43
        private const int MinPolls = 12;    // 标称 ~45
        private const int Attempts = 3;     // 仅重试"子进程未退出/未完成播放"这类基础设施抖动; 计数断言不重试
        private const int PerAttemptTimeoutMs = 25_000;

        [Fact]
        public async Task NativeDispatch_Playback_KeepsTicking()
        {
            string? deployDir = ResolveDeployDir(out string? exe);
            if (deployDir is null || exe is null)
            {
                _out.WriteLine("跳过: 未找到已部署的 IOStudio.exe (Binaries\\Win64\\{Debug|Release})");
                return;
            }
            _out.WriteLine($"deployDir={deployDir}");

            // 自包含: 生成一份播放时间轴，指向部署 Config 里已配置的设备 (Simulator-1) ——
            // 派发到真实存在的设备才会进入完整的 SetDO 路径，复现窗口才稳定。
            var timeline = MotionTestFactory.Timeline(
                "native_dispatch_probe",
                6000,
                MotionTestFactory.OAxisTrack(
                    "Simulator-1",
                    "MoveX",
                    "OAxis_01",
                    "Pitch",
                    MotionTestFactory.FullClip(
                        6000,
                        MotionTestFactory.LinearKf(0, 0.0f),
                        MotionTestFactory.LinearKf(3000, 1.0f),
                        MotionTestFactory.LinearKf(6000, 0.0f)
                    )
                )
            );

            string motion = Path.Combine(Path.GetTempPath(), $"native_dispatch_{Guid.NewGuid():N}.motion");
            File.WriteAllText(motion, MotionFileReader.ToJson(timeline));

            try
            {
                ChildRun? run = null;
                string? completedJson = null;
                for (int attempt = 1; attempt <= Attempts; attempt++)
                {
                    run = await RunOnceAsync(exe, deployDir, motion);
                    if (!run.Exited)
                    {
                        // 真死锁会稳定命中 (计数冻结) 或挂起；超时多为环境抖动，重试仍有意义。
                        _out.WriteLine($"[attempt {attempt}/{Attempts}] 子进程未在超时内退出，重试");
                        TryDelete(run.StatusFile);
                        continue;
                    }
                    if (!File.Exists(run.StatusFile))
                    {
                        _out.WriteLine($"[attempt {attempt}/{Attempts}] 未写出状态文件，重试");
                        continue;
                    }

                    string json = File.ReadAllText(run.StatusFile);
                    using (var probe = JsonDocument.Parse(json))
                    {
                        string ph = probe.RootElement.TryGetProperty("phase", out var pp) ? pp.GetString() ?? "" : "";
                        if (ph != "exited")
                        {
                            // 进程在播放完成前退出 (只留下初始 "playing" 快照) —— 与死锁无关的抖动。
                            _out.WriteLine($"[attempt {attempt}/{Attempts}] 未完成播放 (phase={ph})，重试");
                            TryDelete(run.StatusFile);
                            continue;
                        }
                    }

                    completedJson = json;
                    break;
                }

                Assert.True(
                    run is { Exited: true },
                    $"IOStudio CLI 连续 {Attempts} 次未在 {PerAttemptTimeoutMs}ms 内退出 —— " +
                    "native 派发下播放永久卡死的典型表现 (AB-BA 死锁)。" +
                    $"stdout='{run?.StdOut}' stderr='{run?.StdErr}'"
                );
                Assert.True(
                    completedJson is not null,
                    $"连续 {Attempts} 次未取得一次完成的播放状态；stdout='{run?.StdOut}' stderr='{run?.StdErr}'"
                );

                using var doc = JsonDocument.Parse(completedJson!);
                var root = doc.RootElement;

                string phase = root.TryGetProperty("phase", out var p) ? p.GetString() ?? "" : "";
                long ticks = root.TryGetProperty("tickCount", out var t) ? t.GetInt64() : -1;
                long polls = root.TryGetProperty("pollCount", out var q) ? q.GetInt64() : -1;
                string tickError = root.TryGetProperty("tickError", out var te) ? te.GetString() ?? "" : "";
                string engineError = root.TryGetProperty("lastEngineError", out var le) ? le.GetString() ?? "" : "";

                _out.WriteLine($"phase={phase} tickCount={ticks} pollCount={polls} " +
                               $"tickError='{tickError}' lastEngineError='{engineError}'");

                Assert.Equal("exited", phase);
                Assert.True(tickError.Length == 0, $"播放 tick 抛异常: {tickError}");
                Assert.True(engineError.Length == 0, $"引擎报错: {engineError}");

                Assert.True(
                    ticks >= MinTicks,
                    $"native 派发下播放 tick 应持续到 --exit-after (标称 ~250)，实际仅 {ticks} —— " +
                    "典型的 AB-BA 死锁回归: 编辑器播放线程与后台轮询线程互相持锁挂起，" +
                    "计数冻结在死锁瞬间 (曾观察到 8~43)。检查 IODeviceController::Update()/EnterSafeState() " +
                    "是否又在持 Registry 时进入 MotionPlayer。"
                );
                Assert.True(
                    polls >= MinPolls,
                    $"后台轮询 threadpool 也应持续推进 (标称 ~45)，实际仅 {polls} —— 与 tick 同时冻结即死锁特征"
                );

                TryDelete(run.StatusFile);
            }
            finally
            {
                TryDelete(motion);
            }
        }

        /// <summary>
        /// 定位部署目录: 环境变量 IOSTUDIO_DEPLOY_DIR 优先；否则从测试输出目录向上找仓库根下的
        /// Binaries\Win64\{Debug|Release}\IOStudio.exe。
        /// </summary>
        private static string? ResolveDeployDir(out string? exe)
        {
            exe = null;

            string? envDir = Environment.GetEnvironmentVariable("IOSTUDIO_DEPLOY_DIR");
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                string p = Path.Combine(envDir, "IOStudio.exe");
                if (File.Exists(p)) { exe = p; return envDir; }
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                foreach (string cfg in new[] { "Debug", "Release" })
                {
                    string candidateDir = Path.Combine(dir.FullName, "Binaries", "Win64", cfg);
                    string candidate = Path.Combine(candidateDir, "IOStudio.exe");
                    if (File.Exists(candidate)) { exe = candidate; return candidateDir; }
                }
            }
            return null;
        }

        private sealed record ChildRun(bool Exited, string StatusFile, string StdOut, string StdErr);

        /// <summary>拉起一次 IOStudio CLI 播放，返回是否在超时内退出及状态文件位置。</summary>
        private static async Task<ChildRun> RunOnceAsync(string exe, string workDir, string motion)
        {
            string status = Path.Combine(Path.GetTempPath(), $"native_dispatch_{Guid.NewGuid():N}.status.json");

            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--motion");
            psi.ArgumentList.Add(motion);
            psi.ArgumentList.Add("--play");
            psi.ArgumentList.Add("--native-dispatch");
            psi.ArgumentList.Add("--live-output");
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--exit-after");
            psi.ArgumentList.Add(PlayMs.ToString());
            psi.ArgumentList.Add("--status-file");
            psi.ArgumentList.Add(status);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 IOStudio CLI 进程");

            // 异步排空管道，避免子进程因输出缓冲写满而阻塞。
            Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderr = proc.StandardError.ReadToEndAsync();

            bool exited;
            using (var cts = new CancellationTokenSource(PerAttemptTimeoutMs))
            {
                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                    exited = true;
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    exited = false;
                }
            }

            string so = await stdout;
            string se = await stderr;

            if (!exited)
                TryDelete(status); // 被强杀的进程状态不完整，丢弃避免误读

            return new ChildRun(exited, status, so, se);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }
}
