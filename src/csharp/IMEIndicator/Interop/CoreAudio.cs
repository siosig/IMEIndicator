// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace IMEIndicator.Interop;

// Core Audio（IMMDeviceEnumerator → IMMDevice → IAudioEndpointVolume）の COM 相互運用定義。
// 移植元: Windows SDK ヘッダ mmdeviceapi.h / endpointvolume.h（C++ 版は
// src/cpp/services/hotkey/commands/VolumeCommands.cpp が #include している）。
// アクティブ化ロジック（CoCreateInstance 呼び出し・COM 初期化）は
// Services/Hotkey/Commands/VolumeCommands.cs 側に置く（このファイルはインターフェース定義のみ）。
//
// 技術選定: .NET 10 のソース生成 COM 相互運用（System.Runtime.InteropServices.Marshalling の
// [GeneratedComInterface]）を使用する。[ComImport] ベースの旧来の相互運用より AOT フレンドリーで
// 実行時コード生成が無く、ソース生成コード検証を context7 で行える。
//
// 参照した公式ドキュメント:
// - https://learn.microsoft.com/dotnet/standard/native-interop/comwrappers-source-generation
//   「Basic usage」: [GeneratedComInterface] + [Guid] を partial interface（internal/public 限定）に付与する。
//   「Implicit HRESULTs and PreserveSig」: 既定では HRESULT 失敗が例外化される。[PreserveSig] を付けると
//   ネイティブ同様に HRESULT を int で受け取れる（本ファイルでは全メソッドに付与し、C++ 版の
//   FAILED(hr) 方式の呼び出し元と対称にしている）。
// - https://learn.microsoft.com/dotnet/standard/native-interop/tutorial-comwrappers
//   「COM Activation with ComWrappers」: CoCreateInstance の生ポインタを
//   ComWrappers.GetOrCreateObjectForComInstance で包む定型パターン（呼び出し側で使用）。
//
// [GeneratedComInterface] の vtable スロットはインターフェース内のメソッド「宣言順」で暗黙的に決まり、
// 明示的なスロット指定はできない。そのため、実際には呼び出さないメソッドであっても、
// 呼び出したいメソッドより前の実際の COM vtable 順序にあるものは「未使用だが宣言だけする」
// プレースホルダとして含めている（コメントで明示）。
// GUID・メソッド宣言順は Windows SDK ヘッダの標準値。NAudio
// （https://github.com/naudio/NAudio の NAudio.Wasapi/CoreAudioApi/Interfaces/
// {IMMDeviceEnumerator,IMMDevice,IAudioEndpointVolume}.cs、[ComImport] 版で長年実運用されている）
// の宣言と GUID・メソッド順序が一致することを照合済み。

/// <summary>EDataFlow（mmdeviceapi.h）。本実装では Render のみ使用。</summary>
internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

/// <summary>ERole（mmdeviceapi.h）。本実装では Console のみ使用。</summary>
internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

/// <summary>
/// IMMDeviceEnumerator（mmdeviceapi.h）。使用するのは GetDefaultAudioEndpoint のみ。
/// </summary>
[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    // vtable スロット 0。未使用（GetDefaultAudioEndpoint をスロット 1 に合わせるためのプレースホルダ）。
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out nint ppDevices);

    // vtable スロット 1。既定の再生/録音エンドポイントの IMMDevice を取得する。
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out nint ppEndpoint);
}

/// <summary>
/// IMMDevice（mmdeviceapi.h）。使用するのは Activate のみ。
/// </summary>
[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    // vtable スロット 0。pActivationParams（PROPVARIANT*）は本実装では常に 0（nullptr）を渡す。
    [PreserveSig]
    int Activate(in Guid iid, uint dwClsCtx, nint pActivationParams, out nint ppInterface);
}

/// <summary>
/// IAudioEndpointVolume（endpointvolume.h）。使用するのは SetMasterVolumeLevelScalar /
/// GetMasterVolumeLevelScalar / SetMute / GetMute のみ。
/// </summary>
[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    // vtable スロット 0〜3、5、7〜10: 未使用（後続メソッドの位置合わせのためのプレースホルダ）。
    [PreserveSig]
    int RegisterControlChangeNotify(nint pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(nint pNotify);

    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, nint pguidEventContext);

    // vtable スロット 4。マスター音量を正規化値（0.0〜1.0）で設定する。
    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, nint pguidEventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);

    // vtable スロット 6。マスター音量を正規化値（0.0〜1.0）で取得する。
    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);

    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, nint pguidEventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, nint pguidEventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    // vtable スロット 11。ミュート状態を設定する。
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, nint pguidEventContext);

    // vtable スロット 12。ミュート状態を取得する。
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

/// <summary>Core Audio のコクラス CLSID（mmdeviceapi.h）。</summary>
internal static class CoreAudioClsids
{
    /// <summary>
    /// MMDeviceEnumerator コクラス。<c>CoCreateInstance</c> にこの CLSID と
    /// <c>typeof(IMMDeviceEnumerator).GUID</c> を渡すと IMMDeviceEnumerator を取得できる。
    /// </summary>
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
}
