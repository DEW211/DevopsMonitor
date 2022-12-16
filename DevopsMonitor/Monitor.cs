using System;
using System.IO;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Newtonsoft.Json;

namespace DevopsMonitor
{
    public class Monitor
    {
        private readonly ILogger _logger;

        public Monitor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Monitor>();
        }

        [Function("MonthlyReportTrigger")]
        [BlobOutput("datalake/costexport/AutSoftMPN/ResourceGroupsCost/{date}/{date}.json", Connection = "Azurite")]
        public async Task<string?> Run([BlobTrigger("datalake/costexport/AutSoftMPN/ResourceGroupsCost/{date}/{name}", Connection = "Azurite")] string myBlob, string date, string name)
        {
            if (name == $"{date}.json") return null;
            _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} Date: {date} \n ");

            var armClient = new ArmClient(new DefaultAzureCredential());

            string[] scopes = { "https://graph.microsoft.com/.default" };
            GraphServiceClient graphServiceClient = new GraphServiceClient(new DefaultAzureCredential(), scopes);
            //User user = await graphServiceClient.Users["82fa1959-be75-4ec8-baa1-729a1bf1581e"].GetAsync();

            var subscription = armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{Environment.GetEnvironmentVariable("SubscriptionId")}"));
            var resourceGroups = subscription.GetResourceGroups().GetAllAsync();

            var list = new List<Object>();
            await foreach(var resourceGroup in resourceGroups)
            {
                var definitions = resourceGroup.GetAuthorizationRoleDefinitions();
                var roleDefinitions = new List<Object>();
                foreach (var definition in definitions)
                {
                    if(definition.HasData && definition.Data.RoleName == "Owner")
                    {
                        roleDefinitions.Add(definition);
                    }
                }
                var assignmentsCollection = resourceGroup.GetRoleAssignments();
                var resourceOwners = new List<Object>();
                foreach (var assignment in assignmentsCollection)
                {
                                                                                        //owner role GUID
                    if (assignment.HasData && assignment.Data.RoleDefinitionId.Name == "8e3af657-a8ff-443c-a75c-2fe8c4bcb635" && assignment.Data.PrincipalId.HasValue && assignment.Data.PrincipalType == "User")
                    {
                        try
                        {
                            var guid = assignment.Data.PrincipalId.Value.ToString();
                            User user = await graphServiceClient.Users[guid].GetAsync();
                            resourceOwners.Add(new { user.DisplayName, user.Mail });
                        }
                        catch (ODataError e)
                        {

                            _logger.LogError(e.Message);
                            _logger.LogError(e.Error.Code);
                            //_logger.LogInformation(e.Message);
                        }
                    }

                }
                var tags = resourceGroup.Data.Tags;

                list.Add(new { Name = resourceGroup.Id.Name, Tags = tags, ResourceOwners = resourceOwners});
            }

            _logger.LogInformation("List", list);

            return JsonConvert.SerializeObject(list);
        }
    }
}
