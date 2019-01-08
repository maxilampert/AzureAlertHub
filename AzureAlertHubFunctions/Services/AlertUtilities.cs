﻿using AzureAlertHubFunctions.Dtos;
using AzureAlertHubFunctions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AzureAlertHubFunctions.Services
{
    public class AlertUtilities : IAlertUtilities
    {
        protected IServiceManagement serviceManagemement;
        public AlertUtilities(IServiceManagement serviceManagemementAdapter)
        {
            serviceManagemement = serviceManagemementAdapter;
        }
        /// <summary>
        /// The GetAlertParameters method returns the SubscriptionId and AlertRuleName if they are available in the payload
        /// </summary>
        /// <param name="payload">The JSON payload from Azure Alerting</param>
        /// <returns>AlertParameters object with SubscriptionId and AlertRuleName</returns>
        public AlertEntity LoadOrCreateAlert(string payload, ILogger log)
        {
            AlertEntity alert = null;

            Newtonsoft.Json.Linq.JObject obj = Newtonsoft.Json.Linq.JObject.Parse(payload);
            if (obj != null && obj["data"] != null)
            {
                string AlertRuleName = "NO-NAME-FOUND";
                if (obj["data"]["AlertRuleName"] != null) AlertRuleName = obj["data"]["AlertRuleName"].ToString();

                string LogAnalyticsUrl = "";
                if (obj["data"]["LinkToSearchResults"] != null) LogAnalyticsUrl = obj["data"]["LinkToSearchResults"].ToString();
                string ResourceName = GetResourceName(AlertRuleName, payload, obj, log);
                string ClientInstance = GetClientInstance(AlertRuleName, ResourceName, payload, obj, log);
                string PartitionKey = ResourceName + " - " + ClientInstance;

                // Add computer to AlertRuleName 
                if (!String.IsNullOrEmpty(PartitionKey) && !String.IsNullOrEmpty(AlertRuleName))
                {
                    alert = RetrieveAlert(PartitionKey, AlertRuleName);
                    if (alert == null)
                    {
                        alert = new AlertEntity(PartitionKey, AlertRuleName);
                        alert.Payload = payload;
                        alert.SearchIntervalStartTimeUtc = DateTime.Parse(obj["data"]["SearchIntervalStartTimeUtc"].ToString());
                        alert.SearchIntervalEndTimeUtc = DateTime.Parse(obj["data"]["SearchIntervalEndtimeUtc"].ToString());
                        alert.LogAnalyticsUrl = LogAnalyticsUrl;
                        alert.Resource = ResourceName;
                        alert.ClientInstance = ClientInstance;
                    }
                    else
                    {
                        alert.LastOccuranceTimestamp = DateTime.Now;
                        alert.Counter++;
                    }

                    if (String.IsNullOrEmpty(alert.IncidentId))
                    {
                        // We don't yet have an IncidentId for this alert
                        ServiceManagementResponseDto incident = serviceManagemement.CreateIncident(alert, log);
                        if (incident != null && incident.result != null)
                        {
                            alert.IncidentId = incident.result.number;
                            alert.IncidentUrl = incident.result.url;
                        }
                    }

                    log.LogInformation("Update Alerts table: " + Newtonsoft.Json.JsonConvert.SerializeObject(alert));
                    InsertUpdateAlert(alert);

                    if (!String.IsNullOrEmpty(alert.IncidentId))
                    {
                        // Insert record of incident ID
                        AlertIncidentEntity incidentEntity = new AlertIncidentEntity("SNOW", alert.IncidentId);
                        incidentEntity.AlertPartitionId = alert.PartitionKey;
                        incidentEntity.AlertRowId = alert.RowKey;
                        incidentEntity.IncidentUrl = alert.IncidentUrl;

                        InsertUpdateAlertIncident(incidentEntity);
                    }
                }
            }

            return alert;
        }

        public string GetResourceName(string alertName, string payload, Newtonsoft.Json.Linq.JObject payloadObj, ILogger log)
        {
            string resourceName = "";

            try
            {
                int ResourceIndex = 1;

                for (int p = 0; p < ((JArray)payloadObj["data"]["SearchResult"]["tables"][0]["columns"]).Count; p++)
                {
                    if (payloadObj["data"]["SearchResult"]["tables"][0]["columns"][p]["name"].ToString() == "Computer")
                    {
                        ResourceIndex = p;
                        break;
                    }
                }

                resourceName = payloadObj["data"]["SearchResult"]["tables"][0]["rows"][0][ResourceIndex].ToString();
            }
            catch (Exception ex)
            {
                log.LogError($"Error retrieving resource name for alert {alertName} - {ex.ToString()}");                
            }

            return resourceName;
        }

        public string GetClientInstance(string alertName, string resourceName, string payload, Newtonsoft.Json.Linq.JObject payloadObj, ILogger log)
        {
            string resourceHostName = resourceName;
            string clientInstance = "";

            if (resourceName.Contains("."))
            {
                resourceHostName = resourceName.Substring(0, resourceName.IndexOf('.') - 1);
            }

            // Define a regular expression for repeated words.
            Regex rx = new Regex($@"({resourceHostName}\\)\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Find matches.
            MatchCollection matches = rx.Matches(payload);

            if (matches.Count > 0)
            {
                // Report on each match.
                foreach (Match match in matches)
                {
                    clientInstance = match.Value;
                }
            }
            else
            {
                // Go through columns if an Instance exists, and if so use it
                int InstanceIndex = 1;

                for (int p = 0; p < ((JArray)payloadObj["data"]["SearchResult"]["tables"][0]["columns"]).Count; p++)
                {
                    if (payloadObj["data"]["SearchResult"]["tables"][0]["columns"][p]["name"].ToString() == "InstanceName")
                    {
                        InstanceIndex = p;
                        break;
                    }
                }

                clientInstance = payloadObj["data"]["SearchResult"]["tables"][0]["rows"][0][InstanceIndex].ToString();
            }

            return clientInstance.Replace("\\", "-");
        }

        public AlertEntity RetrieveAlert(string partitionKey, string rowKey)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("Alerts");
            table.CreateIfNotExistsAsync().Wait();

            TableOperation tableOperation = TableOperation.Retrieve<AlertEntity>(partitionKey, rowKey);
            TableResult tableResult = table.ExecuteAsync(tableOperation).Result;

            return (AlertEntity) tableResult.Result;
        }

        public AlertIncidentEntity RetrieveAlertEntity(string partitionKey, string rowKey)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("AlertIncidents");
            table.CreateIfNotExistsAsync().Wait();

            TableOperation tableOperation = TableOperation.Retrieve<AlertIncidentEntity>(partitionKey, rowKey);
            TableResult tableResult = table.ExecuteAsync(tableOperation).Result;

            return (AlertIncidentEntity)tableResult.Result;
        }

        public void InsertUpdateAlert(AlertEntity alert)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("Alerts");
            table.CreateIfNotExistsAsync().Wait();

            // Create the TableOperation object that inserts the customer entity.
            TableOperation insertOperation = TableOperation.InsertOrReplace(alert);

            // Execute the insert operation.
            table.ExecuteAsync(insertOperation);
        }

        public void InsertUpdateAlertIncident(AlertIncidentEntity alert)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("AlertIncidents");
            table.CreateIfNotExistsAsync().Wait();

            // Create the TableOperation object that inserts the customer entity.
            TableOperation insertOperation = TableOperation.InsertOrReplace(alert);

            // Execute the insert operation.
            table.ExecuteAsync(insertOperation);
        }

        public void DeleteAlert(AlertEntity alert)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("Alerts");
            table.CreateIfNotExistsAsync().Wait();

            // Create the TableOperation object that inserts the customer entity.
            TableOperation deleteOperation = TableOperation.Delete(alert);

            // Execute the insert operation.
            table.ExecuteAsync(deleteOperation);
        }

        public void DeleteAlertIncident(AlertIncidentEntity alert)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference("AlertIncidents");
            table.CreateIfNotExistsAsync().Wait();

            // Create the TableOperation object that inserts the customer entity.
            TableOperation deleteOperation = TableOperation.Delete(alert);

            // Execute the insert operation.
            table.ExecuteAsync(deleteOperation);
        }
    }
}
