// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Concurrent;

namespace DuckovTogetherServer.Core.Logging;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5
}

public static class Log
{
    private static readonly object _lock = new();
    private static StreamWriter? _fileWriter;
    private static LogLevel _minLevel = LogLevel.Debug;
    private static bool _enableColors = true;
    private static bool _enableFile = true;
    private static string _logPath = "";
    private static readonly ConcurrentQueue<string> _buffer = new();
    
    public static void Initialize(string logDirectory = "logs", LogLevel minLevel = LogLevel.Debug)
    {
        _minLevel = minLevel;
        
        if (_enableFile)
        {
            try
            {
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _logPath = Path.Combine(logDirectory, $"server_{timestamp}.log");
                _fileWriter = new StreamWriter(_logPath, false, Encoding.UTF8) { AutoFlush = true };
            }
            catch { _enableFile = false; }
        }
    }
    
    public static void Shutdown()
    {
        lock (_lock)
        {
            _fileWriter?.Flush();
            _fileWriter?.Close();
            _fileWriter = null;
        }
    }
    
    public static void Trace(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Trace, message, file, line);
    
    public static void Debug(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Debug, message, file, line);
    
    public static void Info(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Info, message, file, line);
    
    public static void Warn(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Warn, message, file, line);
    
    public static void Error(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Error, message, file, line);
    
    public static void Error(Exception ex, string context = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var msg = string.IsNullOrEmpty(context) 
            ? $"{ex.GetType().Name}: {ex.Message}" 
            : $"{context} | {ex.GetType().Name}: {ex.Message}";
        Write(LogLevel.Error, msg, file, line);
        if (ex.StackTrace != null)
            WriteRaw(LogLevel.Error, $"  Stack: {ex.StackTrace.Split('\n')[0].Trim()}");
    }
    
    public static void Fatal(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Fatal, message, file, line);
    
    private static void Write(LogLevel level, string message, string file, int line)
    {
        if (level < _minLevel) return;
        
        var time = DateTime.Now.ToString("HH:mm:ss.fff");
        var fileName = Path.GetFileNameWithoutExtension(file);
        var prefix = $"[{time}] [{LevelTag(level)}] [{fileName}:{line}]";
        var fullMessage = $"{prefix} {message}";
        
        lock (_lock)
        {
            if (_enableColors)
            {
                Console.ForegroundColor = GetColor(level);
                Console.WriteLine(fullMessage);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(fullMessage);
            }
            
            _fileWriter?.WriteLine(fullMessage);
        }
    }
    
    private static void WriteRaw(LogLevel level, string message)
    {
        if (level < _minLevel) return;
        
        lock (_lock)
        {
            if (_enableColors)
            {
                Console.ForegroundColor = GetColor(level);
                Console.WriteLine(message);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(message);
            }
            
            _fileWriter?.WriteLine(message);
        }
    }
    
    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Fatal => "FTL",
        _ => "???"
    };
    
    private static ConsoleColor GetColor(LogLevel level) => level switch
    {
        LogLevel.Trace => ConsoleColor.DarkGray,
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Info => ConsoleColor.White,
        LogLevel.Warn => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Fatal => ConsoleColor.DarkRed,
        _ => ConsoleColor.White
    };
}
