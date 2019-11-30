using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AzureAlertHubFunctions.Dtos;
using AzureAlertHubFunctions.Interfaces;
using AzureAlertHubFunctions.Services;

namespace AzureAlertHubFunctions
{
    public static class AzureAlertFunction
    {
        static IAlertUtilities alertUtils;
        
        static AzureAlertFunction()
        {
            string ServiceManagementHost = System.Environment.GetEnvironmentVariable("ServiceManagementType");
            if (ServiceManagementHost.ToUpper() == "SNOW")
            {
                alertUtils = new AlertUtilities(new ServiceManagementSNOW());
            }
            else
            {
                alertUtils = new AlertUtilities(new ServiceManagementTEST());
            }
        }

        [FunctionName("AzureAlertFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string result = "Alert successfully processed.";

            log.LogInformation("C# HTTP trigger function processing an Alert.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            log.LogInformation(requestBody);

            // First extract data from alert payload
            AlertEntity[] alerts = alertUtils.LoadOrCreateAlerts(requestBody, log);
            if (alerts == null || alerts.Length == 0)
            {
                // We do not have normal parameters, error
                result = "Invalid alert data received, no ResourceName or AlertId found, no further action possible.";
                log.LogError(result);
            }

            return (ActionResult)new OkObjectResult(result);
        }
    }
}
