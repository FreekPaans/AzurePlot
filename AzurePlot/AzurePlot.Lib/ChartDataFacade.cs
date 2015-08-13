﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AzurePlot.ApiControllers;
using AzurePlot.Lib.SQLDatabase;

namespace AzurePlot.Lib {
    public class ChartDataFacade {
        public ChartDataFacade(string forUri) {
            _uri = new Uri(forUri);
            _path = _uri.LocalPath.Split(new [] {'/'},StringSplitOptions.RemoveEmptyEntries);
            SubscriptionCredentialsProvider = s=>{ throw new InvalidOperationException("set SubscriptionCredentialsProvider"); };
        }

        public Func<string,MetricsEndpointConfiguration> SubscriptionCredentialsProvider;
        public Func<string,SqlCredentials> SqlCredentialsProvider;
        private Uri _uri;
        private string[] _path;

        public async Task<ChartData> FetchChartData() {
            var interval = GetInterval(_uri);

            var result = await FetchDataForInterval(interval);

            result.Interval = interval;

            return result;
        }

        private Task<ChartData> FetchDataForInterval(TimeSpan interval) {
            switch(_uri.Host) {
                case "dummy":
                    return Dummy(interval);
                case "subscription":
                    return GetSubscriptionChartData(interval);
                case "sql-database":
                    return GetSQLDatabaseChartData(interval);
                default:
                    throw new Exception("don't know how to handle service " + _path[0]);
            }
        }

        private Task<ChartData> GetSQLDatabaseChartData(TimeSpan interval) {
            var client = SQLDatabaseUsageClient.CreateServerUsagesClient(_path[0], SqlCredentialsProvider(_path[0]).Username, SqlCredentialsProvider(_path[0]).Password);
            var usages = client.GetUsages(DateTime.UtcNow.Add(interval.Negate()));

            var serverName = _path[0];
            var counter = _path[_path.Length-1];
            var database = _path[_path.Length-2];

            switch(counter) {
                case "logio":
                    return FilterSQLUsages(usages, serverName,database,"Log I/O","avg_log_write_percent");
                case "dataio":
                    return FilterSQLUsages(usages, serverName,database,"Data I/O", "avg_physical_data_read_percent", "avg_data_io_percent");
                case "cpu":
                    return FilterSQLUsages(usages, serverName,database, "CPU", "avg_cpu_percent");
                case "storage":
                    return FilterSQLUsages(usages, serverName,database,"Storage", "storage_in_megabytes");
                case "memory":
                    return FilterSQLUsages(usages, serverName,database,"Memory","active_memory_used_kb");
                case "sessions":
                    return FilterSQLUsages(usages, serverName,database,"Sessions", "active_session_count");
                case "avg_memory_usage":
                    return FilterSQLUsages(usages, serverName,database,"Average memory usage", "avg_memory_usage_percent");
                default:
                    throw new Exception("Unknown counter " +counter);
            }
        }

        private Task<ChartData> FilterSQLUsages(ICollection<UsageObject> usages,string servername,string database, string chartName, params string[] counters) {
            //usage format: "Azure.SQLDatabase.r2vd5rudps.wadgraphes.usage_in_seconds"
            const int databaseNameField = 3;
            const int counterNameField = 4;
            var perCounter = usages
                .GroupBy(_=>_.GraphiteCounterName)
                .Select(_=>new { usages = _.ToList(), split = _.Key.Split('.') })
                .Where(_=>_.split[databaseNameField] == database).ToDictionary(_=>_.split[counterNameField]);

            var res = new List<SeriesData>();
            foreach(var counter in counters) {
                if(!perCounter.ContainsKey(counter)) {
                    continue;
                }
                res.Add(new SeriesData {
                    DataPoints = perCounter[counter].usages.Select(_=>new DataPoint { Timestamp = _.Timestamp,  Value = _.Value }).ToList(),
                    Name = counter
                });
            }

            return Task.FromResult(new ChartData() { Name = string.Format("{0}.{1} {2} (SQL Database)", servername,database, chartName), Series = res });
        }



        private Task<ChartData> GetSubscriptionChartData(TimeSpan interval) {
            switch(_path[1]) {
                case "websites":
                    return GetWebsiteChartData(interval);
                case "cloud-services":
                    return GetCloudServiceChartData(interval);
                default:
                    throw new Exception(string.Format("Don't know how to handle {0}", _path[1]));
            }
        }

        private Task<ChartData> GetCloudServiceChartData(TimeSpan interval) {
            var serviceId = AMDCloudServiceRoleId.FromUri(_uri);

            var counter = _path[_path.Length-1];

            switch(counter) {
                case "cpu":
                    return GetCloudServiceCPU(serviceId, interval);
                case "disk":
                    return GetCloudServiceDisk(serviceId,interval);
                case "network":
                    return GetCloudServiceNetwork(serviceId,interval);
                default:
                    throw new ArgumentException("don't now how to get " + counter);
            }

        }

        private Task<ChartData> GetCloudServiceNetwork(AMDCloudServiceRoleId serviceRoleId,TimeSpan history) {
            return GetCloudServiceMetrics(serviceRoleId,history, 
                label: "Network traffic",
                regex:"Network",
                formatSeriesLabel:(instanceName,metricName)=>string.Format("{0} {1} bytes", instanceName, metricName)
            );
        }

        private Task<ChartData> GetCloudServiceDisk(AMDCloudServiceRoleId serviceRoleId,TimeSpan history) {
            return GetCloudServiceMetrics(serviceRoleId,history, 
                label: "Disk performance",
                regex:"Disk",
                formatSeriesLabel:(instanceName,metricName)=>string.Format("{0} {1}", instanceName, metricName)
            );
        }

        private string GetSubscriptionId() {
            return GetCredentials().SubscriptionId;
        }

        private Task<ChartData> GetCloudServiceCPU(AMDCloudServiceRoleId serviceRoleId, TimeSpan history) {
            return GetCloudServiceMetrics(serviceRoleId,history, label: "CPU",regex:"CPU",formatSeriesLabel:(instanceName,metricname)=>instanceName);
        }

        private async Task<ChartData> GetCloudServiceMetrics(AMDCloudServiceRoleId serviceRoleId, TimeSpan history, string label,string regex,Func<string,string,string> formatSeriesLabel) {
            var client = new AzureCloudServicesClient(new AzureManagementRestClient(GetCredentials().GetCertificateCloudCredentials()),GetCredentials().GetCertificateCloudCredentials());

            var instances = await client.ListInstancesForServiceRole(serviceRoleId);

            var usages = await client.GetUsage(instances,history,MetricsFilter.FromRegexes(regex));

            var usagesPartitioned = PartitionByInstanceNameAndMetric(usages);

            return new ChartData {
                Name = string.Format("{0} {1} (Cloud Service)", serviceRoleId.DisplayName,label),
                Series = usagesPartitioned.Keys.SelectMany(
                    instance=>usagesPartitioned[instance].Keys.Select(metricName=>new SeriesData {
                        Name = formatSeriesLabel(instance,metricName),
                        DataPoints = usagesPartitioned[instance][metricName].Select(uo=>new DataPoint { Timestamp = uo.Timestamp, Value = uo.Value }).ToList()
                    })).ToList()
            };
                
                
                //usages.Select(_=> new SeriesData{
                //    Name = formatInstanceName(_.Key.InstanceName),
                //    DataPoints = _.Value.Select(uo=>new DataPoint { Timestamp = uo.Timestamp, Value = uo.Value }).ToList()
                //}).ToList()
            
        }

        private Dictionary<string,Dictionary<string,List<UsageObject>>> PartitionByInstanceNameAndMetric(ICollection<UsageObject> usages) {
            //fmt: Azure.CloudServices.MetricsApi.<servicename>.<slot>.<role>.<metricname>.<unit>.<aggregation>.<instancename>
            const int instanceNameIndex = 9;
            const int metricNameIndex = 6;
            return usages.GroupBy(_=>_.GraphiteCounterName.Split('.')[instanceNameIndex])
                .ToDictionary(
                    _=>_.Key,
                    _=>_.GroupBy(x=>x.GraphiteCounterName.Split('.')[metricNameIndex]).ToDictionary(y=>y.Key,y=>y.ToList()));
        }

        private Task<ChartData> GetWebsiteChartData(TimeSpan interval) {
            var webspace = _path[2];
            var websiteName = _path[3];
            var counter = _path[4];
            switch(counter) {
                case "requests":
                    return GetWebsiteRequests(webspace,websiteName,interval);
                case "cpu":
                    return GetWebsiteCPU(webspace,websiteName,interval);
                case "memory":
                    return GetWebsiteMemory(webspace,websiteName,interval);
                case "traffic":
                    return GetWebsiteTraffic(webspace,websiteName,interval);
                case "response-times":
                    return GetWebsiteResponseTimes(webspace,websiteName,interval);
                default:
                    throw new Exception("Don't know how to get " + counter);
            }
        }


        private Task<ChartData> GetWebsiteCPU(string webspace,string websiteName, TimeSpan interval) {
			return GetWebsiteUsages(webspace,websiteName,x=>x,string.Format("{0} CPU",websiteName), interval,"^CpuTime");
		}

		private Task<ChartData> GetWebsiteRequests(string webspace,string websiteName, TimeSpan interval) {
			return GetWebsiteUsages(webspace,websiteName,x=>x.Replace(".Count",""),string.Format("{0} requests", websiteName),interval,"^Http", "^Requests");
		}

        
        private Task<ChartData> GetWebsiteMemory(string webspace,string websiteName, TimeSpan interval) {
            return GetWebsiteUsages(webspace,websiteName,x=>x.Replace(".Bytes",""),string.Format("{0} memory usage (bytes)", websiteName),interval,"MemoryWorkingSet");
        }

        private Task<ChartData> GetWebsiteTraffic(string webspace,string websiteName, TimeSpan interval) {
            return GetWebsiteUsages(webspace,websiteName,x=>x.Replace(".Bytes",""),string.Format("{0} traffic (bytes)", websiteName),interval,"(^BytesSent|^BytesReceived)");
        }

        private Task<ChartData> GetWebsiteResponseTimes(string webspace,string websiteName, TimeSpan interval) {
            return GetWebsiteUsages(webspace,websiteName,x=>x.Replace(".Milliseconds",""),string.Format("{0} response times (ms)", websiteName),interval,"^AverageResponseTime");
        }

		private async Task<ChartData> GetWebsiteUsages(string webspace,string websiteName,Func<string,string> formatSeries,string charttitle, TimeSpan interval,params string[] filters) {
			var usageClient = new AzureUsageClient(GetCredentials());
			var usages = await usageClient.GetWebsitesUsageForWebsite(webspace,websiteName,interval,filters);
			return new ChartData {
				Name = charttitle + " (website)",
				Series = usages.GroupBy(_ => _.GraphiteCounterName).Select(_ =>
					new SeriesData {
						Name = formatSeries(_.Key),
						DataPoints = _.Select(dp => new DataPoint { Timestamp = dp.Timestamp,Value = dp.Value }).ToList()
					}
				).ToList()
			};
		}

        private MetricsEndpointConfiguration GetCredentials() {
            return SubscriptionCredentialsProvider(_path[0]);
        }

         private static TimeSpan GetInterval(Uri uri) {

             var qs = ParseQueryString(uri);

            var unit = GetUnit(qs, TimeSpan.FromHours(1));
            return GetInterval(qs, unit, 1);
        }

         private static NameValueCollection ParseQueryString(Uri uri) {
             //todo: add decent implementation without relying on system.web
             if(string.IsNullOrEmpty(uri.Query)) {
                return new NameValueCollection();
             }

             var query = uri.Query;

             query = query.Substring(1);

             var spl = query.Split('&');

             var pairs  = spl.Select(_ => _.Split('=')).ToDictionary(p => p[0],p => p[1]);

             var qs = new NameValueCollection();
             foreach(var pair in pairs) {
                 qs.Add(pair.Key,pair.Value);
             }
             return qs;
         }

        private static TimeSpan GetInterval(System.Collections.Specialized.NameValueCollection qs,TimeSpan unit,int @defaultValue) {
            var value = @defaultValue;
            if(!string.IsNullOrEmpty(qs["interval"])) {
                value = int.Parse(qs["interval"]);
            }
            if(value<=0) {
                throw new ArgumentException("Cannot have negative interval");
            }
            return TimeSpan.FromSeconds(unit.TotalSeconds * value);            
        }

        private static TimeSpan GetUnit(System.Collections.Specialized.NameValueCollection qs,TimeSpan @default) {
            if(string.IsNullOrEmpty(qs["unit"])) {
                return @default;
            }
            switch(qs["unit"]) {
                case "minutes": return TimeSpan.FromMinutes(1);
                case "hours": return TimeSpan.FromHours(1);
            }
            throw new ArgumentOutOfRangeException("Not a valid unit");
        }

        

        private static Task<ChartData> Dummy(TimeSpan interval) {
            var data = new ChartData { 
                Name = "Dummy",
                Series = new List<SeriesData> {
                    new SeriesData {
                        Name = "200",
                        DataPoints = GenerateData(interval,40)
                    },
                    new SeriesData {
                        Name = "all",
                        DataPoints = GenerateData(interval,50)
                    },
                }
            };

            return Task.FromResult(data);
        }
        
      

		private static List<DataPoint> GenerateData(TimeSpan period,double magnitude) {
			var res = new List<DataPoint>();
			var end = DateTime.Now;
			var start = end.Add(period.Negate());

			var rand = new Random();
			
			for(var i = start; i<=end; i = i.AddMinutes(5)) {
				res.Add(new DataPoint {
					Timestamp = i.ToUniversalTime().ToString("o"),
					Value = magnitude * (1 + rand.NextDouble() + Math.Sin(2*Math.PI * (i-start).TotalMinutes / period.TotalMinutes))
				});
			}

			return res;
		}
                     //var usages = GetUsageClient()
            //    .GetWebsitesUsageForWebsite(GetWebspace(), _website,_history, _filters.ToArray())
            //    .Result;
        //private string GetWebspace() {
        //    return AzureWebsitesInfoApiClientFacade.FindWebspace(_config,_website);
        //}

        //private AzureUsageClient GetUsageClient() {
        //    return new AzureUsageClient(_config);
        //}
    }
}
