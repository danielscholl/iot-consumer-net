using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using log4net;
using log4net.Config;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;
using Newtonsoft.Json;

namespace Iot
{
    public class SimpleEventProcessor : IEventProcessor
    {
        private bool insights = false;
        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));
        private Stopwatch checkpointStopWatch;
        private TelemetryClient telemetryClient;

        public SimpleEventProcessor()
        {
            // Setup the logger
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
            {
                insights = true;
                this.telemetryClient = new TelemetryClient();
            }
        }

        public Task OpenAsync(PartitionContext context)
        {
            this.telemetryClient.Context.Device.Id = context.PartitionId;
            this.telemetryClient.TrackEvent("iotConsumer started");
            this.telemetryClient.GetMetric("ConsumerCount").TrackValue(1);

            Log.Info($"SimpleEventProcessor initialized. Partition: '{context.PartitionId}'");

            this.checkpointStopWatch = new Stopwatch();
            this.checkpointStopWatch.Start();

            return Task.CompletedTask;
        }

        public Task CloseAsync(PartitionContext context, CloseReason reason)
        {
            Log.Info($"Processor Shutting Down. Partition '{context.PartitionId}', Reason: '{reason}'.");
            this.checkpointStopWatch.Stop();

            return context.CheckpointAsync();
        }

        public Task ProcessErrorAsync(PartitionContext context, Exception error)
        {
            Log.Info($"Error on Partition: {context.PartitionId}, Error: {error.Message}");
            if (insights) telemetryClient.TrackTrace(String.Format($"Error on Partition: {context.PartitionId}, Error: {error.Message}"));

            return Task.CompletedTask;
        }

        public Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            // Process the received data for futher processing keep it simple fast and reliable.
            try
            {
                foreach (EventData message in messages)
                {

                   // var logContext = $"--- Partition {context.Lease.PartitionId}, Owner: {context.Lease.Owner}, Offset: {message.Body.Offset} --- {DateTime.Now.ToString()}";
                   // Log.Info(logContext);

                    // if(insights) {
                    //    Metric messageRead = telemetryClient.GetMetric("EventMsgProcessed", "Partition");
                    //    messageRead.TrackValue(1, $"Partition${context.Lease.PartitionId}");
                    // }

                    if (insights) telemetryClient.GetMetric("EventMsgProcessed", "Partition").TrackValue(1, context.Lease.PartitionId);

                    string data = $"{Encoding.UTF8.GetString(message.Body.Array)}";

                    var logData = JsonConvert.SerializeObject(new { Partition = context.Lease.PartitionId, Size = data.Length });

                    Log.Info($"{DateTime.Now.ToString()} {logData}");
                    Log.Debug($"{data} {Environment.NewLine}");
                }

                if(this.checkpointStopWatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    if (insights) telemetryClient.GetMetric("CheckPoint").TrackValue(1);
                    lock (this)
                    {
                        this.checkpointStopWatch.Restart();
                        return context.CheckpointAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                if (insights) telemetryClient.TrackTrace(ex.Message);
            }

            return Task.FromResult<object>(null);
        }
    }
}
