// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// <see cref="PriorityLevel"/> ↔ <see cref="ProcessPriorityClass"/> の対応。
/// 移植元: src/cpp/models/ProcessPriorityRule.cpp の priorityLevelToProcessPriorityClass。
/// </summary>
public static class PriorityLevelMapping
{
    public static ProcessPriorityClass ToProcessPriorityClass(PriorityLevel level) => level switch
    {
        PriorityLevel.Idle => ProcessPriorityClass.Idle,
        PriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
        PriorityLevel.Normal => ProcessPriorityClass.Normal,
        PriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
        PriorityLevel.High => ProcessPriorityClass.High,
        PriorityLevel.Realtime => ProcessPriorityClass.RealTime,
        _ => ProcessPriorityClass.Normal,
    };
}
