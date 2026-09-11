// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.App;

/// <summary>
/// <c>/powertoggle</c> コマンドライン引数の送受信を担う名前付きカーネルオブジェクト IPC。
/// 名前は <see cref="AppConstants.PowerToggleEventName"/>
/// （specs/014-port-to-csharp/data-model.md §6「名前付きカーネルオブジェクト」）。
/// </summary>
/// <remarks>
/// 現行 C++ 版は <c>App</c> クラス内に直接実装されている（<c>App::startPowerToggleListener</c> /
/// <c>EntryPoint_PowerToggle.cpp</c>）が、C# 版では独立クラスへ抽出した
/// （specs/014-port-to-csharp/data-model.md §3 実行時モデル: 「<c>App</c> 内実装 → <c>EventWaitHandle</c> 化」）。
/// カーネルオブジェクトの生成方法（自動リセット・無名初期状態 false）と、停止用
/// 手動リセットイベントを <c>WaitHandle.WaitAny</c> で束ねて監視する構成は、現行 C++ 版
/// （<c>::CreateEventW(nullptr, FALSE, FALSE, name)</c> + 停止用 <c>CreateEventW(nullptr, TRUE, FALSE, nullptr)</c>）
/// と等価にし、移行期間中に C++ 版・C# 版のどちらが常駐していても相互にシグナルできるようにしている。
/// </remarks>
public static class PowerToggleIpc
{
    /// <summary>
    /// 既存の常駐インスタンスが待機している <see cref="EventWaitHandle"/> を探し、見つかれば
    /// シグナルする。<c>/powertoggle</c> 起動時、常駐の先行判定に使う
    /// （現行 C++ 版 <c>EntryPoint_PowerToggle.cpp</c> の <c>OpenEventW</c> + <c>SetEvent</c> に相当）。
    /// </summary>
    /// <returns>常駐インスタンスへシグナルできた場合は <see langword="true"/>、
    /// 見つからなかった場合は <see langword="false"/>。</returns>
    public static bool TrySignalExisting()
    {
        if (!EventWaitHandle.TryOpenExisting(AppConstants.PowerToggleEventName, out EventWaitHandle? handle)
            || handle is null)
        {
            return false;
        }

        using (handle)
        {
            handle.Set();
        }

        return true;
    }

    /// <summary>
    /// 常駐側が使う受信リスナー。専用の待機スレッドで <c>/powertoggle</c> シグナルを監視し、
    /// 受信のたびに <see cref="Signaled"/> を発火する。
    /// </summary>
    /// <remarks>
    /// <see cref="Signaled"/> は既定では待機専用スレッド上で直接発火する。UI（WinForms）の
    /// コントロール・トレイメニューを扱うハンドラを購読する場合、呼び出し元がコンストラクタへ
    /// UI スレッドの <see cref="SynchronizationContext"/> を渡すこと。渡された場合は
    /// <see cref="SynchronizationContext.Post"/> 経由で <see cref="Signaled"/> を発火するため、
    /// 購読側は追加のスレッドマーシャリングを行わずに安全に UI を操作できる。
    /// </remarks>
    public sealed class Listener : IDisposable
    {
        private readonly SynchronizationContext? _synchronizationContext;
        private readonly EventWaitHandle _signalHandle;
        private readonly ManualResetEvent _stopHandle = new(initialState: false);
        private readonly Thread _waitThread;
        private bool _disposed;

        /// <summary>
        /// 待機を開始する。
        /// </summary>
        /// <param name="synchronizationContext">
        /// <see cref="Signaled"/> の発火先。UI スレッドで安全に購読したい場合は、その
        /// スレッドの <see cref="SynchronizationContext.Current"/> を渡すこと。
        /// <see langword="null"/> の場合は待機専用スレッド上で直接発火する
        /// （呼び出し元が必要に応じて自前で UI スレッドへ Post すること）。
        /// </param>
        public Listener(SynchronizationContext? synchronizationContext = null)
        {
            _synchronizationContext = synchronizationContext;
            // 現行 C++ 版と同じ構成: 無名初期状態 false・自動リセット・名前付き（相互シグナル用）。
            _signalHandle = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: AppConstants.PowerToggleEventName);

            _waitThread = new Thread(WaitLoop)
            {
                IsBackground = true,
                Name = "PowerToggleIpc.Listener",
            };
            _waitThread.Start();
        }

        /// <summary>
        /// <c>/powertoggle</c> シグナルを受信するたびに発火する。
        /// 発火スレッドの扱いはコンストラクタ引数 <c>synchronizationContext</c> を参照。
        /// </summary>
        public event Action? Signaled;

        private void WaitLoop()
        {
            // 生成コストを避けるためループの外で 1 度だけ配列化する。
            WaitHandle[] handles = [_signalHandle, _stopHandle];
            while (true)
            {
                int signaledIndex = WaitHandle.WaitAny(handles);
                if (signaledIndex == 1)
                {
                    // _stopHandle。Dispose() からの停止要求。
                    return;
                }

                if (_synchronizationContext is not null)
                {
                    _synchronizationContext.Post(static state => ((Listener)state!).RaiseSignaled(), this);
                }
                else
                {
                    RaiseSignaled();
                }
            }
        }

        private void RaiseSignaled() => Signaled?.Invoke();

        /// <summary>待機スレッドを停止し、ハンドルを解放する。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopHandle.Set();
            _waitThread.Join();
            _signalHandle.Dispose();
            _stopHandle.Dispose();
        }
    }
}
