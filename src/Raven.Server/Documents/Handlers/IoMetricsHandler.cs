﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Raven.Server.Routing;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Extensions;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Utils;
using Voron;

namespace Raven.Server.Documents.Handlers
{
    public class IoMetricsHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/debug/io-metrics", "GET")]
        public Task IoMetrics()
        {
            JsonOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var result = GetIoMetricsResponse(Database);
                context.Write(writer, result.ToJson());
            }
            return Task.CompletedTask;
        }

        [RavenAction("/databases/*/debug/io-metrics/live", "GET", SkipUsagesCount = true)]
        public async Task IoMetricsLive()
        {
            using (var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync())
            {
                var receiveBuffer = new ArraySegment<byte>(new byte[1024]);
                var receive = webSocket.ReceiveAsync(receiveBuffer, Database.DatabaseShutdown);

                using (var ms = new MemoryStream())
                using (var collector = new LiveIOStatsCollector(Database))
                {
                    while (Database.DatabaseShutdown.IsCancellationRequested == false)
                    {
                        if (receive.IsCompleted || webSocket.State != WebSocketState.Open)
                            break;

                        // Check queue for new data from server
                        var tuple = await collector.MetricsQueue.TryDequeueAsync(TimeSpan.FromSeconds(4));
                        if (tuple.Item1 == false)
                        {
                            // No new info, Send heart beat
                            await webSocket.SendAsync(WebSocketHelper.Heartbeat, WebSocketMessageType.Text, true, Database.DatabaseShutdown);
                            continue;
                        }

                        // New info, Send data 
                        ms.SetLength(0);
                        JsonOperationContext context;

                        using (ContextPool.AllocateOperationContext(out context))
                        using (var writer = new BlittableJsonTextWriter(context, ms))
                        {
                            context.Write(writer, tuple.Item2.ToJson());
                        }

                        ArraySegment<byte> bytes;
                        ms.TryGetBuffer(out bytes);
                        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, Database.DatabaseShutdown);
                    }
                }
            }
        }

        public static IOMetricsResponse GetIoMetricsResponse(DocumentDatabase documentDatabase)
        {
            var result = new IOMetricsResponse();

            foreach (var storageEnvironment in documentDatabase.GetAllStoragesEnvironment())
            {
                result.Environments.Add(GetIoMetrics(storageEnvironment.Environment));
            }

            return result;
        }

        public static IOMetricsEnvironment GetIoMetrics(StorageEnvironment storageEnvironment)
        {
            var ioMetrics = new IOMetricsEnvironment
            {
                Path = storageEnvironment.Options.BasePath
            };

            foreach (var fileMetric in storageEnvironment.Options.IoMetrics.Files)
            {
                ioMetrics.Files.Add(GetFileMetrics(fileMetric));
            }

            return ioMetrics;
        }

        private static IOMetricsFileStats GetFileMetrics(IoMetrics.FileIoMetrics fileMetric)
        {
            var fileMetrics = new IOMetricsFileStats
            {
                File = Path.GetFileName(fileMetric.FileName),
                Status = fileMetric.Closed ? FileStatus.Closed : FileStatus.InUse,
            };

            foreach (var recentMetric in fileMetric.GetRecentMetrics())
            {
                fileMetrics.Recent.Add(GetIoMetricsRecentStats(recentMetric));
            }

            foreach (var historyMetric in fileMetric.GetSummaryMetrics())
            {
                fileMetrics.History.Add(new IOMetricsHistoryStats
                {
                    Start = historyMetric.TotalTimeStart.GetDefaultRavenFormat(),
                    End = historyMetric.TotalTimeEnd.GetDefaultRavenFormat(),
                    Size = historyMetric.TotalSize,
                    HumaneSize = Sizes.Humane(historyMetric.TotalSize),
                    FileSize = historyMetric.TotalFileSize,
                    HumaneFileSize = Sizes.Humane(historyMetric.TotalFileSize),
                    Duration = Math.Round((historyMetric.TotalTimeEnd - historyMetric.TotalTimeStart).TotalMilliseconds, 2),
                    ActiveDuration = Math.Round(historyMetric.TotalTime.TotalMilliseconds, 2),
                    MaxDuration = Math.Round(historyMetric.MaxTime.TotalMilliseconds, 2),
                    MinDuration = Math.Round(historyMetric.MinTime.TotalMilliseconds, 2),
                    MaxAcceleration = historyMetric.MaxAcceleration,
                    MinAcceleration = historyMetric.MinAcceleration,
                    CompressedSize = historyMetric.TotalCompressedSize,
                    HumanCompressedSize = Sizes.Humane(historyMetric.TotalCompressedSize),
                    Type = historyMetric.Type
                });
            }

            return fileMetrics;
        }

        public static IOMetricsRecentStats GetIoMetricsRecentStats(IoMeterBuffer.MeterItem recentMetric)
        {
            return new IOMetricsRecentStats
            {
                Start = recentMetric.Start.GetDefaultRavenFormat(),
                Size = recentMetric.Size,
                Acceleration = recentMetric.Acceleration,
                CompressedSize = recentMetric.CompressedSize,
                HumanCompressedSize = Sizes.Humane(recentMetric.CompressedSize),
                HumaneSize = Sizes.Humane(recentMetric.Size),
                FileSize = recentMetric.FileSize,
                HumaneFileSize = Sizes.Humane(recentMetric.FileSize),
                Duration = Math.Round(recentMetric.Duration.TotalMilliseconds, 2),
                Type = recentMetric.Type,
            };
        }
    }

    public class IOMetricsHistoryStats
    {
        public string Start { get; set; }
        public string End { get; set; }
        public long Size { get; set; }
        public string HumaneSize { get; set; }
        public long FileSize { get; set; }
        public string HumaneFileSize { get; set; }
        public double Duration { get; set; }
        public double ActiveDuration { get; set; }
        public double MaxDuration { get; set; }
        public double MinDuration { get; set; }
        public int MaxAcceleration { get; set; }
        public int MinAcceleration { get; set; }
        public long CompressedSize { get; set; }
        public string HumanCompressedSize { get; set; }
        public IoMetrics.MeterType Type { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Start)] = Start,
                [nameof(End)] = End,
                [nameof(Size)] = Size,
                [nameof(HumaneSize)] = HumaneSize,
                [nameof(FileSize)] = FileSize,
                [nameof(HumaneFileSize)] = HumaneFileSize,
                [nameof(Duration)] = Duration,
                [nameof(ActiveDuration)] = ActiveDuration,
                [nameof(MaxDuration)] = MaxDuration,
                [nameof(MinDuration)] = MinDuration,
                [nameof(MaxAcceleration)] = MaxAcceleration,
                [nameof(MinAcceleration)] = MinAcceleration,
                [nameof(CompressedSize)] = CompressedSize,
                [nameof(HumanCompressedSize)] = HumanCompressedSize,
                [nameof(Type)] = Type,
            };
        }
    }

    public class IOMetricsRecentStatsAdditionalTypes : IOMetricsRecentStats
    {
        public long OriginalSize;
        public string HumaneOriginalSize;
        public long CompressedSize;
        public string HumaneCompressedSize;
        public double CompressionRatio;
        public int Acceleration;
    }

    public class IOMetricsRecentStats
    {
        public string Start { get; set; }
        public long Size { get; set; }
        public string HumaneSize { get; set; }

        public long CompressedSize { get; set; }
        public string HumanCompressedSize { get; set; }

        public int Acceleration { get; set; }

        public long FileSize { get; set; }
        public string HumaneFileSize { get; set; }
        public double Duration { get; set; }
        public IoMetrics.MeterType Type { get; set; }

        public DynamicJsonValue ToJson()
        {
            switch (Type)
            {
                case IoMetrics.MeterType.Compression:
                    return new DynamicJsonValue
                    {
                        [nameof(Start)] = Start,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.OriginalSize)] = Size,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.HumaneOriginalSize)] = HumaneSize,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.CompressedSize)] = CompressedSize,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.HumanCompressedSize)] = HumanCompressedSize,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.Acceleration)] = Acceleration,
                        [nameof(IOMetricsRecentStatsAdditionalTypes.CompressionRatio)] = CompressedSize * 1.0 / Size,
                        [nameof(Duration)] = Duration,
                        [nameof(Type)] = Type
                    };
                default:
                    return new DynamicJsonValue
                    {
                        [nameof(Start)] = Start,
                        [nameof(Size)] = Size,
                        [nameof(HumaneSize)] = HumaneSize,
                        [nameof(FileSize)] = FileSize,
                        [nameof(HumaneFileSize)] = HumaneFileSize,
                        [nameof(Duration)] = Duration,
                        [nameof(Type)] = Type
                    };
            }
        }
    }

    public enum FileStatus
    {
        Closed,
        InUse
    }

    public class IOMetricsFileStats
    {
        public IOMetricsFileStats()
        {
            Recent = new List<IOMetricsRecentStats>();
            History = new List<IOMetricsHistoryStats>();
        }

        public string File { get; set; }
        public FileStatus Status { get; set; }
        public List<IOMetricsRecentStats> Recent { get; set; }
        public List<IOMetricsHistoryStats> History { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(File)] = File,
                [nameof(Status)] = Status,
                [nameof(Recent)] = new DynamicJsonArray(Recent.Select(x => x.ToJson())),
                [nameof(History)] = new DynamicJsonArray(History.Select(x => x.ToJson()))
            };
        }
    }

    public class IOMetricsEnvironment
    {
        public IOMetricsEnvironment()
        {
            Files = new List<IOMetricsFileStats>();
        }

        public string Path { get; set; }
        public List<IOMetricsFileStats> Files { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Path)] = Path,
                [nameof(Files)] = new DynamicJsonArray(Files.Select(x => x.ToJson()))
            };
        }
    }

    public class IOMetricsResponse
    {
        public IOMetricsResponse()
        {
            Environments = new List<IOMetricsEnvironment>();
        }

        public List<IOMetricsEnvironment> Environments { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Environments)] = new DynamicJsonArray(Environments.Select(x => x.ToJson()))
            };
        }
    }
}
