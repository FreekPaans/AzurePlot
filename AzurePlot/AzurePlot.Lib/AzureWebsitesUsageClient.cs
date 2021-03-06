﻿using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.Monitoring.Metrics;
using Microsoft.WindowsAzure.Management.Monitoring.Metrics.Models;
using Microsoft.WindowsAzure.Management.Monitoring.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AzurePlot.Lib {
	class AzureWebsitesUsageClient {
		
		readonly AzureWebsitesInfoApiClient _azureWebsitesInfoClient;
		readonly AzureMetricsApiClient _metricsApiClient;
		readonly AzureWebsitesMetricsApiClient _azureWebsitesMetricsClient;
		public AzureWebsitesUsageClient(AzureManagementRestClient client, CertificateCloudCredentials credentials) {
			var azureWebsiteApiClient = new AzureWebsiteApiClient(client);
			_azureWebsitesInfoClient = new AzureWebsitesInfoApiClient(azureWebsiteApiClient);
			_azureWebsitesMetricsClient = new AzureWebsitesMetricsApiClient(azureWebsiteApiClient);
			_metricsApiClient =  new AzureMetricsApiClient(credentials);
		}
		
		

		internal async Task<ICollection<UsageObject>> GetUsageCollection() {
			var websites = await _azureWebsitesInfoClient.ListAzureWebsites();

			var result = await Task.WhenAll(websites.Select(
				w=>new AzureWebsiteUsage(w,_metricsApiClient,_azureWebsitesMetricsClient).GetUsage()
			));

			return result.SelectMany(_=>_).ToList();
			
		}


		internal async Task<ICollection<UsageObject>> GetUsageCollectionForWebsite(AzureWebsiteId websiteId, TimeSpan history, MetricsFilter filter) {
			var metrics = await _metricsApiClient.GetMetricsForWebsite(websiteId, history,filter);
			var res = new List<UsageObject>();
			foreach(var metric in metrics) {
				foreach(var result in metric.MetricValues.OrderBy(_=>_.Timestamp)) {
					res.Add(new UsageObject {
						GraphiteCounterName =metric.Name  + "." +metric.Unit,
						Value = result.Average.Value,
						Timestamp = result.Timestamp.ToString("o")
					});
				}
			}

			return res;
		}
	}
}
