using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.ServiceFabric.Services.Client;
using Newtonsoft.Json;
using SF_HKDev.Web.Models;

namespace SF_HKDev.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly FabricClient _fabricClient;
        private readonly StatelessServiceContext _serviceContext;
        private string _reverseProxyAddressAndPort = "http://localhost:19081";
        private string _statelessAPIDNSNameAndPort = "SF_HKDev.StatelessAPI:8899";

        public HomeController(HttpClient httpClient, StatelessServiceContext context, FabricClient fabricClient)
        {
            _fabricClient = fabricClient;
            _httpClient = httpClient;
            _serviceContext = context;
        }

        public IActionResult Index()
            => View();

        #region Initial Connection Demo
        /// <summary>
        /// http://rev-proxy/app-name/service-name/your-api-url
        /// </summary>
        /// <returns></returns>
        public async Task<JsonResult> TestConnection()
        {
            var url = $"{_reverseProxyAddressAndPort}{Web.GetStatelessAPIServiceName(_serviceContext).AbsolutePath}/api/connection";

            using (var response = await _httpClient.GetAsync(url))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return Json(true);
            }

            return Json(false);
        }
        #endregion

        #region Stateless Service Demo
        public async Task<JsonResult> GetNodeName()
        {
            var url = $"{_reverseProxyAddressAndPort}{Web.GetStatelessAPIServiceName(_serviceContext).AbsolutePath}/api/values";

            using (var response = await _httpClient.GetAsync(url))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return Json(await response.Content.ReadAsStringAsync());
            }

            return Json("Something went wrong");
        }

        public async Task<JsonResult> GetNodeNameDNS()
        {
            var url = $"http://{_statelessAPIDNSNameAndPort}/api/values";

            using (var response = await _httpClient.GetAsync(url))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return Json(await response.Content.ReadAsStringAsync());
            }

            return Json("Something went wrong");
        }
        #endregion

        #region Stateful Service Demo
        [HttpPost]
        public async Task<JsonResult> SaveValue(string newValue)
        {
            var url = $"{_reverseProxyAddressAndPort}{Web.GetStatefulAPIServiceName(_serviceContext).AbsolutePath}/api/values?value={newValue}&PartitionKey=1&PartitionKind=Int64Range";

            using (var response = await _httpClient.PostAsync(url, null))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return Json(true);
            }

            return Json(false);
        }

        public async Task<JsonResult> ReadValues()
        {
            var url = $"{_reverseProxyAddressAndPort}{Web.GetStatefulAPIServiceName(_serviceContext).AbsolutePath}/api/values?PartitionKey=1&PartitionKind=Int64Range";

            using (var response = await _httpClient.GetAsync(url))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var resultList = JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync());

                    return Json(string.Join("<BR/>", resultList));
                }
            }

            return Json("Something went wrong");
        }
        #endregion

        #region Service Registry Query Demo
        public async Task<IActionResult> CommunicationWithServiceRegistryQuery()
        {
            ServicePartitionResolver resolver = ServicePartitionResolver.GetDefault();

            var cancellationToken = new CancellationToken(); //TODO: use it!

            ResolvedServicePartition partition = await resolver.ResolveAsync(new Uri("fabric:/MyApp/MyService"), new ServicePartitionKey(), cancellationToken);

            string url = "http://{0}/api/yourController";
            bool urlIsOk = false;

            foreach (var endpoint in partition.Endpoints)
            {
                switch (endpoint.Role)
                {
                    case ServiceEndpointRole.Stateless: //TODO: implement load balancing!
                    case ServiceEndpointRole.StatefulPrimary:
                        url = string.Format(url, endpoint.Address);
                        urlIsOk = true;
                        break;
                    case ServiceEndpointRole.Invalid:
                    case ServiceEndpointRole.StatefulSecondary: //TODO: may be used for read operations
                    default:
                        continue;
                }
            }

            if (urlIsOk)
            {
                //DO YOUR STUFF
            }
            return new OkResult();
        }
        #endregion

        #region Programmatic Scale Demo
        //THIS PART USES THE Microsoft.Azure.Management.Fluent NUGET PACKAGE

        public IAzure AuthenticateAndGetClient()
        {
            var credentials = new AzureCredentials(new ServicePrincipalLoginInformation
            {
                ClientId = "Service Principal ID",
                ClientSecret = "Service principal key"
            }, "Azure Tenant Id", AzureEnvironment.AzureGlobalCloud);

            IAzure azureClient = Azure.Authenticate(credentials).WithSubscription("Azure Subscription Id");

            if (azureClient?.SubscriptionId == "Azure Subscription Id")
            {
                ServiceEventSource.Current.ServiceMessage(_serviceContext, "Successfully logged into Azure");
                return azureClient;
            }
            else
            {
                ServiceEventSource.Current.ServiceMessage(_serviceContext, "ERROR: Failed to login to Azure");
                return null;
            }
        }
        public async Task ScaleOut()
        {
            //SCALE OUT
            var azureClient = AuthenticateAndGetClient();
            var scaleSet = await azureClient.VirtualMachineScaleSets.GetByIdAsync("VM Scale Set Id");

            var maximumNodeCount = 20;
            var newCapacity = Math.Min(maximumNodeCount, scaleSet.Capacity + 1);
            await scaleSet.Update().WithCapacity(newCapacity).ApplyAsync();

            //No need to update SF, new machine will be added automatically to the cluster
        }

        public async Task ScaleIN()
        {
            //Find the node to deactivate - In this case the latest added
            var azureClient = AuthenticateAndGetClient();

            using (var client = new FabricClient())
            {
                var mostRecentLiveNode = (await client.QueryManager.GetNodeListAsync())
                    .Where(n => n.NodeType.Equals("Node Type To Scale", StringComparison.OrdinalIgnoreCase))
                    .Where(n => n.NodeStatus == System.Fabric.Query.NodeStatus.Up)
                    .OrderByDescending(n =>
                    {
                        var instanceIdIndex = n.NodeName.LastIndexOf("_");
                        var instanceIdString = n.NodeName.Substring(instanceIdIndex + 1);
                        return int.Parse(instanceIdString);
                    })
                    .FirstOrDefault();

                var scaleSet = await azureClient.VirtualMachineScaleSets.GetByIdAsync("VM Scale Set Id");

                // Remove the node from the Service Fabric cluster
                ServiceEventSource.Current.ServiceMessage(_serviceContext, $"Disabling node {mostRecentLiveNode.NodeName}");
                await client.ClusterManager.DeactivateNodeAsync(mostRecentLiveNode.NodeName, NodeDeactivationIntent.RemoveNode);

                // Wait (up to a timeout) for the node to gracefully shutdown
                var timeout = TimeSpan.FromMinutes(5);
                var waitStart = DateTime.Now;
                while ((mostRecentLiveNode.NodeStatus == System.Fabric.Query.NodeStatus.Up || mostRecentLiveNode.NodeStatus == System.Fabric.Query.NodeStatus.Disabling) &&
                        DateTime.Now - waitStart < timeout)
                {
                    mostRecentLiveNode = (await client.QueryManager.GetNodeListAsync()).FirstOrDefault(n => n.NodeName == mostRecentLiveNode.NodeName);
                    await Task.Delay(10 * 1000);
                }

                // Decrement VMSS capacity
                var minimumNodeCount = 5;
                var newCapacity = Math.Max(minimumNodeCount, scaleSet.Capacity - 1); // Check min count 

                scaleSet.Update().WithCapacity(newCapacity).Apply();

                //Finally, remove node state from SF
                await client.ClusterManager.RemoveNodeStateAsync(mostRecentLiveNode.NodeName);
            }
        }
        #endregion

        #region Programmatic Service Management Demo
        public async Task IncreaseDecreaseServiceInstances(bool increase, string appName, string serviceName, string serviceType)
        {
            //string appName = "fabric:/MyApplication";
            //string serviceName = "fabric:/MyApplication/Stateless1";
            //string serviceType = "Stateless1Type";

            using (var fabricClient = new FabricClient())
            {
                if (increase)
                {
                    // Create the stateless service description.  For stateful services, use a StatefulServiceDescription object.
                    StatelessServiceDescription serviceDescription = new StatelessServiceDescription
                    {
                        ApplicationName = new Uri(appName),
                        InstanceCount = 1,
                        PartitionSchemeDescription = new SingletonPartitionSchemeDescription(),
                        ServiceName = new Uri(serviceName),
                        ServiceTypeName = serviceType
                    };

                    // Create the service instance.
                    try
                    {
                        fabricClient.ServiceManager.CreateServiceAsync(serviceDescription).Wait();
                        ServiceEventSource.Current.ServiceMessage(_serviceContext, "Created service instance {0}", serviceName);
                    }
                    catch (AggregateException ae)
                    {
                        ServiceEventSource.Current.ServiceMessage(_serviceContext, "CreateService failed.");
                        foreach (Exception ex in ae.InnerExceptions)
                        {
                            ServiceEventSource.Current.ServiceMessage(_serviceContext, "HResult: {0} Message: {1}", ex.HResult, ex.Message);
                        }
                    }
                }
                else
                {
                    // Delete a service instance.
                    try
                    {
                        DeleteServiceDescription deleteServiceDescription = new DeleteServiceDescription(new Uri(serviceName));

                        await fabricClient.ServiceManager.DeleteServiceAsync(deleteServiceDescription);
                        ServiceEventSource.Current.ServiceMessage(_serviceContext, "Deleted service instance {0}", serviceName);
                    }
                    catch (AggregateException ae)
                    {
                        ServiceEventSource.Current.ServiceMessage(_serviceContext, "DeleteService failed.");
                        foreach (Exception ex in ae.InnerExceptions)
                        {
                            ServiceEventSource.Current.ServiceMessage(_serviceContext, "HResult: {0} Message: {1}", ex.HResult, ex.Message);
                        }
                    }

                }
            }
        }
        #endregion 

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
