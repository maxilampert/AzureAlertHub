﻿using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace AzureAlertHubFunctions.Dtos
{
    public class AlertEntity : TableEntity
    {
        public AlertEntity(string partitionKey, string alertName)
            : base(partitionKey, alertName) {
            AlertName = alertName;
        }

        public AlertEntity() { }

        public string SubscriptionId { get; set; }
        public string AlertName { get; set; }
        public string Resource { get; set; }
        public string ClientInstance { get; set; } = "";
        public DateTime? SearchIntervalStartTimeUtc { get; set; }
        public DateTime? SearchIntervalEndTimeUtc { get; set; }
        public string Status { get; set; } = "Open";
        public string IncidentId { get; set; }
        public string IncidentUrl { get; set; }
        public string LogAnalyticsUrl { get; set; }
        public int Counter { get; set; } = 1;
        public string EventId { get; set; } = "";
        public DateTime CreationTimestamp { get; set; } = DateTime.Now;
        public DateTime LastOccuranceTimestamp { get; set; } = DateTime.Now;
        public string Description { get; set; } = "";
        public string Type { get; set; } = "OTHER";
        public string Payload { get; set; }
    }
}
