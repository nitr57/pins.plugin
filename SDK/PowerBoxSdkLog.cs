using NINA.Core.Utility;
using System;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NINA.PINS.SDK {
    /// <summary>
    /// Forwards the log messages of the PowerBox SDK into the N.I.N.A. log ("PowerBox SDK: ...").
    /// The SDK calls the callback from its own threads, possibly while it holds its locks: the
    /// callback only copies the message into a bounded queue; a background task writes it to the
    /// N.I.N.A. logger (which writes synchronously to the disk). The SDK still writes every
    /// message to stderr (journal) as well.
    /// </summary>
    internal static class PowerBoxSdkLog {
        private static readonly object _lock = new object();
        private static bool _done;        // registration attempted (also when the SDK is too old)
        private static bool _registered;  // callback set in the SDK
        private static Task _writer;      // background task writing the queue to the logger

        // Kept in a static field: the SDK holds a pointer to it (must not be collected)
        private static readonly PowerBoxSDK.PBLogCallback _callback = OnSdkLog;

        private static readonly Channel<(int Level, string Message)> _queue =
            Channel.CreateBounded<(int, string)>(new BoundedChannelOptions(256) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        /// <summary>
        /// Sets the SDK log callback once per process. Call before the first SDK call (scan),
        /// so that the messages of the scan are forwarded too.
        /// </summary>
        public static void EnsureRegistered() {
            lock (_lock) {
                if (_done) {
                    return;
                }
                _done = true;
                try {
                    PowerBoxSDK.PBSetLogCallback(_callback);
                    _registered = true;
                    _writer = Task.Run(WriteLoop);
                } catch (EntryPointNotFoundException) {
                    Logger.Warning("PowerBox SDK without log callback (older version): its messages are only in the system journal");
                } catch (DllNotFoundException ex) {
                    Logger.Warning($"PowerBox SDK not found, no SDK messages in this log: {ex.Message}");
                } catch (Exception ex) {
                    Logger.Warning($"PowerBox SDK log callback could not be set: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Removes the callback (plugin teardown, process exit) and writes what is still queued
        /// to the logger (waits at most 0.5 s). The SDK waits up to 1 s for a running callback
        /// (ours never blocks). Safe to call more than once.
        /// </summary>
        public static void Unregister() {
            Task writer;
            lock (_lock) {
                if (!_registered) {
                    return;
                }
                _registered = false;
                try {
                    PowerBoxSDK.PBSetLogCallback(null);
                } catch {
                    // Process is ending, nothing to report
                }
                writer = _writer;
            }

            // No more messages can come: let the writer empty the queue and end
            _queue.Writer.TryComplete();
            try {
                writer?.Wait(500);
            } catch {
                // Writer ended with an error, nothing to report at shutdown
            }
        }

        // Runs on SDK threads: copy and queue only. No logger, no locks, no notifications,
        // and never an exception into native code.
        private static void OnSdkLog(int level, IntPtr message) {
            try {
                string text = message == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(message) ?? string.Empty;
                _queue.Writer.TryWrite((level, text));
            } catch {
                // Dropped
            }
        }

        private static async Task WriteLoop() {
            try {
                await foreach (var (level, message) in _queue.Reader.ReadAllAsync()) {
                    try {
                        string text = $"PowerBox SDK: {message}";
                        switch (level) {
                            case PowerBoxSDK.PB_LOG_ERROR:
                                Logger.Error(text);
                                break;
                            case PowerBoxSDK.PB_LOG_WARNING:
                                Logger.Warning(text);
                                break;
                            case PowerBoxSDK.PB_LOG_DEBUG:
                                Logger.Debug(text);
                                break;
                            default:
                                Logger.Info(text);
                                break;
                        }
                    } catch {
                        // Logger not available (shutdown)
                    }
                }
            } catch {
                // Reader ended
            }
        }
    }
}
